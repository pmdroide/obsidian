using System;
using System.Collections.Generic;
using Engine.Entities;
using Microsoft.Xna.Framework;

namespace Engine.Physics
{
    /// <summary>Which particles of a cloth hold still (they follow the gameobject) instead of falling. Saved by name: never rename.</summary>
    public enum ClothPinMode
    {
        /// <summary>Nothing: the whole cloth falls and drapes over whatever it lands on.</summary>
        None,
        /// <summary>The highest row in the model's Z: a curtain or a banner on a bar.</summary>
        TopEdge,
        /// <summary>The two ends of the highest row: a sagging banner, washing on a line.</summary>
        TopCorners,
        /// <summary>The outline of the mesh: a trampoline, a tent roof.</summary>
        Border,
    }

    /// <summary>
    /// The particles and constraints of a cloth mesh. Vertices at the same position (the two sides of a
    /// double-sided mesh, UV seams) become one particle. Every triangle edge keeps its length (stretch), and the
    /// far corners of two triangles sharing an edge keep theirs too, which resists folding (bend).
    /// </summary>
    public sealed class ClothTopology
    {
        /// <summary>Model-space rest position of each particle.</summary>
        public readonly Vector3[] RestPositions;
        /// <summary>The particle of each input vertex; -1 for a vertex no triangle uses.</summary>
        public readonly int[] VertexParticles;
        /// <summary>Distinct triangles, three particles each, wound like the first copy found.</summary>
        public readonly int[] Triangles;
        /// <summary>Stretch constraints: pairs of particles (every triangle edge).</summary>
        public readonly int[] Edges;
        /// <summary>Bend constraints: pairs of particles (far corners of neighbouring triangles).</summary>
        public readonly int[] Bends;
        /// <summary>True for particles on the outline (an edge only one triangle uses).</summary>
        public readonly bool[] OnBorder;

        public int ParticleCount => RestPositions.Length;

        private ClothTopology(Vector3[] restPositions, int[] vertexParticles, int[] triangles, int[] edges, int[] bends, bool[] onBorder)
        {
            RestPositions = restPositions;
            VertexParticles = vertexParticles;
            Triangles = triangles;
            Edges = edges;
            Bends = bends;
            OnBorder = onBorder;
        }

        /// <summary>Builds the topology of a triangle list (three vertex indices per triangle).</summary>
        public static ClothTopology Build(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices)
        {
            // Weld the vertices the triangles use.
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            for (int i = 0; i < indices.Count; i++)
            {
                min = Vector3.Min(min, vertices[indices[i]]);
                max = Vector3.Max(max, vertices[indices[i]]);
            }
            float tolerance = indices.Count > 0 ? Math.Max((max - min).Length() * 1e-5f, 1e-7f) : 1;
            var cells = new Dictionary<(long, long, long), int>();
            var rest = new List<Vector3>();
            var vertexParticles = new int[vertices.Count];
            Array.Fill(vertexParticles, -1);
            for (int i = 0; i < indices.Count; i++)
            {
                int vertex = indices[i];
                if (vertexParticles[vertex] >= 0) continue;
                Vector3 p = vertices[vertex];
                var key = ((long)MathF.Round(p.X / tolerance), (long)MathF.Round(p.Y / tolerance), (long)MathF.Round(p.Z / tolerance));
                if (!cells.TryGetValue(key, out int particle))
                {
                    particle = rest.Count;
                    rest.Add(p);
                    cells.Add(key, particle);
                }
                vertexParticles[vertex] = particle;
            }

            // Distinct triangles (both sides of a double-sided mesh are the same triangle), and who shares each edge.
            var seen = new HashSet<(int, int, int)>();
            var triangles = new List<int>();
            var edgeCorners = new Dictionary<(int, int), (int First, int Second, int Count)>();
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = vertexParticles[indices[i]], b = vertexParticles[indices[i + 1]], c = vertexParticles[indices[i + 2]];
                if (a == b || b == c || a == c || !seen.Add(Sorted(a, b, c))) continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                AddEdge(a, b, c);
                AddEdge(b, c, a);
                AddEdge(c, a, b);
            }

