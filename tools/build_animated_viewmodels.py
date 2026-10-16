#!/usr/bin/env python3
"""Build animated first-person viewmodel GLBs for Hood Global Offensive.

Source assets are user-provided FBX drops staged under ``.assets-scratch``:

* ``anim/1-AK74U.fbx``        — mixamo FPS rig (hoodie arms + hands) with the
  DRAW / IDLE / INSPEC / RELOAD1 / RELOAD2 / SHOOT actions, plus the original
  AK74U inverse-kinematic gun bones (Bone carries the gun body, carg/carg2 the
  magazines, recam the bolt, gatillo the trigger, seg the safety).
* ``ak/AKM.fbx``              — AKM, untextured base mesh (shaded with the
  project's own wood/metal/bakelite palette; the supplied PNGs are flat
  training maps and are deliberately unbound).
* ``awm/source/AWM.fbx``      — AWM sniper (uses its single supplied albedo).
* ``mp9/source/...fbx``       — MP9-SMG (flat named materials are mapped to a
  dark-polymer/steel palette).

For every weapon the script retargets the mesh onto the shared rig by rigid
skinning (weight 1): gun body -> ``Bone``, magazine parts -> ``carg``, trigger
-> ``gatillo``, charging/bolt parts -> ``recam``, safety -> ``seg``. The weapon
is scaled to its real-world length ratio against the AK74U and placed so its
trigger lands on the rig's ``gatillo`` head with the muzzle along the rig's
``-Y`` forward axis.

The exported GLBs keep the glTF Y-up contract inside the rig space; the game
places the whole rig, not the raw mesh, so the old ``+X`` muzzle contract no
longer applies to these files (see ``docs/BUILDING.md``).

Run with the bundled Blender:

    blender --factory-startup --disable-autoexec --background --python \
      tools/build_animated_viewmodels.py -- --scratch <dir> --out <assets\\weapons> \
      [--only rifle,smg,sniper,pistol] [--preview <dir>]

The script was used to produce ``viewmodel-rifle.glb``, ``viewmodel-sniper.glb``,
``viewmodel-smg.glb`` and ``viewmodel-pistol.glb`` from the staged drops; it is
kept so the builds can be reproduced without hand-edited GLBs.
"""

from __future__ import annotations

import json
import math
import re
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

RIG_GUN_TRIGGER = Vector((-0.0674, -0.2013, 1.5040))
RIG_AK74U_LENGTH = 0.658
AK74U_REAL_MM = 730.0

WEAPONS: dict[str, dict] = {
    "rifle": {
        "source": "ak/**/*.fbx",
        "real_mm": 880.0,
        "rotate_z": -90.0,
        "trigger": (4.435, -0.380, 0.127),
        "shift": (0.0, -0.030, -0.010),
        "groups": [
            (r"^bm_magazine", "carg"),
            (r"^bm_trigger_low$", "gatillo"),
            (r"^m_safetyswitch", "seg"),
        ],
        "palette": "akm",
    },
    "sniper": {
        "source": "awm/**/*.fbx",
        "real_mm": 1180.0,
        "rotate_z": -90.0,
        "trigger": (-0.020, 0.002, 0.140),
        "shift": (0.0, -0.040, -0.012),
        "groups": [],
        "palette": "keep",
    },
    "smg": {
        "source": "mp9/**/*.fbx",
        "real_mm": 523.0,
        "rotate_z": -90.0,
        "trigger": (2.124, 0.000, 3.252),
        "shift": (0.0, -0.022, -0.012),
        "groups": [
            (r"^Cube\.002$", "carg"),
            (r"^Cylinder\.002$", "carg"),
            (r"^Cube\.012$", "gatillo"),
            (r"^Cylinder\.008$", "recam"),
        ],
        "palette": "mp9",
    },
    "pistol": {
        "source": str(Path(__file__).resolve().parent.parent / "assets" / "weapons" / "glock17.glb"),
        "real_mm": 186.0,
        "rotate_z": 90.0,
        "trigger": (0.029, 0.0, -0.098),
        "shift": (0.0, -0.020, -0.005),
        "groups": [],
        "palette": "keep",
    },
}

AKM_WOOD = ("l_stockWood_low", "r_woodLower_low", "ur_woodUpper_low")
AKM_GRIP = ("bl_grip_low",)
AKM_MAG = ("bm_magazine",)
AKM_STEEL = ("m_safetyswitch", "r_button", "l_button", "ls2_button", "rs2_button", "m_button", "screw", "trigger", "belt", "rotor")

