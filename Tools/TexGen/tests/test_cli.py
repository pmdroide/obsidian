import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

import _bootstrap

ROOT = _bootstrap.TEXGEN_ROOT


def image_info(path):
    with Image.open(path) as img:
        return img.mode, img.size


def run_cli(*args):
    proc = subprocess.run([sys.executable, "-m", "texgen", *args], cwd=ROOT, capture_output=True, text=True)
    lines = [json.loads(line) for line in proc.stdout.splitlines()]  # every stdout line must be JSON
    return proc.returncode, lines, proc.stderr


class GenerateTests(unittest.TestCase):
    def test_fake_backend_writes_the_contract(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "3fa1c09b7d2e4a55"
            code, events, _ = run_cli("generate", "--prompt", "mossy cobblestone", "--seed", "1234", "--out", str(out),
                                      "--backend", "fake", "--resolution", "256")
            self.assertEqual(code, 0, events)
            self.assertEqual(events[-1]["event"], "done")
            self.assertTrue(events[-1]["out"].endswith("3fa1c09b7d2e4a55"))
            stages = [e["stage"] for e in events if e["event"] == "progress"]
            self.assertEqual(stages, ["load", "diffusion", "estimation", "style", "postprocess", "validate", "write"])

            slug = "mossy_cobblestone"
            expected = {"BaseColor": "RGB", "Normal": "RGB", "Roughness": "L", "AO": "L", "Height": "L"}
            for suffix, mode in expected.items():
                self.assertEqual(image_info(out / f"{slug}_{suffix}.png"), (mode, (256, 256)), suffix)
            self.assertFalse((out / f"{slug}_Metallic.png").exists(), "all-dielectric: Metallic is omitted")
            self.assertTrue((out / "_debug" / "source_rgb.png").exists())
            self.assertEqual(image_info(out / "_debug" / "height16.png")[0], "I;16")
            self.assertTrue((out / "_debug" / "validation.json").exists())
            self.assertFalse(Path(str(out) + ".tmp").exists(), "the temporary folder is renamed away")

            meta = json.loads((out / "texgen.json").read_text(encoding="utf-8"))
            self.assertEqual(meta["schema"], 1)
            self.assertEqual(meta["id"], "3fa1c09b7d2e4a55")
            self.assertEqual(meta["params"]["prompt"], "mossy cobblestone")
            self.assertEqual(meta["params"]["seed"], 1234)
            self.assertEqual(meta["params"]["backend"], "fake")
            self.assertEqual(meta["normal_convention"], "directx")
            self.assertEqual(meta["pipeline_version"], "4")
            self.assertEqual(meta["colorspace"]["basecolor"], "srgb")
            self.assertIn("tiling", meta["suggested"])
            self.assertLess(meta["seam_score"], 1.5)
            self.assertIn("passed", meta["validation"])

    def test_same_seed_gives_identical_maps_and_a_new_seed_differs(self):
        with tempfile.TemporaryDirectory() as tmp:
            a, b, c = (Path(tmp) / n for n in "abc")
            for out, seed in ((a, "7"), (b, "7"), (c, "8")):
                self.assertEqual(run_cli("generate", "--prompt", "red brick", "--seed", seed, "--out", str(out),
                                         "--resolution", "128")[0], 0)
            read = lambda p: np.array(Image.open(p / "red_brick_BaseColor.png"))
            self.assertTrue((read(a) == read(b)).all())
            self.assertFalse((read(a) == read(c)).all())

    def test_metal_family_writes_a_metallic_map(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "m"
            code, _, _ = run_cli("generate", "--prompt", "polished steel", "--seed", "3", "--out", str(out),
                                 "--resolution", "128")
            self.assertEqual(code, 0)
            self.assertEqual(image_info(out / "polished_steel_Metallic.png")[0], "L")

    def test_invalid_arguments_exit_2(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = str(Path(tmp) / "x")
            base = ["generate", "--prompt", "x", "--seed", "1", "--out", out]
            self.assertEqual(run_cli(*base, "--resolution", "100")[0], 2)
            self.assertEqual(run_cli("generate", "--prompt", "x", "--out", out)[0], 2)  # no seed
            self.assertEqual(run_cli(*base, "--backend", "nope")[0], 2)
            self.assertEqual(run_cli(*base, "--family", "lava")[0], 2)
            self.assertEqual(run_cli(*base, "--style", "nope")[0], 2)
            self.assertEqual(run_cli(*base, "--backend", "classic")[0], 2)  # needs --input-rgb
            code, events, _ = run_cli(*base, "--resolution", "100")
            self.assertEqual(events[-1]["event"], "error")
            self.assertFalse(Path(out).exists())

    def test_gpu_backend_without_dependencies_exits_3(self):
        if run_cli("check", "--backend", "sdxl_classic")[0] == 0:
            self.skipTest("sdxl_classic is installed here; the unavailable case can't be checked")
        with tempfile.TemporaryDirectory() as tmp:
            code, events, _ = run_cli("generate", "--prompt", "x", "--seed", "1", "--out", str(Path(tmp) / "x"),
                                      "--backend", "sdxl_classic", "--resolution", "128")
            self.assertEqual(code, 3)
            self.assertEqual(events[-1]["event"], "error")
            self.assertIn(events[-1]["code"], ("dependency_missing", "model_missing", "cuda_unavailable",
                                               "backend_not_implemented"))

    def test_classic_backend_from_a_photo_and_the_seam_check(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            # Tileable source: produced by the fake backend. Non-tileable: a horizontal ramp.
            self.assertEqual(run_cli("generate", "--prompt", "src", "--seed", "1", "--out", str(tmp / "src"),
                                     "--resolution", "128")[0], 0)
            code, events, _ = run_cli("generate", "--prompt", "photo", "--seed", "1", "--out", str(tmp / "ok"),
                                      "--backend", "classic", "--input-rgb", str(tmp / "src" / "_debug" / "source_rgb.png"),
                                      "--resolution", "128")
            self.assertEqual(code, 0, events)
            ramp = np.tile(np.linspace(0, 255, 128).astype(np.uint8)[None, :, None], (128, 1, 3))
            Image.fromarray(ramp).save(tmp / "ramp.png")
            code, events, _ = run_cli("generate", "--prompt", "ramp", "--seed", "1", "--out", str(tmp / "bad"),
                                      "--backend", "classic", "--input-rgb", str(tmp / "ramp.png"), "--resolution", "128")
            # Since pipeline version 4 a photo that doesn't tile is made tileable first instead of being rejected.
            self.assertEqual(code, 0, events)
            meta = json.loads((tmp / "bad" / "texgen.json").read_text(encoding="utf-8"))
            self.assertTrue(meta["source_made_tileable"])
            self.assertLess(meta["seam_score"], 1.5)
            code, _, _ = run_cli("generate", "--prompt", "ramp", "--seed", "1", "--out", str(tmp / "forced"),
                                 "--backend", "classic", "--input-rgb", str(tmp / "ramp.png"), "--resolution", "128",
                                 "--no-seam-check")
            self.assertEqual(code, 0)
            meta = json.loads((tmp / "forced" / "texgen.json").read_text(encoding="utf-8"))
            self.assertFalse(meta["source_made_tileable"], "--no-seam-check keeps the photo as it is")

    def test_strict_exits_5_when_a_style_rule_breaks(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            Image.fromarray(np.zeros((64, 64, 3), np.uint8)).save(tmp / "black.png")
            args = ["generate", "--prompt", "black", "--seed", "1", "--backend", "classic", "--input-rgb",
                    str(tmp / "black.png"), "--resolution", "64", "--no-seam-check"]
            code, events, _ = run_cli(*args, "--out", str(tmp / "loose"))
            self.assertEqual(code, 0)
            self.assertIn("albedo_too_dark", [e["code"] for e in events if e["event"] == "warning"])
            code, events, _ = run_cli(*args, "--out", str(tmp / "strict"), "--strict")
            self.assertEqual((code, events[-1]["code"]), (5, "validation_failed"))

    def test_opengl_normal_convention_flips_green(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            for conv in ("directx", "opengl"):
                self.assertEqual(run_cli("generate", "--prompt", "n", "--seed", "5", "--out", str(tmp / conv),
                                         "--resolution", "128", "--normal-convention", conv)[0], 0)
            dx = np.array(Image.open(tmp / "directx" / "n_Normal.png")).astype(int)
            gl = np.array(Image.open(tmp / "opengl" / "n_Normal.png")).astype(int)
            self.assertTrue((dx[..., 0] == gl[..., 0]).all() and (dx[..., 2] == gl[..., 2]).all())
            self.assertTrue((np.abs((255 - dx[..., 1]) - gl[..., 1]) <= 1).all())


class MiscCommandTests(unittest.TestCase):
    def test_version(self):
        proc = subprocess.run([sys.executable, "-m", "texgen", "--version"], cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0)
        self.assertRegex(proc.stdout.strip(), r"^\d+\.\d+\.\d+$")

    def test_styles_lists_source2_and_its_families(self):
        code, events, _ = run_cli("styles")
        self.assertEqual((code, len(events), events[0]["event"]), (0, 1, "done"))
        style = next(s for s in events[0]["styles"] if s["name"] == "source2")
        for family in ("stone", "concrete", "brick", "tile", "plaster", "wood", "metal", "painted_metal", "fabric",
                       "ground", "organic"):
            self.assertIn(family, style["families"])

    def test_check_reports_one_event(self):
        for backend in ("fake", "classic"):
            code, events, _ = run_cli("check", "--backend", backend)
            self.assertEqual((code, len(events), events[0]["event"]), (0, 1, "done"))
        code, events, _ = run_cli("check", "--backend", "sdxl_chord")
        self.assertEqual(len(events), 1)
        if code == 0:  # installed here: a single "done"
            self.assertEqual(events[0]["event"], "done")
        else:
            self.assertEqual((code, events[0]["event"]), (3, "error"))
            self.assertIn(events[0]["code"], ("dependency_missing", "model_missing", "cuda_unavailable"))


if __name__ == "__main__":
    unittest.main()
