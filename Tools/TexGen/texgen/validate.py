"""Style validation: measures the value ranges of the source2 profile and returns one warning per broken rule."""
import numpy as np

from .imageops import luminance


def _w(code, message):
    return {"code": code, "message": message}


def validate(maps, style, family):
    """maps: albedo (uint8 sRGB), normal (Y+ unit vectors), roughness, metalness, ao (floats). Returns a list of
    {"code", "message"}; empty = fine."""
    common, cfg = style.common, style.family(family)
    out = []
    albedo = maps["albedo"].astype(np.float64) / 255.0
    lum = luminance(albedo) * 255.0
    metal = maps["metalness"] >= 0.5
    dielectric = ~metal

    if dielectric.any():
        d = lum[dielectric]
        lo, hi = common["dielectric_albedo_mean"]
        clip_lo, clip_hi = common["dielectric_albedo_clip"]
        frac = common["clip_fraction"]
        if d.mean() < lo:
            out.append(_w("albedo_too_dark", "dielectric albedo mean luminance %.0f is below %d" % (d.mean(), lo)))
        if d.mean() > hi:
            out.append(_w("albedo_too_bright", "dielectric albedo mean luminance %.0f is above %d" % (d.mean(), hi)))
        if (d < clip_lo).mean() > frac:
            out.append(_w("albedo_clipped_dark", "more than %.1f%% of the dielectric albedo is darker than %d"
                          % (frac * 100, clip_lo)))
        if (d > clip_hi).mean() > frac:
            out.append(_w("albedo_clipped_bright", "more than %.1f%% of the dielectric albedo is brighter than %d"
                          % (frac * 100, clip_hi)))
    if metal.any():
        m = lum[metal]
        lo, hi = common["metal_albedo_luminance"]
        if m.mean() < lo:
            out.append(_w("metal_albedo_dark", "metal albedo (its F0 colour) luminance %.0f is below %d" % (m.mean(), lo)))
        if m.mean() > hi:
            out.append(_w("metal_albedo_bright", "metal albedo luminance %.0f is above %d" % (m.mean(), hi)))

    rough = maps["roughness"]
    if float(rough.std()) < common["roughness_min_std"]:
        out.append(_w("roughness_flat", "roughness is almost constant (std %.3f < %.2f)"
                      % (rough.std(), common["roughness_min_std"])))
    lo, hi = cfg["roughness"]
    if rough.mean() < lo - 0.05 or rough.mean() > hi + 0.05:
        out.append(_w("roughness_out_of_range", "mean roughness %.2f is outside %.2f-%.2f for %s"
                      % (rough.mean(), lo, hi, family)))

    n = maps["normal"]
    if float(n[..., 2].min()) <= 0 or abs(float(np.linalg.norm(n, axis=-1).mean()) - 1.0) > 0.01:
        out.append(_w("normal_invalid", "normals must be unit length with z > 0"))
    if float(maps["ao"].min()) < common["ao_min"] - 1e-6:
        out.append(_w("ao_too_dark", "AO goes below %.2f" % common["ao_min"]))
    return out
