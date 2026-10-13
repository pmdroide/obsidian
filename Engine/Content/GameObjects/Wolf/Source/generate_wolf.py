"""
Generates the built-in Wolf model (Assets.Wolf): a low-poly skinned four-legged animal for the non-humanoid
ragdoll sample (Content/Scenes/AnimationTest.obsc, Ragdoll component > Skeleton = Auto/Quadruped).

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python generate_wolf.py

Output: Content/GameObjects/Wolf/Wolf.fbx (built by Content.mgcb with the SkinnedModelProcessor, RotationX=90).

Conventions:
    * Z is up, metres, facing -Y with its left side at +X (like the Mixamo characters once imported); the paws
      stand on Z = 0.
    * Bones: Pelvis (root) > Spine > Chest > Neck > Head, Pelvis > Tail1..Tail3, Pelvis > thigh/shin/foot.L/.R
      (hind legs), Chest > upper_arm/forearm/hand.L/.R (front legs). The legs use Rigify's human names on
      purpose: RagdollRig's Auto must still recognise a quadruped from its shape.
    * One action, "Idle" (2 s loop): breathing, a tail wag and a slow look around.

Deterministic: re-running produces the same file.
"""

import math
import os

import bpy
from mathutils import Vector

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Wolf.fbx"))

# ----------------------------------------------------------------------------------------------------------
#  Body shape: a skin-modifier graph (position, radius) smoothed by one subdivision level
# ----------------------------------------------------------------------------------------------------------

SPINE = [
    ((0, 0.47, 0.67), 0.115),   # rump
    ((0, 0.34, 0.66), 0.135),   # hips
    ((0, 0.08, 0.65), 0.14),    # belly
    ((0, -0.20, 0.68), 0.165),  # chest
    ((0, -0.36, 0.75), 0.115),  # base of the neck
    ((0, -0.46, 0.85), 0.085),  # neck
    ((0, -0.58, 0.92), 0.105),  # skull
    ((0, -0.73, 0.88), 0.06),   # muzzle
    ((0, -0.84, 0.86), 0.035),  # nose
]
TAIL = [
    ((0, 0.53, 0.67), 0.05),
    ((0, 0.65, 0.61), 0.06),
    ((0, 0.78, 0.53), 0.055),
    ((0, 0.90, 0.46), 0.04),
    ((0, 0.99, 0.41), 0.018),
]
# x is mirrored for the right side.
HIND_LEG = [
    ((0.10, 0.34, 0.60), 0.08),   # hip (joins the hips vertex)
    ((0.12, 0.27, 0.36), 0.055),  # stifle (knee), forward
    ((0.12, 0.42, 0.17), 0.038),  # hock, back
    ((0.12, 0.37, 0.04), 0.042),  # paw
    ((0.12, 0.31, 0.03), 0.04),   # toes
]
FRONT_LEG = [
    ((0.10, -0.24, 0.58), 0.075),  # shoulder (joins the chest vertex)
    ((0.12, -0.17, 0.37), 0.05),   # elbow, back
    ((0.12, -0.20, 0.17), 0.036),  # wrist
    ((0.12, -0.25, 0.04), 0.042),  # paw
    ((0.12, -0.31, 0.03), 0.04),   # toes
]
EAR = [((0.05, -0.56, 0.98), 0.03), ((0.06, -0.53, 1.08), 0.008)]

# ----------------------------------------------------------------------------------------------------------
#  Skeleton: (name, parent, head, tail)
# ----------------------------------------------------------------------------------------------------------

