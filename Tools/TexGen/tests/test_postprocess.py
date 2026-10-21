import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

import _bootstrap  # noqa: F401
from texgen import postprocess as pp


class NormalTests(unittest.TestCase):
    def test_flat_normal_encodes_to_128_128_255(self):
        n = np.zeros((4, 4, 3))
        n[..., 2] = 1.0
        for conv in ("directx", "opengl"):
            self.assertTrue((pp.sanitize_normal(n, conv) == [128, 128, 255]).all(), conv)

    def test_dx_gl_round_trip(self):
        rng = np.random.default_rng(1)
        n = rng.normal(size=(16, 16, 3))
        n[..., 2] = np.abs(n[..., 2]) + 0.5
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        for conv in ("directx", "opengl"):
            back = pp.decode_normal(pp.sanitize_normal(n, conv), conv)
            self.assertLess(float(np.abs(back - n).max()), 0.02, conv)
        dx, gl = pp.sanitize_normal(n, "directx").astype(int), pp.sanitize_normal(n, "opengl").astype(int)
        self.assertTrue((np.abs(255 - dx[..., 1] - gl[..., 1]) <= 1).all())
        self.assertTrue(np.allclose(pp.normal_flip_y(pp.normal_flip_y(n)), n))

    def test_sanitize_renormalises_and_forces_positive_z(self):
        n = np.zeros((2, 2, 3))
        n[..., 0] = 5.0
        n[..., 2] = -2.0
        out = pp.decode_normal(pp.sanitize_normal(n, "opengl"), "opengl")
        self.assertTrue((out[..., 2] > 0).all())
        self.assertTrue(np.allclose(np.linalg.norm(out, axis=-1), 1.0, atol=1e-6))

    def test_unknown_convention_raises(self):
        with self.assertRaises(ValueError):
            pp.sanitize_normal(np.zeros((1, 1, 3)) + [0, 0, 1], "vulkan")


class ScalarMapTests(unittest.TestCase):
    def test_roughness_is_clamped(self):
        r = pp.clamp_roughness(np.array([-1.0, 0.0, 0.5, 2.0]))
        self.assertEqual(list(r), [0.04, 0.04, 0.5, 1.0])

    def test_metal_is_binary_and_force_dielectric_zeroes_it(self):
        m = np.array([0.0, 0.3, 0.5, 0.9, 1.0])
        self.assertEqual(list(pp.binarize_metal(m)), [0, 0, 1, 1, 1])
        self.assertEqual(float(pp.binarize_metal(m, force_dielectric=True).max()), 0.0)

    def test_flat_height_gives_full_ao_and_a_pit_gets_dark_but_not_below_the_floor(self):
        self.assertTrue((pp.ao_from_height(np.full((64, 64), 0.5)) == 1.0).all())
        h = np.ones((64, 64))
        h[28:36, 28:36] = 0.0  # a pit
        ao = pp.ao_from_height(h)
        self.assertLess(float(ao[28, 32]), 0.9, "the pit edge is occluded by the rim")
        self.assertGreaterEqual(float(ao.min()), 0.35)
        self.assertLessEqual(float(ao.max()), 1.0)

    def test_ao_wraps_at_the_edges(self):
        h = np.zeros((32, 32))
        h[:, 0] = 1.0  # a wall on column 0 darkens column 31 through the wrap
        ao = pp.ao_from_height(h)
        self.assertLess(float(ao[16, 31]), float(ao[16, 16]))


class MagentaAndFilesTests(unittest.TestCase):
    def test_no_magenta_survives(self):
        img = np.zeros((4, 4, 3), np.uint8)
        img[1, 1] = (255, 0, 255)
        img[2, 2] = (255, 0, 254)
        out = pp.remove_magenta(img)
        self.assertFalse(((out[..., 0] == 255) & (out[..., 1] == 0) & (out[..., 2] == 255)).any())
        self.assertTrue((out[2, 2] == (255, 0, 254)).all(), "only exact magenta is touched")

    def test_png_modes(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            pp.save_rgb(tmp / "c.png", np.zeros((4, 4, 3), np.uint8))
            pp.save_gray(tmp / "g.png", np.full((4, 4), 0.5))
            pp.save_gray16(tmp / "h.png", np.full((4, 4), 0.5))
            modes = {}
            for name in ("c", "g", "h"):
                with Image.open(tmp / (name + ".png")) as img:
                    modes[name] = img.mode
                    if name == "g":
                        self.assertEqual(int(np.array(img)[0, 0]), 128)
            self.assertEqual(modes, {"c": "RGB", "g": "L", "h": "I;16"})


if __name__ == "__main__":
    unittest.main()
