using System.Collections.Generic;
using BepuPhysics;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>
    /// A collider a cloth is kept out of during one frame, in world space, built from a BEPU body or static by
    /// <see cref="PhysicsSystem.CollectClothColliders"/>. Convex shapes (hulls, boxes) are a set of planes and a
    /// particle inside all of them is pushed out through the nearest one. Meshes are the triangles near the
    /// cloth; a particle can't cross one from either side.
    /// </summary>
    public sealed class ClothCollider
    {
        /// <summary>The gameobject that owns the collider (null for a body without one).</summary>
        public BasicEntity GameObject;
        public BodyHandle? Body;
        public StaticHandle? Static;
        /// <summary>World-space bounds of the collider.</summary>
        public Vector3 Min, Max;
        /// <summary>Convex shapes: outward unit normal (XYZ) and offset (W); a point is inside when every dot(N, p) - W &lt; 0.</summary>
        public readonly List<Vector4> Planes = new List<Vector4>();
        /// <summary>Mesh shapes: the triangles overlapping the cloth's bounds.</summary>
        public readonly List<ClothTriangle> Triangles = new List<ClothTriangle>();
        /// <summary>Centre of mass and velocity of a moving body (zero for statics), so the cloth can be dragged along.</summary>
        public Vector3 Center, LinearVelocity, AngularVelocity;

        public bool IsMoving => LinearVelocity != Vector3.Zero || AngularVelocity != Vector3.Zero;

        /// <summary>Velocity of the collider's surface at <paramref name="point"/>.</summary>
        public Vector3 VelocityAt(Vector3 point) => LinearVelocity + Vector3.Cross(AngularVelocity, point - Center);

        internal void Reset()
        {
            GameObject = null;
            Body = null;
            Static = null;
            Min = Max = Center = LinearVelocity = AngularVelocity = Vector3.Zero;
            Planes.Clear();
            Triangles.Clear();
        }
    }

    /// <summary>A world-space mesh triangle with what <see cref="Cloth"/> needs to test particles against it.</summary>
    public readonly struct ClothTriangle
    {
        public readonly Vector3 A, Edge0, Edge1, Normal, Min, Max;
        private readonly float _d00, _d01, _d11, _inverseDenominator;

        public ClothTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            Edge0 = b - a;
            Edge1 = c - a;
            Vector3 normal = Vector3.Cross(Edge0, Edge1);
            float length = normal.Length();
            Normal = length > 1e-12f ? normal / length : Vector3.Zero;
            Min = Vector3.Min(a, Vector3.Min(b, c));
            Max = Vector3.Max(a, Vector3.Max(b, c));
            _d00 = Vector3.Dot(Edge0, Edge0);
            _d01 = Vector3.Dot(Edge0, Edge1);
            _d11 = Vector3.Dot(Edge1, Edge1);
            float denominator = _d00 * _d11 - _d01 * _d01;
            _inverseDenominator = denominator > 1e-20f ? 1 / denominator : 0;
        }

        /// <summary>False for a zero-area triangle, which nothing can touch.</summary>
        public bool IsValid => Normal != Vector3.Zero && _inverseDenominator > 0;

        /// <summary>Whether <paramref name="point"/>, projected onto the triangle's plane, lies inside it.</summary>
        public bool Contains(Vector3 point)
        {
            const float tolerance = -1e-4f; // closes the seams between neighbouring triangles
            Vector3 offset = point - A;
            float d20 = Vector3.Dot(offset, Edge0);
            float d21 = Vector3.Dot(offset, Edge1);
            float v = (_d11 * d20 - _d01 * d21) * _inverseDenominator;
            if (v < tolerance) return false;
            float w = (_d00 * d21 - _d01 * d20) * _inverseDenominator;
            return w >= tolerance && v + w <= 1 - tolerance;
        }
    }

    /// <summary>The colliders near one cloth; their objects are reused from frame to frame.</summary>
    public sealed class ClothColliderList
    {
        private readonly List<ClothCollider> _items = new List<ClothCollider>();

        public int Count { get; private set; }
        public ClothCollider this[int index] => _items[index];

        public void Clear() => Count = 0;

        internal ClothCollider Add()
        {
            if (Count == _items.Count) _items.Add(new ClothCollider());
            ClothCollider collider = _items[Count++];
            collider.Reset();
            return collider;
        }

        internal void RemoveLast()
        {
            if (Count > 0) Count--;
        }

        /// <summary>Drops one collider (the order changes: the last takes its place).</summary>
        internal void RemoveAt(int index)
        {
            int last = Count - 1;
            (_items[index], _items[last]) = (_items[last], _items[index]);
            Count--;
        }
    }
}