MP9_COLORS = {
    "Main Color": ((0.13, 0.13, 0.14, 1.0), 0.0, 0.60),
    "Secondary": ((0.21, 0.21, 0.23, 1.0), 0.0, 0.55),
    "Screw": ((0.45, 0.45, 0.48, 1.0), 1.0, 0.35),
    "Rail": ((0.09, 0.09, 0.10, 1.0), 0.8, 0.45),
    "Barrel": ((0.08, 0.08, 0.09, 1.0), 1.0, 0.35),
    "Magazine": ((0.16, 0.16, 0.17, 1.0), 0.0, 0.60),
    "Ammo": ((0.55, 0.38, 0.13, 1.0), 1.0, 0.35),
    "Ammo (2)": ((0.55, 0.38, 0.13, 1.0), 1.0, 0.35),
    "RDS": ((0.05, 0.05, 0.05, 1.0), 0.2, 0.40),
    "Transparent": ((0.45, 0.62, 0.75, 0.30), 0.0, 0.10),
    "Butt Stock": ((0.12, 0.12, 0.13, 1.0), 0.0, 0.55),
    "Ejection Port": ((0.30, 0.30, 0.32, 1.0), 1.0, 0.35),
}

KEEP_ANIMATIONS = ("DRAW", "IDLE", "INSPEC", "RELOAD1", "RELOAD2", "SHOOT")


def parse_args() -> dict:
    args = sys.argv[sys.argv.index("--") + 1 :]
    parsed: dict = {"only": None, "preview": None}
    i = 0
    while i < len(args):
        if args[i] == "--scratch":
            parsed["scratch"] = Path(args[i + 1]).resolve()
            i += 2
        elif args[i] == "--out":
            parsed["out"] = Path(args[i + 1]).resolve()
            i += 2
        elif args[i] == "--only":
            parsed["only"] = args[i + 1].split(",")
            i += 2
        elif args[i] == "--preview":
            parsed["preview"] = Path(args[i + 1]).resolve()
            i += 2
        else:
            raise SystemExit(f"unknown argument {args[i]}")
    if "scratch" not in parsed or "out" not in parsed:
        raise SystemExit("expected: -- --scratch <dir> --out <dir> [--only a,b] [--preview <dir>]")
    parsed["out"].mkdir(parents=True, exist_ok=True)
    if parsed["preview"] is not None:
        parsed["preview"].mkdir(parents=True, exist_ok=True)
    return parsed


def norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower())


def relink_images(textures_dir: Path) -> None:
    files = [p for p in textures_dir.iterdir() if p.is_file()]
    for img in bpy.data.images:
        if img.source == "GENERATED":
            continue
        target = norm(Path(img.name).stem)
        for f in files:
            fn = norm(f.stem)
            if target and (target in fn or fn in target):
                img.filepath = str(f)
                img.reload()
                break


def downscale_images(limit: int = 1024) -> None:
    for img in bpy.data.images:
        if img.source == "GENERATED" or img.size[0] == 0:
            continue
        if max(img.size) > limit:
            img.scale(limit, limit)
        try:
            img.pack()
        except RuntimeError:
            pass


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def object_set() -> set:
    return {o.name for o in bpy.data.objects}


def new_objects(before: set) -> list:
    return [bpy.data.objects[n] for n in object_set() - before]


def combined_bounds(objs) -> tuple[Vector, Vector]:
    mn = Vector((1e18, 1e18, 1e18))
    mx = Vector((-1e18, -1e18, -1e18))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], w[i])
                mx[i] = max(mx[i], w[i])
    return mn, mx