            var edges = new List<int>(edgeCorners.Count * 2);
            var bendPairs = new HashSet<(int, int)>();
            var bends = new List<int>();
            var onBorder = new bool[rest.Count];
            foreach (var (edge, corners) in edgeCorners)
            {
                edges.Add(edge.Item1);
                edges.Add(edge.Item2);
                if (corners.Count == 1)
                {
                    onBorder[edge.Item1] = true;
                    onBorder[edge.Item2] = true;
                }
                else if (corners.Count == 2)
                {
                    var pair = (Math.Min(corners.First, corners.Second), Math.Max(corners.First, corners.Second));
                    if (pair.Item1 == pair.Item2 || edgeCorners.ContainsKey(pair) || !bendPairs.Add(pair)) continue;
                    bends.Add(pair.Item1);
                    bends.Add(pair.Item2);
                }
            }
            return new ClothTopology(rest.ToArray(), vertexParticles, triangles.ToArray(), edges.ToArray(), bends.ToArray(), onBorder);

            void AddEdge(int a, int b, int corner)
            {
                var key = (Math.Min(a, b), Math.Max(a, b));
                edgeCorners[key] = edgeCorners.TryGetValue(key, out var found)
                    ? (found.First, found.Count == 1 ? corner : found.Second, found.Count + 1)
                    : (corner, -1, 1);
            }
        }

        private static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        /// <summary>The particles <paramref name="pin"/> holds, from their model-space rest positions.</summary>
        public List<int> PinnedParticles(ClothPinMode pin)
        {
            var pinned = new List<int>();
            if (ParticleCount == 0 || pin == ClothPinMode.None) return pinned;
            if (pin == ClothPinMode.Border)
            {
                for (int i = 0; i < ParticleCount; i++)
                    if (OnBorder[i]) pinned.Add(i);
                return pinned;
            }

            Vector3 min = RestPositions[0], max = RestPositions[0];
            foreach (Vector3 p in RestPositions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            float tolerance = Math.Max((max - min).Length() * 1e-3f, 1e-6f);
            for (int i = 0; i < ParticleCount; i++)
                if (RestPositions[i].Z >= max.Z - tolerance) pinned.Add(i);
            if (pin != ClothPinMode.TopCorners || pinned.Count < 2) return pinned;

            // The two ends of the top row, along its longer horizontal direction.
            bool alongX = max.X - min.X >= max.Y - min.Y;
            int first = pinned[0], last = pinned[0];
            foreach (int i in pinned)
            {
                float value = alongX ? RestPositions[i].X : RestPositions[i].Y;
                if (value < (alongX ? RestPositions[first].X : RestPositions[first].Y)) first = i;
                if (value > (alongX ? RestPositions[last].X : RestPositions[last].Y)) last = i;
            }
            pinned.Clear();
            pinned.Add(first);
            if (last != first) pinned.Add(last);
            return pinned;
        }
    }

    /// <summary>
    /// Cloth simulated as particles (extended position-based dynamics in small substeps): gravity, air drag and
    /// wind move them, stretch and bend constraints hold the shape, and <see cref="Colliders"/> (bodies and
    /// statics near the cloth, see <see cref="ScenePhysics.CollectClothColliders"/>) push them out, with
    /// friction. Pinned particles follow the gameobject's transform. The cloth doesn't push bodies back.
    /// Positions are in world space. Game thread only.
    /// </summary>
    public sealed class Cloth
    {
        /// <summary>Simulation steps per frame; small steps keep a stiff cloth stable with one solver pass each.</summary>
        public const int Substeps = 10;
        /// <summary>Particles never move faster than this (m/s).</summary>
        public const float MaxSpeed = 40f;
        // A frame hitch must not launch the cloth through a collider.
        private const float MaxStep = 1f / 30f;
        private const float AirDensity = 1.2f;
        private const float DragCoefficient = 1.2f;

        public ClothTopology Topology { get; }

        /// <summary>How well the cloth keeps its size: 1 doesn't stretch, 0 is elastic.</summary>
        public float Stiffness = 0.95f;
        /// <summary>How much it resists folding: 0 is silk, 1 is cardboard.</summary>
        public float Bend = 0.3f;
        /// <summary>Share of its speed lost per second, besides air drag.</summary>
        public float Damping = 0.1f;
        /// <summary>Coulomb friction against colliders (0 slides off everything).</summary>
        public float Friction = 0.5f;
        /// <summary>Distance (m) particles keep from colliders.</summary>
        public float Thickness = 0.03f;
        public Vector3 Gravity = new Vector3(0, 0, -9.81f);
        /// <summary>Air velocity (m/s). Still air still slows the cloth down.</summary>
        public Vector3 Wind;

