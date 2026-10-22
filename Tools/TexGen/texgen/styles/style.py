"""Style profiles (styles/<name>.json) and the family adjustments applied after the PBR estimate."""
import json
import re
from pathlib import Path

import numpy as np

from ..imageops import (gaussian_blur_wrap, lab_to_srgb, linear_to_srgb, luminance, normalize01, srgb_to_lab,
                        srgb_to_linear)

STYLES_DIR = Path(__file__).resolve().parent


class Style:
    def __init__(self, data):
        self.data = data
        self.name = data["name"]
        self.common = data["common"]
        self.families = data["families"]

    def family(self, name):
        return self.families[name]

    def albedo_mean(self, family):
        """Dielectric albedo mean luminance band (sRGB 0..255): the family's own, else the common one."""
        return self.families[family].get("albedo_mean", self.common["dielectric_albedo_mean"])

    def albedo_clip(self, family):
        return self.families[family].get("albedo_clip", self.common["dielectric_albedo_clip"])

    def max_saturation(self, family):
        return self.families[family].get("max_saturation", self.common["max_saturation"])

    def neutral_warmth(self, family):
        """Lab b* the near-grey parts of the albedo are warmed up to (0 = left alone)."""
        return self.families[family].get("neutral_warmth", self.common["neutral_warmth"])

    def macro_contrast(self, family):
        return self.families[family].get("macro_contrast", self.common["macro_contrast"])

    def detect_family(self, prompt):
        """Keyword -> family, by whole words, walking the families in file order (first hit wins). Falls back to
        the profile's fallback family."""
        tokens = re.findall(r"[a-z0-9]+", prompt.lower())
        words = set(tokens)
        text = " " + " ".join(tokens) + " "
        for name, cfg in self.families.items():
            for keyword in cfg["keywords"]:
                if " " in keyword:
                    if " " + keyword + " " in text:
                        return name
                elif keyword in words:
                    return name
        return self.common["fallback_family"]

    def resolve_family(self, requested, prompt):
        if requested in (None, "", "auto"):
            return self.detect_family(prompt)
        if requested not in self.families:
            raise KeyError(requested)
        return requested

    def prompt_for_diffusion(self, prompt, family):
        return prompt + self.families[family]["prompt_suffix"] + self.common["prompt_suffix"]

    def negative_for_diffusion(self, negative):
        return self.common["negative_prompt"] + (", " + negative if negative else "")


def available_styles():
    return sorted(p.stem for p in STYLES_DIR.glob("*.json"))


def load_style(name):
    path = STYLES_DIR / (name + ".json")
    if not path.is_file():
        raise KeyError(name)
    with open(path, "r", encoding="utf-8") as f:
        return Style(json.load(f))


def describe_styles():
    out = []
    for name in available_styles():
        style = load_style(name)
        out.append({"name": style.name, "description": style.data.get("description", ""),
                    "families": list(style.families.keys())})
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Adjustments
# ---------------------------------------------------------------------------------------------------------------------

def _mean_luma255(rgb, mask=None):
    lum = luminance(rgb) * 255.0
    return float(lum[mask].mean()) if mask is not None else float(lum.mean())


def fit_mean_luminance(rgb, lo, hi, mask=None, max_gain=8.0):
    """Scales sRGB colours (0..1) so the mean luminance (0..255) lands inside [lo, hi]. Black stays black."""
    if mask is not None and not mask.any():
        return rgb
    mean = _mean_luma255(rgb, mask)
    if mean <= 1e-6 or lo <= mean <= hi:
        return rgb
    target = lo + 5.0 if mean < lo else hi - 5.0
    gain = float(np.clip(target / mean, 1.0 / max_gain, max_gain))
    scaled = np.clip(rgb * gain, 0.0, 1.0)
    if mask is None:
        return scaled
    return np.where(mask[..., None], scaled, rgb)


def clamp_pixel_luminance(rgb, lo, hi, mask=None):
    """Brings pixels whose luminance (0..255) is outside [lo, hi] back to the edge by scaling them (black stays)."""
    lum = luminance(rgb) * 255.0
    gain = np.ones_like(lum)
    low = (lum > 1e-6) & (lum < lo)
    high = lum > hi
    gain[low] = lo / lum[low]
    gain[high] = hi / lum[high]
    if mask is not None:
        gain = np.where(mask, gain, 1.0)
    return np.clip(rgb * gain[..., None], 0.0, 1.0)


