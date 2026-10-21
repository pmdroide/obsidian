"""Classic RGB -> PBR estimator (no models). Works in OpenGL / Y+ internally; every filter wraps around."""
import numpy as np

from ..imageops import (gaussian_blur_wrap, linear_to_srgb, luminance, normalize01, sobel_wrap, srgb_to_linear,
                        to_u8)


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


def height_from_rgb(rgb_u8):
    """0..1 height from the luminance. The range follows the input's contrast, so a nearly uniform photo (polished
    steel) stays nearly flat instead of being stretched into bumps; the dark tail is softened (soften_shadows)."""
    gray = gaussian_blur_wrap(luminance(rgb_u8.astype(np.float64) / 255.0), 1.5)
    gray = soften_shadows(gray)
    span = min(1.0, float(gray.std()) / HEIGHT_REFERENCE_STD)
    return 0.5 + (normalize01(gray) - 0.5) * span


def normal_from_height(height, strength, size):
    """Y+ (OpenGL) unit normals from a 0..1 height field; `strength` is independent of the resolution."""
    dx, dy = sobel_wrap(height)
    k = strength * size / 128.0
    # Row index grows downwards, +Y of the normal map points up: dH/dy_up = -dy.
    n = np.stack([-dx * k, dy * k, np.ones_like(height)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


# Steepest slope (|xy| / z) a classic normal keeps: ~40 degrees, rolled off (tanh) above, so a luminance edge
# estimated as a cliff doesn't draw as a black line under grazing light.
MAX_SLOPE = 0.85


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
    height = height_from_rgb(rgb_u8)
    shape = normal_from_height(height, normal_strength, size)
    hf = soften_shadows(luminance(albedo.astype(np.float64) / 255.0))
    hf = hf - gaussian_blur_wrap(hf, 3.0)
    # Thin high-contrast lines (outlines a game or photo drew around a ledge) would make the steepest slopes of all.
    c = max(2.0 * float(hf.std()), 1e-6)
    hf = c * np.tanh(hf / c)
    detail = normal_from_height(hf * 3.0, normal_strength * 0.35, size)
    return {
        "albedo": albedo,
        "height": height,
        "normal": limit_slope(reoriented_blend(shape, detail)),
        "roughness_norm": roughness_norm_from_rgb(rgb_u8),
        "metalness": np.zeros((size, size)),
    }
