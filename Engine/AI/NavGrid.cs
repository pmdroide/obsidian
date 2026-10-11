using Engine.Entities;
using Engine.Physics;
using Microsoft.Xna.Framework;

namespace Engine.AI;

/// <summary>
/// What a navigation grid is built for: the size and climbing ability of the characters walking it.
/// Zero (or less) means the default, so <c>new NavGridSettings()</c> and <c>default</c> both give the defaults.
/// </summary>
/// <param name="AgentRadius">Cells closer than this to a wall, a ledge or the edge of the ground are blocked (metres).</param>
/// <param name="MaxStep">Highest height change between neighbouring cells a character steps over (metres).</param>
/// <param name="MaxSlope">Steepest walkable surface, in degrees.</param>
/// <param name="CellSize">Grid spacing in metres; grown automatically for very large scenes.</param>
public readonly record struct NavGridSettings(float AgentRadius = NavGridSettings.DefaultRadius, float MaxStep = NavGridSettings.DefaultStep,
    float MaxSlope = NavGridSettings.DefaultSlope, float CellSize = NavGrid.DefaultCellSize)
{
    public const float DefaultRadius = 0.5f, DefaultStep = 0.45f, DefaultSlope = 40f;

    public NavGridSettings() : this(DefaultRadius) { }

    /// <summary>Values rounded and clamped, so characters of nearly the same size share one grid.</summary>
    public NavGridSettings Normalized() => new(
        Round(Math.Clamp(Valid(AgentRadius, DefaultRadius), 0.05f, 3f), 0.05f),
        Round(Math.Clamp(Valid(MaxStep, DefaultStep), 0.01f, 2f), 0.01f),
        Math.Clamp(Valid(MaxSlope, DefaultSlope), 1f, 80f),
        Math.Clamp(Valid(CellSize, NavGrid.DefaultCellSize), 0.1f, NavGrid.MaxCellSize));

    private static float Valid(float value, float fallback) => float.IsFinite(value) && value > 0f ? value : fallback;
    private static float Round(float value, float step) => MathF.Round(value / step) * step;
}

/// <summary>
/// Where ground characters can walk in a scene, as a height field of square cells seen from above (Z up).
/// Built by casting a ray straight down through every cell against the scene's <b>static</b> colliders
/// (<see cref="ScenePhysics.RaycastStatic"/>): dynamic bodies such as crates and characters never block it,
/// so they are avoided by physics and repathing instead. A cell is walkable when its surface is flat enough
/// and no wall, ledge or hole lies within the agent radius. Only the topmost surface counts, so there is one
/// walking level (no paths under bridges or tables). <see cref="FindPath"/> runs A* over the walkable cells
/// and shortens the result to straight segments. Get a shared, automatically rebuilt grid from
/// <see cref="Navigation.For"/>.
/// </summary>
public sealed class NavGrid
{
    public const float DefaultCellSize = 0.25f;
    public const float MaxCellSize = 1f;
    /// <summary>Cells per grid at most; larger scenes get bigger cells, then a square around the focus point.</summary>
    public const int MaxCells = 160_000;
    /// <summary>How far (metres) a start or goal off the walkable area is moved onto it.</summary>
    public const float SnapDistance = 4f;

    private static readonly float Sqrt2 = MathF.Sqrt(2f);

    public NavGridSettings Settings { get; }
    /// <summary>World position of the corner of cell (0, 0).</summary>
    public Vector2 Origin { get; }
    public float CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Walkable cells in the grid.</summary>
    public int WalkableCount { get; private set; }
    /// <summary>Static colliders the grid was built from.</summary>
    public int ObstacleCount { get; }

    // Per cell: surface height (NaN = nothing below), walkable after the radius check, connected area.
    private readonly float[] _height;
    private readonly bool[] _walkable;
    private readonly int[] _region;

    // A* scratch, reused between searches (every search is on the game thread).
    private readonly float[] _cost;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private int _search;
    private readonly PriorityQueue<int, float> _open = new();
    private readonly List<int> _cells = new();

