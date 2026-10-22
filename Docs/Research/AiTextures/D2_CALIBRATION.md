# D2 — Calibration of the `classic` backend against CC0 references

Run on 2026-10-21 by Claude. The proposed changes were approved and applied the same day (`PIPELINE_VERSION` and `MaterialGenerationId.PipelineVersion` are now `"2"`); see "Applied" at the end.

**References.** The three Poly Haven (CC0) sets already in `Engine/Content/GameObjects/Textures/Ground/` (Bricks, Cobblestone, RockyDirt; DirectX / Y- normals) stand in for `Tools/TexGen/reference/`. There is no metallic reference yet: that part of D2 still needs a person to supply one.

**Method.** For each set: `python -m texgen generate --backend classic --input-rgb <Name>_BaseColor.jpg --family <brick|stone|ground> --resolution 1024 --out cache/_calib/<Name> --no-seam-check` (from `Tools/TexGen`), then `lab/calibrate_references.py` compares every map with the reference's. Lab captures: `Engine.exe --scene Scenes/MaterialLab.obsc --material Materials/Reference/Poly_Cobblestone.mat` against `--import-generated Tools/TexGen/cache/_calib/Cobblestone` (real mgcb import, then removed again).

| | Bricks ref / gen | Cobblestone ref / gen | RockyDirt ref / gen |
|---|---|---|---|
| albedo luminance mean (linear) | 0.103 / 0.103 | 0.278 / 0.277 | 0.234 / 0.234 |
| roughness mean | 0.971 / 0.873 | 0.578 / 0.817 | 0.649 / 0.875 |
| roughness std | 0.056 / 0.075 | 0.114 / 0.069 | 0.070 / 0.045 |
| normal tilt mean \|xy\| | 0.164 / 0.497 | 0.281 / 0.590 | 0.367 / 0.763 |
| normal x / y corr | 0.13 / 0.14 | 0.39 / 0.46 | 0.37 / 0.38 |
| height corr (high-pass) | -0.03 | 0.70 | 0.44 |

**Findings**

1. **Albedo is untouched**: the delighting and the style leave an already-flat albedo as it was (same mean and spread).
2. **Normal convention**: the generated x and y slopes correlate *positively* with the Poly Haven maps on all three sets (a flipped green channel would give a negative y correlation), and in the lab the generated relief is lit from the same side as the reference's. So `texgen`'s DirectX / Y- matches the maps the engine already renders correctly. This supports task 0.2 but is not the human confirmation it asks for.
3. **Normals are about 2-3x too strong**: mean tilt 0.50-0.76 against 0.16-0.37, and the lab shows harsh, sculpted cracks. The bricks height map doesn't follow the reference (corr -0.03): luminance is a poor height for light mortar between dark bricks, as expected of the classic estimator.
4. **Roughness is too high and too flat for stone and dirt** (0.82-0.88 against 0.58-0.65; correlation with the reference is ~0 or negative). Bricks are close.
5. AO sits on its 0.35 floor in the darkest joints, as designed.

**Proposed changes to `styles/source2.json`** (bump `PIPELINE_VERSION` and `MaterialGenerationId.PipelineVersion` together if approved):

- `normal_strength`: stone/brick 2.5 -> ~1.0; tile/concrete/plaster 2.0 -> ~1.0; ground/fabric/organic 2.0 -> ~0.9 (tilt ratios measured 0.33-0.48 of the reference).
- `roughness`: stone [0.55, 0.95] -> [0.4, 0.9]; ground [0.7, 1.0] -> [0.5, 0.95].
- Re-run `lab/calibrate_references.py` after the change; three samples are thin, so ideally add the metallic reference and one wood/plaster set first.

**[HUMANO] checkpoint still open**: relief direction on a point light, plausible values and the Source 2 look (compare with HLA captures if you have them). Captures: `Tools/TexGen/cache/_shots/lab_ref_cobblestone.png`, `lab_gen_cobblestone.png`, `lab_bricks.png`.

## Applied (pipeline version 2)

`normal_strength` brick/stone 2.5 -> 1.0, tile/concrete/plaster 2.0 -> 1.0, fabric/ground/organic 2.0 -> 0.9; `roughness` stone [0.55, 0.95] -> [0.4, 0.9], ground [0.7, 1.0] -> [0.5, 0.95]. Same run again:

