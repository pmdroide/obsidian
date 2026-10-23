"""Generates Engine/Content/Scenes/AudioTest.obsc, the audio sample scene (see Docs/markdown/Audio_Spatial_and_Reverb.md).

    python Tools/Scripts/generate_audio_test_scene.py

Five stations on one ground plane (Z up, metres; a Cube's Scale is its half size):
  Stereo   (0, 0)     a 2D left/right check, a 2D pan sweep and a 3D emitter orbiting the listening spot.
  Distance (60, -42)  a beacon on a lane with posts at its Min (3 m), 10 m and Max (25 m) distance.
  Reverb   (x < -40)  rooms along a path, each with a Reverb Zone: Bathroom, Concert Hall (with a Padded
                      Booth nested inside it) and a Sewer Tunnel. Outdoors has no reverb.
  Walls    (-20, 12)  a radio in a closed hut (door on its +X side) behind a freestanding wall: 2 walls, 1, 0.
  Doppler  (0, -60)   an engine sound racing past along y = -50.
Every wall has a Static collider with a Sound Material (Physics component): walls muffle 3D sounds by ray
casts against static colliders, each by its material. Hut and Concert Hall are Wood, Bathroom Plaster,
Padded Booth Fabric, Sewer Tunnel Concrete; the Walls station's free wall starts as Concrete (M cycles it).
The Main Camera runs Freecam + the audio-test script (Content/Scripts/AudioTestSceneScript.cs).
Sounds come from Tools/Scripts/generate_audio_test_sounds.py.
"""
import json
import os
import uuid

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Engine", "Content", "Scenes", "AudioTest.obsc")

entities = []
point_lights = []
_next_id = [1]


def new_id():
    i = _next_id[0]
    _next_id[0] += 1
    return i


def guid(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "obsidian/audiotest/" + name))


def entity(name, pos, scale, color, roughness=0.8, model="Cube", components=None):
    e = {
        "Id": new_id(),
        "Name": name,
        "ModelKey": model,
        "Position": [round(v, 4) for v in pos],
        "Scale": [round(v, 4) for v in scale],
        "Material": {"DiffuseColor": list(color), "Roughness": roughness},
    }
    if components:
        e["Components"] = components
    entities.append(e)
    return e


def audio(name, clip, *, volume=1.0, loop=True, play=True, spatial=True, min_d=2.0, max_d=25.0,
          rolloff="Linear", pan=0.0, reverb=1.0, spread=0.0, muffling=1.0, doppler=1.0):
    return {
        "Type": "audio",
        "InstanceId": guid("audio/" + name),
        "Data": {
            "Enabled": True,
            "ClipPath": clip,
            "Volume": volume,
            "Pitch": 1,
            "Loop": loop,
            "PlayOnStart": play,
            "Spatial": spatial,
            "Output": "Effects",
            "Pan": pan,
            "SpatialBlend": 1,
            "Spread": spread,
            "MinDistance": min_d,
            "MaxDistance": max_d,
            "Rolloff": rolloff,
            "ReverbSend": reverb,
            "WallMuffling": muffling,
            "DopplerLevel": doppler,
        },
    }


def reverb_zone(name, preset, blend=1.0, decay=1.0, wet=0.0, priority=0):
    return {
        "Type": "reverb-zone",
        "InstanceId": guid("reverb/" + name),
        "Data": {
            "Enabled": True,
            "Preset": preset,
            "Infinite": False,
            "BlendDistance": blend,
            "Weight": 1,
            "Priority": priority,
            "DecayScale": decay,
            "WetLevel": wet,
        },
    }


def static_body(name, sound="Default"):
    return {
        "Type": "physics",
        "InstanceId": guid("physics/" + name),
        "Data": {"BodyType": "Static", "Mass": 1.0, "Buoyancy": False, "BuoyancyStrength": 2.0, "WaterDrag": 3.0,
                 "IsTrigger": False, "FreezeRotation": False, "SoundMaterial": sound, "Enabled": True},
    }