        /// <summary>What the cloth collides with this frame; filled by the caller before <see cref="Step"/>.</summary>
        public ClothColliderList Colliders { get; } = new ClothColliderList();

        private readonly Vector3[] _position, _previous, _velocity;
        private readonly float[] _inverseMass;
        private readonly bool[] _touched;
        // This substep's contact of each particle: the collider's normal and surface velocity.
        private readonly bool[] _inContact;
        private readonly Vector3[] _contactNormal, _contactVelocity;
        private readonly float[] _edgeRest, _bendRest;
        private int[] _pinned = Array.Empty<int>();
        private Vector3[] _pinFrom = Array.Empty<Vector3>(), _pinTo = Array.Empty<Vector3>();
        private readonly HashSet<BasicEntity> _touching = new HashSet<BasicEntity>();
        private Matrix _world;
        private float _mass;
        private float _time;

        public Cloth(ClothTopology topology, Matrix world, ClothPinMode pin = ClothPinMode.TopEdge, float mass = 1f)
        {
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            int count = topology.ParticleCount;
            _position = new Vector3[count];
            _previous = new Vector3[count];
            _velocity = new Vector3[count];
            _inverseMass = new float[count];
            _touched = new bool[count];
            _inContact = new bool[count];
            _contactNormal = new Vector3[count];
            _contactVelocity = new Vector3[count];
            _world = world;
            for (int i = 0; i < count; i++) _position[i] = _previous[i] = Vector3.Transform(topology.RestPositions[i], world);

            // Rest lengths in world units: the gameobject's scale is part of the cloth's size.
            _edgeRest = RestLengths(topology.Edges);
            _bendRest = RestLengths(topology.Bends);
            _mass = mass;
            SetPin(pin);
            UpdateBounds();
        }

        public int ParticleCount => _position.Length;
        /// <summary>World-space particle positions.</summary>
        public ReadOnlySpan<Vector3> Positions => _position;
        public ReadOnlySpan<Vector3> Velocities => _velocity;
        public ClothPinMode Pin { get; private set; }
        public int PinnedCount => _pinned.Length;
        public float Mass => _mass;
        /// <summary>World-space bounds of the particles after the last step.</summary>
        public BoundingBox Bounds { get; private set; }
        /// <summary>Speed (m/s) of the fastest particle after the last step.</summary>
        public float TopSpeed { get; private set; }
        /// <summary>Particles a collider pushed during the last step.</summary>
        public int ContactCount { get; private set; }
        /// <summary>Gameobjects the cloth touched during the last step.</summary>
        public IReadOnlyCollection<BasicEntity> Touching => _touching;

        public bool IsPinned(int particle) => _inverseMass[particle] == 0;
        public bool IsTouched(int particle) => _touched[particle];

        /// <summary>Total mass in kilograms, shared by the free particles.</summary>
        public void SetMass(float mass)
        {
            _mass = float.IsFinite(mass) ? Math.Max(mass, 1e-3f) : 1f;
            UpdateInverseMasses();
        }

        public void SetPin(ClothPinMode pin)
        {
            Pin = pin;
            _pinned = Topology.PinnedParticles(pin).ToArray();
            _pinFrom = new Vector3[_pinned.Length];
            _pinTo = new Vector3[_pinned.Length];
            UpdateInverseMasses();
        }

        private void UpdateInverseMasses()
        {
            int free = ParticleCount - _pinned.Length;
            float inverse = free > 0 ? free / Math.Max(_mass, 1e-3f) : 0;
            Array.Fill(_inverseMass, inverse);
            foreach (int i in _pinned) _inverseMass[i] = 0;
        }

        /// <summary>Puts every particle back at its rest position under <paramref name="world"/>, at rest.</summary>
        public void Reset(Matrix world)
        {
            _world = world;
            for (int i = 0; i < ParticleCount; i++)
            {
                _position[i] = _previous[i] = Vector3.Transform(Topology.RestPositions[i], world);
                _velocity[i] = Vector3.Zero;
            }
            UpdateBounds();
        }

