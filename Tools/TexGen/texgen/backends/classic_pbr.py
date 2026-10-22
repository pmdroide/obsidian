"""Classic RGB -> PBR estimator (no models). Works in OpenGL / Y+ internally; every filter wraps around."""
import numpy as np

from ..imageops import (gaussian_blur_wrap, linear_to_srgb, luminance, normalize01, sobel_wrap, srgb_to_lab,
                        srgb_to_linear, to_u8)


def delight(rgb_u8):
    """Removes low-frequency lighting: albedo = colour / (blurred luminance / mean luminance). Returns sRGB uint8."""
    size = rgb_u8.shape[0]
    lin = srgb_to_linear(rgb_u8.astype(np.float64) / 255.0)
    lum = luminance(lin)
    mean = max(float(lum.mean()), 1e-6)
    low = np.maximum(gaussian_blur_wrap(lum, size / 16.0), 1e-4)
    ratio = np.clip(mean / low, 0.25, 4.0)
    return to_u8(linear_to_srgb(np.clip(lin * ratio[..., None], 0.0, 1.0)))


# Spread (std of the sRGB luminance) from which an input counts as fully textured; flatter inputs get a flatter height.
HEIGHT_REFERENCE_STD = 0.10


def soften_shadows(gray, pct=10.0):
    """Rolls off the darkest tail of a luminance field (below its `pct` percentile) so thin contact shadows that the
    photo's lighting put under ledges (plaster over brick, stones in mortar) become shallow dips, not deep grooves."""
    knee = float(np.percentile(gray, pct))
    width = max(0.5 * (float(np.median(gray)) - knee), 1e-6)
    below = gray < knee
    out = gray.copy()
    out[below] = knee - width * np.tanh((knee - gray[below]) / width)
    return out


def contrast_span(rgb_u8):
    """0..1: how textured the input is (1 from HEIGHT_REFERENCE_STD of luminance spread up)."""
    gray = gaussian_blur_wrap(luminance(rgb_u8.astype(np.float64) / 255.0), 1.5)
    return min(1.0, float(gray.std()) / HEIGHT_REFERENCE_STD)


def luminance_height(rgb_u8):
    """0..1 height from the luminance (brighter = higher). The range follows the input's contrast, so a nearly
    uniform photo (polished steel) stays nearly flat instead of being stretched into bumps; the dark tail is softened
    (soften_shadows)."""
    gray = gaussian_blur_wrap(luminance(rgb_u8.astype(np.float64) / 255.0), 1.5)
    gray = soften_shadows(gray)
    return 0.5 + (normalize01(gray) - 0.5) * contrast_span(rgb_u8)


