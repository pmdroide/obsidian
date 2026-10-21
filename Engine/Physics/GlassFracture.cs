using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>How a pane was hit; picks the fracture pattern (Half-Life: Alyx's <c>ShatterGlassStressType</c>).</summary>
    public enum GlassStress
    {
        /// <summary>A bullet: a small hole of fine shards, radial cracks around it.</summary>
        Ballistic,
        /// <summary>A thrown object or a melee hit: a wider hole the size of the impact.</summary>
        Blunt,
        /// <summary>A blast: the whole pane bursts away from the point.</summary>
        Explosive,
    }

    /// <summary>
    /// 2D fracture of a glass pane, in the pane's plane (metres, centred on the pane, X/Y = its two long sides).
    /// Every shard is a convex polygon, counter-clockwise. The first crack follows Half-Life: Alyx's
    /// <c>func_shatterglass</c> (server.dll, recovered in <c>Docs/Research/BreakableGlass/README.md</c>): Voronoi
    /// cells around sites on a jittered grid of <c>shatterglass_voronoi_size</c>, each cell the pane clipped by
    /// the bisectors to the other sites. Hits then split the shards they touch into radial pieces.
    /// Pure math, no GPU or physics (checked by Tests/Components).
    /// </summary>
    public static class GlassFracture
    {
        /// <summary>Valve's limit for a shard polygon (point-in-shard tests reject more).</summary>
        public const int MaxPolygonVertices = 128;
        /// <summary>Distance (m) under which two edges or points count as touching.</summary>
        public const float Epsilon = 1e-3f;

        /// <summary>The pane as one shard.</summary>
        public static Vector2[] Rectangle(Vector2 half) => new[]
        {
            new Vector2(-half.X, -half.Y), new Vector2(half.X, -half.Y), new Vector2(half.X, half.Y), new Vector2(-half.X, half.Y),
        };

        /// <summary>
        /// The first crack of an intact pane, exactly as Half-Life: Alyx seeds it: a site per grid cell of
        /// <paramref name="cellSize"/> (from -half to +half), moved a quarter cell and jittered by ±0.49 cells,
        /// kept when strictly inside 98 % of the pane; then the Voronoi cells of the sites inside the pane.
        /// A pane smaller than one cell along a side stays whole (Valve skips the crack too).
        /// </summary>
        public static List<Vector2[]> CrackPane(Vector2 half, float cellSize, Random random)
        {
            var pane = Rectangle(half);
            if (!(cellSize > 0) || cellSize >= half.X * 2 || cellSize >= half.Y * 2) return new List<Vector2[]> { pane };

            var sites = new List<Vector2>();
            float jitter = 0.49f * cellSize;
            for (float x = -half.X; x <= half.X; x += cellSize)
                for (float y = -half.Y; y <= half.Y; y += cellSize)
                {
                    var site = new Vector2(x + cellSize * 0.25f + Range(random, -jitter, jitter),
                        y + cellSize * 0.25f + Range(random, -jitter, jitter));
                    if (Math.Abs(site.X) < 0.98f * half.X && Math.Abs(site.Y) < 0.98f * half.Y) sites.Add(site);
                }
            return Voronoi(pane, sites);
        }

        /// <summary>
        /// Sites for the pieces around an impact: a jittered centre, then rings (0.35, 0.7, 1, 1.6 and 2.6 x
        /// <paramref name="radius"/>) on shared jittered spokes, so the pieces come out as wedges between radial
        /// cracks, fine in the hole and coarser outside it.
        /// </summary>
        public static List<Vector2> ImpactSites(Vector2 point, float radius, Random random)
        {
            var sites = new List<Vector2> { point + RandomInDisc(random, radius * 0.1f) };
            int spokes = 7 + random.Next(4);
            float turn = Range(random, 0, MathHelper.TwoPi);
            ReadOnlySpan<float> rings = stackalloc float[] { 0.35f, 0.7f, 1f, 1.6f, 2.6f };
            for (int s = 0; s < spokes; s++)
            {
                float angle = turn + MathHelper.TwoPi * (s + Range(random, -0.3f, 0.3f)) / spokes;
                foreach (float ring in rings)
                {
                    float r = radius * ring * Range(random, 0.85f, 1.15f);
                    float a = angle + Range(random, -0.12f, 0.12f);
                    sites.Add(point + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
                }
            }
            return sites;
        }

        /// <summary>
        /// The Voronoi cells of <paramref name="sites"/> clipped to the convex <paramref name="polygon"/> (sites
        /// outside it are dropped). One cell (the polygon) without at least two sites inside.
        /// </summary>
        public static List<Vector2[]> Voronoi(Vector2[] polygon, List<Vector2> sites)
        {
            var inside = new List<Vector2>();
            foreach (Vector2 site in sites)
                if (Contains(polygon, site) && !inside.Exists(s => Vector2.DistanceSquared(s, site) < Epsilon * Epsilon)) inside.Add(site);
            var cells = new List<Vector2[]>();
            if (inside.Count < 2)
            {
                cells.Add(polygon);
                return cells;
            }

            for (int i = 0; i < inside.Count; i++)
            {
                Vector2[] cell = polygon;
                for (int j = 0; j < inside.Count && cell.Length >= 3; j++)
                {
                    if (j == i) continue;
                    // Keep the side of the bisector nearer site i.
                    cell = Clip(cell, (inside[i] + inside[j]) * 0.5f, inside[j] - inside[i]);
                }
                if (cell.Length >= 3 && Area(cell) > Epsilon * Epsilon) cells.Add(cell);
            }
            return cells;
        }

        /// <summary>The part of a convex polygon where (p - <paramref name="origin"/>) · <paramref name="normal"/> ≤ 0.</summary>
        public static Vector2[] Clip(Vector2[] polygon, Vector2 origin, Vector2 normal)
        {
            var result = new List<Vector2>(polygon.Length + 1);
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                float da = Vector2.Dot(a - origin, normal), db = Vector2.Dot(b - origin, normal);
                if (da <= 0) result.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0)) result.Add(a + (b - a) * (da / (da - db)));
            }
            return result.ToArray();
        }

        public static float Area(Vector2[] polygon)
        {
            float area = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
        }

        /// <summary>
        /// Thick enough to be a physics body: area / perimeter (half the radius of the largest inscribed circle for
        /// a regular shape) at least <paramref name="minimum"/> metres. Slivers get no collider.
        /// </summary>
        public static bool IsSolid(Vector2[] polygon, float minimum)
        {
            if (polygon.Length < 3) return false;
            float perimeter = 0;
            for (int i = 0; i < polygon.Length; i++) perimeter += Vector2.Distance(polygon[i], polygon[(i + 1) % polygon.Length]);
            return perimeter > 0 && Math.Abs(Area(polygon)) / perimeter >= minimum;
        }

        public static Vector2 Centroid(Vector2[] polygon)
        {
            float area = 0;
            Vector2 sum = Vector2.Zero;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                float cross = a.X * b.Y - b.X * a.Y;
                area += cross;
                sum += (a + b) * cross;
            }
            if (Math.Abs(area) < 1e-12f)
            {
                foreach (Vector2 p in polygon) sum += p;
                return sum / Math.Max(polygon.Length, 1);
            }
            return sum / (3 * area);
        }

        /// <summary>Inside a convex counter-clockwise polygon (edges included).</summary>
        public static bool Contains(Vector2[] polygon, Vector2 point)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                if ((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X) < -1e-7f) return false;
            }
            return true;
        }

        /// <summary>
        /// Half-Life: Alyx's shard hit test: the polygon (3..128 points) scaled about its centroid by
        /// <paramref name="tolerance"/> (<c>shatterglass_hit_tolerance</c>, 2) must contain the point, so hits near
        /// a small shard still find it.
        /// </summary>
        public static bool HitTest(Vector2[] polygon, Vector2 point, float tolerance)
        {
            if (polygon.Length < 3 || polygon.Length > MaxPolygonVertices) return false;
            Vector2 centroid = Centroid(polygon);
            // Scaling the polygon by t about c contains p  <=>  the polygon contains c + (p - c) / t.
            return Contains(polygon, centroid + (point - centroid) / Math.Max(tolerance, 1e-3f));
        }

        /// <summary>Distance from a point to a convex polygon (0 inside).</summary>
        public static float Distance(Vector2[] polygon, Vector2 point)
        {
            if (Contains(polygon, point)) return 0;
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                Vector2 ab = b - a;
                float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0, 1) : 0;
                best = Math.Min(best, Vector2.Distance(point, a + ab * t));
            }
            return best;
        }

        /// <summary>
        /// Which shards still hang in the frame (Valve's <c>GlassInFrame</c>): those with an edge on the pane's
        /// border, and every shard joined to one through shared edges. The rest fall.
        /// </summary>
        public static bool[] Supported(IReadOnlyList<Vector2[]> shards, Vector2 half)
        {
            int count = shards.Count;
            var supported = new bool[count];
            var queue = new Queue<int>();
            var min = new Vector2[count];
            var max = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Bounds(shards[i], out min[i], out max[i]);
                if (!OnBorder(shards[i], half)) continue;
                supported[i] = true;
                queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < count; j++)
                {
                    if (supported[j]) continue;
                    if (min[j].X > max[i].X + Epsilon || max[j].X < min[i].X - Epsilon
                        || min[j].Y > max[i].Y + Epsilon || max[j].Y < min[i].Y - Epsilon) continue;
                    if (!ShareEdge(shards[i], shards[j])) continue;
                    supported[j] = true;
                    queue.Enqueue(j);
                }
            }
            return supported;
        }

        /// <summary>An edge of the polygon lies on a side of the pane.</summary>
        public static bool OnBorder(Vector2[] polygon, Vector2 half)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                if (Vector2.DistanceSquared(a, b) < Epsilon * Epsilon) continue;
                if ((Near(a.X, -half.X) && Near(b.X, -half.X)) || (Near(a.X, half.X) && Near(b.X, half.X))
                    || (Near(a.Y, -half.Y) && Near(b.Y, -half.Y)) || (Near(a.Y, half.Y) && Near(b.Y, half.Y))) return true;
            }
            return false;
        }

        /// <summary>The two polygons touch along a segment longer than <see cref="Epsilon"/> (not just a corner).</summary>
        public static bool ShareEdge(Vector2[] a, Vector2[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                Vector2 a0 = a[i], a1 = a[(i + 1) % a.Length];
                Vector2 dir = a1 - a0;
                float length = dir.Length();
                if (length < Epsilon) continue;
                dir /= length;
                for (int j = 0; j < b.Length; j++)
                {
                    Vector2 b0 = b[j], b1 = b[(j + 1) % b.Length];
                    if (Math.Abs(Cross(dir, b0 - a0)) > Epsilon || Math.Abs(Cross(dir, b1 - a0)) > Epsilon) continue;
                    float t0 = Vector2.Dot(b0 - a0, dir), t1 = Vector2.Dot(b1 - a0, dir);
                    float overlap = Math.Min(length, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1));
                    if (overlap > Epsilon) return true;
                }
            }
            return false;
        }

        public static void Bounds(Vector2[] polygon, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue);
            max = new Vector2(float.MinValue);
            foreach (Vector2 p in polygon)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        /// <summary>The polygon moved towards its centroid by <paramref name="inset"/> metres (colliders keep a gap).</summary>
        public static Vector2[] Shrink(Vector2[] polygon, float inset)
        {
            Vector2 centroid = Centroid(polygon);
            var result = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 offset = polygon[i] - centroid;
                float length = offset.Length();
                result[i] = length > inset * 2 ? centroid + offset * ((length - inset) / length) : centroid + offset * 0.5f;
            }
            return result;
        }

        public static float Range(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static Vector2 RandomInDisc(Random random, float radius)
        {
            float a = Range(random, 0, MathHelper.TwoPi), r = radius * MathF.Sqrt((float)random.NextDouble());
            return new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static bool Near(float a, float b) => Math.Abs(a - b) < Epsilon;
    }
}