        /// <summary>
        /// Instant change of momentum (kg*m/s) spread over the free particles within <paramref name="radius"/> of
        /// <paramref name="point"/>, more at the centre (the nearest particle when none is that close).
        /// </summary>
        public void AddImpulse(Vector3 impulse, Vector3 point, float radius = 0.5f)
        {
            if (!IsFinite(impulse) || !IsFinite(point)) return;
            radius = float.IsFinite(radius) ? Math.Max(radius, 1e-3f) : 0.5f;
            float total = 0;
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < ParticleCount; i++)
            {
                if (_inverseMass[i] == 0) continue;
                float distance = Vector3.Distance(_position[i], point);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
                total += Math.Max(0, 1 - distance / radius);
            }
            if (nearest < 0) return;
            if (!(total > 0))
            {
                _velocity[nearest] += impulse * _inverseMass[nearest];
                return;
            }
            for (int i = 0; i < ParticleCount; i++)
            {
                if (_inverseMass[i] == 0) continue;
                float weight = Math.Max(0, 1 - Vector3.Distance(_position[i], point) / radius);
                if (weight > 0) _velocity[i] += impulse * (weight / total * _inverseMass[i]);
            }
        }

        /// <summary>
        /// Advances the cloth by <paramref name="dt"/> seconds (at most 1/30). Pinned particles move from the last
        /// transform to <paramref name="world"/> over the step. False when the cloth blew up and was reset.
        /// </summary>
        public bool Step(float dt, Matrix world)
        {
            if (!(dt > 0)) return true;
            dt = Math.Min(dt, MaxStep);
            float h = dt / Substeps;
            for (int k = 0; k < _pinned.Length; k++)
            {
                Vector3 rest = Topology.RestPositions[_pinned[k]];
                _pinFrom[k] = Vector3.Transform(rest, _world);
                _pinTo[k] = Vector3.Transform(rest, world);
            }
            _world = world;

            float stretchAlpha = Compliance(Stiffness, 0.01f) / (h * h);
            float bendAlpha = Compliance(Bend, 1f) / (h * h);
            float damping = Math.Clamp(float.IsFinite(Damping) ? Damping : 0.1f, 0, 0.999f);
            float keep = MathF.Pow(1 - damping, h);
            Vector3 gravity = IsFinite(Gravity) ? Gravity : Vector3.Zero;
            float thickness = float.IsFinite(Thickness) ? Math.Clamp(Thickness, 0, 1) : 0.03f;
            float friction = float.IsFinite(Friction) ? Math.Max(Friction, 0) : 0.5f;

            Array.Clear(_touched);
            _touching.Clear();
            for (int s = 0; s < Substeps; s++)
            {
                _time += h;
                ApplyAir(h);
                for (int i = 0; i < ParticleCount; i++)
                {
                    _previous[i] = _position[i];
                    if (_inverseMass[i] == 0) continue;
                    Vector3 v = (_velocity[i] + gravity * h) * keep;
                    float speed = v.Length();
                    if (speed > MaxSpeed) v *= MaxSpeed / speed;
                    _position[i] += v * h;
                }
                float t = (s + 1) / (float)Substeps;
                for (int k = 0; k < _pinned.Length; k++) _position[_pinned[k]] = Vector3.Lerp(_pinFrom[k], _pinTo[k], t);

                // Cloth hardly resists being pushed together: it buckles (bends) instead of storing the push like a spring.
                Solve(Topology.Edges, _edgeRest, stretchAlpha, Math.Max(stretchAlpha, bendAlpha));
                Solve(Topology.Bends, _bendRest, bendAlpha, bendAlpha);
                Array.Clear(_inContact);
                Collide(h, thickness, friction);

                float inverseH = 1 / h;
                for (int i = 0; i < ParticleCount; i++)
                {
                    Vector3 v = (_position[i] - _previous[i]) * inverseH;
                    if (_inContact[i])
                    {
                        // Inelastic contact: being pushed out of a collider must not bounce the particle off it.
                        float separating = Vector3.Dot(v - _contactVelocity[i], _contactNormal[i]);
                        if (separating > 0) v -= _contactNormal[i] * separating;
                    }
                    float speed = v.Length();
                    _velocity[i] = speed > MaxSpeed ? v * (MaxSpeed / speed) : v;
                }
            }

            int touched = 0;
            float topSpeed = 0;
            for (int i = 0; i < ParticleCount; i++)
            {
                if (_touched[i]) touched++;
                topSpeed = Math.Max(topSpeed, _velocity[i].LengthSquared());
            }
            ContactCount = touched;
            TopSpeed = MathF.Sqrt(topSpeed);

            for (int i = 0; i < ParticleCount; i++)
            {
                if (IsFinite(_position[i])) continue;
                Reset(world);
                return false;
            }
            UpdateBounds();
            return true;
        }

