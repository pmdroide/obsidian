"""Compares a generated material with a Half-Life: Alyx surface (Docs/Research/AiTextures/D2_CALIBRATION.md >
Source 2 look check).

    python Tools/TexGen/lab/compare_source2_look.py Tools/TexGen/cache/<id> [--hla brick] [--shot wall.png] [--out x.png]

Prints the albedo's sRGB luminance (mean, 5th/95th percentile) and HSV saturation (mean, 95th percentile) next to the HLA
crop's and the family's band from styles/source2.json, then writes one image: HLA crop | generated albedo | the scene
capture (--shot, e.g. from Scenes/Source2MaterialTest.obsc). The crops are cut from the screenshots in
Docs/Research/Source2Look/screenshots at run time (they aren't copied into the repository). They are lit, tonemapped
frames, not albedo: compare saturation and contrast closely, brightness only roughly.
"""
import argparse
import glob
import json
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SHOTS = os.path.join(ROOT, "Docs", "Research", "Source2Look", "screenshots", "Captura de ecrã 2026-10-17 %s.png")
STYLE = os.path.join(ROOT, "Tools", "TexGen", "texgen", "styles", "source2.json")

# name -> (screenshot, crop box, family). Picked where the surface fills the crop and the light is soft.
HLA = {
    "brick": ("202630", (1384, 40, 1766, 422), "brick"),        # red brick under flaking plaster, rooftop
    "plaster": ("202733", (1080, 420, 1440, 780), "plaster"),   # corridor wall, dim lamp
    "wood": ("202948", (600, 420, 1000, 680), "wood"),          # lift panelling, fluorescent
    "concrete": ("210142", (890, 80, 1070, 520), "concrete"),   # concrete blocks, sunlit
    "tile": ("211942", (560, 680, 1000, 860), "tile"),          # terracotta floor tiles, lamp pool
    "checker": ("203146", (600, 740, 900, 1000), "tile"),       # chequered floor, cellar
}


def hla_crop(name):
    shot, box, family = HLA[name]
    return Image.open(SHOTS % shot).convert("RGB").crop(box), family


def stats(img):
    a = np.asarray(img.convert("RGB"), dtype=np.float64)
    luma = (a / 255.0) @ np.array([0.2126, 0.7152, 0.0722]) * 255.0
    peak, low = a.max(axis=-1), a.min(axis=-1)
    sat = np.where(peak > 1.0, (peak - low) / np.maximum(peak, 1.0), 0.0)
    return {"luma": float(luma.mean()), "luma_p5": float(np.percentile(luma, 5)),
            "luma_p95": float(np.percentile(luma, 95)), "sat": float(sat.mean()), "sat_p95": float(np.percentile(sat, 95))}


def line(label, s):
    return "%-10s luma %6.1f  (p5 %5.1f, p95 %5.1f)   saturation %.3f  (p95 %.3f)" % (
        label, s["luma"], s["luma_p5"], s["luma_p95"], s["sat"], s["sat_p95"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("material", help="texgen output folder (has *_BaseColor.png and texgen.json)")
    ap.add_argument("--hla", choices=sorted(HLA), help="HLA surface to compare with (default: by the family)")
    ap.add_argument("--shot", help="a capture of the material in a scene, added to the image")
    ap.add_argument("--out", help="comparison image (default: <material>/_debug/source2_compare.png)")
    args = ap.parse_args()

    base = glob.glob(os.path.join(args.material, "*_BaseColor.png"))
    if not base:
        sys.exit("no *_BaseColor.png in " + args.material)
    meta_path = os.path.join(args.material, "texgen.json")
    family = None
    if os.path.isfile(meta_path):
        with open(meta_path, encoding="utf-8") as f:
            family = json.load(f).get("suggested", {}).get("family")
    name = args.hla or next((k for k, v in HLA.items() if v[2] == family), "brick")
    crop, crop_family = hla_crop(name)
    family = family or crop_family
    with open(STYLE, encoding="utf-8") as f:
        style = json.load(f)
    cfg, common = style["families"].get(family, {}), style["common"]

    albedo = Image.open(base[0]).convert("RGB")
    print(line("HLA " + name, stats(crop)))
    print(line("generated", stats(albedo)))
    print("%-10s mean band %s, clip %s, max saturation %s" % (
        family, cfg.get("albedo_mean", common["dielectric_albedo_mean"]),
        cfg.get("albedo_clip", common["dielectric_albedo_clip"]), cfg.get("max_saturation", common["max_saturation"])))

    h = 512
    panels = [crop.resize((h, h), Image.LANCZOS), albedo.resize((h, h), Image.LANCZOS)]
    if args.shot:
        shot = Image.open(args.shot).convert("RGB")
        panels.append(shot.resize((round(shot.width * h / shot.height), h), Image.LANCZOS))
    sheet = Image.new("RGB", (sum(p.width for p in panels), h))
    x = 0
    for p in panels:
        sheet.paste(p, (x, 0))
        x += p.width
    out = args.out or os.path.join(args.material, "_debug", "source2_compare.png")
    sheet.save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
