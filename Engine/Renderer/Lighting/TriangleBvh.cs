using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Bounding volume hierarchy over a static triangle soup, for the CPU light baker.
    /// Built once per bake (binned SAH split, ≤4 triangles per leaf, flattened depth-first).
    /// Thread-safe for concurrent queries after construction.
    /// </summary>
    public sealed class TriangleBvh
    {
        private struct Node
        {
            public Vector3 Min, Max;
            //Leaf: first triangle in _order. Inner: index of the right child (left child = this + 1).
            public int Offset;
            //Triangle count for leaves, 0 for inner nodes
            public int Count;
        }

        private const int LeafSize = 4;
        private const int BinCount = 12;
        private const float Epsilon = 1e-7f;

        private readonly Vector3[] _v0, _e1, _e2;
        private readonly int[] _order;
        private readonly List<Node> _nodes = new List<Node>();
        private Node[] _flat;

        public int TriangleCount => _v0.Length;

        public TriangleBvh(Vector3[] a, Vector3[] b, Vector3[] c)
        {
            int n = a.Length;
            _v0 = a;
            _e1 = new Vector3[n];
            _e2 = new Vector3[n];
            _order = new int[n];

            var centroids = new Vector3[n];
            var triMin = new Vector3[n];
            var triMax = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                _e1[i] = b[i] - a[i];
                _e2[i] = c[i] - a[i];
                _order[i] = i;
                triMin[i] = Vector3.Min(a[i], Vector3.Min(b[i], c[i]));
                triMax[i] = Vector3.Max(a[i], Vector3.Max(b[i], c[i]));
                centroids[i] = (triMin[i] + triMax[i]) * 0.5f;
            }

            if (n > 0) Build(0, n, centroids, triMin, triMax);
            _flat = _nodes.ToArray();
            _nodes.Clear();
        }

        // ---------------- Build ----------------

        private int Build(int start, int count, Vector3[] centroids, Vector3[] triMin, Vector3[] triMax)
        {
            Vector3 bMin = new Vector3(float.MaxValue), bMax = new Vector3(float.MinValue);
            Vector3 cMin = bMin, cMax = bMax;
            for (int i = start; i < start + count; i++)
            {
                int t = _order[i];
                bMin = Vector3.Min(bMin, triMin[t]);
                bMax = Vector3.Max(bMax, triMax[t]);
                cMin = Vector3.Min(cMin, centroids[t]);
                cMax = Vector3.Max(cMax, centroids[t]);
            }

            int nodeIndex = _nodes.Count;
            _nodes.Add(new Node { Min = bMin, Max = bMax, Offset = start, Count = count });
            if (count <= LeafSize) return nodeIndex;

            int mid = FindSahSplit(start, count, cMin, cMax, centroids, triMin, triMax, out bool split);
            if (!split)
            {
                //All centroids coincide (or SAH says don't split) - fall back to an object median
                Vector3 ext = cMax - cMin;
                int axis = ext.X > ext.Y ? (ext.X > ext.Z ? 0 : 2) : (ext.Y > ext.Z ? 1 : 2);
                Array.Sort(_order, start, count, Comparer<int>.Create((p, q) => Axis(centroids[p], axis).CompareTo(Axis(centroids[q], axis))));
                mid = start + count / 2;
            }

            Build(start, mid - start, centroids, triMin, triMax);
            int right = Build(mid, start + count - mid, centroids, triMin, triMax);
            _nodes[nodeIndex] = new Node { Min = bMin, Max = bMax, Offset = right, Count = 0 };
            return nodeIndex;
        }

        private int FindSahSplit(int start, int count, Vector3 cMin, Vector3 cMax, Vector3[] centroids,
            Vector3[] triMin, Vector3[] triMax, out bool split)
        {
            split = false;
            float bestCost = float.MaxValue;
            int bestAxis = -1, bestBin = -1;
            Vector3 ext = cMax - cMin;

            var binCount = new int[BinCount];
            var binMin = new Vector3[BinCount];
            var binMax = new Vector3[BinCount];

            for (int axis = 0; axis < 3; axis++)
            {
                float extent = Axis(ext, axis);
                if (extent < 1e-6f) continue;
                float lo = Axis(cMin, axis);
                float scale = BinCount / extent;

                for (int k = 0; k < BinCount; k++)
                {
                    binCount[k] = 0;
                    binMin[k] = new Vector3(float.MaxValue);
                    binMax[k] = new Vector3(float.MinValue);
                }
                for (int i = start; i < start + count; i++)
                {
                    int t = _order[i];
                    int k = Math.Min(BinCount - 1, (int)((Axis(centroids[t], axis) - lo) * scale));
                    binCount[k]++;
                    binMin[k] = Vector3.Min(binMin[k], triMin[t]);
                    binMax[k] = Vector3.Max(binMax[k], triMax[t]);
                }

                //Sweep: cost of splitting after bin k = leftArea*leftCount + rightArea*rightCount
                var rightArea = new float[BinCount];
                var rightCount = new int[BinCount];
                Vector3 rMin = new Vector3(float.MaxValue), rMax = new Vector3(float.MinValue);
                int rc = 0;
                for (int k = BinCount - 1; k > 0; k--)
                {
                    rc += binCount[k];
                    if (binCount[k] > 0) { rMin = Vector3.Min(rMin, binMin[k]); rMax = Vector3.Max(rMax, binMax[k]); }
                    rightCount[k] = rc;
                    rightArea[k] = rc > 0 ? Area(rMin, rMax) : 0;
                }
                Vector3 lMin = new Vector3(float.MaxValue), lMax = new Vector3(float.MinValue);
                int lc = 0;
                for (int k = 0; k < BinCount - 1; k++)
                {
                    lc += binCount[k];
                    if (binCount[k] > 0) { lMin = Vector3.Min(lMin, binMin[k]); lMax = Vector3.Max(lMax, binMax[k]); }
                    if (lc == 0 || rightCount[k + 1] == 0) continue;
                    float cost = Area(lMin, lMax) * lc + rightArea[k + 1] * rightCount[k + 1];
                    if (cost < bestCost) { bestCost = cost; bestAxis = axis; bestBin = k; }
                }
            }

            if (bestAxis < 0) return start;

            //Partition in place around the chosen bin boundary
            float bLo = Axis(cMin, bestAxis);
            float bScale = BinCount / Axis(ext, bestAxis);
            int i0 = start, i1 = start + count - 1;
            while (i0 <= i1)
            {
                int k = Math.Min(BinCount - 1, (int)((Axis(centroids[_order[i0]], bestAxis) - bLo) * bScale));
                if (k <= bestBin) i0++;
                else { (_order[i0], _order[i1]) = (_order[i1], _order[i0]); i1--; }
            }
            split = i0 > start && i0 < start + count;
            return i0;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static float Area(Vector3 min, Vector3 max)
        {
            Vector3 d = max - min;
            return d.X * d.Y + d.Y * d.Z + d.Z * d.X;
        }

        // ---------------- Queries ----------------

        /// <summary>Closest hit along the ray within (0, tMax). Two-sided.</summary>
        public bool Intersect(Vector3 origin, Vector3 dir, float tMax, out float tHit, out int triangle)
        {
            tHit = tMax;
            triangle = -1;
            if (_flat.Length == 0) return false;

            Vector3 inv = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
            Span<int> stack = stackalloc int[128];
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                int index = stack[--sp];
                ref readonly Node node = ref _flat[index];
                if (!RayBox(origin, inv, node.Min, node.Max, tHit)) continue;

                if (node.Count > 0)
                {
                    for (int i = node.Offset; i < node.Offset + node.Count; i++)
                    {
                        int t = _order[i];
                        if (RayTriangle(origin, dir, t, out float d) && d < tHit)
                        {
                            tHit = d;
                            triangle = t;
                        }
                    }
                }
                else if (sp < stack.Length - 2)
                {
                    stack[sp++] = node.Offset;
                    stack[sp++] = index + 1;
                }
            }
            return triangle >= 0;
        }

        /// <summary>Any hit along the ray within (0, tMax) — shadow rays.</summary>
        public bool Occluded(Vector3 origin, Vector3 dir, float tMax)
        {
            if (_flat.Length == 0) return false;

            Vector3 inv = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
            Span<int> stack = stackalloc int[128];
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                int index = stack[--sp];
                ref readonly Node node = ref _flat[index];
                if (!RayBox(origin, inv, node.Min, node.Max, tMax)) continue;

                if (node.Count > 0)
                {
                    for (int i = node.Offset; i < node.Offset + node.Count; i++)
                        if (RayTriangle(origin, dir, _order[i], out float d) && d < tMax) return true;
                }
                else if (sp < stack.Length - 2)
                {
                    stack[sp++] = node.Offset;
                    stack[sp++] = index + 1;
                }
            }
            return false;
        }

        private static bool RayBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max, float tMax)
        {
            float tx1 = (min.X - o.X) * inv.X, tx2 = (max.X - o.X) * inv.X;
            float tmin = Math.Min(tx1, tx2), tmax = Math.Max(tx1, tx2);
            float ty1 = (min.Y - o.Y) * inv.Y, ty2 = (max.Y - o.Y) * inv.Y;
            tmin = Math.Max(tmin, Math.Min(ty1, ty2)); tmax = Math.Min(tmax, Math.Max(ty1, ty2));
            float tz1 = (min.Z - o.Z) * inv.Z, tz2 = (max.Z - o.Z) * inv.Z;
            tmin = Math.Max(tmin, Math.Min(tz1, tz2)); tmax = Math.Min(tmax, Math.Max(tz1, tz2));
            return tmax >= Math.Max(tmin, 0) && tmin < tMax;
        }

        //Möller–Trumbore, no back-face culling
        private bool RayTriangle(Vector3 o, Vector3 d, int t, out float dist)
        {
            dist = 0;
            Vector3 e1 = _e1[t], e2 = _e2[t];
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -Epsilon && det < Epsilon) return false;
            float invDet = 1f / det;
            Vector3 s = o - _v0[t];
            float u = Vector3.Dot(s, p) * invDet;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * invDet;
            if (v < 0 || u + v > 1) return false;
            dist = Vector3.Dot(e2, q) * invDet;
            return dist > 1e-4f;
        }
    }
}
