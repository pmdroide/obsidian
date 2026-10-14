using Microsoft.Xna.Framework;

namespace Engine.Particles;

/// <summary>A bullet tunnel or blast hole through a volume: a capsule that closes again over the Refill Time.</summary>
public struct ParticleVolumeCarve
{
    public Vector3 From;
    public Vector3 To;
    public float Radius;
    /// <summary>Seconds since it was made.</summary>
    public float Age;
}

/// <summary>
/// The density grid of a volumetric particle system (<see cref="ParticleVolumetricModule"/>), updated by
/// <see cref="ParticleSimulation.Step"/>. Particles: each particle adds a soft ball of density to a grid fitted
/// around them every step. Voxels (Counter-Strike 2 smoke): from the emitter the smoke floods outwards cell by
/// cell until it fills Volume, a step between two of the 26 neighbours only taken when no Static collider lies
/// between their centres, so it fills rooms, flows through doorways and stays out of walls. Cells fill nearest
/// first: by straight-line distance (heights count Flatten times) where nothing is in the way, so an open smoke is
/// a round dome, and by the path around walls and through doors where that is longer; a little low-frequency
/// noise makes the outline billow. Once the system stops emitting it dissipates from the outside in.
/// GPU-free: the renderer uploads <see cref="Cells"/> into a 3D texture when <see cref="Version"/> changes, and
/// cuts the <see cref="Carves"/> out of it per pixel.
/// </summary>
public sealed class ParticleVolume
{
    public const int MaxSize = ParticleVolumetricModule.MaxResolution;
    /// <summary>Cell alpha stores density / this, so overlapping particles have headroom above 1.</summary>
    public const float DensityRange = 2f;
    // Seconds a voxel takes to fade in once the flood reaches it.
    private const float CellFadeIn = 0.3f;
    // Metres the fill order wanders, so the outline billows instead of being a perfect dome.
    private const float Lumps = 0.35f;
    // Outer share of the filled voxels (by fill order) that thins towards the edge.
    private const float EdgeShare = 0.55f;

    private float[] _density = Array.Empty<float>();
    private Vector3[] _color = Array.Empty<Vector3>();
    private Color[] _cells = Array.Empty<Color>();
    private readonly List<ParticleVolumeCarve> _carves = new();

    // Voxel flood fill
    private bool _seeded, _fading;
    private Vector3 _seed;
    private float[] _cost = Array.Empty<float>();
    private float[] _born = Array.Empty<float>();
    private byte[] _filledFlag = Array.Empty<byte>();
    private byte[] _sun = Array.Empty<byte>();
    private readonly List<int> _filled = new();
    private readonly PriorityQueue<int, float> _frontier = new();
    private int _target;
    private float _lumpSeed, _flatten = 1.5f;
    private float _time, _fillTime, _fadeTime, _lastFill = float.NegativeInfinity;
    private int _packedCount = -1;
    private float _packedTint;

    public int SizeX { get; private set; }
    public int SizeY { get; private set; }
    public int SizeZ { get; private set; }
    /// <summary>World position of the grid's minimum corner.</summary>
    public Vector3 Origin { get; private set; }
    /// <summary>Edge of one cell, metres.</summary>
    public float CellSize { get; private set; } = 1f;
    /// <summary>
    /// One per cell, x fastest then y then z, density / <see cref="DensityRange"/> in alpha. Particles: RGB is the
    /// sRGB colour. Voxels (<see cref="UniformColor"/>): the colour is the system's Start Color and R is how much
    /// sun reaches the voxel (0 under a roof).
    /// </summary>
    public Color[] Cells => _cells;
    /// <summary>Voxels: every cell has the system's Start Color; R holds the sunlight instead.</summary>
    public bool UniformColor { get; private set; }
    /// <summary>Seconds the volume has run (paused with the system): drives the drifting detail noise.</summary>
    public float Time => _time;
    /// <summary>Changes whenever <see cref="Cells"/> or the grid change.</summary>
    public int Version { get; private set; }
    /// <summary>Something to draw.</summary>
    public bool HasDensity { get; private set; }
    public BoundingBox Bounds => new(Origin, Origin + new Vector3(SizeX, SizeY, SizeZ) * CellSize);
    public IReadOnlyList<ParticleVolumeCarve> Carves => _carves;

