using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BepuPhysics;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Physics;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Voxels
{
    /// <summary>
    /// Keeps the active scene's voxel world (Inspector > Environment > Voxel World) in step with its
    /// <see cref="EnvironmentSettings"/>, once per frame before the physics step (<see cref="MainSceneLogic.UpdatePhysics"/>):
    /// <list type="bullet">
    /// <item>When the settings change it generates and meshes a new world: on a worker thread while editing
    /// (the old world stays until the new one is ready), synchronously during Play so bodies never fall
    /// through missing ground.</item>
    /// <item>Each chunk's surfaces register in the <see cref="MeshMaterialLibrary"/> like a model's mesh parts, so
    /// the G-buffer, shadows, reflections and picking all draw them. Chunks have ID 0 and can't be selected.</item>
    /// <item>Each chunk gets one static BEPU triangle mesh, so bodies, characters and raycasts collide with it.</item>
    /// <item>Script edits (<see cref="VoxelWorld.SetBlock(int, int, int, VoxelBlock)"/>) remesh the touched chunks.
    /// Stop regenerates a world that Play changed.</item>
    /// </list>
    /// Without a graphics device or physics (GPU-free checks) it keeps the world data only. Game thread only.
    /// </summary>
    public sealed class VoxelSystem : IDisposable
    {
        private readonly GraphicsDevice _graphics;
        private readonly MeshMaterialLibrary _library;
        private readonly PhysicsSystem _physics;

        private Scene _scene;
        // The settings of the world being built or shown; null when there is none.
        private VoxelWorldSettings? _requested;
        private Task<BuildResult> _pending;
        private CancellationTokenSource _cancel;
        private bool _regenerate;
        private Chunk[] _chunks = Array.Empty<Chunk>();
        private readonly List<int> _dirty = new List<int>();
        // Transforms flagged as moved this frame (new or remeshed chunks), so shadows redraw; cleared next frame.
        private readonly List<TransformMatrix> _changed = new List<TransformMatrix>();

        private MaterialEffect[] _materials;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private Sea _sea;

        private sealed class Chunk
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public TransformMatrix Transform;
            public readonly List<ModelMeshPart> Parts = new List<ModelMeshPart>();
            public StaticHandle? Collider;
            public int Triangles;
            public int ColliderTriangles;
        }

        private sealed class Sea
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public ModelMeshPart Part;
            public MaterialEffect Material;
            public TransformMatrix Transform;
            public bool Registered;
        }

        private sealed record BuildResult(VoxelWorldSettings Settings, VoxelWorld World, VoxelChunkMesh[] Meshes, double Milliseconds);

        /// <param name="graphics">Null keeps the world data only (no drawing).</param>
        /// <param name="library">Where chunks register to be drawn; null for none.</param>
        /// <param name="physics">Where chunk colliders go; null for none.</param>
        public VoxelSystem(GraphicsDevice graphics, MeshMaterialLibrary library, PhysicsSystem physics)
        {
            _graphics = graphics;
            _library = library;
            _physics = physics;
        }

        /// <summary>The voxel world of the active scene; null while it has none or the first one is being built.</summary>
        public VoxelWorld World { get; private set; }
        /// <summary>True while a new world is being generated on a worker thread.</summary>
        public bool IsGenerating => _pending != null;
        /// <summary>Counts finished worlds, so scripts can tell that the world was replaced.</summary>
        public int Generation { get; private set; }
        /// <summary>Time the last world took to generate and mesh, in milliseconds.</summary>
        public double LastBuildMilliseconds { get; private set; }
        /// <summary>Render triangles of every chunk.</summary>
        public int TriangleCount { get; private set; }
        /// <summary>Collision triangles of every chunk (faces merged regardless of look).</summary>
        public int ColliderTriangleCount { get; private set; }
        /// <summary>Chunks that have any faces.</summary>
        public int VisibleChunkCount { get; private set; }
        /// <summary>Chunk mesh parts registered for drawing (one per surface per chunk).</summary>
        public int DrawPartCount { get; private set; }
        /// <summary>Why the last build failed, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Per-frame sync: called before the physics step.</summary>
        public void Update(Scene scene, bool playing)
        {
            for (int i = 0; i < _changed.Count; i++) _changed[i].HasChanged = false;
            _changed.Clear();
            Sync(scene, playing);
            if (World != null && World.DirtyChunkCount > 0) RemeshDirty();
        }

        /// <summary>
        /// The world for <paramref name="scene"/>; during Play a world that is missing or still building is built
        /// now, so scripts that start with the scene can use it straight away. Edits are remeshed at the next
        /// <see cref="Update"/>, not here.
        /// </summary>
        public VoxelWorld Ensure(Scene scene, bool playing)
        {
            if (playing) Sync(scene, true);
            return ReferenceEquals(scene, _scene) ? World : null;
        }

        /// <summary>Stop: a world that scripts changed during Play is generated afresh.</summary>
        public void OnPlayStopped()
        {
            if (World != null && World.EditCount > 0) _regenerate = true;
        }

        /// <summary>Drops the world, its meshes and colliders (scene swap, voxel world switched off).</summary>
        public void Reset()
        {
            CancelPending();
            ReleaseChunks();
            UnregisterSea();
            World = null;
            _requested = null;
            _regenerate = false;
            UpdateStats();
        }

        private void Sync(Scene scene, bool playing)
        {
            if (!ReferenceEquals(scene, _scene))
            {
                Reset();
                _scene = scene;
            }
            VoxelWorldSettings? wanted = VoxelWorldSettings.From(scene?.Environment);
            if (wanted == null)
            {
                if (World != null || _requested != null) Reset();
                return;
            }
            if (_regenerate || _requested != wanted)
            {
                _regenerate = false;
                StartBuild(wanted.Value, playing);
            }
            if (_pending == null) return;
            if (playing && !_pending.IsCompleted)
            {
                try { _pending.Wait(); }
                catch (AggregateException) { /* reported by FinishBuild */ }
            }
            if (_pending.IsCompleted) FinishBuild();
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  BUILD
        ////////////////////////////////////////////////////////////////////////////////

        private void StartBuild(VoxelWorldSettings settings, bool synchronous)
        {
            CancelPending();
            _requested = settings;
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            if (synchronous)
            {
                try { _pending = Task.FromResult(Build(settings, cancel.Token)); }
                catch (Exception ex) { _pending = Task.FromException<BuildResult>(ex); }
            }
            else _pending = Task.Run(() => Build(settings, cancel.Token), cancel.Token);
        }

        private void CancelPending()
        {
            _cancel?.Cancel();
            _cancel = null;
            _pending = null;
        }

        private static BuildResult Build(VoxelWorldSettings settings, CancellationToken cancel)
        {
            var timer = Stopwatch.StartNew();
            VoxelWorld world = VoxelGenerator.Generate(settings, cancel);
            var meshes = new VoxelChunkMesh[world.ChunkCount];
            Parallel.For(0, meshes.Length, new ParallelOptions { CancellationToken = cancel },
                i => meshes[i] = VoxelMesher.Build(world, i));
            return new BuildResult(settings, world, meshes, timer.Elapsed.TotalMilliseconds);
        }

        private void FinishBuild()
        {
            Task<BuildResult> task = _pending;
            _pending = null;
            if (task.IsCanceled) return;
            if (task.IsFaulted)
            {
                Exception error = task.Exception?.GetBaseException();
                if (error is OperationCanceledException) return;
                LastError = error?.Message;
                EditorBridge.Log("Voxels: generating the world failed: " + error);
                return;
            }
            BuildResult result = task.Result;
            // A newer request replaced this one while it was building.
            if (result.Settings != _requested) return;
            Apply(result);
        }

        private void Apply(BuildResult result)
        {
            ReleaseChunks();
            World = result.World;
            _chunks = new Chunk[World.ChunkCount];
            for (int i = 0; i < _chunks.Length; i++) _chunks[i] = Upload(result.Meshes[i]);
            UpdateSea();
            Generation++;
            LastError = null;
            LastBuildMilliseconds = result.Milliseconds;
            UpdateStats();
            // Reflections were captured without (or with the old) terrain.
            if (_scene?.EnvironmentSample != null) _scene.EnvironmentSample.NeedsUpdate = true;
            EditorBridge.Log($"Voxels: {World.SizeX}x{World.SizeY}x{World.SizeZ} {World.Settings.Terrain} world, " +
                             $"{VisibleChunkCount} chunks, {TriangleCount} triangles in {LastBuildMilliseconds:0} ms");
        }

        private void RemeshDirty()
        {
            World.TakeDirtyChunks(_dirty);
            foreach (int index in _dirty)
            {
                VoxelChunkMesh mesh = VoxelMesher.Build(World, index);
                Release(_chunks[index]);
                _chunks[index] = Upload(mesh);
            }
            UpdateStats();
        }

        private void UpdateStats()
        {
            int triangles = 0, colliders = 0, visible = 0, parts = 0;
            foreach (Chunk chunk in _chunks)
            {
                if (chunk == null) continue;
                triangles += chunk.Triangles;
                colliders += chunk.ColliderTriangles;
                parts += chunk.Parts.Count;
                if (chunk.Triangles > 0) visible++;
            }
            TriangleCount = triangles;
            ColliderTriangleCount = colliders;
            VisibleChunkCount = visible;
            DrawPartCount = parts;
        }

        ////////////////////////////////////////////////////////////////////////////////
        //  GPU AND PHYSICS
        ////////////////////////////////////////////////////////////////////////////////

        private Chunk Upload(VoxelChunkMesh mesh)
        {
            var chunk = new Chunk { Triangles = mesh.TriangleCount, ColliderTriangles = mesh.ColliderIndices.Length / 3 };
            if (_graphics != null && _library != null && !mesh.IsEmpty)
            {
                EnsureMaterials();
                chunk.Vertices = new VertexBuffer(_graphics, VoxelVertex.Declaration, mesh.Vertices.Length, BufferUsage.WriteOnly);
                chunk.Vertices.SetData(mesh.Vertices);
                chunk.Indices = new IndexBuffer(_graphics, IndexElementSize.ThirtyTwoBits, mesh.Indices.Length, BufferUsage.WriteOnly);
                chunk.Indices.SetData(mesh.Indices);
                // Vertices are in world space. Scale feeds the culling sphere's radius, so it must be 1.
                chunk.Transform = new TransformMatrix(Matrix.Identity, 0)
                {
                    InverseWorld = Matrix.Identity,
                    Scale = Vector3.One,
                    HasChanged = true,
                };
                foreach (VoxelMeshPart part in mesh.Parts)
                {
                    // MonoGame marks this constructor obsolete, but it is the only way to make a part outside the
                    // content pipeline, and the mesh library draws parts.
#pragma warning disable CS0618
                    var meshPart = new ModelMeshPart
#pragma warning restore CS0618
                    {
                        VertexBuffer = chunk.Vertices,
                        IndexBuffer = chunk.Indices,
                        NumVertices = mesh.Vertices.Length,
                        StartIndex = part.StartIndex,
                        PrimitiveCount = part.PrimitiveCount,
                    };
                    _library.Register(_materials[(int)part.Surface], meshPart, chunk.Transform, mesh.Bounds);
                    chunk.Parts.Add(meshPart);
                }
                _changed.Add(chunk.Transform);
            }
            if (_physics != null && World.Settings.Collision && mesh.ColliderIndices.Length > 0)
                chunk.Collider = _physics.AddStaticMesh(mesh.ColliderVertices, mesh.ColliderIndices, Vector3.Zero,
                    Quaternion.Identity, Vector3.One);
            return chunk;
        }

        private void Release(Chunk chunk)
        {
            if (chunk == null) return;
            foreach (ModelMeshPart part in chunk.Parts) _library?.Unregister(part, chunk.Transform);
            chunk.Parts.Clear();
            chunk.Vertices?.Dispose();
            chunk.Indices?.Dispose();
            if (chunk.Collider != null) _physics?.RemoveStatic(chunk.Collider.Value);
            chunk.Collider = null;
        }

        private void ReleaseChunks()
        {
            foreach (Chunk chunk in _chunks) Release(chunk);
            _chunks = Array.Empty<Chunk>();
        }

        private void EnsureMaterials()
        {
            if (_materials != null) return;
            _materials = new MaterialEffect[VoxelBlocks.SurfaceCount];
            for (int i = 0; i < _materials.Length; i++)
            {
                var surface = (VoxelSurface)i;
                VoxelSurfaceStyle style = VoxelSurfaceStyle.For(surface);
                VoxelTextures.Create(_graphics, surface, out Texture2D albedo, out Texture2D normal);
                _textures.Add(albedo);
                _textures.Add(normal);
                var material = new MaterialEffect(Shaders.DeferredClear);
                // Emissive surfaces carry their strength in the metallic channel (DeferredCompose: albedo x metallic x 8).
                material.Initialize(Color.White, style.Roughness, style.Glow > 0 ? style.Glow / 8 : 0,
                    albedoMap: albedo, normalMap: normal, type: style.Type, emissiveStrength: style.Glow);
                material.WeatherWetness = style.RainWet;
                material.PuddleAmount = style.Puddles ? 0.35f : 0;
                material.PuddleSize = 3;
                _materials[i] = material;
            }
        }

        // The sea: one unit quad scaled over the world, drawn by the water pass (which subdivides it for waves).
        private void UpdateSea()
        {
            if (World == null || !World.HasSea || _graphics == null || _library == null)
            {
                UnregisterSea();
                return;
            }
            EnsureSea();
            float width = World.SizeX * World.BlockSize, depth = World.SizeY * World.BlockSize;
            float z = World.SeaSurfaceZ;
            _sea.Transform.World = Matrix.CreateScale(width, depth, 1) * Matrix.CreateTranslation(World.Origin.X, World.Origin.Y, z);
            _sea.Transform.InverseWorld = Matrix.Invert(_sea.Transform.World);
            _sea.Transform.Scale = new Vector3(Math.Max(width, depth), Math.Max(width, depth), 1);
            _sea.Transform.HasChanged = true;
            _changed.Add(_sea.Transform);
            if (!_sea.Registered)
            {
                _library.Register(_sea.Material, _sea.Part, _sea.Transform,
                    new BoundingSphere(new Vector3(0.5f, 0.5f, 0), MathF.Sqrt(0.5f)));
                _sea.Registered = true;
            }
        }

        private void EnsureSea()
        {
            if (_sea != null) return;
            var vertices = new VertexPositionNormalTexture[4];
            Vector3[] corners = { new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0) };
            for (int i = 0; i < 4; i++)
                vertices[i] = new VertexPositionNormalTexture(corners[i], Vector3.UnitZ, new Vector2(corners[i].X, corners[i].Y));
            // Clockwise seen from above.
            int[] indices = { 0, 2, 1, 0, 3, 2 };
            _sea = new Sea
            {
                Vertices = new VertexBuffer(_graphics, VertexPositionNormalTexture.VertexDeclaration, 4, BufferUsage.None),
                Indices = new IndexBuffer(_graphics, IndexElementSize.ThirtyTwoBits, 6, BufferUsage.None),
                Transform = new TransformMatrix(Matrix.Identity, 0) { Scale = Vector3.One },
            };
            // Readable buffers (BufferUsage.None): the water pass reads them back to subdivide the quad.
            _sea.Vertices.SetData(vertices);
            _sea.Indices.SetData(indices);
#pragma warning disable CS0618 // see Upload
            _sea.Part = new ModelMeshPart { VertexBuffer = _sea.Vertices, IndexBuffer = _sea.Indices, NumVertices = 4, PrimitiveCount = 2 };
#pragma warning restore CS0618
            _sea.Material = new MaterialEffect(Shaders.DeferredClear);
            _sea.Material.Initialize(new Color(0.05f, 0.17f, 0.22f), 0.05f, 0, type: MaterialEffect.MaterialTypes.Water);
            _sea.Material.IsTransparent = true;
            _sea.Material.HasShadow = false;
            _sea.Material.Opacity = 0.82f;
            _sea.Material.WaveScale = 1.0f;
            _sea.Material.WaveSpeed = 0.8f;
            _sea.Material.WaveStrength = 0.3f;
            _sea.Material.WaveHeight = 0.08f;
            _sea.Material.WaterClarity = 1.8f;
            _sea.Material.WaterFoam = 0.35f;
        }

        private void UnregisterSea()
        {
            if (_sea == null || !_sea.Registered) return;
            _library?.Unregister(_sea.Part, _sea.Transform);
            _sea.Registered = false;
        }

        public void Dispose()
        {
            Reset();
            if (_sea != null)
            {
                _sea.Vertices.Dispose();
                _sea.Indices.Dispose();
                _sea.Material.Dispose();
                _sea = null;
            }
            if (_materials != null)
                foreach (MaterialEffect material in _materials) material.Dispose();
            _materials = null;
            foreach (Texture2D texture in _textures) texture.Dispose();
            _textures.Clear();
        }
    }
}
