using System;
using System.Collections.Generic;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using Engine.Entities;
using Engine.Recources.Helper;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// The rigid bodies and joints of one skinned gameobject's ragdoll (built from a <see cref="RagdollRig"/>).
    /// Inactive, the parts are kinematic and follow the animated pose (<see cref="DrivePose"/>), so they push
    /// other bodies, report contacts and stop raycasts. <see cref="Activate"/> makes them dynamic and joins
    /// them with limited joints; after each physics step <see cref="BoneModel"/>/<see cref="SkinTransforms"/>
    /// then follow the bodies, and the gameobject moves with the root part. Owned by <see cref="ScenePhysics"/>
    /// (<see cref="ScenePhysics.CreateRagdoll"/>); game thread only.
    /// </summary>
    public sealed class Ragdoll
    {
        // Larger inertia steadies long thin parts (forearms, shins) that would otherwise jitter in their joints.
        private const float InertiaScale = 2f;
        // A kinematic part that would move further than this in one step teleports instead of sweeping.
        private const float MaxDriveDistance = 1f;
        private static readonly SpringSettings JointSpring = new SpringSettings(30, 1);

        public RagdollRig Rig { get; }
        public BasicEntity Owner { get; }
        /// <summary>True while limp (dynamic bodies joined by joints); false while following the animation.</summary>
        public bool IsActive { get; private set; }
        public bool IsDisposed { get; private set; }
        public int PartCount => _bodies.Length;
        /// <summary>Total mass in kilograms.</summary>
        public float Mass { get; }

        /// <summary>Model-space bone transforms the kinematic parts follow (the animation, or the bind pose).</summary>
        public Matrix[] DrivePose;
        /// <summary>Model-space bone transforms after the last step while active.</summary>
        public Matrix[] BoneModel { get; }
        /// <summary>Skin matrices (inverse bind * <see cref="BoneModel"/>) matching <see cref="BoneModel"/>.</summary>
        public Matrix[] SkinTransforms { get; }

        /// <summary>Runs after a step moved the active ragdoll (the skin needs the new pose).</summary>
        internal Action Posed;
        /// <summary>A part touched another gameobject: part, contact seen from the part, the other gameobject, its mass (0 when immovable).</summary>
        internal Action<int, PhysicsContact, BasicEntity, float> Hit;

        internal readonly int Group;
        private readonly PhysicsSystem _physics;
        private readonly BodyHandle[] _bodies;
        private readonly BodyInertia[] _inertia;
        private readonly float[] _partMass;
        // Collider centre in the part bone's rigid frame, and that bone's scale.
        private readonly Vector3[] _center;
        private readonly Vector3[] _scale;
        private readonly float _jointFriction;
        private readonly List<ConstraintHandle> _constraints = new List<ConstraintHandle>();
        // Bones without a part keep their pose relative to their parent, as it was when the ragdoll went limp.
        private readonly Matrix[] _frozenLocal;
        // The kinematic targets of the last step.
        private readonly Vector3[] _targetPosition;
        private readonly Quaternion[] _targetOrientation;
        private bool _hasTargets;
        private bool _posedAsleep;

        internal Ragdoll(PhysicsSystem physics, BasicEntity owner, RagdollRig rig, Matrix[] pose, float mass, float jointFriction, int group)
        {
            _physics = physics ?? throw new ArgumentNullException(nameof(physics));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Rig = rig ?? throw new ArgumentNullException(nameof(rig));
            Group = group;
            Mass = mass;
            _jointFriction = Math.Max(0, jointFriction);
            DrivePose = pose ?? rig.BindModel;

            int parts = rig.Parts.Length, bones = rig.Skeleton.BoneCount;
            _bodies = new BodyHandle[parts];
            _inertia = new BodyInertia[parts];
            _partMass = new float[parts];
            _center = new Vector3[parts];
            _scale = new Vector3[parts];
            _targetPosition = new Vector3[parts];
            _targetOrientation = new Quaternion[parts];
            _frozenLocal = new Matrix[bones];
            BoneModel = new Matrix[bones];
            SkinTransforms = new Matrix[bones];
            Array.Copy(DrivePose, BoneModel, bones);
            for (int i = 0; i < bones; i++) SkinTransforms[i] = rig.Skeleton.InverseBindPose[i] * BoneModel[i];

            bool[] ignored = IgnoredPairs(rig);
            Matrix world = EntityWorld(owner, owner.Position);
            for (int p = 0; p < parts; p++)
            {
                RagdollRig.Part part = rig.Parts[p];
                (BoneModel[part.Bone] * world).Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation);
                _scale[p] = scale;
                var points = new Vector3[part.Points.Length];
                for (int i = 0; i < points.Length; i++) points[i] = part.Points[i] * scale;

                _partMass[p] = Math.Max(Components.PhysicsComponent.MinMass, mass * part.MassShare);
                TypedIndex shape = _physics.CreateConvexShape(points, _partMass[p], out BodyInertia inertia, out _center[p]);
                inertia.InverseInertiaTensor.XX /= InertiaScale;
                inertia.InverseInertiaTensor.YX /= InertiaScale;
                inertia.InverseInertiaTensor.YY /= InertiaScale;
                inertia.InverseInertiaTensor.ZX /= InertiaScale;
                inertia.InverseInertiaTensor.ZY /= InertiaScale;
                inertia.InverseInertiaTensor.ZZ /= InertiaScale;
                _inertia[p] = inertia;

                rotation.Normalize();
                Vector3 position = translation + Vector3.Transform(_center[p], rotation);
                var bodyPose = new RigidPose(MathConverter.ToNumerics(position), MathConverter.ToNumerics(rotation));
                _bodies[p] = _physics.Simulation.Bodies.Add(BodyDescription.CreateKinematic(bodyPose,
                    new CollidableDescription(shape), new BodyActivityDescription(0.01f)));
                _physics.SetCollisionGroup(_bodies[p], group, p, ignored, parts);
                _targetPosition[p] = position;
                _targetOrientation[p] = rotation;
            }
            _hasTargets = true;
        }

        /// <summary>The part a body belongs to, or -1.</summary>
        public int PartOf(BodyHandle body)
        {
            for (int p = 0; p < _bodies.Length; p++)
                if (_bodies[p].Value == body.Value) return p;
            return -1;
        }

        internal BodyHandle BodyOf(int part) => _bodies[part];
        public float PartMass(int part) => _partMass[part];

        /// <summary>World-space centre of a part's collider.</summary>
        public Vector3 PartPosition(int part)
        {
            _physics.GetBodyPose(_bodies[part], out Vector3 position, out _);
            return position;
        }

        /// <summary>World-space position of the root part's bone (usually the hips).</summary>
        public Vector3 RootBonePosition
        {
            get
            {
                _physics.GetBodyPose(_bodies[0], out Vector3 position, out Quaternion orientation);
                return position - Vector3.Transform(_center[0], orientation);
            }
        }

        /// <summary>True when every part sleeps.</summary>
        public bool IsAsleep
        {
            get
            {
                foreach (BodyHandle body in _bodies)
                    if (_physics.IsAwake(body)) return false;
                return true;
            }
        }

        /// <summary>
        /// Goes limp: the parts become dynamic (keeping the velocity the animation gave them) and are
        /// joined at each part's bone origin, with limits that depend on the joint's kind.
        /// </summary>
        public void Activate()
        {
            if (IsActive || IsDisposed) return;
            IsActive = true;
            _posedAsleep = false;

            // Bones without a part keep the animated pose relative to their parent from now on.
            for (int i = 0; i < _frozenLocal.Length; i++)
            {
                int parent = Rig.Skeleton.ParentIndices[i];
                Matrix parentModel = parent >= 0 ? DrivePose[parent] : Rig.Skeleton.RootTransform;
                _frozenLocal[i] = DrivePose[i] * Matrix.Invert(parentModel);
            }

            for (int p = 0; p < _bodies.Length; p++)
            {
                BodyReference body = _physics.Simulation.Bodies[_bodies[p]];
                body.SetLocalInertia(_inertia[p]);
                body.Awake = true;
            }
            for (int p = 0; p < _bodies.Length; p++)
                if (Rig.Parts[p].Parent >= 0) AddJoint(p);
        }

        /// <summary>Stops being limp: the joints go and the parts follow <see cref="DrivePose"/> again.</summary>
        public void Deactivate()
        {
            if (!IsActive || IsDisposed) return;
            IsActive = false;
            RemoveJoints();
            foreach (BodyHandle handle in _bodies)
            {
                BodyReference body = _physics.Simulation.Bodies[handle];
                body.BecomeKinematic();
                body.Velocity = default;
            }
            // The parts lie where they fell: jump to the animation instead of sweeping through the scene.
            _hasTargets = false;
            Array.Copy(DrivePose, BoneModel, BoneModel.Length);
        }

        /// <summary>Instant change of momentum (kg*m/s) on a part, at a world-space point or its centre. Only while active.</summary>
        public void ApplyImpulse(int part, Vector3 impulse, Vector3? worldPoint = null)
        {
            if (!IsActive || IsDisposed || part < 0 || part >= _bodies.Length) return;
            Vector3 offset = Vector3.Zero;
            if (worldPoint.HasValue) offset = worldPoint.Value - PartPosition(part);
            _physics.ApplyImpulse(_bodies[part], impulse, offset);
        }

        /// <summary>The part whose collider centre is nearest <paramref name="worldPoint"/>.</summary>
        public int NearestPart(Vector3 worldPoint)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int p = 0; p < _bodies.Length; p++)
            {
                float distance = Vector3.DistanceSquared(PartPosition(p), worldPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Before a step: kinematic parts get the velocity that carries them to this frame's animated pose.</summary>
        internal void BeforeStep(float dt)
        {
            if (IsActive || IsDisposed || !(dt > 0)) return;
            Matrix world = EntityWorld(Owner, Owner.Position);
            for (int p = 0; p < _bodies.Length; p++)
            {
                (DrivePose[Rig.Parts[p].Bone] * world).Decompose(out _, out Quaternion rotation, out Vector3 translation);
                rotation.Normalize();
                Vector3 position = translation + Vector3.Transform(_center[p], rotation);

                bool teleport = !_hasTargets || Vector3.Distance(position, _targetPosition[p]) > MaxDriveDistance;
                Vector3 linear = Vector3.Zero, angular = Vector3.Zero;
                if (!teleport)
                {
                    linear = (position - _targetPosition[p]) / dt;
                    angular = AngularVelocity(_targetOrientation[p], rotation, dt);
                }
                // Start the step exactly on the last target so the parts never drift from the animation.
                Vector3 start = teleport ? position : _targetPosition[p];
                Quaternion startOrientation = teleport ? rotation : _targetOrientation[p];
                BodyReference body = _physics.Simulation.Bodies[_bodies[p]];
                body.Pose = new RigidPose(MathConverter.ToNumerics(start), MathConverter.ToNumerics(startOrientation));
                body.Velocity.Linear = MathConverter.ToNumerics(linear);
                body.Velocity.Angular = MathConverter.ToNumerics(angular);
                body.Awake = true;
                body.UpdateBounds();

                _targetPosition[p] = position;
                _targetOrientation[p] = rotation;
            }
            _hasTargets = true;
        }

        /// <summary>After a step: an active ragdoll's bones follow the bodies and the gameobject follows the root part.</summary>
        internal void AfterStep()
        {
            if (!IsActive || IsDisposed) return;
            bool asleep = IsAsleep;
            if (asleep && _posedAsleep) return;
            _posedAsleep = asleep;
            UpdatePose();
            Posed?.Invoke();
        }

        /// <summary>Recomputes <see cref="BoneModel"/>/<see cref="SkinTransforms"/> from the bodies.</summary>
        internal void UpdatePose()
        {
            int parts = _bodies.Length;
            var partWorld = new Matrix[parts];
            for (int p = 0; p < parts; p++)
            {
                _physics.GetBodyPose(_bodies[p], out Vector3 position, out Quaternion orientation);
                partWorld[p] = Matrix.CreateScale(_scale[p]) * Matrix.CreateFromQuaternion(orientation) *
                               Matrix.CreateTranslation(position - Vector3.Transform(_center[p], orientation));
            }

            // Keep the root bone where it sits in the model, so culling bounds and the origin stay with the body.
            Vector3 rootBind = Rig.BindModel[Rig.Parts[0].Bone].Translation;
            Vector3 entityPosition = partWorld[0].Translation - Vector3.Transform(rootBind * Owner.Scale, Owner.RotationMatrix);
            if (entityPosition != Owner.Position) Owner.Position = entityPosition;
            Matrix toModel = Matrix.Invert(EntityWorld(Owner, entityPosition));

            var skeleton = Rig.Skeleton;
            for (int i = 0; i < BoneModel.Length; i++)
            {
                int part = Rig.PartOfBone[i];
                if (part >= 0 && Rig.Parts[part].Bone == i)
                    BoneModel[i] = partWorld[part] * toModel;
                else
                {
                    int parent = skeleton.ParentIndices[i];
                    BoneModel[i] = _frozenLocal[i] * (parent >= 0 ? BoneModel[parent] : skeleton.RootTransform);
                }
                SkinTransforms[i] = skeleton.InverseBindPose[i] * BoneModel[i];
            }
        }

        internal void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            IsActive = false;
            RemoveJoints();
            foreach (BodyHandle body in _bodies) _physics.RemoveDynamic(body);
        }

        private void AddJoint(int p)
        {
            RagdollRig.Part part = Rig.Parts[p];
            int parentPart = part.Parent;
            BodyHandle a = _bodies[parentPart], b = _bodies[p];
            _physics.GetBodyPose(a, out Vector3 positionA, out Quaternion orientationA);
            _physics.GetBodyPose(b, out Vector3 positionB, out Quaternion orientationB);

            // Joined at the child bone's origin, where the two parts meet right now.
            Vector3 joint = positionB - Vector3.Transform(_center[p], orientationB);
            Add(new BallSocket
            {
                LocalOffsetA = MathConverter.ToNumerics(Vector3.Transform(joint - positionA, Quaternion.Inverse(orientationA))),
                LocalOffsetB = MathConverter.ToNumerics(-_center[p]),
                SpringSettings = JointSpring,
            });

            // Limits are measured from the bind pose: each axis in the two bones' own spaces.
            Quaternion bindA = BindRotation(Rig.Parts[parentPart].Bone), bindB = BindRotation(part.Bone);
            Vector3 direction = part.RestDirection;
            RagdollJointLimits limits = RagdollJointLimits.For(part.Joint);
            Vector3 hinge = Vector3.Zero;
            if (limits.IsHinge)
            {
                // Elbows bend the forearm forward, knees the shin backward.
                Vector3 flex = part.Joint == RagdollJointKind.Knee ? -Rig.Forward : Rig.Forward;
                hinge = Vector3.Cross(direction, flex);
                if (hinge.LengthSquared() < 0.04f) limits = RagdollJointLimits.For(RagdollJointKind.Other);
                else hinge.Normalize();
            }

            if (limits.IsHinge)
            {
                Add(new AngularHinge
                {
                    LocalHingeAxisA = MathConverter.ToNumerics(Local(hinge, bindA)),
                    LocalHingeAxisB = MathConverter.ToNumerics(Local(hinge, bindB)),
                    SpringSettings = JointSpring,
                });
                // Positive twist about the hinge turns the part towards the flex direction.
                Quaternion basis = Basis(hinge, direction);
                Add(new TwistLimit
                {
                    LocalBasisA = MathConverter.ToNumerics(Quaternion.Concatenate(basis, Quaternion.Inverse(bindA))),
                    LocalBasisB = MathConverter.ToNumerics(Quaternion.Concatenate(basis, Quaternion.Inverse(bindB))),
                    MinimumAngle = MathHelper.ToRadians(limits.MinAngle),
                    MaximumAngle = MathHelper.ToRadians(limits.MaxAngle),
                    SpringSettings = JointSpring,
                });
            }
            else
            {
                Add(new SwingLimit
                {
                    AxisLocalA = MathConverter.ToNumerics(Local(direction, bindA)),
                    AxisLocalB = MathConverter.ToNumerics(Local(direction, bindB)),
                    MaximumSwingAngle = MathHelper.ToRadians(limits.MaxAngle),
                    SpringSettings = JointSpring,
                });
                Vector3 reference = Math.Abs(Vector3.Dot(direction, Rig.Forward)) < 0.9f ? Rig.Forward : Rig.Up;
                Quaternion basis = Basis(direction, reference);
                Add(new TwistLimit
                {
                    LocalBasisA = MathConverter.ToNumerics(Quaternion.Concatenate(basis, Quaternion.Inverse(bindA))),
                    LocalBasisB = MathConverter.ToNumerics(Quaternion.Concatenate(basis, Quaternion.Inverse(bindB))),
                    MinimumAngle = MathHelper.ToRadians(-limits.Twist),
                    MaximumAngle = MathHelper.ToRadians(limits.Twist),
                    SpringSettings = JointSpring,
                });
            }

            // Friction: resists relative spin up to a torque that scales with the child part's mass.
            if (_jointFriction > 0)
                Add(new AngularMotor
                {
                    TargetVelocityLocalA = System.Numerics.Vector3.Zero,
                    Settings = new MotorSettings(_jointFriction * _partMass[p], 0.0001f),
                });

            void Add<T>(T description) where T : unmanaged, ITwoBodyConstraintDescription<T> =>
                _constraints.Add(_physics.Simulation.Solver.Add(a, b, description));
        }

        private void RemoveJoints()
        {
            foreach (ConstraintHandle constraint in _constraints)
                if (_physics.Simulation.Solver.ConstraintExists(constraint)) _physics.Simulation.Solver.Remove(constraint);
            _constraints.Clear();
        }

        private Quaternion BindRotation(int bone)
        {
            Rig.BindModel[bone].Decompose(out _, out Quaternion rotation, out _);
            rotation.Normalize();
            return rotation;
        }

        // A model-space direction in the space of a bone with model-space rotation 'bind'.
        private static Vector3 Local(Vector3 direction, Quaternion bind) => Vector3.Transform(direction, Quaternion.Inverse(bind));

        /// <summary>Rotation whose Z is <paramref name="z"/> and whose X is <paramref name="x"/> made perpendicular to it.</summary>
        internal static Quaternion Basis(Vector3 z, Vector3 x)
        {
            z.Normalize();
            x -= z * Vector3.Dot(x, z);
            x.Normalize();
            Vector3 y = Vector3.Cross(z, x);
            Matrix m = Matrix.Identity;
            m.Right = x;
            m.Up = y;
            m.Backward = z;
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        private static Vector3 AngularVelocity(Quaternion from, Quaternion to, float dt)
        {
            Quaternion delta = Quaternion.Concatenate(Quaternion.Inverse(from), to);
            if (delta.W < 0) delta = -delta;
            float sinHalf = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            if (sinHalf < 1e-6f) return Vector3.Zero;
            float angle = 2 * MathF.Atan2(sinHalf, delta.W);
            return new Vector3(delta.X, delta.Y, delta.Z) / sinHalf * (angle / dt);
        }

        private static Matrix EntityWorld(BasicEntity e, Vector3 position) =>
            Matrix.CreateScale(e.Scale) * e.RotationMatrix * Matrix.CreateTranslation(position);

        /// <summary>
        /// Pairs of parts that never collide: joined parts, parts sharing a parent (thighs at the hips,
        /// arms at the chest) and a part with its grandparent. Everything else (an arm and the
        /// opposite leg, a hand and the torso) does.
        /// </summary>
        private static bool[] IgnoredPairs(RagdollRig rig)
        {
            int n = rig.Parts.Length;
            var ignored = new bool[n * n];
            int ParentOf(int p) => rig.Parts[p].Parent;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int pi = ParentOf(i), pj = ParentOf(j);
                    bool related = i == j || pi == j || pj == i || (pi >= 0 && pi == pj) ||
                                   (pi >= 0 && ParentOf(pi) == j) || (pj >= 0 && ParentOf(pj) == i);
                    ignored[i * n + j] = related;
                }
            return ignored;
        }
    }

    /// <summary>How far a joint of each <see cref="RagdollJointKind"/> may bend, in degrees.</summary>
    public readonly struct RagdollJointLimits
    {
        /// <summary>Elbows and knees: they only bend about one axis, between <see cref="MinAngle"/> and <see cref="MaxAngle"/>.</summary>
        public readonly bool IsHinge;
        /// <summary>Hinge: bend range. Cone: <see cref="MaxAngle"/> is the widest swing away from the bind direction.</summary>
        public readonly float MinAngle, MaxAngle;
        /// <summary>Cone: twist either way about the part's own axis.</summary>
        public readonly float Twist;

        private RagdollJointLimits(bool hinge, float min, float max, float twist)
        {
            IsHinge = hinge;
            MinAngle = min;
            MaxAngle = max;
            Twist = twist;
        }

        private static RagdollJointLimits Cone(float swing, float twist) => new RagdollJointLimits(false, 0, swing, twist);
        private static RagdollJointLimits Hinge(float min, float max) => new RagdollJointLimits(true, min, max, 0);

        public static RagdollJointLimits For(RagdollJointKind kind) => kind switch
        {
            RagdollJointKind.Spine => Cone(30, 20),
            RagdollJointKind.Neck => Cone(40, 40),
            RagdollJointKind.Clavicle => Cone(15, 10),
            RagdollJointKind.UpperArm => Cone(100, 60),
            RagdollJointKind.Elbow => Hinge(-5, 140),
            RagdollJointKind.Thigh => Cone(75, 30),
            RagdollJointKind.Knee => Hinge(-5, 135),
            RagdollJointKind.Hand => Cone(60, 30),
            RagdollJointKind.Foot => Cone(35, 15),
            _ => Cone(45, 30),
        };
    }
}
