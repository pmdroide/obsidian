"""RGB -> PBR with CHORD (Ubisoft La Forge, "Chain of Rendering Decomposition"), a single-step diffusion model.

GPU only, offline. The code and weights are the user's clone in Tools/TexGen/third_party/chord (license: Ubisoft
Machine Learning License, Research-Only - Copyleft: not for a commercial game without checking it). Nothing is
downloaded: the UNet/VAE/CLIP configs and the tokenizer of Stable Diffusion 2.1 come from a local folder
(third_party/chord/sd21, `hf_key` in CHORD's own config), the weights from third_party/chord/weights/chord_v1.safetensors.

The open weights predict base colour, normal, roughness and metalness (no height: it comes from the base colour like
the classic estimator). The image is wrapped around its borders before the model sees it and the centre is cropped
afterwards, so the maps tile. torch and CHORD are imported inside the functions.
"""
import sys

import numpy as np
from PIL import Image

from . import TEXGEN_ROOT
from .classic_pbr import height_from_rgb

CHORD_ROOT = TEXGEN_ROOT / "third_party" / "chord"
WEIGHTS = CHORD_ROOT / "weights" / "chord_v1.safetensors"
SD_CONFIG_DIR = CHORD_ROOT / "sd21"
MODEL_SIZE = 1024
WRAP = 128  # pixels of the image repeated on every side (1024 -> 1280), cropped away again

# How CHORD writes the green channel of its normal maps, measured against Poly Haven's DirectX maps by
# comparing its output with Poly Haven's maps on three sets (green correlates +0.35..+0.61): "directx" (Y-) or "opengl" (Y+).
NORMAL_CONVENTION = "directx"


def missing_files():
    """Paths the backend needs that don't exist (empty when it can run)."""
    needed = [CHORD_ROOT / "chord" / "__init__.py", CHORD_ROOT / "config" / "chord.yaml", WEIGHTS,
              SD_CONFIG_DIR / "unet" / "config.json", SD_CONFIG_DIR / "vae" / "config.json",
              SD_CONFIG_DIR / "text_encoder" / "config.json", SD_CONFIG_DIR / "tokenizer" / "vocab.json",
              SD_CONFIG_DIR / "scheduler" / "scheduler_config.json"]
    return [str(p) for p in needed if not p.is_file()]


def load_model():
    import torch
    from omegaconf import OmegaConf
    if str(CHORD_ROOT) not in sys.path:
        sys.path.insert(0, str(CHORD_ROOT))
    from chord import ChordModel
    from chord.io import load_torch_file
    config = OmegaConf.load(CHORD_ROOT / "config" / "chord.yaml")
    config.model.stable_diffusion.hf_key = str(SD_CONFIG_DIR)  # local configs only, no Hub access
    model = ChordModel(config)
    state = load_torch_file(str(WEIGHTS))
    expected = set(model.state_dict())
    if not any(k.startswith("model.sd.text_encoder.text_model.") for k in expected):
        # The checkpoint uses transformers 4.x names; newer versions drop the "text_model." level of CLIPTextModel.
        state = {k.replace("model.sd.text_encoder.text_model.", "model.sd.text_encoder.", 1): v for k, v in state.items()}
    result = model.load_state_dict(state, strict=False)
    # position_ids is a buffer newer transformers no longer keep; anything else missing is a real problem
    problems = [k for k in list(result.missing_keys) + list(result.unexpected_keys) if not k.endswith("position_ids")]
    if problems:
        raise RuntimeError("CHORD weights don't match the model (%d keys, e.g. %s)" % (len(problems), ", ".join(problems[:3])))
    model.eval()
    return model.to(torch.device("cuda"))


def predict(rgb_u8, model=None):
    """rgb_u8 (S, S, 3) sRGB uint8 -> dict of float32 numpy maps at S x S: basecolor (S,S,3) 0..1 sRGB,
    normal (S,S,3) 0..1 as CHORD wrote it, roughness (S,S), metalness (S,S)."""
    import torch
    own = model is None
    model = model or load_model()
    size = rgb_u8.shape[0]
    img = Image.fromarray(rgb_u8).resize((MODEL_SIZE, MODEL_SIZE), Image.LANCZOS) if size != MODEL_SIZE else Image.fromarray(rgb_u8)
    wrapped = np.pad(np.asarray(img), ((WRAP, WRAP), (WRAP, WRAP), (0, 0)), mode="wrap")
    x = torch.from_numpy(wrapped.astype(np.float32) / 255.0).permute(2, 0, 1)[None].to("cuda")
    with torch.no_grad(), torch.autocast(device_type="cuda"):
        out = model(x)

    def crop(t):
        a = t.float().cpu().numpy()[0]
        a = a[..., WRAP:-WRAP, WRAP:-WRAP]
        a = np.moveaxis(a, 0, -1) if a.ndim == 3 else a
        if size != MODEL_SIZE:
            mode = Image.LANCZOS
            if a.ndim == 3:
                a = np.stack([np.asarray(Image.fromarray(a[..., c]).resize((size, size), mode)) for c in range(a.shape[2])], axis=-1)
            else:
                a = np.asarray(Image.fromarray(a).resize((size, size), mode))
        return np.clip(a, 0.0, 1.0).astype(np.float32)

    result = {k: crop(out[k]) for k in ("basecolor", "normal", "roughness", "metalness")}
    if own:
        del model
        torch.cuda.empty_cache()
    return result


def estimate(rgb_u8, normal_strength=None, model=None):
    """Same dict as classic_pbr.estimate: albedo (uint8 sRGB), height (0..1), normal (Y+ unit vectors),
    roughness_norm (0..1) and metalness. `normal_strength` is unused (CHORD predicts the normals)."""
    maps = predict(rgb_u8, model)
    albedo = np.rint(maps["basecolor"] * 255.0).astype(np.uint8)
    n = maps["normal"].astype(np.float64) * 2.0 - 1.0
    if NORMAL_CONVENTION == "directx":
        n[..., 1] = -n[..., 1]
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
    n[..., 2] = np.abs(n[..., 2])
    return {
        "albedo": albedo,
        "height": height_from_rgb(albedo),
        "normal": n,
        "roughness_norm": maps["roughness"].astype(np.float64),
        "metalness": maps["metalness"].astype(np.float64),
    }
