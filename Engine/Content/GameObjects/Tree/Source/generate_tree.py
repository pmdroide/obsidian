"""
Generates the built-in Tree model (Assets.Tree) and its painted textures for Obsidian.

    python generate_tree.py            (from any folder; writes next to this script's parent)

Outputs in Content/GameObjects/Tree/:
    tree.obj / tree.mtl     two parts: "Bark" (trunk + branches) and "Leaves" (double-sided leaf cards)
    bark_albedo.png, bark_normal.png              tileable bark
    leaves_albedo.png, leaves_mask.png, leaves_normal.png   a sprig of leaves on a transparent card

Conventions the foliage wind shader (Shaders/Common/FoliageWind.fx) relies on:
    * Z is up, metres, the trunk base is at the origin.
    * Leaf cards: the sprig's stem is at the bottom of the leaf texture (OBJ v = 0, DirectX v = 1 after the
      importer flips V), so flutter fades out where a card attaches to its twig.
    * Every leaf card's U is offset by a random whole number (0..15). Sampling wraps, so the texture is
      unchanged, but the shader reads floor(u) as a per-card id and gives each card its own flutter phase.
    * Both sides of a card are separate faces at the same positions with opposite normals; the shader moves
      them identically.

Deterministic (fixed seed): re-running produces the same files.
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SEED = 7
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

rng = np.random.default_rng(SEED)


# ----------------------------------------------------------------------------------------------------------
#  Small vector helpers
# ----------------------------------------------------------------------------------------------------------

def norm(v):
    v = np.asarray(v, dtype=float)
    length = np.linalg.norm(v)
    return v / length if length > 1e-9 else v


def perpendicular(d):
    """Any unit vector perpendicular to d."""
    helper = np.array([0.0, 0.0, 1.0]) if abs(d[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    return norm(np.cross(d, helper))


def rotate(v, axis, angle):
    """Rodrigues rotation of v around the unit axis."""
    axis = norm(axis)
    return (v * math.cos(angle) + np.cross(axis, v) * math.sin(angle)
            + axis * np.dot(axis, v) * (1 - math.cos(angle)))


# ----------------------------------------------------------------------------------------------------------
#  Mesh accumulation (OBJ indices are 1-based and shared across groups)
# ----------------------------------------------------------------------------------------------------------

class Part:
    def __init__(self, name):
        self.name = name
        self.v, self.vt, self.vn, self.f = [], [], [], []

    def vertex(self, p, uv, n):
        self.v.append(p)
        self.vt.append(uv)
        self.vn.append(norm(n))
        return len(self.v) - 1

    def tri(self, a, b, c):
        # Counter-clockwise seen from the side the normals face (OBJ convention).
        self.f.append((a, b, c))


bark = Part("Bark")
leaves = Part("Leaves")


# ----------------------------------------------------------------------------------------------------------
#  Branch skeleton
# ----------------------------------------------------------------------------------------------------------

class Branch:
    def __init__(self, points, radii, level):
        self.points = [np.asarray(p, dtype=float) for p in points]
        self.radii = list(radii)
        self.level = level
        lengths = [0.0]
        for i in range(1, len(self.points)):
            lengths.append(lengths[-1] + np.linalg.norm(self.points[i] - self.points[i - 1]))
        self.lengths = lengths

    @property
    def length(self):
        return self.lengths[-1]

    def sample(self, t):
        """Position, direction and radius at fraction t (0..1) of the branch length."""
        s = t * self.length
        for i in range(1, len(self.points)):
            if self.lengths[i] >= s or i == len(self.points) - 1:
                seg = self.lengths[i] - self.lengths[i - 1]
                k = 0 if seg < 1e-9 else (s - self.lengths[i - 1]) / seg
                k = min(max(k, 0), 1)
                p = self.points[i - 1] * (1 - k) + self.points[i] * k
                r = self.radii[i - 1] * (1 - k) + self.radii[i] * k
                return p, norm(self.points[i] - self.points[i - 1]), r
        return self.points[-1], norm(self.points[-1] - self.points[-2]), self.radii[-1]


def grow(start, direction, length, segments, r0, r1, up_bend, wander, level):
    """A curved branch: bends towards +Z (up_bend per metre) with some random wander."""
    points, radii = [np.asarray(start, dtype=float)], [r0]
    d = norm(direction)
    step = length / segments
    for i in range(1, segments + 1):
        d = norm(d + np.array([0, 0, up_bend * step]) + rng.normal(0, wander * step, 3))
        points.append(points[-1] + d * step)
        t = i / segments
        radii.append(r0 + (r1 - r0) * (t ** 0.8))
    return Branch(points, radii, level)


HEIGHT = 7.2
branches = []

# Trunk + leader: slightly wandering, thick root flare at the base.
trunk_points, trunk_radii = [], []
trunk_top = 6.9
trunk_segments = 22
lean = rng.normal(0, 0.06, 2)
for i in range(trunk_segments + 1):
    t = i / trunk_segments
    z = t * trunk_top
    x = lean[0] * z * z / trunk_top + 0.05 * math.sin(z * 1.7)
    y = lean[1] * z * z / trunk_top + 0.05 * math.cos(z * 1.3 + 0.5)
    r = 0.25 * (1 - 0.88 * t) ** 1.1 + 0.012
    trunk_points.append((x, y, z))
    trunk_radii.append(r)
trunk = Branch(trunk_points, trunk_radii, 0)
branches.append(trunk)

# Main limbs around the trunk (golden angle), long and low-ish at the bottom of the crown, short at the top.
limbs = []
limb_count = 12
for i in range(limb_count):
    t = i / (limb_count - 1)
    h = 2.1 + t * 3.9 + rng.uniform(-0.15, 0.15)
    p, _, r_parent = trunk.sample(h / trunk_top)
    azimuth = i * math.radians(137.5) + rng.uniform(-0.25, 0.25)
    elevation = math.radians(20 + 45 * t + rng.uniform(-6, 6))
    d = np.array([math.cos(azimuth) * math.cos(elevation), math.sin(azimuth) * math.cos(elevation),
                  math.sin(elevation)])
    length = (3.2 - 1.8 * t) * rng.uniform(0.9, 1.1)
    # Start inside the trunk so the joint has no gap.
    start = p - d * r_parent * 0.5
    limb = grow(start, d, length, 9, r_parent * 0.55, 0.016, 0.22, 0.25, 1)
    limbs.append(limb)
    branches.append(limb)

# Twigs on the limbs and on the leader.
twigs = []
for parent in limbs + [trunk]:
    count = 11 if parent is not trunk else 8
    t_start = 0.2 if parent is not trunk else 0.68
    for j in range(count):
        t = t_start + (0.96 - t_start) * (j + rng.uniform(0.1, 0.9)) / count
        p, d_parent, r_parent = parent.sample(t)
        side = rotate(perpendicular(d_parent), d_parent, rng.uniform(0, 2 * math.pi))
        # Twigs prefer the sides and top of their limb, not straight down.
        if side[2] < -0.3:
            side[2] = -side[2]
        angle = math.radians(rng.uniform(35, 60))
        d = norm(d_parent * math.cos(angle) + side * math.sin(angle))
        length = rng.uniform(0.7, 1.25) * (1.15 - 0.45 * t) * (0.8 if parent is trunk else 1)
        twig = grow(p, d, length, 4, max(r_parent * 0.55, 0.012), 0.006, 0.35, 0.5, 2)
        twigs.append(twig)
        branches.append(twig)


# ----------------------------------------------------------------------------------------------------------
#  Bark tubes
# ----------------------------------------------------------------------------------------------------------

BARK_TEXTURE_METRES = 0.7  # world size of one bark texture repeat around a 0.25 m trunk


def tube(branch, sides):
    base_r = branch.radii[0]
    # Whole repeats around the circumference so the seam matches.
    repeats_u = max(1, round(2 * math.pi * base_r / BARK_TEXTURE_METRES))
    v_scale = repeats_u / (2 * math.pi * max(base_r, 0.02))
    rings = []
    frame_n = perpendicular(norm(branch.points[1] - branch.points[0]))
    for i, p in enumerate(branch.points):
        if i == 0:
            d = norm(branch.points[1] - branch.points[0])
        elif i == len(branch.points) - 1:
            d = norm(branch.points[-1] - branch.points[-2])
        else:
            d = norm(branch.points[i + 1] - branch.points[i - 1])
        # Parallel transport the ring frame so the tube doesn't twist.
        frame_n = norm(frame_n - d * np.dot(frame_n, d))
        frame_b = np.cross(d, frame_n)
        ring = []
        z_world = p[2]
        for s in range(sides + 1):
            a = 2 * math.pi * s / sides
            radial = frame_n * math.cos(a) + frame_b * math.sin(a)
            r = branch.radii[i]
            if branch.level == 0:
                # Root flare with buttresses at the base of the trunk.
                flare = math.exp(-z_world / 0.35)
                r *= 1 + 0.55 * flare + 0.35 * flare * max(0.0, math.cos(5 * a + 0.7))
            u = repeats_u * s / sides
            v = branch.lengths[i] * v_scale
            ring.append(bark.vertex(p + radial * r, (u, v), radial))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for s in range(sides):
            a, b = rings[i][s], rings[i][s + 1]
            c, d = rings[i + 1][s], rings[i + 1][s + 1]
            # Ring angle runs counter-clockwise around the direction of growth.
            bark.tri(a, b, d)
            bark.tri(a, d, c)
    # Close the tip with a small cone.
    tip_dir = norm(branch.points[-1] - branch.points[-2])
    tip = bark.vertex(branch.points[-1] + tip_dir * branch.radii[-1] * 2,
                      (0.5, branch.length * v_scale + 0.05), tip_dir)
    last = rings[-1]
    for s in range(sides):
        bark.tri(last[s], last[s + 1], tip)


tube(trunk, 14)
for limb in limbs:
    tube(limb, 8)
for twig in twigs:
    tube(twig, 5)


# ----------------------------------------------------------------------------------------------------------
#  Leaf cards
# ----------------------------------------------------------------------------------------------------------

crown_center = np.array([0.0, 0.0, 4.6])
card_count = 0


def leaf_card(base, direction, size):
    """A slightly cupped and drooping 2 x 2-quad card; base = where the sprig's stem attaches."""
    global card_count
    card_id = int(rng.integers(0, 16))
    d = norm(direction)
    w = rotate(perpendicular(d), d, rng.uniform(0, 2 * math.pi))
    # Lay most cards closer to horizontal so the crown reads as foliage from below and from the side.
    n = norm(np.cross(w, d))
    if abs(n[2]) < 0.35:
        w = rotate(w, d, math.radians(rng.uniform(40, 70)))
        n = norm(np.cross(w, d))
    width, height = size * 0.9, size
    cup = 0.08 * size
    droop = 0.12 * size
    grid = []
    for row in range(3):
        fv = row / 2  # 0 = stem (bottom of the texture), 1 = tip
        line = []
        for col in range(3):
            fu = col / 2
            x = (fu - 0.5) * width
            p = base + d * (fv * height) + w * x
            p = p + n * (cup * (2 * (fu - 0.5)) ** 2) + np.array([0, 0, -droop * fv * fv])
            line.append((p, (fu + card_id, fv)))
        grid.append(line)
    # Shared normal per side: flat-ish cards keep one flutter phase and light evenly.
    for side, normal in ((0, n), (1, -n)):
        ids = [[leaves.vertex(p, uv, normal) for (p, uv) in line] for line in grid]
        for row in range(2):
            for col in range(2):
                a, b = ids[row][col], ids[row][col + 1]
                c, e = ids[row + 1][col], ids[row + 1][col + 1]
                if side == 0:
                    # Counter-clockwise around n: w (columns) x d (rows) = n.
                    leaves.tri(a, b, e)
                    leaves.tri(a, e, c)
                else:
                    leaves.tri(a, e, b)
                    leaves.tri(a, c, e)
    card_count += 1