def select_only(objs) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def transform_apply(objs) -> None:
    select_only(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def make_material(name: str, color, metallic: float, roughness: float, cache: dict):
    if name in cache:
        return cache[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        if "Base Color" in bsdf.inputs:
            bsdf.inputs["Base Color"].default_value = color
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = metallic
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = roughness
        if color[3] < 1.0:
            if "Alpha" in bsdf.inputs:
                bsdf.inputs["Alpha"].default_value = color[3]
            mat.blend_method = "BLEND"
    mat.diffuse_color = color
    cache[name] = mat
    return mat


def akm_material_for(obj_name: str, cache: dict):
    if any(obj_name.startswith(p) for p in AKM_MAG):
        return make_material("akm-magazine", (0.16, 0.15, 0.15, 1.0), 0.7, 0.45, cache)
    if any(obj_name.startswith(p) for p in AKM_WOOD):
        return make_material("akm-wood", (0.29, 0.16, 0.075, 1.0), 0.0, 0.55, cache)
    if any(obj_name.startswith(p) for p in AKM_GRIP):
        return make_material("akm-grip", (0.24, 0.11, 0.06, 1.0), 0.0, 0.6, cache)
    if any(p in obj_name for p in AKM_STEEL):
        return make_material("akm-steel", (0.45, 0.45, 0.48, 1.0), 1.0, 0.35, cache)
    return make_material("akm-metal", (0.10, 0.10, 0.11, 1.0), 0.85, 0.45, cache)


def apply_palette(kind: str, objs, cache: dict) -> None:
    if kind == "akm":
        for o in objs:
            o.data.materials.clear()
            o.data.materials.append(akm_material_for(o.name, cache))
    elif kind == "mp9":
        for o in objs:
            for slot in range(len(o.material_slots)):
                old = o.material_slots[slot].material
                spec = MP9_COLORS.get(old.name if old else "", ((0.15, 0.15, 0.16, 1.0), 0.0, 0.6))
                o.material_slots[slot].material = make_material(f"mp9-{old.name if old else slot}", *spec, cache)


def group_for(name: str, rules: list) -> str:
    for pattern, group in rules:
        if re.search(pattern, name):
            return group
    return "Bone"


def mount_weapon(arm, spec: dict, scratch: Path, scratch_glob: str) -> list:
    before = object_set()
    if "*" in scratch_glob:
        sources = sorted(scratch.glob(scratch_glob))
    else:
        sources = [Path(scratch_glob)]
    if not sources or not sources[0].is_file():
        raise SystemExit(f"no source found for {scratch_glob}")
    ext = sources[0].suffix.lower()
    if ext == ".glb":
        bpy.ops.import_scene.gltf(filepath=str(sources[0]))
    else:
        bpy.ops.import_scene.fbx(filepath=str(sources[0]))
    gun_objs = [o for o in new_objects(before) if o.type == "MESH"]
    if not gun_objs:
        raise SystemExit("no meshes imported")

    mn, mx = combined_bounds(gun_objs)
    length_src = mx.x - mn.x

    trigger_src = Vector(spec["trigger"]) if spec["trigger"] is not None else Vector(
        (mn.x + 0.45 * length_src, (mn.y + mx.y) / 2.0, mn.z + 0.40 * (mx.z - mn.z))
    )

    target_length = RIG_AK74U_LENGTH * (spec["real_mm"] / AK74U_REAL_MM)
    scale = target_length / max(length_src, 1e-6)
    rot = Matrix.Rotation(math.radians(spec["rotate_z"]), 4, "Z")
    rs = Matrix.Scale(scale, 4) @ rot
    translation = (RIG_GUN_TRIGGER + Vector(spec["shift"])) - (rs @ trigger_src)
    mat = Matrix.Translation(translation) @ rs

    # The rig's bind/rest pose parks the weapon bones at the floor; the
    # animations carry them into the hands. Skinning deforms by
    # pose x rest^-1, so verts must be authored in rest space: map the
    # target placement through rest x pose^-1 (pose sampled at IDLE frame 1,
    # the same reference used for RIG_GUN_TRIGGER and the -Y muzzle frame).
    bpy.context.scene.frame_set(1)
    for o in gun_objs:
        group = group_for(o.name, spec["groups"])
        rest = arm.data.bones[group].matrix_local
        pose = arm.pose.bones[group].matrix
        o.matrix_world = (rest @ pose.inverted()) @ mat @ o.matrix_world
    transform_apply(gun_objs)

    palette_cache: dict = {}
    apply_palette(spec["palette"], gun_objs, palette_cache)

    rules = spec["groups"]
    for o in gun_objs:
        group = group_for(o.name, rules)
        vg = o.vertex_groups.new(name=group)
        vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")

    select_only(gun_objs)
    bpy.ops.object.join()
    gun = bpy.context.view_layer.objects.active
    gun.name = f"viewmodel-{spec['name']}-gun"

    gun.parent = arm
    gun.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = gun.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return [gun]


def rename_actions() -> None:
    for act in list(bpy.data.actions):
        short = act.name.split("|")[-1]
        if short in KEEP_ANIMATIONS:
            act.name = short
        else:
            bpy.data.actions.remove(act)


def preview_renders(name: str, outdir: Path, actions: list[tuple[str, int, str]]) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 640

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    mn, mx = combined_bounds(meshes)
    centre = Vector((0.0, (mn.y + mx.y) / 2.0, 1.35))
    size = max(mx.y - mn.y, 0.9) * 1.25

    cam_data = bpy.data.cameras.new("preview")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size
    cam_data.clip_end = 100.0
    cam = bpy.data.objects.new("preview", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    ad = arm.animation_data
    for action_name, frame, view in actions:
        act = bpy.data.actions.get(action_name)
        if act is None:
            continue
        ad.action = act
        if hasattr(ad, "action_slot") and len(act.slots):
            ad.action_slot = act.slots[0]
        scene.frame_set(frame)
        if view == "side":
            cam.location = centre + Vector((10.0, 0.0, 0.0))
            cam.rotation_euler = (math.pi / 2.0, 0.0, math.pi / 2.0)
        else:
            cam.location = centre + Vector((0.0, 0.0, 10.0))
            cam.rotation_euler = (0.0, 0.0, 0.0)
        scene.render.filepath = str(outdir / f"weapon_{name}_{action_name.lower()}_f{frame:04d}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print("  preview", scene.render.filepath)

    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data)


def export_glb(path: Path) -> None:
    wanted = {
        "filepath": str(path),
        "export_format": "GLB",
        "export_yup": True,
        "export_animations": True,
        "export_animation_mode": "ACTIONS",
        "export_optimize_animation_size": False,
        "export_skins": True,
        "export_morph": False,
        "export_cameras": False,
        "export_lights": False,
        "export_extras": False,
        "export_apply": False,
        "export_image_format": "AUTO",
    }
    props = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    kwargs = {k: v for k, v in wanted.items() if k in props}
    dropped = sorted(set(wanted) - set(kwargs))
    if dropped:
        print(f"  note: exporter ignored unsupported options {dropped}")
    bpy.ops.export_scene.gltf(**kwargs)


def inspect_glb(path: Path) -> None:
    data = path.read_bytes()
    if data[:4] != b"glTF":
        print(f"  {path.name}: not a GLB?!")
        return
    length = struct.unpack_from("<I", data, 8)[0]
    offset, chunks = 12, {}
    while offset < length:
        clen, ctype = struct.unpack_from("<II", data, offset)
        chunks[ctype] = data[offset + 8 : offset + 8 + clen]
        offset += 8 + clen
    gltf = json.loads(chunks[0x4E4F534A].decode("utf-8"))
    images = gltf.get("images", [])
    image_bytes = 0
    for img in images:
        bv = img.get("bufferView")
        if bv is not None:
            image_bytes += gltf["bufferViews"][bv].get("byteLength", 0)
    anims = []
    for a in gltf.get("animations", []):
        times = set()
        for ch in a.get("samplers", []):
            acc = gltf["accessors"][ch["input"]]
            times.add(acc.get("max", [0])[0])
        dur = max(times) if times else 0.0
        anims.append(f"{a.get('name', '?')} ({dur:.3f}s, {len(a.get('channels', []))}ch)")
    print(f"  {path.name}: {len(data) / 1e6:.2f}MB meshes={len(gltf.get('meshes', []))} images={len(images)} ({image_bytes / 1e6:.2f}MB) anims=[{', '.join(anims)}]")


def build_base_rig(scratch: Path) -> object:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.render.fps_base = 1.0

    fbx = next(scratch.glob("anim/**/*.fbx"))
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    relink_images(scratch / "anim" / "textures")

    gun = bpy.data.objects.get("Gun")
    if gun is not None:
        bpy.data.objects.remove(gun, do_unlink=True)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    arm.name = "HgoRig"

    rename_actions()
    idles = bpy.data.actions.get("IDLE")
    if idles is not None:
        arm.animation_data.action = idles
        if hasattr(arm.animation_data, "action_slot") and len(idles.slots):
            arm.animation_data.action_slot = idles.slots[0]
    return arm


def main() -> None:
    args = parse_args()
    only = args["only"]
    for name, spec in WEAPONS.items():
        if only is not None and name not in only:
            continue
        spec = dict(spec, name=name)
        print(f"== building {name} ==")
        arm = build_base_rig(args["scratch"])
        mount_weapon(arm, spec, args["scratch"], spec["source"])
        bpy.context.scene.render.fps = 30
        bpy.context.scene.render.fps_base = 1.0
        downscale_images(1024)
        if args["preview"] is not None:
            preview_renders(name, args["preview"], [
                ("IDLE", 1, "side"),
                ("IDLE", 1, "top"),
                ("RELOAD1", 34, "side"),
                ("DRAW", 12, "side"),
            ])
        out = args["out"] / f"viewmodel-{name}.glb"
        export_glb(out)
        inspect_glb(out)
    print("BUILD DONE")


main()
