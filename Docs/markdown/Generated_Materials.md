# Generated Materials (AI textures)

Obsidian can generate a tileable PBR material from a text prompt (or from a photo) and import it as a normal material asset (`.mat`). Nothing leaves your machine: the generator is a local Python tool (`Tools/TexGen`), and the result is the same kind of asset as one made with **Create Material**. Drag it onto gameobjects in the Hierarchy, share it between them, edit it in the Inspector, and it ships with builds.

The look targets Half-Life: Alyx / Source 2: delit albedo with no baked light, believable PBR value ranges, worn painted metal, a two-scale normal map and a soft ambient occlusion map that only dims indirect light.

## Set up

The generator needs Python and two packages (numpy and Pillow). From the repository root:

```powershell
py -3 -m venv Tools\TexGen\.venv
Tools\TexGen\.venv\Scripts\pip install -r Tools\TexGen\requirements-core.txt
```

Anvil looks for `Tools/TexGen/.venv/Scripts/python.exe`. To use another Python, a different cache folder or backend, copy `Tools/TexGen/texgen.example.json` to `Tools/TexGen/texgen.json` (it is git-ignored) and edit it: `pythonExe`, `texgenRoot`, `cacheRoot`, `defaultBackend`, `timeoutSeconds`. Relative paths are relative to the repository root.

The dialog tells you when the generator can't run and why.

## Generate a material

- **Assets panel:** right-click a folder > **Generate Material...**
- **GameObject > Generate Material for Selection...:** the same dialog; each imported material is also applied to the selected gameobject.

| Field | Meaning |
|---|---|
| Prompt | What the surface is: `mossy cobblestone`, `rusty steel plate`, `red brick wall`. |
| Avoid | Optional negative prompt (used by the diffusion backends). |
| Seed | The same prompt, seed, size, backend and family always give the same material. **Random** picks a new one. |
| Size | 256, 512, 1024 or 2048 pixels, square. |
| Backend | Only the backends that work on this machine are listed. `fake` is a procedural stand-in (no GPU); `sdxl_classic` (Stable Diffusion XL, see below) appears once it is installed. |
| Family | `auto` reads the prompt (a keyword picks the family); or choose stone, brick, concrete, tile, plaster, wood, metal, painted_metal, fabric, ground or organic. Each family has its own albedo brightness band and saturation cap, roughness range, normal strength and real-world tile size. |
| Force dielectric | No metal at all, even for a metal prompt. |
| Use photo... | Turn a photo or an existing texture into a PBR set (the classic estimator). A photo that doesn't tile is made tileable first (its middle is kept, the borders are blended with shifted copies of itself); a photo with strong features near its edges shows ghosting in that band, so a crop that already tiles is best. |

Press **Generate**: a progress bar shows the stage (diffusion, estimation, style, postprocess, validate, write), **Cancel** stops it, and the maps appear as thumbnails (Base color, Normal, Roughness, AO, Height, and Metallic when there is metal) straight from the cache folder. Style warnings are listed under the progress bar; they mean a value is outside the Source 2 ranges (for example `albedo_too_dark`) and the result is still usable.

**Regenerate** rolls a new seed and generates again. **Import** adds the shown material to the project; the dialog stays open, so one prompt can give several independent materials (each seed is its own `.mat`). The new material appears in Assets and opens in the Inspector.

Importing builds the textures with the content pipeline (mgcb), which freezes the editor for the length of the content build (see `ISSUES.md`).

## What you get

```
Engine/Content/Materials/Generated/<slug>_<id>.mat            the material asset
Engine/Content/Materials/Generated/<slug>_<id>/               its maps (built into Content.mgcb)
    <slug>_BaseColor.png  <slug>_Normal.png  <slug>_Roughness.png
    <slug>_Metallic.png (only when there is metal)  <slug>_AO.png  <slug>_Height.png
    texgen.json                                               what it was made from
Tools/TexGen/cache/<id>/                                      raw generator output (git-ignored)
```

`<slug>` is the prompt as lowercase `a-z0-9_` (32 characters at most) and `<id>` the first 16 hex characters of a SHA-256 of the request. The id is in every path on purpose: the ContentManager caches textures by name, so a new generation is a new asset and never shows a stale texture. Asking for the same request again reuses the cache and the same material.

