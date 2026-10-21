"""Writes Engine/Content/Scenes/MaterialLab.obsc, the scene that judges generated materials (Docs/Research/AiTextures/PLAN.md D1).

    python Tools/TexGen/lab/generate_material_lab.py [--reference-mats]

Everything named "MaterialLab.Target*" takes the material under test, so one scene shows it as a tiled floor (4 x 4
slabs of 1 m, each repeating the texture once, so seams show), a 1 m cube and a sphere:

    Engine.exe --scene Scenes/MaterialLab.obsc --material Materials/Generated/<x>.mat --shot out/lab.png
    Engine.exe --scene Scenes/MaterialLab.obsc --import-generated Tools/TexGen/cache/<id> --shot out/lab.png

Next to them stand the calibration references: a neutral grey dielectric (albedo 0.5 linear, roughness 0.5) and a smooth
chrome sphere, a low sun, a point light from the side (so the relief reads) and a reflection probe.
--reference-mats also writes Materials/Reference/*.mat for the CC0 Poly Haven ground textures already in Content
(Bricks, Cobblestone, RockyDirt), the yardstick of the calibration (D2).
Z is up, metres. Material colours are sRGB like the Inspector's.
"""
import argparse
import json
import math
import os
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CONTENT = os.path.join(ROOT, "Engine", "Content")
OUT = os.path.join(CONTENT, "Scenes", "MaterialLab.obsc")
GROUND = "GameObjects/Textures/Ground"

entities = []
next_id = [1]


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "materiallab/" + name))


def srgb(linear):
    return 12.92 * linear if linear <= 0.0031308 else 1.055 * linear ** (1 / 2.4) - 0.055


def entity(name, model, position, scale, components):
    e = {"Id": next_id[0], "Name": name, "ModelKey": model, "Position": list(position), "Scale": list(scale),
         "Components": list(components), "IsEnabled": True}
    next_id[0] += 1
    entities.append(e)
    return e


def material(name, rgb, roughness, metallic=0.0):
    return {"Type": "material", "InstanceId": guid(name + "/material"),
            "Data": {"Red": rgb[0], "Green": rgb[1], "Blue": rgb[2], "Roughness": roughness, "Metallic": metallic,
                     "CastShadows": True, "Enabled": True}}


def static_body(name):
    return {"Type": "physics", "InstanceId": guid(name + "/physics"),
            "Data": {"BodyType": "Static", "Mass": 1.0, "Enabled": True}}


# The materials under test: a floor of 1 m slabs (the Cube model spans -1..1, so scale = half size), a cube, a sphere.
SLAB = 0.04
for ix in range(4):
    for iy in range(4):
        name = f"MaterialLab.Target Floor {ix}{iy}"
        entity(name, "Cube", (-1.5 + ix, -1.5 + iy, -SLAB / 2), (0.5, 0.5, SLAB / 2),
               [material(name, (0.6, 0.6, 0.6), 0.8), static_body(name)])
entity("MaterialLab.Target Cube", "Cube", (-0.9, 0.5, 0.5 + SLAB / 2 - 0.02), (0.5, 0.5, 0.5),
       [material("cube", (0.6, 0.6, 0.6), 0.8), static_body("cube")])
entity("MaterialLab.Target Sphere", "IsoSphere", (0.45, 0.5, 0.5), (0.5, 0.5, 0.5),
       [material("sphere", (0.6, 0.6, 0.6), 0.8), static_body("sphere")])

# Calibration references: albedo 0.5 linear = 0.735 sRGB grey; chrome F0 ~0.77 sRGB.
grey = srgb(0.5)
entity("MaterialLab Reference Grey", "IsoSphere", (1.25, -0.2, 0.3), (0.3, 0.3, 0.3),
       [material("ref grey", (grey, grey, grey), 0.5)])
entity("MaterialLab Reference Metal", "IsoSphere", (1.25, -1.0, 0.3), (0.3, 0.3, 0.3),
       [material("ref metal", (0.77, 0.78, 0.78), 0.05, 1.0)])

# A big box probe over the lab so the metal sphere and smooth materials reflect something.
entity("MaterialLab Reflection Probe", "Cube", (0, 0, 2.0), (6, 6, 3),
       [{"Type": "reflection-probe", "InstanceId": guid("probe"),
         "Data": {"BlendDistance": 0.5, "BoxProjection": False, "Priority": 0, "Resolution": 128, "Enabled": True}}])

doc = {
    "Version": 2,
    "Name": "MaterialLab",
    "Entities": entities,
    "PointLights": [
        # From the side and low: the relief of the normal map reads against it (the sun grazes from the other side)
        {"Id": 900, "Name": "Side Light", "Position": [-2.4, -1.6, 0.7], "Radius": 8, "Color": [255, 236, 214, 255],
         "Intensity": 3, "CastShadows": True, "ShadowResolution": 1024, "SourceRadius": 0.05},
    ],
    "SpotLights": [],
    "DirectionalLights": [
        {"Id": 903, "Name": "Sun", "Direction": [0.55, 0.45, -0.7], "Position": [0, 0, 10],
         "Color": [255, 247, 235, 255], "Intensity": 60, "CastShadows": True, "ShadowSize": 20,
         "ShadowDepth": 40, "ShadowResolution": 2048, "ShadowFiltering": 1},
    ],
    "Decals": [],
    "EnvironmentSample": {"Position": [0, 0, 1.0], "SpecularStrength": 1, "DiffuseStrength": 0.25, "AutoUpdate": True},
    "Environment": {"DayNightCycle": False, "TimeOfDay": 15, "MaxAutoExposure": 3},
    "MainCamera": {"Position": [0.2, -4.6, 2.5], "Forward": [-0.02, 0.88, -0.47], "FieldOfView": 0.9, "Components": []},
}


def reference_mats():
    """Poly Haven (CC0) ground sets already in Content as .mat files, texture slots only (neutral scalars)."""
    folder = os.path.join(CONTENT, "Materials", "Reference")
    os.makedirs(folder, exist_ok=True)
    for name in ("Bricks", "Cobblestone", "RockyDirt"):
        data = {"Version": 1, "Material": {
            "MaterialType": 0, "Red": 1, "Green": 1, "Blue": 1, "Roughness": 1, "Metallic": 0, "CastShadows": True,
            "TilingU": 1, "TilingV": 1, "Enabled": True,
            "BaseColorTexture": f"{GROUND}/{name}_BaseColor.jpg", "NormalTexture": f"{GROUND}/{name}_Normal.jpg",
            "RoughnessTexture": f"{GROUND}/{name}_Roughness.jpg", "DisplacementTexture": f"{GROUND}/{name}_Height.jpg",
            "MetallicTexture": "", "MaskTexture": "", "AmbientOcclusionTexture": ""}}
        path = os.path.join(folder, f"Poly_{name}.mat")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=2)
        print("wrote", path)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--reference-mats", action="store_true")
    args = ap.parse_args()
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
    print("wrote", OUT, len(entities), "gameobjects")
    if args.reference_mats:
        reference_mats()
