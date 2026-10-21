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


def height_from_rgb(rgb_u8):
    gray = luminance(rgb_u8.astype(np.float64) / 255.0)
    return normalize01(gaussian_blur_wrap(gray, 1.5))


def normal_from_height(height, strength, size):
    """Y+ (OpenGL) unit normals from a 0..1 height field; `strength` is independent of the resolution."""
    dx, dy = sobel_wrap(height)
    k = strength * size / 128.0
    # Row index grows downwards, +Y of the normal map points up: dH/dy_up = -dy.
    n = np.stack([-dx * k, dy * k, np.ones_like(height)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


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
    hf = luminance(albedo.astype(np.float64) / 255.0)
    hf = hf - gaussian_blur_wrap(hf, 3.0)
    detail = normal_from_height(hf * 3.0, normal_strength * 0.35, size)
    return {
        "albedo": albedo,
        "height": height,
        "normal": reoriented_blend(shape, detail),
        "roughness_norm": roughness_norm_from_rgb(rgb_u8),
        "metalness": np.zeros((size, size)),
    }
