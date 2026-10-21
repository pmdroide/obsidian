using System;
using System.Collections.Generic;
using Engine.Recources;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// Fill-level tables of a container model for the Liquid component: for the model upright (+Z up), on its side
    /// (+X up) and upside down (-Z up), the height of the liquid surface at <see cref="Samples"/> fill fractions,
    /// measured along "up" from the model's bounds centre. Half-Life: Alyx has the artist type these per material
    /// (<c>g_vLiquidLevel{Upwards,Sidewards,Downwards}MinMidMax</c> = heights at fill 0, 0.5 and 1; Docs/Research/BottleLiquid);
    /// here they come from the closed mesh: the volume under a plane is summed from the triangles clipped to it.
    /// </summary>
    public sealed class LiquidVolume
    {
        /// <summary>Fill fractions per table: 0, 1/8 ... 1 (Alyx uses 3: 0, 0.5, 1).</summary>
        public const int Samples = 9;

        /// <summary>Surface heights (model units, from <see cref="Centre"/>) at fill i / (Samples - 1).</summary>
        public readonly float[] Upwards = new float[Samples];
        public readonly float[] Sidewards = new float[Samples];
        public readonly float[] Downwards = new float[Samples];
        /// <summary>Bounds centre of the model, the point the heights are measured from.</summary>
        public Vector3 Centre;
        /// <summary>Largest distance of the mesh from the vertical axis through <see cref="Centre"/> (model units).</summary>
        public float Radius;
        /// <summary>Volume of the closed mesh in model units³; 0 when the mesh isn't closed (the tables then follow the bounds).</summary>
        public float Volume;

        private static readonly Dictionary<ModelDefinition, LiquidVolume> Cache = new();

        /// <summary>The tables of <paramref name="model"/>, computed once per model.</summary>
        public static LiquidVolume For(ModelDefinition model)
        {
            if (model?.Model == null) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(model, out LiquidVolume cached)) return cached;
                LiquidVolume volume;
                try
                {
                    ModelDataExtractor.GetVerticesAndIndicesFromModel(model.Model, out Vector3[] vertices, out int[] indices);
                    volume = Compute(vertices, indices, model.BoundingBox);
                }
                catch (Exception ex)
                {
                    Editor.EditorBridge.Log("Liquid: couldn't read the model's mesh (" + ex.Message + "); using its bounds.");
                    volume = Compute(Array.Empty<Vector3>(), Array.Empty<int>(), model.BoundingBox);
                }
                Cache[model] = volume;
                return volume;
            }
        }

        public static LiquidVolume Compute(Vector3[] vertices, int[] indices, BoundingBox bounds)
        {
            var result = new LiquidVolume { Centre = (bounds.Min + bounds.Max) * 0.5f };
            float total = TotalVolume(vertices, indices);
            result.Volume = Math.Abs(total);
            foreach (Vector3 p in vertices)
                result.Radius = Math.Max(result.Radius, new Vector2(p.X - result.Centre.X, p.Y - result.Centre.Y).Length());
            if (result.Radius <= 0) result.Radius = Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y) * 0.5f;
            Fill(result.Upwards, vertices, indices, total, Vector3.UnitZ, result.Centre, bounds);
            Fill(result.Sidewards, vertices, indices, total, Vector3.UnitX, result.Centre, bounds);
            Fill(result.Downwards, vertices, indices, total, -Vector3.UnitZ, result.Centre, bounds);
            return result;
        }

        /// <summary>Surface height (model units from <see cref="Centre"/>) of a <paramref name="fill"/> fraction, <paramref name="cosUp"/> = dot(model +Z, world up).</summary>
        public float Height(float cosUp, float fill)
        {
            // Alyx (vr_simple_liquid PS): upside down -> on its side over cos -1..0, then -> upright over 0..1.
            float side = Sample(Sidewards, fill);
            if (cosUp >= 0) return MathHelper.Lerp(side, Sample(Upwards, fill), Math.Min(cosUp, 1));
            return MathHelper.Lerp(side, Sample(Downwards, fill), Math.Min(-cosUp, 1));
        }

        private static float Sample(float[] table, float fill)
        {
            float x = Math.Clamp(fill, 0, 1) * (Samples - 1);
            int i = Math.Min((int)x, Samples - 2);
            return MathHelper.Lerp(table[i], table[i + 1], x - i);
        }

        private static void Fill(float[] table, Vector3[] v, int[] idx, float total, Vector3 axis, Vector3 centre, BoundingBox bounds)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 p in v)
            {
                float d = Vector3.Dot(p, axis);
                lo = Math.Min(lo, d);
                hi = Math.Max(hi, d);
            }
            if (lo > hi)
            {
                // No mesh: the bounds along the axis.
                lo = Math.Min(Vector3.Dot(bounds.Min, axis), Vector3.Dot(bounds.Max, axis));
                hi = Math.Max(Vector3.Dot(bounds.Min, axis), Vector3.Dot(bounds.Max, axis));
            }
            float c = Vector3.Dot(centre, axis);
            // Empty and full sit just outside the mesh so no sliver of liquid (or glass) shows at the ends.
            float pad = (hi - lo) * 0.01f;
            table[0] = lo - pad - c;
            table[Samples - 1] = hi + pad - c;
            bool closed = Math.Abs(total) > 1e-12f;
            for (int i = 1; i < Samples - 1; i++)
            {
                float target = (float)i / (Samples - 1);
                float h;
                if (!closed) h = MathHelper.Lerp(lo, hi, target);
                else
                {
                    // The volume under the plane grows with its height: bisect.
                    float a = lo, b = hi;
                    for (int step = 0; step < 32; step++)
                    {
                        float m = (a + b) * 0.5f;
                        if (VolumeBelow(v, idx, axis, m) / total < target) a = m; else b = m;
                    }
                    h = (a + b) * 0.5f;
                }
                table[i] = h - c;
            }
        }

        /// <summary>Signed volume of the closed mesh (positive for outward, counter-clockwise faces).</summary>
        private static float TotalVolume(Vector3[] v, int[] idx)
        {
            double sum = 0;
            for (int t = 0; t + 2 < idx.Length; t += 3)
                sum += Vector3.Dot(v[idx[t]], Vector3.Cross(v[idx[t + 1]], v[idx[t + 2]]));
            return (float)(sum / 6);
        }

        /// <summary>
        /// Signed volume of the mesh where dot(p, axis) &lt;= h: each triangle clipped to that half space forms
        /// tetrahedra with a point on the plane, so the plane's cap adds nothing and needs no polygon.
        /// </summary>
        private static float VolumeBelow(Vector3[] v, int[] idx, Vector3 axis, float h)
        {
            Vector3 origin = axis * h;
            Span<Vector3> poly = stackalloc Vector3[4];
            double sum = 0;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                Vector3 a = v[idx[t]], b = v[idx[t + 1]], c = v[idx[t + 2]];
                int n = Clip(a, b, c, axis, h, poly);
                for (int k = 1; k + 1 < n; k++)
                    sum += Vector3.Dot(poly[0] - origin, Vector3.Cross(poly[k] - origin, poly[k + 1] - origin));
            }
            return (float)(sum / 6);
        }

        private static int Clip(Vector3 a, Vector3 b, Vector3 c, Vector3 axis, float h, Span<Vector3> output)
        {
            Span<Vector3> input = stackalloc Vector3[3] { a, b, c };
            int n = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = input[i], q = input[(i + 1) % 3];
                float dp = Vector3.Dot(p, axis) - h, dq = Vector3.Dot(q, axis) - h;
                if (dp <= 0) output[n++] = p;
                if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                    output[n++] = p + (q - p) * (dp / (dp - dq));
            }
            return n;
        }
    }
}