BONES = [
    ("Pelvis", None, (0, 0.38, 0.66), (0, 0.08, 0.66)),
    ("Spine", "Pelvis", (0, 0.08, 0.66), (0, -0.22, 0.69)),
    ("Chest", "Spine", (0, -0.22, 0.69), (0, -0.40, 0.78)),
    ("Neck", "Chest", (0, -0.40, 0.78), (0, -0.52, 0.90)),
    ("Head", "Neck", (0, -0.52, 0.90), (0, -0.84, 0.86)),
    ("Tail1", "Pelvis", (0, 0.50, 0.67), (0, 0.67, 0.60)),
    ("Tail2", "Tail1", (0, 0.67, 0.60), (0, 0.84, 0.50)),
    ("Tail3", "Tail2", (0, 0.84, 0.50), (0, 0.99, 0.41)),
]
for side, sx in (("L", 1), ("R", -1)):
    h = [(p[0] * sx, p[1], p[2]) for p, _ in HIND_LEG]
    f = [(p[0] * sx, p[1], p[2]) for p, _ in FRONT_LEG]
    BONES += [
        (f"thigh.{side}", "Pelvis", h[0], h[1]),
        (f"shin.{side}", f"thigh.{side}", h[1], h[2]),
        (f"foot.{side}", f"shin.{side}", h[2], h[4]),
        (f"upper_arm.{side}", "Chest", f[0], f[1]),
        (f"forearm.{side}", f"upper_arm.{side}", f[1], f[2]),
        (f"hand.{side}", f"forearm.{side}", f[2], f[4]),
    ]


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build_body():
    verts, radii, edges = [], [], []

    def add_chain(points, attach=None):
        start = len(verts)
        for i, (p, r) in enumerate(points):
            verts.append(p)
            radii.append(r)
            if i > 0:
                edges.append((start + i - 1, start + i))
        if attach is not None:
            edges.append((attach, start))
        return start

    add_chain(SPINE)
    hips, chest, skull = 1, 3, 6
    add_chain(TAIL, attach=0)
    for sx in (1, -1):
        mirror = lambda chain: [((p[0] * sx, p[1], p[2]), r) for p, r in chain]
        add_chain(mirror(HIND_LEG), attach=hips)
        add_chain(mirror(FRONT_LEG), attach=chest)
        add_chain(mirror(EAR), attach=skull)

    mesh = bpy.data.meshes.new("Wolf")
    mesh.from_pydata(verts, edges, [])
    obj = bpy.data.objects.new("Wolf", mesh)
    bpy.context.scene.collection.objects.link(obj)

    skin = obj.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    for i, data in enumerate(mesh.skin_vertices[0].data):
        data.radius = (radii[i], radii[i])
        data.use_root = i == 2  # the belly
    sub = obj.modifiers.new("Subdivision", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1

    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Skin")
    bpy.ops.object.modifier_apply(modifier="Subdivision")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    # Texture coordinates (the content pipeline's tangent frames need them).
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    for poly in mesh.polygons:
        poly.use_smooth = True

    material = bpy.data.materials.new("WolfFur")
    material.use_nodes = True
    bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.32, 0.29, 0.26, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    material.diffuse_color = (0.32, 0.29, 0.26, 1)
    mesh.materials.append(material)
    return obj


def build_armature():
    data = bpy.data.armatures.new("WolfRig")
    rig = bpy.data.objects.new("WolfRig", data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, parent, head, tail in BONES:
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0
        if parent:
            bone.parent = data.edit_bones[parent]
            bone.use_connect = Vector(data.edit_bones[parent].tail) == Vector(head)
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def bind(body, rig):
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    # Bone heat can leave vertices unweighted; give those to the nearest bone segment.
    bones = [(b.name, Vector(b.head_local), Vector(b.tail_local)) for b in rig.data.bones]

    def segment_distance(p, a, b):
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        return (a + ab * t - p).length

    groups = {g.name: g for g in body.vertex_groups}
    for name, _, _ in bones:
        if name not in groups:
            groups[name] = body.vertex_groups.new(name=name)
    fixed = 0
    for v in body.data.vertices:
        if sum(g.weight for g in v.groups) > 1e-4:
            continue
        nearest = min(bones, key=lambda bone: segment_distance(v.co, bone[1], bone[2]))
        groups[nearest[0]].add([v.index], 1.0, "REPLACE")
        fixed += 1
    print(f"generate_wolf: {len(body.data.vertices)} vertices, {fixed} weighted by distance")


def animate(rig):
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start, scene.frame_end = 0, 60
    rig.animation_data_create()
    action = bpy.data.actions.new("Idle")
    rig.animation_data.action = action

    pose = rig.pose.bones
    for bone in pose:
        bone.rotation_mode = "XYZ"

    def key(frame, angles):
        scene.frame_set(frame)
        for bone in pose:
            bone.rotation_euler = angles.get(bone.name, (0, 0, 0))
            bone.keyframe_insert("rotation_euler", frame=frame)

    # Bone-local axes: Y runs along each bone, so a tail wags about its local Z (roll is 0, X points to +X).
    for frame in range(0, 61, 5):
        t = frame / 60 * 2 * math.pi
        wag = math.radians(18) * math.sin(2 * t)
        breathe = math.radians(1.5) * math.sin(t)
        look = math.radians(10) * math.sin(t + 0.6)
        angles = {
            "Chest": (breathe, 0, 0),
            "Neck": (-breathe, 0, 0),
            "Head": (math.radians(3) * math.sin(t), 0, look),
            "Tail1": (0, 0, wag * 0.6),
            "Tail2": (0, 0, wag * 0.8),
            "Tail3": (0, 0, wag),
        }
        key(frame, angles)
    scene.frame_set(0)
    bpy.ops.object.mode_set(mode="OBJECT")


def export(body, rig):
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=OUT,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
        armature_nodetype="NULL",
        mesh_smooth_type="FACE",
        use_armature_deform_only=True,
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
    )
    print("generate_wolf: wrote", OUT)


clear_scene()
wolf = build_body()
skeleton = build_armature()
bind(wolf, skeleton)
animate(skeleton)
export(wolf, skeleton)
