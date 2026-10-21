"""End-to-end generation: RGB source -> PBR estimate -> style -> post-processing -> validation -> atomic write."""
import json
import os
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image

from . import PIPELINE_VERSION, protocol, postprocess, seams
from .backends import BackendUnavailable, check_backend, classic_pbr, fake_rgb
from .imageops import luminance
from .styles import style as style_module
from .validate import validate

SLUG_MAX = 32


class TexGenError(Exception):
    def __init__(self, code, message, exit_code=1):
        super().__init__(message)
        self.code = code
        self.message = message
        self.exit_code = exit_code


def slugify(prompt):
    """prompt -> lowercase [a-z0-9_], at most 32 characters (the engine uses the same rule for file names)."""
    slug = re.sub(r"[^a-z0-9]+", "_", prompt.lower()).strip("_")[:SLUG_MAX].strip("_")
    return slug or "material"


def default_params(**overrides):
    params = {
        "prompt": "", "negative_prompt": "", "seed": 0, "resolution": 1024, "backend": "fake", "style": "source2",
        "family": "auto", "force_dielectric": False, "input_rgb": None, "normal_convention": "directx",
        "seam_check": True, "strict": False, "pipeline_version": PIPELINE_VERSION,
    }
    params.update(overrides)
    return params


def load_input_rgb(path, size):
    try:
        img = Image.open(path).convert("RGB")
    except (OSError, ValueError) as e:
        raise TexGenError("input_unreadable", "cannot read --input-rgb %s: %s" % (path, e), 2)
    w, h = img.size
    side = min(w, h)
    img = img.crop(((w - side) // 2, (h - side) // 2, (w - side) // 2 + side, (h - side) // 2 + side))
    return np.asarray(img.resize((size, size), Image.LANCZOS), dtype=np.uint8)


def make_rgb(params, family, style, size):
    backend = params["backend"]
    if backend == "fake":
        return fake_rgb.generate(params["prompt"], params["seed"], size, family)
    if backend == "classic":
        if not params["input_rgb"]:
            raise TexGenError("input_required", "the classic backend needs --input-rgb", 2)
        return load_input_rgb(params["input_rgb"], size)
    ok, code, message = check_backend(backend)
    raise BackendUnavailable(code or "backend_not_implemented", message)


def _file_names(slug):
    return {"basecolor": slug + "_BaseColor.png", "normal": slug + "_Normal.png", "roughness": slug + "_Roughness.png",
            "metallic": slug + "_Metallic.png", "ao": slug + "_AO.png", "height": slug + "_Height.png"}


def run(params, out_dir):
    """Runs the pipeline; returns the final output folder (a Path). Raises TexGenError / BackendUnavailable."""
    params = default_params(**params)
    out = Path(out_dir).resolve()
    size = int(params["resolution"])
    try:
        style = style_module.load_style(params["style"])
    except KeyError:
        raise TexGenError("unknown_style", "unknown style '%s'" % params["style"], 2)
    try:
        family = style.resolve_family(params["family"], params["prompt"])
    except KeyError:
        raise TexGenError("unknown_family", "unknown family '%s'" % params["family"], 2)
    cfg = style.family(family)

    protocol.progress("load", 0.0)
    protocol.progress("diffusion", 0.1)
    rgb = make_rgb(params, family, style, size)

    protocol.progress("estimation", 0.3)
    est = classic_pbr.estimate(rgb, cfg["normal_strength"])

    protocol.progress("style", 0.5)
    albedo, rough, metal = style_module.apply_style(style, family, est, params["force_dielectric"])

    protocol.progress("postprocess", 0.65)
    albedo_u8 = postprocess.remove_magenta(np.clip(np.rint(albedo * 255.0), 0, 255).astype(np.uint8))
    rough = postprocess.clamp_roughness(rough)
    metal = postprocess.binarize_metal(metal, force_dielectric=params["force_dielectric"])
    ao = postprocess.ao_from_height(est["height"], minimum=style.common["ao_min"])
    normal_u8 = postprocess.sanitize_normal(est["normal"], params["normal_convention"])

    protocol.progress("validate", 0.8)
    seam = max(seams.seam_score(luminance(albedo_u8.astype(np.float64) / 255.0)), seams.seam_score(est["height"]))
    if params["seam_check"] and not seams.passes(seam):
        raise TexGenError("seam_failed", "seam score %.2f is not below %.1f; the source is not tileable "
                          "(use --no-seam-check to keep it anyway)" % (seam, seams.PASS_BELOW), 4)
    maps = {"albedo": albedo_u8, "normal": postprocess.decode_normal(normal_u8, params["normal_convention"]),
            "roughness": rough, "metalness": metal, "ao": ao}
    warnings = validate(maps, style, family)
    for w in warnings:
        protocol.warning(w["code"], w["message"])
    if params["strict"] and warnings:
        raise TexGenError("validation_failed", "%d style rule(s) failed: %s"
                          % (len(warnings), ", ".join(w["code"] for w in warnings)), 5)

    protocol.progress("write", 0.9)
    slug = slugify(params["prompt"])
    names = _file_names(slug)
    tmp = out.with_name(out.name + ".tmp")
    if tmp.exists():
        shutil.rmtree(tmp)
    (tmp / "_debug").mkdir(parents=True)
    postprocess.save_rgb(tmp / names["basecolor"], albedo_u8)
    postprocess.save_rgb(tmp / names["normal"], normal_u8)
    postprocess.save_gray(tmp / names["roughness"], rough)
    if metal.any():
        postprocess.save_gray(tmp / names["metallic"], metal)
    postprocess.save_gray(tmp / names["ao"], ao)
    postprocess.save_gray(tmp / names["height"], est["height"])
    postprocess.save_rgb(tmp / "_debug" / "source_rgb.png", rgb)
    postprocess.save_gray16(tmp / "_debug" / "height16.png", est["height"])
    validation = {"passed": not warnings, "warnings": warnings}
    with open(tmp / "_debug" / "validation.json", "w", encoding="utf-8") as f:
        json.dump(validation, f, indent=2)
    meta = {
        "schema": 1,
        "id": out.name,
        "params": {
            "prompt": params["prompt"], "negative_prompt": params["negative_prompt"], "seed": params["seed"],
            "resolution": size, "backend": params["backend"], "style": params["style"], "family": params["family"],
            "force_dielectric": bool(params["force_dielectric"]),
            "input_rgb": os.path.basename(params["input_rgb"]) if params["input_rgb"] else None,
        },
        "pipeline_version": str(params["pipeline_version"]),
        "normal_convention": params["normal_convention"],
        "colorspace": {"basecolor": "srgb", "normal": "linear", "roughness": "linear", "metallic": "linear",
                       "ao": "linear", "height": "linear"},
        "seam_score": round(seam, 4),
        "validation": validation,
        "suggested": style_module.suggest(style, family),
        "created_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    with open(tmp / "texgen.json", "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    if out.exists():
        shutil.rmtree(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    os.replace(tmp, out)
    return out
