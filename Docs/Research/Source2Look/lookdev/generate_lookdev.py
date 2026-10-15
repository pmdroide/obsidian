"""Writes Engine/Content/Scenes/LookDev.obsc, the Source 2 look-dev scene (Docs/Research/Source2Look phase 0.2).

    python Docs/Research/Source2Look/lookdev/generate_lookdev.py

Every phase of the plan is judged here: a dielectric roughness sweep, gold/copper/chrome/iron spheres, an X-Rite
colour checker, a dark room lit only through a doorway (bounce light, reflections that must not glow), a flashlight
spot light with a cookie and shadows, a tube light, glass in front of a wall, and a Y Bot on the floor.
Z is up, metres. Material colours are sRGB like the Inspector's.
"""
import json
import math
import os
import uuid


def aim(origin, target):
    """Quaternion [x, y, z, w] turning a spot light's local -Z (SpotLight.LocalDirection) towards target."""
    d = [t - o for t, o in zip(target, origin)]
    n = math.sqrt(sum(v * v for v in d))
    d = [v / n for v in d]
    a = (0.0, 0.0, -1.0)
    dot = sum(x * y for x, y in zip(a, d))
    if dot > 0.99999:
        return [0, 0, 0, 1]
    if dot < -0.99999:
        return [1, 0, 0, 0]
    axis = (a[1] * d[2] - a[2] * d[1], a[2] * d[0] - a[0] * d[2], a[0] * d[1] - a[1] * d[0])
    an = math.sqrt(sum(v * v for v in axis))
    axis = [v / an for v in axis]
    half = math.acos(max(-1, min(1, dot))) / 2
    return [round(axis[0] * math.sin(half), 6), round(axis[1] * math.sin(half), 6),
            round(axis[2] * math.sin(half), 6), round(math.cos(half), 6)]

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
OUT = os.path.join(ROOT, "Engine", "Content", "Scenes", "LookDev.obsc")

entities = []
next_id = [1]


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "lookdev/" + name))


def material(name, rgb, roughness, metallic=0.0, **extra):
    data = {"Red": rgb[0], "Green": rgb[1], "Blue": rgb[2], "Roughness": roughness, "Metallic": metallic,
            "CastShadows": True, "Enabled": True}
    data.update(extra)
    return {"Type": "material", "InstanceId": guid(name + "/material"), "Data": data}


def static_body(name):
    return {"Type": "physics", "InstanceId": guid(name + "/physics"),
            "Data": {"BodyType": "Static", "Mass": 1.0, "Enabled": True}}


def entity(name, model, position, scale=(1, 1, 1), components=(), rotation=None):
    e = {"Id": next_id[0], "Name": name, "ModelKey": model, "Position": list(position), "Scale": list(scale),
         "Components": list(components), "IsEnabled": True}
    if rotation is not None:
        e["Rotation"] = rotation
    next_id[0] += 1
    entities.append(e)
    return e


def box(name, center, size, rgb=(0.6, 0.6, 0.6), roughness=0.8, metallic=0.0, collide=True, **extra):
    comps = [material(name, rgb, roughness, metallic, **extra)]
    if collide:
        comps.append(static_body(name))
    # The Cube model spans -1..1: scale = half size
    return entity(name, "Cube", center, (size[0] / 2, size[1] / 2, size[2] / 2), comps)


def ao_proxy(name, shape="Auto"):
    return {"Type": "ao-proxy", "InstanceId": guid(name + "/aoproxy"),
            "Data": {"Shape": shape, "Intensity": 1.0, "SizeScale": 1.0, "Falloff": 2.0, "CullDistance": 25, "Enabled": True}}


def sphere(name, center, radius, rgb, roughness, metallic=0.0):
    # AO proxies give the spheres a soft contact shadow on the floor (phase 6.1)
    return entity(name, "IsoSphere", center, (radius, radius, radius), [material(name, rgb, roughness, metallic), ao_proxy(name)])


# ---- Floor ----
box("Floor", (0, 0, -0.125), (60, 60, 0.25), rgb=(0.45, 0.45, 0.45), roughness=0.85)

# ---- Dielectric roughness sweep (white plastic) ----
for i, r in enumerate([0.05, 0.25, 0.5, 0.75, 1.0]):
    sphere(f"Plastic r{r:.2f}", (-8 + i * 1.6, 0, 0.6), 0.6, (0.8, 0.8, 0.8), r)