def wall(name, pos, scale, color, roughness=0.85, sound="Default", extra=None):
    """A box with a Static collider: it blocks rays, so it muffles sounds behind it by its Sound Material."""
    return entity(name, pos, scale, color, roughness, components=[static_body(name, sound)] + (extra or []))


def light(name, pos, radius, intensity, color=(255, 236, 210, 255)):
    point_lights.append({
        "Id": 900 + len(point_lights),
        "Name": name,
        "Position": list(pos),
        "Radius": radius,
        "Color": list(color),
        "Intensity": intensity,
        "CastShadows": False,
    })


CLAP = "Audio/Test/clap.wav"
PULSE = "Audio/Test/pulse_loop.wav"
STEREO = "Audio/Test/stereo_check.wav"
ENGINE = "Audio/Test/engine_loop.wav"

GROUND = (0.34, 0.35, 0.36)
WALL_T = 0.1  # half wall thickness


def room(name, center, half, color, preset, door_half=0.8, door_height=2.2, ceiling=False, roughness=0.85,
         blend=1.0, decay=1.0, wet=0.0, zone=True, floor=None, door_side="-y", sound="Default"):
    """Four walls around an interior box (floor at z = 0), a doorway in the -Y (or +X) wall, the Reverb Zone inside."""
    cx, cy = center
    hx, hy, hz = half
    t = WALL_T
    wall(f"{name} - Back Wall", (cx, cy + hy + t, hz), (hx + 2 * t, t, hz), color, roughness, sound)
    wall(f"{name} - Left Wall", (cx - hx - t, cy, hz), (t, hy, hz), color, roughness, sound)
    lintel = hz - door_height / 2
    if door_side == "-y":
        wall(f"{name} - Right Wall", (cx + hx + t, cy, hz), (t, hy, hz), color, roughness, sound)
        # Front wall: two pieces either side of the doorway and a lintel above it.
        side = (hx + 2 * t - door_half) / 2
        wall(f"{name} - Front Wall L", (cx - door_half - side, cy - hy - t, hz), (side, t, hz), color, roughness, sound)
        wall(f"{name} - Front Wall R", (cx + door_half + side, cy - hy - t, hz), (side, t, hz), color, roughness, sound)
        if lintel > 0.01:
            wall(f"{name} - Lintel", (cx, cy - hy - t, door_height + lintel), (door_half, t, lintel), color, roughness, sound)
    else:
        wall(f"{name} - Front Wall", (cx, cy - hy - t, hz), (hx + 2 * t, t, hz), color, roughness, sound)
        # Right (+X) wall: two pieces either side of the doorway and a lintel above it.
        side = (hy - door_half) / 2
        wall(f"{name} - Right Wall S", (cx + hx + t, cy - door_half - side, hz), (t, side, hz), color, roughness, sound)
        wall(f"{name} - Right Wall N", (cx + hx + t, cy + door_half + side, hz), (t, side, hz), color, roughness, sound)
        if lintel > 0.01:
            wall(f"{name} - Lintel", (cx + hx + t, cy, door_height + lintel), (t, door_half, lintel), color, roughness, sound)
    if ceiling:
        wall(f"{name} - Ceiling", (cx, cy, 2 * hz + t), (hx + 2 * t, hy + 2 * t, t), color, roughness, sound)
    if floor:
        entity(f"{name} - Floor", (cx, cy, 0.01), (hx, hy, 0.01), floor, 0.3)
    if zone:
        entity(f"{name} - Reverb Zone", (cx, cy, hz), (hx, hy, hz), (0.3, 0.5, 0.9), 0.5,
               components=[reverb_zone(name, preset, blend, decay, wet)])


# ---------------------------------------------------------------- ground
entity("Ground", (-25, -5, -0.25), (110, 60, 0.25), GROUND, 0.9)
entity("Path", (-30, -3, 0.005), (95, 1.5, 0.005), (0.48, 0.45, 0.4), 0.85)

