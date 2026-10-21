using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>A vertex of a mesh being fractured (model-local, scaled metres).</summary>
    public struct FractureVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Tangent;
        public Vector2 TextureCoordinate;
    }

    /// <summary>
    /// An indexed triangle mesh for <see cref="MeshFracture"/>: three indices per triangle and one group per triangle
    /// (the model's mesh part it came from, or <see cref="MeshFracture.InteriorGroup"/> for faces a cut made).
    /// </summary>
    public sealed class FractureMesh
    {
        public readonly List<FractureVertex> Vertices = new();
        public readonly List<int> Indices = new();
        public readonly List<int> Groups = new();

        public int TriangleCount => Groups.Count;

        public void AddTriangle(int a, int b, int c, int group)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
            Groups.Add(group);
        }
    }

    /// <summary>One piece of a fractured mesh: its closed mesh (same space as the source), centre of mass and volume.</summary>
    public sealed class FracturePiece
    {
        public FractureMesh Mesh;
        public Vector3 Center;
        public float Volume;
    }

    /// <summary>
    /// 3D Voronoi fracture of a closed triangle mesh (a vase, a head, a pot), the solid counterpart of
    /// <see cref="GlassFracture"/>: each piece is the mesh clipped by the bisector planes between its site and every
    /// other site, and every cut is closed with a cap (the piece's inside, <see cref="InteriorGroup"/>) triangulated
    /// from the loops the cut leaves on the plane, holes included, so a hollow vase gives curved shell shards and a
    /// solid head gives wedges with a red inside. Pure math, no GPU or physics (checked by Tests/Components).
    /// </summary>
    public static class MeshFracture
    {
        /// <summary>Group of the faces cuts made (drawn with the interior material).</summary>
        public const int InteriorGroup = -1;
        /// <summary>Distances to a plane under this (m) count as on it.</summary>
        public const float Epsilon = 1e-6f;
        // Cut points closer than this (m) are welded when the cut loops are traced.
        private const float WeldGrid = 1e-5f;

        /// <summary>
        /// Enclosed volume (m³): positive when the triangles are counter-clockwise seen from outside, negative when
        /// clockwise. Its sign tells <see cref="Fracture"/> which way the caps must face.
        /// </summary>
        public static float SignedVolume(FractureMesh mesh)
        {
            double volume = 0;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                Vector3 a = mesh.Vertices[mesh.Indices[t * 3]].Position;
                Vector3 b = mesh.Vertices[mesh.Indices[t * 3 + 1]].Position;
                Vector3 c = mesh.Vertices[mesh.Indices[t * 3 + 2]].Position;
                volume += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            return (float)(volume / 6);
        }

        /// <summary>Centre of mass of the enclosed volume (uniform density); the vertex average for a flat or open mesh.</summary>
        public static Vector3 Centroid(FractureMesh mesh)
        {
            double volume = 0, x = 0, y = 0, z = 0;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                Vector3 a = mesh.Vertices[mesh.Indices[t * 3]].Position;
                Vector3 b = mesh.Vertices[mesh.Indices[t * 3 + 1]].Position;
                Vector3 c = mesh.Vertices[mesh.Indices[t * 3 + 2]].Position;
                double v = Vector3.Dot(a, Vector3.Cross(b, c));
                Vector3 s = a + b + c;
                volume += v;
                x += v * s.X;
                y += v * s.Y;
                z += v * s.Z;
            }
            if (Math.Abs(volume) > 1e-15)
                return new Vector3((float)(x / (4 * volume)), (float)(y / (4 * volume)), (float)(z / (4 * volume)));
            Vector3 sum = Vector3.Zero;
            foreach (FractureVertex v in mesh.Vertices) sum += v.Position;
            return mesh.Vertices.Count > 0 ? sum / mesh.Vertices.Count : Vector3.Zero;
        }

        /// <summary>
        /// Sites for the pieces: random points on the surface (by area) moved up to <paramref name="depth"/> metres
        /// inwards. Sites near the surface make pieces that run from the skin to the middle: shell shards for a
        /// hollow object, wedges for a solid one.
        /// </summary>
        public static List<Vector3> SurfaceSites(FractureMesh mesh, int count, Random random, float depth)
        {
            var sites = new List<Vector3>(count);
            int triangles = mesh.TriangleCount;
            if (triangles == 0 || count <= 0) return sites;
            float winding = SignedVolume(mesh) < 0 ? -1 : 1;
            var cumulative = new double[triangles];
            double total = 0;
            for (int t = 0; t < triangles; t++)
            {
                total += Vector3.Cross(Position(mesh, t, 1) - Position(mesh, t, 0), Position(mesh, t, 2) - Position(mesh, t, 0)).Length();
                cumulative[t] = total;
            }
            if (!(total > 0)) return sites;

            for (int attempt = 0; sites.Count < count && attempt < count * 8; attempt++)
            {
                int t = Array.BinarySearch(cumulative, random.NextDouble() * total);
                if (t < 0) t = ~t;
                t = Math.Min(t, triangles - 1);
                Vector3 a = Position(mesh, t, 0), b = Position(mesh, t, 1), c = Position(mesh, t, 2);
                float r1 = MathF.Sqrt((float)random.NextDouble()), r2 = (float)random.NextDouble();
                Vector3 point = a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2);
                Vector3 normal = Vector3.Cross(b - a, c - a) * winding;
                if (normal.LengthSquared() < 1e-20f) continue;
                normal.Normalize();
                point -= normal * depth * (0.2f + 0.8f * (float)random.NextDouble());
                bool near = false;
                foreach (Vector3 site in sites)
                    if (Vector3.DistanceSquared(site, point) < depth * depth * 0.01f + 1e-8f) { near = true; break; }
                if (!near) sites.Add(point);
            }
            return sites;
        }

        /// <summary>
        /// The Voronoi pieces of <paramref name="mesh"/> around <paramref name="sites"/>, each closed with caps on
        /// its cut faces (texture coordinates = cap position in metres x <paramref name="interiorTiling"/>). Sites
        /// whose cell misses the mesh give no piece. One site, or none, returns the mesh whole.
        /// </summary>
        public static List<FracturePiece> Fracture(FractureMesh mesh, IReadOnlyList<Vector3> sites, float interiorTiling = 1f)
        {
            var pieces = new List<FracturePiece>();
            float volume = SignedVolume(mesh);
            float winding = volume < 0 ? -1 : 1;
            if (sites == null || sites.Count < 2)
            {
                pieces.Add(new FracturePiece { Mesh = mesh, Center = Centroid(mesh), Volume = Math.Abs(volume) });
                return pieces;
            }

            var order = new List<int>(sites.Count);
            for (int i = 0; i < sites.Count; i++)
            {
                Vector3 site = sites[i];
                // Nearest neighbours first: their planes cut the most, so later clips have less to do.
                order.Clear();
                for (int j = 0; j < sites.Count; j++)
                    if (j != i) order.Add(j);
                order.Sort((p, q) => Vector3.DistanceSquared(site, sites[p]).CompareTo(Vector3.DistanceSquared(site, sites[q])));

                FractureMesh piece = mesh;
                foreach (int j in order)
                {
                    Vector3 normal = sites[j] - site;
                    float length = normal.Length();
                    if (length < 1e-6f) continue;
                    normal /= length;
                    piece = Clip(piece, normal, Vector3.Dot(normal, (site + sites[j]) * 0.5f), winding, interiorTiling);
                    if (piece == null) break;
                }
                if (piece == null || piece.TriangleCount == 0) continue;
                float pieceVolume = SignedVolume(piece) * winding;
                if (!(pieceVolume > 1e-12f)) continue;
                pieces.Add(new FracturePiece { Mesh = piece, Center = Centroid(piece), Volume = pieceVolume });
            }
            return pieces;
        }

        /// <summary>
        /// The part of a closed mesh where p · <paramref name="normal"/> ≤ <paramref name="distance"/>, closed with a
        /// cap on the plane. Returns the same mesh when nothing is cut away and null when nothing is left.
        /// <paramref name="winding"/> is the sign of the mesh's <see cref="SignedVolume"/>.
        /// </summary>
        public static FractureMesh Clip(FractureMesh mesh, Vector3 normal, float distance, float winding, float interiorTiling = 1f)
        {
            int vertexCount = mesh.Vertices.Count;
            var side = new float[vertexCount];
            bool anyOut = false, anyIn = false;
            for (int i = 0; i < vertexCount; i++)
            {
                float s = Vector3.Dot(normal, mesh.Vertices[i].Position) - distance;
                if (Math.Abs(s) < Epsilon) s = 0;
                side[i] = s;
                if (s > 0) anyOut = true;
                else anyIn = true;
            }
            if (!anyOut) return mesh;
            if (!anyIn) return null;

            var result = new FractureMesh();
            var map = new int[vertexCount];
            Array.Fill(map, -1);
            var cuts = new Dictionary<long, int>();
            var segments = new List<(Vector3 A, Vector3 B)>();
            var polygon = new List<int>(4);
            var cutPoints = new List<Vector3>(2);

            int Keep(int i)
            {
                if (map[i] < 0)
                {
                    map[i] = result.Vertices.Count;
                    result.Vertices.Add(mesh.Vertices[i]);
                }
                return map[i];
            }

            // The point where the edge from a kept vertex to a dropped one crosses the plane, shared by both triangles.
            int Cut(int kept, int dropped)
            {
                long key = ((long)kept << 32) | (uint)dropped;
                if (cuts.TryGetValue(key, out int index)) return index;
                float t = side[kept] / (side[kept] - side[dropped]);
                FractureVertex a = mesh.Vertices[kept], b = mesh.Vertices[dropped];
                var v = new FractureVertex
                {
                    Position = a.Position + (b.Position - a.Position) * t,
                    Normal = SafeNormalize(a.Normal + (b.Normal - a.Normal) * t, a.Normal),
                    Tangent = SafeNormalize(a.Tangent + (b.Tangent - a.Tangent) * t, a.Tangent),
                    TextureCoordinate = a.TextureCoordinate + (b.TextureCoordinate - a.TextureCoordinate) * t,
                };
                index = result.Vertices.Count;
                result.Vertices.Add(v);
                cuts[key] = index;
                return index;
            }

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
                int group = mesh.Groups[t];
                int outside = (side[a] > 0 ? 1 : 0) + (side[b] > 0 ? 1 : 0) + (side[c] > 0 ? 1 : 0);
                if (outside == 3) continue;
                if (outside == 0)
                {
                    result.AddTriangle(Keep(a), Keep(b), Keep(c), group);
                    continue;
                }

                polygon.Clear();
                cutPoints.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int i = k == 0 ? a : k == 1 ? b : c;
                    int j = k == 0 ? b : k == 1 ? c : a;
                    bool keepI = side[i] <= 0, keepJ = side[j] <= 0;
                    if (keepI) polygon.Add(Keep(i));
                    if (keepI == keepJ) continue;
                    int cut = keepI ? Cut(i, j) : Cut(j, i);
                    polygon.Add(cut);
                    cutPoints.Add(result.Vertices[cut].Position);
                }
                for (int m = 1; m + 1 < polygon.Count; m++)
                {
                    Vector3 p0 = result.Vertices[polygon[0]].Position;
                    Vector3 cross = Vector3.Cross(result.Vertices[polygon[m]].Position - p0, result.Vertices[polygon[m + 1]].Position - p0);
                    if (cross.LengthSquared() > 1e-24f) result.AddTriangle(polygon[0], polygon[m], polygon[m + 1], group);
                }
                if (cutPoints.Count == 2 && Vector3.DistanceSquared(cutPoints[0], cutPoints[1]) > 1e-16f)
                    segments.Add((cutPoints[0], cutPoints[1]));
            }

            AddCap(result, segments, normal, winding, interiorTiling);
            return result.TriangleCount > 0 ? result : null;
        }

        /// <summary>
        /// Closes a cut: traces the cut segments into loops on the plane, sorts them into outlines and holes by how
        /// deeply they nest, and triangulates each outline with its holes (ear clipping, holes bridged in).
        /// </summary>
        private static void AddCap(FractureMesh mesh, List<(Vector3 A, Vector3 B)> segments, Vector3 normal, float winding, float tiling)
        {
            if (segments.Count < 3) return;
            Vector3 u = Perpendicular(normal);
            Vector3 v = Vector3.Cross(normal, u);

            // Weld the segment ends into points.
            var points = new List<Vector3>();
            var ids = new Dictionary<(long, long, long), int>();
            int PointId(Vector3 p)
            {
                var key = ((long)MathF.Round(p.X / WeldGrid), (long)MathF.Round(p.Y / WeldGrid), (long)MathF.Round(p.Z / WeldGrid));
                if (ids.TryGetValue(key, out int id)) return id;
                id = points.Count;
                points.Add(p);
                ids[key] = id;
                return id;
            }
            var edges = new List<(int A, int B)>();
            var seen = new HashSet<(int, int)>();
            foreach (var (a, b) in segments)
            {
                int ia = PointId(a), ib = PointId(b);
                if (ia == ib || !seen.Add((Math.Min(ia, ib), Math.Max(ia, ib)))) continue;
                edges.Add((ia, ib));
            }
            var adjacent = new List<int>[points.Count];
            for (int e = 0; e < edges.Count; e++)
            {
                (adjacent[edges[e].A] ??= new List<int>(2)).Add(e);
                (adjacent[edges[e].B] ??= new List<int>(2)).Add(e);
            }

            // Trace closed loops; open chains (a mesh that isn't watertight) get no cap.
            var used = new bool[edges.Count];
            var loops = new List<List<int>>();
            for (int start = 0; start < edges.Count; start++)
            {
                if (used[start]) continue;
                used[start] = true;
                var loop = new List<int> { edges[start].A };
                int first = edges[start].A, current = edges[start].B;
                bool closed = false;
                for (int guard = 0; guard <= edges.Count; guard++)
                {
                    if (current == first)
                    {
                        closed = true;
                        break;
                    }
                    loop.Add(current);
                    int next = -1;
                    foreach (int e in adjacent[current])
                        if (!used[e]) { next = e; break; }
                    if (next < 0) break;
                    used[next] = true;
                    current = edges[next].A == current ? edges[next].B : edges[next].A;
                }
                if (closed && loop.Count >= 3) loops.Add(loop);
            }
            if (loops.Count == 0) return;

            var flat = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++) flat[i] = new Vector2(Vector3.Dot(points[i], u), Vector3.Dot(points[i], v));

            // Nesting depth: even = an outline, odd = a hole in the outline around it.
            var areas = new float[loops.Count];
            for (int i = 0; i < loops.Count; i++) areas[i] = Area(flat, loops[i]);
            var depth = new int[loops.Count];
            for (int i = 0; i < loops.Count; i++)
            {
                Vector2 probe = flat[loops[i][0]];
                for (int j = 0; j < loops.Count; j++)
                    if (j != i && Math.Abs(areas[j]) > Math.Abs(areas[i]) && Inside(flat, loops[j], probe)) depth[i]++;
            }
            var holes = new List<int>[loops.Count];
            for (int i = 0; i < loops.Count; i++)
            {
                if (depth[i] % 2 == 0 || Math.Abs(areas[i]) < 1e-14f) continue;
                // The smallest outline around the hole.
                int parent = -1;
                Vector2 probe = flat[loops[i][0]];
                for (int j = 0; j < loops.Count; j++)
                    if (depth[j] == depth[i] - 1 && Inside(flat, loops[j], probe) && (parent < 0 || Math.Abs(areas[j]) < Math.Abs(areas[parent])))
                        parent = j;
                if (parent >= 0) (holes[parent] ??= new List<int>()).Add(i);
            }

            var triangles = new List<int>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (depth[i] % 2 != 0 || Math.Abs(areas[i]) < 1e-14f) continue;
                List<int> outline = new List<int>(loops[i]);
                if (areas[i] < 0) outline.Reverse();
                if (holes[i] != null)
                {
                    // Bridge the holes in from the rightmost one, each hole clockwise.
                    var holeLoops = new List<List<int>>();
                    foreach (int h in holes[i])
                    {
                        var hole = new List<int>(loops[h]);
                        if (areas[h] > 0) hole.Reverse();
                        holeLoops.Add(hole);
                    }
                    holeLoops.Sort((p, q) => MaxX(flat, q).CompareTo(MaxX(flat, p)));
                    foreach (List<int> hole in holeLoops) outline = BridgeHole(flat, outline, hole);
                }
                EarClip(flat, outline, triangles);
            }
            if (triangles.Count == 0) return;

            // Cap vertices: flat, facing out of the piece (+normal), texture coordinates from the plane.
            var capIndex = new Dictionary<int, int>();
            int CapVertex(int point)
            {
                if (capIndex.TryGetValue(point, out int index)) return index;
                index = mesh.Vertices.Count;
                mesh.Vertices.Add(new FractureVertex
                {
                    Position = points[point], Normal = normal, Tangent = u, TextureCoordinate = flat[point] * tiling,
                });
                capIndex[point] = index;
                return index;
            }
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (cross.LengthSquared() < 1e-24f) continue;
                // Same winding as the rest of the mesh: counter-clockwise seen from outside when the volume is positive.
                if (Vector3.Dot(cross, normal) * winding < 0) (b, c) = (c, b);
                mesh.AddTriangle(CapVertex(a), CapVertex(b), CapVertex(c), InteriorGroup);
            }
        }

        /// <summary>
        /// Joins a clockwise hole into a counter-clockwise outline (Eberly's bridge): from the hole's rightmost point
        /// a ray along +X finds the outline edge it hits, and the bridge goes to the visible vertex of that edge (or
        /// to the reflex vertex inside the triangle that blocks it with the smallest angle to the ray).
        /// </summary>
        private static List<int> BridgeHole(Vector2[] flat, List<int> outline, List<int> hole)
        {
            int m = 0;
            for (int i = 1; i < hole.Count; i++)
                if (flat[hole[i]].X > flat[hole[m]].X) m = i;
            Vector2 mp = flat[hole[m]];

            int best = -1;
            float bestX = float.MaxValue;
            Vector2 hit = Vector2.Zero;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = flat[outline[i]], b = flat[outline[(i + 1) % outline.Count]];
                if ((a.Y > mp.Y) == (b.Y > mp.Y) && a.Y != mp.Y && b.Y != mp.Y) continue;
                if (Math.Abs(b.Y - a.Y) < 1e-12f)
                {
                    float x = Math.Min(a.X, b.X);
                    if (x >= mp.X && x < bestX && Math.Abs(a.Y - mp.Y) < 1e-9f)
                    {
                        bestX = x;
                        best = a.X < b.X ? i : (i + 1) % outline.Count;
                        hit = new Vector2(x, mp.Y);
                    }
                    continue;
                }
                float t = (mp.Y - a.Y) / (b.Y - a.Y);
                if (t < 0 || t > 1) continue;
                float xi = a.X + (b.X - a.X) * t;
                if (xi < mp.X || xi >= bestX) continue;
                bestX = xi;
                hit = new Vector2(xi, mp.Y);
                // The edge's end further along +X is the candidate.
                best = a.X > b.X ? i : (i + 1) % outline.Count;
            }
            if (best < 0) return outline;

            Vector2 pp = flat[outline[best]];
            if (Vector2.DistanceSquared(pp, hit) > 1e-18f)
            {
                // A reflex vertex inside (M, hit, P) would block the bridge: take the one nearest the ray's direction.
                float bestAngle = float.MaxValue;
                int count = outline.Count;
                for (int i = 0; i < count; i++)
                {
                    if (i == best) continue;
                    Vector2 q = flat[outline[i]];
                    Vector2 prev = flat[outline[(i - 1 + count) % count]], next = flat[outline[(i + 1) % count]];
                    if (Cross(q - prev, next - q) > 0) continue; // convex
                    if (!InTriangle(mp, hit, pp, q)) continue;
                    Vector2 d = q - mp;
                    float angle = MathF.Abs(MathF.Atan2(d.Y, d.X));
                    if (angle < bestAngle - 1e-6f || (Math.Abs(angle - bestAngle) <= 1e-6f && d.LengthSquared() < (flat[outline[best]] - mp).LengthSquared()))
                    {
                        bestAngle = angle;
                        best = i;
                    }
                }
            }

            var merged = new List<int>(outline.Count + hole.Count + 2);
            for (int i = 0; i <= best; i++) merged.Add(outline[i]);
            for (int i = 0; i <= hole.Count; i++) merged.Add(hole[(m + i) % hole.Count]);
            merged.Add(outline[best]);
            for (int i = best + 1; i < outline.Count; i++) merged.Add(outline[i]);
            return merged;
        }

        /// <summary>Ear clipping of a counter-clockwise polygon (bridged holes allowed); triangles appended as point ids.</summary>
        private static void EarClip(Vector2[] flat, List<int> polygon, List<int> triangles)
        {
            var remaining = new List<int>(polygon);
            int guard = remaining.Count * remaining.Count + 16;
            while (remaining.Count > 3 && guard-- > 0)
            {
                int n = remaining.Count;
                bool clipped = false;
                for (int i = 0; i < n; i++)
                {
                    int ip = remaining[(i - 1 + n) % n], ic = remaining[i], inx = remaining[(i + 1) % n];
                    Vector2 a = flat[ip], b = flat[ic], c = flat[inx];
                    if (Cross(b - a, c - b) <= 1e-14f) continue; // reflex or flat
                    bool ear = true;
                    for (int k = 0; k < n; k++)
                    {
                        int id = remaining[k];
                        if (id == ip || id == ic || id == inx) continue;
                        Vector2 p = flat[id];
                        if (p == a || p == b || p == c) continue; // a bridge's duplicate
                        if (InTriangle(a, b, c, p)) { ear = false; break; }
                    }
                    if (!ear) continue;
                    triangles.Add(ip);
                    triangles.Add(ic);
                    triangles.Add(inx);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (clipped) continue;

                // Stuck (a numerically twisted outline): drop a flat vertex, else force the most convex corner.
                int flatVertex = -1, convex = 0;
                float most = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = flat[remaining[(i - 1 + n) % n]], b = flat[remaining[i]], c = flat[remaining[(i + 1) % n]];
                    float cross = Cross(b - a, c - b);
                    if (Math.Abs(cross) <= 1e-14f) { flatVertex = i; break; }
                    if (cross > most) { most = cross; convex = i; }
                }
                if (flatVertex >= 0)
                {
                    remaining.RemoveAt(flatVertex);
                    continue;
                }
                triangles.Add(remaining[(convex - 1 + n) % n]);
                triangles.Add(remaining[convex]);
                triangles.Add(remaining[(convex + 1) % n]);
                remaining.RemoveAt(convex);
            }
            if (remaining.Count == 3 && Math.Abs(Cross(flat[remaining[1]] - flat[remaining[0]], flat[remaining[2]] - flat[remaining[0]])) > 1e-14f)
            {
                triangles.Add(remaining[0]);
                triangles.Add(remaining[1]);
                triangles.Add(remaining[2]);
            }
        }

        private static float Area(Vector2[] flat, List<int> loop)
        {
            float area = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = flat[loop[i]], b = flat[loop[(i + 1) % loop.Count]];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
        }

        // Even-odd point in polygon.
        private static bool Inside(Vector2[] flat, List<int> loop, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                Vector2 a = flat[loop[i]], b = flat[loop[j]];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static float MaxX(Vector2[] flat, List<int> loop)
        {
            float max = float.MinValue;
            foreach (int i in loop) max = Math.Max(max, flat[i].X);
            return max;
        }

        // Strictly inside a counter-clockwise triangle (points on its edges don't block an ear).
        private static bool InTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            if (Cross(b - a, c - a) < 0) (b, c) = (c, b);
            return Cross(b - a, p - a) > 1e-14f && Cross(c - b, p - b) > 1e-14f && Cross(a - c, p - c) > 1e-14f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static Vector3 Position(FractureMesh mesh, int triangle, int corner) => mesh.Vertices[mesh.Indices[triangle * 3 + corner]].Position;

        private static Vector3 Perpendicular(Vector3 n)
        {
            Vector3 axis = Math.Abs(n.X) < 0.6f ? Vector3.UnitX : Math.Abs(n.Y) < 0.6f ? Vector3.UnitY : Vector3.UnitZ;
            return Vector3.Normalize(Vector3.Cross(axis, n));
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float length = v.Length();
            return length > 1e-8f ? v / length : fallback;
        }
    }
}
