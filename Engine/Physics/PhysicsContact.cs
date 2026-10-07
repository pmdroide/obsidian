using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using Engine.Recources.Helper;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Engine.Physics
{
    /// <summary>One touching pair (or trigger overlap) from the last physics step, at its deepest contact.</summary>
    public struct PhysicsContact
    {
        public BodyHandle? BodyA;
        public StaticHandle? StaticA;
        public BodyHandle? BodyB;
        public StaticHandle? StaticB;
        /// <summary>World-space contact position.</summary>
        public XnaVector3 Point;
        /// <summary>Contact normal pointing from collider B towards collider A (unit length).</summary>
        public XnaVector3 Normal;
        /// <summary>Penetration depth; a small negative value is a gap the solver closes this step.</summary>
        public float Depth;
        /// <summary>How fast the surfaces approached along the normal when the step began (0 when separating).</summary>
        public float ImpactSpeed;
        /// <summary>Either collider is a trigger, so there was no collision response.</summary>
        public bool IsTrigger;

        /// <summary>The same contact seen from collider B.</summary>
        public PhysicsContact Swapped() => new PhysicsContact
        {
            BodyA = BodyB, StaticA = StaticB,
            BodyB = BodyA, StaticB = StaticA,
            Point = Point, Normal = -Normal, Depth = Depth, ImpactSpeed = ImpactSpeed, IsTrigger = IsTrigger,
        };
    }

    /// <summary>
    /// Collects the pairs that touch during a step, fed by <see cref="NarrowPhaseCallbacks"/>.
    /// A speculative contact (a gap the solver closes within the step) counts as touching, so a
    /// fast impact is reported in the step that resolves it, together with its approach speed.
    /// </summary>
    internal sealed class ContactRecorder
    {
        // Resting contacts hover around zero depth; the tolerance keeps them from flickering apart.
        private const float RestingTolerance = 0.005f;

        public Simulation Simulation;
        /// <summary>Packed <see cref="CollidableReference"/>s of trigger colliders.</summary>
        public readonly HashSet<uint> Triggers = new HashSet<uint>();
        public readonly List<PhysicsContact> Contacts = new List<PhysicsContact>();
        // The engine steps single-threaded, but a thread dispatcher would call Record in parallel.
        private readonly object _lock = new object();
        private float _dt;

        public void Begin(float dt)
        {
            lock (_lock)
            {
                Contacts.Clear();
                _dt = dt;
            }
        }

        public bool IsTrigger(CollidableReference collidable) => Triggers.Count > 0 && Triggers.Contains(collidable.Packed);

        public void Record<TManifold>(CollidablePair pair, ref TManifold manifold, bool trigger)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            int deepest = -1;
            float depth = float.MinValue;
            for (int i = 0; i < manifold.Count; i++)
            {
                manifold.GetContact(i, out _, out _, out float d, out _);
                if (d > depth)
                {
                    depth = d;
                    deepest = i;
                }
            }
            if (deepest < 0) return;
            manifold.GetContact(deepest, out Vector3 offset, out Vector3 normal, out _, out _);

            GetMotion(pair.A, out Vector3 positionA, out Vector3 linearA, out Vector3 angularA);
            GetMotion(pair.B, out Vector3 positionB, out Vector3 linearB, out Vector3 angularB);
            Vector3 point = positionA + offset;
            // Velocity of each surface at the contact; A closes on B when it moves against the B->A normal.
            Vector3 relative = linearA + Vector3.Cross(angularA, offset)
                             - linearB - Vector3.Cross(angularB, point - positionB);
            float closing = Math.Max(0f, -Vector3.Dot(relative, normal));

            // A trigger has no solver to close a gap, so only a real overlap counts.
            bool touching = trigger ? depth >= 0 : depth >= -RestingTolerance || closing * _dt >= -depth;
            if (!touching) return;

            if (normal.LengthSquared() > 1e-12f) normal = Vector3.Normalize(normal);
            var contact = new PhysicsContact
            {
                Point = MathConverter.ToXna(point),
                Normal = MathConverter.ToXna(normal),
                Depth = depth,
                ImpactSpeed = closing,
                IsTrigger = trigger,
            };
            if (pair.A.Mobility == CollidableMobility.Static) contact.StaticA = pair.A.StaticHandle;
            else contact.BodyA = pair.A.BodyHandle;
            if (pair.B.Mobility == CollidableMobility.Static) contact.StaticB = pair.B.StaticHandle;
            else contact.BodyB = pair.B.BodyHandle;

            lock (_lock) Contacts.Add(contact);
        }

        private void GetMotion(CollidableReference collidable, out Vector3 position, out Vector3 linear, out Vector3 angular)
        {
            linear = angular = default;
            if (collidable.Mobility == CollidableMobility.Static)
            {
                position = Simulation.Statics[collidable.StaticHandle].Pose.Position;
                return;
            }
            var body = Simulation.Bodies[collidable.BodyHandle];
            position = body.Pose.Position;
            linear = body.Velocity.Linear;
            angular = body.Velocity.Angular;
        }
    }
}
