import unittest

import numpy as np

import _bootstrap  # noqa: F401
from texgen import seams
from texgen.backends import fake_rgb
from texgen.imageops import gaussian_blur_wrap


class SeamTests(unittest.TestCase):
    def test_periodic_noise_passes(self):
        rng = np.random.default_rng(3)
        self.assertTrue(seams.passes(seams.seam_score(rng.random((128, 128)))))
        self.assertTrue(seams.passes(seams.seam_score(fake_rgb.fbm(128, rng))))

    def test_linear_gradient_fails(self):
        ramp_x = np.tile(np.linspace(0, 1, 128), (128, 1))
        self.assertFalse(seams.passes(seams.seam_score(ramp_x)))
        self.assertFalse(seams.passes(seams.seam_score(ramp_x.T)))
        self.assertFalse(seams.passes(seams.seam_score(np.dstack([ramp_x] * 3))))

    def test_two_different_halves_fail(self):
        rng = np.random.default_rng(4)
        a, b = (gaussian_blur_wrap(rng.random((128, 128)), 2.0) for _ in range(2))
        self.assertFalse(seams.passes(seams.seam_score(np.concatenate([a[:, :64], b[:, 64:]], axis=1))))

    def test_joints_on_the_tile_border_do_not_count_as_a_seam(self):
        for size in (128, 256, 512):
            tile = fake_rgb.generate("t", 1, size, "tile").astype(float)
            self.assertTrue(seams.passes(seams.seam_score(tile)), size)
            brick = fake_rgb.generate("b", 1, size, "brick").astype(float)
            self.assertTrue(seams.passes(seams.seam_score(brick)), size)


if __name__ == "__main__":
    unittest.main()
