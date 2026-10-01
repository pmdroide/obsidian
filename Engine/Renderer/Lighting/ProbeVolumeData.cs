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
        private const int FormatVersion = 1;

        public int CountX, CountY, CountZ;
        public Vector3 BoundsMin, BoundsMax;

        //(c0, cx, cy, cz) per probe, one array per colour channel
        public Vector4[] SHR, SHG, SHB;

        //1 = valid probe, 0 = was inside geometry and got filled from its neighbours
        public byte[] Valid;

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
            };
        }

        /// <summary>
        /// Trilinear irradiance lookup, matching the GPU sampling in DeferredEnvironmentMap.fx.
        /// Used by the baker to light the hit points of later bounces. Positions outside the
        /// volume clamp to the border probes.
        /// </summary>
        public Vector3 EvaluateIrradiance(Vector3 position, Vector3 normal)
        {
            Vector3 extent = BoundsMax - BoundsMin;
            float fx = Cell(position.X - BoundsMin.X, extent.X, CountX, out int x0);
            float fy = Cell(position.Y - BoundsMin.Y, extent.Y, CountY, out int y0);
            float fz = Cell(position.Z - BoundsMin.Z, extent.Z, CountZ, out int z0);
            int x1 = Math.Min(x0 + 1, CountX - 1), y1 = Math.Min(y0 + 1, CountY - 1), z1 = Math.Min(z0 + 1, CountZ - 1);

            Vector4 r = Vector4.Zero, g = Vector4.Zero, b = Vector4.Zero;
            Accumulate(Index(x0, y0, z0), (1 - fx) * (1 - fy) * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x1, y0, z0), fx * (1 - fy) * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x0, y1, z0), (1 - fx) * fy * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x1, y1, z0), fx * fy * (1 - fz), ref r, ref g, ref b);
            Accumulate(Index(x0, y0, z1), (1 - fx) * (1 - fy) * fz, ref r, ref g, ref b);
            Accumulate(Index(x1, y0, z1), fx * (1 - fy) * fz, ref r, ref g, ref b);
            Accumulate(Index(x0, y1, z1), (1 - fx) * fy * fz, ref r, ref g, ref b);
            Accumulate(Index(x1, y1, z1), fx * fy * fz, ref r, ref g, ref b);

            return new Vector3(
                Math.Max(0, Eval(r, normal)),
                Math.Max(0, Eval(g, normal)),
                Math.Max(0, Eval(b, normal)));
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
        }

        public static ProbeVolumeData Load(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a probe volume file");
            int version = r.ReadInt32();
            if (version != FormatVersion) throw new InvalidDataException($"Unsupported probe volume version {version}");

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
