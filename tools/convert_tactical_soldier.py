#!/usr/bin/env python3
"""Build the raw Tactical Soldier 02 GLB in Blender.

This script is deliberately run by Blender, not the system Python.  It reads a
*staged copy* of the user-supplied FBX, relinks its maps by basename, removes
the facial blend-shape payload that HGO does not use, creates HGO-local
locomotion loops, and writes an intermediate GLB.  ``finalize_tactical_soldier.py``
then turns every embedded image into PNG and applies the runtime validation.

Example (Windows):

    blender --factory-startup --disable-autoexec --background --python \
      tools/convert_tactical_soldier.py -- source.fbx texture-folder raw.glb

The source FBX is never modified.  Do not point this at an arbitrary rig: the
small, hand-authored loops below target the CC_Base skeleton supplied with
Tactical Soldier 02.
"""

from __future__ import annotations

import math
import os
import re
import sys
import hashlib
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Vector


FPS = 30
EXPECTED_SOURCE_SHA256 = "cccacbaa091a2406174c45abd55c33db69481ca5c6d72c8472b4f804383ee27d"

# Motion deliberately remains restrained.  It is presentation only: simulation,
# collision, aim, weapons and player movement continue to be owned by HGO.
MOTION_BONES = (
    "CC_Base_Hip",
    "CC_Base_Spine01",
    "CC_Base_Spine02",
    "CC_Base_L_Thigh",
    "CC_Base_R_Thigh",
    "CC_Base_L_Calf",
    "CC_Base_R_Calf",
    "CC_Base_L_Upperarm",
    "CC_Base_R_Upperarm",
    "CC_Base_L_Forearm",
    "CC_Base_R_Forearm",
)

# Character Creator exports its neutral shoulder line as a broad A-pose.  The
# supplied character contains no firearm or combat actions, so HGO needs its
# generated loops to carry both forearms forward into a low-ready posture.  X
# bends the CC_Base shoulder axes toward source-forward; Z keeps the elbows
# naturally inside the silhouette.  These are pose-space rotations measured
# against the imported CC_Base rest axes, not a transformation of the mesh or
# skeleton.
READY_POSE = {
    # The dominant hand stays on the pistol grip while the support palm has
    # enough lateral separation to land on the foregrip.  This is deliberately
    # not a symmetrical hands-together pose: a real two-hand carbine needs a
    # visible, chest-level contact point for each hand.
    "CC_Base_L_Upperarm": (0.80, 0.0, -1.10),
    "CC_Base_R_Upperarm": (0.80, 0.0, 1.10),
    "CC_Base_L_Forearm": (0.92, 0.0, -0.80),
    "CC_Base_R_Forearm": (0.92, 0.0, 0.80),
}


def usage() -> str:
    return (
        "usage: blender --background --python convert_tactical_soldier.py -- "
        "<source.fbx> <texture-directory> <output.raw.glb>"
    )


def args_after_separator() -> tuple[Path, Path, Path]:
    try:
        args = sys.argv[sys.argv.index("--") + 1:]
    except ValueError as exc:
        raise SystemExit(usage()) from exc
    if len(args) != 3:
        raise SystemExit(usage())
    source, texture_dir, output = (Path(arg).resolve() for arg in args)
    if not source.is_file():
        raise SystemExit(f"source FBX does not exist: {source}")
    if not texture_dir.is_dir():
        raise SystemExit(f"texture directory does not exist: {texture_dir}")
    output.parent.mkdir(parents=True, exist_ok=True)
    return source, texture_dir, output


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def import_fbx(source: Path) -> bpy.types.Object:
    bpy.ops.import_scene.fbx(
        filepath=str(source),
        use_custom_normals=True,
        use_image_search=False,
    )
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        names = ", ".join(obj.name for obj in armatures) or "none"
        raise RuntimeError(f"expected exactly one armature after FBX import, found {names}")
    armature = armatures[0]
    mesh_count = sum(obj.type == "MESH" for obj in bpy.context.scene.objects)
    if mesh_count == 0:
        raise RuntimeError("FBX import produced no mesh objects")
    return armature


def texture_lookup(texture_dir: Path) -> tuple[dict[str, Path], dict[str, Path]]:
    by_name: dict[str, Path] = {}
    by_stem: dict[str, Path] = {}
    for path in texture_dir.rglob("*"):
        if not path.is_file():
            continue
        key = path.name.casefold()
        by_name.setdefault(key, path)
        by_stem.setdefault(path.stem.casefold(), path)
    return by_name, by_stem


