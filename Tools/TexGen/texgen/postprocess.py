"""Final clean-up and PNG writing. Normals work internally in OpenGL / Y+ and are converted on write."""
import numpy as np
from PIL import Image

from .imageops import to_u8

ROUGHNESS_MIN = 0.04
MAGENTA = (255, 0, 255)


def normal_flip_y(n):
    """DirectX <-> OpenGL: the conversion is its own inverse."""
    out = np.array(n, dtype=np.float64, copy=True)
    out[..., 1] = -out[..., 1]
    return out


def sanitize_normal(n, convention="directx"):
    """Renormalises, forces z > 0 and encodes rgb = n * 0.5 + 0.5 in the output convention. Input is Y+ (OpenGL)."""
    n = np.array(n, dtype=np.float64, copy=True)
    n[..., 2] = np.maximum(n[..., 2], 1e-3)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
    if convention == "directx":
        n = normal_flip_y(n)
    elif convention != "opengl":
        raise ValueError("normal convention must be directx or opengl: " + str(convention))
    return to_u8(n * 0.5 + 0.5)


def decode_normal(rgb_u8, convention="directx"):
    """Inverse of sanitize_normal: back to Y+ unit vectors (for checks and validation)."""
    n = rgb_u8.astype(np.float64) / 255.0 * 2.0 - 1.0
    if convention == "directx":
        n = normal_flip_y(n)
    return n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)


def clamp_roughness(r):
    return np.clip(r, ROUGHNESS_MIN, 1.0)


def binarize_metal(m, threshold=0.5, force_dielectric=False):
    if force_dielectric:
        return np.zeros(np.shape(m), dtype=np.float64)
    return (np.asarray(m) >= threshold).astype(np.float64)


def ao_from_height(height, relief=None, radius=None, minimum=0.35):
    """Horizon-style ambient occlusion from a height field in 0..1: 8 directions, short radius, wraps at the edges.
    A flat height gives exactly 1. `relief` = how many pixels tall the full 0..1 range is."""
    h = np.asarray(height, dtype=np.float64)
    size = h.shape[0]
    radius = radius or max(2, size // 64)
    relief = relief or size * 0.04
    occlusion = np.zeros_like(h)
    directions = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
    for dy, dx in directions:
        step_len = float(np.hypot(dy, dx))
        steepest = np.zeros_like(h)
        for k in range(1, radius + 1):
            neighbour = np.roll(h, (-dy * k, -dx * k), axis=(0, 1))
            slope = (neighbour - h) * relief / (k * step_len)
            steepest = np.maximum(steepest, slope)
        occlusion += np.arctan(steepest) / (np.pi / 2.0)
    ao = 1.0 - occlusion / len(directions)
    return np.clip(ao, minimum, 1.0)


def remove_magenta(albedo_u8):
    """The old colour key: a pure magenta pixel would be transparent in some pipelines. Nudge it by one step."""
    out = albedo_u8.copy()
    mask = (out[..., 0] == MAGENTA[0]) & (out[..., 1] == MAGENTA[1]) & (out[..., 2] == MAGENTA[2])
    out[mask] = (254, 1, 254)
    return out


def save_rgb(path, arr_u8):
    Image.fromarray(np.ascontiguousarray(arr_u8), "RGB").save(path, format="PNG")


def save_gray(path, arr01):
    Image.fromarray(to_u8(arr01), "L").save(path, format="PNG")


def save_gray16(path, arr01):
    Image.fromarray(np.clip(np.rint(np.asarray(arr01) * 65535.0), 0, 65535).astype(np.uint16)).save(path, format="PNG")
