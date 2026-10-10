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

    /// <summary>A contact passed to a script's OnCollisionEnter/Stay/Exit, seen from the script's gameobject.</summary>
    public struct Collision
    {
        /// <summary>The other gameobject.</summary>
        public BasicEntity GameObject;
        /// <summary>World-space contact position (the deepest point of the latest contact).</summary>
        public Vector3 Point;
        /// <summary>Unit contact normal pointing from the other gameobject towards this one.</summary>
        public Vector3 Normal;
        /// <summary>Penetration depth in world units (about zero while resting).</summary>
        public float Depth;
        /// <summary>
        /// Speed (units/s) at which the two surfaces were closing along <see cref="Normal"/> just before
        /// the physics step resolved the contact. On Enter this is the impact speed; while resting it is near 0.
        /// </summary>
        public float ImpactSpeed;
    }

    /// <summary>
    /// Keeps BEPU bodies in step with each <see cref="BasicEntity"/>'s
    /// <see cref="Components.PhysicsComponent"/> (read through <see cref="BasicEntity.PhysicsType"/> /
    /// <see cref="BasicEntity.Mass"/>; a disabled or missing component means no body).
    /// Once per frame on the game thread it builds, rebuilds or removes bodies to
    /// match, then either pushes entity transforms into the bodies (editing) or steps
    /// the simulation and writes dynamic poses back into the entities (simulating).
    /// After each step it turns BEPU's contacts into Enter/Stay/Exit events for the
    /// Script Behaviours on both gameobjects (collisions and triggers).
    ///
    /// Static  = triangle mesh of the model (exact, never moves on its own).
    /// Dynamic = convex hull of the model with mass, so gravity and collisions apply.
    /// Trigger = overlaps without colliding; a Static trigger is the model's convex hull (solid volume).
    /// Water-role gameobjects build no body; dynamic bodies with Buoyancy float in them (<see cref="Physics.Buoyancy"/>).
    /// </summary>
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
        // Ragdolls of Ragdoll components; their parts are in _bodyOwners under the owning gameobject.
        private readonly List<Ragdoll> _ragdolls = new List<Ragdoll>();
        private static int _lastCollisionGroup;

        private int _frame;
        private bool _wasSimulating;

        private sealed class Geometry
        {
            public Vector3[] Vertices;
            public int[] Indices;
            public Vector3[] HullPoints; // de-duplicated vertices
        }

        private enum ContactPhase { Enter, Stay, Exit }

        /// <summary>Two gameobjects in contact; the latest contact is stored as seen from <see cref="A"/>.</summary>
        private sealed class Touch
        {
            public BasicEntity A, B;
            public bool IsTrigger;
            public PhysicsContact Contact;
            public int Frame;
        }

        // Keyed by the gameobject pair, so a rebuilt body (new handle) keeps its contacts.
        private readonly Dictionary<(BasicEntity, BasicEntity), Touch> _touches = new Dictionary<(BasicEntity, BasicEntity), Touch>();
        private readonly List<(Touch Touch, ContactPhase Phase)> _contactEvents = new List<(Touch, ContactPhase)>();
        private readonly List<Touch> _ended = new List<Touch>();

        /// <summary>Collider geometry for gameobjects without a loaded model (GPU-free checks in Tests/Components).</summary>
        internal Func<BasicEntity, (Vector3[] Vertices, int[] Indices)> FallbackGeometry;

        public ScenePhysics(PhysicsSystem physics)
        {
            _physics = physics ?? throw new ArgumentNullException(nameof(physics));
            Current = this;
        }

        /// <summary>The scene physics scripts query (raycasts): the most recently created instance.</summary>
        public static ScenePhysics Current { get; private set; }

        /// <summary>
        /// Closest collider along a ray. Water and gameobjects without a Physics component
        /// have no collider and are never hit; triggers only with <paramref name="includeTriggers"/>.
        /// <paramref name="ignore"/> skips one gameobject (e.g. the caster).
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, BasicEntity ignore = null,
            bool includeTriggers = false)
        {
            hit = default;
            if (!_physics.RayCast(origin, direction, maxDistance, ignore?.DynamicBody, ignore?.StaticBody,
                    out float distance, out Vector3 normal, out var body, out var staticHandle, includeTriggers, ignore?.CollisionGroup ?? 0))
                return false;

            hit = new RaycastHit
            {
                GameObject = OwnerOf(body, staticHandle),
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
            for (int i = _ragdolls.Count - 1; i >= 0; i--)
                if (_ragdolls[i].Owner.PhysicsFrame != _frame) RemoveRagdoll(_ragdolls[i]);

            if (!simulate)
            {
                // Contacts only exist while simulating; scripts are stopped or never ran.
                _touches.Clear();
                return;
            }
            if (_attached.Count == 0 && _touches.Count == 0 && _ragdolls.Count == 0) return;

            float step = Math.Min(dt, MaxStep);
            // Without a step there are no fresh contacts, so the current touches stand.
            if (!(step > 0)) return;
            ApplyBuoyancy(entities, time, step);
            for (int i = 0; i < _ragdolls.Count; i++) _ragdolls[i].BeforeStep(step);
            _physics.Step(step);

            for (int i = 0; i < _attached.Count; i++)
                if (_attached[i].AttachedPhysicsType == PhysicsBodyType.Dynamic)
                    PullPose(_attached[i]);
            for (int i = 0; i < _ragdolls.Count; i++) _ragdolls[i].AfterStep();

            UpdateTouches();
            DispatchContactEvents();
        }

        /// <summary>True while the two gameobjects touch or overlap (as of the last physics step).</summary>
        public bool IsTouching(BasicEntity a, BasicEntity b) => FindTouch(a, b) != null;

        private Touch FindTouch(BasicEntity a, BasicEntity b) =>
            a == null || b == null ? null
            : _touches.TryGetValue((a, b), out Touch touch) || _touches.TryGetValue((b, a), out touch) ? touch : null;

        /// <summary>Turns this step's contacts into Enter/Stay events and vanished pairs into Exit events.</summary>
        private void UpdateTouches()
        {
            IReadOnlyList<PhysicsContact> contacts = _physics.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                PhysicsContact contact = contacts[i];
                BasicEntity a = OwnerOf(contact.BodyA, contact.StaticA);
                BasicEntity b = OwnerOf(contact.BodyB, contact.StaticB);
                if (a == null || b == null || a == b) continue;
                if (a.Ragdoll != null) NotifyRagdoll(a.Ragdoll, contact.BodyA, contact, b, contact.BodyB);
                if (b.Ragdoll != null) NotifyRagdoll(b.Ragdoll, contact.BodyB, contact.Swapped(), a, contact.BodyA);

                Touch touch = FindTouch(a, b);
                if (touch != null && touch.IsTrigger != contact.IsTrigger)
                {
                    // Became (or stopped being) a trigger: end the old kind of contact, start the new one.
                    EndTouch(touch);
                    touch = null;
                }
                bool entered = touch == null;
                if (entered)
                {
                    touch = new Touch { A = a, B = b, IsTrigger = contact.IsTrigger };
                    _touches[(a, b)] = touch;
                }
                else if (touch.Frame == _frame) continue;
                touch.Contact = touch.A == a ? contact : contact.Swapped();
                touch.Frame = _frame;
                _contactEvents.Add((touch, entered ? ContactPhase.Enter : ContactPhase.Stay));
            }

            foreach (Touch touch in _touches.Values)
            {
                if (touch.Frame == _frame) continue;
                // BEPU doesn't re-test a pair whose bodies all sleep: it is still resting together.
                if (IsResting(touch.A) && IsResting(touch.B)) touch.Frame = _frame;
                else _ended.Add(touch);
            }
            foreach (Touch touch in _ended) EndTouch(touch);
            _ended.Clear();
        }

        private void EndTouch(Touch touch)
        {
            if (!_touches.Remove((touch.A, touch.B))) _touches.Remove((touch.B, touch.A));
            _contactEvents.Add((touch, ContactPhase.Exit));
        }

        /// <summary>Still in this scene with a static collider, a sleeping body or a sleeping ragdoll.</summary>
        private bool IsResting(BasicEntity e)
        {
            bool hasRagdoll = e.Ragdoll != null && _ragdolls.Contains(e.Ragdoll);
            if (hasRagdoll && !e.Ragdoll.IsAsleep) return false;
            if (e.PhysicsScene != this) return hasRagdoll;
            return e.DynamicBody != null ? !_physics.IsAwake(e.DynamicBody.Value) : e.StaticBody != null;
        }

        /// <summary>Tells a ragdoll which of its parts touched <paramref name="other"/> and how heavy that was.</summary>
        private static void NotifyRagdoll(Ragdoll ragdoll, BepuPhysics.BodyHandle? body, PhysicsContact seenFromRagdoll, BasicEntity other,
            BepuPhysics.BodyHandle? otherBody)
        {
            if (ragdoll.Hit == null || body == null) return;
            int part = ragdoll.PartOf(body.Value);
            if (part < 0) return;
            try { ragdoll.Hit(part, seenFromRagdoll, other, MassOf(other, otherBody)); }
            catch (Exception ex) { EditorBridge.Log($"Ragdoll of '{ragdoll.Owner.Name}' threw on contact: {ex.Message}"); }
        }

        /// <summary>Mass of a moving body (a dynamic gameobject or a limp ragdoll part); 0 for anything immovable.</summary>
        private static float MassOf(BasicEntity e, BepuPhysics.BodyHandle? body)
        {
            if (body == null) return 0;
            if (e.DynamicBody?.Value == body.Value.Value) return e.AttachedTrigger ? 0 : e.AttachedMass;
            if (e.Ragdoll is { IsActive: true } ragdoll)
            {
                int part = ragdoll.PartOf(body.Value);
                if (part >= 0) return ragdoll.PartMass(part);
            }
            return 0;
        }

        /// <summary>
        /// Builds the ragdoll of <paramref name="owner"/> from <paramref name="rig"/>, posed as <paramref name="pose"/>
        /// (model-space bones) and following it until <see cref="Ragdoll.Activate"/>. Replaces any ragdoll it had.
        /// </summary>
        internal Ragdoll CreateRagdoll(BasicEntity owner, RagdollRig rig, Matrix[] pose, float mass, float jointFriction)
        {
            if (owner.Ragdoll != null) RemoveRagdoll(owner.Ragdoll);
            if (owner.CollisionGroup == 0) owner.CollisionGroup = ++_lastCollisionGroup;
            var ragdoll = new Ragdoll(_physics, owner, rig, pose, mass, jointFriction, owner.CollisionGroup);
            for (int p = 0; p < ragdoll.PartCount; p++) _bodyOwners[ragdoll.BodyOf(p).Value] = owner;
            // The gameobject's own collider never collides with its parts.
            if (owner.DynamicBody != null) _physics.SetCollisionGroup(owner.DynamicBody.Value, owner.CollisionGroup);
            owner.Ragdoll = ragdoll;
            owner.PhysicsFrame = _frame;
            _ragdolls.Add(ragdoll);
            return ragdoll;
        }

        /// <summary>Removes a ragdoll's bodies and joints; the gameobject's touches with them end on the next step.</summary>
        internal void RemoveRagdoll(Ragdoll ragdoll)
        {
            if (ragdoll == null || !_ragdolls.Remove(ragdoll)) return;
            for (int p = 0; p < ragdoll.PartCount; p++) _bodyOwners.Remove(ragdoll.BodyOf(p).Value);
            ragdoll.Dispose();
            if (ragdoll.Owner.Ragdoll == ragdoll) ragdoll.Owner.Ragdoll = null;
        }

        private BasicEntity OwnerOf(BepuPhysics.BodyHandle? body, BepuPhysics.StaticHandle? staticHandle)
        {
            BasicEntity owner = null;
            if (body != null) _bodyOwners.TryGetValue(body.Value.Value, out owner);
            else if (staticHandle != null) _staticOwners.TryGetValue(staticHandle.Value.Value, out owner);
            return owner;
        }

        private void DispatchContactEvents()
        {
            // Hooks may spawn or destroy gameobjects; that only touches the body lists, not these events.
            for (int i = 0; i < _contactEvents.Count; i++)
            {
                var (touch, phase) = _contactEvents[i];
                Notify(touch.A, touch.B, touch.Contact, touch.IsTrigger, phase);
                Notify(touch.B, touch.A, touch.Contact.Swapped(), touch.IsTrigger, phase);
            }
            _contactEvents.Clear();
        }

        /// <summary>Runs the matching hook on every running Script Behaviour of <paramref name="self"/>.</summary>
        private static void Notify(BasicEntity self, BasicEntity other, PhysicsContact contact, bool trigger, ContactPhase phase)
        {
            // Most touching pairs (crates on the floor) have no scripts: skip them without allocating.
            bool hasScript = false;
            foreach (var component in self.Components)
                if (component is Components.ScriptBehaviourComponent) { hasScript = true; break; }
            if (!hasScript) return;

            var collision = new Collision
            {
                GameObject = other,
                Point = contact.Point,
                Normal = contact.Normal, // stored B->A, and self is A
                Depth = contact.Depth,
                ImpactSpeed = contact.ImpactSpeed,
            };
            foreach (var script in self.GetComponents<Components.ScriptBehaviourComponent>().ToArray())
            {
                switch (phase)
                {
                    case ContactPhase.Enter when trigger: script.Notify("OnTriggerEnter", s => s.OnTriggerEnter(other)); break;
                    case ContactPhase.Stay when trigger: script.Notify("OnTriggerStay", s => s.OnTriggerStay(other)); break;
                    case ContactPhase.Exit when trigger: script.Notify("OnTriggerExit", s => s.OnTriggerExit(other)); break;
                    case ContactPhase.Enter: script.Notify("OnCollisionEnter", s => s.OnCollisionEnter(collision)); break;
                    case ContactPhase.Stay: script.Notify("OnCollisionStay", s => s.OnCollisionStay(collision)); break;
                    case ContactPhase.Exit: script.Notify("OnCollisionExit", s => s.OnCollisionExit(collision)); break;
                }
            }
        }

        /// <summary>Remove an entity's body right away (e.g. on delete).</summary>
        public void Detach(BasicEntity e)
        {
            if (!_attached.Remove(e)) return;
            DestroyBody(e);
        }

        /// <summary>
        /// Removes every body except those of <paramref name="keep"/> (persistent gameobjects carried into the
        /// next scene). Their touches with removed bodies end on the next step, with the usual Exit events.
        /// </summary>
        public void DetachAll(IReadOnlyCollection<BasicEntity> keep = null)
        {
            for (int i = _ragdolls.Count - 1; i >= 0; i--)
                if (keep == null || !keep.Contains(_ragdolls[i].Owner)) RemoveRagdoll(_ragdolls[i]);
            if (keep == null || keep.Count == 0)
            {
                for (int i = 0; i < _attached.Count; i++) DestroyBody(_attached[i]);
                _attached.Clear();
                _touches.Clear();
                return;
            }
            for (int i = _attached.Count - 1; i >= 0; i--)
            {
                if (keep.Contains(_attached[i])) continue;
                DestroyBody(_attached[i]);
                _attached.RemoveAt(i);
            }
            var forgotten = new List<(BasicEntity, BasicEntity)>();
            foreach (var pair in _touches)
                if (!keep.Contains(pair.Value.A) && !keep.Contains(pair.Value.B)) forgotten.Add(pair.Key);
            foreach (var key in forgotten) _touches.Remove(key);
        }

        private void Reconcile(BasicEntity e)
        {
            // Water is a volume to float in, not a surface to land on.
            bool hasGeometry = e.Model != null || FallbackGeometry != null;
            // A limp ragdoll's parts stand in for the gameobject's own collider.
            bool limp = e.Ragdoll is { IsActive: true, IsDisposed: false };
            PhysicsBodyType desired = !hasGeometry || e.Role == GameObjectRole.Water || limp ? PhysicsBodyType.None : e.PhysicsType;
            float mass = e.Mass;
            bool trigger = desired != PhysicsBodyType.None && e.Physics?.IsTrigger == true;
            bool freezeRotation = desired == PhysicsBodyType.Dynamic && e.Physics?.FreezeRotation == true;

            bool rebuild = e.AttachedPhysicsType != desired
                || (desired != PhysicsBodyType.None && (e.AttachedScale != e.Scale || e.AttachedTrigger != trigger))
                || (desired == PhysicsBodyType.Dynamic && (e.AttachedMass != mass || e.AttachedFreezeRotation != freezeRotation));
            if (!rebuild) return;

            Detach(e);
            if (desired == PhysicsBodyType.None) return;

            try
            {
                Attach(e, desired, mass, trigger, freezeRotation);
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

        private void Attach(BasicEntity e, PhysicsBodyType type, float mass, bool trigger, bool freezeRotation)
        {
            Geometry g = GetGeometry(e);
            Quaternion orientation = Orientation(e);

            if (type == PhysicsBodyType.Static && !trigger)
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
                if (type == PhysicsBodyType.Static)
                {
                    // A mesh is hollow: a body fully inside would touch no triangle and leave the trigger.
                    e.StaticBody = _physics.AddStaticConvex(scaled, e.Position, orientation, out e.ColliderOffset);
                    _staticOwners[e.StaticBody.Value.Value] = e;
                    _physics.SetTrigger(e.StaticBody.Value, true);
                }
                else
                {
                    e.DynamicBody = _physics.AddDynamicConvex(scaled, e.Position, orientation, mass, out e.ColliderOffset, freezeRotation);
                    _bodyOwners[e.DynamicBody.Value.Value] = e;
                    if (e.CollisionGroup != 0) _physics.SetCollisionGroup(e.DynamicBody.Value, e.CollisionGroup);
                    if (trigger) _physics.SetTrigger(e.DynamicBody.Value, true);
                }
            }

            e.AttachedPhysicsType = type;
            e.AttachedScale = e.Scale;
            e.AttachedMass = mass;
            e.AttachedTrigger = trigger;
            e.AttachedFreezeRotation = freezeRotation;
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
            // Convex colliders are centred on their own centre; mesh statics have no offset.
            Vector3 center = e.Position + Vector3.Transform(e.ColliderOffset, orientation);
            if (e.StaticBody != null)
                _physics.SetStaticPose(e.StaticBody.Value, center, orientation);
            else if (e.DynamicBody != null)
                _physics.SetBodyPose(e.DynamicBody.Value, center, orientation, resetVelocity);
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

        private Geometry GetGeometry(BasicEntity e)
        {
            Model model = e.Model;
            Vector3[] vertices;
            int[] indices;
            if (model == null)
            {
                (vertices, indices) = FallbackGeometry(e);
                return new Geometry { Vertices = vertices, Indices = indices, HullPoints = new HashSet<Vector3>(vertices).ToArray() };
            }
            if (_geometry.TryGetValue(model, out Geometry g)) return g;

            ModelDataExtractor.GetVerticesAndIndicesFromModel(model, out vertices, out indices);
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
