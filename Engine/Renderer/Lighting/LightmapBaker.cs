using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// CPU lightmap baker (Source 2 look, phase 4.2). Runs on the bake worker after the probe volume, against the same
    /// <see cref="LightingBakeInput"/> snapshot and BVH.
    ///
    /// <list type="number">
    /// <item>Lightmap UVs: each model is unwrapped once (<see cref="LightmapUnwrap"/>); each Lightmap Static gameobject packs
    /// its model's charts at its own size (texels per metre × Lightmap Scale) into a block, and the blocks into pages.</item>
    /// <item>Texels: every page texel a triangle covers (or lies within ¾ texel of, so bilinear filtering at chart edges reads
    /// valid light) gets the world position and normal there.</item>
    /// <item>Gather: per texel, cosine-distributed rays over the hemisphere. Misses see the sky (ground below the horizon);
    /// hits see albedo × (direct light + the baked probes' irradiance) there. One exact bounce, the rest from the probes.
    /// Emissive triangles are sampled as area lights. Rays that hit back faces mark the texel as inside geometry; it is
    /// filled from its neighbours.</item>
    /// <item>Per texel also: AO (share of rays hitting something within <see cref="LightingSettings.LightmapAoDistance"/>),
    /// the dominant direction of the incoming light, and for each static light with a shadow mask channel how much of the
    /// light's source (sun disc, light sphere) it sees.</item>
    /// <item>An edge-aware blur per gameobject removes the sampling noise, then the gutters are dilated.</item>
    /// </list>
    ///
    /// Units match the probes (diffuse × 0.1, linear colour): what the environment pass multiplies by albedo.
    /// Direct light of the scene's lights is not baked: it stays real-time, shadowed on lightmapped surfaces by the mask.
    /// </summary>
    public static class LightmapBaker
    {
        private const float RayBias = 0.01f;
        //Texels further than this from every triangle (in texels) stay empty and are dilated
        private const float CoverDistance = 0.75f;
        //Smallest light source used for the soft shadow mask (m), so point lights still get a slightly soft edge
        private const float MinShadowSourceRadius = 0.03f;
        private static readonly Vector3 Luma = new Vector3(0.2126f, 0.7152f, 0.0722f);

        private struct Texel
        {
            public int Page, X, Y, Instance;
            public Vector3 Position, Normal, GeoNormal;
            public float Distance;
        }

        private struct Gathered
        {
            public Vector3 Irradiance;
            public Vector3 Direction; //luminance-weighted mean direction of the incoming light
            public float Luminance;
            public float Ao;
            public Vector4 Mask;
            public bool Valid;
        }

        private sealed class InstanceLayout
        {
            public LightingBakeInput.LightmapInstanceInput Input;
            public int Mesh;
            public LightmapUnwrap Unwrap;
            public float Density;
            //Chart rectangles in the block (texels): position and the scale from chart coordinates to texels
            public Point[] ChartOrigin;
            public Vector2[] ChartScale;
            public int BlockWidth, BlockHeight;
            public int Page;
            public Point BlockOrigin;
            //Texel-space lightmap coordinate per part vertex (page texels)
            public Vector2[][] Texels;
        }

        public static LightmapData Bake(LightingBakeInput input, LightingSettings s, TriangleBvh bvh, ProbeVolumeData probes,
            ProbeVolumeBaker.ProgressCallback progress, CancellationToken ct)
        {
            var timer = Stopwatch.StartNew();
            var data = new LightmapData
            {
                TexelsPerUnit = s.LightmapTexelsPerUnit,
                Samples = Math.Max(16, s.LightmapSamples),
            };
            if (input.LightmapInstances.Count == 0) return null;

            progress?.Invoke(0, $"Lightmaps: unwrapping {input.LightmapInstances.Count} gameobjects");

            // ---- UVs: one unwrap per model, one block per gameobject, blocks into pages ----
            var meshOf = new Dictionary<object, int>();
            var unwraps = new List<LightmapUnwrap>();
            var layouts = new List<InstanceLayout>();
            int maxSize = Math.Clamp(s.LightmapMaxSize, 64, 8192);
            foreach (LightingBakeInput.LightmapInstanceInput instance in input.LightmapInstances)
            {
                ct.ThrowIfCancellationRequested();
                if (!meshOf.TryGetValue(instance.ModelKey, out int mesh))
                {
                    mesh = unwraps.Count;
                    LightmapUnwrap unwrap = LightmapUnwrap.Build(instance.Parts);
                    unwraps.Add(unwrap);
                    meshOf[instance.ModelKey] = mesh;
                    data.Meshes.Add(MeshOf(unwrap));
                }
                var layout = new InstanceLayout { Input = instance, Mesh = mesh, Unwrap = unwraps[mesh], Density = instance.TexelsPerUnit };
                LayoutBlock(layout, maxSize);
                layouts.Add(layout);
            }
            PackPages(layouts, maxSize, data);

            // ---- Texels ----
            var texelMaps = new int[data.Pages.Count][];
            for (int p = 0; p < data.Pages.Count; p++)
            {
                texelMaps[p] = new int[data.Pages[p].Width * data.Pages[p].Height];
                Array.Fill(texelMaps[p], -1);
            }
            var texels = new List<Texel>();
            for (int i = 0; i < layouts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Rasterize(layouts[i], i, data.Pages[layouts[i].Page], texelMaps[layouts[i].Page], texels);
            }
            data.TexelCount = texels.Count;
            foreach (Texel t in texels) data.Instances[t.Instance].Texels++;

            // ---- Static lights: shadow mask channels ----
            var channelLights = AssignChannels(input, data);

            // ---- Gather ----
            ProbeVolumeBaker.SkyRadiance(s, out Vector3 sky, out Vector3 ground);
            int samples = data.Samples;
            int shadowSamples = Math.Clamp(s.LightmapShadowSamples, 1, 256);
            Vector2[] pattern = Hammersley(samples);
            Vector2[] shadowPattern = Hammersley(shadowSamples);
            float maxDistance = Vector3.Distance(input.GeometryBounds.Min, input.GeometryBounds.Max) * 2 + 1;
            float aoDistance = Math.Max(0.01f, s.LightmapAoDistance);
            float sunCos = (float)Math.Cos(MathHelper.ToRadians(Math.Clamp(s.SunAngle, 0, 20) * 0.5f));
            float validity = Math.Clamp(s.ValidityThreshold, 0, 1) * samples;
            var results = new Gathered[texels.Count];
            int done = 0;
            string stage = $"Lightmaps: {texels.Count:N0} texels × {samples} rays";
            progress?.Invoke(0, stage);

            Parallel.For(0, texels.Count, new ParallelOptions { CancellationToken = ct }, i =>
            {
                Texel texel = texels[i];
                float bias = RayBias + 0.05f / Math.Max(layouts[texel.Instance].Density, 0.01f);
                Vector3 origin = texel.Position + texel.GeoNormal * bias;
                Vector3 n = texel.Normal, ng = texel.GeoNormal;
                Frame(n, out Vector3 tangent, out Vector3 bitangent);
                uint h = Hash((uint)i * 2654435761u + 17u);
                float r1 = (h & 0xFFFF) / 65536f, r2 = (Hash(h) & 0xFFFF) / 65536f;

                Vector3 sum = Vector3.Zero, dirSum = Vector3.Zero;
                float lumSum = 0;
                int backFaces = 0, occluded = 0;
                for (int k = 0; k < samples; k++)
                {
                    //Cosine-weighted hemisphere (Malley), the pattern shifted per texel (Cranley-Patterson)
                    float u = pattern[k].X + r1, v = pattern[k].Y + r2;
                    if (u >= 1) u -= 1;
                    if (v >= 1) v -= 1;
                    float radius = (float)Math.Sqrt(u), phi = MathHelper.TwoPi * v;
                    Vector3 dir = tangent * (radius * (float)Math.Cos(phi)) + bitangent * (radius * (float)Math.Sin(phi)) +
                                  n * (float)Math.Sqrt(Math.Max(0, 1 - u));
                    //A smooth normal tilts the hemisphere below the face: mirror those rays back above it
                    float below = Vector3.Dot(dir, ng);
                    if (below < 0) dir -= ng * (2 * below);

                    Vector3 radiance;
                    if (!bvh.Intersect(origin, dir, maxDistance, out float t, out int tri))
                    {
                        radiance = dir.Z >= 0 ? sky : ground;
                    }
                    else
                    {
                        Vector3 hitNormal = input.Normal[tri];
                        if (Vector3.Dot(hitNormal, dir) > 0)
                        {
                            backFaces++;
                            continue;
                        }
                        if (t < aoDistance) occluded++;
                        Vector3 p = origin + dir * t + hitNormal * RayBias;
                        Vector3 irradiance = ProbeVolumeBaker.DirectIrradiance(input, bvh, p, hitNormal, maxDistance);
                        if (probes != null) irradiance += probes.VolumeAt(p).EvaluateIrradiance(p, hitNormal);
                        radiance = input.AlbedoAt(tri, p) * irradiance;
                    }
                    sum += radiance;
                    float lum = Vector3.Dot(radiance, Luma);
                    dirSum += dir * lum;
                    lumSum += lum;
                }

                float invN = 1f / samples;
                var g = new Gathered
                {
                    Irradiance = sum * invN,
                    Direction = dirSum * invN,
                    Luminance = lumSum * invN,
                    Ao = 1 - occluded / (float)Math.Max(1, samples - backFaces),
                    Valid = backFaces <= validity,
                };

                //Emissive surfaces as area lights (their light is indirect here: nothing real-time draws it)
                foreach (int emitter in input.Emitters)
                {
                    if (!ProbeVolumeBaker.EmitterSolidAngle(input, emitter, origin, out Vector3 toEmitter, out float distance, out float solidAngle)) continue;
                    float ndl = Vector3.Dot(n, toEmitter);
                    if (ndl <= 0) continue;
                    if (bvh.Occluded(origin, toEmitter, distance - RayBias * 4)) continue;
                    Vector3 e = input.Emission[emitter] * (solidAngle * ndl / MathHelper.Pi);
                    g.Irradiance += e;
                    float lum = Vector3.Dot(e, Luma);
                    g.Direction += toEmitter * lum;
                    g.Luminance += lum;
                }

                g.Mask = ShadowMask(origin, ng, channelLights, bvh, shadowPattern, sunCos, maxDistance, h);
                results[i] = g;

                int finished = Interlocked.Increment(ref done);
                if ((finished & 1023) == 0) progress?.Invoke(finished / (float)texels.Count, stage);
            });
            ct.ThrowIfCancellationRequested();

            // ---- Clean up: inside-geometry texels, noise, write pages, gutters ----
            progress?.Invoke(1, "Lightmaps: filtering");
            FillInvalid(texels, results, texelMaps, data);
            if (s.LightmapDenoise) Denoise(texels, results, texelMaps, data, layouts);
            for (int p = 0; p < data.Pages.Count; p++)
                WritePage(data.Pages[p], texels, results, texelMaps[p]);

            data.BakedAtUtc = DateTime.UtcNow;
            data.BakeSeconds = timer.Elapsed.TotalSeconds;
            return data;
        }

        // ---------------- Layout ----------------

        private static LightmapData.Mesh MeshOf(LightmapUnwrap unwrap)
        {
            var mesh = new LightmapData.Mesh();
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (LightmapUnwrap.Part part in unwrap.Parts)
            {
                mesh.Parts.Add(part);
                foreach (Vector3 p in part.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            }
            if (min.X <= max.X)
            {
                mesh.Center = (min + max) * 0.5f;
                mesh.Radius = Vector3.Distance(min, max) * 0.5f;
            }
            return mesh;
        }

        /// <summary>Sizes the gameobject's charts in texels and shelf-packs them into its block (lowering the density until it fits a page).</summary>
        private static void LayoutBlock(InstanceLayout layout, int maxSize)
        {
            List<LightmapUnwrap.ChartInfo> charts = layout.Unwrap.Charts;
            layout.ChartOrigin = new Point[charts.Count];
            layout.ChartScale = new Vector2[charts.Count];
            var sizes = new Point[charts.Count];
            float worldArea = layout.Unwrap.Authored ? WorldArea(layout) : 0;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                long area = 0;
                int widest = 1;
                for (int c = 0; c < charts.Count; c++)
                {
                    LightmapUnwrap.ChartInfo chart = charts[c];
                    Vector2 extent = chart.Max - chart.Min;
                    Vector2 texels;
                    if (chart.Axis < 0)
                    {
                        //Authored layouts get the texels their surface area needs, assuming ~60 % of the square is used
                        float side = (float)Math.Sqrt(worldArea / 0.6f) * layout.Density;
                        texels = new Vector2(side);
                        layout.ChartScale[c] = new Vector2(side / Math.Max(extent.X, 1e-6f), side / Math.Max(extent.Y, 1e-6f));
                    }
                    else
                    {
                        Vector2 scale = LightmapUnwrap.PlaneScale(chart.Axis, layout.Input.Scale) * layout.Density;
                        texels = extent * scale;
                        layout.ChartScale[c] = scale;
                    }
                    //One gutter texel each side and room for the fractional end (see the class comment)
                    sizes[c] = new Point((int)Math.Ceiling(texels.X + 0.5f) + 1, (int)Math.Ceiling(texels.Y + 0.5f) + 1);
                    area += (long)sizes[c].X * sizes[c].Y;
                    widest = Math.Max(widest, sizes[c].X);
                }

                int width = Math.Max(widest, (int)Math.Ceiling(Math.Sqrt(area * 1.1)));
                ShelfPack(sizes, width, layout.ChartOrigin, out int usedWidth, out int usedHeight);
                layout.BlockWidth = usedWidth;
                layout.BlockHeight = usedHeight;
                if (usedWidth <= maxSize && usedHeight <= maxSize) return;
                layout.Density *= 0.95f * maxSize / Math.Max(usedWidth, usedHeight);
            }
        }

        private static float WorldArea(InstanceLayout layout)
        {
            float area = 0;
            foreach (LightmapUnwrap.Part part in layout.Unwrap.Parts)
                for (int t = 0; t + 2 < part.Indices.Length; t += 3)
                {
                    Vector3 a = Vector3.Transform(part.Positions[part.Indices[t]], layout.Input.World);
                    Vector3 b = Vector3.Transform(part.Positions[part.Indices[t + 1]], layout.Input.World);
                    Vector3 c = Vector3.Transform(part.Positions[part.Indices[t + 2]], layout.Input.World);
                    area += Vector3.Cross(b - a, c - a).Length() * 0.5f;
                }
            return Math.Max(area, 1e-4f);
        }

        /// <summary>Rows of rectangles, tallest first, wrapping at <paramref name="width"/>.</summary>
        private static void ShelfPack(Point[] sizes, int width, Point[] origins, out int usedWidth, out int usedHeight)
        {
            var order = new int[sizes.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => sizes[b].Y != sizes[a].Y ? sizes[b].Y.CompareTo(sizes[a].Y) : a.CompareTo(b));

            int x = 0, y = 0, shelf = 0;
            usedWidth = 0;
            foreach (int i in order)
            {
                if (x > 0 && x + sizes[i].X > width)
                {
                    y += shelf;
                    x = 0;
                    shelf = 0;
                }
                origins[i] = new Point(x, y);
                x += sizes[i].X;
                shelf = Math.Max(shelf, sizes[i].Y);
                usedWidth = Math.Max(usedWidth, x);
            }
            usedHeight = y + shelf;
        }

        /// <summary>Packs the gameobjects' blocks into pages and gives every lightmap vertex its page coordinate.</summary>
        private static void PackPages(List<InstanceLayout> layouts, int maxSize, LightmapData data)
        {
            long total = 0;
            int widest = 1;
            foreach (InstanceLayout l in layouts)
            {
                total += (long)l.BlockWidth * l.BlockHeight;
                widest = Math.Max(widest, l.BlockWidth);
            }
            int width = Math.Min(maxSize, Math.Max(widest, RoundUp4((int)Math.Ceiling(Math.Sqrt(total * 1.1)))));

            var order = new List<int>();
            for (int i = 0; i < layouts.Count; i++) order.Add(i);
            order.Sort((a, b) => layouts[b].BlockHeight != layouts[a].BlockHeight
                ? layouts[b].BlockHeight.CompareTo(layouts[a].BlockHeight) : a.CompareTo(b));

            var pageHeights = new List<int>();
            int page = -1, x = 0, y = 0, shelf = 0;
            foreach (int i in order)
            {
                InstanceLayout l = layouts[i];
                if (page < 0) { page = 0; pageHeights.Add(0); }
                if (x > 0 && x + l.BlockWidth > width)
                {
                    y += shelf;
                    x = 0;
                    shelf = 0;
                }
                if (y + l.BlockHeight > maxSize)
                {
                    page++;
                    pageHeights.Add(0);
                    x = 0; y = 0; shelf = 0;
                }
                l.Page = page;
                l.BlockOrigin = new Point(x, y);
                x += l.BlockWidth;
                shelf = Math.Max(shelf, l.BlockHeight);
                pageHeights[page] = Math.Max(pageHeights[page], y + shelf);
            }

            foreach (int height in pageHeights)
            {
                int w = RoundUp4(width), h = RoundUp4(Math.Max(height, 4));
                int n = w * h;
                data.Pages.Add(new LightmapData.Page { Width = w, Height = h, Irradiance = new ulong[n], Direction = new uint[n], Mask = new uint[n] });
            }

            //Page coordinates of every lightmap vertex
            foreach (InstanceLayout l in layouts)
            {
                LightmapData.Page target = data.Pages[l.Page];
                var instance = new LightmapData.Instance
                {
                    EntityId = l.Input.EntityId,
                    Mesh = l.Mesh,
                    Page = l.Page,
                    Uv = new Vector2[l.Unwrap.Parts.Count][],
                };
                l.Texels = new Vector2[l.Unwrap.Parts.Count][];
                for (int p = 0; p < l.Unwrap.Parts.Count; p++)
                {
                    LightmapUnwrap.Part part = l.Unwrap.Parts[p];
                    if (!part.Lightmapped || part.Positions.Length == 0) continue;
                    var texel = new Vector2[part.Positions.Length];
                    var uv = new Vector2[part.Positions.Length];
                    for (int v = 0; v < texel.Length; v++)
                    {
                        int c = part.Chart[v];
                        LightmapUnwrap.ChartInfo chart = l.Unwrap.Charts[c];
                        Vector2 origin = new Vector2(l.BlockOrigin.X + l.ChartOrigin[c].X + 1, l.BlockOrigin.Y + l.ChartOrigin[c].Y + 1);
                        texel[v] = origin + (part.Coords[v] - chart.Min) * l.ChartScale[c];
                        uv[v] = new Vector2(texel[v].X / target.Width, texel[v].Y / target.Height);
                    }
                    l.Texels[p] = texel;
                    instance.Uv[p] = uv;
                }
                data.Instances.Add(instance);
            }
        }

        private static int RoundUp4(int v) => (v + 3) & ~3;

        // ---------------- Texels ----------------

        private static void Rasterize(InstanceLayout layout, int instanceIndex, LightmapData.Page page, int[] map, List<Texel> texels)
        {
            Matrix world = layout.Input.World;
            Matrix normalMatrix = Matrix.Transpose(Matrix.Invert(world));
            for (int p = 0; p < layout.Unwrap.Parts.Count; p++)
            {
                LightmapUnwrap.Part part = layout.Unwrap.Parts[p];
                Vector2[] uv = layout.Texels[p];
                if (uv == null) continue;
                LightmapUnwrap.SourcePart source = layout.Input.Parts[p];

                for (int t = 0; t + 2 < part.Indices.Length; t += 3)
                {
                    int i0 = part.Indices[t], i1 = part.Indices[t + 1], i2 = part.Indices[t + 2];
                    Vector3 p0 = Vector3.Transform(part.Positions[i0], world);
                    Vector3 p1 = Vector3.Transform(part.Positions[i1], world);
                    Vector3 p2 = Vector3.Transform(part.Positions[i2], world);
                    Vector3 ng = Vector3.Cross(p1 - p0, p2 - p0);
                    if (ng.LengthSquared() < 1e-20f) continue;
                    ng.Normalize();

                    Vector3 n0 = ng, n1 = ng, n2 = ng;
                    if (source.Normals != null)
                    {
                        n0 = SafeNormal(Vector3.TransformNormal(source.Normals[part.SourceVertex[i0]], normalMatrix), ng);
                        n1 = SafeNormal(Vector3.TransformNormal(source.Normals[part.SourceVertex[i1]], normalMatrix), ng);
                        n2 = SafeNormal(Vector3.TransformNormal(source.Normals[part.SourceVertex[i2]], normalMatrix), ng);
                        //Same winding rule as the gather: the authored normals decide which side is the front
                        if (Vector3.Dot(n0 + n1 + n2, ng) < 0) ng = -ng;
                    }

                    Vector2 a = uv[i0], b = uv[i1], c = uv[i2];
                    float area = Cross(b - a, c - a);
                    if (Math.Abs(area) < 1e-10f) continue;
                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) - 1));
                    int x1 = Math.Min(page.Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)) + 1));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 1));
                    int y1 = Math.Min(page.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) + 1));

                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var q = new Vector2(x + 0.5f, y + 0.5f);
                        float distance = ClosestBarycentric(q, a, b, c, area, out float w0, out float w1, out float w2);
                        if (distance > CoverDistance) continue;
                        int index = y * page.Width + x;
                        int existing = map[index];
                        if (existing >= 0 && texels[existing].Distance <= distance) continue;

                        var texel = new Texel
                        {
                            Page = layout.Page, X = x, Y = y, Instance = instanceIndex,
                            Position = p0 * w0 + p1 * w1 + p2 * w2,
                            Normal = SafeNormal(n0 * w0 + n1 * w1 + n2 * w2, ng),
                            GeoNormal = ng,
                            Distance = distance,
                        };
                        if (existing >= 0) texels[existing] = texel;
                        else
                        {
                            map[index] = texels.Count;
                            texels.Add(texel);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Barycentric coordinates of the point of triangle abc closest to q, and the distance to it (0 inside).
        /// <paramref name="area"/> = cross(b - a, c - a).
        /// </summary>
        private static float ClosestBarycentric(Vector2 q, Vector2 a, Vector2 b, Vector2 c, float area,
            out float w0, out float w1, out float w2)
        {
            //q = a + w1 (b - a) + w2 (c - a)
            w1 = Cross(q - a, c - a) / area;
            w2 = Cross(b - a, q - a) / area;
            w0 = 1 - w1 - w2;
            const float inside = -1e-5f;
            if (w0 >= inside && w1 >= inside && w2 >= inside) return 0;

            //Outside: nearest point on the three edges
            float best = float.MaxValue;
            float bw0 = 0, bw1 = 0, bw2 = 0;
            Edge(q, a, b, 0, 1, ref best, ref bw0, ref bw1, ref bw2);
            Edge(q, b, c, 1, 2, ref best, ref bw0, ref bw1, ref bw2);
            Edge(q, c, a, 2, 0, ref best, ref bw0, ref bw1, ref bw2);
            w0 = bw0; w1 = bw1; w2 = bw2;
            return (float)Math.Sqrt(best);
        }

        private static void Edge(Vector2 q, Vector2 p, Vector2 r, int ip, int ir, ref float best, ref float w0, ref float w1, ref float w2)
        {
            Vector2 e = r - p;
            float len = e.LengthSquared();
            float t = len > 1e-12f ? MathHelper.Clamp(Vector2.Dot(q - p, e) / len, 0, 1) : 0;
            float dist = Vector2.DistanceSquared(q, p + e * t);
            if (dist >= best) return;
            best = dist;
            Span<float> w = stackalloc float[3];
            w[ip] = 1 - t;
            w[ir] = t;
            w0 = w[0]; w1 = w[1]; w2 = w[2];
        }

        private static Vector3 SafeNormal(Vector3 n, Vector3 fallback)
        {
            float len = n.Length();
            return len > 1e-8f ? n / len : fallback;
        }

        // ---------------- Shadow mask ----------------

        private sealed class ChannelLight
        {
            public bool Directional;
            public Vector3 ToLight;   //directional
            public Vector3 Position;  //point/spot
            public float Radius, SourceRadius;
        }

        /// <summary>
        /// Gives static lights (Baked Shadows) one of the 4 mask channels, so that no two lights whose ranges overlap share
        /// one (graph colouring, strongest lights first; the sun overlaps everything). Lights left over keep real-time
        /// shadows only.
        /// </summary>
        private static List<ChannelLight>[] AssignChannels(LightingBakeInput input, LightmapData data)
        {
            var lights = new List<ChannelLight>[LightmapData.MaxChannels];
            for (int c = 0; c < lights.Length; c++) lights[c] = new List<ChannelLight>();

            foreach (LightingBakeInput.DirectionalLightInput dl in input.DirectionalLights)
            {
                if (!dl.BakedShadows) continue;
                int channel = -1;
                for (int c = 0; c < lights.Length && channel < 0; c++)
                    if (lights[c].Count == 0) channel = c;
                if (channel < 0) { data.LightsWithoutChannel++; continue; }
                lights[channel].Add(new ChannelLight { Directional = true, ToLight = dl.ToLight });
                data.Channels.Add(new LightmapData.LightChannel { LightId = dl.LightId, Channel = channel, Directional = true, Direction = dl.ToLight });
            }

            var points = new List<LightingBakeInput.PointLightInput>();
            foreach (LightingBakeInput.PointLightInput pl in input.PointLights)
                if (pl.BakedShadows) points.Add(pl);
            points.Sort((a, b) => (Vector3.Dot(b.Radiance, Luma) * b.Radius * b.Radius).CompareTo(Vector3.Dot(a.Radiance, Luma) * a.Radius * a.Radius));

            foreach (LightingBakeInput.PointLightInput pl in points)
            {
                int channel = -1;
                for (int c = 0; c < lights.Length && channel < 0; c++)
                {
                    bool free = true;
                    foreach (ChannelLight other in lights[c])
                        if (other.Directional || Vector3.Distance(other.Position, pl.Position) < other.Radius + pl.Radius)
                        {
                            free = false;
                            break;
                        }
                    if (free) channel = c;
                }
                if (channel < 0) { data.LightsWithoutChannel++; continue; }
                lights[channel].Add(new ChannelLight { Position = pl.Position, Radius = pl.Radius, SourceRadius = pl.SourceRadius });
                data.Channels.Add(new LightmapData.LightChannel { LightId = pl.LightId, Channel = channel, Position = pl.Position, Radius = pl.Radius });
            }
            return lights;
        }

        /// <summary>Share of each channel's light source the point sees (1 where no light of the channel reaches).</summary>
        private static Vector4 ShadowMask(Vector3 origin, Vector3 ng, List<ChannelLight>[] channels, TriangleBvh bvh,
            Vector2[] pattern, float sunCos, float maxDistance, uint seed)
        {
            Span<float> mask = stackalloc float[LightmapData.MaxChannels];
            for (int c = 0; c < mask.Length; c++)
            {
                mask[c] = 1;
                ChannelLight light = null;
                foreach (ChannelLight candidate in channels[c])
                    if (candidate.Directional || Vector3.DistanceSquared(candidate.Position, origin) < candidate.Radius * candidate.Radius)
                    {
                        light = candidate;
                        break;
                    }
                if (light == null) continue;

                Vector3 centre = light.Directional ? light.ToLight : Vector3.Normalize(light.Position - origin);
                if (Vector3.Dot(centre, ng) < -0.2f) { mask[c] = 0; continue; }

                uint h = Hash(seed + (uint)c * 7919u);
                float r1 = (h & 0xFFFF) / 65536f, r2 = (Hash(h) & 0xFFFF) / 65536f;
                int visible = 0;
                for (int k = 0; k < pattern.Length; k++)
                {
                    float u = pattern[k].X + r1, v = pattern[k].Y + r2;
                    if (u >= 1) u -= 1;
                    if (v >= 1) v -= 1;
                    if (light.Directional)
                    {
                        //Uniform over the sun's disc (a cone of angular radius acos(sunCos))
                        Frame(centre, out Vector3 t, out Vector3 b);
                        float cosTheta = 1 - u * (1 - sunCos);
                        float sinTheta = (float)Math.Sqrt(Math.Max(0, 1 - cosTheta * cosTheta));
                        float phi = MathHelper.TwoPi * v;
                        Vector3 dir = t * (sinTheta * (float)Math.Cos(phi)) + b * (sinTheta * (float)Math.Sin(phi)) + centre * cosTheta;
                        if (!bvh.Occluded(origin, dir, maxDistance)) visible++;
                    }
                    else
                    {
                        //A point on the light's sphere
                        float z = 1 - 2 * u, r = (float)Math.Sqrt(Math.Max(0, 1 - z * z)), phi = MathHelper.TwoPi * v;
                        Vector3 target = light.Position + new Vector3(r * (float)Math.Cos(phi), r * (float)Math.Sin(phi), z) *
                                         Math.Max(light.SourceRadius, MinShadowSourceRadius);
                        Vector3 toTarget = target - origin;
                        float distance = toTarget.Length();
                        if (distance < 1e-4f) { visible++; continue; }
                        if (!bvh.Occluded(origin, toTarget / distance, distance - RayBias)) visible++;
                    }
                }
                mask[c] = visible / (float)pattern.Length;
            }
            return new Vector4(mask[0], mask[1], mask[2], mask[3]);
        }

        // ---------------- Filtering ----------------

        /// <summary>Texels whose rays mostly hit back faces sit inside geometry: take their valid neighbours' light.</summary>
        private static void FillInvalid(List<Texel> texels, Gathered[] results, int[][] maps, LightmapData data)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                bool changed = false;
                var filled = new List<(int Index, Gathered Value)>();
                for (int i = 0; i < texels.Count; i++)
                {
                    if (results[i].Valid) continue;
                    Texel t = texels[i];
                    LightmapData.Page page = data.Pages[t.Page];
                    Gathered sum = default;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = t.X + dx, y = t.Y + dy;
                        if (x < 0 || y < 0 || x >= page.Width || y >= page.Height) continue;
                        int j = maps[t.Page][y * page.Width + x];
                        if (j < 0 || !results[j].Valid || texels[j].Instance != t.Instance) continue;
                        sum.Irradiance += results[j].Irradiance;
                        sum.Direction += results[j].Direction;
                        sum.Luminance += results[j].Luminance;
                        sum.Ao += results[j].Ao;
                        count++;
                    }
                    if (count == 0) continue;
                    float inv = 1f / count;
                    var value = new Gathered
                    {
                        Irradiance = sum.Irradiance * inv, Direction = sum.Direction * inv, Luminance = sum.Luminance * inv,
                        Ao = sum.Ao * inv, Mask = results[i].Mask, Valid = true,
                    };
                    filled.Add((i, value));
                    changed = true;
                }
                foreach (var (index, value) in filled) results[index] = value;
                if (!changed) break;
            }
        }

        /// <summary>
        /// Edge-aware 5×5 blur within each gameobject: neighbours count less the more their normal or position differs
        /// (one texel of world space per <see cref="InstanceLayout.Density"/>). The shadow mask gets a lighter 3×3 pass.
        /// </summary>
        private static void Denoise(List<Texel> texels, Gathered[] results, int[][] maps, LightmapData data, List<InstanceLayout> layouts)
        {
            var output = new Gathered[results.Length];
            Parallel.For(0, texels.Count, i =>
            {
                Texel t = texels[i];
                LightmapData.Page page = data.Pages[t.Page];
                float texelSize = 1f / Math.Max(layouts[t.Instance].Density, 0.01f);
                float positionScale = 1f / (2 * (texelSize * 1.5f) * (texelSize * 1.5f));
                Vector3 irradiance = Vector3.Zero, direction = Vector3.Zero;
                float luminance = 0, ao = 0, weight = 0;
                Vector4 mask = Vector4.Zero;
                float maskWeight = 0;
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = t.X + dx, y = t.Y + dy;
                    if (x < 0 || y < 0 || x >= page.Width || y >= page.Height) continue;
                    int j = maps[t.Page][y * page.Width + x];
                    if (j < 0 || texels[j].Instance != t.Instance) continue;
                    float facing = Math.Max(0, Vector3.Dot(texels[j].Normal, t.Normal));
                    facing *= facing; facing *= facing; facing *= facing; //^8
                    float w = facing * (float)Math.Exp(-(dx * dx + dy * dy) / 4.5f) *
                              (float)Math.Exp(-Vector3.DistanceSquared(texels[j].Position, t.Position) * positionScale);
                    irradiance += results[j].Irradiance * w;
                    direction += results[j].Direction * w;
                    luminance += results[j].Luminance * w;
                    ao += results[j].Ao * w;
                    weight += w;
                    if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                    {
                        float wm = dx == 0 && dy == 0 ? 2 * w : w;
                        mask += results[j].Mask * wm;
                        maskWeight += wm;
                    }
                }
                Gathered g = results[i];
                if (weight > 1e-6f)
                {
                    g.Irradiance = irradiance / weight;
                    g.Direction = direction / weight;
                    g.Luminance = luminance / weight;
                    g.Ao = ao / weight;
                }
                if (maskWeight > 1e-6f) g.Mask = mask / maskWeight;
                output[i] = g;
            });
            Array.Copy(output, results, results.Length);
        }

        /// <summary>
        /// Writes one page: per texel the irradiance divided by the directional term at the baked normal (the lightmap pass
        /// multiplies it back with the G-buffer normal), the direction and its strength, AO and the mask; then grows every
        /// chart into its gutter so bilinear filtering never reads an empty texel.
        /// </summary>
        private static void WritePage(LightmapData.Page page, List<Texel> texels, Gathered[] results, int[] map)
        {
            int n = page.Width * page.Height;
            var irradiance = new Vector4[n];
            var direction = new Vector4[n];
            var mask = new Vector4[n];
            var filled = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int j = map[i];
                if (j < 0) continue;
                Gathered g = results[j];
                float strength = g.Luminance > 1e-8f ? MathHelper.Clamp(g.Direction.Length() / g.Luminance, 0, 1) : 0;
                Vector3 dir = g.Direction.LengthSquared() > 1e-16f ? Vector3.Normalize(g.Direction) : texels[j].Normal;
                float lobe = Vector3.Dot(texels[j].Normal, dir) * 0.5f + 0.5f;
                float term = Math.Max(0.25f, 1 - strength + strength * lobe);
                irradiance[i] = new Vector4(Vector3.Max(g.Irradiance, Vector3.Zero) / term, MathHelper.Clamp(g.Ao, 0, 1));
                direction[i] = new Vector4(dir * 0.5f + new Vector3(0.5f), strength);
                mask[i] = g.Mask;
                filled[i] = true;
            }

            //Gutters: a few rings of the average of filled neighbours
            for (int ring = 0; ring < 3; ring++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 0; y < page.Height; y++)
                for (int x = 0; x < page.Width; x++)
                {
                    int i = y * page.Width + x;
                    if (filled[i]) continue;
                    Vector4 a = Vector4.Zero, b = Vector4.Zero, c = Vector4.Zero;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= page.Width || ny >= page.Height) continue;
                        int j = ny * page.Width + nx;
                        if (!filled[j]) continue;
                        a += irradiance[j]; b += direction[j]; c += mask[j];
                        count++;
                    }
                    if (count == 0) continue;
                    irradiance[i] = a / count; direction[i] = b / count; mask[i] = c / count;
                    next[i] = true;
                }
                filled = next;
            }

            for (int i = 0; i < n; i++)
            {
                page.Irradiance[i] = LightmapData.PackIrradiance(irradiance[i]);
                page.Direction[i] = filled[i] ? new Color(direction[i]).PackedValue : new Color(128, 128, 255, 0).PackedValue;
                page.Mask[i] = filled[i] ? new Color(mask[i]).PackedValue : Color.White.PackedValue;
            }
        }

        // ---------------- Sampling helpers ----------------

        private static Vector2[] Hammersley(int count)
        {
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                uint bits = (uint)i;
                bits = (bits << 16) | (bits >> 16);
                bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
                bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
                bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
                bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
                result[i] = new Vector2((i + 0.5f) / count, bits * 2.3283064365386963e-10f);
            }
            return result;
        }

        private static void Frame(Vector3 n, out Vector3 t, out Vector3 b)
        {
            Vector3 up = Math.Abs(n.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
            t = Vector3.Normalize(Vector3.Cross(up, n));
            b = Vector3.Cross(n, t);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }
}
