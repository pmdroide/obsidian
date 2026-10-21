"""Backends: where the colour image comes from. The core (fake, classic) needs only numpy + Pillow; the GPU backends
import torch/diffusers lazily and are reported as unavailable until their dependencies, models and CUDA exist."""
import importlib.util
from pathlib import Path

TEXGEN_ROOT = Path(__file__).resolve().parent.parent.parent
MODELS_DIR = TEXGEN_ROOT / "models"

BACKENDS = ("fake", "classic", "sdxl_classic", "sdxl_chord")
GPU_PACKAGES = ("torch", "diffusers", "transformers", "accelerate", "safetensors")


class BackendUnavailable(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


def check_backend(name):
    """Returns (ok, code, message). code is one of dependency_missing / model_missing / cuda_unavailable /
    backend_not_implemented when not ok."""
    if name in ("fake", "classic"):
        return True, "", "%s needs no GPU and no models" % name
    if name not in BACKENDS:
        return False, "unknown_backend", "unknown backend '%s'" % name
    missing = [p for p in GPU_PACKAGES if importlib.util.find_spec(p) is None]
    if missing:
        return False, "dependency_missing", "missing Python packages: " + ", ".join(missing)
    model_dir = MODELS_DIR / "sdxl"
    if not (model_dir / "model_index.json").is_file():
        return False, "model_missing", "no diffusers model (model_index.json) in " + str(model_dir)
    import torch  # noqa: lazy, GPU backends only
    if not torch.cuda.is_available():
        return False, "cuda_unavailable", "torch cannot see a CUDA device"
    if name == "sdxl_chord":
        from . import chord_pbr
        extra = [p for p in ("omegaconf", "torchvision", "imageio") if importlib.util.find_spec(p) is None]
        if extra:
            return False, "dependency_missing", "CHORD needs Python packages: " + ", ".join(extra)
        absent = chord_pbr.missing_files()
        if absent:
            return False, "model_missing", "CHORD files missing: " + ", ".join(absent)
    return True, "", "%s is ready (%s)" % (name, torch.cuda.get_device_name(0))