def relink_images(texture_dir: Path) -> None:
    by_name, by_stem = texture_lookup(texture_dir)
    linked = 0
    unresolved: list[str] = []

    for image in bpy.data.images:
        if image.source != "FILE":
            continue
        file_name = Path(bpy.path.abspath(image.filepath)).name
        match = by_name.get(file_name.casefold())
        if match is None:
            match = by_stem.get(Path(file_name).stem.casefold())
        if match is None:
            match = by_name.get(image.name.casefold())
        if match is None:
            match = by_stem.get(Path(image.name).stem.casefold())

        if match is not None:
            image.filepath = str(match)
            image.filepath_raw = str(match)
            try:
                image.reload()
            except RuntimeError as exc:
                unresolved.append(f"{image.name} ({exc})")
                continue
            linked += 1
        elif image.size[0] == 0 or image.size[1] == 0:
            unresolved.append(image.name)

    if unresolved:
        raise RuntimeError(
            "could not resolve image data for: " + ", ".join(sorted(unresolved)))
    print(f"relinked/verified {linked} external texture paths")


def image_kind(image: bpy.types.Image) -> str:
    name = (Path(image.filepath).name if image.filepath else image.name).casefold()
    if "normal" in name:
        return "normal"
    if any(token in name for token in ("metallic", "roughness", "ao", "opacity", "specular")):
        return "data"
    return "color"


def pack_png_images(staging_dir: Path) -> None:
    """Downscale editor maps, convert to PNG, and pack them into the GLB source.

    The game needs embedded PNGs because its raylib build cannot decode JPEG
    images inside GLB files.  Alpha is kept by Blender and checked again by the
    finalizer, rather than using an RGB-only texture helper.
    """
    staging_dir.mkdir(parents=True, exist_ok=True)
    saved = 0
    for index, image in enumerate(bpy.data.images):
        if image.source != "FILE" or image.size[0] <= 0 or image.size[1] <= 0:
            continue

        kind = image_kind(image)
        limit = 1024 if kind == "color" else 512
        width, height = image.size
        if max(width, height) > limit:
            factor = limit / max(width, height)
            image.scale(max(1, round(width * factor)), max(1, round(height * factor)))

        safe_stem = re.sub(r"[^a-zA-Z0-9_.-]+", "_", Path(image.name).stem) or f"image_{index:02d}"
        target = staging_dir / f"{index:02d}_{safe_stem}.png"
        image.filepath_raw = str(target)
        image.file_format = "PNG"
        image.save()
        image.pack()
        saved += 1
    if saved == 0:
        raise RuntimeError("no texture images could be prepared for GLB export")
    print(f"converted and packed {saved} images as PNG source files")


def prepare_meshes() -> int:
    triangles = 0
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue

        # Facial blend shapes add a large payload but are not used by HGO's world
        # actors.  Removing them has no gameplay or silhouette effect in the
        # supplied masked tactical character.
        if obj.data.shape_keys is not None:
            # ``bpy.data.shape_keys`` stopped exposing a collection ``remove``
            # method in Blender 5.2.  The object operator is the supported
            # version-stable route and removes the entire shape-key stack at
            # once (rather than iterating through 444 keys one by one).
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.shape_key_remove(all=True)

        # Raylib's glTF path is deliberately conservative.  HGO materials use
        # one UV set, so retain the active map only and make it TEXCOORD_0 on
        # export instead of shipping Reallusion's unused secondary sets.
        active = obj.data.uv_layers.active
        if active is not None:
            for layer in list(obj.data.uv_layers):
                if layer != active:
                    obj.data.uv_layers.remove(layer)
            for material in obj.data.materials:
                if material is None or material.node_tree is None:
                    continue
                for node in material.node_tree.nodes:
                    if node.type == "UVMAP":
                        node.uv_map = active.name

        triangles += sum(len(poly.vertices) - 2 for poly in obj.data.polygons if len(poly.vertices) >= 3)
    return triangles