        // Inverse stiffness (m/N) for a 0..1 setting: 1 is rigid.
        private static float Compliance(float setting, float softest)
        {
            float s = float.IsFinite(setting) ? Math.Clamp(setting, 0, 1) : 0.5f;
            float soft = 1 - s;
            return softest * soft * soft * soft;
        }

        /// <param name="alpha">Compliance over h^2 against stretching.</param>
        /// <param name="compressAlpha">Compliance over h^2 against being pushed together.</param>
        private void Solve(int[] pairs, float[] restLengths, float alpha, float compressAlpha)
        {
            for (int c = 0, n = restLengths.Length; c < n; c++)
            {
                int a = pairs[c * 2], b = pairs[c * 2 + 1];
                float wa = _inverseMass[a], wb = _inverseMass[b];
                float w = wa + wb;
                if (w == 0) continue;
                Vector3 d = _position[b] - _position[a];
                float length = d.Length();
                if (length < 1e-9f) continue;
                // One XPBD iteration per substep (the multiplier starts at zero each substep).
                float rest = restLengths[c];
                float lambda = (rest - length) / (w + (length < rest ? compressAlpha : alpha));
                Vector3 correction = d * (lambda / length);
                _position[a] -= correction * wa;
                _position[b] += correction * wb;
            }
        }

        /// <summary>Quadratic drag of each triangle against the air (the wind, gusting), shared by its corners.</summary>
        private void ApplyAir(float h)
        {
            Vector3 wind = IsFinite(Wind) ? Wind : Vector3.Zero;
            float windSpeed = wind.Length();
            Vector3 windDirection = windSpeed > 0 ? wind / windSpeed : Vector3.Zero;
            float slow = 0.6f + 0.4f * MathF.Sin(_time * 0.43f);
            int[] triangles = Topology.Triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 cross = Vector3.Cross(_position[b] - _position[a], _position[c] - _position[a]);
                float twiceArea = cross.Length();
                if (twiceArea < 1e-12f) continue;
                Vector3 normal = cross / twiceArea;
                Vector3 air = wind;
                if (windSpeed > 0)
                {
                    // Gusts roll across the cloth along the wind.
                    Vector3 center = (_position[a] + _position[b] + _position[c]) / 3;
                    air *= 1 + 0.35f * slow * MathF.Sin(_time * 1.7f + Vector3.Dot(center, windDirection) * 0.9f);
                }
                Vector3 relative = air - (_velocity[a] + _velocity[b] + _velocity[c]) / 3;
                float normalSpeed = Vector3.Dot(normal, relative);
                if (normalSpeed == 0) continue;
                float impulse = 0.5f * AirDensity * DragCoefficient * (twiceArea * 0.5f) * normalSpeed * Math.Abs(normalSpeed) * h / 3;
                Push(a, normal, impulse, normalSpeed);
                Push(b, normal, impulse, normalSpeed);
                Push(c, normal, impulse, normalSpeed);
            }