    /// <summary>Voxels: cells the smoke fills so far.</summary>
    public int FilledVoxels => _filled.Count;
    /// <summary>Voxels: cells the smoke fills when fully bloomed (Volume / voxel size³, at most the grid).</summary>
    public int TargetVoxels => _seeded ? _target : 0;
    /// <summary>Voxels: where the flood started (the centre of its first cell).</summary>
    public Vector3 Seed => _seed;
    /// <summary>Voxels: the system stopped emitting and the smoke is thinning out.</summary>
    public bool IsDissipating => _seeded && _fading;

    /// <summary>Removes the volume and its holes (the next emission floods again from the emitter).</summary>
    public void Clear()
    {
        _carves.Clear();
        ResetVoxels();
        if (!HasDensity) return;
        HasDensity = false;
        Version++;
    }

    /// <summary>Cuts a tunnel of <paramref name="radius"/> from <paramref name="from"/> to <paramref name="to"/> (a bullet), or a ball when they are equal (a blast).</summary>
    public void Carve(Vector3 from, Vector3 to, float radius)
    {
        if (!IsFinite(from) || !IsFinite(to) || !(radius > 0) || !float.IsFinite(radius)) return;
        if (_carves.Count >= ParticleVolumetricModule.MaxCarves) _carves.RemoveAt(0); // the oldest has mostly refilled
        _carves.Add(new ParticleVolumeCarve { From = from, To = to, Radius = Math.Min(radius, 100f) });
    }

    /// <summary>
    /// 0..1: how much a hole leaves of the volume at <paramref name="point"/> (0 in a fresh hole). Holes shrink as
    /// they refill; the shader cuts the same shape.
    /// </summary>
    public float CarveFactor(Vector3 point, float refillTime)
    {
        float factor = 1;
        float refill = Math.Max(refillTime, 0.01f);
        foreach (ParticleVolumeCarve carve in _carves)
        {
            float strength = 1 - carve.Age / refill;
            if (strength <= 0) continue;
            float radius = carve.Radius * (float)Math.Sqrt(strength);
            float distance = DistanceToSegment(point, carve.From, carve.To);
            factor *= MathHelper.Clamp((distance - radius * 0.6f) / (radius * 0.4f), 0, 1);
            if (factor <= 0) return 0;
        }
        return factor;
    }

