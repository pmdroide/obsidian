"""Tileable text-to-image with a local diffusion model (Stable Diffusion XL through diffusers). GPU only, offline.

torch/diffusers are imported inside the functions so the rest of texgen runs without them. The weights are read from
Tools/TexGen/models/sdxl (a diffusers folder: model_index.json, unet/, vae/, ...) with local_files_only, never downloaded.
Tileable = every Conv2d of the UNet and the VAE pads circularly, so the image wraps around its borders.
"""
import numpy as np
from PIL import Image

from . import MODELS_DIR

MODEL_DIR = MODELS_DIR / "sdxl"
STEPS = 25
NATIVE_SIZE = 1024


def make_tileable(module):
    """Sets padding_mode='circular' on every Conv2d inside `module` (a UNet, a VAE or any nn.Module); returns the count."""
    import torch
    count = 0
    for m in module.modules():
        if isinstance(m, torch.nn.Conv2d):
            m.padding_mode = "circular"
            count += 1
    return count


def generation_size(size):
    """SDXL is trained at 1024: smaller requests are generated at 1024 and scaled down; larger ones are generated as asked."""
    return max(NATIVE_SIZE, size)


def load_pipeline(model_dir=MODEL_DIR):
    import torch
    from diffusers import DPMSolverMultistepScheduler, StableDiffusionXLPipeline
    # A folder holding only the half-precision files (diffusion_pytorch_model.fp16.safetensors) needs variant="fp16"
    half_only = (model_dir / "unet" / "diffusion_pytorch_model.fp16.safetensors").is_file() and         not (model_dir / "unet" / "diffusion_pytorch_model.safetensors").is_file()
    pipe = StableDiffusionXLPipeline.from_pretrained(
        str(model_dir), torch_dtype=torch.float16, local_files_only=True, use_safetensors=True,
        variant="fp16" if half_only else None)
    # DPM++ 2M
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(pipe.scheduler.config)
    make_tileable(pipe.unet)
    make_tileable(pipe.vae)
    return pipe.to("cuda")


def generate(prompt, negative_prompt, seed, size, on_step=None, model_dir=MODEL_DIR):
    """prompt/negative_prompt are the final texts (the style already added its suffixes). Returns (size, size, 3) uint8 sRGB.
    on_step(fraction) is called after every denoising step."""
    import torch
    pipe = load_pipeline(model_dir)
    side = generation_size(size)

    def step_done(_pipe, step, _timestep, kwargs):
        if on_step:
            on_step((step + 1) / STEPS)
        return kwargs

    generator = torch.Generator("cuda").manual_seed(int(seed) & 0xFFFFFFFF)
    image = pipe(prompt=prompt, negative_prompt=negative_prompt, num_inference_steps=STEPS, width=side, height=side,
                 generator=generator, callback_on_step_end=step_done).images[0]
    del pipe
    torch.cuda.empty_cache()
    if side != size:
        image = image.resize((size, size), Image.LANCZOS)
    return np.asarray(image.convert("RGB"), dtype=np.uint8)
