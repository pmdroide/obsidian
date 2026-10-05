using System;
using System.IO;
using Microsoft.Xna.Framework;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Baked result of the probe volume: a regular grid of probes spanning
    /// [BoundsMin, BoundsMax] (probes sit on the grid corners, both ends inclusive).
    ///
    /// Each probe stores diffuse irradiance as L1 spherical harmonics per colour channel, already
    /// convolved with the cosine lobe and expressed in the engine's light units, so evaluation is
    /// just <c>E(n) = c.x + dot(c.yzw, n)</c> — the same value the deferred light shaders would
    /// write into the diffuse light buffer for a white surface.
    ///
    /// Immutable once a bake finishes; the GPU copy lives in <see cref="LightingSystem"/>.
    /// </summary>
    public sealed class ProbeVolumeData
    {
        private const uint Magic = 0x3156504F; // "OPV1"
        //2 added the per-probe free distances (DistPos/DistNeg)
        private const int FormatVersion = 2;

        //Free distance used for probes baked before distances were stored: never occludes
        public const float UnlimitedDistance = 10000f;

        public int CountX, CountY, CountZ;
        public Vector3 BoundsMin, BoundsMax;

        //(c0, cx, cy, cz) per probe, one array per colour channel
        public Vector4[] SHR, SHG, SHB;

        //1 = valid probe, 0 = was inside geometry and got filled from its neighbours
        public byte[] Valid;

        //Mean distance to the nearest geometry seen from each probe along +X/+Y/+Z (DistPos.xyz) and
        //-X/-Y/-Z (DistNeg.xyz). Sampling ignores probes whose view of the shaded point is blocked,
        //so light doesn't leak through walls thinner than the probe spacing.
        public Vector4[] DistPos, DistNeg;

        // Bake metadata (shown in the editor)
        public int SamplesPerProbe;
        public int Bounces;
        public DateTime BakedAtUtc;
        public double BakeSeconds;

        public int ProbeCount => CountX * CountY * CountZ;

        public int Index(int x, int y, int z) => x + CountX * (y + CountY * z);

        public Vector3 ProbePosition(int x, int y, int z)
        {
            Vector3 extent = BoundsMax - BoundsMin;
            return BoundsMin + extent * new Vector3(
                CountX > 1 ? x / (float)(CountX - 1) : 0.5f,
                CountY > 1 ? y / (float)(CountY - 1) : 0.5f,
                CountZ > 1 ? z / (float)(CountZ - 1) : 0.5f);
        }

        public static ProbeVolumeData Create(int countX, int countY, int countZ, Vector3 min, Vector3 max)
        {
            int n = countX * countY * countZ;
            return new ProbeVolumeData
            {
                CountX = countX, CountY = countY, CountZ = countZ,
                BoundsMin = min, BoundsMax = max,
                SHR = new Vector4[n], SHG = new Vector4[n], SHB = new Vector4[n],
                Valid = new byte[n],
                DistPos = new Vector4[n], DistNeg = new Vector4[n],
            };
        }

        /// <summary>
        /// Irradiance at a surface point, matching the GPU sampling in DeferredEnvironmentMap.fx:
        /// sampled half a cell out along the normal, and each of the 8 surrounding probes weighted by
        /// whether it can see that point and whether it is in front of the surface. Used by the baker
        /// to light the hit points of later bounces. Positions outside the volume clamp to the border probes.
        /// </summary>
        public Vector3 EvaluateIrradiance(Vector3 position, Vector3 normal)
        {
            Vector3 extent = BoundsMax - BoundsMin;
            Vector3 cellSize = new Vector3(
                CountX > 1 ? extent.X / (CountX - 1) : extent.X,
                CountY > 1 ? extent.Y / (CountY - 1) : extent.Y,
                CountZ > 1 ? extent.Z / (CountZ - 1) : extent.Z);
            position += normal * cellSize * 0.5f;

            float fx = Cell(position.X - BoundsMin.X, extent.X, CountX, out int x0);
            float fy = Cell(position.Y - BoundsMin.Y, extent.Y, CountY, out int y0);
            float fz = Cell(position.Z - BoundsMin.Z, extent.Z, CountZ, out int z0);

            Vector4 r = Vector4.Zero, g = Vector4.Zero, b = Vector4.Zero;
            Vector4 plainR = Vector4.Zero, plainG = Vector4.Zero, plainB = Vector4.Zero;
            float total = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                int cx = corner & 1, cy = (corner >> 1) & 1, cz = (corner >> 2) & 1;
                int x = Math.Min(x0 + cx, CountX - 1), y = Math.Min(y0 + cy, CountY - 1), z = Math.Min(z0 + cz, CountZ - 1);
                float trilinear = (cx == 1 ? fx : 1 - fx) * (cy == 1 ? fy : 1 - fy) * (cz == 1 ? fz : 1 - fz);
                int i = Index(x, y, z);

                Vector3 toPoint = position - ProbePosition(x, y, z);
                float w = trilinear * Visibility(toPoint, DistPos[i], DistNeg[i]) * BackfaceWeight(toPoint, normal);
                r += SHR[i] * w; g += SHG[i] * w; b += SHB[i] * w;
                total += w;
                plainR += SHR[i] * trilinear; plainG += SHG[i] * trilinear; plainB += SHB[i] * trilinear;
            }

            //Every neighbour blocked (e.g. a point inside geometry): fall back to plain trilinear
            if (total > 1e-5f) { r /= total; g /= total; b /= total; }
            else { r = plainR; g = plainG; b = plainB; }

            return new Vector3(
                Math.Max(0, Eval(r, normal)),
                Math.Max(0, Eval(g, normal)),
                Math.Max(0, Eval(b, normal)));
        }

        /// <summary>
        /// 1 when <paramref name="toPoint"/> (probe to shaded point) stays within the probe's free
        /// distance in that direction, falling off steeply beyond it. Same as ProbeVisibility in
        /// DeferredEnvironmentMap.fx.
        /// </summary>
        public static float Visibility(Vector3 toPoint, Vector4 distPos, Vector4 distNeg)
        {
            float d = toPoint.Length();
            if (d < 1e-4f) return 1;
            Vector3 dir = toPoint / d;
            //Harmonic blend of the three axis distances facing dir, so the nearest blocker dominates:
            //for a wall at distance h along one axis it gives h / cos², close to the true h / cos.
            float inverse = dir.X * dir.X / Math.Max(dir.X >= 0 ? distPos.X : distNeg.X, 1e-4f)
                          + dir.Y * dir.Y / Math.Max(dir.Y >= 0 ? distPos.Y : distNeg.Y, 1e-4f)
                          + dir.Z * dir.Z / Math.Max(dir.Z >= 0 ? distPos.Z : distNeg.Z, 1e-4f);
            float free = 1 / Math.Max(inverse, 1e-6f);
            if (d <= free) return 1;
            float ratio = free / d;
            ratio *= ratio;
            return ratio * ratio;
        }

        /// <summary>Down-weights probes behind the surface (DDGI's smooth backface term).</summary>
        public static float BackfaceWeight(Vector3 toPoint, Vector3 normal)
        {
            float d = toPoint.Length();
            if (d < 1e-4f) return 1;
            float facing = (Vector3.Dot(-toPoint / d, normal) + 1) * 0.5f;
            return facing * facing + 0.2f;
        }

        /// <summary>
        /// Plain trilinear SH coefficients (c0, cx, cy, cz) per colour channel at a point in open
        /// space (no visibility weighting). Positions outside the volume clamp to the border probes.
        /// </summary>
        public void SampleSH(Vector3 position, out Vector4 r, out Vector4 g, out Vector4 b)
        {
            Vector3 extent = BoundsMax - BoundsMin;
            float fx = Cell(position.X - BoundsMin.X, extent.X, CountX, out int x0);
            float fy = Cell(position.Y - BoundsMin.Y, extent.Y, CountY, out int y0);
            float fz = Cell(position.Z - BoundsMin.Z, extent.Z, CountZ, out int z0);
            int x1 = Math.Min(x0 + 1, CountX - 1), y1 = Math.Min(y0 + 1, CountY - 1), z1 = Math.Min(z0 + 1, CountZ - 1);

            r = Vector4.Zero; g = Vector4.Zero; b = Vector4.Zero;
            Accumulate(Index(x0, y0, z0), (1 - fx) * (1 - fy) * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x1, y0, z0), fx * (1 - fy) * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x0, y1, z0), (1 - fx) * fy * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x1, y1, z0), fx * fy * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x0, y0, z1), (1 - fx) * (1 - fy) * fz, ref r, ref g, ref b);
            Accumulate(Index(x1, y0, z1), fx * (1 - fy) * fz, ref r, ref g, ref b);
            Accumulate(Index(x0, y1, z1), (1 - fx) * fy * fz, ref r, ref g, ref b);
            Accumulate(Index(x1, y1, z1), fx * fy * fz, ref r, ref g, ref b);
        }

        private static float Eval(Vector4 c, Vector3 n) => c.X + c.Y * n.X + c.Z * n.Y + c.W * n.Z;

        private void Accumulate(int i, float w, ref Vector4 r, ref Vector4 g, ref Vector4 b)
        {
            r += SHR[i] * w;
            g += SHG[i] * w;
            b += SHB[i] * w;
        }

        private static float Cell(float offset, float extent, int count, out int i0)
        {
            if (count <= 1 || extent <= 0) { i0 = 0; return 0; }
            float t = MathHelper.Clamp(offset / extent, 0, 1) * (count - 1);
            i0 = Math.Min((int)t, count - 2);
            return t - i0;
        }

        // ---------------- Persistence (.probes sidecar next to the .obsc) ----------------

        public void Save(string path)
        {
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(CountX); w.Write(CountY); w.Write(CountZ);
            WriteVector3(w, BoundsMin); WriteVector3(w, BoundsMax);
            w.Write(SamplesPerProbe); w.Write(Bounces);
            w.Write(BakedAtUtc.Ticks); w.Write(BakeSeconds);
            WriteArray(w, SHR); WriteArray(w, SHG); WriteArray(w, SHB);
            w.Write(Valid);
            WriteArray(w, DistPos); WriteArray(w, DistNeg);
        }

        public static ProbeVolumeData Load(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a probe volume file");
            int version = r.ReadInt32();
            if (version < 1 || version > FormatVersion) throw new InvalidDataException($"Unsupported probe volume version {version}");

            int cx = r.ReadInt32(), cy = r.ReadInt32(), cz = r.ReadInt32();
            if (cx <= 0 || cy <= 0 || cz <= 0 || (long)cx * cy * cz > 16_000_000)
                throw new InvalidDataException($"Invalid probe grid {cx}x{cy}x{cz}");

            var data = Create(cx, cy, cz, ReadVector3(r), ReadVector3(r));
            data.SamplesPerProbe = r.ReadInt32();
            data.Bounces = r.ReadInt32();
            data.BakedAtUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            data.BakeSeconds = r.ReadDouble();
            ReadArray(r, data.SHR); ReadArray(r, data.SHG); ReadArray(r, data.SHB);
            data.Valid = r.ReadBytes(data.ProbeCount);
            if (version >= 2)
            {
                ReadArray(r, data.DistPos); ReadArray(r, data.DistNeg);
            }
            else
            {
                //Older bakes have no distances: every probe sees everything, as before
                var unlimited = new Vector4(UnlimitedDistance);
                Array.Fill(data.DistPos, unlimited);
                Array.Fill(data.DistNeg, unlimited);
            }
            return data;
        }

        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        private static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteArray(BinaryWriter w, Vector4[] a)
        {
            foreach (Vector4 v in a) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(v.W); }
        }

        private static void ReadArray(BinaryReader r, Vector4[] a)
        {
            for (int i = 0; i < a.Length; i++)
                a[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }
    }
}