| | Bricks ref / gen | Cobblestone ref / gen | RockyDirt ref / gen |
|---|---|---|---|
| normal tilt mean \|xy\| | 0.164 / **0.292** (was 0.497) | 0.281 / **0.348** (was 0.590) | 0.367 / **0.515** (was 0.763) |
| roughness mean | 0.971 / 0.873 | 0.578 / **0.733** (was 0.817) | 0.649 / **0.763** (was 0.875) |

Normals are still a little strong and roughness a little high, but close; the roughness estimate itself (not the range) doesn't follow the references' structure (correlation ~0), which needs a better estimator rather than more range tuning. Old cache entries and ids are not reused (new version in the id).

## Metal reference (Metal063, ambientCG-style set in `References/`)

`classic` from `Metal063_1K-PNG_Color.png`, families `metal` and `painted_metal` (not applied, proposals only):

| | Reference | `metal` | `painted_metal` |
|---|---|---|---|
| albedo luminance mean (linear) / std | 0.155 / 0.020 | 0.291 / 0.028 | 0.282 / 0.212 |
| roughness mean / std | 0.134 / 0.047 | 0.390 / 0.045 | 0.546 / 0.164 |
| metalness mean | 0.964 | 1.000 | 0.280 |
| normal tilt mean \|xy\| | 0.006 (almost flat) | 0.542 | 0.680 |
| normal x / y corr | - | 0.35 / 0.35 | 0.36 / 0.36 |

The reference is dark, polished, worn steel. Findings:

1. **Flat surfaces get invented relief**: the reference has no normal detail, yet the generated normals tilt 0.54-0.68. The classic estimator normalises the height to the full 0-1 range whatever the contrast of the input, so a nearly flat albedo (luminance std 0.02) turns into strong bumps. Proposal: scale the height by the input's local contrast (e.g. cap the height range by `std(luminance) * k`) so flat inputs stay flat. This is an estimator change in `classic_pbr.py`.
2. **Albedo is brightened to the style's metal range** (140-250 sRGB, i.e. >= 0.26 linear): the reference's 110 sRGB steel is below it. That rule comes from PLAN 4.2 and a single dark sample doesn't justify lowering it; a dark `metal` variant (oxidised/blued steel, range ~90-160) is the option if such materials matter.
3. **Roughness floor**: `metal` bottoms out at 0.2 while polished steel here sits at 0.13. A `polished metal` family or a lower floor (0.08) would cover it.
4. Metalness is near 1 in both (0.96 vs 1.0), as intended; `painted_metal` is a different material type (mask).
5. The DirectX / Y- convention again agrees (positive x and y correlation).

## Source 2 look check: HLA plastered brick (2026-10-21)

**Reference.** The brick wall on the right of `Docs/Research/Source2Look/screenshots/Captura de ecrã 2026-10-17 202630.png` (red brick under flaking grey plaster, near-frontal soft light), cropped to 382 px square at (1384, 40). It is a lit, tonemapped frame, not albedo, so its values are an upper estimate of HLA's albedo.