def outward(p):
    o = p - crown_center
    o[2] *= 0.6
    return norm(o)


for twig in twigs:
    cards = 7
    for k in range(cards):
        t = 0.15 + 0.85 * (k + rng.uniform(0.0, 0.8)) / cards
        p, d, _ = twig.sample(t)
        direction = norm(d * 0.6 + outward(p) * 0.7 + rng.normal(0, 0.45, 3) + np.array([0, 0, 0.15]))
        leaf_card(p, direction, rng.uniform(0.75, 1.05))
    # A tuft at the very tip.
    p, d, _ = twig.sample(1.0)
    for k in range(3):
        leaf_card(p, norm(d + rng.normal(0, 0.5, 3)), rng.uniform(0.65, 0.9))

for limb in limbs:
    for k in range(9):
        t = 0.3 + 0.7 * (k + rng.uniform(0, 1)) / 9
        p, d, _ = limb.sample(t)
        direction = norm(outward(p) + rng.normal(0, 0.5, 3) + np.array([0, 0, 0.2]))
        leaf_card(p, direction, rng.uniform(0.75, 1.05))


# ----------------------------------------------------------------------------------------------------------
#  OBJ / MTL
# ----------------------------------------------------------------------------------------------------------

def write_obj():
    path = os.path.join(OUT, "tree.obj")
    with open(path, "w", newline="\n") as f:
        f.write("# Tree: a 7 m broadleaf tree, Z up, metres, trunk base at the origin. Generated by Source/generate_tree.py.\n")
        f.write("# Parts: Bark (trunk + branches) and Leaves (double-sided leaf cards; U offset by whole numbers = card id).\n")
        f.write("mtllib tree.mtl\n")
        offset = 0
        for part in (bark, leaves):
            f.write(f"o {part.name}\n")
            for p in part.v:
                f.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
            for uv in part.vt:
                f.write(f"vt {uv[0]:.5f} {uv[1]:.5f}\n")
            for n in part.vn:
                f.write(f"vn {n[0]:.5f} {n[1]:.5f} {n[2]:.5f}\n")
            f.write(f"usemtl {part.name}\n")
            for a, b, c in part.f:
                ia, ib, ic = a + 1 + offset, b + 1 + offset, c + 1 + offset
                f.write(f"f {ia}/{ia}/{ia} {ib}/{ib}/{ib} {ic}/{ic}/{ic}\n")
            offset += len(part.v)
    with open(os.path.join(OUT, "tree.mtl"), "w", newline="\n") as f:
        f.write("# Colours only; the engine binds the textures (Assets.LoadTree).\n")
        f.write("newmtl Bark\nKd 0.36 0.29 0.23\n\n")
        f.write("newmtl Leaves\nKd 0.25 0.42 0.14\n")
    print(f"tree.obj: bark {len(bark.v)} verts / {len(bark.f)} tris, leaves {card_count} cards, "
          f"{len(leaves.v)} verts / {len(leaves.f)} tris")


