"""Writes Engine/Content/Scenes/NormalReliefTest.obsc (light from the left) and NormalReliefTest_Top.obsc (light from
above): the scenes that judge a generated normal map, and the normal-map convention the engine reads (task 0.2 of
Docs/Research/AiTextures/PLAN.md).

    python Tools/TexGen/lab/generate_normal_relief_test.py
    Engine.exe --scene Scenes/NormalReliefTest_Top.obsc --import-generated Tools/TexGen/cache/<id> --tiling 1 --shot top.png
    Engine.exe --scene Scenes/NormalReliefTest.obsc --import-generated Tools/TexGen/cache/<id> --tiling 1 --shot left.png

Source2MaterialTest lights its wall from the front, which is right for judging the albedo and wrong for judging relief:
frontal light hardly changes N.L, so any normal map looks flat there. Here the sky is night-dark and every wall has its
own point light just in front of its plane, grazing it from one side, so each brick edge facing the light brightens and
each edge facing away darkens.

Three walls stand side by side, each lit the same way:
  - "MaterialLab.Target*": a 2 x 2 m wall (four 1 m slabs) plus a cube and a sphere in front, with the material under test.
  - the Poly Haven bricks (CC0, DirectX / Y- normal map) as shipped: what correct relief looks like.
  - the same bricks with the green channel inverted (Bricks_Normal_FlipG.png, written by generate_normal_convention_test.py).
With the light from above (_Top), the correct one of the two brick walls shows lit top edges and dark undersides (the
bricks stand out); the inverted one looks sunken. The generated material must read like the correct one.
Z is up, metres.
"""
import json
import os
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CONTENT = os.path.join(ROOT, "Engine", "Content")
GROUND = "GameObjects/Textures/Ground"

# The slab's -Z face looks at the camera (-Y) with the texture upright (its first row at the top): the
# STAND_UP of generate_source2_material_test.py turned half a turn about the face normal.
STAND_UP = [-0.5, 0.5, 0.5, 0.5]
SLAB = 0.04
# Each wall's light sits this far in front of the wall (metres): close enough that the light grazes it.
LIGHT_OFFSET = 0.25


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "normalrelieftest/" + name))


def material(name, rgb, roughness, **extra):
    data = {"Red": rgb[0], "Green": rgb[1], "Blue": rgb[2], "Roughness": roughness, "Metallic": 0.0,
            "CastShadows": True, "Enabled": True}
    data.update(extra)
    return {"Type": "material", "InstanceId": guid(name + "/material"), "Data": data}


def bricks(normal):
    return {"TilingU": 1, "TilingV": 1, "BaseColorTexture": f"{GROUND}/Bricks_BaseColor.jpg", "NormalTexture": normal,
            "RoughnessTexture": f"{GROUND}/Bricks_Roughness.jpg"}


# (name prefix, left edge x, width in slabs, material extras or None for the material under test)
WALLS = [
    ("MaterialLab.Target Wall", -3.3, 2, None),
    ("ReliefTest Bricks DirectX", -0.6, 1, bricks(f"{GROUND}/Bricks_Normal.jpg")),
    ("ReliefTest Bricks FlipG", 1.1, 1, bricks(f"{GROUND}/Bricks_Normal_FlipG.png")),
]
WALL_HEIGHT = 2


def build(name, light_from):
    entities = []

    def entity(ename, model, position, scale, components, rotation=None):
        e = {"Id": len(entities) + 1, "Name": ename, "ModelKey": model, "Position": list(position),
             "Scale": list(scale), "Components": components, "IsEnabled": True}
        if rotation:
            e["Rotation"] = list(rotation)
        entities.append(e)

    lights = []
    for prefix, left, width, extra in WALLS:
        for ix in range(width):
            for iz in range(WALL_HEIGHT):
                ename = f"{prefix} {ix}{iz}"
                mat = material(ename, (0.6, 0.6, 0.6), 0.8) if extra is None else material(ename, (1, 1, 1), 1.0, **extra)
                entity(ename, "Cube", (left + 0.5 + ix, -SLAB / 2, 0.5 + iz), (0.5, 0.5, SLAB / 2), [mat], STAND_UP)
        # One light per wall, the same place relative to each wall, so the walls compare directly.
        if light_from == "top":
            position = [left + width / 2.0, -LIGHT_OFFSET, WALL_HEIGHT + 0.6]
            radius = 4.5
        else:
            position = [left - 0.4, -LIGHT_OFFSET, WALL_HEIGHT / 2.0]
            radius = 2.5 + width
        lights.append({"Id": 900 + len(lights), "Name": prefix + " Light", "Position": position, "Radius": radius,
                       "Color": [255, 250, 242, 255], "Intensity": 40, "CastShadows": False, "SourceRadius": 0.05})

    # The material on curved and boxy shapes, in front of its wall, for the same grazing light.
    entity("MaterialLab.Target Cube", "Cube", (-2.75, -0.75, 0.25), (0.25, 0.25, 0.25),
           [material("cube", (0.6, 0.6, 0.6), 0.8)])
    entity("MaterialLab.Target Sphere", "IsoSphere", (-1.85, -0.75, 0.3), (0.3, 0.3, 0.3),
           [material("sphere", (0.6, 0.6, 0.6), 0.8)])
    lights.append({"Id": 950, "Name": "Shapes Light",
                   "Position": [-3.4, -0.75, 0.45] if light_from == "left" else [-2.3, -0.75, 1.0],
                   "Radius": 2.2, "Color": [255, 250, 242, 255], "Intensity": 10, "CastShadows": False,
                   "SourceRadius": 0.05})

    dark = 0.03
    entity("ReliefTest Floor", "Cube", (-0.6, -1.5, -SLAB / 2), (3.5, 1.6, SLAB / 2),
           [material("floor", (dark, dark, dark), 0.9)])

    return {
        "Version": 2,
        "Name": name,
        "Entities": entities,
        "PointLights": lights,
        "SpotLights": [],
        "DirectionalLights": [],
        "Decals": [],
        # Almost no sky light: only the grazing lights should shade the walls.
        "EnvironmentSample": {"Position": [0, -1.5, 1.0], "SpecularStrength": 0.2, "DiffuseStrength": 0.05,
                              "AutoUpdate": True},
        # The night half of the day-night cycle, frozen at midnight with no moon or stars: a static sky would light
        # the walls with its full ambient. NightExposure 0 undoes the cycle's night darkening of the lamps.
        "Environment": {"DayNightCycle": True, "TimeOfDay": 0, "CycleDurationMinutes": 100000, "MoonBrightness": 0,
                        "StarBrightness": 0, "CloudCoverage": 0, "NightExposure": 0, "MaxAutoExposure": 1},
        "MainCamera": {"Position": [-0.6, -3.9, 1.05], "Forward": [0.0, 1.0, -0.02], "FieldOfView": 0.9,
                       "Components": []},
    }


if __name__ == "__main__":
    for file_name, light_from in (("NormalReliefTest.obsc", "left"), ("NormalReliefTest_Top.obsc", "top")):
        out = os.path.join(CONTENT, "Scenes", file_name)
        doc = build(os.path.splitext(file_name)[0], light_from)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2)
        print("wrote", out, len(doc["Entities"]), "gameobjects")
