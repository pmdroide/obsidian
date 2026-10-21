// Generates Content/GameObjects/Vase/vase.obj: a closed ceramic vase with real walls (base at the origin, Z up,
// metres) for the Breakable component. Run from this folder: dotnet run generate_vase.cs
// The surface is one closed lathe: the outside from the bottom centre up to the lip, over the lip, and the inside
// back down to the inner floor, so a break cuts curved shell shards with clay showing on their edges. Hard edges
// (foot, lip) get their own vertex rings.
using System.Globalization;
using System.Text;

const int Segments = 48;
// Profile from the outer bottom centre to the inner floor centre: (radius, height, hard edge at this point).
var profile = new (double R, double Z, bool Hard)[]
{
    (0.000, 0.000, false),
    (0.060, 0.000, true),
    (0.066, 0.006, false),
    (0.064, 0.016, true),
    (0.080, 0.040, false),
    (0.108, 0.090, false),
    (0.124, 0.150, false),
    (0.122, 0.205, false),
    (0.104, 0.255, false),
    (0.074, 0.300, false),
    (0.056, 0.335, false),
    (0.054, 0.360, false),
    (0.062, 0.385, false),
    (0.076, 0.400, true),
    (0.072, 0.406, false),
    (0.064, 0.404, true),
    (0.050, 0.385, false),
    (0.045, 0.360, false),
    (0.047, 0.335, false),
    (0.064, 0.300, false),
    (0.094, 0.255, false),
    (0.112, 0.205, false),
    (0.114, 0.150, false),
    (0.098, 0.090, false),
    (0.070, 0.045, false),
    (0.050, 0.028, true),
    (0.000, 0.026, false),
};

var inv = CultureInfo.InvariantCulture;
string F(double v) => v.ToString("0.000000", inv);

// Arc length along the profile for the V texture coordinate.
var arc = new double[profile.Length];
for (int i = 1; i < profile.Length; i++)
    arc[i] = arc[i - 1] + Math.Sqrt(Math.Pow(profile[i].R - profile[i - 1].R, 2) + Math.Pow(profile[i].Z - profile[i - 1].Z, 2));
double total = arc[^1];

// 2D outward normal (dr, dz) of each profile segment (the profile runs outside-up then inside-down, so "outward" is
// to the right of the direction of travel on both sides of the wall).
(double R, double Z) SegmentNormal(int i)
{
    double dr = profile[i + 1].R - profile[i].R, dz = profile[i + 1].Z - profile[i].Z;
    double l = Math.Sqrt(dr * dr + dz * dz);
    return (dz / l, -dr / l);
}

// Rings: each profile point, split in two where the profile has a hard edge.
var rings = new List<(int Point, double Nr, double Nz)>();
for (int i = 0; i < profile.Length; i++)
{
    bool hasPrev = i > 0, hasNext = i < profile.Length - 1;
    var prev = hasPrev ? SegmentNormal(i - 1) : SegmentNormal(i);
    var next = hasNext ? SegmentNormal(i) : SegmentNormal(i - 1);
    if (profile[i].Hard && hasPrev && hasNext)
    {
        rings.Add((i, prev.R, prev.Z));
        rings.Add((i, next.R, next.Z));
    }
    else
    {
        double nr = prev.R + next.R, nz = prev.Z + next.Z, l = Math.Sqrt(nr * nr + nz * nz);
        rings.Add((i, nr / l, nz / l));
    }
}

var sb = new StringBuilder();
sb.AppendLine("# Vase: a closed ceramic vase with 8-10 mm walls, 0.41 m tall, 25 cm wide, base at the origin, Z up. Generated for Obsidian by Source/generate_vase.cs.");
sb.AppendLine("mtllib vase.mtl");
sb.AppendLine("o Vase");
foreach (var ring in rings)
{
    var p = profile[ring.Point];
    for (int s = 0; s <= Segments; s++)
    {
        double a = 2 * Math.PI * s / Segments;
        sb.AppendLine($"v {F(p.R * Math.Cos(a))} {F(p.R * Math.Sin(a))} {F(p.Z)}");
    }
}
foreach (var ring in rings)
    for (int s = 0; s <= Segments; s++)
        sb.AppendLine($"vt {F(2.0 * s / Segments)} {F(1 - 2 * arc[ring.Point] / total)}");
foreach (var ring in rings)
    for (int s = 0; s <= Segments; s++)
    {
        double a = 2 * Math.PI * s / Segments;
        sb.AppendLine($"vn {F(ring.Nr * Math.Cos(a))} {F(ring.Nr * Math.Sin(a))} {F(ring.Nz)}");
    }
sb.AppendLine("usemtl Clay");
sb.AppendLine("s 1");
int stride = Segments + 1;
for (int r = 0; r < rings.Count - 1; r++)
{
    // A hard edge's two rings sit at the same point: no faces between them.
    if (rings[r].Point == rings[r + 1].Point) continue;
    for (int s = 0; s < Segments; s++)
    {
        int a = r * stride + s + 1, b = a + 1, c = a + stride, d = c + 1;
        // Counter-clockwise seen from outside, like the bottle: on the inner wall the profile runs down, which
        // turns the same winding to face into the vase's hollow (the inside of the wall's solid is behind it).
        if (profile[rings[r].Point].R > 0)
            sb.AppendLine($"f {a}/{a}/{a} {b}/{b}/{b} {d}/{d}/{d}");
        if (profile[rings[r + 1].Point].R > 0)
            sb.AppendLine($"f {a}/{a}/{a} {d}/{d}/{d} {c}/{c}/{c}");
    }
}

string dir = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
File.WriteAllText(Path.Combine(dir, "vase.obj"), sb.ToString());
File.WriteAllText(Path.Combine(dir, "vase.mtl"), "# Vase glaze (Assets.VaseMaterial replaces it at load)\nnewmtl Clay\nKd 0.65 0.32 0.2\n");
Console.WriteLine($"Wrote {Path.Combine(dir, "vase.obj")}: {rings.Count * stride} vertices");
