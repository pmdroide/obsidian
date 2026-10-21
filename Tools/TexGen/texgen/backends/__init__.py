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
    if not model_dir.is_dir() or not any(model_dir.iterdir()):
        return False, "model_missing", "no model weights in " + str(model_dir)
    if name == "sdxl_chord" and not (TEXGEN_ROOT / "third_party" / "chord").is_dir():
        return False, "model_missing", "CHORD is not installed in " + str(TEXGEN_ROOT / "third_party" / "chord")
    import torch  # noqa: lazy, GPU backends only
    if not torch.cuda.is_available():
        return False, "cuda_unavailable", "torch cannot see a CUDA device"
    return False, "backend_not_implemented", "the %s backend arrives with phase E of the plan" % name
