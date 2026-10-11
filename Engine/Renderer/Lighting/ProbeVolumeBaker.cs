using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// CPU reference baker for the irradiance probe volume. Runs on a worker thread against a
    /// <see cref="LightingBakeInput"/> snapshot.
    ///
    /// Per pass, every probe traces <see cref="LightingSettings.SamplesPerProbe"/> rays:
    /// <list type="bullet">
    /// <item>miss  → sky radiance</item>
    /// <item>back face → counted towards "probe is inside geometry", contributes nothing</item>
    /// <item>front face → albedo × (direct light at the hit + previous pass's probe irradiance)</item>
    /// </list>
    /// The radiance is projected into cosine-convolved L1 SH. Pass k reads the probes of pass k-1
    /// for the hit points' indirect light, so N passes give N bounces (the DDGI recurrence,
    /// converged offline). Invalid probes are then filled from valid neighbours.
    ///
    /// Units match the deferred light shaders (diffuse × 0.1, linear colour), so a probe's
    /// irradiance can go straight into the diffuse light buffer.
    /// </summary>
    public static class ProbeVolumeBaker
    {
        //Same output scale as DeferredPointLight.fx (OUTPUTCONST) / DeferredDirectionalLight.fx
        private const float LightOutputScale = 0.1f;
        private const float RayBias = 0.01f;
        private const int MinProbesPerAxis = 2;

        public delegate void ProgressCallback(float progress, string stage);

        /// <summary>Volume layout (bounds + per-axis probe counts) for the given settings.</summary>
        public static void ComputeLayout(LightingSettings s, BoundingBox geometryBounds,
            out Vector3 min, out Vector3 max, out int countX, out int countY, out int countZ)
        {
            if (s.AutoBounds)
            {
                Vector3 pad = new Vector3(Math.Max(0, s.BoundsPadding));
                min = geometryBounds.Min - pad;
                max = geometryBounds.Max + pad;
            }
            else
            {
                min = Vector3.Min(s.BoundsMin, s.BoundsMax);
                max = Vector3.Max(s.BoundsMin, s.BoundsMax);
            }

            //Never degenerate: the shader divides by the extent
            Vector3 extent = Vector3.Max(max - min, new Vector3(0.01f));
            max = min + extent;

            int maxPerAxis = Math.Max(MinProbesPerAxis, s.MaxProbesPerAxis);
            float spacing = Math.Max(0.05f, s.ProbeSpacing);
            countX = AxisCount(extent.X, spacing, maxPerAxis);
            countY = AxisCount(extent.Y, spacing, maxPerAxis);
            countZ = AxisCount(extent.Z, spacing, maxPerAxis);
        }

        private static int AxisCount(float extent, float spacing, int max) =>
            Math.Clamp((int)Math.Ceiling(extent / spacing) + 1, MinProbesPerAxis, max);

        public static ProbeVolumeData Bake(LightingBakeInput input, LightingSettings s,
            ProgressCallback progress, CancellationToken ct)
        {
            var timer = Stopwatch.StartNew();

            ComputeLayout(s, input.GeometryBounds, out Vector3 min, out Vector3 max,
                out int cx, out int cy, out int cz);

            progress?.Invoke(0, $"Building BVH ({input.TriangleCount:N0} triangles)");
            var bvh = new TriangleBvh(input.A, input.B, input.C);
            ct.ThrowIfCancellationRequested();

            int sampleCount = Math.Max(16, s.SamplesPerProbe);
            int passes = Math.Max(1, s.Bounces);
            Vector3[] directions = FibonacciSphere(sampleCount);
            Vector3 sky = new Vector3(
                (float)Math.Pow(MathHelper.Clamp(s.SkyColor.X, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(s.SkyColor.Y, 0, 1), 2.2),
                (float)Math.Pow(MathHelper.Clamp(s.SkyColor.Z, 0, 1), 2.2)) * Math.Max(0, s.SkyIntensity);

            //Rays that leave the volume's neighbourhood count as escaped
            float maxDistance = Vector3.Distance(Vector3.Min(min, input.GeometryBounds.Min),
                                                 Vector3.Max(max, input.GeometryBounds.Max)) * 2 + 1;

            ProbeVolumeData previous = null;
            ProbeVolumeData current = null;
            int probeCount = cx * cy * cz;

            //Free distances are only needed out to a few probes: a wall further away than that
            //can't sit between a probe and the points it is blended into.
            Vector3 extent = max - min;
            float cellSize = Math.Max(extent.X / Math.Max(1, cx - 1), Math.Max(extent.Y / Math.Max(1, cy - 1), extent.Z / Math.Max(1, cz - 1)));
            float distanceCap = cellSize * 4;

            for (int pass = 0; pass < passes; pass++)
            {
                current = ProbeVolumeData.Create(cx, cy, cz, min, max);
                int done = 0;
                int passIndex = pass;
                ProbeVolumeData indirect = previous;
                string stage = $"Bounce {pass + 1}/{passes} — {probeCount:N0} probes × {sampleCount} rays";
                progress?.Invoke(pass / (float)passes, stage);

                Parallel.For(0, probeCount, new ParallelOptions { CancellationToken = ct }, i =>
                {
                    int x = i % cx, y = i / cx % cy, z = i / (cx * cy);
                    Vector3 origin = current.ProbePosition(x, y, z);
                    Matrix rotation = RandomRotation(i, passIndex);

                    Vector3 sum = Vector3.Zero, sumX = Vector3.Zero, sumY = Vector3.Zero, sumZ = Vector3.Zero;
                    int backFaces = 0;
                    Vector3 distPos = Vector3.Zero, distNeg = Vector3.Zero, weightPos = Vector3.Zero, weightNeg = Vector3.Zero;

                    foreach (Vector3 baseDir in directions)
                    {
                        Vector3 dir = Vector3.TransformNormal(baseDir, rotation);
                        Vector3 radiance;

                        bool hit = bvh.Intersect(origin, dir, maxDistance, out float t, out int tri);
                        AccumulateDistance(dir, hit ? Math.Min(t, distanceCap) : distanceCap,
                            ref distPos, ref distNeg, ref weightPos, ref weightNeg);

                        if (!hit)
                        {
                            radiance = sky;
                        }
                        else
                        {
                            Vector3 n = input.Normal[tri];
                            if (Vector3.Dot(n, dir) > 0)
                            {
                                backFaces++;
                                continue;
                            }
                            Vector3 p = origin + dir * t + n * RayBias;
                            Vector3 irradiance = DirectIrradiance(input, bvh, p, n, maxDistance);
                            if (indirect != null) irradiance += indirect.EvaluateIrradiance(p, n);
                            radiance = input.Albedo[tri] * irradiance;
                        }

                        sum += radiance;
                        sumX += radiance * dir.X;
                        sumY += radiance * dir.Y;
                        sumZ += radiance * dir.Z;
                    }

                    //Cosine-convolved L1 SH, divided by π so a uniform radiance L gives E = L:
                    //  c0 = mean(L),  c1 = (2/N) Σ L·dir
                    float invN = 1f / directions.Length;
                    StoreProbe(current, i, sum * invN, sumX * (2 * invN), sumY * (2 * invN), sumZ * (2 * invN));
                    current.DistPos[i] = new Vector4(distPos / Vector3.Max(weightPos, new Vector3(1e-6f)), 0);
                    current.DistNeg[i] = new Vector4(distNeg / Vector3.Max(weightNeg, new Vector3(1e-6f)), 0);
                    current.Valid[i] = (byte)(backFaces <= s.ValidityThreshold * directions.Length ? 1 : 0);

                    int finished = Interlocked.Increment(ref done);
                    if ((finished & 255) == 0)
                        progress?.Invoke((passIndex + finished / (float)probeCount) / passes, stage);
                });

                ct.ThrowIfCancellationRequested();
                DilateInvalidProbes(current);
                previous = current;
            }

            current.SamplesPerProbe = sampleCount;
            current.Bounces = passes;
            current.BakedAtUtc = DateTime.UtcNow;
            current.BakeSeconds = timer.Elapsed.TotalSeconds;
            progress?.Invoke(1, "Done");
            return current;
        }

        /// <summary>
        /// Irradiance from the scene's lights at a surface point (white albedo), with hard shadows.
        /// Point and spot light attenuation mirrors DeferredPointLight.fx.
        /// </summary>
        private static Vector3 DirectIrradiance(LightingBakeInput input, TriangleBvh bvh, Vector3 p, Vector3 n, float maxDistance)
        {
            Vector3 result = Vector3.Zero;

            foreach (LightingBakeInput.DirectionalLightInput dl in input.DirectionalLights)
            {
                float ndl = Vector3.Dot(n, dl.ToLight);
                if (ndl <= 0) continue;
                if (bvh.Occluded(p, dl.ToLight, maxDistance)) continue;
                result += dl.Radiance * ndl;
            }

            foreach (LightingBakeInput.PointLightInput pl in input.PointLights)
            {
                Vector3 toLight = pl.Position - p;
                float distSq = toLight.LengthSquared();
                if (distSq >= pl.Radius * pl.Radius || distSq < 1e-8f) continue;
                float dist = (float)Math.Sqrt(distSq);
                Vector3 l = toLight / dist;
                float ndl = Vector3.Dot(n, l);
                if (ndl <= 0) continue;

                float relative = dist / pl.Radius;
                float denominator = 4 * relative + 1;
                float attenuation = MathHelper.Clamp(1 / (denominator * denominator) - 0.04f * relative, 0, 1);
                attenuation *= SpotLight.ConeFactor(-l, pl.SpotDirection, pl.SpotCosOuter, pl.SpotCosInner);
                if (attenuation <= 0) continue;
                if (bvh.Occluded(p, l, dist - RayBias)) continue;

                result += pl.Radiance * (ndl * attenuation);
            }

            return result * LightOutputScale;
        }

        /// <summary>
        /// Adds a ray's hit distance to the probe's six axis distances, weighted by cos⁴ to the axis so
        /// each one is the mean free distance in a ~35° cone around it.
        /// </summary>
        private static void AccumulateDistance(Vector3 dir, float distance,
            ref Vector3 distPos, ref Vector3 distNeg, ref Vector3 weightPos, ref Vector3 weightNeg)
        {
            Vector3 w = dir * dir;
            w *= w;
            Vector3 positive = new Vector3(dir.X > 0 ? 1 : 0, dir.Y > 0 ? 1 : 0, dir.Z > 0 ? 1 : 0);
            Vector3 negative = Vector3.One - positive;
            weightPos += w * positive; distPos += w * positive * distance;
            weightNeg += w * negative; distNeg += w * negative * distance;
        }

        private static void StoreProbe(ProbeVolumeData data, int i, Vector3 c0, Vector3 cx, Vector3 cy, Vector3 cz)
        {
            data.SHR[i] = Deringed(c0.X, cx.X, cy.X, cz.X);
            data.SHG[i] = Deringed(c0.Y, cx.Y, cy.Y, cz.Y);
            data.SHB[i] = Deringed(c0.Z, cx.Z, cy.Z, cz.Z);
        }

        //Scale the linear band down so E(n) = c0 + c1·n never goes negative
        private static Vector4 Deringed(float c0, float x, float y, float z)
        {
            float len = (float)Math.Sqrt(x * x + y * y + z * z);
            if (len > c0 && len > 0)
            {
                float k = c0 / len;
                x *= k; y *= k; z *= k;
            }
            return new Vector4(c0, x, y, z);
        }

        /// <summary>
        /// Replace probes that sit inside geometry with the average of their valid neighbours
        /// (26-neighbourhood, a few rings), so trilinear sampling doesn't drag black into walls.
        /// </summary>
        private static void DilateInvalidProbes(ProbeVolumeData d)
        {
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool changed = false;
                var filled = (byte[])d.Valid.Clone();

                for (int z = 0; z < d.CountZ; z++)
                for (int y = 0; y < d.CountY; y++)
                for (int x = 0; x < d.CountX; x++)
                {
                    int i = d.Index(x, y, z);
                    if (d.Valid[i] != 0) continue;

                    Vector4 r = Vector4.Zero, g = Vector4.Zero, b = Vector4.Zero;
                    int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, nz = z + dz;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= d.CountX || ny >= d.CountY || nz >= d.CountZ) continue;
                        int j = d.Index(nx, ny, nz);
                        if (d.Valid[j] == 0) continue;
                        r += d.SHR[j]; g += d.SHG[j]; b += d.SHB[j];
                        count++;
                    }
                    if (count == 0) continue;

                    d.SHR[i] = r / count; d.SHG[i] = g / count; d.SHB[i] = b / count;
                    filled[i] = 2; //filled this ring; still reported as invalid afterwards
                    changed = true;
                }

                for (int i = 0; i < filled.Length; i++)
                    if (filled[i] == 2) d.Valid[i] = 2;
                if (!changed) break;
            }

            //2 was only a "filled" marker for the next ring
            for (int i = 0; i < d.Valid.Length; i++)
                if (d.Valid[i] == 2) d.Valid[i] = 0;
        }

        //Evenly distributed unit vectors (spherical Fibonacci)
        private static Vector3[] FibonacciSphere(int count)
        {
            var result = new Vector3[count];
            float goldenAngle = MathHelper.Pi * (3 - (float)Math.Sqrt(5));
            for (int i = 0; i < count; i++)
            {
                float z = 1 - (2 * i + 1) / (float)count;
                float r = (float)Math.Sqrt(Math.Max(0, 1 - z * z));
                float phi = goldenAngle * i;
                result[i] = new Vector3((float)Math.Cos(phi) * r, (float)Math.Sin(phi) * r, z);
            }
            return result;
        }

        //Deterministic per-probe/per-pass random rotation: decorrelates the sample pattern
        //between neighbouring probes (less banding) while keeping bakes reproducible.
        private static Matrix RandomRotation(int probe, int pass)
        {
            uint h = Hash((uint)probe * 9781u + (uint)pass * 6271u + 1u);
            float u1 = (h & 0xFFFF) / 65535f;
            h = Hash(h);
            float u2 = (h & 0xFFFF) / 65535f;
            h = Hash(h);
            float u3 = (h & 0xFFFF) / 65535f;

            //Uniform random unit quaternion (Shoemake)
            float s1 = (float)Math.Sqrt(1 - u1), s2 = (float)Math.Sqrt(u1);
            var q = new Quaternion(
                s1 * (float)Math.Sin(MathHelper.TwoPi * u2),
                s1 * (float)Math.Cos(MathHelper.TwoPi * u2),
                s2 * (float)Math.Sin(MathHelper.TwoPi * u3),
                s2 * (float)Math.Cos(MathHelper.TwoPi * u3));
            return Matrix.CreateFromQuaternion(q);
        }

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }
    }
}
