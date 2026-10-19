// Generates Content/GameObjects/Bottle/bottle.obj: a closed glass bottle shell (base at the origin, Z up, metres) for
// the Liquid component. Run from this folder: dotnet run generate_bottle.cs
// The surface is one closed lathe (bottom disk, body, shoulder, neck, lip, corked top) so the Liquid component can
// measure the volume under any plane. Hard edges (bottom rim, top) get their own vertex rings.
using System.Globalization;
using System.Text;

const int Segments = 40;
// Profile from the bottom centre to the top centre: (radius, height, hard edge before this point).
var profile = new (double R, double Z, bool Hard)[]
{
    (0.000, 0.000, false),
    (0.030, 0.000, true),
    (0.034, 0.002, false),
    (0.036, 0.008, false),
    (0.037, 0.020, false),
    (0.037, 0.190, false),
    (0.0355, 0.205, false),
    (0.031, 0.220, false),
    (0.024, 0.235, false),
    (0.017, 0.248, false),
    (0.0140, 0.258, false),
    (0.0135, 0.268, false),
    (0.0135, 0.286, false),
    (0.0150, 0.288, false),
    (0.0152, 0.297, false),
    (0.0140, 0.300, true),
    (0.000, 0.300, false),
};

var inv = CultureInfo.InvariantCulture;
string F(double v) => v.ToString("0.000000", inv);

// Arc length along the profile for the V texture coordinate.
var arc = new double[profile.Length];
for (int i = 1; i < profile.Length; i++)
    arc[i] = arc[i - 1] + Math.Sqrt(Math.Pow(profile[i].R - profile[i - 1].R, 2) + Math.Pow(profile[i].Z - profile[i - 1].Z, 2));
double total = arc[^1];

// 2D outward normal (dr, dz) of each profile segment.
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
sb.AppendLine("# Bottle: a closed glass bottle shell, 0.30 m tall, 7.4 cm wide, base at the origin, Z up. Generated for Obsidian by Source/generate_bottle.cs.");
sb.AppendLine("mtllib bottle.mtl");
sb.AppendLine("o Bottle");
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
        sb.AppendLine($"vt {F((double)s / Segments)} {F(1 - arc[ring.Point] / total)}");
foreach (var ring in rings)
    for (int s = 0; s <= Segments; s++)
    {
        double a = 2 * Math.PI * s / Segments;
        sb.AppendLine($"vn {F(ring.Nr * Math.Cos(a))} {F(ring.Nr * Math.Sin(a))} {F(ring.Nz)}");
    }
sb.AppendLine("usemtl Glass");
sb.AppendLine("s 1");
int stride = Segments + 1;
for (int r = 0; r < rings.Count - 1; r++)
{
    // A hard edge's two rings sit at the same point: no faces between them.
    if (rings[r].Point == rings[r + 1].Point) continue;
    for (int s = 0; s < Segments; s++)
    {
        int a = r * stride + s + 1, b = a + 1, c = a + stride, d = c + 1;
        // Counter-clockwise seen from outside (the profile goes up, the angle goes counter-clockwise from +Z).
        if (profile[rings[r].Point].R > 0)
            sb.AppendLine($"f {a}/{a}/{a} {b}/{b}/{b} {d}/{d}/{d}");
        if (profile[rings[r + 1].Point].R > 0)
            sb.AppendLine($"f {a}/{a}/{a} {d}/{d}/{d} {c}/{c}/{c}");
    }
}

string dir = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
File.WriteAllText(Path.Combine(dir, "bottle.obj"), sb.ToString());
File.WriteAllText(Path.Combine(dir, "bottle.mtl"), "# Bottle glass (Assets.BottleGlassMaterial replaces it at load)\nnewmtl Glass\nKd 0.8 0.9 0.85\nd 0.3\n");
Console.WriteLine($"Wrote {Path.Combine(dir, "bottle.obj")}: {rings.Count * stride} vertices");
