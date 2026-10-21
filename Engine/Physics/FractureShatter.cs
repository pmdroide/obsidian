using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepuPhysics;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Engine.Renderer.Helper;
using Engine.Voxels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Physics
{
    /// <summary>
    /// A breakable gameobject during Play (<see cref="Components.BreakableComponent"/>): its model is fractured into
    /// <see cref="MeshFracture"/> pieces on a worker thread when Play starts, and <see cref="Break"/> swaps the
    /// gameobject for them: each piece is a dynamic convex body with its own meshes (the model's materials outside,
    /// the interior material on the cut faces), thrown out from the hit, fading out after its lifetime. The scene
    /// keeps at most <see cref="MaxPieces"/> pieces (the oldest go first). Created through
    /// <see cref="ScenePhysics.CreateFracture"/>; game thread only (the fracture itself runs on a worker).
    /// </summary>
    public sealed class FractureShatter : IDisposable
    {
        /// <summary>Pieces alive in the whole scene before the oldest are removed.</summary>
        public static int MaxPieces = 200;
        /// <summary>Models with more triangles than this aren't fractured (the break just hides them).</summary>
        public const int MaxSourceTriangles = 40000;
        private const float FadeTime = 0.5f;
        private const float MaxPieceSpeed = 80f;
        // Collider points per piece (the hull of a few hundred cap vertices is the same shape).
        private const int MaxColliderPoints = 64;
        // Pieces thinner than this (m) get a box collider of this size instead of a flat hull.
        private const float MinColliderSize = 0.01f;

        private sealed class Piece
        {
            public FractureShatter Owner;
            public BodyHandle Body;
            public Vector3 LocalCenter;
            public readonly List<(ModelMeshPart Part, VertexBuffer Vertices, IndexBuffer Indices)> Parts = new();
            public TransformMatrix Transform;
            public float Radius;
            public float Age;
        }

        // Pieces of every breakable, oldest first (for MaxPieces).
        private static readonly List<Piece> AllPieces = new();

        private readonly ScenePhysics _scene;
        private readonly PhysicsSystem _physics;
        private readonly MeshMaterialLibrary _library;
        private readonly GraphicsDevice _device;
        private readonly int _collisionGroup;
        private readonly Random _random;
        // Material of each model part (the group index of the fracture mesh), and of the cut faces.
        private readonly List<MaterialEffect> _partMaterials = new();
        private MaterialEffect _interior;
        private bool _ownsInterior;
        private readonly List<Piece> _pieces = new();
        private Task<List<FracturePiece>> _fracture;
        private int _preparedCount;
        private Vector3 _preparedScale;
        private bool _hidOwner;

        public BasicEntity Owner { get; }
        /// <summary>Broken: the gameobject is hidden, its collider gone, its pieces flying.</summary>
        public bool IsBroken { get; private set; }
        public bool IsDisposed { get; private set; }
        /// <summary>The fracture has been computed (pieces are ready to throw).</summary>
        public bool IsReady => _fracture is { IsCompleted: true };
        /// <summary>Pieces of this gameobject still in the scene.</summary>
        public int PieceCount => _pieces.Count;

        // Settings, copied from the component every frame.
        public float DebrisLifetime = 10f;
        public float Density = 1500f;
        public Color InteriorColor = new(200, 190, 175);
        public float InteriorRoughness = 0.85f;
        /// <summary>
        /// Collision group the pieces join instead of their own (0 = their own): a head's pieces ignore the body it
        /// rode on, whose ragdoll would otherwise blast them out of its head capsule.
        /// </summary>
        public int SharedCollisionGroup;

        /// <summary>A contact with the gameobject (seen from it), the other gameobject and its mass (0 = immovable).</summary>
        public Action<PhysicsContact, BasicEntity, float> Hit;
        // A break queued from a contact, applied after the step's contacts are read (ApplyPending).
        private Action _pending;

        internal FractureShatter(ScenePhysics scene, PhysicsSystem physics, BasicEntity owner, MeshMaterialLibrary library, int collisionGroup)
        {
            _scene = scene;
            _physics = physics;
            Owner = owner;
            _library = library;
            _collisionGroup = collisionGroup;
            _random = new Random(owner.Id * 7919 + 31);
            _device = FindDevice(owner.Model);
            if (owner.Model != null)
            {
                int index = 0;
                foreach (ModelMesh mesh in owner.Model.Meshes)
                    foreach (ModelMeshPart part in mesh.MeshParts)
                        _partMaterials.Add(owner.PartMaterial(part, index++));
            }
        }

        /// <summary>
        /// Starts fracturing the model into about <paramref name="count"/> pieces on a worker thread (again when the
        /// count or the gameobject's scale changed). Reads the model's buffers here, on the game thread.
        /// </summary>
        public void Prepare(int count)
        {
            if (IsDisposed || IsBroken) return;
            if (_fracture != null && _preparedCount == count && _preparedScale == Owner.Scale) return;
            _preparedCount = count;
            _preparedScale = Owner.Scale;
            FractureMesh mesh = ReadMesh(Owner);
            // Without readable buffers (or a model) it breaks as its bounding box.
            if (mesh == null || mesh.TriangleCount == 0) mesh = BoxMesh(Owner);
            if (mesh.TriangleCount > MaxSourceTriangles)
            {
                EditorBridge.Log($"Breakable '{Owner.Name}': {mesh.TriangleCount} triangles is too many to fracture (max {MaxSourceTriangles}).");
                _fracture = Task.FromResult(new List<FracturePiece>());
                return;
            }
            int seed = _random.Next();
            string name = Owner.Name;
            _fracture = Task.Run(() =>
            {
                try
                {
                    Vector3 size = Bounds(mesh);
                    // Sites sit up to a fifth of the object's smallest side under its skin.
                    float depth = Math.Max(Math.Min(size.X, Math.Min(size.Y, size.Z)) * 0.2f, 0.002f);
                    List<Vector3> sites = MeshFracture.SurfaceSites(mesh, count, new Random(seed), depth);
                    return MeshFracture.Fracture(mesh, sites, 4f);
                }
                catch (Exception ex)
                {
                    EditorBridge.Log($"Breakable '{name}': fracturing failed: {ex.Message}");
                    return new List<FracturePiece>();
                }
            });
        }

        /// <summary>Breaks after the physics step (contacts are being read now).</summary>
        internal void Queue(Action breakNow) => _pending = breakNow;

        internal void ApplyPending()
        {
            Action pending = _pending;
            _pending = null;
            pending?.Invoke();
        }

        /// <summary>
        /// Swaps the gameobject for its pieces. They start with <paramref name="inheritedLinear"/>/<paramref name="inheritedAngular"/>
        /// (the gameobject's own motion) and fly out from <paramref name="point"/> at about <paramref name="force"/> m/s,
        /// leaning along <paramref name="direction"/>. Returns the number of pieces thrown.
        /// </summary>
        public int Break(Vector3 point, Vector3 direction, float force, Vector3 inheritedLinear, Vector3 inheritedAngular)
        {
            if (IsDisposed || IsBroken) return 0;
            if (_fracture == null) Prepare(Math.Max(_preparedCount, 2));
            List<FracturePiece> fractured;
            try { fractured = _fracture.Result; } // usually done long ago; a break right after Play starts waits for it
            catch (Exception ex)
            {
                EditorBridge.Log($"Breakable '{Owner.Name}': fracturing failed: {ex.Message}");
                fractured = new List<FracturePiece>();
            }

            // A dynamic gameobject keeps its mass; anything else weighs its volume x Density.
            float ownMass = Owner.AttachedPhysicsType == PhysicsBodyType.Dynamic && Owner.AttachedMass > 0 ? Owner.AttachedMass : 0;
            IsBroken = true;
            // The collider goes now, so the pieces don't spawn inside it (Reconcile keeps it gone while broken).
            _scene.Detach(Owner);
            if (!Owner.WorldTransform.Hidden)
            {
                Owner.WorldTransform.Hidden = true;
                Owner.WorldTransform.HasChanged = true;
                _hidOwner = true;
            }

            Quaternion orientation = Quaternion.CreateFromRotationMatrix(Owner.RotationMatrix);
            Vector3 center = Vector3.Zero;
            float totalVolume = 0;
            foreach (FracturePiece piece in fractured)
            {
                center += piece.Center * piece.Volume;
                totalVolume += piece.Volume;
            }
            center = totalVolume > 0 ? center / totalVolume : Vector3.Zero;
            Vector3 worldCenter = Owner.Position + Vector3.Transform(center, orientation);
            Vector3 along = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.Zero;
            float totalMass = ownMass > 0 ? ownMass : Math.Max(totalVolume * Math.Max(Density, 1f), 0.05f);
            float size = MathF.Cbrt(Math.Max(totalVolume, 1e-9f));

            int thrown = 0;
            foreach (FracturePiece fracturePiece in fractured)
            {
                Vector3 position = Owner.Position + Vector3.Transform(fracturePiece.Center, orientation);
                // Out from the hit (or the middle), harder for pieces near it, plus a share along the hit.
                Vector3 away = position - (IsFinite(point) ? point : worldCenter);
                float distance = away.Length();
                away = distance > 1e-4f ? away / distance : RandomUnit();
                float near = 1f / (1f + distance / Math.Max(size, 0.02f));
                Vector3 linear = inheritedLinear + Vector3.Cross(inheritedAngular, position - worldCenter)
                    + away * force * (0.5f + near) * GlassFracture.Range(_random, 0.6f, 1.15f)
                    + along * force * 0.4f * GlassFracture.Range(_random, 0.3f, 1f);
                Vector3 angular = inheritedAngular + RandomUnit() * force * GlassFracture.Range(_random, 1.5f, 4f);
                float mass = Math.Max(totalMass * fracturePiece.Volume / Math.Max(totalVolume, 1e-12f), 0.005f);
                if (Spawn(fracturePiece, position, orientation, mass, linear, angular)) thrown++;
            }
            return thrown;
        }

        private bool Spawn(FracturePiece source, Vector3 position, Quaternion orientation, float mass, Vector3 linear, Vector3 angular)
        {
            FractureMesh mesh = source.Mesh;
            Vector3[] hull = ColliderPoints(mesh, source.Center);
            BodyHandle body;
            Vector3 localCenter;
            try { body = _physics.AddDynamicConvex(hull, position, orientation, mass, out localCenter); }
            catch (Exception ex)
            {
                EditorBridge.Log($"Breakable '{Owner.Name}': couldn't create a piece body: {ex.Message}");
                return false;
            }
            _physics.SetCollisionGroup(body, SharedCollisionGroup != 0 ? SharedCollisionGroup : _collisionGroup);
            if (linear.LengthSquared() > MaxPieceSpeed * MaxPieceSpeed) linear = Vector3.Normalize(linear) * MaxPieceSpeed;
            _physics.SetBodyVelocity(body, linear, angular);

            var piece = new Piece { Owner = this, Body = body, LocalCenter = localCenter };
            foreach (FractureVertex v in mesh.Vertices) piece.Radius = Math.Max(piece.Radius, (v.Position - source.Center).Length());
            if (_device != null && _library != null)
            {
                piece.Transform = new TransformMatrix(PieceWorld(piece, 1), Owner.Id) { Scale = Vector3.One, HasChanged = true };
                piece.Transform.InverseWorld = Matrix.Invert(piece.Transform.World);
                var groups = new SortedSet<int>(mesh.Groups);
                foreach (int group in groups)
                {
                    MaterialEffect material = group == MeshFracture.InteriorGroup ? InteriorMaterial(group) : MaterialOf(group);
                    if (material == null) continue;
                    ModelMeshPart part = Upload(mesh, group, source.Center, out VertexBuffer vertices, out IndexBuffer indices);
                    if (part == null) continue;
                    piece.Parts.Add((part, vertices, indices));
                    _library.Register(material, part, piece.Transform, new BoundingSphere(Vector3.Zero, piece.Radius));
                }
            }
            _pieces.Add(piece);
            AllPieces.Add(piece);
            while (AllPieces.Count > Math.Max(MaxPieces, 0)) AllPieces[0].Owner.RemovePiece(AllPieces[0]);
            return true;
        }

        /// <summary>Moves the pieces to their bodies, ages and removes them. Called by ScenePhysics after each step.</summary>
        internal void AfterStep(float dt)
        {
            float lifetime = Math.Max(DebrisLifetime, 0.1f);
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                Piece piece = _pieces[i];
                piece.Age += dt;
                if (piece.Age >= lifetime)
                {
                    RemovePiece(piece);
                    continue;
                }
                // A body the solver lost (non-finite or flung away) would poison the broad phase next step.
                _physics.GetBodyState(piece.Body, out Vector3 position, out Quaternion orientation, out Vector3 velocity, out _);
                if (!IsFinite(position) || !float.IsFinite(orientation.W) || !IsFinite(velocity)
                    || velocity.LengthSquared() > MaxPieceSpeed * MaxPieceSpeed * 4)
                {
                    RemovePiece(piece);
                    continue;
                }
                if (piece.Transform == null) continue;
                float scale = Math.Clamp((lifetime - piece.Age) / FadeTime, 0, 1);
                Matrix world = PieceWorld(piece, scale);
                if (world == piece.Transform.World) continue;
                piece.Transform.World = world;
                piece.Transform.InverseWorld = Matrix.Invert(world);
                piece.Transform.Scale = new Vector3(Math.Max(scale, 1e-3f));
                piece.Transform.HasChanged = true;
            }
        }

        /// <summary>Clears last frame's moved flags (the renderer has drawn them).</summary>
        internal void BeginFrame()
        {
            foreach (Piece piece in _pieces)
                if (piece.Transform != null) piece.Transform.HasChanged = false;
        }

        private Matrix PieceWorld(Piece piece, float scale)
        {
            _physics.GetBodyPose(piece.Body, out Vector3 position, out Quaternion orientation);
            return Matrix.CreateTranslation(-piece.LocalCenter) * Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(orientation)
                * Matrix.CreateTranslation(position);
        }

        private void RemovePiece(Piece piece)
        {
            if (!_pieces.Remove(piece)) return;
            AllPieces.Remove(piece);
            try { _physics.RemoveDynamic(piece.Body); }
            catch (Exception ex) { EditorBridge.Log($"Breakable: removing a piece body threw: {ex.Message}"); }
            foreach (var (part, vertices, indices) in piece.Parts)
            {
                _library?.Unregister(part, piece.Transform);
                vertices.Dispose();
                indices.Dispose();
            }
            piece.Parts.Clear();
        }

        private MaterialEffect MaterialOf(int group)
        {
            if (group >= 0 && group < _partMaterials.Count && _partMaterials[group] != null) return _partMaterials[group];
            return Owner.DrawMaterial;
        }

        // The cut faces: a see-through surface (glass) stays itself inside; anything else gets a plain interior colour.
        private MaterialEffect InteriorMaterial(int group)
        {
            if (_interior != null) return _interior;
            MaterialEffect surface = MaterialOf(0);
            if (surface != null && (surface.Type == MaterialEffect.MaterialTypes.ForwardShaded || surface.IsTransparent))
                return _interior = surface;
            _interior = new MaterialEffect(Shaders.DeferredClear);
            _interior.Initialize(InteriorColor, Math.Clamp(InteriorRoughness, 0.05f, 1f), 0);
            _ownsInterior = true;
            return _interior;
        }

        private ModelMeshPart Upload(FractureMesh mesh, int group, Vector3 offset, out VertexBuffer vertexBuffer, out IndexBuffer indexBuffer)
        {
            vertexBuffer = null;
            indexBuffer = null;
            var map = new Dictionary<int, int>();
            var vertices = new List<VoxelVertex>();
            var indices = new List<int>();
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                if (mesh.Groups[t] != group) continue;
                for (int k = 0; k < 3; k++)
                {
                    int source = mesh.Indices[t * 3 + k];
                    if (!map.TryGetValue(source, out int index))
                    {
                        FractureVertex v = mesh.Vertices[source];
                        Vector3 normal = v.Normal.LengthSquared() > 1e-8f ? v.Normal : Vector3.UnitZ;
                        Vector3 tangent = v.Tangent.LengthSquared() > 1e-8f ? v.Tangent : Vector3.UnitX;
                        index = vertices.Count;
                        vertices.Add(new VoxelVertex
                        {
                            Position = v.Position - offset,
                            Normal = normal,
                            TextureCoordinate = v.TextureCoordinate,
                            Tangent = tangent,
                            Binormal = Vector3.Cross(normal, tangent),
                        });
                        map[source] = index;
                    }
                    indices.Add(index);
                }
            }
            if (indices.Count < 3) return null;
            vertexBuffer = new VertexBuffer(_device, VoxelVertex.Declaration, vertices.Count, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices.ToArray());
            indexBuffer = new IndexBuffer(_device, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices.ToArray());
            // MonoGame marks this constructor obsolete, but it is the only way to make a part outside the content pipeline.
#pragma warning disable CS0618
            return new ModelMeshPart
#pragma warning restore CS0618
            {
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                NumVertices = vertices.Count,
                StartIndex = 0,
                PrimitiveCount = indices.Count / 3,
            };
        }

        /// <summary>Up to <see cref="MaxColliderPoints"/> of the piece's corners around its centre; a sliver gets a small box.</summary>
        private static Vector3[] ColliderPoints(FractureMesh mesh, Vector3 center)
        {
            var unique = new List<Vector3>();
            var seen = new HashSet<(int, int, int)>();
            foreach (FractureVertex v in mesh.Vertices)
            {
                Vector3 p = v.Position - center;
                if (seen.Add(((int)MathF.Round(p.X * 2000), (int)MathF.Round(p.Y * 2000), (int)MathF.Round(p.Z * 2000)))) unique.Add(p);
            }
            if (unique.Count > MaxColliderPoints)
            {
                // Keep the farthest point in many directions: the hull of those is close to the full one.
                var kept = new List<Vector3>(MaxColliderPoints);
                for (int i = 0; i < MaxColliderPoints; i++)
                {
                    // Fibonacci sphere directions.
                    float z = 1 - 2 * (i + 0.5f) / MaxColliderPoints, r = MathF.Sqrt(1 - z * z), a = i * 2.39996323f;
                    var direction = new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
                    Vector3 best = unique[0];
                    float most = float.MinValue;
                    foreach (Vector3 p in unique)
                    {
                        float d = Vector3.Dot(p, direction);
                        if (d > most) { most = d; best = p; }
                    }
                    if (!kept.Contains(best)) kept.Add(best);
                }
                unique = kept;
            }
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Vector3 p in unique)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 extent = max - min;
            if (unique.Count < 4 || Math.Min(extent.X, Math.Min(extent.Y, extent.Z)) < MinColliderSize * 0.25f)
            {
                // Too flat for a hull: a box around it, at least MinColliderSize thick.
                Vector3 mid = (min + max) * 0.5f, half = Vector3.Max(extent * 0.5f, new Vector3(MinColliderSize * 0.5f));
                var box = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    box[i] = mid + new Vector3((i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z);
                return box;
            }
            return unique.ToArray();
        }

        /// <summary>
        /// The model as one closed mesh in the gameobject's scaled local space (positions, normals, tangents, texture
        /// coordinates; the group is the part index). Null when the buffers can't be read.
        /// </summary>
        internal static FractureMesh ReadMesh(BasicEntity owner)
        {
            if (owner?.Model == null) return null;
            var mesh = new FractureMesh();
            Vector3 scale = owner.Scale;
            var inverseScale = new Vector3(SafeInverse(scale.X), SafeInverse(scale.Y), SafeInverse(scale.Z));
            try
            {
                int group = 0;
                foreach (ModelMesh modelMesh in owner.Model.Meshes)
                    foreach (ModelMeshPart part in modelMesh.MeshParts)
                    {
                        int partGroup = group++;
                        VertexBuffer buffer = part.VertexBuffer;
                        IndexBuffer indexBuffer = part.IndexBuffer;
                        if (buffer == null || indexBuffer == null || part.PrimitiveCount <= 0) continue;
                        int position = -1, normal = -1, texture = -1, tangent = -1;
                        foreach (VertexElement element in buffer.VertexDeclaration.GetVertexElements())
                        {
                            if (element.UsageIndex != 0 || element.VertexElementFormat != VertexElementFormat.Vector3
                                && element.VertexElementUsage != VertexElementUsage.TextureCoordinate) continue;
                            switch (element.VertexElementUsage)
                            {
                                case VertexElementUsage.Position: position = element.Offset; break;
                                case VertexElementUsage.Normal: normal = element.Offset; break;
                                case VertexElementUsage.Tangent: tangent = element.Offset; break;
                                case VertexElementUsage.TextureCoordinate:
                                    if (element.VertexElementFormat == VertexElementFormat.Vector2) texture = element.Offset;
                                    break;
                            }
                        }
                        if (position < 0) continue;
                        int stride = buffer.VertexDeclaration.VertexStride;
                        int first = part.VertexOffset, count = part.NumVertices;
                        if (count <= 0 || first + count > buffer.VertexCount) continue;
                        var data = new byte[count * stride];
                        buffer.GetData(first * stride, data, 0, data.Length, 1);

                        int indexCount = part.PrimitiveCount * 3;
                        var indices = new int[indexCount];
                        if (indexBuffer.IndexElementSize == IndexElementSize.SixteenBits)
                        {
                            var shorts = new ushort[indexCount];
                            indexBuffer.GetData(part.StartIndex * 2, shorts, 0, indexCount);
                            for (int i = 0; i < indexCount; i++) indices[i] = shorts[i];
                        }
                        else indexBuffer.GetData(part.StartIndex * 4, indices, 0, indexCount);

                        int baseVertex = mesh.Vertices.Count;
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * stride;
                            Vector3 p = Unsafe.ReadUnaligned<Vector3>(ref data[o + position]) * scale;
                            Vector3 n = normal >= 0 ? Unsafe.ReadUnaligned<Vector3>(ref data[o + normal]) * inverseScale : Vector3.Zero;
                            Vector3 t = tangent >= 0 ? Unsafe.ReadUnaligned<Vector3>(ref data[o + tangent]) * scale : Vector3.Zero;
                            mesh.Vertices.Add(new FractureVertex
                            {
                                Position = p,
                                Normal = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.Zero,
                                Tangent = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : Vector3.Zero,
                                TextureCoordinate = texture >= 0 ? Unsafe.ReadUnaligned<Vector2>(ref data[o + texture]) : Vector2.Zero,
                            });
                        }
                        for (int i = 0; i + 2 < indexCount; i += 3)
                        {
                            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                            if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count) continue;
                            // A mirrored scale flips the winding; keep every triangle facing the way it did.
                            if (scale.X * scale.Y * scale.Z < 0) (b, c) = (c, b);
                            mesh.AddTriangle(baseVertex + a, baseVertex + b, baseVertex + c, partGroup);
                        }
                    }
            }
            catch (Exception ex)
            {
                EditorBridge.Log($"Breakable '{owner.Name}': couldn't read the model: {ex.Message}");
                return null;
            }
            return mesh;
        }

        /// <summary>The model's bounding box (scaled) as a closed mesh, counter-clockwise seen from outside.</summary>
        internal static FractureMesh BoxMesh(BasicEntity owner)
        {
            var mesh = new FractureMesh();
            Vector3 min = owner.BoundingBox.Min * owner.Scale, max = owner.BoundingBox.Max * owner.Scale;
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            // Each face: outward normal and two in-plane axes whose cross product is the normal.
            (Vector3 N, Vector3 U, Vector3 V)[] faces =
            {
                (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
                (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
            };
            Vector3 center = (lo + hi) * 0.5f, half = (hi - lo) * 0.5f;
            foreach (var (n, u, v) in faces)
            {
                int first = mesh.Vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float su = corner == 1 || corner == 2 ? 1 : -1, sv = corner >= 2 ? 1 : -1;
                    Vector3 p = center + (n + u * su + v * sv) * half;
                    mesh.Vertices.Add(new FractureVertex { Position = p, Normal = n, Tangent = u, TextureCoordinate = new Vector2((su + 1) * 0.5f, (sv + 1) * 0.5f) });
                }
                mesh.AddTriangle(first, first + 1, first + 2, 0);
                mesh.AddTriangle(first, first + 2, first + 3, 0);
            }
            return mesh;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _pending = null;
            for (int i = _pieces.Count - 1; i >= 0; i--) RemovePiece(_pieces[i]);
            if (_ownsInterior) _interior?.Dispose();
            _interior = null;
            if (_hidOwner) Owner.WorldTransform.Hidden = false;
            Owner.WorldTransform.HasChanged = true;
        }

        private Vector3 RandomUnit()
        {
            float z = GlassFracture.Range(_random, -1, 1), a = GlassFracture.Range(_random, 0, MathHelper.TwoPi), r = MathF.Sqrt(1 - z * z);
            return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
        }

        private static Vector3 Bounds(FractureMesh mesh)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (FractureVertex v in mesh.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            return mesh.Vertices.Count > 0 ? max - min : Vector3.Zero;
        }

        private static float SafeInverse(float v) => Math.Abs(v) > 1e-8f ? 1f / v : 0f;
        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static GraphicsDevice FindDevice(Model model)
        {
            if (model == null) return null;
            foreach (ModelMesh mesh in model.Meshes)
                foreach (ModelMeshPart part in mesh.MeshParts)
                    if (part.VertexBuffer != null) return part.VertexBuffer.GraphicsDevice;
            return null;
        }
    }
}