The `.mat` uses the six texture slots: base color, normal, roughness, metallic, displacement (the height map) and ambient occlusion. The maps are separate grayscale files read from the red channel (there is no packed ARM map). A set without metal leaves the Metallic slot at "none" and Metallic at 0. Tiling is set from the family's real-world tile size for a 1 m plane; change it in the Inspector.

Conventions: the albedo is sRGB with no baked light, every other map is linear, the normal map is DirectX / Y- (green points down, like the Poly Haven textures already in `GameObjects/Textures/Ground`), and a pure magenta pixel never appears in an albedo.

## From an assistant (MCP)

With Anvil's MCP server running and **Read-only** off, the `materials_generate` tool does the same job. It returns at once with a pending operation; poll `editor_get_operation` with the same `requestId` until it completes and returns `material.materialPath`. See [Editor_MCP.md](Editor_MCP.md).

## From the command line

```powershell
Tools\TexGen\.venv\Scripts\python -m texgen generate --prompt "red brick wall" --seed 7 --out Tools\TexGen\cache\test --resolution 512
Tools\TexGen\.venv\Scripts\python -m texgen check --backend fake
Tools\TexGen\.venv\Scripts\python -m texgen styles
```

stdout is JSON lines only (`progress`, `warning`, `done`, `error`). Exit codes: 0 ok, 1 internal error, 2 bad arguments, 3 model or dependency missing, 4 seam check failed (a generated image that doesn't tile; `--input-rgb` photos are made tileable instead, unless `--no-seam-check` keeps them as they are), 5 style validation failed (`--strict`). Tests: `Tools\TexGen\.venv\Scripts\python -m unittest discover Tools/TexGen/tests` and `Tests/Components/TexGenChecks*.cs`.

## Where to tune

| Change | Where |
|---|---|
| Value ranges, families, keywords, roughness, tile sizes | `Tools/TexGen/texgen/styles/source2.json` and `style.py` (`albedo_mean` / `albedo_clip` / `max_saturation` / `neutral_warmth` / `macro_contrast` per family override the `common` ones; highlights roll off towards the clip in `compress_highlights`; near-grey pixels are warmed to `neutral_warmth` (Lab b*) in `warm_neutrals`; large-scale contrast is soft-capped at `macro_contrast` in `limit_macro_contrast`) |
| Making photos tileable | `make_tileable` in `Tools/TexGen/texgen/imageops.py` |
| Delighting, height, normals, roughness estimate | `Tools/TexGen/texgen/backends/classic_pbr.py` (`soften_shadows` keeps contact shadows shallow, `MAX_SLOPE` caps the normals) |
| AO, metal, PNG writing | `Tools/TexGen/texgen/postprocess.py` |
| Warnings | `Tools/TexGen/texgen/validate.py` |
| Id, runner, import | `Engine/Recources/TextureGeneration/` |
| The dialog | `Editor/Anvil/ViewModels/GenerateMaterialViewModel.cs`, `Views/GenerateMaterialWindow.axaml` |

When a change alters results, bump **both** `PIPELINE_VERSION` in `Tools/TexGen/texgen/__init__.py` and `MaterialGenerationId.PipelineVersion`, so old cache entries aren't reused.

## Checking a material in the MaterialLab

`Engine.exe --scene Scenes/MaterialLab.obsc --material Materials/Generated/<x>.mat --shot lab.png` (or `--import-generated Tools/TexGen/cache/<id>` to import and view a fresh set) shows it on a tiled floor, a cube and a sphere next to a grey and a chrome reference. Regenerate the scene with `python Tools/TexGen/lab/generate_material_lab.py`. Calibration against CC0 references: `Docs/Research/AiTextures/D2_CALIBRATION.md`.

## Checking the Source 2 look (`Source2MaterialTest`)

MaterialLab's warm low sun tints every material orange. To judge a material against Half-Life: Alyx, use the wall scene, framed and lit like the HLA screenshots (a 3 x 2 m wall of 1 m slabs, a high neutral sun from behind the camera, a dim fill, a bright sky ambient; the Poly Haven bricks, an 18 % grey card, a white card and a chrome sphere beside it):

```powershell
Engine\bin\Debug\net10.0-windows\Engine.exe --scene Scenes/Source2MaterialTest.obsc --import-generated Tools/TexGen/cache/<id> --tiling 1 --shot wall.png
Tools\TexGen\.venv\Scripts\python Tools/TexGen/lab/compare_source2_look.py Tools/TexGen/cache/<id> --hla brick --shot wall.png
```

`--tiling 1` makes each slab repeat the texture exactly once, so a seam between slabs means the texture doesn't tile (without it the material's real-world tiling, e.g. 0.667 for brick, makes the slabs disagree). The compare script prints the albedo's luminance and saturation next to an HLA surface's (brick, plaster, wood, concrete, tile, checker; cut from `Docs/Research/Source2Look/screenshots` at run time) and the family band, and writes `HLA crop | albedo | capture` to `<cache>/_debug/source2_compare.png`. Regenerate the scene with `python Tools/TexGen/lab/generate_source2_material_test.py`.