def apply_runtime_units(armature: bpy.types.Object) -> None:
    """Bake FBX's 0.01 armature scale into rest data before authoring clips.

    The Character Creator FBX imports with an armature object scaled to 0.01.
    Blender draws that correctly, but Raylib evaluates glTF animation bones
    without that parent scale and stretches animated limbs across the map.  Bake
    the armature first, then its mesh children, so rest vertices, inverse-bind
    matrices and animated bone transforms all use the same metre-scale basis.
    """
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.context.view_layer.update()
    print("baked runtime units: armature scale="
          f"{tuple(round(value, 6) for value in armature.scale)}")


def ensure_pbr_materials() -> None:
    """Make imported FBX material maps explicit Principled-PBR inputs.

    Character Creator's materials use ordinary diffuse/normal/metallic map names.
    The FBX importer normally wires these already, but explicitly completing
    unlinked sockets makes the GLB result stable across Blender versions.
    """
    for material in bpy.data.materials:
        if material.node_tree is None:
            continue
        tree = material.node_tree
        nodes = tree.nodes
        links = tree.links
        principled = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
        output = next((node for node in nodes if node.type == "OUTPUT_MATERIAL"), None)
        if principled is None:
            principled = nodes.new("ShaderNodeBsdfPrincipled")
        if output is not None and not output.inputs["Surface"].is_linked:
            links.new(principled.outputs["BSDF"], output.inputs["Surface"])

        for node in (node for node in nodes if node.type == "TEX_IMAGE" and node.image is not None):
            name = image_kind(node.image)
            if name == "normal":
                node.image.colorspace_settings.name = "Non-Color"
                if not principled.inputs["Normal"].is_linked:
                    normal = nodes.new("ShaderNodeNormalMap")
                    links.new(node.outputs["Color"], normal.inputs["Color"])
                    links.new(normal.outputs["Normal"], principled.inputs["Normal"])
            elif name == "data":
                node.image.colorspace_settings.name = "Non-Color"
                file_name = Path(node.image.filepath or node.image.name).name.casefold()
                if "metallic" in file_name and not principled.inputs["Metallic"].is_linked:
                    links.new(node.outputs["Color"], principled.inputs["Metallic"])
                elif "roughness" in file_name and not principled.inputs["Roughness"].is_linked:
                    links.new(node.outputs["Color"], principled.inputs["Roughness"])
            elif not principled.inputs["Base Color"].is_linked:
                links.new(node.outputs["Color"], principled.inputs["Base Color"])


def reset_pose(armature: bpy.types.Object) -> None:
    for bone in armature.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def write_action(
    armature: bpy.types.Object,
    name: str,
    frames: list[tuple[int, dict[str, tuple[float, float, float]]]],
) -> bpy.types.Action:
    action = bpy.data.actions.new(name=name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    participating = sorted({bone for _, pose in frames for bone in pose})

    for frame, pose in frames:
        bpy.context.scene.frame_set(frame)
        reset_pose(armature)
        for bone_name in participating:
            bone = armature.pose.bones.get(bone_name)
            if bone is None:
                raise RuntimeError(f"required motion bone missing from armature: {bone_name}")
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = Euler(pose.get(bone_name, (0.0, 0.0, 0.0)), "XYZ")
            bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone_name)

    # Blender 5.2 moved F-curves behind action slots/layers.  The exporter
    # force-samples these actions anyway, so leaving Blender's default Bezier
    # interpolation is both API-stable and visually smoother at the gait turns.
    return action


def gait_pose(stride: float, arm: float, calf: float, spine: float) -> dict[str, tuple[float, float, float]]:
    """Return a readable, neutral tactical gait pose in the CC_Base bone frame."""
    deltas = {
        "CC_Base_Spine01": (spine, 0.0, 0.0),
        "CC_Base_Spine02": (spine * 0.45, 0.0, 0.0),
        "CC_Base_L_Thigh": (stride, 0.0, 0.0),
        "CC_Base_R_Thigh": (-stride, 0.0, 0.0),
        "CC_Base_L_Calf": (-calf if stride > 0 else calf * 0.22, 0.0, 0.0),
        "CC_Base_R_Calf": (calf if stride > 0 else -calf * 0.22, 0.0, 0.0),
        "CC_Base_L_Upperarm": (-arm, 0.0, 0.0),
        "CC_Base_R_Upperarm": (arm, 0.0, 0.0),
        "CC_Base_L_Forearm": (-arm * 0.22, 0.0, 0.0),
        "CC_Base_R_Forearm": (arm * 0.22, 0.0, 0.0),
    }
    return {
        name: tuple(READY_POSE.get(name, (0.0, 0.0, 0.0))[axis] + rotation[axis]
                    for axis in range(3))
        for name, rotation in deltas.items()
    }


