using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Engine.Recources.Helper;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Engine.Physics
{
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

        // Single-threaded to start: Timestep is given a null dispatcher. A real
        // IThreadDispatcher can be slotted in here later without touching callers.
        private readonly IThreadDispatcher _threadDispatcher = null;

        /// <param name="gravity">World-space gravity in engine (Z-up) coordinates, e.g. (0,0,-9.81).</param>
        public PhysicsSystem(XnaVector3 gravity)
        {
            BufferPool = new BufferPool();
            Simulation = Simulation.Create(
                BufferPool,
                new NarrowPhaseCallbacks(new SpringSettings(30, 1)),
                new PoseIntegratorCallbacks(MathConverter.ToNumerics(gravity)),
                new SolveDescription(8, 1));
        }

        /// <summary>Advance the simulation by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            // BEPU v2 rejects a non-positive timestep (the old v1 Space.Update tolerated
            // it). MonoGame's very first Update tick — and any paused/0ms frame — reports
            // dt == 0, so skip those instead of throwing.
            if (dt <= 0f) return;
            Simulation.Timestep(dt, _threadDispatcher);
        }

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
            Simulation.Statics.Remove(handle);
            // RemoveAndDispose returns the mesh's triangle buffer to the pool.
            Simulation.Shapes.RemoveAndDispose(description.Shape, BufferPool);
        }

        public void RemoveDynamic(BodyHandle handle)
        {
            Simulation.Bodies.GetDescription(handle, out var description);
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
    /// Minimal contact callbacks (all dynamic pairs collide). Ported from the
    /// BepuPhysics demos' default callbacks.
    /// </summary>
    public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public SpringSettings ContactSpringiness;
        public float MaximumRecoveryVelocity;
        public float FrictionCoefficient;

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
            return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
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
            return true;
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
