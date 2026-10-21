"""Writes Engine/Content/Scenes/Source2MaterialTest.obsc, the scene that judges a generated material against the
Half-Life: Alyx look (Docs/Research/AiTextures/D2_CALIBRATION.md > Source 2 look check).

    python Tools/TexGen/lab/generate_source2_material_test.py

MaterialLab (generate_material_lab.py) lights a floor with a warm low sun, which turns every material orange and hides
how a wall reads in the game. This scene is framed like the HLA screenshots instead: a 3 x 2 m wall facing the camera,
lit by soft, neutral light (a high white sun from behind the camera, a dim fill and a bright sky ambient), so the albedo
reads at its own value. Everything named "MaterialLab.Target*" takes the material under test, as in MaterialLab:

    Engine.exe --scene Scenes/Source2MaterialTest.obsc --material Materials/Generated/<x>.mat --tiling 1 --shot out/wall.png
    Engine.exe --scene Scenes/Source2MaterialTest.obsc --import-generated Tools/TexGen/cache/<id> --tiling 1 --shot out/wall.png
    python Tools/TexGen/lab/compare_source2_look.py Tools/TexGen/cache/<id> --hla brick --shot out/wall.png

The wall is six 1 m slabs of the Cube model stood up so that the texture's rows run horizontally and its top is up; use
--tiling 1 so each slab repeats the texture exactly once (a seam between slabs then means it doesn't tile; generated
materials otherwise carry their real-world scale). Next to it stand the yardsticks: the Poly Haven bricks (CC0, Materials/Reference/Poly_Bricks.mat), an 18 % grey card (albedo 0.18 linear, the usual
photographic mid grey), a white card (0.8) and a chrome sphere; a grey concrete-coloured floor. Z is up, metres.
"""
import json
import os
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CONTENT = os.path.join(ROOT, "Engine", "Content")
OUT = os.path.join(CONTENT, "Scenes", "Source2MaterialTest.obsc")
GROUND = "GameObjects/Textures/Ground"

entities = []
next_id = [1]

# Quaternion [x, y, z, w] (XNA Matrix.CreateFromQuaternion, row vectors) taking local X -> +Z, Y -> +X, Z -> +Y: the
# slab's -Z face, whose texture U runs along local Y, faces the camera at -Y with U horizontal and V up.
STAND_UP = [-0.5, -0.5, -0.5, 0.5]
SLAB = 0.04


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "source2materialtest/" + name))


def srgb(linear):
    return 12.92 * linear if linear <= 0.0031308 else 1.055 * linear ** (1 / 2.4) - 0.055


def entity(name, model, position, scale, components, rotation=None):
    e = {"Id": next_id[0], "Name": name, "ModelKey": model, "Position": list(position), "Scale": list(scale),
         "Components": list(components), "IsEnabled": True}
    if rotation:
        e["Rotation"] = list(rotation)
    next_id[0] += 1
    entities.append(e)
    return e


def material(name, rgb, roughness, metallic=0.0, **extra):
    data = {"Red": rgb[0], "Green": rgb[1], "Blue": rgb[2], "Roughness": roughness, "Metallic": metallic,
            "CastShadows": True, "Enabled": True}
    data.update(extra)
    return {"Type": "material", "InstanceId": guid(name + "/material"), "Data": data}


def static_body(name):
    return {"Type": "physics", "InstanceId": guid(name + "/physics"),
            "Data": {"BodyType": "Static", "Mass": 1.0, "Enabled": True}}


def wall_slab(name, x, z, components):
    # Back face at Y = 0: the visible face sits at Y = -SLAB.
    entity(name, "Cube", (x, -SLAB / 2, z), (0.5, 0.5, SLAB / 2), components, STAND_UP)


# The material under test: a 3 x 2 m wall of 1 m slabs, bottom at the floor.
for ix in range(3):
    for iz in range(2):
        name = f"MaterialLab.Target Wall {ix}{iz}"
        wall_slab(name, -1.0 + ix, 0.5 + iz, [material(name, (0.6, 0.6, 0.6), 0.8), static_body(name)])
