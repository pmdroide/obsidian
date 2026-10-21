using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Lightmap UVs (UV2) of one model, built on the CPU at bake time (Source 2 look, phase 4.2).
    ///
    /// Models without an authored second UV set are cut into charts: triangles are grouped by the axis their normal
    /// points along most (±X/±Y/±Z), each connected group is projected flat onto that axis' plane, and a group that
    /// would overlap itself in that projection (a ramp winding over itself) is split until it doesn't. Vertices are
    /// split where charts meet, so each lightmap vertex belongs to exactly one chart. Chart coordinates are in model
    /// units; <see cref="LightmapBaker"/> scales them by each instance's Scale and texel density and packs the charts
    /// into the lightmap pages.
    ///
    /// A model whose vertices carry TEXCOORD1 keeps that layout as a single chart ("authored").
    ///
    /// Pure CPU, deterministic, thread-safe (no shared state).
    /// </summary>
    public sealed class LightmapUnwrap
    {
        /// <summary>One mesh part of the source model, as the bake gathered it (model space).</summary>
        public sealed class SourcePart
        {
            public Vector3[] Positions;
            public Vector3[] Normals;   //may be null
            public Vector2[] Uv2;       //authored lightmap UVs (TEXCOORD1), may be null
            public int[] Indices;
            //False for parts that are not drawn into the G-buffer (glass, water): no lightmap
            public bool Lightmapped = true;
        }

        public sealed class Part
        {
            //Vertex count of the model part these were built from, compared against the live model at runtime
            public int SourceVertexCount;
            public bool Lightmapped;
            //Per lightmap vertex: model-space position, the source vertex it was split from, chart plane coordinate, chart
            public Vector3[] Positions = Array.Empty<Vector3>();
            public int[] SourceVertex = Array.Empty<int>();
            public Vector2[] Coords = Array.Empty<Vector2>();
            public int[] Chart = Array.Empty<int>();
            //Triangles over the lightmap vertices
            public int[] Indices = Array.Empty<int>();
        }

        public sealed class ChartInfo
        {
            //Chart plane coordinates (model units; 0..1 for an authored layout)
            public Vector2 Min, Max;
            //Model axis the chart is projected along (0 = X, 1 = Y, 2 = Z), -1 = authored UV2
            public int Axis;
            //Model-space surface area, for authored layouts (sized by area)
            public float Area;
        }

        public readonly List<Part> Parts = new List<Part>();
        public readonly List<ChartInfo> Charts = new List<ChartInfo>();
        public bool Authored;

        //Overlap test grid per chart (cells per side at most)
        private const int OverlapGrid = 96;

        /// <summary>Chart plane axes (model axes) for a projection axis: X → (Y, Z), Y → (X, Z), Z → (X, Y).</summary>
        public static void PlaneAxes(int axis, out int u, out int v)
        {
            u = axis == 0 ? 1 : 0;
            v = axis == 2 ? 1 : 2;
        }

        /// <summary>World size of one model unit along a chart's plane axes, for an instance scaled by <paramref name="scale"/>.</summary>
        public static Vector2 PlaneScale(int axis, Vector3 scale)
        {
            if (axis < 0) return Vector2.One;
            PlaneAxes(axis, out int u, out int v);
            return new Vector2(Math.Abs(Component(scale, u)), Math.Abs(Component(scale, v)));
        }

        public static LightmapUnwrap Build(IReadOnlyList<SourcePart> parts)
        {
            bool authored = parts.Count > 0;
            foreach (SourcePart p in parts)
                if (p.Lightmapped && p.Uv2 == null) authored = false;
            return authored ? BuildAuthored(parts) : BuildCharts(parts);
        }

        // ---------------- Authored UV2 ----------------

        private static LightmapUnwrap BuildAuthored(IReadOnlyList<SourcePart> parts)
        {
            var result = new LightmapUnwrap { Authored = true };
            var chart = new ChartInfo { Axis = -1, Min = new Vector2(float.MaxValue), Max = new Vector2(float.MinValue) };
            result.Charts.Add(chart);
            foreach (SourcePart source in parts)
            {
                var part = new Part { SourceVertexCount = source.Positions.Length, Lightmapped = source.Lightmapped };
                result.Parts.Add(part);
                if (!source.Lightmapped) continue;
                int n = source.Positions.Length;
                part.Positions = (Vector3[])source.Positions.Clone();
                part.SourceVertex = new int[n];
                part.Coords = new Vector2[n];
                part.Chart = new int[n];
                for (int i = 0; i < n; i++)
                {
                    part.SourceVertex[i] = i;
                    part.Coords[i] = source.Uv2[i];
                    chart.Min = Vector2.Min(chart.Min, source.Uv2[i]);
                    chart.Max = Vector2.Max(chart.Max, source.Uv2[i]);
                }
                part.Indices = (int[])source.Indices.Clone();
                for (int t = 0; t + 2 < part.Indices.Length; t += 3)
                    chart.Area += TriangleArea(part.Positions[part.Indices[t]], part.Positions[part.Indices[t + 1]], part.Positions[part.Indices[t + 2]]);
            }
            if (chart.Min.X > chart.Max.X) { chart.Min = Vector2.Zero; chart.Max = Vector2.One; }
            return result;
        }

        // ---------------- Automatic charts ----------------

        private struct Triangle
        {
            public int Part, First; //first index of the triangle in the part's index list
            public int W0, W1, W2;  //welded corners
            public int Class;       //axis * 2 + (negative ? 1 : 0)
        }

        private static LightmapUnwrap BuildCharts(IReadOnlyList<SourcePart> parts)
        {
            var result = new LightmapUnwrap();

            //Weld positions across parts (model splits vertices at UV and normal seams; charts shouldn't)
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (SourcePart p in parts)
                if (p.Lightmapped)
                    foreach (Vector3 v in p.Positions) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            float weldSize = min.X <= max.X ? Math.Max(Vector3.Distance(min, max) * 1e-5f, 1e-6f) : 1e-6f;

            var welded = new Dictionary<(long, long, long), int>();
            var weldOf = new int[parts.Count][];
            for (int pi = 0; pi < parts.Count; pi++)
            {
                SourcePart p = parts[pi];
                if (!p.Lightmapped) continue;
                weldOf[pi] = new int[p.Positions.Length];
                for (int i = 0; i < p.Positions.Length; i++)
                {
                    Vector3 v = p.Positions[i];
                    var key = ((long)Math.Round(v.X / weldSize), (long)Math.Round(v.Y / weldSize), (long)Math.Round(v.Z / weldSize));
                    if (!welded.TryGetValue(key, out int w)) { w = welded.Count; welded[key] = w; }
                    weldOf[pi][i] = w;
                }
            }

            //Triangles, classified by their dominant normal axis
            var triangles = new List<Triangle>();
            for (int pi = 0; pi < parts.Count; pi++)
            {
                SourcePart p = parts[pi];
                if (!p.Lightmapped) continue;
                for (int t = 0; t + 2 < p.Indices.Length; t += 3)
                {
                    int i0 = p.Indices[t], i1 = p.Indices[t + 1], i2 = p.Indices[t + 2];
                    Vector3 n = Vector3.Cross(p.Positions[i1] - p.Positions[i0], p.Positions[i2] - p.Positions[i0]);
                    if (n.LengthSquared() < 1e-24f) continue; //degenerate: never drawn visibly
                    if (p.Normals != null)
                    {
                        //Winding conventions vary between exporters; trust the authored normals for the side
                        Vector3 vn = p.Normals[i0] + p.Normals[i1] + p.Normals[i2];
                        if (Vector3.Dot(vn, n) < 0) n = -n;
                    }
                    Vector3 a = new Vector3(Math.Abs(n.X), Math.Abs(n.Y), Math.Abs(n.Z));
                    int axis = a.X >= a.Y && a.X >= a.Z ? 0 : a.Y >= a.Z ? 1 : 2;
                    triangles.Add(new Triangle
                    {
                        Part = pi, First = t,
                        W0 = weldOf[pi][i0], W1 = weldOf[pi][i1], W2 = weldOf[pi][i2],
                        Class = axis * 2 + (Component(n, axis) < 0 ? 1 : 0),
                    });
                }
            }

            //Edge adjacency over welded corners
            var edges = new Dictionary<long, List<int>>();
            for (int t = 0; t < triangles.Count; t++)
            {
                Triangle tri = triangles[t];
                AddEdge(edges, tri.W0, tri.W1, t);
                AddEdge(edges, tri.W1, tri.W2, t);
                AddEdge(edges, tri.W2, tri.W0, t);
            }

            //Flood fill charts: connected triangles of the same class, split where the projection overlaps itself
            var chartOf = new int[triangles.Count];
            Array.Fill(chartOf, -1);
            var pending = new bool[triangles.Count];
            var group = new List<int>();
            var queue = new Queue<int>();
            for (int seed = 0; seed < triangles.Count; seed++)
            {
                if (chartOf[seed] >= 0 || pending[seed]) continue;
                //Connected component of this class
                group.Clear();
                pending[seed] = true;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int t = queue.Dequeue();
                    group.Add(t);
                    Triangle tri = triangles[t];
                    Visit(edges, triangles, tri.W0, tri.W1, tri.Class, pending, chartOf, queue);
                    Visit(edges, triangles, tri.W1, tri.W2, tri.Class, pending, chartOf, queue);
                    Visit(edges, triangles, tri.W2, tri.W0, tri.Class, pending, chartOf, queue);
                }
                SplitIntoCharts(result, parts, triangles, group, chartOf, edges);
                foreach (int t in group) pending[t] = false;
            }

            //Split vertices per chart and write the parts
            for (int pi = 0; pi < parts.Count; pi++)
                result.Parts.Add(new Part { SourceVertexCount = parts[pi].Positions.Length, Lightmapped = parts[pi].Lightmapped });

            var vertexLists = new List<Vector3>[parts.Count];
            var sourceLists = new List<int>[parts.Count];
            var coordLists = new List<Vector2>[parts.Count];
            var chartLists = new List<int>[parts.Count];
            var indexLists = new List<int>[parts.Count];
            var splitMaps = new Dictionary<long, int>[parts.Count];
            for (int pi = 0; pi < parts.Count; pi++)
            {
                vertexLists[pi] = new List<Vector3>(); sourceLists[pi] = new List<int>(); coordLists[pi] = new List<Vector2>();
                chartLists[pi] = new List<int>(); indexLists[pi] = new List<int>(); splitMaps[pi] = new Dictionary<long, int>();
            }

            for (int t = 0; t < triangles.Count; t++)
            {
                Triangle tri = triangles[t];
                int chart = chartOf[t];
                if (chart < 0) continue;
                SourcePart p = parts[tri.Part];
                int axis = result.Charts[chart].Axis;
                for (int corner = 0; corner < 3; corner++)
                {
                    int source = p.Indices[tri.First + corner];
                    long key = ((long)chart << 32) | (uint)source;
                    if (!splitMaps[tri.Part].TryGetValue(key, out int index))
                    {
                        index = vertexLists[tri.Part].Count;
                        splitMaps[tri.Part][key] = index;
                        vertexLists[tri.Part].Add(p.Positions[source]);
                        sourceLists[tri.Part].Add(source);
                        coordLists[tri.Part].Add(Project(p.Positions[source], axis));
                        chartLists[tri.Part].Add(chart);
                    }
                    indexLists[tri.Part].Add(index);
                }
            }

            for (int pi = 0; pi < parts.Count; pi++)
            {
                Part part = result.Parts[pi];
                if (!part.Lightmapped) continue;
                part.Positions = vertexLists[pi].ToArray();
                part.SourceVertex = sourceLists[pi].ToArray();
                part.Coords = coordLists[pi].ToArray();
                part.Chart = chartLists[pi].ToArray();
                part.Indices = indexLists[pi].ToArray();
            }
            return result;
        }

        /// <summary>
        /// Turns one connected group of same-axis triangles into charts. Triangles are taken in flood order and laid onto
        /// a coarse grid of the projection; a triangle that lands on cells another one already holds would overlap it in
        /// the lightmap, so it waits for the next chart. Repeats with what is left until every triangle has a chart.
        /// </summary>
        private static void SplitIntoCharts(LightmapUnwrap result, IReadOnlyList<SourcePart> parts, List<Triangle> triangles,
            List<int> group, int[] chartOf, Dictionary<long, List<int>> edges)
        {
            int axis = triangles[group[0]].Class / 2;
            var remaining = new List<int>(group);
            var inGroup = new HashSet<int>(group);
            var accepted = new List<int>();
            var rejected = new List<int>();
            var order = new List<int>();
            var queue = new Queue<int>();
            var visited = new HashSet<int>();

            while (remaining.Count > 0)
            {
                //Flood order from the first remaining triangle, so each chart grows outwards and stays connected where it can
                order.Clear();
                visited.Clear();
                var remainingSet = new HashSet<int>(remaining);
                foreach (int start in remaining)
                {
                    if (visited.Contains(start)) continue;
                    visited.Add(start);
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        int t = queue.Dequeue();
                        order.Add(t);
                        Triangle tri = triangles[t];
                        foreach (long key in new[] { EdgeKey(tri.W0, tri.W1), EdgeKey(tri.W1, tri.W2), EdgeKey(tri.W2, tri.W0) })
                            foreach (int n in edges[key])
                                if (remainingSet.Contains(n) && visited.Add(n)) queue.Enqueue(n);
                    }
                }

                //Projected bounds of what is left
                Vector2 bMin = new Vector2(float.MaxValue), bMax = new Vector2(float.MinValue);
                foreach (int t in order)
                    for (int c = 0; c < 3; c++)
                    {
                        Vector2 q = Project(Corner(parts, triangles[t], c), axis);
                        bMin = Vector2.Min(bMin, q); bMax = Vector2.Max(bMax, q);
                    }
                Vector2 extent = Vector2.Max(bMax - bMin, new Vector2(1e-6f));
                float cell = Math.Max(extent.X, extent.Y) / OverlapGrid;
                int gw = Math.Max(1, (int)Math.Ceiling(extent.X / cell) + 1), gh = Math.Max(1, (int)Math.Ceiling(extent.Y / cell) + 1);
                var owner = new int[gw * gh];
                Array.Fill(owner, -1);

                accepted.Clear();
                rejected.Clear();
                foreach (int t in order)
                {
                    Vector2 a = (Project(Corner(parts, triangles[t], 0), axis) - bMin) / cell;
                    Vector2 b = (Project(Corner(parts, triangles[t], 1), axis) - bMin) / cell;
                    Vector2 c = (Project(Corner(parts, triangles[t], 2), axis) - bMin) / cell;
                    if (CellsFree(owner, gw, gh, a, b, c))
                    {
                        Claim(owner, gw, gh, a, b, c, t);
                        accepted.Add(t);
                    }
                    else rejected.Add(t);
                }
                //Grid empty at the start, so the first triangle is always accepted: this terminates
                if (accepted.Count == 0) { accepted.Add(rejected[0]); rejected.RemoveAt(0); }

                int chart = result.Charts.Count;
                var info = new ChartInfo { Axis = axis, Min = new Vector2(float.MaxValue), Max = new Vector2(float.MinValue) };
                foreach (int t in accepted)
                {
                    chartOf[t] = chart;
                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 corner = Corner(parts, triangles[t], c);
                        Vector2 q = Project(corner, axis);
                        info.Min = Vector2.Min(info.Min, q); info.Max = Vector2.Max(info.Max, q);
                    }
                    info.Area += TriangleArea(Corner(parts, triangles[t], 0), Corner(parts, triangles[t], 1), Corner(parts, triangles[t], 2));
                }
                result.Charts.Add(info);

                remaining.Clear();
                remaining.AddRange(rejected);
            }
        }

        //True when no grid cell whose centre lies inside the triangle is taken yet
        private static bool CellsFree(int[] owner, int gw, int gh, Vector2 a, Vector2 b, Vector2 c)
        {
            bool free = true;
            ForCellsInside(gw, gh, a, b, c, i => { if (owner[i] >= 0) free = false; });
            return free;
        }

        private static void Claim(int[] owner, int gw, int gh, Vector2 a, Vector2 b, Vector2 c, int t) =>
            ForCellsInside(gw, gh, a, b, c, i => owner[i] = t);

        //Cells (grid units, centre at i + 0.5) strictly inside the triangle; shared edges belong to neither side
        private static void ForCellsInside(int gw, int gh, Vector2 a, Vector2 b, Vector2 c, Action<int> action)
        {
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(gw - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(gh - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            float area = Cross(b - a, c - a);
            if (Math.Abs(area) < 1e-12f) return;
            float sign = Math.Sign(area);
            const float inset = 1e-3f;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Cross(b - a, p - a) * sign, w1 = Cross(c - b, p - b) * sign, w2 = Cross(a - c, p - c) * sign;
                if (w0 > inset && w1 > inset && w2 > inset) action(y * gw + x);
            }
        }

        private static void Visit(Dictionary<long, List<int>> edges, List<Triangle> triangles, int a, int b, int cls,
            bool[] pending, int[] chartOf, Queue<int> queue)
        {
            foreach (int n in edges[EdgeKey(a, b)])
            {
                if (pending[n] || chartOf[n] >= 0 || triangles[n].Class != cls) continue;
                pending[n] = true;
                queue.Enqueue(n);
            }
        }

        private static void AddEdge(Dictionary<long, List<int>> edges, int a, int b, int t)
        {
            long key = EdgeKey(a, b);
            if (!edges.TryGetValue(key, out List<int> list)) { list = new List<int>(2); edges[key] = list; }
            list.Add(t);
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static Vector3 Corner(IReadOnlyList<SourcePart> parts, Triangle t, int c) =>
            parts[t.Part].Positions[parts[t.Part].Indices[t.First + c]];

        public static Vector2 Project(Vector3 p, int axis)
        {
            PlaneAxes(axis, out int u, out int v);
            return new Vector2(Component(p, u), Component(p, v));
        }

        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }
}
