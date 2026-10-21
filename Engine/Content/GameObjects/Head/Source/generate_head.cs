// Generates Content/GameObjects/Head/head.obj: a closed, solid human head (a skull-shaped ovoid with jaw, nose, brow,
// cheekbones and ears; 25 cm tall, centre at the origin, Z up, face towards -Y like the Mixamo characters) for the
// Breakable component's zombie heads. Run from this folder: dotnet run generate_head.cs
// It is a deformed UV sphere (one closed surface, poles at the crown and under the chin) with smooth normals.
using System.Globalization;
using System.Text;

const int Segments = 40, Rings = 30;
var inv = CultureInfo.InvariantCulture;
string F(double v) => v.ToString("0.000000", inv);

static double Smooth(double e0, double e1, double x)
{
    double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
    return t * t * (3 - 2 * t);
}

static double Bump(double x, double y, double z, double cx, double cy, double cz, double size) =>
    Math.Exp(-((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz)) / (size * size));

// Surface point in the direction (x, y, z) (unit): an ovoid, narrowed into a jaw below, with features added.
static (double X, double Y, double Z) Surface(double x, double y, double z)
{
    // Half sizes: 7.8 cm wide, 10 cm deep, 12.5 cm tall above the middle, 10.6 cm below it (a blunt jaw, not a point).
    double ax = 0.078, ay = 0.100, az = z < 0 ? 0.106 : 0.125;
    // The jaw narrows a little below the cheekbones; the back of the skull bulges.
    double jaw = 1 - 0.16 * Smooth(-0.35, -0.95, z);
    double back = 1 + 0.07 * Smooth(0.0, 0.7, y) * Smooth(-0.3, 0.4, z);
    double px = x * ax * jaw, py = y * ay * (y < 0 ? 0.94 + 0.06 * jaw : back), pz = z * az;
    // Push outward along the direction for features (metres).
    double add = 0
        + 0.030 * Bump(x, y, z, 0, -0.97, -0.10, 0.15)                       // nose
        + 0.010 * Bump(x, y, z, 0, -0.85, 0.30, 0.28) * (1 - Smooth(0.5, 0.8, Math.Abs(x)))  // brow ridge
        - 0.012 * Bump(x, y, z, 0.32, -0.88, 0.15, 0.14)                     // eye sockets
        - 0.012 * Bump(x, y, z, -0.32, -0.88, 0.15, 0.14)
        + 0.008 * Bump(x, y, z, 0.62, -0.70, -0.05, 0.22)                    // cheekbones
        + 0.008 * Bump(x, y, z, -0.62, -0.70, -0.05, 0.22)
        + 0.006 * Bump(x, y, z, 0, -0.80, -0.62, 0.20)                       // chin
        + 0.014 * Bump(x, y, z, 0.99, 0.05, 0.02, 0.17)                      // ears
        + 0.014 * Bump(x, y, z, -0.99, 0.05, 0.02, 0.17)
        - 0.006 * Bump(x, y, z, 0, -0.95, -0.38, 0.12);                      // mouth line
    double length = Math.Sqrt(px * px + py * py + pz * pz);
    double scale = (length + add) / length;
    // The chin sits a little forward of the throat.
    return (px * scale, py * scale - 0.012 * Smooth(-0.2, -0.9, z), pz * scale);
}

var positions = new List<(double X, double Y, double Z)>();
var uvs = new List<(double U, double V)>();
for (int r = 0; r <= Rings; r++)
{
    double theta = Math.PI * r / Rings; // 0 = crown, pi = under the chin
    for (int s = 0; s <= Segments; s++)
    {
        // The seam runs down the back of the head (+Y); s goes around counter-clockwise seen from above.
        double phi = Math.PI / 2 + 2 * Math.PI * s / Segments;
        double x = Math.Sin(theta) * Math.Cos(phi), y = Math.Sin(theta) * Math.Sin(phi), z = Math.Cos(theta);
        positions.Add(Surface(x, y, z));
        uvs.Add(((double)s / Segments, (double)r / Rings));
    }
}

// Faces: counter-clockwise seen from outside (as the bottle), skipping the collapsed triangles at the poles.
int stride = Segments + 1;
var faces = new List<(int A, int B, int C)>();
for (int r = 0; r < Rings; r++)
    for (int s = 0; s < Segments; s++)
    {
        int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
        if (r > 0) faces.Add((a, c, b));
        if (r < Rings - 1) faces.Add((b, c, d));
    }

// Smooth normals, welded across the seam and the poles by position.
var normals = new (double X, double Y, double Z)[positions.Count];
var byPosition = new Dictionary<(long, long, long), List<int>>();
for (int i = 0; i < positions.Count; i++)
{
    var key = ((long)Math.Round(positions[i].X * 1e6), (long)Math.Round(positions[i].Y * 1e6), (long)Math.Round(positions[i].Z * 1e6));
    if (!byPosition.TryGetValue(key, out var list)) byPosition[key] = list = new List<int>();
    list.Add(i);
}
var accumulated = new (double X, double Y, double Z)[positions.Count];
foreach (var (a, b, c) in faces)
{
    var pa = positions[a]; var pb = positions[b]; var pc = positions[c];
    double ux = pb.X - pa.X, uy = pb.Y - pa.Y, uz = pb.Z - pa.Z, vx = pc.X - pa.X, vy = pc.Y - pa.Y, vz = pc.Z - pa.Z;
    var n = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx);
    foreach (int i in new[] { a, b, c }) accumulated[i] = (accumulated[i].X + n.Item1, accumulated[i].Y + n.Item2, accumulated[i].Z + n.Item3);
}
foreach (var group in byPosition.Values)
{
    (double X, double Y, double Z) sum = (0, 0, 0);
    foreach (int i in group) sum = (sum.X + accumulated[i].X, sum.Y + accumulated[i].Y, sum.Z + accumulated[i].Z);
    double l = Math.Sqrt(sum.X * sum.X + sum.Y * sum.Y + sum.Z * sum.Z);
    if (l < 1e-12) l = 1;
    foreach (int i in group) normals[i] = (sum.X / l, sum.Y / l, sum.Z / l);
}

var sb = new StringBuilder();
sb.AppendLine("# Head: a closed solid human head, 25 cm tall, centre at the origin, Z up, face towards -Y. Generated for Obsidian by Source/generate_head.cs.");
sb.AppendLine("mtllib head.mtl");
sb.AppendLine("o Head");
foreach (var p in positions) sb.AppendLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");
foreach (var t in uvs) sb.AppendLine($"vt {F(t.U)} {F(1 - t.V)}");
foreach (var n in normals) sb.AppendLine($"vn {F(n.X)} {F(n.Y)} {F(n.Z)}");
sb.AppendLine("usemtl Skin");
sb.AppendLine("s 1");
foreach (var (a, b, c) in faces) sb.AppendLine($"f {a + 1}/{a + 1}/{a + 1} {b + 1}/{b + 1}/{b + 1} {c + 1}/{c + 1}/{c + 1}");

string dir = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
File.WriteAllText(Path.Combine(dir, "head.obj"), sb.ToString());
File.WriteAllText(Path.Combine(dir, "head.mtl"), "# Head skin (Assets.HeadMaterial replaces it at load)\nnewmtl Skin\nKd 0.5 0.55 0.42\n");
Console.WriteLine($"Wrote {Path.Combine(dir, "head.obj")}: {positions.Count} vertices, {faces.Count} triangles");