def build_actions(armature: bpy.types.Object) -> list[bpy.types.Action]:
    missing = [name for name in MOTION_BONES if armature.pose.bones.get(name) is None]
    if missing:
        raise RuntimeError("Tactical Soldier rig is missing expected CC_Base bones: " + ", ".join(missing))

    neutral = {name: READY_POSE.get(name, (0.0, 0.0, 0.0)) for name in MOTION_BONES}
    idle_up = dict(neutral, **{
        "CC_Base_Spine01": (0.025, 0.0, 0.0),
        "CC_Base_Spine02": (0.012, 0.0, 0.0),
        "CC_Base_L_Upperarm": (READY_POSE["CC_Base_L_Upperarm"][0] - 0.018, 0.0,
            READY_POSE["CC_Base_L_Upperarm"][2]),
        "CC_Base_R_Upperarm": (READY_POSE["CC_Base_R_Upperarm"][0] + 0.018, 0.0,
            READY_POSE["CC_Base_R_Upperarm"][2]),
    })
    idle_down = dict(neutral, **{
        "CC_Base_Spine01": (-0.025, 0.0, 0.0),
        "CC_Base_Spine02": (-0.012, 0.0, 0.0),
        "CC_Base_L_Upperarm": (READY_POSE["CC_Base_L_Upperarm"][0] + 0.018, 0.0,
            READY_POSE["CC_Base_L_Upperarm"][2]),
        "CC_Base_R_Upperarm": (READY_POSE["CC_Base_R_Upperarm"][0] - 0.018, 0.0,
            READY_POSE["CC_Base_R_Upperarm"][2]),
    })

    actions = [
        write_action(armature, "Idle", [(1, neutral), (16, idle_up), (31, neutral), (46, idle_down), (61, neutral)]),
        write_action(armature, "Run", [
            (1, gait_pose(0.74, 0.07, 0.52, 0.08)),
            (9, gait_pose(0.0, 0.0, 0.0, 0.0)),
            (17, gait_pose(-0.74, -0.07, 0.52, -0.08)),
            (25, gait_pose(0.0, 0.0, 0.0, 0.0)),
            (33, gait_pose(0.74, 0.07, 0.52, 0.08)),
        ]),
        write_action(armature, "Reference", [(1, neutral), (2, neutral)]),
        write_action(armature, "Walk", [
            (1, gait_pose(0.42, 0.045, 0.34, 0.035)),
            (11, gait_pose(0.0, 0.0, 0.0, 0.0)),
            (21, gait_pose(-0.42, -0.045, 0.34, -0.035)),
            (31, gait_pose(0.0, 0.0, 0.0, 0.0)),
            (41, gait_pose(0.42, 0.045, 0.34, 0.035)),
        ]),
        write_action(armature, "Crouch", [
            (1, gait_pose(0.30, 0.025, 0.56, -0.14)),
            (16, gait_pose(-0.30, -0.025, 0.56, -0.14)),
            (31, gait_pose(0.30, 0.025, 0.56, -0.14)),
        ]),
    ]

    armature.animation_data.action = actions[0]
    bpy.context.scene.frame_set(1)
    return actions


