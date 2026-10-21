"""Writes the scenes for task 0.2 of Docs/Research/AiTextures/PLAN.md: which green-channel convention does the engine read?

    python Tools/TexGen/lab/generate_normal_convention_test.py [--variant-b-out PATH]

Two floor slabs side by side use the Poly Haven Bricks material (a DirectX / Y- normal map): the left one with the map
as shipped, the right one with the green channel inverted (Bricks_Normal_FlipG.png, written next to the other ground
textures and registered in Content.mgcb). A low sun grazes them from one side. Variant A (the content scene) has the
sun coming from -X, variant B (written to --variant-b-out, default next to the A scene's name with _B) from +X.
In the correct slab the bricks' faces towards the sun are brighter and the mortar joints on the far side darker, in both
variants; the other slab looks sunken (lit the wrong way round).
Z is up, metres.
"""
import argparse
import json
import os
import re
import uuid

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CONTENT = os.path.join(ROOT, "Engine", "Content")
GROUND = os.path.join(CONTENT, "GameObjects", "Textures", "Ground")
FLIPPED = "GameObjects/Textures/Ground/Bricks_Normal_FlipG.png"


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "normalconvention/" + name))


def write_flipped_normal():
    src = Image.open(os.path.join(GROUND, "Bricks_Normal.jpg")).convert("RGB")
    r, g, b = src.split()
    g = g.point(lambda v: 255 - v)
    Image.merge("RGB", (r, g, b)).save(os.path.join(CONTENT, FLIPPED.replace("/", os.sep)))


def register_in_mgcb(rel):
    path = os.path.join(CONTENT, "Content.mgcb")
    with open(path, "r", encoding="utf-8", newline="") as f:
        text = f.read()
    if "#begin " + rel in text:
        return
    nl = "\r\n" if "\r\n" in text else "\n"
    block = nl.join([
        "#begin " + rel,
        "/importer:TextureImporter",
        "/processor:TextureProcessor",
        "/processorParam:ColorKeyColor=255,0,255,255",
        "/processorParam:ColorKeyEnabled=False",
        "/processorParam:GenerateMipmaps=True",
        "/processorParam:PremultiplyAlpha=True",
        "/processorParam:ResizeToPowerOfTwo=False",
        "/processorParam:MakeSquare=False",
        "/processorParam:TextureFormat=Color",
        "/build:" + rel,
    ])
    if not text.endswith(nl):
        text += nl
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text + nl + block + nl)


def slab(entity_id, name, x, normal):
    return {
        "Id": entity_id, "Name": name, "ModelKey": "Cube", "Position": [x, 0, -0.01], "Scale": [1.0, 1.0, 0.01],
        "Components": [{
            "Type": "material", "InstanceId": guid(name),
            "Data": {
                "Red": 1.0, "Green": 1.0, "Blue": 1.0, "Roughness": 0.9, "Metallic": 0.0, "CastShadows": True,
                "TilingU": 2, "TilingV": 2,
                "BaseColorTexture": "GameObjects/Textures/Ground/Bricks_BaseColor.jpg",
                "NormalTexture": normal,
                "RoughnessTexture": "GameObjects/Textures/Ground/Bricks_Roughness.jpg",
                "Enabled": True,
            }}],
        "IsEnabled": True,
    }


def scene(name, sun_dir):
    return {
        "Version": 2, "Name": name,
        "Entities": [
            slab(1, "As Shipped (DirectX)", -1.1, "GameObjects/Textures/Ground/Bricks_Normal.jpg"),
            slab(2, "Green Inverted", 1.1, FLIPPED),
        ],
        "PointLights": [], "SpotLights": [],
        "DirectionalLights": [{
            "Id": 10, "Name": "Low Sun", "Direction": sun_dir, "Position": [0, 0, 10], "Color": [255, 247, 235, 255],
            "Intensity": 100, "CastShadows": True, "ShadowSize": 20, "ShadowDepth": 40, "ShadowResolution": 1024, "ShadowFiltering": 1}],
        "Decals": [],
        "EnvironmentSample": {"Position": [0, 0, 1.0], "SpecularStrength": 1, "DiffuseStrength": 0.02, "AutoUpdate": True},
        "Environment": {"DayNightCycle": False, "TimeOfDay": 15},
        "MainCamera": {"Position": [0, -3.0, 3.6], "Forward": [0.0, 0.64, -0.77], "FieldOfView": 0.9, "Components": []},
    }


def main():
    ap = argparse.ArgumentParser()
    default_b = os.path.join(CONTENT, "Scenes", "NormalConventionTest_B.obsc")
    ap.add_argument("--variant-b-out", default=default_b)
    args = ap.parse_args()

    write_flipped_normal()
    register_in_mgcb(FLIPPED)

    out_a = os.path.join(CONTENT, "Scenes", "NormalConventionTest.obsc")
    for path, name, sun in ((out_a, "NormalConventionTest", [0.92, 0.0, -0.38]),
                            (args.variant_b_out, "NormalConventionTest_B", [-0.92, 0.0, -0.38])):
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(scene(name, sun), f, indent=2)
        print("wrote", path)


if __name__ == "__main__":
    main()
