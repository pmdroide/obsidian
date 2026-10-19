using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Renderer.Helper.HelperGeometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Engine.Renderer.Lighting
{
    /// <summary>
    /// Owns baked lighting at runtime: starts/cancels probe volume bakes and keeps the GPU copy of
    /// the active scene's <see cref="Scene.BakedProbes"/> in sync.
    ///
    /// Threading: StartBake, ClearBake, Update and the debug drawing run on the game thread. The
    /// bake itself runs on the thread pool against a gathered snapshot; its result is handed back
    /// in <see cref="Update"/>. <see cref="Cancel"/> and <see cref="Status"/> are safe from any thread.
    /// </summary>
    public sealed class LightingSystem : IDisposable
    {
        //Debug drawing draws one octahedron per probe (one draw call each), so cap it
        private const int MaxDebugProbes = 4096;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

        private volatile LightingBakeStatus _status = LightingBakeStatus.Idle;
        private CancellationTokenSource _cts;
        private Task<BakeResult> _bakeTask;

        /// <summary>What one bake produces: the probes always, lightmaps when Lightmap Static gameobjects were baked.</summary>
        private sealed class BakeResult
        {
            public ProbeVolumeData Probes;
            public LightmapData Lightmaps;
            public bool BakedLightmaps;
            public List<string> LightmapSkipped;
        }
        private Scene _bakeScene;
        private long _lastProgressTicks;

        private ProbeVolumeData _uploaded;

        /// <summary>
        /// The five probe grids side by side along X (SH red, green, blue, distances +, -): what the shaders bind, one
        /// texture slot instead of five (Shaders: ProbeTexel).
        /// </summary>
        public Texture3D ProbeData { get; private set; }

        /// <summary>Volumes packed in <see cref="ProbeData"/>, highest priority first (the main volume last), with their first Z slice.</summary>
        public IReadOnlyList<(ProbeVolumeData Volume, int ZOffset)> PackedVolumes => _packed;
        private readonly List<(ProbeVolumeData Volume, int ZOffset)> _packed = new List<(ProbeVolumeData, int)>();
        public Texture3D ProbeSHR { get; private set; }
        public Texture3D ProbeSHG { get; private set; }
        public Texture3D ProbeSHB { get; private set; }
        //Per-probe free distances (+X/+Y/+Z and -X/-Y/-Z), see ProbeVolumeData.DistPos
        public Texture3D ProbeDistPos { get; private set; }
        public Texture3D ProbeDistNeg { get; private set; }

        /// <summary>Probe data currently on the GPU (null when nothing is baked).</summary>
        public ProbeVolumeData GpuData => _uploaded;

        /// <summary>Lightmaps of the active scene on the GPU (Source 2 look, phase 4.2), or null.</summary>
        public LightmapResources Lightmaps { get; private set; }
        private LightmapData _uploadedLightmaps;

        public LightingBakeStatus Status => _status;
        public bool IsBaking => _bakeTask != null;

        /// <summary>Raised on the game thread or the bake worker; handlers must marshal.</summary>
        public event Action<LightingBakeStatus> StatusChanged;

        // ---------------- Bake control ----------------

        public bool StartBake(Scene scene)
        {
            if (scene == null || IsBaking) return false;

            LightingBakeInput input;
            try
            {
                Publish(new LightingBakeStatus(LightingBakeState.Baking, 0, "Gathering scene geometry", null));
                input = LightingBakeInput.Gather(scene, scene.Lighting.BakeLightmaps);
            }
            catch (Exception e)
            {
                EditorBridge.Log("LightingSystem: gather failed: " + e);
                Publish(new LightingBakeStatus(LightingBakeState.Failed, 0, "", "Couldn't read the scene geometry: " + e.Message));
                return false;
            }

            if (input.TriangleCount == 0)
            {
                Publish(new LightingBakeStatus(LightingBakeState.Failed, 0, "", "Nothing to bake: the scene has no meshes."));
                return false;
            }

            LightingSettings settings = scene.Lighting.Clone();
            _bakeScene = scene;
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            EditorBridge.Log($"LightingSystem: bake started — {input.EntityCount} entities, {input.TriangleCount} triangles, " +
                             $"{input.DirectionalLights.Count} directional + {input.PointLights.Count} point lights, " +
                             $"{input.LightmapInstances.Count} lightmapped");
            foreach (string skipped in input.LightmapSkipped)
                EditorBridge.Log("LightingSystem: no lightmap for " + skipped);

            bool lightmaps = settings.BakeLightmaps;
            _bakeTask = Task.Run(() => Bake(input, settings, lightmaps, ct), ct);
            return true;
        }

        private BakeResult Bake(LightingBakeInput input, LightingSettings settings, bool lightmaps, CancellationToken ct)
        {
            //The probes come first: the lightmap gather lights its ray hits with them. Progress: probes, then lightmaps.
            bool withLightmaps = lightmaps && input.LightmapInstances.Count > 0;
            float probeShare = withLightmaps ? 0.35f : 1f;
            OnProgress(0, $"Building BVH ({input.TriangleCount:N0} triangles)");
            var bvh = new TriangleBvh(input.A, input.B, input.C);
            var result = new BakeResult { BakedLightmaps = lightmaps, LightmapSkipped = input.LightmapSkipped };
            result.Probes = ProbeVolumeBaker.Bake(input, settings, (p, stage) => OnProgress(p * probeShare, stage), ct, bvh);
            if (withLightmaps)
                result.Lightmaps = LightmapBaker.Bake(input, settings, bvh, result.Probes,
                    (p, stage) => OnProgress(probeShare + p * (1 - probeShare), stage), ct);
            return result;
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void ClearBake(Scene scene)
        {
            if (scene?.BakedProbes == null && scene?.BakedLightmaps == null) return;
            scene.BakedProbes = null;
            scene.BakedLightmaps = null;
            scene.IsDirty = true;
            Publish(new LightingBakeStatus(LightingBakeState.Idle, 0, "", "Baked lighting cleared."));
        }

        private void OnProgress(float progress, string stage)
        {
            //Called concurrently from Parallel.For; throttle so the editor isn't flooded
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastProgressTicks);
            if (progress < 1 && now - last < ProgressInterval.TotalSeconds * Stopwatch.Frequency) return;
            if (Interlocked.CompareExchange(ref _lastProgressTicks, now, last) != last) return;
            Publish(new LightingBakeStatus(LightingBakeState.Baking, progress, stage, null));
        }

        private void Publish(LightingBakeStatus status)
        {
            _status = status;
            try { StatusChanged?.Invoke(status); }
            catch (Exception e) { EditorBridge.Log("LightingSystem.StatusChanged handler threw: " + e); }
        }

        // ---------------- Per frame (game thread) ----------------

        public void Update(GraphicsDevice graphicsDevice, Scene activeScene)
        {
            if (_bakeTask != null && _bakeTask.IsCompleted)
                FinishBake();

            ProbeVolumeData wanted = activeScene?.BakedProbes;
            if (!ReferenceEquals(wanted, _uploaded))
                Upload(graphicsDevice, wanted);

            LightmapData wantedLightmaps = activeScene?.BakedLightmaps;
            if (!ReferenceEquals(wantedLightmaps, _uploadedLightmaps))
                UploadLightmaps(graphicsDevice, wantedLightmaps);
        }

        private void UploadLightmaps(GraphicsDevice graphicsDevice, LightmapData data)
        {
            Lightmaps?.Dispose();
            Lightmaps = null;
            _uploadedLightmaps = data;
            if (data == null || graphicsDevice == null) return;
            try { Lightmaps = new LightmapResources(graphicsDevice, data); }
            catch (Exception e) { EditorBridge.Log("LightingSystem: lightmap upload failed: " + e); }
        }

        /// <summary>
        /// The lightmap shadow mask channel of a light (one-hot), or zero when it has none: no lightmaps, Baked Shadows off,
        /// not given a channel, or moved since the bake.
        /// </summary>
        public Vector4 ShadowMaskChannel(PointLight light)
        {
            LightmapData data = Lightmaps?.Data;
            if (data == null || light == null || !light.BakedShadows) return Vector4.Zero;
            return data.ChannelOf(light.Id, light.Position, light.Radius);
        }

        public Vector4 ShadowMaskChannel(Entities.DirectionalLight light)
        {
            LightmapData data = Lightmaps?.Data;
            if (data == null || light == null || !light.BakedShadows || light.Direction.LengthSquared() < 1e-8f) return Vector4.Zero;
            return data.ChannelOf(light.Id, -Vector3.Normalize(light.Direction));
        }

        private void FinishBake()
        {
            Task<BakeResult> task = _bakeTask;
            Scene scene = _bakeScene;
            _bakeTask = null;
            _bakeScene = null;
            _cts?.Dispose();
            _cts = null;

            if (task.IsCanceled || (task.IsFaulted && task.Exception?.GetBaseException() is OperationCanceledException))
            {
                Publish(new LightingBakeStatus(LightingBakeState.Cancelled, 0, "", "Bake cancelled."));
                return;
            }
            if (task.IsFaulted)
            {
                Exception e = task.Exception?.GetBaseException();
                EditorBridge.Log("LightingSystem: bake failed: " + e);
                Publish(new LightingBakeStatus(LightingBakeState.Failed, 0, "", "Bake failed: " + e?.Message));
                return;
            }

            BakeResult result = task.Result;
            ProbeVolumeData data = result.Probes;
            scene.BakedProbes = data;
            //A bake with lightmaps on replaces the old ones (none when nothing is Lightmap Static any more)
            if (result.BakedLightmaps) scene.BakedLightmaps = result.Lightmaps;
            scene.IsDirty = true;
            int invalid = 0;
            foreach (byte v in data.Valid) if (v == 0) invalid++;
            string message = $"Baked {data.CountX}×{data.CountY}×{data.CountZ} probes in {data.BakeSeconds:0.0} s" +
                             (invalid > 0 ? $" ({invalid} inside geometry, filled from neighbours)." : ".");
            LightmapData lightmaps = result.Lightmaps;
            if (lightmaps != null)
            {
                message += $" Lightmaps: {lightmaps.Instances.Count} gameobjects, {lightmaps.TexelCount:N0} texels on {lightmaps.Pages.Count} page" +
                           (lightmaps.Pages.Count == 1 ? "" : "s") + $" in {lightmaps.BakeSeconds:0.0} s";
                if (lightmaps.LightsWithoutChannel > 0)
                    message += $" ({lightmaps.LightsWithoutChannel} overlapping static lights got no shadow mask channel)";
                message += ".";
            }
            else if (result.BakedLightmaps && result.LightmapSkipped.Count == 0)
                message += " No Lightmap Static gameobjects, so no lightmaps.";
            if (result.LightmapSkipped.Count > 0)
                message += $" {result.LightmapSkipped.Count} Lightmap Static gameobjects can't be lightmapped (see the console).";
            Publish(new LightingBakeStatus(LightingBakeState.Completed, 1, "", message));
        }

        private void Upload(GraphicsDevice graphicsDevice, ProbeVolumeData data)
        {
            DisposeTextures();
            _uploaded = data;
            if (data == null || graphicsDevice == null) return;

            try
            {
                ProbeSHR = CreateTexture(graphicsDevice, data, data.SHR);
                ProbeSHG = CreateTexture(graphicsDevice, data, data.SHG);
                ProbeSHB = CreateTexture(graphicsDevice, data, data.SHB);
                ProbeDistPos = CreateTexture(graphicsDevice, data, data.DistPos);
                ProbeDistNeg = CreateTexture(graphicsDevice, data, data.DistNeg);
                ProbeData = CreatePackedTexture(graphicsDevice, data);
            }
            catch (Exception e)
            {
                EditorBridge.Log("LightingSystem: probe texture upload failed: " + e);
                DisposeTextures();
            }
        }

        /// <summary>
        /// Every volume's five grids side by side along X, the volumes stacked along Z: the main volume first (slice 0,
        /// what the fog reads), then the detail volumes.
        /// </summary>
        private Texture3D CreatePackedTexture(GraphicsDevice device, ProbeVolumeData main)
        {
            List<ProbeVolumeData> order = main.VolumesByPriority();
            var stacked = new List<ProbeVolumeData> { main };
            foreach (ProbeVolumeData v in order) if (!ReferenceEquals(v, main)) stacked.Add(v);

            int width = 0, height = 0, depth = 0;
            foreach (ProbeVolumeData v in stacked)
            {
                width = Math.Max(width, v.CountX * 5);
                height = Math.Max(height, v.CountY);
                depth += v.CountZ;
            }
            var texture = new Texture3D(device, width, height, depth, false, SurfaceFormat.HalfVector4);
            var packed = new HalfVector4[width * height * depth];
            var offsets = new Dictionary<ProbeVolumeData, int>();
            int zOffset = 0;
            foreach (ProbeVolumeData v in stacked)
            {
                offsets[v] = zOffset;
                Vector4[][] grids = { v.SHR, v.SHG, v.SHB, v.DistPos, v.DistNeg };
                for (int z = 0; z < v.CountZ; z++)
                for (int y = 0; y < v.CountY; y++)
                for (int g = 0; g < grids.Length; g++)
                for (int x = 0; x < v.CountX; x++)
                {
                    int source = (z * v.CountY + y) * v.CountX + x;
                    packed[((zOffset + z) * height + y) * width + g * v.CountX + x] = new HalfVector4(grids[g][source]);
                }
                zOffset += v.CountZ;
            }
            texture.SetData(packed);

            _packed.Clear();
            foreach (ProbeVolumeData v in order) _packed.Add((v, offsets[v]));
            return texture;
        }

        private static Texture3D CreateTexture(GraphicsDevice device, ProbeVolumeData data, Vector4[] values)
        {
            var texture = new Texture3D(device, data.CountX, data.CountY, data.CountZ, false, SurfaceFormat.HalfVector4);
            var packed = new HalfVector4[values.Length];
            for (int i = 0; i < values.Length; i++) packed[i] = new HalfVector4(values[i]);
            texture.SetData(packed);
            return texture;
        }

        // ---------------- Editor debug view ----------------

        /// <summary>Queue the probe positions (coloured by average irradiance) and volume bounds.</summary>
        public void DrawDebug(LightingSettings settings, BoundingBox? previewBounds)
        {
            HelperGeometryManager helpers = HelperGeometryManager.GetInstance();
            ProbeVolumeData data = _uploaded;

            if (data != null)
            {
                DrawBox(helpers, data.BoundsMin, data.BoundsMax, Color.Gold);

                int step = 1;
                while (data.ProbeCount / (step * step * step) > MaxDebugProbes) step++;

                //Size markers relative to the probe spacing so they read at any scene scale
                Vector3 cell = (data.BoundsMax - data.BoundsMin) /
                               new Vector3(data.CountX - 1, data.CountY - 1, data.CountZ - 1);
                float radius = Math.Min(cell.X, Math.Min(cell.Y, cell.Z)) * 0.12f;
                for (int z = 0; z < data.CountZ; z += step)
                for (int y = 0; y < data.CountY; y += step)
                for (int x = 0; x < data.CountX; x += step)
                {
                    int i = data.Index(x, y, z);
                    Vector3 c = new Vector3(data.SHR[i].X, data.SHG[i].X, data.SHB[i].X) * settings.Intensity;
                    //Simple Reinhard so bright probes stay readable; invalid probes tinted magenta
                    c /= Vector3.One + c;
                    Vector4 color = data.Valid[i] != 0 ? new Vector4(c, 1) : new Vector4(1, 0, 1, 1);
                    helpers.AddOctahedron(data.ProbePosition(x, y, z), color, radius);
                }
            }
            else if (previewBounds.HasValue)
            {
                //Nothing baked yet: show where the volume would go
                DrawBox(helpers, previewBounds.Value.Min, previewBounds.Value.Max, Color.Gray);
            }
        }

        private static void DrawBox(HelperGeometryManager helpers, Vector3 min, Vector3 max, Color color)
        {
            Vector3[] c = new BoundingBox(min, max).GetCorners();
            int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int i = 0; i < edges.Length; i += 2)
                helpers.AddLineStartEnd(c[edges[i]], c[edges[i + 1]], 1, color, color);
        }

        private void DisposeTextures()
        {
            ProbeData?.Dispose();
            ProbeData = null;
            ProbeSHR?.Dispose(); ProbeSHR = null;
            ProbeSHG?.Dispose(); ProbeSHG = null;
            ProbeSHB?.Dispose(); ProbeSHB = null;
            ProbeDistPos?.Dispose(); ProbeDistPos = null;
            ProbeDistNeg?.Dispose(); ProbeDistNeg = null;
        }

        public void Dispose()
        {
            Cancel();
            DisposeTextures();
            _uploaded = null;
            Lightmaps?.Dispose();
            Lightmaps = null;
            _uploadedLightmaps = null;
        }
    }
}
