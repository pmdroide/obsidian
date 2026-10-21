import unittest

import numpy as np

import _bootstrap  # noqa: F401
from texgen import pipeline, postprocess
from texgen.backends import classic_pbr, fake_rgb
from texgen.styles import style as st
from texgen.validate import validate

STYLE = st.load_style("source2")


def finish(rgb, family, force_dielectric=False):
    """The pipeline's stages between the estimate and the files, returned as the maps validate() reads."""
    est = classic_pbr.estimate(rgb, STYLE.family(family)["normal_strength"])
    albedo, rough, metal = st.apply_style(STYLE, family, est, force_dielectric)
    albedo = postprocess.remove_magenta(np.clip(np.rint(albedo * 255), 0, 255).astype(np.uint8))
    return {"albedo": albedo, "normal": est["normal"], "roughness": postprocess.clamp_roughness(rough),
            "metalness": postprocess.binarize_metal(metal, force_dielectric=force_dielectric),
            "ao": postprocess.ao_from_height(est["height"])}, est


class FamilyTests(unittest.TestCase):
    def test_auto_detection(self):
        pick = STYLE.detect_family
        self.assertIn(pick("rusty steel plate"), ("metal", "painted_metal"))
        self.assertEqual(pick("mossy cobblestone"), "stone")
        self.assertEqual(pick("red brick wall"), "brick")
        self.assertEqual(pick("old oak planks"), "wood")
        self.assertEqual(pick("polished steel"), "metal")
        self.assertEqual(pick("chipped paint over metal"), "painted_metal")
        self.assertEqual(pick("painted wall"), "plaster")
        self.assertEqual(pick("grey ceramic tile"), "tile")
        self.assertEqual(pick("wet sand"), "ground")
        self.assertEqual(pick("something unknown"), "stone")
        self.assertEqual(pick("STONE"), "stone")

    def test_requested_family_wins_and_unknown_raises(self):
        self.assertEqual(STYLE.resolve_family("wood", "steel"), "wood")
        self.assertEqual(STYLE.resolve_family("auto", "steel"), "metal")
        with self.assertRaises(KeyError):
            STYLE.resolve_family("lava", "x")

    def test_prompt_suffixes(self):
        text = STYLE.prompt_for_diffusion("old wall", "brick")
        self.assertTrue(text.startswith("old wall, photographed surface"))
        self.assertTrue(text.endswith("no perspective"))
        self.assertIn("perspective", STYLE.negative_for_diffusion(""))
        self.assertTrue(STYLE.negative_for_diffusion("blurry").endswith(", blurry"))

    def test_suggested_scale(self):
        s = st.suggest(STYLE, "brick")
        self.assertEqual((s["material_type"], s["family"]), ("Basic", "brick"))
        self.assertAlmostEqual(s["tiling"], 1 / 1.5, places=3)

    def test_slug(self):
        self.assertEqual(pipeline.slugify("Mossy  Cobblestone!"), "mossy_cobblestone")
        self.assertEqual(pipeline.slugify("!!!"), "material")
        self.assertLessEqual(len(pipeline.slugify("a" * 80)), 32)


class StyleRuleTests(unittest.TestCase):
    def test_painted_metal_mixes_metal_and_paint_with_different_roughness(self):
        rgb = fake_rgb.generate("chipped paint", 5, 128, "painted_metal")
        maps, _ = finish(rgb, "painted_metal")
        m = maps["metalness"] >= 0.5
        self.assertTrue(m.any() and (~m).any(), "both 0 and 1 present")
        self.assertEqual(set(np.unique(maps["metalness"])), {0.0, 1.0})
        self.assertLess(float(maps["roughness"][m].mean()), float(maps["roughness"][~m].mean()))
        self.assertLessEqual(float(maps["roughness"][m].max()), 0.4 + 1e-9)
        self.assertGreaterEqual(float(maps["roughness"][~m].min()), 0.5 - 1e-9)

    def test_bare_metal_is_all_metal_with_a_bright_f0_albedo(self):
        maps, _ = finish(fake_rgb.generate("steel", 2, 128, "metal"), "metal")
        self.assertTrue((maps["metalness"] == 1.0).all())
        self.assertEqual(validate(maps, STYLE, "metal"), [])

    def test_force_dielectric_removes_metal(self):
        maps, _ = finish(fake_rgb.generate("steel", 2, 128, "metal"), "metal", force_dielectric=True)
        self.assertEqual(float(maps["metalness"].max()), 0.0)

    def test_every_family_of_the_fake_backend_validates_clean(self):
        for family in STYLE.families:
            maps, _ = finish(fake_rgb.generate(family, 9, 128, family), family)
            self.assertEqual([w["code"] for w in validate(maps, STYLE, family)], [], family)

    def test_roughness_never_constant_and_inside_the_family_range(self):
        maps, _ = finish(fake_rgb.generate("wall", 4, 128, "brick"), "brick")
        self.assertGreaterEqual(float(maps["roughness"].std()), 0.03)
        self.assertGreaterEqual(float(maps["roughness"].min()), 0.55 - 1e-9)
        self.assertLessEqual(float(maps["roughness"].max()), 0.95 + 1e-9)

    def test_saturation_is_capped(self):
        red = np.dstack([np.full((4, 4), 1.0), np.zeros((4, 4)), np.zeros((4, 4))])
        out = st.cap_saturation(red, 0.75)
        peak, low = out.max(axis=-1), out.min(axis=-1)
        self.assertLessEqual(float(((peak - low) / peak).max()), 0.75 + 1e-6)