## Stable Diffusion backend (`sdxl_classic`)

Real text-to-texture, local and offline (the code never downloads anything). Needs an NVIDIA GPU:

1. Install the GPU packages in the venv, with the torch build that matches your CUDA driver: `Tools\TexGen\.venv\Scripts\python -m pip install -r Tools/TexGen/requirements-gpu.txt` (see https://pytorch.org for the right torch wheel).
2. Put an SDXL model in diffusers format (`model_index.json`, `unet/`, `vae/`, ...) in `Tools/TexGen/models/sdxl/` (git-ignored). The license of the weights is yours to check.
3. `Tools\TexGen\.venv\Scripts\python -m texgen check --backend sdxl_classic` must report `done`; the Generate Material dialog then lists the backend.

The image is generated at 1024 px (25 steps, DPM++ 2M, float16, seeded) with every convolution of the UNet and the VAE padding circularly so it wraps, then goes through the same estimator and `source2` style as the other backends. ## CHORD backend (`sdxl_chord`)

Same SDXL image, but the PBR maps come from CHORD (Ubisoft La Forge) instead of the classic estimator: better roughness, softer and more natural normals. **Its license is research-only (Ubisoft Machine Learning License, copyleft): check it before shipping anything made with it.** Needs, besides the SDXL setup:

1. `git clone https://github.com/ubisoft/ubisoft-laforge-chord Tools/TexGen/third_party/chord`
2. Ask for access at https://huggingface.co/Ubisoft/ubisoft-laforge-chord, `hf auth login`, then `hf download Ubisoft/ubisoft-laforge-chord --local-dir Tools/TexGen/third_party/chord/weights`
3. `pip install torchvision omegaconf imageio` in the venv (torchvision from the same CUDA index as torch).
4. The Stable Diffusion 2.1 configs and tokenizer (a few KB, no weights), so nothing is fetched at run time: `hf download sd2-community/stable-diffusion-2-1-base --local-dir Tools/TexGen/third_party/chord/sd21 --include unet/config.json --include vae/config.json --include text_encoder/config.json --include "tokenizer/*" --include "scheduler/*" --include model_index.json`

`texgen check --backend sdxl_chord` must say ready. It takes about 45 s per material on an RTX 3060 (model load plus one 1280 px pass). The open weights don't predict a height map; it is derived from the albedo.

## Limits

- The `fake` backend is a placeholder look. Real text-to-texture needs the `sdxl_classic` backend (GPU, local model weights); it is written and tested without a GPU, and has been run on SDXL base 1.0 (fp16) on an RTX 3060; it tends to draw many small elements per tile (see the scale note in `ISSUES.md`).
- CHORD (an optional, better RGB-to-PBR model) is research-only licensed: don't use it in a commercial game without checking its license.
- mgcb builds mip maps with a box filter on the stored values, without linearizing the albedo or renormalizing normals, so distant surfaces darken and flatten slightly.
- The height map is 8-bit in Content (the 16-bit version stays in `Tools/TexGen/cache/<id>/_debug`).
- The normal map convention was not yet confirmed against the engine (task 0.2 of the plan).
- Generated materials live in the project's Content, so **Delete Project** removes them.