# ---- Metals (sRGB of the measured linear F0) ----
METALS = [
    ("Gold", (1.0, 0.86, 0.57), 0.2),
    ("Copper", (0.98, 0.82, 0.76), 0.3),
    ("Chrome", (0.77, 0.78, 0.78), 0.03),
    ("Rough Iron", (0.77, 0.76, 0.75), 0.6),
]
for i, (name, rgb, r) in enumerate(METALS):
    sphere(name + " Sphere", (0.5 + i * 1.6, 0, 0.6), 0.6, rgb, r, metallic=1.0)

# ---- Colour checker (X-Rite ColorChecker Classic, sRGB D50) on a dark board ----
CHECKER = [
    (115, 82, 68), (194, 150, 130), (98, 122, 157), (87, 108, 67), (133, 128, 177), (103, 189, 170),
    (214, 126, 44), (80, 91, 166), (193, 90, 99), (94, 60, 108), (157, 188, 64), (224, 163, 46),
    (56, 61, 150), (70, 148, 73), (175, 54, 60), (231, 199, 31), (187, 86, 149), (8, 133, 161),
    (243, 243, 242), (200, 200, 200), (160, 160, 160), (122, 122, 121), (85, 85, 85), (52, 52, 52),
]
box("Checker Board", (-4, 4.05, 1.25), (3.2, 0.05, 2.2), rgb=(0.05, 0.05, 0.05), roughness=0.9)
for i, c in enumerate(CHECKER):
    col, row = i % 6, i // 6
    box(f"Checker {i + 1:02d}", (-5.25 + col * 0.5, 4.0, 2.0 - row * 0.5), (0.42, 0.04, 0.42),
        rgb=tuple(v / 255 for v in c), roughness=0.9, collide=False)

# ---- Dark room: lit through a doorway in its west wall (the sun shines in going +X) ----
RX0, RX1, RY0, RY1, RH, T = 10, 18, -4, 4, 4, 0.3
box("Room Floor", ((RX0 + RX1) / 2, 0, 0.05), (RX1 - RX0, RY1 - RY0, 0.1), rgb=(0.5, 0.48, 0.45), roughness=0.7)
box("Room Roof", ((RX0 + RX1) / 2, 0, RH + T / 2), (RX1 - RX0 + 2 * T, RY1 - RY0 + 2 * T, T), rgb=(0.6, 0.6, 0.6))
box("Room East Wall", (RX1 + T / 2, 0, RH / 2), (T, RY1 - RY0 + 2 * T, RH), rgb=(0.7, 0.68, 0.64))
box("Room North Wall", ((RX0 + RX1) / 2, RY1 + T / 2, RH / 2), (RX1 - RX0, T, RH), rgb=(0.7, 0.68, 0.64))
box("Room South Wall", ((RX0 + RX1) / 2, RY0 - T / 2, RH / 2), (RX1 - RX0, T, RH), rgb=(0.7, 0.68, 0.64))
DOOR = 1.2  # half width of the doorway
box("Room West Wall N", (RX0 - T / 2, (DOOR + RY1) / 2, RH / 2), (T, RY1 - DOOR, RH), rgb=(0.7, 0.68, 0.64))
box("Room West Wall S", (RX0 - T / 2, (RY0 - DOOR) / 2, RH / 2), (T, -DOOR - RY0, RH), rgb=(0.7, 0.68, 0.64))
box("Room Lintel", (RX0 - T / 2, 0, 3.0 + (RH - 3.0) / 2), (T, 2 * DOOR, RH - 3.0), rgb=(0.7, 0.68, 0.64))
sphere("Room Chrome Sphere", (16, -2, 0.7), 0.6, (0.77, 0.78, 0.78), 0.05, metallic=1.0)
sphere("Room Grey Sphere", (16, 2, 0.7), 0.6, (0.5, 0.5, 0.5), 0.5)
box("Room Crate", (14.5, 2.8, 0.5), (0.8, 0.8, 0.8), rgb=(0.55, 0.35, 0.2), roughness=0.7)
# A finer probe volume for the room (phase 4.1): 0.75 m probes instead of the main volume's 2 m
entity("Room Probe Volume", "Cube", ((RX0 + RX1) / 2, 0, RH / 2), ((RX1 - RX0) / 2 + 0.5, (RY1 - RY0) / 2 + 0.5, RH / 2 + 0.3),
       [{"Type": "probe-volume", "InstanceId": guid("room/probevolume"),
         "Data": {"ProbeSpacing": 0.75, "Priority": 1, "BlendMargin": 0.5, "Enabled": True}}])
