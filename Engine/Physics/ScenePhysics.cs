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
    /// Keeps BEPU bodies in step with each <see cref="BasicEntity"/>'s physics
    /// component (<see cref="BasicEntity.PhysicsType"/> / <see cref="BasicEntity.Mass"/>).
    /// Once per frame on the game thread it builds, rebuilds or removes bodies to
    /// match, then either pushes entity transforms into the bodies (editing) or steps
    /// the simulation and writes dynamic poses back into the entities (simulating).
    ///
    /// Static  = triangle mesh of the model (exact, never moves on its own).
    /// Dynamic = convex hull of the model with mass, so gravity and collisions apply.
    /// </summary>
    public sealed class ScenePhysics
    {
        // Large frame hitches would otherwise launch bodies through thin colliders.
        private const float MaxStep = 1f / 20f;

        private readonly PhysicsSystem _physics;
        private readonly List<BasicEntity> _attached = new List<BasicEntity>();
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
        }

        /// <param name="simulate">True to step gravity/collisions (Play mode); false keeps bodies glued to their entities.</param>
        public void Update(List<BasicEntity> entities, float dt, bool simulate)
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

            _physics.Step(Math.Min(dt, MaxStep));

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
            PhysicsBodyType desired = e.Model == null ? PhysicsBodyType.None : e.PhysicsType;
            float mass = Math.Max(e.Mass, 0.001f);

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
                // Don't retry every frame — fall back to no physics and report it.
                EditorBridge.Log($"ScenePhysics: couldn't create {desired} body for '{e.Name}': {ex.Message}");
                DestroyBody(e);
                e.PhysicsType = PhysicsBodyType.None;
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
            }
            else
            {
                var scaled = new Vector3[g.HullPoints.Length];
                for (int i = 0; i < scaled.Length; i++) scaled[i] = g.HullPoints[i] * e.Scale;
                e.DynamicBody = _physics.AddDynamicConvex(scaled, e.Position, orientation, mass, out e.ColliderOffset);
            }

            e.AttachedPhysicsType = type;
            e.AttachedScale = e.Scale;
            e.AttachedMass = mass;
            MarkSynced(e);
            _attached.Add(e);
        }

        private void DestroyBody(BasicEntity e)
        {
            try
            {
                if (e.StaticBody != null) _physics.RemoveStatic(e.StaticBody.Value);
                if (e.DynamicBody != null) _physics.RemoveDynamic(e.DynamicBody.Value);
            }
            catch (Exception ex) { EditorBridge.Log($"ScenePhysics: removing body of '{e.Name}' threw: {ex.Message}"); }

            e.StaticBody = null;
            e.DynamicBody = null;
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