**Runs** (pipeline version 3, `source2` style, family `brick`, 1024 px):
- `classic --input-rgb <crop>`: rejected by the seam check (score 10.05, limit 1.5: a screenshot crop doesn't tile), kept with `--no-seam-check`.
- `sdxl_chord --prompt "old red brick wall partly covered in flaking grey plaster, weathered, half-life alyx" --seed 7`: seam score 0.71, no warnings.

Lab: `Engine.exe --scene Scenes/MaterialLab.obsc --import-generated Tools/TexGen/cache/_hla_brick_<x> --shot ...`, plus a close-up of the cube (`--camera -0.75,-1.1,0.75,-0.9,0.5,0.45`) and Poly_Bricks from both views. Shots in `Tools/TexGen/cache/_shots/hla_brick/` (the imports were removed afterwards).

| | HLA crop | `classic` | `sdxl_chord` |
|---|---|---|---|
| albedo luminance (linear) mean ± std | 0.128 ± 0.044 | 0.127 ± 0.037 | **0.321 ± 0.141** (SDXL source 0.229) |
| albedo saturation (HSV) | 0.250 | 0.249 | 0.165 (white plaster pulls it down; the bricks are vivid orange) |
| roughness mean ± std | (matte) | 0.85 ± 0.07 | 0.83 ± 0.05 |
| normal tilt mean \|xy\| | - | 0.189 | 0.121 |

**Findings**

1. **`classic` keeps the HLA colour** (same luminance and saturation). Its relief reads right (plaster raised over the bricks), but the dark outlines that HLA's lighting puts under the plaster edges become deep, black grooves in the lab, and the plaster gets lumpy high-frequency bumps HLA doesn't have. The delighting can't remove those contact shadows. It also needs a tileable input, which a screenshot crop isn't (hard seams on the floor slabs).
2. **`sdxl_chord` doesn't look like Source 2**: it tiles and its relief is clean, but the albedo is ~2.5x brighter than the HLA wall, with vivid orange bricks and near-white plaster, so it looks stylised or freshly painted rather than grimy. "half-life alyx" in the prompt didn't help. The `brick` prompt suffix ("worn, realistic, subtle grime") isn't enough.
3. **The style can't catch it**: the only albedo rule is the dielectric mean 50-220 sRGB shared by every family, so 0.32 linear (~155 sRGB) passes. Proposal (not applied): per-family albedo targets in `styles/source2.json` (e.g. brick/stone/concrete mean ~70-120 sRGB, saturation cap ~0.35) and a remap in the style step towards them, like the metal luminance range. Measure a few more HLA surfaces (concrete in 205004, the tile floor and plaster wall in 211942) before choosing the numbers.
4. The lab's warm low sun makes every material (Poly_Bricks too) look orange; to compare with HLA's overcast/indirect shots a neutral, soft-light lab camera/lighting preset would help.
5. The lab cube's front face shows the texture turned 90° (horizontal courses run vertically), so judge orientation-dependent materials on the floor.


### Applied (pipeline version 4, same day)

More HLA surfaces measured first (`Tools/TexGen/lab/compare_source2_look.py` crops; sRGB luma mean / HSV saturation p95): plaster 46 / 0.38 (dim), wood panelling 81 / 0.52, concrete blocks 57 / 0.22, terracotta tiles 152 / 0.57 (lamp pool), chequered floor 78 / 0.14. Saturation turned out not to be what made CHORD read wrong (its p95 0.48 is the HLA brick's 0.50): brightness and the near-white top end were.

- **Per-family albedo** in `styles/source2.json` (`albedo_mean` band, `albedo_clip`, `max_saturation`; the `common` values stay the fallback, fabric uses them): brick [70, 125] / [25, 200] / 0.6, tile [50, 200] / [20, 235] / 0.65, concrete [85, 160] / [25, 210] / 0.3, plaster [90, 210] / [30, 235] / 0.45, wood [45, 140] / [15, 200] / 0.7, stone [60, 160] / [20, 215] / 0.45, ground [50, 130] / [15, 200] / 0.55, organic [40, 150] / [15, 210] / 0.7 (real-world albedo ranges, with the HLA crops as an upper bound). Highlights now roll off (tanh, 40 luma knee) towards the clip instead of being cut flat (`compress_highlights`), then the mean is fitted again. The validator uses the same per-family values.
- **Prompts**: masonry/tile suffixes say "aged, weathered, dirty, muted colours"; the negative prompt adds "stylized, painterly, oversaturated, vivid colours, clean, brand new".
- **classic**: an `--input-rgb` photo that fails the seam check is made tileable (`imageops.make_tileable`: the photo and copies shifted half its size in X, Y and both, faded over 30 % of each half from the border, variance-preserving; `texgen.json` `source_made_tileable`). The darkest 10 % of the luminance is rolled off before it becomes height and detail normals (`soften_shadows`), the detail layer's amplitude is soft-limited at 2 std, and normal slopes roll off above 0.85 (`limit_slope`, about 40 degrees).
- **Engine**: the parallax pass (`DrawTextureDisplacement`, `Gbuffer.fx`) built its view vector from SV_POSITION (pixels) instead of the world position, which smeared every material with a height map across the screen in curved streaks (most of the "mush" in the first wall captures, and the Poly Haven bricks too). Fixed; the offset is also bounded at grazing angles. `LookDevCapture --tiling <t>` overrides the targets' tiling, because generated materials carry their real-world scale (brick 0.667) and the lab's 1 m slabs then don't meet.
- **Scene**: `Scenes/Source2MaterialTest.obsc` (`lab/generate_source2_material_test.py`): a 3 x 2 m wall of slabs facing the camera, high neutral sun from behind the camera, dim fill without shadows (they streaked the wall), bright sky ambient, Poly Haven bricks, 18 % grey and white cards, chrome sphere.

Same HLA brick runs again (`Tools/TexGen/cache/_shots/hla_brick_v4/`, `compare_*.png`, `wall_*.png`):

| | HLA crop | `classic` v3 -> v4 | `sdxl_chord` v3 -> v4 |
|---|---|---|---|
| albedo luma mean (p95) | 98 (124) | 98 (121) -> 98 (119) | 148 (189) -> **120 (158)** |
| saturation mean (p95) | 0.25 (0.50) | 0.25 (0.50) -> 0.24 (0.44) | 0.17 (0.48) -> 0.14 (0.42) |
| normal tilt mean (p99) | - | 0.19 (0.78) -> 0.17 (**0.54**) | 0.12 (0.73) -> 0.10 (0.63) |
| seam score | - | 10.1 (refused) -> **0.72** | 0.71 -> 0.48 |

Both now read as worn brick under grey plaster in the wall scene; the CHORD plaster is grey instead of white. Left: `classic` keeps soft, slightly blurry detail from a 382 px crop and repeats visibly at 1 m; its thin black outlines are fainter but some remain in the lab close-up (they are in the screenshot's albedo). CHORD's mean sits at the top of the brick band (fitted down to 120, HLA is ~98), so the band could narrow once more HLA brick is measured. The [HUMANO] check of the look is still open.

## Pipeline version 5: warm greys and large-scale contrast (2026-10-22)

The v4 CHORD wall still didn't read as HLA: next to the HLA crop its plaster was a clean, cold grey and stood far brighter than the bricks. Measured on 512 px versions (scratch script; Lab D65; "neutral" = the least chromatic 40 % of the pixels; macro = std of the log luminance blurred at size/64):

| | neutral b* | macro contrast |
|---|---|---|
| HLA brick / plaster / wood (lit crops) | 5.8 / 7.7 / 11.8 | 0.24 / 0.26 / 0.54 |
| HLA concrete / checker | -0.5 / 0.2 | 0.74 / 0.42 (lighting, black-white tiles) |
| Poly Haven Bricks / Cobblestone / RockyDirt (delit) | 9.6 / 4.7 / 10.9 | 0.09 / 0.23 / 0.04 |
| `classic` from the HLA crop (v4) | 5.7 | 0.13 |
| `sdxl_chord` HLA brick prompt, seed 7 (v4) | **-0.5** | **0.45** |

Real greys are warm (dust, iron oxides); the image generator paints them dead neutral or slightly blue, and the style step had nothing that touched hue. The plaster-over-brick contrast is nearly twice that of the lit HLA frame, and higher than any delit photo albedo.

**Applied** (`styles/source2.json`, `style.py`; `PIPELINE_VERSION` and `MaterialGenerationId.PipelineVersion` = "5"):

- `warm_neutrals`: if the neutral pixels' mean b* is below the family's `neutral_warmth`, near-grey pixels (chroma < 12, fading in above L* 20 so black stays black) move towards it, a quarter of the shift into a* (dust brown). L* is kept; already-warm albedos are left alone. Targets just below the measurements: brick 6, plaster 6, stone 5, ground 9, wood 10, concrete 2, tile 3; fabric, organic and painted metal 0 (off).
- `limit_macro_contrast`: soft cap (tanh) on the macro contrast, three passes so it converges; `common` 0.25, tile 0.45 and wood 0.4 (their patterns are real). Fine detail is untouched.
- Brick `albedo_mean` [70, 125] -> [65, 110]: Poly Haven Bricks luma 88, lit HLA brick 98 (an upper bound).
- Tried and reverted: treating CHORD's roughness as absolute (fitted into the range instead of remapped). Its spread didn't grow (0.045 either way, CHORD's own is small) and the brick got glossier (0.85 -> 0.75; Poly Haven Bricks 0.97).

Same CHORD run (`Tools/TexGen/cache/_shots/hla_brick_v5/compare_chord.png`): neutral b* -0.5 -> 4.8, macro 0.45 -> 0.23, albedo luma 120 -> 105 (HLA 98), no validation warnings. `classic` from the HLA crop is unchanged (already warm and below the cap). In `Source2MaterialTest` the plaster reads warm grey-beige and sits closer to the bricks. Still different: HLA's plaster carries dense mottled grime and its bricks are a muddier red; that is what the generator painted, which a grade can't invent.