# ---------------------------------------------------------------- stereo station (x = 0)
entity("Stereo - Listen Here", (0, 0, 0.02), (0.6, 0.6, 0.02), (0.95, 0.85, 0.2), 0.4)
entity("Stereo - Left Marker", (-2.5, 5, 1.0), (0.35, 0.35, 1.0), (0.85, 0.2, 0.18), 0.5)
entity("Stereo - Right Marker", (2.5, 5, 1.0), (0.35, 0.35, 1.0), (0.18, 0.35, 0.9), 0.5)
# 2D sounds: their position is irrelevant; started by keys 1 / 2 (Play On Start off).
entity("Stereo Check (2D)", (-1.2, -2, 0.3), (0.3, 0.3, 0.3), (0.6, 0.6, 0.62), 0.6,
       components=[audio("stereo-check", STEREO, play=False, spatial=False, reverb=0)])
entity("Pan Sweep (2D)", (1.2, -2, 0.3), (0.3, 0.3, 0.3), (0.6, 0.6, 0.62), 0.6,
       components=[audio("pan-sweep", PULSE, volume=0.8, play=False, spatial=False, reverb=0)])
entity("Orbiting Emitter", (0, 4, 1.7), (0.3, 0.3, 0.3), (1.0, 0.75, 0.15), 0.3, model="IsoSphere",
       components=[audio("orbit", PULSE, volume=0.9, min_d=2, max_d=25, rolloff="Linear", reverb=0.3)])

# ---------------------------------------------------------------- distance station (x = 60)
BX = 60
entity("Distance - Lane", (BX, -20, 0.006), (1.2, 26, 0.006), (0.4, 0.4, 0.42), 0.85)
entity("Distance Beacon", (BX, 0, 1.5), (0.4, 0.4, 0.4), (0.95, 0.35, 0.1), 0.3, model="IsoSphere",
       components=[audio("beacon", PULSE, volume=1.0, min_d=3, max_d=25, rolloff="Linear", reverb=0.3)])
entity("Distance - Beacon Pole", (BX, 0, 0.55), (0.06, 0.06, 0.55), (0.3, 0.3, 0.3), 0.6)
for label, d, color in (("Min 3 m", 3, (0.2, 0.75, 0.3)), ("10 m", 10, (0.9, 0.8, 0.2)),
                        ("Max 25 m", 25, (0.85, 0.2, 0.18)), ("40 m", 40, (0.5, 0.5, 0.5))):
    for side in (-1, 1):
        entity(f"Distance - Post {label} {'L' if side < 0 else 'R'}", (BX + side * 1.6, -d, 0.75),
               (0.12, 0.12, 0.75), color, 0.5)

# ---------------------------------------------------------------- reverb rooms (x < -40), doors face -Y
room("Bathroom", (-48, 4), (2.0, 2.0, 1.4), (0.9, 0.92, 0.94), "Bathroom", door_half=0.6, roughness=0.15,
     floor=(0.75, 0.82, 0.86), sound="Plaster")
room("Concert Hall", (-70, 13), (10, 11, 5), (0.62, 0.52, 0.42), "ConcertHall", door_half=1.5, door_height=3.5,
     blend=1.5, sound="Wood")
# Nested inside the hall: the smaller box wins on a tie, so no priority needed.
room("Padded Booth", (-76, 19), (1.6, 1.6, 1.3), (0.25, 0.22, 0.3), "PaddedCell", door_half=0.6, roughness=1.0,
     blend=0.5, sound="Fabric")
room("Sewer Tunnel", (-92, 11), (1.6, 9, 1.6), (0.36, 0.38, 0.33), "SewerPipe", door_half=1.0, door_height=2.6,
     ceiling=True, blend=1.0, sound="Concrete")
light("Tunnel Lamp A", (-92, 6, 2.6), 7, 8)
light("Tunnel Lamp B", (-92, 15, 2.6), 7, 8)
# Sound sources inside the rooms (the script claps the one nearest to the camera).
entity("Bathroom - Echo Emitter", (-48, 5, 1.2), (0.15, 0.15, 0.15), (0.2, 0.8, 0.9), 0.3, model="IsoSphere",
       components=[audio("echo-bathroom", CLAP, loop=False, play=False, min_d=1.5, max_d=15)])