def compress_highlights(rgb, hi, knee=40.0, mask=None):
    """Rolls pixel luminance (0..255) above `hi - knee` off towards `hi` (tanh), scaling the colour so hue stays: bright
    patches (white plaster, glare an image generator painted in) keep their detail instead of clipping flat at `hi`."""
    lum = luminance(rgb) * 255.0
    start = hi - knee
    over = lum > start
    if mask is not None:
        over &= mask
    if not over.any():
        return rgb
    target = start + knee * np.tanh((lum[over] - start) / knee)
    gain = np.ones_like(lum)
    gain[over] = target / lum[over]
    return np.clip(rgb * gain[..., None], 0.0, 1.0)


def cap_saturation(rgb, max_sat):
    peak = rgb.max(axis=-1)
    low = rgb.min(axis=-1)
    sat = np.where(peak > 1e-6, (peak - low) / np.maximum(peak, 1e-6), 0.0)
    k = np.where(sat > max_sat, max_sat / np.maximum(sat, 1e-6), 1.0)
    # Pull the channels towards the brightest one: keeps hue and value, and HSV saturation becomes sat * k.
    peak = peak[..., None]
    return np.clip(peak - (peak - rgb) * k[..., None], 0.0, 1.0)


def warm_neutrals(rgb, target_b, chroma_limit=12.0, neutral_pct=40.0):
    """Real greys (plaster, mortar, concrete dust) are warm: Lab b* +5..+12 in Poly Haven's delit photos and the HLA
    crops, while image generators paint them dead neutral or cool. If the least chromatic `neutral_pct` % of the
    pixels average below `target_b`, near-grey pixels are moved towards it (fully at chroma 0, not at all from
    `chroma_limit` up), a quarter of the shift going to a* (dust brown rather than yellow). L* is kept, so the
    brightness fit afterwards is unaffected; an albedo that is already warm is left alone, and black stays black."""
    if target_b <= 0:
        return rgb
    lab = srgb_to_lab(rgb)
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    neutral = chroma <= np.percentile(chroma, neutral_pct)
    delta = target_b - float(lab[..., 2][neutral].mean())
    if delta <= 0:
        return rgb
    share = np.clip(1.0 - chroma / chroma_limit, 0.0, 1.0) * np.clip(lab[..., 0] / 20.0, 0.0, 1.0)
    # Pixels only partly grey move partly: scale up so the neutral pixels' mean reaches the target.
    w = share * delta / max(float(share[neutral].mean()), 0.25)
    lab[..., 1] += 0.25 * w
    lab[..., 2] += w
    return lab_to_srgb(lab)


def limit_macro_contrast(rgb, max_std, scale=1.0 / 64.0):
    """Soft-caps the large-scale contrast: the std of the log luminance blurred at size * `scale` (whole bricks, plaster
    patches). Delit photo albedos keep it at 0.04-0.23 and even lit HLA brick sits at 0.24, while a generator can paint
    near-white plaster beside bright orange brick (0.45), which reads clean and graphic instead of grimy. Fine detail is
    untouched; the colour is scaled, so hue stays."""
    if max_std <= 0:
        return rgb
    lin = srgb_to_linear(rgb)
    sigma = rgb.shape[0] * scale
    target = None
    # The correction is itself blurred when measured again, so a single pass undershoots: three converge.
    for _ in range(3):
        ll = np.log(np.maximum(luminance(lin), 1e-4))
        macro = gaussian_blur_wrap(ll, sigma)
        dev = macro - macro.mean()
        std = float(dev.std())
        if target is None:
            target = max_std * np.tanh(std / max_std)
        if std <= target + 1e-3:
            break
        lin = np.clip(lin * np.exp(dev * (target / std - 1.0))[..., None], 0.0, 1.0)
    return linear_to_srgb(lin)