            // Never faster than the air itself, however light the particle.
            void Push(int i, Vector3 normal, float impulse, float limit)
            {
                float dv = impulse * _inverseMass[i];
                if (Math.Abs(dv) > Math.Abs(limit)) dv = limit;
                _velocity[i] += normal * dv;
            }
        }

        private void Collide(float h, float thickness, float friction)
        {
            for (int ci = 0; ci < Colliders.Count; ci++)
            {
                ClothCollider collider = Colliders[ci];
                Vector3 min = collider.Min - new Vector3(thickness), max = collider.Max + new Vector3(thickness);
                bool convex = collider.Planes.Count > 0;
                for (int i = 0; i < ParticleCount; i++)
                {
                    if (_inverseMass[i] == 0) continue;
                    Vector3 p = _position[i];
                    if (p.X < min.X || p.Y < min.Y || p.Z < min.Z || p.X > max.X || p.Y > max.Y || p.Z > max.Z) continue;

                    float depth;
                    Vector3 normal;
                    if (convex)
                    {
                        if (!PushOutConvex(collider.Planes, ref p, thickness, out normal, out depth)) continue;
                    }
                    else if (!PushOutTriangles(collider.Triangles, _previous[i], ref p, thickness, out normal, out depth)) continue;

                    // Coulomb friction: the slide along the surface (relative to it, for a moving body) shrinks by
                    // up to Friction times how far the particle was pushed out.
                    Vector3 surface = collider.IsMoving ? collider.VelocityAt(p) * h : Vector3.Zero;
                    Vector3 slide = p - _previous[i] - surface;
                    slide -= normal * Vector3.Dot(slide, normal);
                    float length = slide.Length();
                    float limit = friction * depth;
                    if (length > 0) p -= length <= limit ? slide : slide * (limit / length);

                    _position[i] = p;
                    _inContact[i] = true;
                    _contactNormal[i] = normal;
                    _contactVelocity[i] = collider.IsMoving ? collider.VelocityAt(p) : Vector3.Zero;
                    _touched[i] = true;
                    if (collider.GameObject != null) _touching.Add(collider.GameObject);
                }
            }
        }

        /// <summary>Inside (or within thickness of) every plane: out through the nearest one.</summary>
        private static bool PushOutConvex(List<Vector4> planes, ref Vector3 p, float thickness, out Vector3 normal, out float depth)
        {
            normal = Vector3.Zero;
            depth = 0;
            float nearest = float.MinValue;
            for (int k = 0; k < planes.Count; k++)
            {
                Vector4 plane = planes[k];
                float distance = plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z - plane.W;
                if (distance >= thickness) return false; // outside, clear of the collider
                if (distance > nearest)
                {
                    nearest = distance;
                    normal = new Vector3(plane.X, plane.Y, plane.Z);
                }
            }
            if (normal == Vector3.Zero) return false;
            depth = thickness - nearest;
            p += normal * depth;
            return true;
        }

        /// <summary>
        /// Keeps the particle on the side of each triangle it started the substep on, at least thickness away.
        /// Two-sided, so open meshes (a floor plane) work too; a fast particle is caught where its path crossed.
        /// </summary>
        private static bool PushOutTriangles(List<ClothTriangle> triangles, Vector3 previous, ref Vector3 p, float thickness,
            out Vector3 normal, out float depth)
        {
            normal = Vector3.Zero;
            depth = 0;
            bool hit = false;
            for (int k = 0; k < triangles.Count; k++)
            {
                ClothTriangle triangle = triangles[k];
                Vector3 low = Vector3.Min(p, previous) - new Vector3(thickness), high = Vector3.Max(p, previous) + new Vector3(thickness);
                if (high.X < triangle.Min.X || high.Y < triangle.Min.Y || high.Z < triangle.Min.Z ||
                    low.X > triangle.Max.X || low.Y > triangle.Max.Y || low.Z > triangle.Max.Z) continue;

                float before = Vector3.Dot(previous - triangle.A, triangle.Normal);
                float side = before >= 0 ? 1 : -1;
                before *= side;
                float after = Vector3.Dot(p - triangle.A, triangle.Normal) * side;
                if (after >= thickness) continue;
                Vector3 test = after < 0 && before - after > 1e-9f ? previous + (p - previous) * (before / (before - after)) : p;
                if (!triangle.Contains(test)) continue;

                Vector3 n = triangle.Normal * side;
                float push = thickness - after;
                p += n * push;
                if (push > depth)
                {
                    depth = push;
                    normal = n;
                }
                hit = true;
            }
            return hit;
        }

        private void UpdateBounds()
        {
            if (ParticleCount == 0)
            {
                Bounds = new BoundingBox(Vector3.Transform(Vector3.Zero, _world), Vector3.Transform(Vector3.Zero, _world));
                return;
            }
            Vector3 min = _position[0], max = _position[0];
            for (int i = 1; i < ParticleCount; i++)
            {
                min = Vector3.Min(min, _position[i]);
                max = Vector3.Max(max, _position[i]);
            }
            Bounds = new BoundingBox(min, max);
        }

        private float[] RestLengths(int[] pairs)
        {
            var lengths = new float[pairs.Length / 2];
            for (int c = 0; c < lengths.Length; c++)
                lengths[c] = Vector3.Distance(_position[pairs[c * 2]], _position[pairs[c * 2 + 1]]);
            return lengths;
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }
}
