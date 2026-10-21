import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import numpy as np

import _bootstrap  # noqa: F401
from texgen import pipeline
from texgen.backends import fake_rgb, sdxl_rgb


class SdxlWiringTests(unittest.TestCase):
    """No GPU needed: the diffusion call is replaced, the rest of the wiring is the real one."""

    def test_generation_size(self):
        self.assertEqual(sdxl_rgb.generation_size(512), 1024)
        self.assertEqual(sdxl_rgb.generation_size(1024), 1024)
        self.assertEqual(sdxl_rgb.generation_size(2048), 2048)

    def test_pipeline_sends_the_style_prompts_to_the_model(self):
        seen = {}

        def fake_generate(prompt, negative, seed, size, on_step=None):
            seen.update(prompt=prompt, negative=negative, seed=seed, size=size)
            on_step(1.0)
            return fake_rgb.generate("mossy cobblestone", 1, size, "stone")

        with tempfile.TemporaryDirectory() as tmp, contextlib.redirect_stdout(io.StringIO()) as out, \
                mock.patch.object(pipeline, "check_backend", return_value=(True, "", "")), \
                mock.patch.object(sdxl_rgb, "generate", fake_generate):
            result = pipeline.run({"prompt": "mossy cobblestone", "negative_prompt": "blurry", "seed": 5, "resolution": 128,
                                   "backend": "sdxl_classic", "family": "stone", "seam_check": False}, Path(tmp) / "id")
            self.assertTrue((result / "texgen.json").is_file())
        self.assertTrue(seen["prompt"].startswith("mossy cobblestone"))
        self.assertIn("seamless tileable", seen["prompt"])
        self.assertTrue(seen["negative"].endswith(", blurry"))
        self.assertEqual((seen["seed"], seen["size"]), (5, 128))
        self.assertIn('"stage": "diffusion"', out.getvalue())


@unittest.skipUnless(importlib.util.find_spec("torch"), "torch is not installed")
class TileableTests(unittest.TestCase):
    def test_every_conv2d_wraps_and_the_output_is_periodic(self):
        import torch
        net = torch.nn.Sequential(torch.nn.Conv2d(3, 4, 3, padding=1), torch.nn.ReLU(), torch.nn.Conv2d(4, 3, 3, padding=1))
        self.assertEqual(sdxl_rgb.make_tileable(net), 2)
        self.assertTrue(all(m.padding_mode == "circular" for m in net.modules() if isinstance(m, torch.nn.Conv2d)))
        x = torch.randn(1, 3, 16, 16)
        shifted = torch.roll(x, shifts=5, dims=3)
        # circular padding makes the convolution shift-equivariant, which zero padding is not
        self.assertTrue(torch.allclose(torch.roll(net(x), shifts=5, dims=3), net(shifted), atol=1e-5))


if __name__ == "__main__":
    unittest.main()
