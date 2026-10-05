using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Physics
{
    /// <summary>
    /// Keeps BEPU bodies in step with each <see cref="BasicEntity"/>'s
    /// <see cref="Components.PhysicsComponent"/> (read through <see cref="BasicEntity.PhysicsType"/> /
    /// <see cref="BasicEntity.Mass"/>; a disabled or missing component means no body).
    /// Once per frame on the game thread it builds, rebuilds or removes bodies to
    /// match, then either pushes entity transforms into the bodies (editing) or steps
    /// the simulation and writes dynamic poses back into the entities (simulating).
    ///
    /// Static  = triangle mesh of the model (exact, never moves on its own).
    /// Dynamic = convex hull of the model with mass, so gravity and collisions apply.
    /// Water-role gameobjects build no body; dynamic bodies with Buoyancy float in them (<see cref="Physics.Buoyancy"/>).
    /// </summary>
    /// <summary>Closest collider hit by <see cref="ScenePhysics.Raycast"/>.</summary>
    public struct RaycastHit
    {
        /// <summary>The gameobject that owns the hit collider.</summary>
        public BasicEntity GameObject;
        /// <summary>World-space hit position.</summary>
        public Vector3 Point;
        /// <summary>World-space surface normal at the hit (unit length).</summary>
        public Vector3 Normal;
        /// <summary>Distance from the ray origin to <see cref="Point"/>.</summary>
        public float Distance;
    }

    public sealed class ScenePhysics
    {
        // Large frame hitches would otherwise launch bodies through thin colliders.
        private const float MaxStep = 1f / 20f;

        private readonly PhysicsSystem _physics;
        private readonly List<BasicEntity> _attached = new List<BasicEntity>();
        private readonly List<WaterVolume> _water = new List<WaterVolume>();
        // Reverse lookup from BEPU handles for raycasts.
        private readonly Dictionary<int, BasicEntity> _bodyOwners = new Dictionary<int, BasicEntity>();
        private readonly Dictionary<int, BasicEntity> _staticOwners = new Dictionary<int, BasicEntity>();
        private readonly Dictionary<Model, Geometry> _geometry =
            new Dictionary<Model, Geometry>(ReferenceEqualityComparer.Instance);

        private int _frame;
        private bool _wasSimulating;

        private sealed class Geometry
        {
            public Vector3[] Vertices;
            public int[] Indices;
            public Vector3[] HullPoints; // de-duplicated vertices
        }

        public ScenePhysics(PhysicsSystem physics)
        {
            _physics = physics ?? throw new ArgumentNullException(nameof(physics));
            Current = this;
        }

        /// <summary>The scene physics scripts query (raycasts): the most recently created instance.</summary>
        public static ScenePhysics Current { get; private set; }

        /// <summary>
        /// Closest collider along a ray. Water and gameobjects without a Physics component
        /// have no collider and are never hit. <paramref name="ignore"/> skips one gameobject (e.g. the caster).
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, BasicEntity ignore = null)
        {
            hit = default;
            if (!_physics.RayCast(origin, direction, maxDistance, ignore?.DynamicBody, ignore?.StaticBody,
                    out float distance, out Vector3 normal, out var body, out var staticHandle))
                return false;

            BasicEntity owner = null;
            if (body != null) _bodyOwners.TryGetValue(body.Value.Value, out owner);
            else if (staticHandle != null) _staticOwners.TryGetValue(staticHandle.Value.Value, out owner);
            hit = new RaycastHit
            {
                GameObject = owner,
                Point = origin + Vector3.Normalize(direction) * distance,
                Normal = normal,
                Distance = distance,
            };
            return true;
        }

        /// <summary>Linear (units/s) and angular (radians/s) velocity of a dynamic body; false without one.</summary>
        public bool TryGetVelocity(BasicEntity e, out Vector3 linear, out Vector3 angular)
        {
            linear = angular = Vector3.Zero;
            if (e.DynamicBody == null) return false;
            _physics.GetBodyState(e.DynamicBody.Value, out _, out _, out linear, out angular);
            return true;
        }

        public void SetVelocity(BasicEntity e, Vector3 linear, Vector3 angular)
        {
            if (e.DynamicBody != null) _physics.SetBodyVelocity(e.DynamicBody.Value, linear, angular);
        }

        /// <summary>Instant change in momentum (kg*units/s) at a world-space point, or at the centre of mass.</summary>
        public void ApplyImpulse(BasicEntity e, Vector3 impulse, Vector3? worldPoint = null)
        {
            if (e.DynamicBody == null) return;
            Vector3 offset = Vector3.Zero;
            if (worldPoint.HasValue)
            {
                _physics.GetBodyPose(e.DynamicBody.Value, out Vector3 center, out _);
                offset = worldPoint.Value - center;
            }
            _physics.ApplyImpulse(e.DynamicBody.Value, impulse, offset);
        }

        public void ApplyAngularImpulse(BasicEntity e, Vector3 impulse)
        {
            if (e.DynamicBody != null) _physics.ApplyAngularImpulse(e.DynamicBody.Value, impulse);
        }

        /// <param name="simulate">True to step gravity/collisions (Play mode); false keeps bodies glued to their entities.</param>
        /// <param name="time">Total game time in seconds, the clock the water waves animate with.</param>
        public void Update(List<BasicEntity> entities, float dt, bool simulate, float time = 0)
        {
            _frame++;
            // Leaving simulation: zero leftover velocities when bodies snap back.
            bool resetAll = _wasSimulating && !simulate;
            _wasSimulating = simulate;

            for (int i = 0; i < entities.Count; i++)
            {
                BasicEntity e = entities[i];
                e.PhysicsFrame = _frame;
                Reconcile(e);

                if (e.AttachedPhysicsType == PhysicsBodyType.None) continue;
                bool moved = e.Position != e.SyncedPosition || e.RotationMatrix != e.SyncedRotation;
                bool editing = !simulate || e.AttachedPhysicsType == PhysicsBodyType.Static;

                if (editing && (moved || resetAll))
                    PushPose(e, resetVelocity: true);
                else if (!editing && moved) // moved by a script or the gizmo mid-Play
                    PushPose(e, resetVelocity: false);
            }

            // Drop bodies whose entity left the scene (deleted, or scene swapped).
            for (int i = _attached.Count - 1; i >= 0; i--)
            {
                if (_attached[i].PhysicsFrame == _frame) continue;
                DestroyBody(_attached[i]);
                _attached.RemoveAt(i);
            }

            if (!simulate || _attached.Count == 0) return;

            float step = Math.Min(dt, MaxStep);
            ApplyBuoyancy(entities, time, step);
            _physics.Step(step);

            for (int i = 0; i < _attached.Count; i++)
                if (_attached[i].AttachedPhysicsType == PhysicsBodyType.Dynamic)
                    PullPose(_attached[i]);
        }

        /// <summary>Remove an entity's body right away (e.g. on delete).</summary>
        public void Detach(BasicEntity e)
        {
            if (!_attached.Remove(e)) return;
            DestroyBody(e);
        }

        public void DetachAll()
        {
            for (int i = 0; i < _attached.Count; i++) DestroyBody(_attached[i]);
            _attached.Clear();
        }

        private void Reconcile(BasicEntity e)
        {
            // Water is a volume to float in, not a surface to land on.
            PhysicsBodyType desired = e.Model == null || e.Role == GameObjectRole.Water ? PhysicsBodyType.None : e.PhysicsType;
            float mass = e.Mass;

            bool rebuild = e.AttachedPhysicsType != desired
                || (desired != PhysicsBodyType.None && e.AttachedScale != e.Scale)
                || (desired == PhysicsBodyType.Dynamic && e.AttachedMass != mass);
            if (!rebuild) return;

            Detach(e);
            if (desired == PhysicsBodyType.None) return;

            try
            {
                Attach(e, desired, mass);
            }
            catch (Exception ex)
            {
                // Don't retry every frame — disable the component and report it.
                EditorBridge.Log($"ScenePhysics: couldn't create {desired} body for '{e.Name}': {ex.Message}");
                DestroyBody(e);
                if (e.Physics != null) e.Physics.Enabled = false;
            }
        }

        private void ApplyBuoyancy(List<BasicEntity> entities, float time, float dt)
        {
            if (!(dt > 0)) return;
            WaterVolume.Collect(entities, _water);
            if (_water.Count == 0) return;

            for (int i = 0; i < _attached.Count; i++)
            {
                BasicEntity e = _attached[i];
                var physics = e.Physics;
                if (e.DynamicBody == null || physics == null || !physics.Buoyancy) continue;

                // The model's bounds in body space: scaled, then relative to the collider's centre of mass.
                Vector3 a = e.BoundingBox.Min * e.Scale - e.ColliderOffset;
                Vector3 b = e.BoundingBox.Max * e.Scale - e.ColliderOffset;
                Buoyancy.Apply(_physics, e.DynamicBody.Value, Vector3.Min(a, b), Vector3.Max(a, b), e.AttachedMass,
                    physics.ClampedBuoyancyStrength, physics.ClampedWaterDrag, _water, time, dt);
            }
        }

        private void Attach(BasicEntity e, PhysicsBodyType type, float mass)
        {
            Geometry g = GetGeometry(e.Model);
            Quaternion orientation = Orientation(e);

            if (type == PhysicsBodyType.Static)
            {
                // The mesh shape carries the (non-uniform) scale; the static carries the pose.
                e.StaticBody = _physics.AddStaticMesh(g.Vertices, g.Indices, e.Position, orientation, e.Scale);
                e.ColliderOffset = Vector3.Zero;
                _staticOwners[e.StaticBody.Value.Value] = e;
            }
            else
            {
                var scaled = new Vector3[g.HullPoints.Length];
                for (int i = 0; i < scaled.Length; i++) scaled[i] = g.HullPoints[i] * e.Scale;
                e.DynamicBody = _physics.AddDynamicConvex(scaled, e.Position, orientation, mass, out e.ColliderOffset);
                _bodyOwners[e.DynamicBody.Value.Value] = e;
            }

            e.AttachedPhysicsType = type;
            e.AttachedScale = e.Scale;
            e.AttachedMass = mass;
            e.PhysicsScene = this;
            MarkSynced(e);
            _attached.Add(e);
        }

        private void DestroyBody(BasicEntity e)
        {
            try
            {
                if (e.StaticBody != null)
                {
                    _staticOwners.Remove(e.StaticBody.Value.Value);
                    _physics.RemoveStatic(e.StaticBody.Value);
                }
                if (e.DynamicBody != null)
                {
                    _bodyOwners.Remove(e.DynamicBody.Value.Value);
                    _physics.RemoveDynamic(e.DynamicBody.Value);
                }
            }
            catch (Exception ex) { EditorBridge.Log($"ScenePhysics: removing body of '{e.Name}' threw: {ex.Message}"); }

            e.StaticBody = null;
            e.DynamicBody = null;
            e.PhysicsScene = null;
            e.AttachedPhysicsType = PhysicsBodyType.None;
        }

        private void PushPose(BasicEntity e, bool resetVelocity)
        {
            Quaternion orientation = Orientation(e);
            if (e.StaticBody != null)
            {
                _physics.SetStaticPose(e.StaticBody.Value, e.Position, orientation);
            }
            else if (e.DynamicBody != null)
            {
                Vector3 center = e.Position + Vector3.Transform(e.ColliderOffset, orientation);
                _physics.SetBodyPose(e.DynamicBody.Value, center, orientation, resetVelocity);
            }
            MarkSynced(e);
        }

        private void PullPose(BasicEntity e)
        {
            _physics.GetBodyPose(e.DynamicBody.Value, out Vector3 center, out Quaternion orientation);
            Matrix rotation = Matrix.CreateFromQuaternion(orientation);
            Vector3 position = center - Vector3.Transform(e.ColliderOffset, rotation);

            // Setters flag WorldTransform.HasChanged — skip them while the body sleeps.
            if (position != e.Position) e.Position = position;
            if (rotation != e.RotationMatrix) e.RotationMatrix = rotation;
            MarkSynced(e);
        }

        private static void MarkSynced(BasicEntity e)
        {
            e.SyncedPosition = e.Position;
            e.SyncedRotation = e.RotationMatrix;
        }

        private static Quaternion Orientation(BasicEntity e) =>
            Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(e.RotationMatrix));

        private Geometry GetGeometry(Model model)
        {
            if (_geometry.TryGetValue(model, out Geometry g)) return g;

            ModelDataExtractor.GetVerticesAndIndicesFromModel(model, out Vector3[] vertices, out int[] indices);
            g = new Geometry
            {
                Vertices = vertices,
                Indices = indices,
                HullPoints = new HashSet<Vector3>(vertices).ToArray(),
            };
            _geometry[model] = g;
            return g;
        }
    }
}