entity("Concert Hall - Echo Emitter", (-66, 20, 1.5), (0.2, 0.2, 0.2), (0.2, 0.8, 0.9), 0.3, model="IsoSphere",
       components=[audio("echo-hall", CLAP, loop=False, play=False, min_d=3, max_d=30)])
entity("Sewer Tunnel - Echo Emitter", (-92, 17, 1.2), (0.15, 0.15, 0.15), (0.2, 0.8, 0.9), 0.3, model="IsoSphere",
       components=[audio("echo-tunnel", CLAP, loop=False, play=False, min_d=2, max_d=20)])

# ---------------------------------------------------------------- walls station (-20, 12): muffling
# Radio in a closed hut whose door is on its +X side, behind a freestanding wall. From the station spot the
# sound passes two walls; walk round the free wall (one wall), then to the door (none).
room("Hut", (-20, 22), (2.0, 2.0, 1.3), (0.55, 0.42, 0.3), None, door_half=0.6, ceiling=True, zone=False,
     door_side="+x", sound="Wood")
# The script's M key cycles this wall's Sound Material and recolours it through its Material component.
wall("Walls - Free Wall", (-20, 17, 1.25), (3.5, 0.1, 1.25), (0.5, 0.5, 0.52), sound="Concrete", extra=[{
    "Type": "material",
    "InstanceId": guid("material/free-wall"),
    "Data": {"Red": 0.5, "Green": 0.5, "Blue": 0.52, "Roughness": 0.85, "Metallic": 0, "CastShadows": True, "Enabled": True},
}])
entity("Walls - Listen Here", (-20, 12, 0.02), (0.6, 0.6, 0.02), (0.95, 0.85, 0.2), 0.4)
entity("Hut - Radio", (-20.8, 22.5, 1.0), (0.25, 0.15, 0.15), (0.85, 0.2, 0.18), 0.4,
       components=[audio("radio", PULSE, volume=1.0, min_d=2, max_d=20, rolloff="Linear", reverb=0.3)])

# ---------------------------------------------------------------- doppler station (0, -60)
entity("Doppler - Track", (0, -50, 0.006), (85, 0.8, 0.006), (0.3, 0.3, 0.32), 0.8)
entity("Doppler - Listen Here", (0, -60, 0.02), (0.6, 0.6, 0.02), (0.95, 0.85, 0.2), 0.4)
entity("Doppler Racer", (-80, -50, 1.0), (0.5, 0.5, 0.5), (0.9, 0.15, 0.12), 0.3, model="IsoSphere",
       components=[audio("racer", ENGINE, volume=1.0, play=False, min_d=4, max_d=60, rolloff="Linear", reverb=0.2)])

scene = {
    "Version": 2,
    "Name": "AudioTest",
    "Entities": entities,
    "PointLights": point_lights,
    "DirectionalLights": [{
        "Id": 950,
        "Name": "Sun",
        "Direction": [0.45, 0.55, -0.7],
        "Position": [0, 0, 30],
        "Color": [255, 246, 232, 255],
        "Intensity": 100,
        "CastShadows": True,
        "ShadowSize": 60,
        "ShadowDepth": 120,
        "ShadowResolution": 2048,
        "ShadowFiltering": 1,
    }],
    "Decals": [],
    "EnvironmentSample": {"Position": [0, 0, 2], "SpecularStrength": 1, "DiffuseStrength": 0.3, "AutoUpdate": True},
    "Environment": {"DayNightCycle": False, "TimeOfDay": 15},
    "MainCamera": {
        "Position": [0, 0, 1.7],
        "Forward": [0, 1, -0.08],
        "FieldOfView": 1.2,
        "Components": [
            {"Type": "script-behaviour", "InstanceId": guid("freecam"),
             "Data": {"ScriptId": "freecam", "Enabled": True}},
            {"Type": "script-behaviour", "InstanceId": guid("audio-test"),
             "Data": {"ScriptId": "audio-test", "Enabled": True}},
        ],
    },
}

with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(scene, f, indent=2)
print("wrote", os.path.normpath(OUT), len(entities), "gameobjects")
