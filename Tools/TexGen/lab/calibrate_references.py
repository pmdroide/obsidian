"""D2 of Docs/Research/AiTextures/PLAN.md: the `classic` backend against reference PBR sets (CC0 Poly Haven).

    cd Tools/TexGen
    .venv/Scripts/python lab/calibrate_references.py [--refs DIR] [--generated DIR] [--names Bricks Cobblestone RockyDirt]

For every <Name> it compares the maps `python -m texgen generate --backend classic --input-rgb <Name>_BaseColor.jpg --out
<generated>/<Name>` wrote (default cache/_calib) with <Name>_BaseColor/Normal/Roughness/Height from the reference folder
(default Engine/Content/GameObjects/Textures/Ground, DirectX / Y- normals) and prints the numbers that decide whether
source2.json needs a tweak: albedo luminance, roughness mean/spread, normal strength, how well the generated normals and
height follow the reference ones.
"""
import argparse
import glob
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


def load(path, size, mode):
    img = Image.open(path).convert(mode)
    if img.size != (size, size):
        img = img.resize((size, size), Image.LANCZOS)
    return np.asarray(img, dtype=np.float32) / 255.0


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def luminance(rgb):
    lin = srgb_to_linear(rgb)
    return 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]


def normal_vectors(rgb):
    v = rgb * 2.0 - 1.0
    return v[..., 0], v[..., 1], v[..., 2]


def corr(a, b):
    a = a - a.mean()
    b = b - b.mean()
    d = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / d) if d > 1e-9 else 0.0


def highpass(x, sigma=8):
    """x minus its blur (FFT, wrap): the detail the normal map is made of."""
    n = x.shape[0]
    f = np.fft.fftfreq(n)
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    return x - np.real(np.fft.ifft2(np.fft.fft2(x) * kernel))


def compare(name, refs, generated):
    size = 1024
    gen = glob.glob(os.path.join(generated, name, "*_BaseColor.png"))
    if not gen:
        print(f"{name}: no generated set in {generated}")
        return None
    stem = gen[0][: -len("_BaseColor.png")]
    rows = {}
    ra, ga = load(os.path.join(refs, f"{name}_BaseColor.jpg"), size, "RGB"), load(stem + "_BaseColor.png", size, "RGB")
    rl, gl = luminance(ra), luminance(ga)
    rows["albedo luminance mean (linear)"] = (rl.mean(), gl.mean())
    rows["albedo luminance std"] = (rl.std(), gl.std())
    rr, gr = load(os.path.join(refs, f"{name}_Roughness.jpg"), size, "L"), load(stem + "_Roughness.png", size, "L")
    rows["roughness mean"] = (rr.mean(), gr.mean())
    rows["roughness std"] = (rr.std(), gr.std())
    rows["roughness corr with reference"] = (1.0, corr(rr, gr))
    rn, gn = load(os.path.join(refs, f"{name}_Normal.jpg"), size, "RGB"), load(stem + "_Normal.png", size, "RGB")
    rx, ry, rz = normal_vectors(rn)
    gx, gy, gz = normal_vectors(gn)
    rows["normal tilt mean |xy|"] = (np.hypot(rx, ry).mean(), np.hypot(gx, gy).mean())
    rows["normal z mean"] = (rz.mean(), gz.mean())
    # Same convention (DirectX / Y-) and same surface: the slopes of both maps should correlate positively
    rows["normal x corr"] = (1.0, corr(rx, gx))
    rows["normal y corr"] = (1.0, corr(ry, gy))
    rh, gh = load(os.path.join(refs, f"{name}_Height.jpg"), size, "L"), load(stem + "_Height.png", size, "L")
    rows["height corr with reference"] = (1.0, corr(highpass(rh), highpass(gh)))
    rows["height std"] = (rh.std(), gh.std())
    ao = load(stem + "_AO.png", size, "L")
    rows["AO min / mean"] = (float("nan"), float(ao.min()))
    print(f"\n== {name}   (reference | generated)")
    for key, (r, g) in rows.items():
        print(f"  {key:34s} {r:8.3f} | {g:8.3f}")
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--refs", default=os.path.join(ROOT, "Engine", "Content", "GameObjects", "Textures", "Ground"))
    ap.add_argument("--generated", default=os.path.join(ROOT, "Tools", "TexGen", "cache", "_calib"))
    ap.add_argument("--names", nargs="+", default=["Bricks", "Cobblestone", "RockyDirt"])
    args = ap.parse_args()
    ok = [compare(n, args.refs, args.generated) for n in args.names]
    return 0 if all(ok) else 1


if __name__ == "__main__":
    sys.exit(main())
