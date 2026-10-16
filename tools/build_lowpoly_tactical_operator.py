#!/usr/bin/env python3
"""Build HGO's original low-poly rounded capsule combatant.

This is a clean-room Blender recipe: it uses only procedural geometry and
flat PBR colours authored below.  It does not import, trace, texture, or
redistribute a third-party character or weapon asset.

The exported GLB contains a single lightweight armature plus the original
``Idle``, ``Walk``, ``Run``, ``Crouch``, ``Prone``, and ``Reference`` clips
expected by HGO's Raylib character loader. The weapon is a generic, compact
carbine rigidly skinned to the right hand; it is deliberately a world-model,
not a replacement for any first-person Gun Game view-model.

Run with Blender 5.2 or later:

    blender --factory-startup --disable-autoexec --background --python \
      tools/build_lowpoly_tactical_operator.py -- \
      --output assets/characters/hgo-lowpoly-tactical-operator.glb

Optionally write a local review render without placing a camera or light in the
exported asset:

    ... --preview C:/Temp/hgo-lowpoly-tactical-operator.png
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector


FPS = 30
REQUIRED_CLIPS = ("Idle", "Walk", "Run", "Crouch", "Prone", "Reference")


def arguments() -> argparse.Namespace:
    """Parse only arguments after Blender's ``--`` separator."""
    values = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", required=True, type=Path, help="destination .glb")
    parser.add_argument("--preview", type=Path, help="optional review PNG (not packed into the GLB)")
    return parser.parse_args(values)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.actions, bpy.data.materials, bpy.data.meshes, bpy.data.armatures):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def make_material(name: str, colour: tuple[float, float, float], roughness: float = 0.72,
                  metallic: float = 0.0) -> bpy.types.Material:
    """Create a small, texture-free material that remains legible in Raylib."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (*colour, 1.0)
    nodes = material.node_tree.nodes if material.node_tree is not None else None
    principled = nodes.get("Principled BSDF") if nodes is not None else None
    if principled is None:
        raise RuntimeError(f"could not initialise PBR material {name}")
    principled.inputs["Base Color"].default_value = (*colour, 1.0)
    if "Roughness" in principled.inputs:
        principled.inputs["Roughness"].default_value = roughness
    # Blender 4+ calls this socket Metallic IOR Level while older versions use
    # Metallic.  Keeping both names makes the repeatable recipe version-tolerant.
    for socket_name in ("Metallic IOR Level", "Metallic"):
        if socket_name in principled.inputs:
            principled.inputs[socket_name].default_value = metallic
            break
    return material


def material_set() -> dict[str, bpy.types.Material]:
    # The runtime character is intentionally a friendly, graphic low-poly
    # capsule instead of a realistic military mesh.  White body panels, a deep
    # blue-grey face panel and large coloured accessory accents stay legible in
    # HGO's intentionally simple world lighting without texture dependencies.
    return {
        "capsule": make_material("HGO Capsule White", (0.865, 0.890, 0.905), 0.76),
        "trim": make_material("HGO Capsule Trim", (0.525, 0.600, 0.630), 0.66),
        "fabric": make_material("HGO Capsule Soft Grey", (0.650, 0.700, 0.720), 0.78),
        "carrier": make_material("HGO Capsule Blue Grey", (0.180, 0.255, 0.295), 0.64),
        "plate": make_material("HGO Capsule Panel", (0.360, 0.455, 0.500), 0.58),
        "webbing": make_material("HGO Capsule Cool Accent", (0.140, 0.590, 0.700), 0.66),
        "pad": make_material("HGO Capsule Sole", (0.095, 0.145, 0.175), 0.70),
        "boot": make_material("HGO Capsule Sole Dark", (0.045, 0.070, 0.090), 0.76),
        "skin": make_material("HGO Capsule Face White", (0.940, 0.950, 0.955), 0.84),
        "visor": make_material("HGO Capsule Face Panel", (0.032, 0.100, 0.135), 0.28, 0.10),
        "metal": make_material("HGO Capsule Weapon Steel", (0.225, 0.325, 0.370), 0.42, 0.20),
        "polymer": make_material("HGO Capsule Weapon Dark", (0.055, 0.095, 0.120), 0.71),
        "furniture": make_material("HGO Capsule Weapon Accent", (0.155, 0.555, 0.660), 0.70),
        "accent": make_material("HGO Capsule Warm Accent", (0.990, 0.520, 0.115), 0.64),
    }


def create_armature() -> bpy.types.Object:
    """Create a compact conventional humanoid rig in a disciplined ready pose."""
    bpy.ops.object.armature_add(enter_editmode=True, location=(0.0, 0.0, 0.0))
    armature = bpy.context.object
    if armature is None:
        raise RuntimeError("could not create armature")
    armature.name = "HGO_LowPoly_Operator_Rig"
    armature.data.name = "HGO_LowPoly_Operator_Skeleton"
    edit = armature.data.edit_bones
    for bone in list(edit):
        edit.remove(bone)

    made: dict[str, bpy.types.EditBone] = {}

    def bone(name: str, head: tuple[float, float, float], tail: tuple[float, float, float],
             parent: str | None = None) -> None:
        entry = edit.new(name)
        entry.head = head
        entry.tail = tail
        if parent is not None:
            entry.parent = made[parent]
        made[name] = entry

    bone("root", (0.0, 0.0, 0.0), (0.0, 0.0, 0.88))
    bone("spine", (0.0, 0.0, 0.88), (0.0, 0.0, 1.24), "root")
    bone("chest", (0.0, 0.0, 1.24), (0.0, 0.0, 1.54), "spine")
    bone("neck", (0.0, 0.0, 1.54), (0.0, -0.015, 1.70), "chest")
    bone("head", (0.0, -0.015, 1.70), (0.0, -0.055, 1.95), "neck")

    # The neutral pose already places both hands around the rifle.  This avoids
    # a T-pose even when the player only sees an Idle frame at distance.
    bone("upper_arm.L", (-0.290, -0.005, 1.49), (-0.255, -0.175, 1.39), "chest")
    bone("forearm.L", (-0.255, -0.175, 1.39), (-0.145, -0.405, 1.30), "upper_arm.L")
    bone("hand.L", (-0.145, -0.405, 1.30), (-0.135, -0.500, 1.29), "forearm.L")
    bone("upper_arm.R", (0.290, -0.005, 1.49), (0.230, -0.165, 1.37), "chest")
    bone("forearm.R", (0.230, -0.165, 1.37), (0.105, -0.345, 1.25), "upper_arm.R")
    bone("hand.R", (0.105, -0.345, 1.25), (0.095, -0.440, 1.24), "forearm.R")

    for side, x in (("L", -0.155), ("R", 0.155)):
        bone(f"thigh.{side}", (x, 0.0, 0.90), (x, 0.01, 0.54), "root")
        bone(f"shin.{side}", (x, 0.01, 0.54), (x, -0.02, 0.17), f"thigh.{side}")
        bone(f"foot.{side}", (x, -0.02, 0.17), (x, -0.19, 0.08), f"shin.{side}")

    bpy.ops.object.mode_set(mode="OBJECT")
    armature.show_in_front = True
    for pose_bone in armature.pose.bones:
        pose_bone.rotation_mode = "XYZ"
    return armature


def transformed_primitive(obj: bpy.types.Object, transform: Matrix) -> bpy.types.Object:
    """Bake a primitive into armature-local coordinates before it is skinned."""
    obj.matrix_world = transform
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def skin_part(obj: bpy.types.Object, armature: bpy.types.Object, bone_name: str,
              material: bpy.types.Material) -> bpy.types.Object:
    """Rigidly weight one modular, low-poly component to a single body bone."""
    if obj.type != "MESH":
        raise RuntimeError(f"{obj.name} is not a mesh")
    obj.data.name = f"{obj.name}_Mesh"
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    group = obj.vertex_groups.new(name=bone_name)
    group.add([vertex.index for vertex in obj.data.vertices], 1.0, "REPLACE")
    modifier = obj.modifiers.new("HGO_Armature", "ARMATURE")
    modifier.object = armature
    obj.parent = armature
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    return obj


def cube(name: str, centre: Vector, size: tuple[float, float, float], armature: bpy.types.Object,
         bone_name: str, material: bpy.types.Material, rotation: Quaternion | None = None) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    obj = bpy.context.object
    if obj is None:
        raise RuntimeError(f"could not create {name}")
    obj.name = name
    orientation = (rotation or Quaternion((1.0, 0.0, 0.0, 0.0))).to_matrix().to_4x4()
    scale = Matrix.Diagonal(Vector((*size, 1.0)))
    transformed_primitive(obj, Matrix.Translation(centre) @ orientation @ scale)
    return skin_part(obj, armature, bone_name, material)


def icosphere(name: str, centre: Vector, size: tuple[float, float, float], armature: bpy.types.Object,
              bone_name: str, material: bpy.types.Material) -> bpy.types.Object:
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0)
    obj = bpy.context.object
    if obj is None:
        raise RuntimeError(f"could not create {name}")
    obj.name = name
    scale = Matrix.Diagonal(Vector((size[0] * 0.5, size[1] * 0.5, size[2] * 0.5, 1.0)))
    transformed_primitive(obj, Matrix.Translation(centre) @ scale)
    return skin_part(obj, armature, bone_name, material)


def cone_between(name: str, start: Vector, end: Vector, radius_start: float, radius_end: float,
                 vertices: int, armature: bpy.types.Object, bone_name: str,
                 material: bpy.types.Material) -> bpy.types.Object:
    """Create an economical tapered form between two rest-space points."""
    delta = end - start
    length = delta.length
    if length <= 0.0001:
        raise RuntimeError(f"{name} has no length")
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius_start, radius2=radius_end, depth=1.0)
    obj = bpy.context.object
    if obj is None:
        raise RuntimeError(f"could not create {name}")
    obj.name = name
    orientation = Vector((0.0, 0.0, 1.0)).rotation_difference(delta.normalized())
    transform = (
        Matrix.Translation((start + end) * 0.5)
        @ orientation.to_matrix().to_4x4()
        @ Matrix.Diagonal(Vector((1.0, 1.0, length, 1.0)))
    )
    # The primitive's local -Z/+Z radii map to start/end, so it stays genuinely
    # tapered and faceted rather than becoming an ellipse-scaled cylinder.
    transformed_primitive(obj, transform)
    return skin_part(obj, armature, bone_name, material)


def add_body(armature: bpy.types.Object, mats: dict[str, bpy.types.Material]) -> None:
    """Author a compact, readable tactical silhouette around the settled rig."""
    # Boots, shins, knees, thighs and segmented thigh panels.  The layered
    # profile deliberately widens toward the knee and boot: at a distance it
    # reads as a person in protective equipment rather than a column of boxes.
    for side, x in (("L", -0.155), ("R", 0.155)):
        cube(f"HGO_Boot_{side}", Vector((x, -0.105, 0.095)), (0.215, 0.365, 0.19), armature,
             f"foot.{side}", mats["boot"])
        cube(f"HGO_BootToe_{side}", Vector((x, -0.260, 0.105)), (0.205, 0.120, 0.12), armature,
             f"foot.{side}", mats["pad"])
        cube(f"HGO_BootAnkle_{side}", Vector((x, -0.035, 0.185)), (0.19, 0.20, 0.095), armature,
             f"shin.{side}", mats["webbing"])
        cone_between(f"HGO_Shin_{side}", Vector((x, -0.01, 0.20)), Vector((x, 0.0, 0.50)),
                     0.110, 0.140, 6, armature, f"shin.{side}", mats["fabric"])
        icosphere(f"HGO_Kneepad_{side}", Vector((x, -0.122, 0.545)), (0.225, 0.120, 0.205),
                  armature, f"shin.{side}", mats["pad"])
        cone_between(f"HGO_Thigh_{side}", Vector((x, 0.005, 0.56)), Vector((x, 0.0, 0.88)),
                     0.150, 0.180, 6, armature, f"thigh.{side}", mats["fabric"])
        cube(f"HGO_ThighPanel_{side}", Vector((x * 1.06, -0.125, 0.73)), (0.175, 0.060, 0.225),
             armature, f"thigh.{side}", mats["webbing"])
        cube(f"HGO_ThighStrap_{side}", Vector((x, -0.015, 0.655)), (0.285, 0.245, 0.040), armature,
             f"thigh.{side}", mats["carrier"])

    cube("HGO_Pelvis", Vector((0.0, 0.0, 0.93)), (0.535, 0.325, 0.23), armature, "root", mats["fabric"])
    cube("HGO_BeltFront", Vector((0.0, -0.185, 0.985)), (0.535, 0.055, 0.095), armature, "root", mats["webbing"])
    cube("HGO_BeltSide_L", Vector((-0.282, 0.0, 0.985)), (0.055, 0.315, 0.095), armature, "root", mats["webbing"])
    cube("HGO_BeltSide_R", Vector((0.282, 0.0, 0.985)), (0.055, 0.315, 0.095), armature, "root", mats["webbing"])
    cube("HGO_UtilityPouch_L", Vector((-0.18, -0.215, 0.93)), (0.155, 0.075, 0.165), armature, "root", mats["carrier"])
    cube("HGO_UtilityPouch_R", Vector((0.18, -0.215, 0.93)), (0.155, 0.075, 0.165), armature, "root", mats["webbing"])
    cube("HGO_DropPouch", Vector((0.0, 0.155, 0.89)), (0.265, 0.100, 0.14), armature, "root", mats["carrier"])

    cone_between("HGO_Torso", Vector((0.0, 0.0, 1.01)), Vector((0.0, 0.0, 1.50)),
                 0.340, 0.300, 6, armature, "spine", mats["fabric"])
    cube("HGO_PlateFront", Vector((0.0, -0.225, 1.34)), (0.500, 0.065, 0.395), armature, "chest", mats["carrier"])
    cube("HGO_PlateInset", Vector((0.0, -0.265, 1.39)), (0.365, 0.025, 0.245), armature, "chest", mats["plate"])
    cube("HGO_PlateLower", Vector((0.0, -0.227, 1.15)), (0.425, 0.060, 0.10), armature, "spine", mats["plate"])
    cube("HGO_CarrierSide_L", Vector((-0.320, -0.02, 1.34)), (0.060, 0.335, 0.355), armature, "chest", mats["carrier"])
    cube("HGO_CarrierSide_R", Vector((0.320, -0.02, 1.34)), (0.060, 0.335, 0.355), armature, "chest", mats["carrier"])
    for index, x in enumerate((-0.165, 0.0, 0.165)):
        cube(f"HGO_MagPouch_{index}", Vector((x, -0.292, 1.23)), (0.115, 0.065, 0.19), armature,
             "chest", mats["webbing"])
    cube("HGO_Radio", Vector((0.235, -0.295, 1.47)), (0.080, 0.065, 0.120), armature, "chest", mats["accent"])
    cube("HGO_RadioAntenna", Vector((0.250, -0.270, 1.59)), (0.020, 0.020, 0.16), armature, "chest", mats["accent"])
    cube("HGO_Backpack", Vector((0.0, 0.165, 1.31)), (0.405, 0.16, 0.40), armature, "chest", mats["carrier"])

    # Shoulder pads and explicitly bent, layered sleeves/forearms.
    for side, x in (("L", -1.0), ("R", 1.0)):
        label = "L" if x < 0 else "R"
        icosphere(f"HGO_Shoulder_{label}", Vector((x * 0.315, -0.015, 1.48)), (0.225, 0.20, 0.19),
                  armature, f"upper_arm.{label}", mats["pad"])
    cone_between("HGO_UpperArm_L", Vector((-0.270, -0.065, 1.46)), Vector((-0.250, -0.190, 1.39)),
                 0.105, 0.095, 6, armature, "upper_arm.L", mats["fabric"])
    cone_between("HGO_UpperArm_R", Vector((0.270, -0.055, 1.46)), Vector((0.225, -0.185, 1.37)),
                 0.105, 0.095, 6, armature, "upper_arm.R", mats["fabric"])
    icosphere("HGO_Elbow_L", Vector((-0.250, -0.190, 1.39)), (0.165, 0.145, 0.145), armature, "forearm.L", mats["pad"])
    icosphere("HGO_Elbow_R", Vector((0.225, -0.185, 1.37)), (0.165, 0.145, 0.145), armature, "forearm.R", mats["pad"])
    cone_between("HGO_Forearm_L", Vector((-0.240, -0.210, 1.37)), Vector((-0.145, -0.390, 1.30)),
                 0.09, 0.075, 6, armature, "forearm.L", mats["fabric"])
    cone_between("HGO_Forearm_R", Vector((0.215, -0.205, 1.35)), Vector((0.105, -0.330, 1.25)),
                 0.09, 0.075, 6, armature, "forearm.R", mats["fabric"])
    cube("HGO_Glove_L", Vector((-0.135, -0.465, 1.295)), (0.115, 0.14, 0.095), armature, "hand.L", mats["boot"])
    cube("HGO_Glove_R", Vector((0.100, -0.375, 1.245)), (0.110, 0.13, 0.095), armature, "hand.R", mats["boot"])

    cone_between("HGO_Neck", Vector((0.0, 0.0, 1.51)), Vector((0.0, -0.015, 1.65)),
                 0.115, 0.10, 6, armature, "neck", mats["fabric"])
    icosphere("HGO_Head", Vector((0.0, -0.045, 1.735)), (0.280, 0.270, 0.295), armature, "head", mats["skin"])
    icosphere("HGO_Helmet", Vector((0.0, 0.0, 1.835)), (0.370, 0.355, 0.245), armature, "head", mats["carrier"])
    cube("HGO_HelmetRear", Vector((0.0, 0.145, 1.79)), (0.315, 0.115, 0.17), armature, "head", mats["carrier"])
    cube("HGO_HelmetBrim", Vector((0.0, -0.195, 1.815)), (0.325, 0.11, 0.050), armature, "head", mats["carrier"])
    cube("HGO_Visor", Vector((0.0, -0.190, 1.745)), (0.255, 0.040, 0.105), armature, "head", mats["visor"])
    cube("HGO_VisorBand", Vector((0.0, -0.214, 1.745)), (0.115, 0.012, 0.022), armature, "head", mats["accent"])
    icosphere("HGO_Comms_L", Vector((-0.205, -0.010, 1.75)), (0.080, 0.085, 0.12), armature, "head", mats["pad"])
    icosphere("HGO_Comms_R", Vector((0.205, -0.010, 1.75)), (0.080, 0.085, 0.12), armature, "head", mats["pad"])


def add_capsule_body(armature: bpy.types.Object, mats: dict[str, bpy.types.Material]) -> None:
    """Build HGO's own friendly rounded capsule combatant.

    This is deliberately not a re-skinned human soldier.  Large faceted white
    capsule volumes, tiny separated feet, a simple face panel and two rounded
    arms make a readable opponent at gameplay distance.  The existing compact
    rig remains so the shipped movement clips and HGO character loader continue
    to have a stable, deterministic skeleton contract.
    """
    # Tiny feet and short legs keep the body silhouette playful and bean-like
    # while still leaving a believable grounded stance.
    for side, x in (("L", -0.175), ("R", 0.175)):
        icosphere(f"HGO_Capsule_Foot_{side}", Vector((x, -0.070, 0.115)),
                  (0.265, 0.360, 0.205), armature, f"foot.{side}", mats["pad"])
        cube(f"HGO_Capsule_Sole_{side}", Vector((x, -0.090, 0.060)),
             (0.270, 0.350, 0.070), armature, f"foot.{side}", mats["boot"])
        icosphere(f"HGO_Capsule_Leg_{side}", Vector((x, 0.010, 0.430)),
                  (0.255, 0.285, 0.460), armature, f"thigh.{side}", mats["capsule"])
        cube(f"HGO_Capsule_Knee_{side}", Vector((x, -0.145, 0.470)),
             (0.185, 0.045, 0.105), armature, f"shin.{side}", mats["trim"])

    # The overlapping ellipsoids create one soft, intentional character mass
    # without a human waist, armour vest or exposed neck.
    icosphere("HGO_Capsule_Body", Vector((0.0, 0.010, 1.000)),
              (0.800, 0.680, 1.180), armature, "spine", mats["capsule"])
    icosphere("HGO_Capsule_Head", Vector((0.0, -0.005, 1.630)),
              (0.740, 0.635, 0.650), armature, "head", mats["skin"])
    cube("HGO_Capsule_BellyPanel", Vector((0.0, -0.350, 1.075)),
         (0.405, 0.050, 0.315), armature, "chest", mats["carrier"])
    cube("HGO_Capsule_BellyLight", Vector((0.0, -0.382, 1.075)),
         (0.140, 0.014, 0.075), armature, "chest", mats["webbing"])
    cube("HGO_Capsule_FacePanel", Vector((0.0, -0.335, 1.635)),
         (0.430, 0.050, 0.185), armature, "head", mats["visor"])
    cube("HGO_Capsule_Eye_L", Vector((-0.105, -0.365, 1.645)),
         (0.090, 0.013, 0.038), armature, "head", mats["webbing"])
    cube("HGO_Capsule_Eye_R", Vector((0.105, -0.365, 1.645)),
         (0.090, 0.013, 0.038), armature, "head", mats["webbing"])
    cube("HGO_Capsule_FaceTrim", Vector((0.0, -0.365, 1.545)),
         (0.290, 0.013, 0.024), armature, "head", mats["accent"])

    # Rounded arms are deliberately placed at the authored rifle sockets. The
    # support hand lands on the foregrip and the trigger hand owns the weapon,
    # so the object cannot look stapled to the capsule's face or torso.
    for side, shoulder, elbow, hand in (
        ("L", Vector((-0.340, -0.020, 1.390)), Vector((-0.245, -0.180, 1.355)), Vector((-0.135, -0.470, 1.295))),
        ("R", Vector((0.340, -0.020, 1.390)), Vector((0.220, -0.175, 1.340)), Vector((0.100, -0.375, 1.245))),
    ):
        bone = f"upper_arm.{side}"
        forearm = f"forearm.{side}"
        hand_bone = f"hand.{side}"
        icosphere(f"HGO_Capsule_Shoulder_{side}", shoulder, (0.245, 0.235, 0.235), armature, bone, mats["capsule"])
        cone_between(f"HGO_Capsule_UpperArm_{side}", shoulder, elbow, 0.115, 0.105, 6, armature, bone, mats["capsule"])
        icosphere(f"HGO_Capsule_Elbow_{side}", elbow, (0.190, 0.180, 0.180), armature, forearm, mats["trim"])
        cone_between(f"HGO_Capsule_Forearm_{side}", elbow, hand, 0.105, 0.090, 6, armature, forearm, mats["capsule"])
        icosphere(f"HGO_Capsule_Hand_{side}", hand, (0.160, 0.145, 0.145), armature, hand_bone, mats["pad"])

    # Tall actor-specific silhouettes (cone, horn pair, halo, and top hat) are
    # attached by CharacterModels at draw time.  Keeping the shared skinned GLB
    # neutral means one compact mesh can safely render several recognisable bot
    # variants without duplicating materials, hitboxes or gameplay entities.


def add_ready_carbine(armature: bpy.types.Object, mats: dict[str, bpy.types.Material]) -> None:
    """Build a purpose-made, correctly scaled non-branded low-poly carbine.

    The muzzle sweeps from the trigger hand toward the support hand, across the
    operator's chest.  That intentional three-quarter low-ready pose avoids a
    head-on, foreshortened gun that would read as a dark rectangle or a prop
    hanging off the body.  It spans 0.84 m from butt pad to muzzle: visibly a
    rifle, but still physically believable beside a 2 m source character.
    """
    hand = "hand.R"
    grip = Vector((0.10, -0.375, 1.245))
    # Actor front is Blender -Y.  A strong -X component makes the world-model
    # read as a real carbine from the common front/three-quarter game camera;
    # the mild upward component lands the support hand on its handguard.
    forward = Vector((-0.88, -0.43, 0.18)).normalized()
    orientation = Vector((0.0, 1.0, 0.0)).rotation_difference(forward)

    def point(distance: float, height: float = 0.0, lateral: float = 0.0) -> Vector:
        # Local X/right and Z/up run through the same settled orientation as the receiver.
        basis = orientation.to_matrix()
        return grip + forward * distance + basis @ Vector((lateral, 0.0, height))

    def gun_cube(name: str, distance: float, height: float, dimensions: tuple[float, float, float],
                 material: bpy.types.Material, local_rotation: Quaternion | None = None) -> None:
        rotation = orientation if local_rotation is None else orientation @ local_rotation
        cube(name, point(distance, height), dimensions, armature, hand, material, rotation)

    # Receiver, upper rail, handguard, compact stock, thin barrel and muzzle.
    # Material breaks are intentionally large enough to survive Raylib's
    # minified world-model shading without relying on textures.
    gun_cube("HGO_Carbine_Receiver", 0.06, 0.018, (0.145, 0.27, 0.145), mats["metal"])
    gun_cube("HGO_Carbine_Rail", 0.06, 0.112, (0.080, 0.29, 0.036), mats["polymer"])
    gun_cube("HGO_Carbine_Handguard", 0.285, 0.025, (0.115, 0.25, 0.115), mats["furniture"])
    gun_cube("HGO_Carbine_HandguardBand", 0.345, 0.026, (0.125, 0.034, 0.125), mats["metal"])
    gun_cube("HGO_Carbine_Stock", -0.17, 0.018, (0.155, 0.22, 0.155), mats["polymer"])
    gun_cube("HGO_Carbine_ButtPad", -0.285, 0.012, (0.175, 0.048, 0.165), mats["pad"])
    cone_between("HGO_Carbine_Barrel", point(0.42, 0.032), point(0.57, 0.032), 0.025, 0.021,
                 6, armature, hand, mats["metal"])
    gun_cube("HGO_Carbine_Muzzle", 0.60, 0.032, (0.064, 0.060, 0.064), mats["metal"])

    # Distinct grip, two-piece magazine, foregrip and irons.  The left hand's
    # rest-space end lands at the foregrip, so this is a genuine two-hand pose
    # rather than a rifle floating in front of a single hand.
    gun_cube("HGO_Carbine_Grip", -0.005, -0.13, (0.080, 0.115, 0.185), mats["polymer"],
             Quaternion((1.0, 0.0, 0.0), math.radians(-18.0)))
    gun_cube("HGO_Carbine_MagUpper", 0.085, -0.13, (0.090, 0.105, 0.16), mats["metal"],
             Quaternion((1.0, 0.0, 0.0), math.radians(-12.0)))
    gun_cube("HGO_Carbine_MagLower", 0.11, -0.255, (0.080, 0.090, 0.155), mats["polymer"],
             Quaternion((1.0, 0.0, 0.0), math.radians(-24.0)))
    gun_cube("HGO_Carbine_Foregrip", 0.30, -0.092, (0.078, 0.078, 0.150), mats["furniture"],
             Quaternion((1.0, 0.0, 0.0), math.radians(-10.0)))
    gun_cube("HGO_Carbine_RearSight", -0.025, 0.152, (0.074, 0.046, 0.074), mats["pad"])
    gun_cube("HGO_Carbine_FrontSight", 0.41, 0.122, (0.048, 0.040, 0.092), mats["pad"])
    cone_between("HGO_Carbine_Sling", point(-0.22, 0.045), point(0.33, 0.055), 0.012, 0.012,
                 5, armature, hand, mats["webbing"])


def join_by_material(armature: bpy.types.Object) -> None:
    """Collapse rigid components into one skinned draw mesh per material.

    The authoring recipe stays modular for clarity, while the delivered GLB
    remains economical for a full bot match.  Every component uses the same
    armature and named vertex groups, so joining retains the intended rigid
    bone assignments while reducing the render mesh count from dozens to the
    small material palette.
    """
    groups: dict[str, list[bpy.types.Object]] = {}
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH" or not obj.data.materials:
            continue
        groups.setdefault(obj.data.materials[0].name, []).append(obj)
    for material_name, parts in sorted(groups.items()):
        if len(parts) <= 1:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        parts.sort(key=lambda item: item.name)
        for part in parts:
            part.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        bpy.ops.object.join()
        joined = bpy.context.object
        if joined is None or joined.type != "MESH":
            raise RuntimeError(f"could not merge low-poly material group {material_name}")
        joined.name = f"HGO_Operator_{material_name.replace(' ', '_')}"
        joined.data.name = f"{joined.name}_Mesh"
        joined.parent = armature
        joined.matrix_parent_inverse = armature.matrix_world.inverted()


def reset_pose(armature: bpy.types.Object) -> None:
    for pose_bone in armature.pose.bones:
        pose_bone.rotation_mode = "XYZ"
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)
        pose_bone.location = (0.0, 0.0, 0.0)


def write_action(armature: bpy.types.Object, name: str,
                 frames: list[tuple[int, dict[str, tuple[float, float, float]],
                                    dict[str, tuple[float, float, float]]]]) -> bpy.types.Action:
    """Key small original presentation motion; it never affects simulation data."""
    action = bpy.data.actions.new(name=name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    key_bones = sorted({bone for _, rotations, locations in frames for bone in (*rotations, *locations)})
    for frame, rotations, locations in frames:
        bpy.context.scene.frame_set(frame)
        reset_pose(armature)
        for bone_name in key_bones:
            pose_bone = armature.pose.bones.get(bone_name)
            if pose_bone is None:
                raise RuntimeError(f"animation requested missing bone: {bone_name}")
            pose_bone.rotation_euler = Euler(rotations.get(bone_name, (0.0, 0.0, 0.0)), "XYZ")
            pose_bone.location = locations.get(bone_name, (0.0, 0.0, 0.0))
            pose_bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone_name)
            pose_bone.keyframe_insert(data_path="location", frame=frame, group=bone_name)
    return action


def gait(left: float, right: float, chest: float, arm: float) -> dict[str, tuple[float, float, float]]:
    return {
        "thigh.L": (left, 0.0, 0.0),
        "thigh.R": (right, 0.0, 0.0),
        "shin.L": (-max(left, 0.0) * 0.64, 0.0, 0.0),
        "shin.R": (-max(right, 0.0) * 0.64, 0.0, 0.0),
        "spine": (chest, 0.0, 0.0),
        "chest": (chest * 0.45, 0.0, 0.0),
        # Minimal weapon-side arm motion keeps the hands planted on the
        # carbine rather than returning to an open swing/T-pose.
        "upper_arm.L": (arm * 0.45, 0.0, 0.0),
        "upper_arm.R": (-arm * 0.35, 0.0, 0.0),
    }


def build_actions(armature: bpy.types.Object) -> None:
    zero: dict[str, tuple[float, float, float]] = {}
    write_action(armature, "Idle", [
        (1, zero, zero),
        (16, {"spine": (0.022, 0.0, 0.0), "chest": (0.012, 0.0, 0.0), "head": (-0.009, 0.0, 0.0)}, zero),
        (31, zero, zero),
        (46, {"spine": (-0.018, 0.0, 0.0), "chest": (-0.010, 0.0, 0.0), "head": (0.008, 0.0, 0.0)}, zero),
        (61, zero, zero),
    ])
    write_action(armature, "Walk", [
        (1, gait(0.36, -0.36, 0.035, 0.045), zero),
        (11, gait(0.0, 0.0, 0.0, 0.0), zero),
        (21, gait(-0.36, 0.36, -0.035, -0.045), zero),
        (31, gait(0.0, 0.0, 0.0, 0.0), zero),
        (41, gait(0.36, -0.36, 0.035, 0.045), zero),
    ])
    write_action(armature, "Run", [
        (1, gait(0.62, -0.62, 0.065, 0.065), zero),
        (8, gait(0.0, 0.0, 0.0, 0.0), zero),
        (15, gait(-0.62, 0.62, -0.065, -0.065), zero),
        (22, gait(0.0, 0.0, 0.0, 0.0), zero),
        (29, gait(0.62, -0.62, 0.065, 0.065), zero),
    ])
    crouch_a = {
        "thigh.L": (0.62, 0.0, 0.0), "thigh.R": (0.62, 0.0, 0.0),
        "shin.L": (-0.70, 0.0, 0.0), "shin.R": (-0.70, 0.0, 0.0),
        "spine": (-0.13, 0.0, 0.0), "chest": (-0.08, 0.0, 0.0),
    }
    crouch_b = dict(crouch_a, **{"thigh.L": (0.49, 0.0, 0.0), "thigh.R": (0.72, 0.0, 0.0)})
    crouch_c = dict(crouch_a, **{"thigh.L": (0.72, 0.0, 0.0), "thigh.R": (0.49, 0.0, 0.0)})
    write_action(armature, "Crouch", [
        (1, crouch_a, {"root": (0.0, 0.0, -0.11)}),
        (16, crouch_b, {"root": (0.0, 0.0, -0.11)}),
        (31, crouch_c, {"root": (0.0, 0.0, -0.11)}),
        (46, crouch_a, {"root": (0.0, 0.0, -0.11)}),
    ])
    # A low, braced pose for renderer-side prone presentation.  The renderer
    # lays the whole actor along its forward axis, while this clip keeps the
    # knees tucked, torso low and head lifted over the original two-hand rifle
    # instead of showing an upright standing cycle in a prone hitbox.
    prone_a = {
        "thigh.L": (-0.42, 0.0, 0.08), "thigh.R": (-0.34, 0.0, -0.08),
        "shin.L": (0.94, 0.0, 0.04), "shin.R": (0.88, 0.0, -0.04),
        "spine": (-0.15, 0.0, 0.0), "chest": (-0.08, 0.0, 0.0),
        "head": (0.16, 0.0, 0.0),
    }
    prone_b = dict(prone_a, **{
        "thigh.L": (-0.38, 0.0, 0.10), "thigh.R": (-0.38, 0.0, -0.10),
        "head": (0.13, 0.0, 0.0),
    })
    write_action(armature, "Prone", [
        (1, prone_a, zero),
        (22, prone_b, zero),
        (43, prone_a, zero),
    ])
    # Give the reference clip an imperceptible authored head settle.  Blender's
    # exporter drops a completely empty static action, while HGO's review path
    # benefits from retaining a named reference clip beside runtime locomotion.
    write_action(armature, "Reference", [
        (1, zero, zero),
        (2, {"head": (0.0001, 0.0, 0.0)}, zero),
    ])
    armature.animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(1)


def bounds() -> tuple[Vector, Vector]:
    minimum = Vector((float("inf"), float("inf"), float("inf")))
    maximum = Vector((float("-inf"), float("-inf"), float("-inf")))
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            minimum.x, minimum.y, minimum.z = min(minimum.x, point.x), min(minimum.y, point.y), min(minimum.z, point.z)
            maximum.x, maximum.y, maximum.z = max(maximum.x, point.x), max(maximum.y, point.y), max(maximum.z, point.z)
    return minimum, maximum


def triangle_count() -> int:
    return sum(max(0, len(poly.vertices) - 2) for obj in bpy.context.scene.objects if obj.type == "MESH" for poly in obj.data.polygons)


def validate(armature: bpy.types.Object) -> None:
    names = {action.name for action in bpy.data.actions}
    missing = [name for name in REQUIRED_CLIPS if name not in names]
    if missing:
        raise RuntimeError("missing required character clips: " + ", ".join(missing))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("operator has no mesh parts")
    for obj in meshes:
        if not any(mod.type == "ARMATURE" and mod.object == armature for mod in obj.modifiers):
            raise RuntimeError(f"unskinned operator mesh: {obj.name}")
    minimum, maximum = bounds()
    dimensions = maximum - minimum
    if not 1.65 <= dimensions.z <= 2.15:
        raise RuntimeError(f"unexpected standing height: {dimensions.z:.3f} m")
    # The body itself is deliberately compact; the ready-position carbine adds
    # a safe forward projection, so a complete actor can be up to 1.60 m deep.
    if not 0.45 <= dimensions.y <= 1.60:
        raise RuntimeError(f"unexpected front-to-back depth: {dimensions.y:.3f} m")
    print("validation: "
          f"{len(meshes)} mesh parts, {triangle_count()} triangles, "
          f"bounds {dimensions.x:.3f} x {dimensions.y:.3f} x {dimensions.z:.3f} m, "
          f"clips {', '.join(REQUIRED_CLIPS)}")


def export_glb(armature: bpy.types.Object, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 61
    bpy.ops.object.select_all(action="DESELECT")
    for obj in bpy.context.scene.objects:
        if obj.type in {"ARMATURE", "MESH"}:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        export_image_format="AUTO",
        export_materials="EXPORT",
        export_texcoords=False,
        export_normals=True,
        export_tangents=False,
        export_yup=True,
        export_apply=False,
        export_animations=True,
        export_animation_mode="ACTIONS",
        export_nla_strips=False,
        export_force_sampling=True,
        export_frame_range=False,
        export_skins=True,
        export_influence_nb=4,
        export_all_influences=False,
        export_morph=False,
        export_morph_normal=False,
        export_morph_tangent=False,
        export_lights=False,
        export_cameras=False,
        export_try_sparse_sk=False,
        export_try_omit_sparse_sk=True,
    )


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_preview(destination: Path) -> None:
    """Render a review-only image after GLB export, leaving the GLB unpolluted."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.mesh.primitive_plane_add(size=10.0, location=(0.0, 0.0, 0.0))
    ground = bpy.context.object
    if ground is not None:
        ground.name = "Review Ground (not exported)"
        ground.data.materials.append(make_material("Review Ground", (0.10, 0.12, 0.13), 0.90))
    bpy.ops.object.light_add(type="AREA", location=(3.2, -4.0, 5.0))
    key = bpy.context.object
    if key is not None:
        key.data.energy = 950
        key.data.shape = "DISK"
        key.data.size = 4.0
        look_at(key, Vector((0.0, -0.05, 1.1)))
    bpy.ops.object.light_add(type="AREA", location=(-3.0, -2.0, 2.7))
    fill = bpy.context.object
    if fill is not None:
        fill.data.energy = 450
        fill.data.size = 3.0
        look_at(fill, Vector((0.0, -0.05, 1.2)))
    bpy.ops.object.camera_add(location=(3.15, -6.4, 2.35))
    camera = bpy.context.object
    if camera is None:
        raise RuntimeError("could not create preview camera")
    look_at(camera, Vector((0.0, -0.05, 1.04)))
    bpy.context.scene.camera = camera
    scene = bpy.context.scene
    # Blender 5.2 LTS retains the stable enum name even though the renderer is
    # commonly marketed as Eevee Next.
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 720
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(destination)
    scene.world.color = (0.075, 0.09, 0.11)
    bpy.ops.render.render(write_still=True)


def main() -> int:
    args = arguments()
    clear_scene()
    armature = create_armature()
    mats = material_set()
    add_capsule_body(armature, mats)
    add_ready_carbine(armature, mats)
    join_by_material(armature)
    build_actions(armature)
    validate(armature)
    export_glb(armature, args.output.resolve())
    if not args.output.is_file() or args.output.stat().st_size <= 0:
        raise RuntimeError("Blender did not produce a GLB")
    print(f"wrote {args.output.resolve()} ({args.output.stat().st_size / 1024:.1f} KiB)")
    if args.preview is not None:
        render_preview(args.preview.resolve())
        print(f"wrote review render {args.preview.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