# ----------------------------------------------------------------------------------------------------------
#  Textures
# ----------------------------------------------------------------------------------------------------------

def tileable_noise(size, freqs, seed, warp=0.0, jitter_y=True):
    """Periodic noise from random-phase sines on integer frequencies (tiles on both axes).
    warp bends x by a low-frequency wave along y (wavy bark plates); jitter_y=False keeps y frequencies as given."""
    r = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size] / size
    if warp:
        x = x + warp * (np.sin(2 * np.pi * (2 * y + 0.0)) * 0.5 + np.sin(2 * np.pi * (3 * y + x) + 1.3) * 0.5)
    out = np.zeros((size, size))
    for fx, fy, amp in freqs:
        for _ in range(4):
            kx = fx + int(r.integers(-2, 3))
            ky = fy + (int(r.integers(-1, 2)) if jitter_y else int(r.integers(0, 2)) * (1 if r.random() < 0.5 else -1))
            out += amp * np.sin(2 * np.pi * (kx * x + ky * y) + r.uniform(0, 2 * np.pi))
    out -= out.min()
    return out / max(out.max(), 1e-9)


def normal_map(height, strength, wrap):
    if wrap:
        dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5
        dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5
    else:
        dy, dx = np.gradient(height)
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / length, ny / length, nz / length], -1) * 0.5 + 0.5
    return Image.fromarray((rgb * 255).astype(np.uint8), "RGB")


