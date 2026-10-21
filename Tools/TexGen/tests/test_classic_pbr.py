import unittest

import numpy as np

import _bootstrap  # noqa: F401
from texgen import seams
from texgen.backends import classic_pbr, fake_rgb
from texgen.imageops import gaussian_blur_wrap, luminance


class EstimatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.size = 128
        cls.rgb = fake_rgb.generate("stone", 11, cls.size, "stone")
        cls.est = classic_pbr.estimate(cls.rgb, 2.5)

    def test_shapes_types_and_ranges(self):
        e, s = self.est, self.size
        self.assertEqual(e["albedo"].shape, (s, s, 3))
        self.assertEqual(e["albedo"].dtype, np.uint8)
        for key in ("height", "roughness_norm", "metalness"):
            self.assertEqual(e[key].shape, (s, s), key)
        self.assertTrue(0.0 <= e["height"].min() and e["height"].max() <= 1.0)
        self.assertTrue(0.0 <= e["roughness_norm"].min() and e["roughness_norm"].max() <= 1.0)
        self.assertEqual(float(e["metalness"].max()), 0.0)
        self.assertEqual(e["normal"].shape, (s, s, 3))

    def test_normals_are_unit_and_face_up(self):
        n = self.est["normal"]
        self.assertTrue(np.allclose(np.linalg.norm(n, axis=-1), 1.0, atol=1e-6))
        self.assertGreater(float(n[..., 2].min()), 0.0)

    def test_albedo_is_delit(self):
        lit = np.clip(self.rgb.astype(float) * np.linspace(0.3, 1.0, self.size)[None, :, None], 0, 255).astype(np.uint8)
        low = lambda img: gaussian_blur_wrap(luminance(img / 255.0), self.size / 8).std()
        delit = classic_pbr.delight(lit)
        self.assertLess(low(delit), low(lit) * 0.5, "the horizontal lighting gradient is mostly removed")

    def test_slope_direction_follows_the_height_in_y_plus(self):
        # A single raised bump: on its left side (smaller x) the normal leans towards -X; on its upper side
        # (smaller row index = +Y in the normal map) it leans towards +Y.
        s = 64
        yy, xx = np.mgrid[0:s, 0:s]
        bump = np.exp(-(((xx - 32) ** 2 + (yy - 32) ** 2) / (2 * 6.0 ** 2)))
        n = classic_pbr.normal_from_height(bump, 2.0, s)
        self.assertLess(n[32, 24, 0], -0.05)
        self.assertGreater(n[32, 40, 0], 0.05)
        self.assertGreater(n[24, 32, 1], 0.05)
        self.assertLess(n[40, 32, 1], -0.05)

    def test_flat_height_gives_the_flat_normal(self):
        n = classic_pbr.normal_from_height(np.full((32, 32), 0.5), 2.0, 32)
        self.assertTrue(np.allclose(n, [0, 0, 1]))

    def test_reoriented_blend_with_a_flat_detail_keeps_the_base(self):
        base = classic_pbr.normal_from_height(self.est["height"], 2.0, self.size)
        flat = np.broadcast_to(np.array([0.0, 0.0, 1.0]), base.shape)
        self.assertTrue(np.allclose(classic_pbr.reoriented_blend(base, flat), base, atol=1e-6))

    def test_tileable_input_gives_tileable_maps(self):
        for key in ("height", "roughness_norm"):
            self.assertLess(seams.seam_score(self.est[key]), 1.5, key)
        self.assertLess(seams.seam_score(luminance(self.est["albedo"] / 255.0)), 1.5)
        self.assertLess(seams.seam_score(self.est["normal"]), 1.5)


if __name__ == "__main__":
    unittest.main()
