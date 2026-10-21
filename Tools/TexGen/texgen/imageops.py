"""numpy/Pillow image helpers. Every filter wraps around the edges so tileable input stays tileable."""
import numpy as np

LUMA = np.array([0.2126, 0.7152, 0.0722])


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def luminance(rgb):
    """Relative luminance of an RGB array (any colour space: it is only a weighted sum)."""
    return np.asarray(rgb, dtype=np.float64) @ LUMA


def gaussian_blur_wrap(a, sigma):
    a = np.asarray(a, dtype=np.float64)
    if sigma <= 0:
        return a
    if a.ndim == 3:
        return np.stack([gaussian_blur_wrap(a[..., c], sigma) for c in range(a.shape[2])], axis=-1)
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    kernel = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.fft.irfft2(np.fft.rfft2(a) * kernel, s=(h, w))


def sobel_wrap(h):
    """(dx, dy) per pixel; dx grows with the column, dy with the row (down). Wraps at the edges."""
    roll = np.roll
    xp, xm = roll(h, -1, 1), roll(h, 1, 1)
    dx = ((xp - xm) * 2 + roll(xp, -1, 0) - roll(xm, -1, 0) + roll(xp, 1, 0) - roll(xm, 1, 0)) / 8.0
    yp, ym = roll(h, -1, 0), roll(h, 1, 0)
    dy = ((yp - ym) * 2 + roll(yp, -1, 1) - roll(ym, -1, 1) + roll(yp, 1, 1) - roll(ym, 1, 1)) / 8.0
    return dx, dy


def normalize01(a, lo_pct=1.0, hi_pct=99.0):
    lo, hi = np.percentile(a, [lo_pct, hi_pct])
    if hi - lo < 1e-9:
        return np.zeros_like(a, dtype=np.float64)
    return np.clip((a - lo) / (hi - lo), 0.0, 1.0)


def to_u8(a):
    return np.clip(np.rint(np.asarray(a, dtype=np.float64) * 255.0), 0, 255).astype(np.uint8)
