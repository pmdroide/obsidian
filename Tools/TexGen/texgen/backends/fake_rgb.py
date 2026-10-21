"""`fake` backend: procedural, tileable-by-construction colour texture (periodic value noise). No GPU, no models."""
import hashlib

import numpy as np

# sRGB base colour per family (0..1); the prompt hash tints it.
FAMILY_BASE = {
    "stone": (0.50, 0.49, 0.47), "brick": (0.55, 0.30, 0.24), "concrete": (0.55, 0.55, 0.53),
    "tile": (0.62, 0.60, 0.55), "plaster": (0.72, 0.68, 0.60), "wood": (0.45, 0.30, 0.18),
    "metal": (0.62, 0.63, 0.65), "painted_metal": (0.25, 0.42, 0.50), "fabric": (0.45, 0.38, 0.35),
    "ground": (0.38, 0.30, 0.22), "organic": (0.28, 0.40, 0.20),
}


def prompt_rng(prompt, seed):
    digest = hashlib.sha256(("%d|%s" % (seed, prompt)).encode("utf-8")).digest()
    return np.random.default_rng(int.from_bytes(digest[:8], "little")), digest


def value_noise(size, cells_x, cells_y, rng):
    """Smooth periodic value noise in 0..1: the grid wraps, so the image tiles."""
    grid = rng.random((cells_y, cells_x))
    tx = np.arange(size) * cells_x / size
    ty = np.arange(size) * cells_y / size
    x0 = np.floor(tx).astype(int); fx = tx - x0; fx = fx * fx * (3 - 2 * fx); x1 = (x0 + 1) % cells_x; x0 %= cells_x
    y0 = np.floor(ty).astype(int); fy = ty - y0; fy = fy * fy * (3 - 2 * fy); y1 = (y0 + 1) % cells_y; y0 %= cells_y
    rows = grid[:, x0] * (1 - fx)[None, :] + grid[:, x1] * fx[None, :]
    return rows[y0, :] * (1 - fy)[:, None] + rows[y1, :] * fy[:, None]


def fbm(size, rng, base_cells=4, octaves=6, stretch=(1, 1)):
    total = np.zeros((size, size))
    amplitude, weight = 1.0, 0.0
    for o in range(octaves):
        cx = max(1, base_cells * stretch[0] * 2 ** o)
        cy = max(1, base_cells * stretch[1] * 2 ** o)
        if cx > size or cy > size:
            break
        total += amplitude * value_noise(size, cx, cy, rng)
        weight += amplitude
        amplitude *= 0.55
    total /= max(weight, 1e-9)
    lo, hi = total.min(), total.max()
    return (total - lo) / max(hi - lo, 1e-9)


def _joints(size, family):
    """Periodic mortar/joint mask for brick and tile (1 = joint)."""
    yy, xx = np.mgrid[0:size, 0:size]
    if family == "brick":
        rows, cols = 8, 4
        bh, bw = size // rows, size // cols
        offset = ((yy // bh) % 2) * (bw // 2)
        gx, gy = (xx + offset) % bw, yy % bh
        width = max(1, size // 128)
    else:
        n = 8
        gx, gy = xx % (size // n), yy % (size // n)
        width = max(2, size // 128)
    return ((gx < width) | (gy < width)).astype(np.float64)


def generate(prompt, seed, size, family="stone"):
    """Returns an sRGB uint8 image (size, size, 3)."""
    rng, digest = prompt_rng(prompt, seed)
    base = np.array(FAMILY_BASE.get(family, FAMILY_BASE["stone"]))
    tint = 1.0 + (np.array([digest[8], digest[9], digest[10]]) / 255.0 - 0.5) * 0.35
    base = np.clip(base * tint, 0.02, 0.98)

    stretch = (1, 16) if family in ("wood", "metal") else (1, 1)
    low = fbm(size, rng, base_cells=3, octaves=3, stretch=stretch)
    detail = fbm(size, rng, base_cells=16, octaves=4, stretch=stretch)
    grain = rng.random((size, size))
    tone = 0.55 + 0.55 * low + 0.35 * (detail - 0.5) + 0.12 * (grain - 0.5)

    if family == "wood":
        yy = np.arange(size)[:, None] / size
        rings = 0.5 + 0.5 * np.sin(2 * np.pi * (14 * yy + 2.5 * low))
        tone *= 0.8 + 0.3 * rings
    if family in ("brick", "tile"):
        joints = _joints(size, family)
        tone = np.where(joints > 0, tone * 0.55, tone)
        rgb = np.where(joints[..., None] > 0, np.array([0.45, 0.43, 0.40]) * tone[..., None], base * tone[..., None])
    else:
        rgb = base * tone[..., None]
    return np.clip(np.rint(rgb * 255.0), 0, 255).astype(np.uint8)
