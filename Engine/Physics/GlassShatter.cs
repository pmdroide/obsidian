using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// A broken (or breakable) glass pane during Play: the shards still in the frame, drawn as one mesh on the
    /// pane with a static convex collider each (owned by the gameobject, so rays and contacts find it), and the
    /// falling debris, a dynamic body and mesh per shard that is removed after <see cref="DebrisLifetime"/>
    /// (Half-Life: Alyx's <c>shatterglass_shard_lifetime</c>, 15 s) or when more than <see cref="MaxDebris"/>
    /// (<c>shatterglass_cleanup_max</c>, 150) fall in the whole scene. Shards draw with the gameobject's own
    /// material; the intact mesh is hidden once the pane cracks. Created and driven by
    /// <see cref="Components.BreakableGlassComponent"/> through <see cref="ScenePhysics.CreateGlass"/>; game thread only.
    /// </summary>
    public sealed class GlassShatter : IDisposable
    {
        /// <summary>Kilograms per cubic metre.</summary>
        public const float Density = 2500f;
        /// <summary>Debris alive in the whole scene before the oldest are removed.</summary>
        public static int MaxDebris = 150;
        /// <summary>Most shards one hit may throw; the rest of a fine hole just disappears (Valve skips them too).</summary>
        public const int MaxDebrisPerHit = 48;
        /// <summary>Most shards hanging in one frame; hits on a pane this cracked drop the hit shards whole.</summary>
        public const int MaxFrameShards = 400;
        private const float FadeTime = 0.4f;
        private const float ColliderInset = 0.0015f;
        private const float MinColliderThickness = 0.01f;
        // Shards thinner than this (area / perimeter, m) are slivers: no collider in the frame, no body when they fall.
        private const float MinSolidSize = 0.004f;
        private const float MaxDebrisSpeed = 60f;

        private sealed class Debris
        {
            public GlassShatter Glass;
            public BodyHandle Body;
            public Vector3 LocalCenter;
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public ModelMeshPart Part;
            public TransformMatrix Transform;
            public float Radius;
            public float Age;
        }

        private struct Pending
        {
            public Vector3 Point, Direction;
            public GlassStress Stress;
            public float Speed, Radius;
            public BasicEntity Passer;
            public float PasserSpeed;
        }

        // Debris of every pane, oldest first (for MaxDebris).
        private static readonly List<Debris> AllDebris = new();

        private readonly ScenePhysics _scene;
        private readonly PhysicsSystem _physics;
        private readonly MeshMaterialLibrary _library;
        private readonly GraphicsDevice _device;
        private readonly MaterialEffect _material;
        private readonly Random _random;
        private readonly int _collisionGroup;

        // The pane: world matrix of its plane (rows U, V, N, centre; no scale), half size, thickness.
        private readonly Matrix _panel;
        private readonly Quaternion _orientation;
        private readonly Vector3 _right, _up, _normal, _center;
        private readonly Vector2 _half;
        private readonly float _halfThickness;
        // Texture coordinates as affine functions of the pane position (fitted to the model's front face).
        private readonly Vector3 _uFromPanel, _vFromPanel;

        private readonly List<Vector2[]> _shards = new();
        private readonly List<StaticHandle> _colliders = new();
        private readonly List<Debris> _debris = new();
        private readonly List<Pending> _pending = new();
        private VertexBuffer _frameVertices;
        private IndexBuffer _frameIndices;
        private ModelMeshPart _framePart;
        private TransformMatrix _frameTransform;
        private bool _hidOwner;

        public BasicEntity Owner { get; }
        /// <summary>The pane has cracked: the gameobject's mesh and collider are replaced by the shards.</summary>
        public bool IsBroken { get; private set; }
        public bool IsDisposed { get; private set; }
        /// <summary>Shards still hanging in the frame.</summary>
        public int FrameShardCount => _shards.Count;
        /// <summary>Falling shards of this pane.</summary>
        public int DebrisCount => _debris.Count;
        /// <summary>Hits applied so far.</summary>
        public int HitCount { get; private set; }
        /// <summary>The shards still in the frame (pane space, metres); read-only use.</summary>
        public IReadOnlyList<Vector2[]> Shards => _shards;
        /// <summary>Size of the pane (metres, its two long sides) and its thickness.</summary>
        public Vector2 Size => _half * 2;
        public float Thickness => _halfThickness * 2;

        // Settings, copied from the component every frame.
        public float CellSize = 0.381f;
        public float HoleRadius = 0.06f;
        public float HitTolerance = 2f;
        public float DebrisLifetime = 15f;
        public bool DropUnsupported = true;
        public bool Distort = true;
        public bool SolidShards = true;

        /// <summary>A contact with the pane or a shard in its frame (seen from the pane), the other gameobject and its mass (0 = immovable).</summary>
        public Action<PhysicsContact, BasicEntity, float> Hit;

        /// <summary>After every hit (point in world space, the stress, shards thrown).</summary>
        public event Action<Vector3, GlassStress, int> Shattered;

        internal GlassShatter(ScenePhysics scene, PhysicsSystem physics, BasicEntity owner, MeshMaterialLibrary library, int collisionGroup)
        {
            _scene = scene;
            _physics = physics;
            Owner = owner;
            _library = library;
            _collisionGroup = collisionGroup;
            _random = new Random(owner.Id * 7919 + 17);
            _device = FindDevice(owner.Model);
            _material = owner.DrawMaterial;

            // The thinnest local side of the model's bounds is the pane's normal.
            BoundingBox box = owner.BoundingBox;
            Vector3 scale = new Vector3(Math.Abs(owner.Scale.X), Math.Abs(owner.Scale.Y), Math.Abs(owner.Scale.Z));
            Vector3 half = (box.Max - box.Min) * 0.5f * scale;
            int thin = half.X <= half.Y && half.X <= half.Z ? 0 : half.Y <= half.Z ? 1 : 2;
            int u = (thin + 1) % 3, v = (thin + 2) % 3;
            _half = new Vector2(Axis(half, u), Axis(half, v));
            _halfThickness = Math.Max(Axis(half, thin), 0.001f);

            Matrix rotation = owner.RotationMatrix;
            _right = Vector3.TransformNormal(Unit(u), rotation);
            _up = Vector3.TransformNormal(Unit(v), rotation);
            _normal = Vector3.TransformNormal(Unit(thin), rotation);
            Vector3 localCenter = (box.Min + box.Max) * 0.5f * owner.Scale;
            _center = owner.Position + Vector3.TransformNormal(localCenter, rotation);
            _panel = new Matrix(_right.X, _right.Y, _right.Z, 0, _up.X, _up.Y, _up.Z, 0, _normal.X, _normal.Y, _normal.Z, 0,
                _center.X, _center.Y, _center.Z, 1);
            _orientation = Quaternion.CreateFromRotationMatrix(new Matrix(_right.X, _right.Y, _right.Z, 0, _up.X, _up.Y, _up.Z, 0,
                _normal.X, _normal.Y, _normal.Z, 0, 0, 0, 0, 1));
            FitTextureCoordinates(owner, box, thin, u, v, out _uFromPanel, out _vFromPanel);
        }

        /// <summary>World point to pane coordinates (metres from the centre along its two sides).</summary>
        public Vector2 ToPanel(Vector3 world)
        {
            Vector3 d = world - _center;
            return new Vector2(Vector3.Dot(d, _right), Vector3.Dot(d, _up));
        }

        public Vector3 ToWorld(Vector2 panel) => _center + _right * panel.X + _up * panel.Y;

        /// <summary>Queues a hit, applied by <see cref="ApplyPending"/> (after the physics step's contacts, or right away from scripts).</summary>
        internal void Queue(Vector3 point, Vector3 direction, GlassStress stress, float speed, float radius, BasicEntity passer = null, float passerSpeed = 0)
        {
            if (IsDisposed) return;
            _pending.Add(new Pending
            {
                Point = point, Direction = direction, Stress = stress, Speed = speed, Radius = radius, Passer = passer, PasserSpeed = passerSpeed,
            });
        }

        internal void ApplyPending()
        {
            if (_pending.Count == 0) return;
            // Hooks of Shattered may queue more hits; take this batch first.
            var batch = _pending.ToArray();
            _pending.Clear();
            foreach (Pending hit in batch) Apply(hit);
        }

        private void Apply(Pending hit)
        {
            if (IsDisposed) return;
            Vector2 point = ToPanel(hit.Point);
            point = Vector2.Clamp(point, -_half, _half);
            Vector3 direction = hit.Direction.LengthSquared() > 1e-8f ? Vector3.Normalize(hit.Direction) : -_normal;
            HitCount++;

            if (!IsBroken)
            {
                IsBroken = true;
                _shards.Clear();
                _shards.AddRange(GlassFracture.CrackPane(_half, CellSize, _random));
                if (!Owner.WorldTransform.Hidden)
                {
                    Owner.WorldTransform.Hidden = true;
                    _hidOwner = true;
                }
            }

            int thrown = 0;
            var keep = new List<Vector2[]>(_shards.Count + 32);
            var falling = new List<(Vector2[] Shard, bool Hole)>();
            float hole = hit.Stress == GlassStress.Ballistic ? HoleRadius : Math.Max(hit.Radius, HoleRadius);
            foreach (Vector2[] shard in _shards)
            {
                bool hitShard = hit.Stress == GlassStress.Explosive
                    || GlassFracture.HitTest(shard, point, HitTolerance) || GlassFracture.Distance(shard, point) < hole;
                if (!hitShard)
                {
                    keep.Add(shard);
                    continue;
                }
                if (hit.Stress == GlassStress.Explosive || _shards.Count > MaxFrameShards || Math.Abs(GlassFracture.Area(shard)) < hole * hole * 0.25f)
                {
                    falling.Add((shard, true));
                    continue;
                }
                // Radial pieces: inside the hole they fall, outside they stay as cracked glass.
                foreach (Vector2[] piece in GlassFracture.Voronoi(shard, GlassFracture.ImpactSites(point, hole, _random)))
                {
                    float distance = Vector2.Distance(GlassFracture.Centroid(piece), point);
                    if (distance < hole || Math.Abs(GlassFracture.Area(piece)) < hole * hole * 0.04f) falling.Add((piece, true));
                    else keep.Add(piece);
                }
            }

            _shards.Clear();
            if (DropUnsupported && keep.Count > 0)
            {
                bool[] supported = GlassFracture.Supported(keep, _half);
                for (int i = 0; i < keep.Count; i++)
                    if (supported[i]) _shards.Add(keep[i]);
                    else falling.Add((keep[i], false));
            }
            else _shards.AddRange(keep);

            // Throw the hole's shards first, nearest the impact first, so the cap drops the far slivers.
            falling.Sort((a, b) => (b.Hole.CompareTo(a.Hole)) != 0 ? b.Hole.CompareTo(a.Hole)
                : Vector2.DistanceSquared(GlassFracture.Centroid(a.Shard), point).CompareTo(Vector2.DistanceSquared(GlassFracture.Centroid(b.Shard), point)));
            foreach (var (shard, inHole) in falling)
            {
                if (thrown >= MaxDebrisPerHit) break;
                if (SpawnDebris(shard, point, direction, hit, inHole)) thrown++;
            }

            RebuildFrame();
            PassThrough(hit, direction);
            try { Shattered?.Invoke(hit.Point, hit.Stress, thrown); }
            catch (Exception ex) { EditorBridge.Log($"Breakable Glass of '{Owner.Name}': a Shattered handler threw: {ex.Message}"); }
        }

        /// <summary>The body that broke the pane bounced off it in the step; give it back most of its speed.</summary>
        private void PassThrough(Pending hit, Vector3 direction)
        {
            if (hit.Passer == null || !(hit.PasserSpeed > 0) || !_scene.TryGetVelocity(hit.Passer, out Vector3 linear, out Vector3 angular)) return;
            float along = Vector3.Dot(linear, direction);
            float keep = hit.PasserSpeed * 0.75f;
            if (along >= keep) return;
            _scene.SetVelocity(hit.Passer, linear + direction * (keep - along), angular);
        }

        private bool SpawnDebris(Vector2[] shard, Vector2 point, Vector3 direction, Pending hit, bool inHole)
        {
            float area = Math.Abs(GlassFracture.Area(shard));
            // A sliver would make a hull without volume; it just disappears in the spray.
            if (area < 1e-6f || !GlassFracture.IsSolid(shard, MinSolidSize)) return false;
            Vector2 centroid = GlassFracture.Centroid(shard);
            var local = new Vector2[shard.Length];
            for (int i = 0; i < shard.Length; i++) local[i] = shard[i] - centroid;

            float halfCollider = Math.Max(_halfThickness, MinColliderThickness * 0.5f);
            Vector2[] inset = GlassFracture.Shrink(local, ColliderInset);
            var hull = new Vector3[inset.Length * 2];
            for (int i = 0; i < inset.Length; i++)
            {
                hull[i] = new Vector3(inset[i], halfCollider);
                hull[i + inset.Length] = new Vector3(inset[i], -halfCollider);
            }
            Vector3 position = ToWorld(centroid);
            float mass = Math.Max(area * _halfThickness * 2 * Density, 0.01f);
            BodyHandle body;
            Vector3 localCenter;
            try { body = _physics.AddDynamicConvex(hull, position, _orientation, mass, out localCenter); }
            catch (Exception ex)
            {
                EditorBridge.Log($"Breakable Glass of '{Owner.Name}': couldn't create a shard body: {ex.Message}");
                return false;
            }
            _physics.SetCollisionGroup(body, _collisionGroup);

            // Velocity: away from the impact along the hit, spreading out radially; loose shards just slip.
            Vector3 radial = ToWorld(centroid) - ToWorld(point);
            float distance = radial.Length();
            radial = distance > 1e-4f ? radial / distance : Vector3.Zero;
            Vector3 linear;
            if (hit.Stress == GlassStress.Explosive)
            {
                Vector3 away = position - hit.Point;
                away = away.LengthSquared() > 1e-6f ? Vector3.Normalize(away) : direction;
                linear = (away + direction * 0.5f) * hit.Speed * GlassFracture.Range(_random, 0.5f, 1f);
            }
            else if (inHole)
            {
                float falloff = 1f / (1f + distance / Math.Max(HoleRadius, 0.01f));
                linear = direction * hit.Speed * GlassFracture.Range(_random, 0.4f, 1f) * (0.5f + falloff)
                    + radial * hit.Speed * 0.15f * GlassFracture.Range(_random, 0.2f, 1f);
            }
            else linear = direction * GlassFracture.Range(_random, 0.05f, 0.4f);
            var angular = new Vector3(GlassFracture.Range(_random, -1, 1), GlassFracture.Range(_random, -1, 1), GlassFracture.Range(_random, -1, 1))
                * (inHole ? 12f : 2f);
            _physics.SetBodyVelocity(body, linear, angular);

            var debris = new Debris { Glass = this, Body = body, LocalCenter = localCenter };
            foreach (Vector2 p in local) debris.Radius = Math.Max(debris.Radius, p.Length());
            debris.Radius = MathF.Sqrt(debris.Radius * debris.Radius + _halfThickness * _halfThickness);
            if (_device != null && _material != null && _library != null)
            {
                var vertices = new List<VoxelVertex>();
                var indices = new List<int>();
                AppendPrism(vertices, indices, shard, centroid, Vector3.UnitZ);
                debris.Vertices = Upload(vertices, indices, out debris.Indices, out debris.Part);
                debris.Transform = new TransformMatrix(DebrisWorld(debris, 1), Owner.Id) { Scale = Vector3.One, HasChanged = true };
                debris.Transform.InverseWorld = Matrix.Invert(debris.Transform.World);
                _library.Register(_material, debris.Part, debris.Transform, new BoundingSphere(Vector3.Zero, debris.Radius));
            }
            _debris.Add(debris);
            AllDebris.Add(debris);
            while (AllDebris.Count > Math.Max(MaxDebris, 0)) AllDebris[0].Glass.RemoveDebris(AllDebris[0]);
            return true;
        }

        /// <summary>Moves the debris to their bodies, ages and removes them. Called by ScenePhysics after each step.</summary>
        internal void AfterStep(float dt)
        {
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Debris debris = _debris[i];
                debris.Age += dt;
                float lifetime = Math.Max(DebrisLifetime, 0.1f);
                if (debris.Age >= lifetime)
                {
                    RemoveDebris(debris);
                    continue;
                }
                // A body the solver lost (non-finite or flung away) would poison the broad phase next step.
                _physics.GetBodyState(debris.Body, out Vector3 position, out Quaternion orientation, out Vector3 velocity, out _);
                if (!IsFinite(position) || !float.IsFinite(orientation.W) || !IsFinite(velocity)
                    || velocity.LengthSquared() > MaxDebrisSpeed * MaxDebrisSpeed)
                {
                    RemoveDebris(debris);
                    continue;
                }
                if (debris.Transform == null) continue;
                float scale = Math.Clamp((lifetime - debris.Age) / FadeTime, 0, 1);
                Matrix world = DebrisWorld(debris, scale);
                if (world == debris.Transform.World) continue;
                debris.Transform.World = world;
                debris.Transform.InverseWorld = Matrix.Invert(world);
                debris.Transform.Scale = new Vector3(Math.Max(scale, 1e-3f));
                debris.Transform.HasChanged = true;
            }
        }

        /// <summary>Clears last frame's moved flags (the renderer has drawn them).</summary>
        internal void BeginFrame()
        {
            foreach (Debris debris in _debris)
                if (debris.Transform != null) debris.Transform.HasChanged = false;
            if (_frameTransform != null) _frameTransform.HasChanged = false;
        }

        private Matrix DebrisWorld(Debris debris, float scale)
        {
            _physics.GetBodyPose(debris.Body, out Vector3 position, out Quaternion orientation);
            return Matrix.CreateTranslation(-debris.LocalCenter) * Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(orientation)
                * Matrix.CreateTranslation(position);
        }

        private void RemoveDebris(Debris debris)
        {
            if (!_debris.Remove(debris)) return;
            AllDebris.Remove(debris);
            try { _physics.RemoveDynamic(debris.Body); }
            catch (Exception ex) { EditorBridge.Log($"Breakable Glass: removing a shard body threw: {ex.Message}"); }
            if (debris.Part != null) _library?.Unregister(debris.Part, debris.Transform);
            debris.Vertices?.Dispose();
            debris.Indices?.Dispose();
        }

        /// <summary>Redraws the frame's shards as one mesh and gives each a static collider.</summary>
        private void RebuildFrame()
        {
            ReleaseFrame();
            if (_shards.Count == 0) return;

            if (SolidShards)
            {
                foreach (Vector2[] shard in _shards)
                {
                    if (!GlassFracture.IsSolid(shard, MinSolidSize)) continue;
                    Vector2 centroid = GlassFracture.Centroid(shard);
                    Vector2[] inset = GlassFracture.Shrink(shard, ColliderInset);
                    float halfCollider = Math.Max(_halfThickness, MinColliderThickness * 0.5f);
                    var hull = new Vector3[inset.Length * 2];
                    for (int i = 0; i < inset.Length; i++)
                    {
                        hull[i] = new Vector3(inset[i] - centroid, halfCollider);
                        hull[i + inset.Length] = new Vector3(inset[i] - centroid, -halfCollider);
                    }
                    try { _colliders.Add(_scene.AddOwnedStatic(Owner, hull, ToWorld(centroid), _orientation)); }
                    catch (Exception ex) { EditorBridge.Log($"Breakable Glass of '{Owner.Name}': couldn't create a shard collider: {ex.Message}"); }
                }
            }

            if (_device == null || _material == null || _library == null) return;
            var vertices = new List<VoxelVertex>();
            var indices = new List<int>();
            foreach (Vector2[] shard in _shards)
            {
                // Distort: each cracked piece leans a little, so reflections break up along the cracks.
                Vector3 normal = Vector3.UnitZ;
                if (Distort)
                    normal = Vector3.Normalize(new Vector3(GlassFracture.Range(_random, -0.02f, 0.02f), GlassFracture.Range(_random, -0.02f, 0.02f), 1));
                AppendPrism(vertices, indices, shard, Vector2.Zero, normal);
            }
            _frameVertices = Upload(vertices, indices, out _frameIndices, out _framePart);
            _frameTransform = new TransformMatrix(_panel, Owner.Id) { Scale = Vector3.One, HasChanged = true, InverseWorld = Matrix.Invert(_panel) };
            float radius = MathF.Sqrt(_half.X * _half.X + _half.Y * _half.Y + _halfThickness * _halfThickness);
            _library.Register(_material, _framePart, _frameTransform, new BoundingSphere(Vector3.Zero, radius));
        }

        private void ReleaseFrame()
        {
            foreach (StaticHandle handle in _colliders) _scene.RemoveOwnedStatic(handle);
            _colliders.Clear();
            if (_framePart != null) _library?.Unregister(_framePart, _frameTransform);
            _frameVertices?.Dispose();
            _frameIndices?.Dispose();
            _framePart = null;
            _frameVertices = null;
            _frameIndices = null;
            _frameTransform = null;
        }

        /// <summary>
        /// A shard as a prism in pane space minus <paramref name="offset"/>: front (+Z, normal <paramref name="normal"/>),
        /// back and edge faces, wound clockwise seen from outside like the engine's other generated meshes.
        /// </summary>
        private void AppendPrism(List<VoxelVertex> vertices, List<int> indices, Vector2[] shard, Vector2 offset, Vector3 normal)
        {
            float h = _halfThickness;
            int n = shard.Length;
            var back = new Vector3(-normal.X, -normal.Y, -normal.Z);

            int front = vertices.Count;
            for (int i = 0; i < n; i++) vertices.Add(Vertex(shard[i], offset, h, normal, Vector3.UnitX));
            for (int i = 1; i + 1 < n; i++) { indices.Add(front); indices.Add(front + i + 1); indices.Add(front + i); }

            int rear = vertices.Count;
            for (int i = 0; i < n; i++) vertices.Add(Vertex(shard[i], offset, -h, back, Vector3.UnitX));
            for (int i = 1; i + 1 < n; i++) { indices.Add(rear); indices.Add(rear + i); indices.Add(rear + i + 1); }

            for (int i = 0; i < n; i++)
            {
                Vector2 a = shard[i], b = shard[(i + 1) % n];
                Vector2 edge = b - a;
                if (edge.LengthSquared() < 1e-12f) continue;
                edge.Normalize();
                var outward = new Vector3(edge.Y, -edge.X, 0);
                var tangent = new Vector3(edge, 0);
                int s = vertices.Count;
                vertices.Add(Vertex(a, offset, h, outward, tangent));
                vertices.Add(Vertex(b, offset, h, outward, tangent));
                vertices.Add(Vertex(b, offset, -h, outward, tangent));
                vertices.Add(Vertex(a, offset, -h, outward, tangent));
                indices.Add(s); indices.Add(s + 1); indices.Add(s + 2);
                indices.Add(s); indices.Add(s + 2); indices.Add(s + 3);
            }
        }

        private VoxelVertex Vertex(Vector2 panel, Vector2 offset, float z, Vector3 normal, Vector3 tangent)
        {
            var uv = new Vector2(Vector3.Dot(_uFromPanel, new Vector3(panel, 1)), Vector3.Dot(_vFromPanel, new Vector3(panel, 1)));
            return new VoxelVertex
            {
                Position = new Vector3(panel - offset, z),
                Normal = normal,
                TextureCoordinate = uv,
                Tangent = tangent,
                Binormal = Vector3.Cross(normal, tangent),
            };
        }

        private VertexBuffer Upload(List<VoxelVertex> vertices, List<int> indices, out IndexBuffer indexBuffer, out ModelMeshPart part)
        {
            var vertexBuffer = new VertexBuffer(_device, VoxelVertex.Declaration, vertices.Count, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices.ToArray());
            indexBuffer = new IndexBuffer(_device, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices.ToArray());
            // MonoGame marks this constructor obsolete, but it is the only way to make a part outside the content pipeline.
#pragma warning disable CS0618
            part = new ModelMeshPart
#pragma warning restore CS0618
            {
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                NumVertices = vertices.Count,
                StartIndex = 0,
                PrimitiveCount = indices.Count / 3,
            };
            return vertexBuffer;
        }

        /// <summary>
        /// Texture coordinates of the model's front face as affine functions of the pane position (least squares
        /// over the vertices on that face), so the shards keep the pane's texture. A planar 0..1 map otherwise.
        /// </summary>
        private void FitTextureCoordinates(BasicEntity owner, BoundingBox box, int thin, int u, int v, out Vector3 uFit, out Vector3 vFit)
        {
            uFit = new Vector3(0.5f / _half.X, 0, 0.5f);
            vFit = new Vector3(0, -0.5f / _half.Y, 0.5f);
            if (owner.Model == null) return;

            Vector3 center = (box.Min + box.Max) * 0.5f;
            double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = 0, sux = 0, suy = 0, su = 0, svx = 0, svy = 0, sv = 0;
            try
            {
                foreach (ModelMesh mesh in owner.Model.Meshes)
                    foreach (ModelMeshPart part in mesh.MeshParts)
                    {
                        VertexBuffer buffer = part.VertexBuffer;
                        if (buffer == null) continue;
                        int position = -1, texture = -1;
                        foreach (VertexElement element in buffer.VertexDeclaration.GetVertexElements())
                        {
                            if (element.UsageIndex != 0) continue;
                            if (element.VertexElementUsage == VertexElementUsage.Position && element.VertexElementFormat == VertexElementFormat.Vector3) position = element.Offset;
                            if (element.VertexElementUsage == VertexElementUsage.TextureCoordinate && element.VertexElementFormat == VertexElementFormat.Vector2) texture = element.Offset;
                        }
                        if (position < 0 || texture < 0) continue;
                        int stride = buffer.VertexDeclaration.VertexStride;
                        var data = new byte[buffer.VertexCount * stride];
                        buffer.GetData(0, data, 0, data.Length, 1);
                        for (int i = 0; i < buffer.VertexCount; i++)
                        {
                            Vector3 p = (Unsafe.ReadUnaligned<Vector3>(ref data[i * stride + position]) - center) * owner.Scale;
                            if (Axis(p, thin) < _halfThickness * 0.9f) continue; // front face only
                            Vector2 t = Unsafe.ReadUnaligned<Vector2>(ref data[i * stride + texture]);
                            double x = Axis(p, u), y = Axis(p, v);
                            sxx += x * x; sxy += x * y; sx += x; syy += y * y; sy += y; n++;
                            sux += t.X * x; suy += t.X * y; su += t.X;
                            svx += t.Y * x; svy += t.Y * y; sv += t.Y;
                        }
                    }
            }
            catch (Exception) { return; } // a buffer that can't be read back keeps the planar map

            if (n < 3) return;
            // Normal equations [xx xy x; xy yy y; x y n] * (a, b, c) = (Σtx, Σty, Σt).
            double det = sxx * (syy * n - sy * sy) - sxy * (sxy * n - sy * sx) + sx * (sxy * sy - syy * sx);
            if (Math.Abs(det) < 1e-12) return;
            Vector3 Solve(double rx, double ry, double r)
            {
                double a = (rx * (syy * n - sy * sy) - sxy * (ry * n - sy * r) + sx * (ry * sy - syy * r)) / det;
                double b = (sxx * (ry * n - r * sy) - rx * (sxy * n - sy * sx) + sx * (sxy * r - ry * sx)) / det;
                double c = (sxx * (syy * r - sy * ry) - sxy * (sxy * r - ry * sx) + rx * (sxy * sy - syy * sx)) / det;
                return new Vector3((float)a, (float)b, (float)c);
            }
            uFit = Solve(sux, suy, su);
            vFit = Solve(svx, svy, sv);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _pending.Clear();
            for (int i = _debris.Count - 1; i >= 0; i--) RemoveDebris(_debris[i]);
            ReleaseFrame();
            if (_hidOwner) Owner.WorldTransform.Hidden = false;
            Owner.WorldTransform.HasChanged = true;
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        private static Vector3 Unit(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;

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