class ValidateTests(unittest.TestCase):
    def codes(self, rgb, family="stone"):
        maps, _ = finish(rgb, family)
        return [w["code"] for w in validate(maps, STYLE, family)]

    def test_black_image_is_too_dark(self):
        self.assertIn("albedo_too_dark", self.codes(np.zeros((64, 64, 3), np.uint8)))

    def test_constant_gray_has_flat_roughness(self):
        self.assertIn("roughness_flat", self.codes(np.full((64, 64, 3), 128, np.uint8)))

    def test_white_image_is_fixed_not_flagged_for_brightness(self):
        self.assertNotIn("albedo_too_bright", self.codes(np.full((64, 64, 3), 255, np.uint8)))

    def test_dark_metal_albedo_is_flagged(self):
        maps, _ = finish(fake_rgb.generate("steel", 2, 64, "metal"), "metal")
        maps["albedo"] = (maps["albedo"] * 0.3).astype(np.uint8)
        self.assertIn("metal_albedo_dark", [w["code"] for w in validate(maps, STYLE, "metal")])

    def test_bad_normals_and_ao_are_flagged(self):
        maps, _ = finish(fake_rgb.generate("x", 2, 64, "stone"), "stone")
        maps["normal"] = maps["normal"].copy()
        maps["normal"][..., 2] = -1.0
        maps["ao"] = maps["ao"] * 0.1
        codes = [w["code"] for w in validate(maps, STYLE, "stone")]
        self.assertIn("normal_invalid", codes)
        self.assertIn("ao_too_dark", codes)


class Source2LookTests(unittest.TestCase):
    """Pipeline version 4: per-family albedo bands, highlight roll-off, tileable photos, softened contact shadows
    (Docs/Research/AiTextures/D2_CALIBRATION.md > Source 2 look check)."""

    def test_family_band_overrides_the_common_one(self):
        self.assertEqual(STYLE.albedo_mean("brick"), [70, 125])
        self.assertEqual(STYLE.albedo_mean("fabric"), STYLE.common["dielectric_albedo_mean"])

    def test_bright_brick_is_brought_into_the_brick_band(self):
        rgb = fake_rgb.generate("brick", 3, 128, "brick").astype(np.float64)
        bright = np.clip(rgb * 1.8 + 40, 0, 255).astype(np.uint8)
        maps, _ = finish(bright, "brick")
        mean = float(st.luminance(maps["albedo"] / 255.0).mean() * 255)
        self.assertTrue(70 <= mean <= 125, mean)
        self.assertNotIn("albedo_too_bright", [w["code"] for w in validate(maps, STYLE, "brick")])

    def test_highlights_roll_off_but_keep_their_order(self):
        ramp = np.linspace(0.5, 1.0, 64)
        rgb = np.dstack([ramp, ramp, ramp])[None, ...].repeat(4, axis=0)[:, 0]
        out = st.compress_highlights(rgb, 200.0)
        lum = st.luminance(out) * 255
        self.assertLessEqual(float(lum.max()), 200.0)
        self.assertTrue((np.diff(lum[0]) >= -1e-9).all(), "brighter stays brighter")
        self.assertAlmostEqual(float(lum[0, 0]), 0.5 * 255, places=6)

    def test_a_photo_that_does_not_tile_is_made_tileable(self):
        from texgen import seams
        from texgen.imageops import make_tileable
        y, x = np.mgrid[0:128, 0:128] / 127.0
        photo = (np.dstack([x, y, x * y]) * 255).astype(np.uint8)
        self.assertFalse(seams.passes(seams.seam_score(photo.mean(-1))))
        tiled = make_tileable(photo)
        self.assertTrue(seams.passes(seams.seam_score(tiled.mean(-1))))
        # No break inside either: the shifted copies' own seams (in their middle) must be faded out everywhere.
        g = tiled.mean(-1)
        self.assertTrue(seams.passes(seams.seam_score(np.roll(g, (64, 64), axis=(0, 1)))))
        self.assertLess(float(np.abs(g[56:72, 56:72] - photo.mean(-1)[56:72, 56:72]).max()), 1.0, "the middle stays the photo")

    def test_contact_shadows_become_shallow(self):
        gray = np.full((64, 64), 0.5)
        gray[30:32, :] = 0.02
        soft = classic_pbr.soften_shadows(gray)
        self.assertGreater(float(soft.min()), 0.02 + 0.1)
        self.assertEqual(float(soft[0, 0]), 0.5)


if __name__ == "__main__":
    unittest.main()