    /// <summary>Density at a world point (0 outside; about 1 inside a full smoke), holes included, detail noise not.</summary>
    public float DensityAt(Vector3 point, float refillTime)
    {
        if (!HasDensity) return 0;
        Vector3 cell = (point - Origin) / CellSize;
        int x = (int)Math.Floor(cell.X), y = (int)Math.Floor(cell.Y), z = (int)Math.Floor(cell.Z);
        if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ) return 0;
        float density = _cells[x + SizeX * (y + SizeY * z)].A / 255f * DensityRange;
        return density > 0 && _carves.Count > 0 ? density * CarveFactor(point, refillTime) : density;
    }

    /// <summary>Share of light (0..1) that gets through the volume between two points; <paramref name="extinction"/> is the module's Density.</summary>
    public float Transmittance(Vector3 from, Vector3 to, float extinction, float refillTime)
    {
        if (!HasDensity || !(extinction > 0)) return 1;
        Vector3 delta = to - from;
        float length = delta.Length();
        if (!(length > 1e-4f)) return 1;
        var ray = new Ray(from, delta / length);
        BoundingBox bounds = Bounds;
        float? enter = bounds.Contains(from) == ContainmentType.Contains ? 0 : ray.Intersects(bounds);
        if (enter == null || enter.Value >= length) return 1;
        float start = enter.Value;
        int steps = (int)Math.Clamp(Math.Ceiling((length - start) / (CellSize * 0.5f)), 1, 1024);
        float step = (length - start) / steps, optical = 0;
        for (int i = 0; i < steps; i++)
            optical += DensityAt(from + ray.Direction * (start + (i + 0.5f) * step), refillTime) * step;
        return (float)Math.Exp(-optical * extinction);
    }

    // -------------------------------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------------------------------

    internal void Update(ParticleSimulation simulation, float dt, ParticleStepContext context)
    {
        ParticleVolumetricModule settings = simulation.System.Volumetric;
        _time += dt;
        float refill = Math.Max(float.IsFinite(settings.RefillTime) ? settings.RefillTime : 2.5f, 0.01f);
        for (int i = _carves.Count - 1; i >= 0; i--)
        {
            ParticleVolumeCarve carve = _carves[i];
            carve.Age += dt;
            if (carve.Age >= refill) _carves.RemoveAt(i);
            else _carves[i] = carve;
        }

        if (!settings.Enabled)
        {
            Clear();
            return;
        }
        UniformColor = settings.Source == ParticleVolumeSource.Voxels;
        if (UniformColor) UpdateVoxels(simulation, settings, dt, context);
        else
        {
            ResetVoxels();
            BuildFromParticles(simulation.Particles, settings);
        }
    }

    private void BuildFromParticles(ReadOnlySpan<Particle> particles, ParticleVolumetricModule settings)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        int contributing = 0;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle p = ref particles[i];
            float radius = p.Size * 0.5f;
            if (!(radius > 0) || !(p.Color.W > 0.001f) || !IsFinite(p.Position)) continue;
            min = Vector3.Min(min, p.Position - new Vector3(radius));
            max = Vector3.Max(max, p.Position + new Vector3(radius));
            contributing++;
        }
        if (contributing == 0)
        {
            if (HasDensity)
            {
                HasDensity = false;
                Version++;
            }
            return;
        }

        // Quarter-octave cell sizes and a grid snapped to them, so the cells don't swim as the bounds change.
        int resolution = settings.ClampedResolution;
        Vector3 extent = max - min;
        float longest = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        float cell = Math.Max(longest / resolution, 0.02f);
        cell = (float)Math.Pow(2, Math.Ceiling(Math.Log2(cell) * 4) / 4);
        // One empty cell around the particles, so the volume fades out before the box ends.
        Vector3 origin = new Vector3((float)Math.Floor(min.X / cell), (float)Math.Floor(min.Y / cell), (float)Math.Floor(min.Z / cell)) * cell - new Vector3(cell);
        int sx = Math.Clamp((int)Math.Ceiling((max.X - origin.X) / cell) + 1, 2, MaxSize);
        int sy = Math.Clamp((int)Math.Ceiling((max.Y - origin.Y) / cell) + 1, 2, MaxSize);
        int sz = Math.Clamp((int)Math.Ceiling((max.Z - origin.Z) / cell) + 1, 2, MaxSize);
        Resize(sx, sy, sz);
        Origin = origin;
        CellSize = cell;
        int count = sx * sy * sz;
        Array.Clear(_density, 0, count);
        Array.Clear(_color, 0, count);

        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle p = ref particles[i];
            float radius = p.Size * 0.5f;
            if (!(radius > 0) || !(p.Color.W > 0.001f) || !IsFinite(p.Position)) continue;
            // Particles smaller than a cell still land in one: same amount, spread over a cell.
            float reach = Math.Max(radius, cell * 0.75f);
            float weight = Math.Min(p.Color.W, 1) * (radius < reach ? (float)Math.Pow(radius / reach, 3) : 1);
            Vector3 color = new(p.Color.X, p.Color.Y, p.Color.Z);
            Vector3 local = (p.Position - origin) / cell - new Vector3(0.5f);
            float r = reach / cell, inverse = 1 / (r * r);
            int x0 = Math.Max(0, (int)Math.Ceiling(local.X - r)), x1 = Math.Min(sx - 1, (int)Math.Floor(local.X + r));
            int y0 = Math.Max(0, (int)Math.Ceiling(local.Y - r)), y1 = Math.Min(sy - 1, (int)Math.Floor(local.Y + r));
            int z0 = Math.Max(0, (int)Math.Ceiling(local.Z - r)), z1 = Math.Min(sz - 1, (int)Math.Floor(local.Z + r));
            for (int z = z0; z <= z1; z++)
            {
                float dz = (z - local.Z) * (z - local.Z);
                for (int y = y0; y <= y1; y++)
                {
                    float dyz = dz + (y - local.Y) * (y - local.Y);
                    int row = sx * (y + sy * z);
                    for (int x = x0; x <= x1; x++)
                    {
                        float d2 = (dyz + (x - local.X) * (x - local.X)) * inverse;
                        if (d2 >= 1) continue;
                        float w = (1 - d2) * (1 - d2) * weight;
                        _density[row + x] += w;
                        _color[row + x] += color * w;
                    }
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            float d = _density[i];
            _cells[i] = d > 0 ? new Color(_color[i] / d) { A = ToByte(d / DensityRange) } : Color.Transparent;
        }
        HasDensity = true;
        Version++;
    }

    private void UpdateVoxels(ParticleSimulation simulation, ParticleVolumetricModule settings, float dt, ParticleStepContext context)
    {
        bool emitting = simulation.IsVolumeEmitting;
        // Starts on the first emission, and again when the system plays while the last smoke still thins out.
        if (emitting && (!_seeded || _fading)) SeedAt(simulation.VolumeSeed, settings);
        if (!_seeded)
        {
            if (HasDensity)
            {
                HasDensity = false;
                Version++;
            }
            return;
        }

        if (emitting) _fillTime += dt;
        else if (!_fading)
        {
            _fading = true;
            _fadeTime = 0;
        }
        if (_fading) _fadeTime += dt;

        float fade = _fading ? _fadeTime / Math.Max(float.IsFinite(settings.FadeTime) ? settings.FadeTime : 3f, 0.01f) : 0;
        if (fade >= 1)
        {
            ResetVoxels();
            HasDensity = false;
            Version++;
            return;
        }

        // Bloom out fast, then slow down (ease out) until the whole volume is filled.
        float expand = Math.Max(float.IsFinite(settings.ExpandTime) ? settings.ExpandTime : 1.4f, 0.01f);
        float progress = Math.Clamp(_fillTime / expand, 0, 1);
        int want = (int)Math.Ceiling(_target * (1 - Math.Pow(1 - progress, 3)));
        _flatten = float.IsFinite(settings.Flatten) ? Math.Clamp(settings.Flatten, 0.1f, 10f) : 1.5f;
        float radius = settings.ClampedMaxRadius;
        while (_filled.Count < want && _frontier.TryDequeue(out int cell, out _))
        {
            if (_filledFlag[cell] != 0) continue; // reached again by a shorter path before it was its turn
            _filledFlag[cell] = 1;
            _born[cell] = _time;
            _lastFill = _time;
            _filled.Add(cell);
            _sun[cell] = Sunlit(cell, context) ? (byte)255 : (byte)0;
            Spread(cell, radius, context?.RaycastStatic);
        }

        Pack(simulation.System.Main.StartColor.A / 255f, fade);
    }

    private void SeedAt(Vector3 seed, ParticleVolumetricModule settings)
    {
        float voxel = settings.ClampedVoxelSize, radius = settings.ClampedMaxRadius;
        int n = (int)Math.Ceiling(2 * radius / voxel) + 3;
        if (n > MaxSize)
        {
            n = MaxSize;
            voxel = 2 * radius / (MaxSize - 3);
        }
        Resize(n, n, n);
        int count = n * n * n;
        CellSize = voxel;
        int c = n / 2;
        // A grenade lies on the floor: start half a voxel above it.
        _seed = seed + new Vector3(0, 0, voxel * 0.5f);
        Origin = _seed - new Vector3((c + 0.5f) * voxel);
        Array.Fill(_cost, float.PositiveInfinity, 0, count);
        Array.Clear(_filledFlag, 0, count);
        Array.Clear(_cells, 0, count);
        _filled.Clear();
        _frontier.Clear();
        float volume = float.IsFinite(settings.Volume) ? Math.Max(settings.Volume, 0) : 110f;
        _target = (int)Math.Clamp(Math.Round(volume / (voxel * voxel * voxel)), 1, count);
        int start = c + n * (c + n * c);
        _cost[start] = 0;
        _frontier.Enqueue(start, 0);
        // Each smoke billows differently.
        _lumpSeed = Hash((int)(seed.X * 7.31f) ^ (int)(seed.Y * 13.7f) * 31 ^ (int)(seed.Z * 3.1f) * 977) * 500f;
        _seeded = true;
        _fading = false;
        _fillTime = _fadeTime = 0;
        _packedCount = -1;
        HasDensity = false;
        Version++;
    }

    private void Spread(int cell, float radius, ParticleRaycast raycastStatic)
    {
        int n = SizeX;
        int x = cell % n, y = cell / n % n, z = cell / (n * n);
        Vector3 from = CellCenter(x, y, z);
        float cost = _cost[cell];
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            int nx = x + dx, ny = y + dy, nz = z + dz;
            // The outer layer stays empty so the texture fades to nothing at the box.
            if (nx < 1 || ny < 1 || nz < 1 || nx >= n - 1 || ny >= n - 1 || nz >= n - 1) continue;
            int next = nx + n * (ny + n * nz);
            if (_filledFlag[next] != 0) continue;
            float up = dz > 0 ? dz * _flatten : dz;
            float step = CellSize * (float)Math.Sqrt(dx * dx + dy * dy + up * up);
            float total = cost + step;
            if (total >= _cost[next]) continue;
            Vector3 to = CellCenter(nx, ny, nz);
            if (Vector3.DistanceSquared(to, _seed) > radius * radius) continue;
            if (raycastStatic != null)
            {
                Vector3 direction = to - from;
                float length = direction.Length();
                if (raycastStatic(from, direction / length, length, out _, out _, out _)) continue; // a wall between them
            }
            _cost[next] = total;
            _frontier.Enqueue(next, FillOrder(to, total));
        }
    }

    private bool Sunlit(int cell, ParticleStepContext context)
    {
        if (context?.SunDirection is not { } sun || context.RaycastStatic == null) return true;
        int n = SizeX;
        Vector3 center = CellCenter(cell % n, cell / n % n, cell / (n * n));
        return !context.RaycastStatic(center, sun, 500f, out _, out _, out _);
    }

    // Straight-line distance where the way is clear (a round dome; the 26-neighbour path alone would make an
    // octagon), the path where walls make it longer, plus a slow wobble so the outline billows.
    private float FillOrder(Vector3 cell, float path)
    {
        Vector3 offset = cell - _seed;
        if (offset.Z > 0) offset.Z *= _flatten;
        float order = Math.Max(offset.Length(), path * 0.85f);
        return order + ParticleSimulation.Turbulence(cell * 0.6f, _lumpSeed).X * Lumps;
    }

    // Voxels -> cells: thin towards the last-filled edge, fade in as the flood arrives, and when dissipating
    // let the outer (and a random scatter of) voxels go first. Start Color alpha scales the density.
    private void Pack(float tint, float fade)
    {
        int count = _filled.Count;
        bool animating = _fading || _time - _lastFill < CellFadeIn + 0.05f;
        if (!animating && count == _packedCount && tint == _packedTint && HasDensity) return;
        _packedCount = count;
        _packedTint = tint;
        float edge = Math.Max(1, count * EdgeShare);
        for (int k = 0; k < count; k++)
        {
            int cell = _filled[k];
            float inner = Math.Clamp((count - k) / edge, 0, 1);
            float density = 0.2f + 0.8f * inner * inner * (3 - 2 * inner);
            density *= Math.Clamp((_time - _born[cell]) / CellFadeIn, 0, 1);
            if (_fading)
            {
                float order = 0.6f * k / count + 0.4f * Hash(cell);
                density *= Math.Clamp((1 - order - fade * 1.25f) * 4 + 1, 0, 1) * (1 - 0.3f * fade);
            }
            _cells[cell] = new Color(_sun[cell], (byte)0, (byte)0, ToByte(density * tint / DensityRange));
        }
        HasDensity = count > 0;
        Version++;
    }

    private void ResetVoxels()
    {
        if (!_seeded) return;
        _seeded = false;
        _fading = false;
        _filled.Clear();
        _frontier.Clear();
        _packedCount = -1;
    }

    private void Resize(int sx, int sy, int sz)
    {
        SizeX = sx;
        SizeY = sy;
        SizeZ = sz;
        int count = sx * sy * sz;
        if (_cells.Length >= count) return;
        _cells = new Color[count];
        _density = new float[count];
        _color = new Vector3[count];
        _cost = new float[count];
        _born = new float[count];
        _filledFlag = new byte[count];
        _sun = new byte[count];
    }

    private Vector3 CellCenter(int x, int y, int z) => Origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * CellSize;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static float Hash(int i)
    {
        unchecked
        {
            uint h = (uint)i * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 65535f;
        }
    }

    private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 1e-8f ? Math.Clamp(Vector3.Dot(point - a, ab) / lengthSquared, 0, 1) : 0;
        return Vector3.Distance(point, a + ab * t);
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