    private NavGrid(NavGridSettings settings, Vector2 origin, float cellSize, int width, int height, int obstacles)
    {
        Settings = settings;
        Origin = origin;
        CellSize = cellSize;
        Width = width;
        Height = height;
        ObstacleCount = obstacles;
        int count = width * height;
        _height = new float[count];
        _walkable = new bool[count];
        _region = new int[count];
        _cost = new float[count];
        _parent = new int[count];
        _stamp = new int[count];
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  BUILD
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>True for a gameobject whose collider the grid is built from: an attached, solid, static body.</summary>
    public static bool IsObstacle(BasicEntity e) =>
        e is { IsEnabled: true, StaticBody: not null } && e.Physics is { Enabled: true, IsTrigger: false };

    /// <summary>
    /// Casts the grid against <paramref name="physics"/>, covering every static collider among
    /// <paramref name="entities"/>. A scene too large for <see cref="MaxCells"/> at <see cref="MaxCellSize"/>
    /// is cut to a square around <paramref name="focus"/>. Null when there is no static collider.
    /// </summary>
    public static NavGrid Build(ScenePhysics physics, IEnumerable<BasicEntity> entities, NavGridSettings settings, Vector3 focus = default)
    {
        if (physics == null || entities == null) return null;
        settings = settings.Normalized();

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int obstacles = 0;
        foreach (BasicEntity e in entities)
        {
            if (!IsObstacle(e)) continue;
            WorldBounds(e, out Vector3 a, out Vector3 b);
            min = Vector3.Min(min, a);
            max = Vector3.Max(max, b);
            obstacles++;
        }
        if (obstacles == 0) return null;

        // Bigger cells for big scenes, then (still too big) only a square around the focus.
        float cell = settings.CellSize;
        float sizeX = max.X - min.X, sizeY = max.Y - min.Y;
        while ((sizeX / cell + 1) * (sizeY / cell + 1) > MaxCells && cell < MaxCellSize) cell = Math.Min(cell * 1.5f, MaxCellSize);
        float maxExtent = MathF.Sqrt(MaxCells) * cell;
        if (sizeX > maxExtent)
        {
            min.X = Math.Clamp(focus.X - maxExtent / 2, min.X, max.X - maxExtent);
            max.X = min.X + maxExtent;
        }
        if (sizeY > maxExtent)
        {
            min.Y = Math.Clamp(focus.Y - maxExtent / 2, min.Y, max.Y - maxExtent);
            max.Y = min.Y + maxExtent;
        }

        int width = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cell));
        int height = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / cell));
        var grid = new NavGrid(settings, new Vector2(min.X, min.Y), cell, width, height, obstacles);
        grid.Cast(physics, min.Z - 1f, max.Z + 1f);
        grid.LabelRegions();
        return grid;
    }

    private void Cast(ScenePhysics physics, float bottom, float top)
    {
        // Surfaces steeper than the slope limit (and empty cells) are kept as NaN: nothing to stand on.
        float minNormalZ = MathF.Cos(MathHelper.ToRadians(Settings.MaxSlope));
        float[] surface = _height;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = x + y * Width;
                Vector2 c = CellCenter(i);
                surface[i] = physics.RaycastStatic(new Vector3(c, top), -Vector3.UnitZ, top - bottom, out RaycastHit hit) &&
                             Math.Abs(hit.Normal.Z) >= minNormalZ - 1e-4f
                    ? hit.Point.Z
                    : float.NaN;
            }

        // Keep the agent radius clear of walls (a jump up), ledges (a drop) and holes. Neighbours further
        // away may differ more, as on a ramp. A blocked cell's edge can be half a cell nearer than its centre,
        // so the radius grows by that much: the clearance is never less than the agent radius.
        float slope = MathF.Tan(MathHelper.ToRadians(Math.Min(Settings.MaxSlope, 80f)));
        float clearance = Settings.AgentRadius + CellSize * 0.5f;
        int reach = (int)MathF.Ceiling(clearance / CellSize);
        var ring = new List<(int Dx, int Dy, float Allowed)>();
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float distance = MathF.Sqrt(dx * dx + dy * dy) * CellSize;
                if ((dx != 0 || dy != 0) && distance <= clearance + 1e-4f)
                    ring.Add((dx, dy, Settings.MaxStep + distance * slope));
            }

        bool[] walkable = _walkable;
        int walkableCount = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = x + y * Width;
                float h = surface[i];
                if (float.IsNaN(h)) continue;
                bool clear = true;
                foreach (var (dx, dy, allowed) in ring)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) { clear = false; break; }
                    float n = surface[nx + ny * Width];
                    if (float.IsNaN(n) || Math.Abs(n - h) > allowed) { clear = false; break; }
                }
                walkable[i] = clear;
                if (clear) walkableCount++;
            }
        WalkableCount = walkableCount;
    }

    /// <summary>Numbers the connected walkable areas (0 = not walkable), so unreachable goals are known at once.</summary>
    private void LabelRegions()
    {
        var queue = new Queue<int>();
        int next = 0;
        for (int start = 0; start < _region.Length; start++)
        {
            if (!_walkable[start] || _region[start] != 0) continue;
            _region[start] = ++next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int d = 0; d < 8; d++)
                {
                    int n = Neighbour(i, d, out _);
                    if (n < 0 || _region[n] != 0) continue;
                    _region[n] = next;
                    queue.Enqueue(n);
                }
            }
        }
    }

    /// <summary>A gameobject's collider box in world space (its model bounds, Collider Size, scale, rotation).</summary>
    public static void WorldBounds(BasicEntity e, out Vector3 min, out Vector3 max)
    {
        ScenePhysics.ColliderBounds(e, e.Physics?.ColliderStretchFor(e) ?? Vector3.One, out Vector3 a, out Vector3 b);
        Matrix world = Matrix.CreateScale(e.Scale) * e.RotationMatrix * Matrix.CreateTranslation(e.Position);
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (Vector3 corner in new BoundingBox(Vector3.Min(a, b), Vector3.Max(a, b)).GetCorners())
        {
            Vector3 p = Vector3.Transform(corner, world);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  CELLS
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>Index of the cell under a world position, or -1 outside the grid.</summary>
    public int CellAt(Vector3 position)
    {
        int x = (int)MathF.Floor((position.X - Origin.X) / CellSize);
        int y = (int)MathF.Floor((position.Y - Origin.Y) / CellSize);
        return x < 0 || y < 0 || x >= Width || y >= Height ? -1 : x + y * Width;
    }

    public Vector2 CellCenter(int cell) =>
        Origin + new Vector2((cell % Width + 0.5f) * CellSize, (cell / Width + 0.5f) * CellSize);

    /// <summary>The cell's centre on its ground surface (NaN height when it has none).</summary>
    public Vector3 CellPoint(int cell) => new(CellCenter(cell), _height[cell]);

    public bool IsWalkable(int cell) => cell >= 0 && cell < _walkable.Length && _walkable[cell];

    /// <summary>True when a character can stand at the position (its cell is walkable).</summary>
    public bool IsWalkable(Vector3 position) => IsWalkable(CellAt(position));

    /// <summary>The surface height under a position (walkable or not); false over a hole or outside the grid.</summary>
    public bool TryGetHeight(Vector3 position, out float height)
    {
        int cell = CellAt(position);
        height = cell >= 0 ? _height[cell] : float.NaN;
        return !float.IsNaN(height);
    }

    /// <summary>Connected walkable area of a cell; 0 when it isn't walkable.</summary>
    public int RegionOf(int cell) => cell >= 0 && cell < _region.Length ? _region[cell] : 0;

    /// <summary>True when a path can lead from one position to the other (both snapped onto the walkable area).</summary>
    public bool IsReachable(Vector3 from, Vector3 to)
    {
        int a = NearestWalkable(from, SnapDistance), b = NearestWalkable(to, SnapDistance);
        return a >= 0 && b >= 0 && _region[a] == _region[b];
    }

    /// <summary>
    /// The walkable cell closest to <paramref name="position"/> within <paramref name="maxDistance"/>, optionally only
    /// in one <paramref name="region"/>; -1 when there is none.
    /// </summary>
    public int NearestWalkable(Vector3 position, float maxDistance, int region = 0)
    {
        int cell = CellAt(position);
        if (cell >= 0 && _walkable[cell] && (region == 0 || _region[cell] == region)) return cell;

        // Grow square rings around the (clamped) cell; the nearest on the first ring with a hit is close enough.
        int cx = Math.Clamp((int)MathF.Floor((position.X - Origin.X) / CellSize), 0, Width - 1);
        int cy = Math.Clamp((int)MathF.Floor((position.Y - Origin.Y) / CellSize), 0, Height - 1);
        var flat = new Vector2(position.X, position.Y);
        int rings = (int)MathF.Ceiling(maxDistance / CellSize) + 1;
        int best = -1;
        float bestDistance = maxDistance * maxDistance;
        for (int r = 0; r <= rings; r++)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    int i = x + y * Width;
                    if (!_walkable[i] || (region != 0 && _region[i] != region)) continue;
                    float d = Vector2.DistanceSquared(CellCenter(i), flat);
                    if (d > bestDistance) continue;
                    bestDistance = d;
                    best = i;
                }
            // A cell on the next ring is at least (r + 1 - 1) cells away from the clamped cell.
            if (best >= 0 && r * CellSize >= MathF.Sqrt(bestDistance)) break;
        }
        return best;
    }

    /// <summary>
    /// Neighbour <paramref name="direction"/> (0-3 straight, 4-7 diagonal) of a walkable cell when a character can
    /// move there: walkable, within a step, and no diagonal squeezing past a blocked corner. -1 otherwise.
    /// </summary>
    private int Neighbour(int cell, int direction, out float length)
    {
        int x = cell % Width, y = cell / Width;
        int dx = direction switch { 0 => 1, 1 => -1, 4 or 6 => 1, 5 or 7 => -1, _ => 0 };
        int dy = direction switch { 2 => 1, 3 => -1, 4 or 5 => 1, 6 or 7 => -1, _ => 0 };
        length = direction < 4 ? 1f : Sqrt2;
        int nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) return -1;
        int n = nx + ny * Width;
        if (!_walkable[n] || !CanStep(cell, n)) return -1;
        if (direction >= 4 && (!_walkable[nx + y * Width] || !_walkable[x + ny * Width])) return -1;
        return n;
    }

    private bool CanStep(int a, int b) => Math.Abs(_height[a] - _height[b]) <= Settings.MaxStep + 1e-4f;

    ////////////////////////////////////////////////////////////////////////////////
    //  PATHS
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Fills <paramref name="path"/> with ground points from <paramref name="from"/> (excluded) to
    /// <paramref name="to"/>: the corners of the shortest route, joined by straight walkable segments.
    /// A goal off the walkable area (beside a wall, on a crate) is moved to the nearest walkable cell, and an
    /// unreachable goal (another walled-off area) leads as close to it as possible with
    /// <paramref name="complete"/> false. False with an empty path when the start isn't near the walkable area.
    /// </summary>
    public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path, out bool complete)
    {
        path.Clear();
        complete = false;
        int start = NearestWalkable(from, SnapDistance);
        if (start < 0) return false;
        int region = _region[start];
        // The goal's own walkable cell must be in the start's area; otherwise get as close as that area allows.
        int target = NearestWalkable(to, SnapDistance);
        complete = target >= 0 && _region[target] == region;
        int goal = complete ? target : NearestWalkable(to, SnapDistance, region);
        if (goal < 0) goal = ClosestInRegion(to, region);

        if (!Search(start, goal)) return false;
        if (start == goal)
        {
            path.Add(CellAt(to) == goal ? new Vector3(to.X, to.Y, _height[goal]) : CellPoint(goal));
            return true;
        }

        // Back from the goal, then shorten: keep a cell only where the straight line to the next one is blocked.
        _cells.Clear();
        for (int c = goal; c != start; c = _parent[c]) _cells.Add(c);
        _cells.Add(start);
        _cells.Reverse();

        int anchor = 0;
        while (anchor < _cells.Count - 1)
        {
            int reach = anchor + 1;
            for (int k = anchor + 2; k < _cells.Count; k++)
            {
                if (!LineWalkable(_cells[anchor], _cells[k])) break;
                reach = k;
            }
            path.Add(CellPoint(_cells[reach]));
            anchor = reach;
        }

        // End exactly on the goal when it lies in the goal cell.
        if (path.Count > 0 && CellAt(to) == goal) path[^1] = new Vector3(to.X, to.Y, _height[goal]);
        return true;
    }

    /// <summary>A* over 8 neighbours with the octile distance.</summary>
    private bool Search(int start, int goal)
    {
        if (start == goal) return true;
        if (++_search == int.MaxValue)
        {
            Array.Clear(_stamp);
            _search = 1;
        }
        _open.Clear();
        _stamp[start] = _search;
        _cost[start] = 0;
        _parent[start] = start;
        _open.Enqueue(start, Heuristic(start, goal));
        while (_open.TryDequeue(out int cell, out float priority))
        {
            if (cell == goal) return true;
            // Stale queue entry: the cell was reached more cheaply since.
            if (priority > _cost[cell] + Heuristic(cell, goal) + 1e-3f) continue;
            for (int d = 0; d < 8; d++)
            {
                int n = Neighbour(cell, d, out float length);
                if (n < 0) continue;
                float cost = _cost[cell] + length;
                if (_stamp[n] == _search && cost >= _cost[n]) continue;
                _stamp[n] = _search;
                _cost[n] = cost;
                _parent[n] = cell;
                _open.Enqueue(n, cost + Heuristic(n, goal));
            }
        }
        return false;
    }

    private float Heuristic(int a, int b)
    {
        int dx = Math.Abs(a % Width - b % Width), dy = Math.Abs(a / Width - b / Width);
        return Math.Max(dx, dy) + (Sqrt2 - 1f) * Math.Min(dx, dy);
    }

    private int ClosestInRegion(Vector3 position, int region)
    {
        var flat = new Vector2(position.X, position.Y);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _region.Length; i++)
        {
            if (_region[i] != region) continue;
            float d = Vector2.DistanceSquared(CellCenter(i), flat);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = i;
        }
        return best;
    }

    /// <summary>
    /// True when a character can walk straight between the two positions: every cell the line crosses is
    /// walkable and within a step of the previous one.
    /// </summary>
    public bool LineWalkable(Vector3 from, Vector3 to)
    {
        int a = CellAt(from), b = CellAt(to);
        return a >= 0 && b >= 0 && _walkable[a] && _walkable[b] && LineWalkable(a, b);
    }

    /// <summary>Walks every cell a line between cell centres crosses (both side cells where it passes through a corner).</summary>
    private bool LineWalkable(int a, int b)
    {
        int x = a % Width, y = a / Width, bx = b % Width, by = b / Width;
        int dx = Math.Abs(bx - x), dy = Math.Abs(by - y);
        int sx = Math.Sign(bx - x), sy = Math.Sign(by - y);
        int previous = a;
        // Sign of the error says which cell border the line crosses next; zero is exactly a corner.
        int error = dx - dy;
        dx *= 2;
        dy *= 2;
        for (int steps = (dx + dy) / 2; steps > 0;)
        {
            if (error > 0)
            {
                x += sx;
                error -= dy;
                steps--;
            }
            else if (error < 0)
            {
                y += sy;
                error += dx;
                steps--;
            }
            else
            {
                if (!_walkable[(x + sx) + y * Width] || !_walkable[x + (y + sy) * Width]) return false;
                x += sx;
                y += sy;
                error += dx - dy;
                steps -= 2;
            }
            int cell = x + y * Width;
            if (!_walkable[cell] || !CanStep(previous, cell)) return false;
            previous = cell;
        }
        return true;
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  POINTS
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>A random walkable point within <paramref name="radius"/> of <paramref name="center"/> that a character there can reach.</summary>
    public bool TryRandomPoint(Vector3 center, float radius, Random random, out Vector3 point)
    {
        point = center;
        int from = NearestWalkable(center, SnapDistance);
        if (from < 0) return false;
        int region = _region[from];
        for (int attempt = 0; attempt < 40; attempt++)
        {
            float angle = (float)random.NextDouble() * MathHelper.TwoPi;
            float distance = radius * MathF.Sqrt((float)random.NextDouble());
            int cell = CellAt(center + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * distance);
            if (cell < 0 || !_walkable[cell] || _region[cell] != region) continue;
            point = CellPoint(cell);
            return true;
        }
        point = CellPoint(from);
        return false;
    }

    /// <summary>
    /// A reachable point about <paramref name="distance"/> away from <paramref name="from"/> in the direction
    /// leading furthest from <paramref name="threat"/>: twelve directions are tried, preferring those away from it.
    /// </summary>
    public bool TryFleePoint(Vector3 from, Vector3 threat, float distance, out Vector3 point)
    {
        point = from;
        int start = NearestWalkable(from, SnapDistance);
        if (start < 0) return false;
        int region = _region[start];
        Vector2 away = new(from.X - threat.X, from.Y - threat.Y);
        away = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitX;
        float bestScore = float.MinValue;
        bool found = false;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathHelper.TwoPi / 12f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            // Shorter tries in the same direction when a wall is close.
            for (float scale = 1f; scale >= 0.25f; scale -= 0.25f)
            {
                Vector2 target = new Vector2(from.X, from.Y) + direction * distance * scale;
                int cell = NearestWalkable(new Vector3(target, from.Z), CellSize * 3, region);
                if (cell < 0) continue;
                Vector3 candidate = CellPoint(cell);
                float score = Vector2.Distance(new Vector2(candidate.X, candidate.Y), new Vector2(threat.X, threat.Y))
                              + Vector2.Dot(direction, away) * distance * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    point = candidate;
                    found = true;
                }
                break;
            }
        }
        return found;
    }
}
