using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Engine.Components;
using Engine.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Lighting
{
    /// <summary>CPU copy of a filtered HDR cubemap, including every face and mip. No graphics resources are owned here.</summary>
    public sealed class ReflectionProbeData
    {
        private const uint Magic = 0x3146524F; // ORF1
        public const string Extension = ".reflections";
        public int Resolution;
        public Matrix Box;
        public byte[][] FaceMips;
        // Session-local: reset to the restored component's request after loading.
        public int CaptureRequest;

        public bool Matches(BasicEntity owner, ReflectionProbeComponent component) =>
            Resolution == component.ClampedResolution && SameBox(Box, ReflectionProbeComponent.BoxWorld(owner)) &&
            CaptureRequest == component.CaptureRequest;

        // Scene rotations round-trip as quaternions. Ignore their float rounding when comparing capture transforms.
        public static bool SameBox(Matrix a, Matrix b) => Near(a.Right, b.Right) && Near(a.Up, b.Up) &&
            Near(a.Backward, b.Backward) && a.Translation == b.Translation;

        private static bool Near(Vector3 a, Vector3 b) =>
            Vector3.DistanceSquared(a, b) <= 1e-10f * Math.Max(1, Math.Max(a.LengthSquared(), b.LengthSquared()));

        public static ReflectionProbeData Capture(ReflectionProbes.Probe probe)
        {
            TextureCube cube = probe.Cube;
            var data = new ReflectionProbeData
            {
                Resolution = cube.Size, Box = probe.CapturedBox, CaptureRequest = probe.CapturedRequest,
                FaceMips = new byte[6 * cube.LevelCount][],
            };
            for (int face = 0; face < 6; face++)
            for (int mip = 0; mip < cube.LevelCount; mip++)
            {
                int size = Math.Max(1, cube.Size >> mip);
                var bytes = new byte[size * size * 8]; // HalfVector4, lossless HDR
                cube.GetData((CubeMapFace)face, mip, null, bytes, 0, bytes.Length);
                data.FaceMips[face * cube.LevelCount + mip] = bytes;
            }
            return data;
        }

        public TextureCube Upload(GraphicsDevice graphics)
        {
            var cube = new TextureCube(graphics, Resolution, true, SurfaceFormat.HalfVector4);
            try
            {
                for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < cube.LevelCount; mip++)
                {
                    byte[] bytes = FaceMips[face * cube.LevelCount + mip];
                    cube.SetData((CubeMapFace)face, mip, null, bytes, 0, bytes.Length);
                }
                return cube;
            }
            catch { cube.Dispose(); throw; }
        }

        public static void Save(string path, IReadOnlyDictionary<Guid, ReflectionProbeData> probes)
        {
            // Replace only after the whole sidecar has been written.
            string temporary = path + ".tmp";
            try
            {
                using (var writer = new BinaryWriter(File.Create(temporary)))
                {
                    writer.Write(Magic);
                    writer.Write(1);
                    writer.Write(probes.Count);
                    foreach (var pair in probes)
                    {
                        writer.Write(pair.Key.ToByteArray());
                        ReflectionProbeData data = pair.Value;
                        writer.Write(data.Resolution);
                        Matrix b = data.Box;
                        foreach (float value in new[] { b.M11, b.M12, b.M13, b.M14, b.M21, b.M22, b.M23, b.M24,
                            b.M31, b.M32, b.M33, b.M34, b.M41, b.M42, b.M43, b.M44 }) writer.Write(value);
                        foreach (byte[] bytes in data.FaceMips) writer.Write(bytes);
                    }
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static Dictionary<Guid, ReflectionProbeData> Load(string path, CancellationToken cancel = default)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != 1)
                throw new InvalidDataException("Unsupported reflection cubemap format.");
            int count = reader.ReadInt32();
            if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / 84)
                throw new InvalidDataException("Invalid reflection cubemap count.");
            var result = new Dictionary<Guid, ReflectionProbeData>();
            for (int i = 0; i < count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var id = new Guid(ReadExact(reader, 16));
                int size = reader.ReadInt32();
                if (size < 64 || size > 512 || (size & (size - 1)) != 0)
                    throw new InvalidDataException("Invalid reflection cubemap resolution.");
                var box = new Matrix(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                int levels = 1;
                for (int n = size; n > 1; n >>= 1) levels++;
                var data = new ReflectionProbeData { Resolution = size, Box = box, FaceMips = new byte[6 * levels][] };
                for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < levels; mip++)
                {
                    cancel.ThrowIfCancellationRequested();
                    int width = Math.Max(1, size >> mip);
                    data.FaceMips[face * levels + mip] = ReadExact(reader, width * width * 8);
                }
                if (!result.TryAdd(id, data)) throw new InvalidDataException("Duplicate reflection probe ID.");
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Unexpected reflection cubemap data.");
            return result;
        }

        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("Truncated reflection cubemap.");
            return bytes;
        }
    }
}
