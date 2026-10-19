using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Baked lightmaps of a scene (Source 2 look, phase 4.2; HLA: m_bHasBakedLightingFromLightMap, vr_directional_lightmaps,
    /// bakelightindex). Saved as a <c>.lightmaps</c> sidecar next to the .obsc.
    ///
    /// Per lightmap texel, three pages hold:
    /// <list type="bullet">
    /// <item><see cref="Page.Irradiance"/> (half RGBA): rgb = indirect irradiance in the probes' units, already divided by the
    /// directional term at the baked normal, a = baked ambient occlusion</item>
    /// <item><see cref="Page.Direction"/> (RGBA8): rgb = dominant direction of the incoming indirect light (world, ×0.5+0.5),
    /// a = how directional it is (0 = from everywhere, 1 = from one direction)</item>
    /// <item><see cref="Page.Mask"/> (RGBA8): shadow mask, how much of each of up to 4 static lights reaches the texel
    /// (channel per light in <see cref="Channels"/>)</item>
    /// </list>
    /// Lightmapped gameobjects are matched by ID (<see cref="Instance.EntityId"/>); their lightmap UVs are per instance,
    /// the split geometry per model (<see cref="Meshes"/>).
    ///
    /// Immutable once a bake finishes; the GPU copy lives in <see cref="LightmapResources"/>.
    /// </summary>
    public sealed class LightmapData
    {
        public const string Extension = ".lightmaps";
        private const uint Magic = 0x314D4C4F; // "OLM1"
        private const int FormatVersion = 1;

        /// <summary>Shadow mask channels: a texel can be shadowed by this many static lights (RGBA).</summary>
        public const int MaxChannels = 4;

        public sealed class Page
        {
            public int Width, Height;
            public ulong[] Irradiance; //HalfVector4 packed
            public uint[] Direction;   //Color packed (r in the low byte)
            public uint[] Mask;        //Color packed
        }

        public sealed class Mesh
        {
            public List<LightmapUnwrap.Part> Parts = new List<LightmapUnwrap.Part>();
            //Model-space bounding sphere of the lightmapped vertices (culling)
            public Vector3 Center;
            public float Radius;
        }

        public sealed class Instance
        {
            public int EntityId;
            public int Mesh;
            public int Page;
            //Lightmap UV (page 0..1) per lightmap vertex, per mesh part; null for parts without a lightmap
            public Vector2[][] Uv;
            //Texels and world area, for the editor summary
            public int Texels;
        }

        /// <summary>A static light given a shadow mask channel, and where it was when baked (moved lights fall back to real-time shadows).</summary>
        public struct LightChannel
        {
            public int LightId;
            public int Channel;
            public bool Directional;
            public Vector3 Position;  //point/spot lights
            public float Radius;
            public Vector3 Direction; //directional lights: towards the light
        }

        public readonly List<Page> Pages = new List<Page>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Instance> Instances = new List<Instance>();
        public readonly List<LightChannel> Channels = new List<LightChannel>();

        // Bake metadata (shown in the editor)
        public float TexelsPerUnit;
        public int Samples;
        public int TexelCount;
        public int LightsWithoutChannel;
        public DateTime BakedAtUtc;
        public double BakeSeconds;

        private Dictionary<int, Instance> _byEntity;

        public Instance FindInstance(int entityId)
        {
            if (_byEntity == null)
            {
                var map = new Dictionary<int, Instance>();
                foreach (Instance instance in Instances) map[instance.EntityId] = instance;
                _byEntity = map;
            }
            return _byEntity.TryGetValue(entityId, out Instance found) ? found : null;
        }

        /// <summary>
        /// The shadow mask channel of a point/spot light as a one-hot vector, or zero when it has none or moved since the
        /// bake (then its real-time shadows are all there is).
        /// </summary>
        public Vector4 ChannelOf(int lightId, Vector3 position, float radius)
        {
            foreach (LightChannel c in Channels)
            {
                if (c.LightId != lightId || c.Directional) continue;
                if (Vector3.DistanceSquared(c.Position, position) > 1e-4f || Math.Abs(c.Radius - radius) > 1e-3f) return Vector4.Zero;
                return OneHot(c.Channel);
            }
            return Vector4.Zero;
        }

        /// <summary>The shadow mask channel of a directional light (<paramref name="toLight"/> = from surfaces towards it), or zero.</summary>
        public Vector4 ChannelOf(int lightId, Vector3 toLight)
        {
            foreach (LightChannel c in Channels)
            {
                if (c.LightId != lightId || !c.Directional) continue;
                if (Vector3.Dot(c.Direction, toLight) < 0.99995f) return Vector4.Zero;
                return OneHot(c.Channel);
            }
            return Vector4.Zero;
        }

        public static Vector4 OneHot(int channel) => channel switch
        {
            0 => new Vector4(1, 0, 0, 0),
            1 => new Vector4(0, 1, 0, 0),
            2 => new Vector4(0, 0, 1, 0),
            3 => new Vector4(0, 0, 0, 1),
            _ => Vector4.Zero,
        };

        public static ulong PackIrradiance(Vector4 value) => new HalfVector4(value).PackedValue;
        public static Vector4 UnpackIrradiance(ulong packed) => new HalfVector4 { PackedValue = packed }.ToVector4();

        // ---------------- Persistence (.lightmaps sidecar next to the .obsc) ----------------

        public void Save(string path)
        {
            using var fs = File.Create(path);
            using (var header = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                header.Write(Magic);
                header.Write(FormatVersion);
            }
            //Pages are mostly smooth or empty gutters: deflate shrinks them a lot
            using var deflate = new DeflateStream(fs, CompressionLevel.Fastest);
            using var w = new BinaryWriter(deflate);
            w.Write(TexelsPerUnit); w.Write(Samples); w.Write(TexelCount); w.Write(LightsWithoutChannel);
            w.Write(BakedAtUtc.Ticks); w.Write(BakeSeconds);

            w.Write(Pages.Count);
            foreach (Page page in Pages)
            {
                w.Write(page.Width); w.Write(page.Height);
                foreach (ulong v in page.Irradiance) w.Write(v);
                foreach (uint v in page.Direction) w.Write(v);
                foreach (uint v in page.Mask) w.Write(v);
            }

            w.Write(Meshes.Count);
            foreach (Mesh mesh in Meshes)
            {
                WriteVector3(w, mesh.Center); w.Write(mesh.Radius);
                w.Write(mesh.Parts.Count);
                foreach (LightmapUnwrap.Part part in mesh.Parts)
                {
                    w.Write(part.SourceVertexCount);
                    w.Write(part.Lightmapped);
                    w.Write(part.Positions.Length);
                    foreach (Vector3 p in part.Positions) WriteVector3(w, p);
                    w.Write(part.Indices.Length);
                    foreach (int i in part.Indices) w.Write(i);
                }
            }

            w.Write(Instances.Count);
            foreach (Instance instance in Instances)
            {
                w.Write(instance.EntityId); w.Write(instance.Mesh); w.Write(instance.Page); w.Write(instance.Texels);
                w.Write(instance.Uv.Length);
                foreach (Vector2[] uv in instance.Uv)
                {
                    w.Write(uv?.Length ?? -1);
                    if (uv != null) foreach (Vector2 t in uv) { w.Write(t.X); w.Write(t.Y); }
                }
            }

            w.Write(Channels.Count);
            foreach (LightChannel c in Channels)
            {
                w.Write(c.LightId); w.Write(c.Channel); w.Write(c.Directional);
                WriteVector3(w, c.Position); w.Write(c.Radius); WriteVector3(w, c.Direction);
            }
        }

        public static LightmapData Load(string path)
        {
            using var fs = File.OpenRead(path);
            using (var header = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                if (header.ReadUInt32() != Magic) throw new InvalidDataException("Not a lightmap file");
                int version = header.ReadInt32();
                if (version < 1 || version > FormatVersion) throw new InvalidDataException($"Unsupported lightmap version {version}");
            }
            using var deflate = new DeflateStream(fs, CompressionMode.Decompress);
            using var r = new BinaryReader(deflate);
            var data = new LightmapData
            {
                TexelsPerUnit = r.ReadSingle(), Samples = r.ReadInt32(), TexelCount = r.ReadInt32(), LightsWithoutChannel = r.ReadInt32(),
                BakedAtUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc), BakeSeconds = r.ReadDouble(),
            };

            int pages = Count(r.ReadInt32(), 256, "page");
            for (int i = 0; i < pages; i++)
            {
                int width = r.ReadInt32(), height = r.ReadInt32();
                if (width <= 0 || height <= 0 || width > 8192 || height > 8192) throw new InvalidDataException($"Invalid lightmap page {width}x{height}");
                int n = width * height;
                var page = new Page { Width = width, Height = height, Irradiance = new ulong[n], Direction = new uint[n], Mask = new uint[n] };
                for (int t = 0; t < n; t++) page.Irradiance[t] = r.ReadUInt64();
                for (int t = 0; t < n; t++) page.Direction[t] = r.ReadUInt32();
                for (int t = 0; t < n; t++) page.Mask[t] = r.ReadUInt32();
                data.Pages.Add(page);
            }

            int meshes = Count(r.ReadInt32(), 1 << 20, "mesh");
            for (int m = 0; m < meshes; m++)
            {
                var mesh = new Mesh { Center = ReadVector3(r), Radius = r.ReadSingle() };
                int parts = Count(r.ReadInt32(), 1 << 16, "mesh part");
                for (int p = 0; p < parts; p++)
                {
                    var part = new LightmapUnwrap.Part { SourceVertexCount = r.ReadInt32(), Lightmapped = r.ReadBoolean() };
                    part.Positions = new Vector3[Count(r.ReadInt32(), 1 << 26, "vertex")];
                    for (int i = 0; i < part.Positions.Length; i++) part.Positions[i] = ReadVector3(r);
                    part.Indices = new int[Count(r.ReadInt32(), 1 << 28, "index")];
                    for (int i = 0; i < part.Indices.Length; i++)
                    {
                        int index = r.ReadInt32();
                        if ((uint)index >= (uint)part.Positions.Length) throw new InvalidDataException("Lightmap index out of range");
                        part.Indices[i] = index;
                    }
                    mesh.Parts.Add(part);
                }
                data.Meshes.Add(mesh);
            }

            int instances = Count(r.ReadInt32(), 1 << 22, "instance");
            for (int i = 0; i < instances; i++)
            {
                var instance = new Instance { EntityId = r.ReadInt32(), Mesh = r.ReadInt32(), Page = r.ReadInt32(), Texels = r.ReadInt32() };
                if ((uint)instance.Mesh >= (uint)data.Meshes.Count || (uint)instance.Page >= (uint)data.Pages.Count)
                    throw new InvalidDataException("Lightmap instance refers to a missing mesh or page");
                instance.Uv = new Vector2[Count(r.ReadInt32(), 1 << 16, "uv set")][];
                for (int p = 0; p < instance.Uv.Length; p++)
                {
                    int n = r.ReadInt32();
                    if (n < 0) continue;
                    var uv = new Vector2[Count(n, 1 << 26, "uv")];
                    for (int t = 0; t < n; t++) uv[t] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    instance.Uv[p] = uv;
                }
                data.Instances.Add(instance);
            }

            int channels = Count(r.ReadInt32(), 4096, "light channel");
            for (int i = 0; i < channels; i++)
                data.Channels.Add(new LightChannel
                {
                    LightId = r.ReadInt32(), Channel = r.ReadInt32(), Directional = r.ReadBoolean(),
                    Position = ReadVector3(r), Radius = r.ReadSingle(), Direction = ReadVector3(r),
                });
            return data;
        }

        private static int Count(int value, int max, string what)
        {
            if (value < 0 || value > max) throw new InvalidDataException($"Invalid {what} count {value}");
            return value;
        }

        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        private static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