def remap_roughness(r_norm, lo_hi, min_std=0.0):
    """Maps 0..1 onto the family's range. A map that varies a little is stretched so its deviation reaches
    min_std * 1.5 (a texture with no variation at all stays flat, and validation says so)."""
    lo, hi = lo_hi
    rough = lo + (hi - lo) * np.clip(r_norm, 0.0, 1.0)
    std = float(rough.std())
    if min_std > 0 and 1e-6 < std < min_std * 1.5:
        mean = float(rough.mean())
        rough = np.clip(mean + (rough - mean) * (min_std * 1.5 / std), lo, hi)
    return rough


def wear_mask(height, size, coverage=0.28):
    """Where the paint has chipped off: the convex spots and edges of the height field (a 0/1 mask)."""
    convexity = height - gaussian_blur_wrap(height, max(2.0, size / 64.0))
    convexity = gaussian_blur_wrap(convexity, 1.0)
    threshold = np.percentile(convexity, 100.0 * (1.0 - coverage))
    return convexity > threshold


def fit_dielectric(albedo, dlo, dhi, clo, chi, mask=None):
    """Mean luminance into [dlo, dhi], highlights rolled off below chi, shadows lifted to clo. The roll-off lowers the
    mean a little, so the mean is fitted once more if it dropped out of the band."""
    albedo = fit_mean_luminance(albedo, dlo, dhi, mask=mask)
    albedo = compress_highlights(albedo, chi - 1, mask=mask)
    albedo = fit_mean_luminance(albedo, dlo, dhi, mask=mask)
    return clamp_pixel_luminance(albedo, clo + 1, chi - 1, mask=mask)


def apply_style(style, family, est, force_dielectric=False):
    """Applies the family's rules to the estimator output. Returns the final albedo (sRGB, floats 0..1),
    roughness and metalness; normal, height and AO are left to the caller."""
    common, cfg = style.common, style.family(family)
    size = est["height"].shape[0]
    albedo = est["albedo"].astype(np.float64) / 255.0
    r_norm = est["roughness_norm"]
    mode = "none" if force_dielectric else cfg["metal"]
    dlo, dhi = style.albedo_mean(family)
    clo, chi = style.albedo_clip(family)
    max_sat = style.max_saturation(family)
    metal_color = np.array(common["metal_color"])
    min_std = common["roughness_min_std"]

    def grade(rgb):
        rgb = limit_macro_contrast(rgb, style.macro_contrast(family))
        rgb = warm_neutrals(rgb, style.neutral_warmth(family))
        return cap_saturation(rgb, max_sat)

    if mode == "none":
        albedo = grade(albedo)
        albedo = fit_dielectric(albedo, dlo, dhi, clo, chi)
        metal = np.zeros(albedo.shape[:2])
        rough = remap_roughness(r_norm, cfg["roughness"], min_std)
    elif mode == "all":
        mlo, mhi = common["metal_albedo_luminance"]
        albedo = fit_mean_luminance(albedo, mlo, mhi)
        albedo = clamp_pixel_luminance(albedo, mlo, mhi)
        metal = np.ones(albedo.shape[:2])
        rough = remap_roughness(r_norm, cfg["roughness"], min_std)
    else:  # "mask": painted metal
        exposed = wear_mask(est["height"], size)
        paint = ~exposed
        tone = 0.9 + 0.2 * normalize01(luminance(albedo))
        steel = np.clip(metal_color[None, None, :] * tone[..., None], 0.0, 1.0)
        mlo, mhi = common["metal_albedo_luminance"]
        steel = clamp_pixel_luminance(steel, mlo, mhi)
        albedo = grade(albedo)
        albedo = fit_dielectric(albedo, dlo, dhi, clo, chi, mask=paint)
        albedo = np.where(exposed[..., None], steel, albedo)
        metal = exposed.astype(np.float64)
        rough = np.where(exposed, remap_roughness(r_norm, cfg["metal_roughness"], min_std),
                         remap_roughness(r_norm, cfg["paint_roughness"], min_std))
    return albedo, rough, metal


def suggest(style, family):
    cfg = style.family(family)
    return {"tiling": round(1.0 / cfg["scale_m"], 3), "material_type": cfg["material_type"], "family": family}