def bark_textures():
    size = 512
    # Long vertical plates split by fissures: high frequency across, low along the trunk.
    plates = tileable_noise(size, [(7, 0, 1.0), (11, 1, 0.6), (17, 1, 0.35), (5, 0, 0.5)], 11, warp=0.012, jitter_y=False)
    depth = tileable_noise(size, [(3, 5, 1.0), (5, 7, 0.6), (8, 11, 0.4)], 14)
    fissure = 1 - np.abs(plates * 2 - 1)            # ridges where the noise crosses 0.5
    fissure = np.clip((fissure - 0.5) / 0.5, 0, 1) ** 1.5 * (0.45 + 0.55 * depth)
    grain = tileable_noise(size, [(40, 3, 1.0), (64, 6, 0.5), (90, 9, 0.3)], 12, warp=0.02)
    height = (1 - fissure) * 0.8 + grain * 0.2
    patches = tileable_noise(size, [(2, 3, 1.0), (3, 2, 0.8), (4, 5, 0.5), (6, 4, 0.4), (1, 2, 0.6)], 13)

    base = np.array([0.40, 0.34, 0.28])
    dark = np.array([0.13, 0.10, 0.08])
    lichen = np.array([0.46, 0.49, 0.38])
    color = base[None, None, :] * (0.75 + 0.35 * grain[..., None])
    color = color * (1 - fissure[..., None]) + dark * fissure[..., None]
    lichen_amount = np.clip((patches - 0.62) / 0.2, 0, 1)[..., None] * (1 - fissure[..., None]) * 0.6
    color = color * (1 - lichen_amount) + lichen * lichen_amount
    Image.fromarray((np.clip(color, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "bark_albedo.png"))
    normal_map(height, 6.0, True).save(os.path.join(OUT, "bark_normal.png"))


def leaf_textures():
    size = 1024
    ss = 2  # supersampling for smooth edges
    S = size * ss
    coverage = Image.new("L", (S, S), 0)
    albedo = Image.new("RGB", (S, S), (0, 0, 0))
    height = Image.new("L", (S, S), 0)
    dc, da, dh = ImageDraw.Draw(coverage), ImageDraw.Draw(albedo), ImageDraw.Draw(height)
    r = np.random.default_rng(21)

    # Twig from the bottom centre (the card's stem) towards the top.
    def twig_point(t):
        return (S * (0.5 + 0.05 * math.sin(t * 2.5)), S * (1.0 - 0.86 * t))

    twig_color = (92, 70, 52)
    steps = 60
    for i in range(steps):
        t0, t1 = i / steps, (i + 1) / steps
        width = int(S * (0.014 * (1 - t0) + 0.004))
        p0, p1 = twig_point(t0), twig_point(t1)
        for draw, value in ((dc, 255), (da, twig_color), (dh, 150)):
            draw.line([p0, p1], fill=value, width=width)

    # Leaves alternating along the twig, the last one at the tip.
    leaf_specs = []
    count = 9
    for i in range(count):
        t = 0.16 + 0.8 * i / (count - 1)
        side = -1 if i % 2 == 0 else 1
        if i == count - 1:
            side, angle = 0, 0.0
        else:
            angle = side * math.radians(r.uniform(38, 62))
        length = S * r.uniform(0.27, 0.34) * (1.0 - 0.25 * abs(t - 0.6))
        leaf_specs.append((t, angle, length))

    for t, angle, length in leaf_specs:
        base = twig_point(t)
        # Leaf axis in image space (up = -y), rotated by angle.
        axis = (math.sin(angle), -math.cos(angle))
        perp = (-axis[1], axis[0])
        petiole = length * 0.12
        start = (base[0] + axis[0] * petiole, base[1] + axis[1] * petiole)
        hue = r.uniform(-1, 1)
        leaf_rgb = np.array([0.30 + 0.06 * hue, 0.48 + 0.05 * r.uniform(-1, 1), 0.13 + 0.03 * hue]) * 0.78
        if r.random() < 0.18:  # a few older, yellower leaves
            leaf_rgb = leaf_rgb * 0.6 + np.array([0.52, 0.50, 0.14]) * 0.4
        # Outline: pointed ovate leaf, widest a third of the way up, serrated edge.
        outline = []
        n = 90
        for k in range(n + 1):
            s = k / n
            w = 0.31 * length * (math.sin(math.pi * s ** 0.75) ** 1.2)
            w *= 1 + 0.04 * math.sin(s * 70)  # serration
            outline.append((s, w))
        poly = ([(start[0] + axis[0] * s * length + perp[0] * w, start[1] + axis[1] * s * length + perp[1] * w)
                 for s, w in outline]
                + [(start[0] + axis[0] * s * length - perp[0] * w, start[1] + axis[1] * s * length - perp[1] * w)
                   for s, w in reversed(outline)])
        dc.line([base, start], fill=255, width=int(S * 0.006))
        da.line([base, start], fill=(70, 88, 40), width=int(S * 0.006))
        dc.polygon(poly, fill=255)
        da.polygon(poly, fill=tuple(int(c * 255) for c in leaf_rgb))
        dh.polygon(poly, fill=120)
        # Midrib and veins: lighter in colour, ridges in the normal map.
        tip = (start[0] + axis[0] * length, start[1] + axis[1] * length)
        vein_rgb = tuple(int(min(1, c * 1.15 + 0.03) * 255) for c in leaf_rgb)
        da.line([start, tip], fill=vein_rgb, width=int(S * 0.004))
        dh.line([start, tip], fill=175, width=int(S * 0.005))
        for k in range(1, 8):
            s = k / 8.5
            w = 0.31 * length * (math.sin(math.pi * s ** 0.75) ** 1.2) * 0.85
            for sign in (-1, 1):
                p0 = (start[0] + axis[0] * s * length, start[1] + axis[1] * s * length)
                p1 = (p0[0] + axis[0] * w * 0.6 + sign * perp[0] * w, p0[1] + axis[1] * w * 0.6 + sign * perp[1] * w)
                da.line([p0, p1], fill=vein_rgb, width=max(1, int(S * 0.0018)))
                dh.line([p0, p1], fill=150, width=max(2, int(S * 0.0025)))

    coverage = coverage.resize((size, size), Image.LANCZOS)
    albedo = albedo.resize((size, size), Image.LANCZOS)
    height = height.resize((size, size), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))

    a = np.asarray(albedo).astype(float) / 255
    m = np.asarray(coverage).astype(float) / 255
    # Mottling so leaves aren't flat colour.
    y, x = np.mgrid[0:size, 0:size]
    mottle = (np.sin(x * 0.05 + np.sin(y * 0.031) * 3) * np.sin(y * 0.043 + np.sin(x * 0.027) * 2)) * 0.06
    a = np.clip(a * (1 + mottle[..., None]), 0, 1)
    # Un-premultiply the downsampled edge colour, then bleed leaf colour into the empty area so the
    # cut-out edge (and its mip maps) never turns black.
    a = np.where(m[..., None] > 0.02, a / np.maximum(m[..., None], 0.02), 0)
    filled = a.copy()
    weight = (m > 0.02).astype(float)
    for radius in (2, 4, 8, 16, 32, 64):
        blurred = np.asarray(Image.fromarray((filled * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(radius))) / 255
        wblur = np.asarray(Image.fromarray((weight * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(radius))) / 255
        fill = blurred / np.maximum(wblur[..., None], 1e-3)
        empty = weight < 0.5
        filled[empty] = fill[empty]
        weight = np.maximum(weight, (wblur > 0.01).astype(float))
    Image.fromarray((np.clip(filled, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "leaves_albedo.png"))
    Image.fromarray((m * 255).astype(np.uint8), "L").save(os.path.join(OUT, "leaves_mask.png"))
    h = np.asarray(height).astype(float) / 255
    normal_map(h, 3.0, False).save(os.path.join(OUT, "leaves_normal.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    write_obj()
    bark_textures()
    leaf_textures()
    print("written to", OUT)