# A warm emissive panel on the north wall: lights the room through the bake (emissive surfaces as light sources)
box("Room Emissive Panel", (16.5, RY1 - 0.06, 2.4), (1.2, 0.08, 0.6), rgb=(1.0, 0.75, 0.45), roughness=0.4, collide=False,
    MaterialType=3, EmissiveStrength=6)
# The room's own reflection probe: box projected onto the walls, so the chrome sphere reflects the room (phase 3.2)
entity("Room Reflection Probe", "Cube", ((RX0 + RX1) / 2, 0, RH / 2), ((RX1 - RX0) / 2, (RY1 - RY0) / 2, RH / 2),
       [{"Type": "reflection-probe", "InstanceId": guid("room/probe"),
         "Data": {"BlendDistance": 0.5, "BoxProjection": True, "Priority": 1, "Resolution": 128, "Enabled": True}}])

# ---- Glass pane in front of a wall ----
box("Glass Wall", (-12, 0, 1.25), (0.25, 4, 2.5), rgb=(0.75, 0.3, 0.2), roughness=0.6)
box("Glass Pane", (-11, 0, 1.0), (0.04, 2.4, 1.6), rgb=(0.12, 0.14, 0.15), roughness=0.05, collide=False,
    MaterialType=5, IsTransparent=True, Opacity=0.35)

# ---- Character ----
entity("Y Bot", "PlayerYBot", (3, -3, 0), components=[material("Y Bot", (0.55, 0.55, 0.58), 0.45), ao_proxy("Y Bot")])
entity("Dragon", "StanfordDragon", (-3, -4, 0), (0.6, 0.6, 0.6), [material("Dragon", (0.8, 0.8, 0.8), 0.35), ao_proxy("Dragon")])

doc = {
    "Version": 2,
    "Name": "LookDev",
    "Entities": entities,
    "PointLights": [
        # A fluorescent tube on the room's ceiling: sphere radius + tube length give a wide, soft highlight
        {"Id": 900, "Name": "Room Tube Light", "Position": [17, 0, 3.6], "Radius": 7, "Color": [210, 225, 255, 255],
         "Intensity": 1.5, "CastShadows": True, "ShadowResolution": 512, "SourceRadius": 0.05, "SourceLength": 1.2,
         "Rotation": [0, 0, 0.7071068, 0.7071068]},
    ],
    "SpotLights": [
        # The flashlight: cookie, shadows, aimed at the room's back corner
        {"Id": 901, "Name": "Flashlight", "Position": [12, -3, 1.6], "Radius": 12, "Color": [255, 244, 225, 255],
         "Intensity": 60, "CastShadows": True, "ShadowResolution": 1024, "SourceRadius": 0.03,
         "Rotation": aim((12, -3, 1.6), (17.8, 2.5, 0.6)), "SpotAngle": 45, "InnerSpotAngle": 20,
         "CookiePath": "System/Cookies/flashlight.png"},
        {"Id": 902, "Name": "Checker Spot", "Position": [-4, 0.5, 4.5], "Radius": 9, "Color": [255, 250, 245, 255],
         "Intensity": 3, "CastShadows": True, "ShadowResolution": 512,
         "Rotation": aim((-4, 0.5, 4.5), (-4, 4, 1.2)), "SpotAngle": 60, "InnerSpotAngle": 40},
    ],
    "DirectionalLights": [
        {"Id": 903, "Name": "Sun", "Direction": [0.62, 0.25, -0.74], "Position": [0, 0, 10],
         "Color": [255, 247, 235, 255], "Intensity": 100, "CastShadows": True, "ShadowSize": 100,
         "ShadowDepth": 120, "ShadowResolution": 2048, "ShadowFiltering": 1,
         "CascadeCount": 4, "ShadowDistance": 120, "SplitLambda": 0.8},
    ],
    "Decals": [],
    "EnvironmentSample": {"Position": [0, -4, 2.0], "SpecularStrength": 1, "DiffuseStrength": 0.2, "AutoUpdate": True},
    "Environment": {"DayNightCycle": False, "TimeOfDay": 15, "FogDensity": 0.02, "FogStartDistance": 20,
                    "MaxAutoExposure": 3},
    "MainCamera": {
        "Position": [0, -14, 3.2], "Forward": [0.0, 0.985, -0.17], "FieldOfView": 0.9,
        "Components": [{"Type": "script-behaviour", "InstanceId": guid("camera/freecam"),
                        "Data": {"ScriptId": "freecam", "Enabled": True}}],
    },
}

with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(doc, f, indent=2)
print("wrote", OUT, len(entities), "gameobjects")
