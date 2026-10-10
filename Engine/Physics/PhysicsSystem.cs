using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using Engine.Recources.Helper;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Engine.Physics
{
    /// <summary>
    /// Physics component of a <see cref="Entities.BasicEntity"/>, picked in Anvil's
    /// Inspector > Physics. <see cref="Static"/> = immovable triangle-mesh collider;
    /// <see cref="Dynamic"/> = convex rigid body affected by gravity and collisions.
    /// </summary>
    public enum PhysicsBodyType
    {
        None,
        Static,
        Dynamic,
    }

    /// <summary>
    /// The single seam between the engine and BEPUphysics v2. Owns the
    /// <see cref="Simulation"/>, the <see cref="BufferPool"/> and the two required
    /// callback structs, and exposes everything in engine (XNA) math types so the
    /// rest of the codebase never references a raw BepuPhysics/BepuUtilities type
    /// (only the lightweight <see cref="BodyHandle"/>/<see cref="StaticHandle"/>
    /// handle structs leak out, by design).
    ///
    /// Replaces the old BEPUphysics v1 <c>Space</c>: ctor here == new Space + gravity,
    /// <see cref="Step"/> == Space.Update, Add/Remove == Space.Add/Remove,
    /// <see cref="GetBodyMatrix"/> == the per-frame pose read-back.
    /// </summary>
    public sealed class PhysicsSystem : IDisposable
    {
        public Simulation Simulation { get; private set; }
        public BufferPool BufferPool { get; private set; }
        /// <summary>World-space gravity (Z-up), as passed to the constructor.</summary>
        public XnaVector3 Gravity { get; }

        // Single-threaded to start: Timestep is given a null dispatcher. A real
        // IThreadDispatcher can be slotted in here later without touching callers.
        private readonly IThreadDispatcher _threadDispatcher = null;

        private readonly ContactRecorder _contacts = new ContactRecorder();

        /// <summary>
        /// Touching pairs and trigger overlaps found by the last <see cref="Step"/>, one entry per
        /// pair (its deepest contact). BEPU does not re-test a pair whose bodies all sleep, so a
        /// resting pair is missing here until something wakes it.
        /// </summary>
        public IReadOnlyList<PhysicsContact> Contacts => _contacts.Contacts;

        /// <param name="gravity">World-space gravity in engine (Z-up) coordinates, e.g. (0,0,-9.81).</param>
        public PhysicsSystem(XnaVector3 gravity)
        {
            Gravity = gravity;
            BufferPool = new BufferPool();
            Simulation = Simulation.Create(
                BufferPool,
                new NarrowPhaseCallbacks(new SpringSettings(30, 1)) { Contacts = _contacts },
                new PoseIntegratorCallbacks(MathConverter.ToNumerics(gravity)),
                new SolveDescription(8, 1));
            _contacts.Simulation = Simulation;
        }

        /// <summary>Advance the simulation by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            // BEPU v2 rejects a non-positive timestep (the old v1 Space.Update tolerated
            // it). MonoGame's very first Update tick — and any paused/0ms frame — reports
            // dt == 0, so skip those instead of throwing.
            if (dt <= 0f) return;
            _contacts.Begin(dt);
            Simulation.Timestep(dt, _threadDispatcher);
        }

        ////////////////////////////////////////////////////////////////////////////
        //  TRIGGERS
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// A trigger reports overlaps in <see cref="Contacts"/> but never pushes or is pushed
        /// (no contact constraint), and raycasts skip it unless asked to include triggers.
        /// </summary>
        public void SetTrigger(BodyHandle handle, bool trigger) =>
            SetTrigger(new CollidableReference(CollidableMobility.Dynamic, handle), trigger);

        public void SetTrigger(StaticHandle handle, bool trigger) => SetTrigger(new CollidableReference(handle), trigger);

        private void SetTrigger(CollidableReference collidable, bool trigger)
        {
            if (trigger) _contacts.Triggers.Add(collidable.Packed);
            else _contacts.Triggers.Remove(collidable.Packed);
        }

        /// <summary>False while the body sleeps (BEPU skips its pairs until something wakes it).</summary>
        public bool IsAwake(BodyHandle handle) => Simulation.Bodies[handle].Awake;

        ////////////////////////////////////////////////////////////////////////////
        //  COLLISION GROUPS
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Puts a body in collision group <paramref name="group"/> (above 0): bodies of one group never
        /// collide, except two ragdoll parts (<paramref name="part"/> 0 and up) whose pair is not set in
        /// <paramref name="ignoredPairs"/> (a parts x parts table). Part -1 collides with nothing in its group.
        /// </summary>
        internal void SetCollisionGroup(BodyHandle handle, int group, int part = -1, bool[] ignoredPairs = null, int parts = 0) =>
            _contacts.Groups[handle.Value] = new CollisionGroup { Group = group, Part = part, IgnoredPairs = ignoredPairs, Parts = parts };

        internal void ClearCollisionGroup(BodyHandle handle) => _contacts.Groups.Remove(handle.Value);

        /// <summary>Whether the collision groups let two bodies touch (checks).</summary>
        internal bool GroupsCollide(BodyHandle a, BodyHandle b) =>
            _contacts.Collides(new CollidableReference(CollidableMobility.Dynamic, a), new CollidableReference(CollidableMobility.Dynamic, b));

        ////////////////////////////////////////////////////////////////////////////
        //  SHAPES
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Build a static triangle <see cref="Mesh"/> shape from engine vertex/index
        /// data (e.g. from <c>ModelDataExtractor</c>) and register it. Non-uniform
        /// scale is applied via the mesh scale, never by pre-scaling the vertices.
        /// </summary>
        public TypedIndex CreateMeshShape(XnaVector3[] vertices, int[] indices, XnaVector3 scale)
        {
            int triangleCount = indices.Length / 3;
            BufferPool.Take<Triangle>(triangleCount, out var triangles);
            for (int i = 0; i < triangleCount; i++)
            {
                triangles[i] = new Triangle(
                    MathConverter.ToNumerics(vertices[indices[i * 3 + 0]]),
                    MathConverter.ToNumerics(vertices[indices[i * 3 + 1]]),
                    MathConverter.ToNumerics(vertices[indices[i * 3 + 2]]));
            }

            var mesh = new Mesh(triangles, MathConverter.ToNumerics(scale), BufferPool);
            return Simulation.Shapes.Add(mesh);
        }

        public TypedIndex CreateBoxShape(float width, float height, float length)
        {
            return Simulation.Shapes.Add(new Box(width, height, length));
        }

        ////////////////////////////////////////////////////////////////////////////
        //  BODIES / STATICS
        ////////////////////////////////////////////////////////////////////////////

        public StaticHandle AddStatic(XnaVector3 position, XnaQuaternion orientation, TypedIndex shape)
        {
            var pose = new RigidPose(MathConverter.ToNumerics(position), MathConverter.ToNumerics(orientation));
            return Simulation.Statics.Add(new StaticDescription(pose, shape));
        }

        /// <summary>
        /// Convenience: build a static triangle-mesh collider from engine vertex/index
        /// data and place it. Callers never touch a BEPU shape index.
        /// </summary>
        public StaticHandle AddStaticMesh(XnaVector3[] vertices, int[] indices, XnaVector3 position, XnaQuaternion orientation, XnaVector3 scale)
        {
            var shape = CreateMeshShape(vertices, indices, scale);
            return AddStatic(position, orientation, shape);
        }

        /// <summary>
        /// Static collider shaped as the convex hull of <paramref name="localPoints"/> (entity-local,
        /// scale applied), or their bounding box when no hull can be built. Unlike a triangle mesh
        /// it is solid, so a body fully inside still touches it (trigger volumes).
        /// <paramref name="localCenter"/> is the collider's centre in entity-local space, as for
        /// <see cref="AddDynamicConvex"/>.
        /// </summary>
        public StaticHandle AddStaticConvex(XnaVector3[] localPoints, XnaVector3 entityPosition, XnaQuaternion orientation, out XnaVector3 localCenter)
        {
            if (!TryCreateHull(localPoints, 1, out TypedIndex shape, out _, out Vector3 center))
                CreateBoundsBox(localPoints, 1, out shape, out _, out center);
            localCenter = MathConverter.ToXna(center);
            return AddStatic(entityPosition + XnaVector3.Transform(localCenter, orientation), orientation, shape);
        }

        /// <summary>
        /// Add a dynamic box rigid body (creates a dedicated box shape sized
        /// <paramref name="width"/>x<paramref name="height"/>x<paramref name="length"/>).
        /// Used by the verification scaffold; a general entity-driven body factory can
        /// build on this.
        /// </summary>
        public BodyHandle AddDynamicBox(XnaVector3 position, float width, float height, float length, float mass)
        {
            var box = new Box(width, height, length);
            var shapeIndex = Simulation.Shapes.Add(box);
            var inertia = box.ComputeInertia(mass);
            var pose = new RigidPose(MathConverter.ToNumerics(position));
            return Simulation.Bodies.Add(
                BodyDescription.CreateDynamic(pose, inertia, new CollidableDescription(shapeIndex), new BodyActivityDescription(0.01f)));
        }

        // Hull building is roughly O(n log n) in the point count; past this a bounding
        // box is used instead so a huge mesh can't stall the frame it is made dynamic.
        private const int MaxHullPoints = 20000;

        /// <summary>
        /// Add a dynamic rigid body whose collider is the convex hull of
        /// <paramref name="localPoints"/> (entity-local space, scale already applied).
        /// Falls back to the points' bounding box when the hull can't be built (flat,
        /// too few or too many points). The collider is centred on its own centre of
        /// mass, so <paramref name="localCenter"/> returns that centre in entity-local
        /// space: body position = entity position + orientation * localCenter.
        /// <paramref name="lockRotation"/> gives the body infinite inertia, so contacts and
        /// impulses never tip or spin it (character controllers).
        /// </summary>
        public BodyHandle AddDynamicConvex(XnaVector3[] localPoints, XnaVector3 entityPosition, XnaQuaternion orientation, float mass, out XnaVector3 localCenter,
            bool lockRotation = false)
        {
            if (!TryCreateHull(localPoints, mass, out TypedIndex shape, out BodyInertia inertia, out Vector3 center))
                CreateBoundsBox(localPoints, mass, out shape, out inertia, out center);
            if (lockRotation) inertia.InverseInertiaTensor = default;

            localCenter = MathConverter.ToXna(center);
            XnaVector3 position = entityPosition + XnaVector3.Transform(localCenter, orientation);
            var pose = new RigidPose(MathConverter.ToNumerics(position), MathConverter.ToNumerics(orientation));
            return Simulation.Bodies.Add(
                BodyDescription.CreateDynamic(pose, inertia, new CollidableDescription(shape), new BodyActivityDescription(0.01f)));
        }

        /// <summary>
        /// Registers the convex hull of <paramref name="localPoints"/> (or their bounding box) as a shape, with
        /// the inertia of <paramref name="mass"/> and the hull's centre in the points' space. The shape is
        /// centred on that centre. The caller owns the shape until a body using it is removed.
        /// </summary>
        internal TypedIndex CreateConvexShape(XnaVector3[] localPoints, float mass, out BodyInertia inertia, out XnaVector3 localCenter)
        {
            if (!TryCreateHull(localPoints, mass, out TypedIndex shape, out inertia, out Vector3 center))
                CreateBoundsBox(localPoints, mass, out shape, out inertia, out center);
            localCenter = MathConverter.ToXna(center);
            return shape;
        }

        private bool TryCreateHull(XnaVector3[] points, float mass, out TypedIndex shape, out BodyInertia inertia, out Vector3 center)
        {
            shape = default;
            inertia = default;
            center = default;
            if (points.Length < 4 || points.Length > MaxHullPoints) return false;

            // A flat point set (plane, quad) has no volume, so no usable inertia.
            GetBounds(points, out Vector3 min, out Vector3 max);
            Vector3 extent = max - min;
            float largest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            float smallest = MathF.Min(extent.X, MathF.Min(extent.Y, extent.Z));
            if (largest <= 0f || smallest < largest * 1e-3f) return false;

            var numerics = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) numerics[i] = MathConverter.ToNumerics(points[i]);

            ConvexHull hull;
            try { hull = new ConvexHull(numerics, BufferPool, out center); }
            catch { return false; }

            inertia = hull.ComputeInertia(mass);
            if (!float.IsFinite(inertia.InverseMass) || !float.IsFinite(inertia.InverseInertiaTensor.XX) ||
                !float.IsFinite(inertia.InverseInertiaTensor.YY) || !float.IsFinite(inertia.InverseInertiaTensor.ZZ))
            {
                hull.Dispose(BufferPool);
                return false;
            }

            shape = Simulation.Shapes.Add(hull);
            return true;
        }

        private void CreateBoundsBox(XnaVector3[] points, float mass, out TypedIndex shape, out BodyInertia inertia, out Vector3 center)
        {
            GetBounds(points, out Vector3 min, out Vector3 max);
            center = (min + max) * 0.5f;
            // Keep a minimum thickness so a flat model still gets a solid slab.
            Vector3 size = Vector3.Max(max - min, new Vector3(0.01f));
            var box = new Box(size.X, size.Y, size.Z);
            inertia = box.ComputeInertia(mass);
            shape = Simulation.Shapes.Add(box);
        }

        private static void GetBounds(XnaVector3[] points, out Vector3 min, out Vector3 max)
        {
            if (points.Length == 0) { min = max = Vector3.Zero; return; }
            min = max = MathConverter.ToNumerics(points[0]);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 p = MathConverter.ToNumerics(points[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        public void GetBodyPose(BodyHandle handle, out XnaVector3 position, out XnaQuaternion orientation)
        {
            var pose = Simulation.Bodies[handle].Pose;
            position = MathConverter.ToXna(pose.Position);
            orientation = MathConverter.ToXna(pose.Orientation);
        }

        public void GetBodyState(BodyHandle handle, out XnaVector3 position, out XnaQuaternion orientation,
            out XnaVector3 linearVelocity, out XnaVector3 angularVelocity)
        {
            var body = Simulation.Bodies[handle];
            position = MathConverter.ToXna(body.Pose.Position);
            orientation = MathConverter.ToXna(body.Pose.Orientation);
            linearVelocity = MathConverter.ToXna(body.Velocity.Linear);
            angularVelocity = MathConverter.ToXna(body.Velocity.Angular);
        }

        /// <summary>Wake a body and apply an impulse at <paramref name="offset"/> from its centre of mass (world-space).</summary>
        public void ApplyImpulse(BodyHandle handle, XnaVector3 impulse, XnaVector3 offset)
        {
            var body = Simulation.Bodies[handle];
            body.Awake = true;
            body.ApplyImpulse(MathConverter.ToNumerics(impulse), MathConverter.ToNumerics(offset));
        }

        /// <summary>Wake a body and change its spin by a world-space angular impulse.</summary>
        public void ApplyAngularImpulse(BodyHandle handle, XnaVector3 impulse)
        {
            var body = Simulation.Bodies[handle];
            body.Awake = true;
            body.ApplyAngularImpulse(MathConverter.ToNumerics(impulse));
        }

        /// <summary>Wake a body and overwrite its linear (units/s) and angular (radians/s) velocity.</summary>
        public void SetBodyVelocity(BodyHandle handle, XnaVector3 linear, XnaVector3 angular)
        {
            var body = Simulation.Bodies[handle];
            body.Velocity.Linear = MathConverter.ToNumerics(linear);
            body.Velocity.Angular = MathConverter.ToNumerics(angular);
            body.Awake = true;
        }

        /// <summary>
        /// Closest hit along a ray against every body and static, skipping the ignored ones and,
        /// unless <paramref name="includeTriggers"/>, triggers.
        /// <paramref name="direction"/> need not be normalized; <paramref name="distance"/> is in world units.
        /// </summary>
        /// <param name="ignoreGroup">Skips every body in this collision group (a ragdoll and its gameobject); 0 for none.</param>
        public bool RayCast(XnaVector3 origin, XnaVector3 direction, float maxDistance,
            BodyHandle? ignoreBody, StaticHandle? ignoreStatic,
            out float distance, out XnaVector3 normal, out BodyHandle? hitBody, out StaticHandle? hitStatic,
            bool includeTriggers = false, int ignoreGroup = 0)
        {
            distance = 0;
            normal = XnaVector3.Zero;
            hitBody = null;
            hitStatic = null;
            if (direction == XnaVector3.Zero || !(maxDistance > 0)) return false;

            var handler = new ClosestHitHandler
            {
                IgnoreBody = ignoreBody,
                IgnoreStatic = ignoreStatic,
                SkipTriggers = includeTriggers || _contacts.Triggers.Count == 0 ? null : _contacts.Triggers,
                Groups = ignoreGroup > 0 && _contacts.Groups.Count > 0 ? _contacts : null,
                IgnoreGroup = ignoreGroup,
            };
            Simulation.RayCast(MathConverter.ToNumerics(origin), MathConverter.ToNumerics(XnaVector3.Normalize(direction)),
                maxDistance, ref handler);
            if (!handler.HasHit) return false;

            distance = handler.T;
            normal = MathConverter.ToXna(handler.Normal);
            if (normal != XnaVector3.Zero) normal.Normalize();
            if (handler.Hit.Mobility == CollidableMobility.Static) hitStatic = handler.Hit.StaticHandle;
            else hitBody = handler.Hit.BodyHandle;
            return true;
        }

        private struct ClosestHitHandler : IRayHitHandler
        {
            public BodyHandle? IgnoreBody;
            public StaticHandle? IgnoreStatic;
            public HashSet<uint> SkipTriggers;
            internal ContactRecorder Groups;
            public int IgnoreGroup;
            public bool HasHit;
            public float T;
            public Vector3 Normal;
            public CollidableReference Hit;

            public bool AllowTest(CollidableReference collidable) =>
                !(SkipTriggers != null && SkipTriggers.Contains(collidable.Packed)) &&
                !(Groups != null && Groups.TryGetGroup(collidable, out CollisionGroup group) && group.Group == IgnoreGroup) &&
                (collidable.Mobility == CollidableMobility.Static
                    ? !(IgnoreStatic.HasValue && collidable.StaticHandle.Value == IgnoreStatic.Value.Value)
                    : !(IgnoreBody.HasValue && collidable.BodyHandle.Value == IgnoreBody.Value.Value));

            public bool AllowTest(CollidableReference collidable, int childIndex) => true;

            public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal,
                CollidableReference collidable, int childIndex)
            {
                if (t >= maximumT) return;
                // Shrinking maximumT lets BEPU skip anything farther than the closest hit so far.
                maximumT = t;
                T = t;
                Normal = normal;
                Hit = collidable;
                HasHit = true;
            }
        }

        /// <summary>
        /// Teleport a body to a full pose and wake it. <paramref name="resetVelocity"/>
        /// zeroes its motion (editor placement / returning from Play mode).
        /// </summary>
        public void SetBodyPose(BodyHandle handle, XnaVector3 position, XnaQuaternion orientation, bool resetVelocity)
        {
            var body = Simulation.Bodies[handle];
            body.Pose = new RigidPose(MathConverter.ToNumerics(position), MathConverter.ToNumerics(orientation));
            if (resetVelocity) body.Velocity = default;
            body.Awake = true;
            body.UpdateBounds();
        }

        /// <summary>Move a static collider; ApplyDescription refreshes its bounds and wakes nearby bodies.</summary>
        public void SetStaticPose(StaticHandle handle, XnaVector3 position, XnaQuaternion orientation)
        {
            Simulation.Statics.GetDescription(handle, out var description);
            description.Pose = new RigidPose(MathConverter.ToNumerics(position), MathConverter.ToNumerics(orientation));
            Simulation.Statics.ApplyDescription(handle, description);
        }

        /// <summary>Per-frame read-back: the body's pose as an XNA rotation*translation matrix (no scale).</summary>
        public XnaMatrix GetBodyMatrix(BodyHandle handle)
        {
            var pose = Simulation.Bodies[handle].Pose;
            return XnaMatrix.CreateFromQuaternion(MathConverter.ToXna(pose.Orientation))
                 * XnaMatrix.CreateTranslation(MathConverter.ToXna(pose.Position));
        }

        /// <summary>Teleport a body (e.g. editor drag) and wake it so the solver re-evaluates it.</summary>
        public void SetBodyPosition(BodyHandle handle, XnaVector3 position)
        {
            var body = Simulation.Bodies[handle];
            body.Pose.Position = MathConverter.ToNumerics(position);
            body.Awake = true;
        }

        public void RemoveStatic(StaticHandle handle)
        {
            Simulation.Statics.GetDescription(handle, out var description);
            // Handles are reused, so a later collider must not inherit the trigger flag.
            SetTrigger(handle, false);
            Simulation.Statics.Remove(handle);
            // RemoveAndDispose returns the mesh's triangle buffer to the pool.
            Simulation.Shapes.RemoveAndDispose(description.Shape, BufferPool);
        }

        public void RemoveDynamic(BodyHandle handle)
        {
            Simulation.Bodies.GetDescription(handle, out var description);
            SetTrigger(handle, false);
            ClearCollisionGroup(handle);
            Simulation.Bodies.Remove(handle);
            Simulation.Shapes.RemoveAndDispose(description.Collidable.Shape, BufferPool);
        }

        public void Dispose()
        {
            Simulation?.Dispose();
            BufferPool?.Clear();
            Simulation = null;
            BufferPool = null;
        }
    }

    /// <summary>
    /// v2 replacement for BEPUphysics v1's <c>ForceUpdater.Gravity</c>: gravity is
    /// applied here, per substep, in <see cref="IntegrateVelocity"/>. Ported from the
    /// BepuPhysics demos' default callbacks. Engine convention is Z-up.
    /// </summary>
    public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        public Vector3 Gravity;
        public float LinearDamping;
        public float AngularDamping;

        private Vector3Wide _gravityWideDt;
        private Vector<float> _linearDampingDt;
        private Vector<float> _angularDampingDt;

        public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = .03f, float angularDamping = .03f) : this()
        {
            Gravity = gravity;
            LinearDamping = linearDamping;
            AngularDamping = angularDamping;
        }

        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            _linearDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
            _angularDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
            _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear = (velocity.Linear + _gravityWideDt) * _linearDampingDt;
            velocity.Angular = velocity.Angular * _angularDampingDt;
        }
    }

    /// <summary>
    /// Minimal contact callbacks (pairs with a dynamic body collide, unless their collision
    /// groups say otherwise). Ported from the BepuPhysics demos' default callbacks. Pairs
    /// involving a trigger get no contact constraint; every pair's contacts are reported to
    /// <see cref="Contacts"/>.
    /// </summary>
    public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public SpringSettings ContactSpringiness;
        public float MaximumRecoveryVelocity;
        public float FrictionCoefficient;
        internal ContactRecorder Contacts;

        public NarrowPhaseCallbacks(SpringSettings contactSpringiness, float maximumRecoveryVelocity = 2f, float frictionCoefficient = 1f)
        {
            ContactSpringiness = contactSpringiness;
            MaximumRecoveryVelocity = maximumRecoveryVelocity;
            FrictionCoefficient = frictionCoefficient;
        }

        public void Initialize(Simulation simulation)
        {
            if (ContactSpringiness.AngularFrequency == 0 && ContactSpringiness.TwiceDampingRatio == 0)
            {
                ContactSpringiness = new SpringSettings(30, 1);
                MaximumRecoveryVelocity = 2f;
                FrictionCoefficient = 1f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic) return false;
            return Contacts == null || Contacts.Collides(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        {
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
            out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial.FrictionCoefficient = FrictionCoefficient;
            pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
            pairMaterial.SpringSettings = ContactSpringiness;
            if (Contacts == null) return true;
            bool trigger = Contacts.IsTrigger(pair.A) || Contacts.IsTrigger(pair.B);
            Contacts.Record(pair, ref manifold, trigger);
            // No constraint: triggers overlap freely.
            return !trigger;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB,
            ref ConvexContactManifold manifold)
        {
            return true;
        }

        public void Dispose() { }
    }
}