def _two_means(feat, iters=10):
    """2-means on (N, C) features, seeded by a median split along the first principal axis. Returns the soft
    membership of class 1 (0..1, a sigmoid along the axis between the class means) and the Fisher ratio of that axis
    (how well the two classes separate)."""
    centred = feat - feat.mean(0)
    _, _, vt = np.linalg.svd(centred[:: max(1, len(feat) // 20000)], full_matrices=False)
    proj = centred @ vt[0]
    ones = proj > np.median(proj)
    for _ in range(iters):
        if ones.all() or not ones.any():
            return np.full(len(feat), 0.5), 0.0
        mu0, mu1 = feat[~ones].mean(0), feat[ones].mean(0)
        ones = ((feat - mu1) ** 2).sum(1) < ((feat - mu0) ** 2).sum(1)
    if ones.all() or not ones.any():
        return np.full(len(feat), 0.5), 0.0
    mu0, mu1 = feat[~ones].mean(0), feat[ones].mean(0)
    p = feat @ ((mu1 - mu0) / max(float(np.linalg.norm(mu1 - mu0)), 1e-9))
    p0, p1 = p[~ones], p[ones]
    fisher = float((p1.mean() - p0.mean()) ** 2 / max(p0.var() + p1.var(), 1e-9))
    spread = max(float(np.sqrt(0.5 * (p0.var() + p1.var()))), 1e-6)
    soft = 1.0 / (1.0 + np.exp(-(p - 0.5 * (p0.mean() + p1.mean())) / (0.5 * spread)))
    return soft, fisher


def solid_phase(rgb_u8):
    """Splits the surface into two colour phases (Lab 2-means) and calls the thinner one the gap (mortar, the joints
    between cobbles, cracks): its pixels have more of the other phase around them. Brightness doesn't decide which is
    which, so light mortar between dark bricks sinks like dark joints between light stones do.
    Returns (solid: 0..1 soft mask, confidence: 0..1). The confidence is low when the colours don't split cleanly
    (low Fisher ratio) or when neither phase is a network (both equally blobby: dirt, plaster)."""
    size = rgb_u8.shape[0]
    s = size / 1024.0
    lab = gaussian_blur_wrap(srgb_to_lab(rgb_u8.astype(np.float64) / 255.0), 1.5 * s)
    soft, fisher = _two_means(lab.reshape(-1, 3) * np.array([1.0, 1.5, 1.5]))
    soft = soft.reshape(size, size)

    def interior(m):
        return float((gaussian_blur_wrap(m, 6 * s) * m).sum() / max(float(m.sum()), 1e-9))

    in1, in0 = interior(soft), interior(1.0 - soft)
    solid = soft if in1 >= in0 else 1.0 - soft
    confidence = smoothstep(STRUCTURE_THIN_LO, STRUCTURE_THIN_HI, abs(in1 - in0)) * smoothstep(1.5, 3.0, fisher)
    return solid, confidence


def smoothstep(lo, hi, x):
    t = min(1.0, max(0.0, (x - lo) / (hi - lo)))
    return t * t * (3.0 - 2.0 * t)


# How much more interior one phase must be than the other before it counts as solid ground with gaps in it.
STRUCTURE_THIN_LO = 0.03
STRUCTURE_THIN_HI = 0.10
# Bevel profile of the solid phase (pixels at 1024, scaled with the size): blurred copies of its mask averaged, so
# the edges round off over ~16 px and wide faces stay flat.
BEVEL_SIGMAS = (1, 2, 4, 8, 16)
# Share of the height the structure gets at full confidence (the rest is the luminance height).
STRUCTURE_WEIGHT = 0.8
# At full confidence: the shape normal is (1 + STRUCTURE_RELIEF) times stronger, the grain STRUCTURE_GRAIN_CUT weaker.
STRUCTURE_RELIEF = 0.8
STRUCTURE_GRAIN_CUT = 0.5


def structure_height(solid):
    s = solid.shape[0] / 1024.0
    return sum(gaussian_blur_wrap(solid, sg * s) for sg in BEVEL_SIGMAS) / len(BEVEL_SIGMAS)


def height_and_structure(rgb_u8):
    """(0..1 height, structure confidence 0..1): the luminance height, mixed with the bevelled solid phase
    (solid_phase) as far as the surface has one. Both follow the input's contrast, so a flat input stays flat."""
    lum = luminance_height(rgb_u8)
    solid, confidence = solid_phase(rgb_u8)
    w = STRUCTURE_WEIGHT * confidence
    if w <= 0.0:
        return lum, 0.0
    span = contrast_span(rgb_u8)
    struct = 0.5 + (normalize01(structure_height(solid), 0.5, 99.5) - 0.5) * span
    return (1.0 - w) * lum + w * struct, confidence


def height_from_rgb(rgb_u8):
    return height_and_structure(rgb_u8)[0]


def normal_from_height(height, strength, size):
    """Y+ (OpenGL) unit normals from a 0..1 height field; `strength` is independent of the resolution."""
    dx, dy = sobel_wrap(height)
    k = strength * size / 128.0
    # Row index grows downwards, +Y of the normal map points up: dH/dy_up = -dy.
    n = np.stack([-dx * k, dy * k, np.ones_like(height)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


# Steepest slope (|xy| / z) a classic normal keeps: ~40 degrees, rolled off (tanh) above, so a luminance edge
# estimated as a cliff doesn't draw as a black line under grazing light.
MAX_SLOPE = 1.3


def limit_slope(n, max_slope=MAX_SLOPE):
    xy = n[..., :2]
    z = np.maximum(n[..., 2:3], 1e-6)
    slope = np.linalg.norm(xy, axis=-1, keepdims=True) / z
    scale = np.where(slope > 1e-9, max_slope * np.tanh(slope / max_slope) / np.maximum(slope, 1e-9), 1.0)
    out = np.concatenate([xy * scale, z], axis=-1)
    return out / np.linalg.norm(out, axis=-1, keepdims=True)


def reoriented_blend(base, detail):
    """Reoriented normal mapping: detail normals laid over base normals (both Y+ unit vectors)."""
    t = base + np.array([0.0, 0.0, 1.0])
    u = detail * np.array([-1.0, -1.0, 1.0])
    dot = np.sum(t * u, axis=-1, keepdims=True)
    r = t * dot / np.maximum(t[..., 2:3], 1e-6) - u
    return r / np.maximum(np.linalg.norm(r, axis=-1, keepdims=True), 1e-9)


def roughness_norm_from_rgb(rgb_u8):
    """1 - normalised local contrast, in 0..1 (the family remaps it to its range afterwards)."""
    size = rgb_u8.shape[0]
    gray = luminance(rgb_u8.astype(np.float64) / 255.0)
    contrast = np.abs(gray - gaussian_blur_wrap(gray, size / 32.0))
    contrast = gaussian_blur_wrap(contrast, 2.0)
    peak = max(float(np.percentile(contrast, 98)), 1e-6)
    return np.clip(1.0 - np.clip(contrast / peak, 0.0, 1.0) * 0.8, 0.0, 1.0)


def estimate(rgb_u8, normal_strength=2.0):
    """rgb_u8: (S, S, 3) sRGB. Returns a dict: albedo (uint8 sRGB), height (0..1), normal (Y+ unit vectors),
    roughness_norm (0..1) and metalness (zeros: the style profile decides)."""
    size = rgb_u8.shape[0]
    albedo = delight(rgb_u8)
    height, structure = height_and_structure(rgb_u8)
    # A surface with real structure (bricks, cobbles) gets deeper forms and less of the luminance grain over them:
    # the grain is where a photo's colour noise turns into invented bumps.
    shape = normal_from_height(height, normal_strength * (1.0 + STRUCTURE_RELIEF * structure), size)
    hf = soften_shadows(luminance(albedo.astype(np.float64) / 255.0))
    hf = hf - gaussian_blur_wrap(hf, 3.0)
    # Thin high-contrast lines (outlines a game or photo drew around a ledge) would make the steepest slopes of all.
    c = max(2.0 * float(hf.std()), 1e-6)
    hf = c * np.tanh(hf / c)
    detail = normal_from_height(hf * 3.0, normal_strength * 0.35 * (1.0 - STRUCTURE_GRAIN_CUT * structure), size)
    return {
        "albedo": albedo,
        "height": height,
        "normal": limit_slope(reoriented_blend(shape, detail)),
        "roughness_norm": roughness_norm_from_rgb(rgb_u8),
        "metalness": np.zeros((size, size)),
    }