def carbine_material(name: str, color: tuple[float, float, float], metallic: float) -> bpy.types.Material:
    """Create a compact, texture-free PBR material for the generated world gun."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes if material.node_tree is not None else None
    principled = nodes.get("Principled BSDF") if nodes is not None else None
    if principled is None:
        raise RuntimeError("could not create the Tactical Soldier carbine material")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = 0.48 if metallic > 0.5 else 0.68
    material.diffuse_color = (*color, 1.0)
    return material


def append_box(
    vertices: list[Vector],
    faces: list[tuple[int, int, int, int]],
    face_materials: list[int],
    transform: Matrix,
    center: tuple[float, float, float],
    size: tuple[float, float, float],
    material_index: int,
) -> None:
    """Append a sharp-edged cuboid, transforming it from gun-local to armature-rest space."""
    half = Vector(size) * 0.5
    origin = Vector(center)
    corners = (
        Vector((-half.x, -half.y, -half.z)), Vector((half.x, -half.y, -half.z)),
        Vector((half.x, half.y, -half.z)), Vector((-half.x, half.y, -half.z)),
        Vector((-half.x, -half.y, half.z)), Vector((half.x, -half.y, half.z)),
        Vector((half.x, half.y, half.z)), Vector((-half.x, half.y, half.z)),
    )
    start = len(vertices)
    vertices.extend(transform @ (origin + corner) for corner in corners)
    faces.extend((
        (start, start + 3, start + 2, start + 1),  # bottom
        (start + 4, start + 5, start + 6, start + 7),  # top
        (start, start + 1, start + 5, start + 4),  # -Y / muzzle side
        (start + 1, start + 2, start + 6, start + 5),  # +X
        (start + 2, start + 3, start + 7, start + 6),  # +Y / stock side
        (start + 3, start, start + 4, start + 7),  # -X
    ))
    face_materials.extend([material_index] * 6)


def append_tapered_box(
    vertices: list[Vector],
    faces: list[tuple[int, int, int, int]],
    face_materials: list[int],
    transform: Matrix,
    start_center: tuple[float, float, float],
    start_size: tuple[float, float, float],
    end_center: tuple[float, float, float],
    end_size: tuple[float, float, float],
    material_index: int,
) -> None:
    """Append a low-poly tapered prism between two Y-axis cross sections.

    A stock, handguard and magazine should read as deliberate forms rather than
    a sequence of equal-size cubes.  The centre-line stays on gun-local Y so
    the same skin-space transform used by the rest of the rifle remains valid.
    """
    start = Vector(start_center)
    end = Vector(end_center)
    start_half = Vector(start_size) * 0.5
    end_half = Vector(end_size) * 0.5

    def section(center: Vector, half: Vector) -> tuple[Vector, Vector, Vector, Vector]:
        return (
            Vector((center.x - half.x, center.y, center.z - half.z)),
            Vector((center.x + half.x, center.y, center.z - half.z)),
            Vector((center.x + half.x, center.y, center.z + half.z)),
            Vector((center.x - half.x, center.y, center.z + half.z)),
        )

    start_index = len(vertices)
    vertices.extend(transform @ point for point in (*section(start, start_half), *section(end, end_half)))
    faces.extend((
        (start_index, start_index + 3, start_index + 2, start_index + 1),
        (start_index + 4, start_index + 5, start_index + 6, start_index + 7),
        (start_index, start_index + 1, start_index + 5, start_index + 4),
        (start_index + 1, start_index + 2, start_index + 6, start_index + 5),
        (start_index + 2, start_index + 3, start_index + 7, start_index + 6),
        (start_index + 3, start_index, start_index + 4, start_index + 7),
    ))
    face_materials.extend([material_index] * 6)


def add_integrated_carbine(armature: bpy.types.Object) -> int:
    """Build a rigid, skinned low-ready carbine attached to the right hand.

    The supplied Tactical Soldier has no firearm mesh or weapon socket.  This
    creates a deliberately compact world-model (about 0.62 metres long) and assigns
    every vertex to the existing right-hand bone.  The mesh shares the character's
    armature/skin, so it follows the authored idle, walk, run and crouch clips
    instead of floating in renderer space.
    """
    hand_name = "CC_Base_R_Hand"
    hand_pose = armature.pose.bones.get(hand_name)
    hand_rest = armature.data.bones.get(hand_name)
    if hand_pose is None or hand_rest is None:
        raise RuntimeError(f"Tactical Soldier rig is missing required weapon hand: {hand_name}")

    # build_actions has selected Idle frame 1.  The midpoint between the wrist
    # and hand tip is a stable palm proxy on this CC_Base rig.
    bpy.context.view_layer.update()
    palm = hand_pose.matrix.translation + (hand_pose.matrix.to_3x3() @ Vector((0.0, 0.035, 0.0)))

    # Gun-local -Y is muzzle-forward.  Rotate it across the chest rather than
    # toward the camera, then give the muzzle a very small safe downward pitch.
    # This makes the rifle legible in a front review frame and brings the left
    # palm over the short foregrip instead of leaving a vertical slab silhouette.
    desired_pose = (
        Matrix.Translation(palm)
        @ Matrix.Rotation(math.radians(80.0), 4, "Z")
        @ Matrix.Rotation(math.radians(8.0), 4, "X")
    )

    # Skinning evaluates pose * inverse(rest) * vertex.  Pre-transform the rigid
    # mesh into rest space so it evaluates to desired_pose on frame 1, then follows
    # the right hand on every later frame.
    rest_space = hand_rest.matrix_local @ hand_pose.matrix.inverted() @ desired_pose
    # The source soldier is intentionally large in the runtime.  Scale the
    # carbine about its grip so its world-space length remains a compact
    # low-ready rifle rather than dominating the full character silhouette.
    gun_space = rest_space @ Matrix.Diagonal((0.65, 0.65, 0.65, 1.0))

    vertices: list[Vector] = []
    faces: list[tuple[int, int, int, int]] = []
    face_materials: list[int] = []
    # The local origin is the dominant-hand pistol grip, not model centre.  The
    # rifle is intentionally built from a small number of hard-edged forms, but
    # each one has a distinct job: a tapered stock, receiver, handguard, barrel,
    # curved magazine and sights.  This keeps it low-poly without reading as the
    # former single block proxy at actual gameplay distance.
    #
    # Scale is deliberately retained: local stock-to-muzzle distance is 0.95m,
    # multiplied by 0.65 at gun_space, or about 0.61m in the soldier's runtime
    # metre basis.

    # Tapered adjustable stock with a visible butt pad and cheek rest.
    append_tapered_box(vertices, faces, face_materials, gun_space,
        (0.0, 0.035, 0.105), (0.09, 0.0, 0.09),
        (0.0, 0.30, 0.10), (0.145, 0.0, 0.135), 3)
    append_box(vertices, faces, face_materials, gun_space, (0.0, 0.345, 0.10), (0.155, 0.035, 0.15), 2)
    append_box(vertices, faces, face_materials, gun_space, (0.0, 0.175, 0.175), (0.085, 0.20, 0.035), 3)
    append_box(vertices, faces, face_materials, gun_space, (0.0, 0.03, 0.125), (0.045, 0.13, 0.045), 1)

    # A slightly flared stamped receiver, upper rail and a tapered handguard.
    append_tapered_box(vertices, faces, face_materials, gun_space,
        (0.0, -0.255, 0.115), (0.12, 0.0, 0.125),
        (0.0, 0.045, 0.12), (0.135, 0.0, 0.165), 0)
    append_box(vertices, faces, face_materials, gun_space, (0.0, -0.10, 0.215), (0.075, 0.275, 0.028), 1)
    append_tapered_box(vertices, faces, face_materials, gun_space,
        (0.0, -0.255, 0.12), (0.115, 0.0, 0.12),
        (0.0, -0.42, 0.125), (0.085, 0.0, 0.085), 3)

    # Thin barrel and flared muzzle brake.  The muzzle only extends a modest
    # forearm past the support-hand foregrip in the combat-ready pose.
    append_box(vertices, faces, face_materials, gun_space, (0.0, -0.495, 0.15), (0.028, 0.15, 0.028), 1)
    append_box(vertices, faces, face_materials, gun_space, (0.0, -0.585, 0.15), (0.055, 0.03, 0.055), 0)

    # A rearward-sloped pistol grip and two angled magazine segments make the
    # silhouette read as a compact AK-pattern carbine rather than a rectangle.
    grip_space = gun_space @ Matrix.Translation(Vector((0.0, 0.025, -0.075))) @ Matrix.Rotation(math.radians(-18.0), 4, "X")
    append_tapered_box(vertices, faces, face_materials, grip_space,
        (0.0, -0.055, 0.045), (0.075, 0.0, 0.075),
        (0.0, 0.055, -0.045), (0.065, 0.0, 0.15), 2)
    magazine_upper = gun_space @ Matrix.Translation(Vector((0.0, -0.065, -0.12))) @ Matrix.Rotation(math.radians(-12.0), 4, "X")
    append_tapered_box(vertices, faces, face_materials, magazine_upper,
        (0.0, -0.045, 0.065), (0.08, 0.0, 0.075),
        (0.0, 0.045, -0.065), (0.072, 0.0, 0.14), 0)
    magazine_lower = gun_space @ Matrix.Translation(Vector((0.0, -0.11, -0.245))) @ Matrix.Rotation(math.radians(-24.0), 4, "X")
    append_tapered_box(vertices, faces, face_materials, magazine_lower,
        (0.0, -0.04, 0.055), (0.072, 0.0, 0.075),
        (0.0, 0.04, -0.055), (0.06, 0.0, 0.12), 0)

    # Support-hand vertical grip, compact optic, plus separate iron sights.
    foregrip_space = gun_space @ Matrix.Translation(Vector((0.0, -0.19, -0.045))) @ Matrix.Rotation(math.radians(-10.0), 4, "X")
    append_box(vertices, faces, face_materials, foregrip_space, (0.0, 0.0, 0.0), (0.07, 0.075, 0.14), 3)
    append_box(vertices, faces, face_materials, gun_space, (0.0, -0.13, 0.252), (0.065, 0.12, 0.055), 1)
    append_box(vertices, faces, face_materials, gun_space, (0.0, 0.00, 0.255), (0.075, 0.04, 0.06), 2)
    append_box(vertices, faces, face_materials, gun_space, (0.0, -0.405, 0.225), (0.045, 0.035, 0.09), 2)

    mesh = bpy.data.meshes.new("HGO_Tactical_Carbine_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    # Keep the compact gun dark, but not pure metallic-black: raylib's default
    # light has no environment reflection, so moderate diffuse contrast keeps
    # the receiver, barrel, furniture and iron-sight silhouette readable against
    # the black plate carrier.
    mesh.materials.append(carbine_material("HGO Carbine Receiver", (0.19, 0.24, 0.25), 0.10))
    mesh.materials.append(carbine_material("HGO Carbine Metal", (0.40, 0.45, 0.46), 0.18))
    mesh.materials.append(carbine_material("HGO Carbine Polymer", (0.055, 0.07, 0.075), 0.0))
    mesh.materials.append(carbine_material("HGO Carbine Furniture", (0.14, 0.17, 0.18), 0.02))
    for polygon, material_index in zip(mesh.polygons, face_materials):
        polygon.material_index = material_index
        polygon.use_smooth = False

    carbine = bpy.data.objects.new("HGO_Tactical_Carbine", mesh)
    bpy.context.collection.objects.link(carbine)
    carbine.parent = armature
    carbine.matrix_parent_inverse = armature.matrix_world.inverted()
    group = carbine.vertex_groups.new(name=hand_name)
    group.add(list(range(len(vertices))), 1.0, "REPLACE")
    modifier = carbine.modifiers.new("HGO_RightHand_Skin", "ARMATURE")
    modifier.object = armature
    carbine["hgo_integrated_carbine"] = True
    return len(faces) * 2


def export_glb(armature: bpy.types.Object, output: Path) -> None:
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 61
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        export_image_format="AUTO",
        export_materials="EXPORT",
        export_texcoords=True,
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
        export_use_gltfpack=False,
    )


def main() -> int:
    source, texture_dir, output = args_after_separator()
    source_hash = sha256(source)
    if source_hash != EXPECTED_SOURCE_SHA256:
        raise RuntimeError(
            "staged FBX SHA-256 does not match the audited Tactical Soldier 02 source: "
            f"{source_hash}")
    clear_scene()
    armature = import_fbx(source)
    relink_images(texture_dir)
    triangles = prepare_meshes()
    apply_runtime_units(armature)
    ensure_pbr_materials()
    png_stage = output.parent / "tactical-soldier-02-png-stage"
    pack_png_images(png_stage)

    # Source FBX only has an all-zero Default action.  The HGO actions are local,
    # intentionally modest locomotion loops rather than pretending that upload
    # contains animations it does not.
    armature.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    actions = build_actions(armature)
    carbine_triangles = add_integrated_carbine(armature)
    export_glb(armature, output)

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    print(f"TACTICAL_SOLDIER_EXPORT source_sha256={source_hash} meshes={len(meshes)} "
          f"triangles={triangles + carbine_triangles} carbine_triangles={carbine_triangles} "
          f"bones={len(armature.pose.bones)} actions={','.join(action.name for action in actions)} "
          f"output={output} bytes={output.stat().st_size}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"TACTICAL_SOLDIER_EXPORT_ERROR: {type(exc).__name__}: {exc}", file=sys.stderr)
        raise