entity("MaterialLab.Target Sphere", "IsoSphere", (0.9, -0.9, 0.35), (0.35, 0.35, 0.35),
       [material("sphere", (0.6, 0.6, 0.6), 0.8), static_body("sphere")])

# Yardstick: the Poly Haven bricks (CC0) on a 1 x 2 m wall to the right, linked to the reference .mat.
POLY = {"MaterialPath": "Materials/Reference/Poly_Bricks.mat", "MaterialType": 0, "TilingU": 1, "TilingV": 1,
        "BaseColorTexture": f"{GROUND}/Bricks_BaseColor.jpg", "NormalTexture": f"{GROUND}/Bricks_Normal.jpg",
        "RoughnessTexture": f"{GROUND}/Bricks_Roughness.jpg", "DisplacementTexture": f"{GROUND}/Bricks_Height.jpg"}
for iz in range(2):
    name = f"Source2Test Reference Bricks {iz}"
    wall_slab(name, 2.15, 0.5 + iz, [material(name, (1, 1, 1), 1.0, **POLY), static_body(name)])

# Value cards (matte) left of the wall and a chrome sphere: what 18 % grey and white look like under this light.
for name, linear, x in (("Source2Test Grey Card 18", 0.18, -1.95), ("Source2Test White Card 80", 0.8, -2.45)):
    v = srgb(linear)
    entity(name, "Cube", (x, -0.3, 0.6), (0.2, 0.02, 0.3), [material(name, (v, v, v), 0.9)])
entity("Source2Test Reference Chrome", "IsoSphere", (-2.2, -0.9, 0.25), (0.25, 0.25, 0.25),
       [material("chrome", (0.77, 0.78, 0.78), 0.05, 1.0)])

# Floor: a neutral, slightly dark concrete grey (albedo ~0.2 linear), not the material under test.
floor_grey = srgb(0.2)
entity("Source2Test Floor", "Cube", (0, -2.0, -SLAB / 2), (4.0, 2.5, SLAB / 2),
       [material("floor", (floor_grey, floor_grey, floor_grey), 0.85), static_body("floor")])

entity("Source2Test Reflection Probe", "Cube", (0, -2.0, 1.5), (5, 4, 2.5),
       [{"Type": "reflection-probe", "InstanceId": guid("probe"),
         "Data": {"BlendDistance": 0.5, "BoxProjection": False, "Priority": 0, "Resolution": 128, "Enabled": True}}])

doc = {
    "Version": 2,
    "Name": "Source2MaterialTest",
    "Entities": entities,
    "PointLights": [
        # A dim, large fill from the left and low: just enough grazing light that the relief reads, like a lamp
        # off-screen in the HLA shots (MaterialLab's side light is far stronger). No shadows: grazing the wall they streak.
        {"Id": 900, "Name": "Fill Light", "Position": [-2.2, -1.6, 0.6], "Radius": 7, "Color": [242, 244, 255, 255],
         "Intensity": 0.8, "CastShadows": False, "SourceRadius": 0.3},
    ],
    "SpotLights": [],
    "DirectionalLights": [
        # High, neutral white, from behind the camera and a little to the left: the wall gets soft, near-frontal light
        # (the HLA brick screenshot), so the albedo reads at its own value instead of a grazing warm tint.
        {"Id": 903, "Name": "Sun", "Direction": [0.3, 0.55, -0.78], "Position": [0, 0, 10],
         "Color": [255, 253, 250, 255], "Intensity": 25, "CastShadows": True, "ShadowSize": 16,
         "ShadowDepth": 40, "ShadowResolution": 2048, "ShadowFiltering": 1},
    ],
    "Decals": [],
    "EnvironmentSample": {"Position": [0, -1.5, 1.0], "SpecularStrength": 1, "DiffuseStrength": 0.6, "AutoUpdate": True},
    "Environment": {"DayNightCycle": False, "TimeOfDay": 13, "MaxAutoExposure": 2},
    "MainCamera": {"Position": [0.15, -3.6, 1.15], "Forward": [-0.04, 1.0, -0.05], "FieldOfView": 0.9, "Components": []},
}


if __name__ == "__main__":
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
    print("wrote", OUT, len(entities), "gameobjects")
