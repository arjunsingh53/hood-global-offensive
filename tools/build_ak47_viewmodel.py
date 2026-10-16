#!/usr/bin/env python3
"""Build Hood Global Offensive's original AK-pattern first-person weapon model.

This file deliberately creates original geometry rather than importing a commercial
or game-extracted weapon.  It is a stylised, game-ready AK-pattern rifle: the
receiver/top cover, gas tube, front and rear sights, muzzle, curved magazine,
wood furniture, stock, pistol grip, trigger guard and small hardware are all
modelled as distinct parts so the silhouette reads at first-person distance.

The exported GLB obeys the art-weapon contract used by ``WeaponModels``:

* +X is the long barrel-to-stock axis;
* the muzzle is at -X;
* +Y is up in the exported glTF coordinate system; and
* +Z is the side axis.

Blender's native Z-up scene is converted to glTF Y-up during export.  The
script is kept with the binary asset so the presentation model can be rebuilt
or adjusted without leaving an opaque hand-edited GLB behind.

Run from the checkout with the bundled Blender:

    blender --factory-startup --disable-autoexec --background --python \
      tools/build_ak47_viewmodel.py -- assets/weapons/ak47-viewmodel.glb

To make a standalone geometry review at the same time, append:

    --preview shots/ak47-viewmodel-model-preview.png
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy


def output_paths() -> tuple[Path, Path | None]:
    """Read output and optional standalone-preview paths after ``--``."""
    try:
        args = sys.argv[sys.argv.index("--") + 1 :]
    except ValueError as exc:
        raise SystemExit("expected: -- <output.glb> [--preview <preview.png>]") from exc
    if len(args) not in (1, 3) or (len(args) == 3 and args[1] != "--preview"):
        raise SystemExit("expected: -- <output.glb> [--preview <preview.png>]")
    path = Path(args[0]).resolve()
    if path.suffix.lower() != ".glb":
        raise SystemExit("output must have a .glb extension")
    path.parent.mkdir(parents=True, exist_ok=True)
    preview = Path(args[2]).resolve() if len(args) == 3 else None
    if preview is not None:
        if preview.suffix.lower() != ".png":
            raise SystemExit("preview must have a .png extension")
        preview.parent.mkdir(parents=True, exist_ok=True)
    return path, preview


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        # Existing objects are removed above; orphaned data needs no preservation in
        # a --factory-startup export scene.
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def material(name: str, color: tuple[float, float, float, float], metallic: float, roughness: float) -> bpy.types.Material:
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get("Principled BSDF")
    if node is not None:
        if "Base Color" in node.inputs:
            node.inputs["Base Color"].default_value = color
        if "Metallic" in node.inputs:
            node.inputs["Metallic"].default_value = metallic
        if "Roughness" in node.inputs:
            node.inputs["Roughness"].default_value = roughness
    mat.diffuse_color = color
    return mat


def finish(obj: bpy.types.Object, mat: bpy.types.Material, bevel: float = 0.0, segments: int = 2) -> bpy.types.Object:
    obj.data.materials.append(mat)
    if bevel > 0.0:
        modifier = obj.modifiers.new("soft game edges", "BEVEL")
        modifier.width = bevel
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    return obj


def cube(name: str, location: tuple[float, float, float], dimensions: tuple[float, float, float], mat: bpy.types.Material,
         bevel: float = 0.0, rotation: tuple[float, float, float] = (0.0, 0.0, 0.0)) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, bevel)


def cylinder(name: str, location: tuple[float, float, float], radius: float, length: float, mat: bpy.types.Material,
             vertices: int = 14, rotation: tuple[float, float, float] = (0.0, math.pi / 2.0, 0.0), bevel: float = 0.0) -> bpy.types.Object:
    # Blender cylinders point along +Z; a Y quarter-turn places the long dimension
    # along our canonical +X barrel/stock axis.
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=length, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return finish(obj, mat, bevel)


def torus(name: str, location: tuple[float, float, float], major: float, minor: float, mat: bpy.types.Material,
          rotation: tuple[float, float, float]) -> bpy.types.Object:
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=14, minor_segments=6,
                                     location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return finish(obj, mat)


def prism(name: str, xz_profile: list[tuple[float, float]], half_side: float, mat: bpy.types.Material,
          bevel: float = 0.0) -> bpy.types.Object:
    """Extrude a small X/Z silhouette across the weapon's side axis (Blender Y)."""
    count = len(xz_profile)
    vertices = [(x, -half_side, z) for x, z in xz_profile] + [(x, half_side, z) for x, z in xz_profile]
    faces: list[tuple[int, ...]] = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    for index in range(count):
        next_index = (index + 1) % count
        faces.append((index, next_index, next_index + count, index + count))
    mesh = bpy.data.meshes.new(name + " mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat, bevel)


def banana_magazine(name: str, mat: bpy.types.Material, accent: bpy.types.Material) -> bpy.types.Object:
    """Make a compact curved magazine from linked, tapering stamped-steel rings.

    This deliberately sits lower than the previous proof-of-concept magazine.
    The old version was well-shaped in isolation but filled too much of the ADS
    frame once the asset was scaled to the view-model barrel length.
    """
    rings = 9
    vertices: list[tuple[float, float, float]] = []
    for index in range(rings):
        t = index / (rings - 1)
        # It drops from the receiver then sweeps toward the muzzle (-X), the
        # unmistakable, functional AK-type magazine curve rather than a slab.
        # It is intentionally compact for a clean, usable first-person sightline.
        centre_x = 0.12 - 0.31 * (t ** 1.38)
        centre_z = -0.055 - 0.47 * t
        half_x = 0.098 - 0.015 * t
        half_y = 0.080 - 0.010 * t
        vertices.extend([
            (centre_x - half_x, -half_y, centre_z),
            (centre_x + half_x, -half_y, centre_z),
            (centre_x + half_x, half_y, centre_z),
            (centre_x - half_x, half_y, centre_z),
        ])
    faces: list[tuple[int, ...]] = [tuple(reversed(range(4))), tuple(range((rings - 1) * 4, rings * 4))]
    for ring in range(rings - 1):
        current = ring * 4
        following = (ring + 1) * 4
        for side in range(4):
            next_side = (side + 1) % 4
            faces.append((current + side, current + next_side, following + next_side, following + side))
    mesh = bpy.data.meshes.new(name + " mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, mat, 0.018)

    # Three stamped reinforcement ribs catch a first-person light and make the
    # magazine read as metal even at the low polygon budget.
    for index, t in enumerate((0.28, 0.58, 0.82), start=1):
        centre_x = 0.12 - 0.31 * (t ** 1.38)
        centre_z = -0.055 - 0.47 * t
        cube(f"Magazine rib {index}", (centre_x, -0.083, centre_z), (0.15, 0.014, 0.028), accent, 0.004)
        cube(f"Magazine rib mirrored {index}", (centre_x, 0.083, centre_z), (0.15, 0.014, 0.028), accent, 0.004)
    return obj


def build(output: Path) -> None:
    clear_scene()

    parkerized = material("Parkerized steel", (0.055, 0.067, 0.076, 1.0), 0.78, 0.34)
    steel_edge = material("Worn steel edges", (0.19, 0.225, 0.245, 1.0), 0.86, 0.28)
    wood_dark = material("Oiled walnut shadow", (0.105, 0.034, 0.010, 1.0), 0.0, 0.44)
    wood_warm = material("Oiled walnut furniture", (0.31, 0.105, 0.025, 1.0), 0.0, 0.39)
    polymer = material("Dark polymer grip", (0.028, 0.033, 0.036, 1.0), 0.0, 0.52)
    brass = material("Brass hardware", (0.35, 0.19, 0.055, 1.0), 0.68, 0.31)
    sight_black = material("Matte iron sights", (0.018, 0.023, 0.029, 1.0), 0.68, 0.42)
    sight_edge = material("Sight edge gunmetal", (0.105, 0.135, 0.145, 1.0), 0.82, 0.30)
    sight_mark = material("Daylight front-sight insert", (0.74, 0.48, 0.12, 1.0), 0.0, 0.46)

    # This is a purpose-built first-person proportioning pass.  Earlier versions
    # were mechanically detailed, but their full-height stock, deep receiver and
    # long magazine became a screen-sized rectangle at HGO's fixed 59 degree
    # viewmodel FOV.  The new silhouette remains an AK-pattern rifle, while its
    # receiver, furniture and magazine stay slim enough to preserve the target
    # acquisition cone during ADS.

    # Receiver: a shallow stamped body below a separate, faceted top cover.  The
    # stepped parts read as a rifle from the hip but never merge into one slab.
    prism("Stamped receiver", [(-0.22, 0.02), (-0.18, -0.095), (0.42, -0.095),
                                (0.47, -0.025), (0.43, 0.145), (-0.16, 0.155)],
          0.082, parkerized, 0.010)
    # Keep the cover deliberately shallow.  In the native ADS framing it must sit
    # below, rather than behind, the raised mechanical sight channel.
    prism("Faceted top cover", [(-0.19, 0.140), (-0.12, 0.205), (0.34, 0.205),
                                 (0.44, 0.140)], 0.072, steel_edge, 0.010)
    cube("Receiver spine", (0.08, 0.0, -0.090), (0.59, 0.13, 0.036), parkerized, 0.006)

    # Barrel group.  The source span is calibrated to the existing 59 degree
    # first-person pass: enough barrel for a readable sight picture, without
    # shrinking the complete rifle into a token at full ADS.
    cylinder("Barrel", (-0.78, 0.0, 0.175), 0.026, 1.10, parkerized, 10, bevel=0.002)
    cylinder("Gas tube", (-0.56, 0.0, 0.275), 0.038, 0.69, steel_edge, 10, bevel=0.003)
    cylinder("Muzzle device", (-1.40, 0.0, 0.175), 0.047, 0.120, steel_edge, 10, bevel=0.004)
    cube("Muzzle vent upper", (-1.40, -0.052, 0.194), (0.068, 0.010, 0.020), polymer, 0.001)
    cube("Muzzle vent lower", (-1.40, 0.052, 0.194), (0.068, 0.010, 0.020), polymer, 0.001)
    cube("Gas block", (-1.04, 0.0, 0.232), (0.098, 0.125, 0.150), parkerized, 0.006)

    # The sights deliberately clear the receiver by a wide margin in the fixed
    # 59-degree viewmodel camera.  Narrow twin risers carry the U-notch and the
    # front hood upward without placing a solid wall on the sight line.  This is
    # a physical, open rear channel--not a HUD reticle--so the player can see the
    # front post and target through the centre of the rear notch while ADS.
    cube("Rear sight base", (-0.055, 0.0, 0.245), (0.145, 0.102, 0.035), parkerized, 0.004)
    cube("Rear sight left riser", (-0.055, -0.050, 0.352), (0.035, 0.014, 0.210), sight_edge, 0.003)
    cube("Rear sight right riser", (-0.055, 0.050, 0.352), (0.035, 0.014, 0.210), sight_edge, 0.003)
    cube("Rear U-notch shelf", (-0.055, 0.0, 0.465), (0.066, 0.100, 0.018), sight_black, 0.002)
    cube("Rear sight left ear", (-0.055, -0.050, 0.500), (0.062, 0.016, 0.105), sight_edge, 0.003)
    cube("Rear sight right ear", (-0.055, 0.050, 0.500), (0.062, 0.016, 0.105), sight_edge, 0.003)

    cube("Front sight base", (-1.18, 0.0, 0.220), (0.078, 0.115, 0.090), parkerized, 0.004)
    cube("Front sight left riser", (-1.18, -0.042, 0.350), (0.026, 0.012, 0.200), sight_edge, 0.002)
    cube("Front sight right riser", (-1.18, 0.042, 0.350), (0.026, 0.012, 0.200), sight_edge, 0.002)
    cube("Front sight left ear", (-1.18, -0.042, 0.502), (0.032, 0.014, 0.105), sight_edge, 0.002)
    cube("Front sight right ear", (-1.18, 0.042, 0.502), (0.032, 0.014, 0.105), sight_edge, 0.002)
    cube("Front sight post", (-1.18, 0.0, 0.518), (0.018, 0.018, 0.080), sight_black, 0.002)
    # A deliberately small, non-emissive daylight insert lets players acquire the
    # front post against HGO's pale Industrial Zone sky without adding a HUD dot.
    cube("Front sight daylight insert", (-1.195, 0.0, 0.535), (0.008, 0.021, 0.035), sight_mark, 0.001)

    # Layered furniture gives the hip-fire weapon a recognisable low-poly rifle
    # silhouette.  Each element is kept shallow so ADS exposes the sight line.
    prism("Lower wood handguard", [(-0.93, 0.005), (-0.86, -0.080), (-0.46, -0.080),
                                   (-0.38, 0.005), (-0.44, 0.105), (-0.86, 0.105)],
          0.105, wood_warm, 0.014)
    prism("Upper wood handguard", [(-0.88, 0.182), (-0.80, 0.244), (-0.47, 0.244),
                                   (-0.40, 0.182)], 0.080, wood_dark, 0.012)
    cube("Forward handguard band", (-0.86, 0.0, 0.060), (0.040, 0.225, 0.175), steel_edge, 0.003)
    cube("Rear handguard band", (-0.42, 0.0, 0.062), (0.040, 0.220, 0.168), steel_edge, 0.003)

    # A compact skeleton stock reads clearly in hip fire but avoids the opaque
    # board that previously covered the lower middle of the ADS image.
    cube("Rear trunnion", (0.43, 0.0, 0.105), (0.090, 0.150, 0.125), parkerized, 0.006)
    cube("Stock upper strut", (0.68, 0.0, 0.150), (0.540, 0.050, 0.050), wood_warm, 0.010, (0.0, -0.085, 0.0))
    cube("Stock lower strut", (0.67, 0.0, 0.010), (0.500, 0.050, 0.046), wood_dark, 0.008, (0.0, 0.090, 0.0))
    cube("Skeleton butt", (0.950, 0.0, 0.082), (0.070, 0.150, 0.170), steel_edge, 0.006)
    cube("Pistol grip", (0.255, 0.0, -0.250), (0.125, 0.125, 0.295), polymer, 0.012, (0.0, -0.25, 0.0))
    torus("Trigger guard", (0.105, 0.0, -0.118), 0.083, 0.010, steel_edge, (math.pi / 2.0, 0.0, 0.0))
    cube("Trigger", (0.080, 0.0, -0.125), (0.018, 0.020, 0.086), parkerized, 0.003, (0.0, -0.34, 0.0))
    banana_magazine("Curved steel magazine", parkerized, steel_edge)

    # Restrained hardware breaks up the receiver in a side-on hip pose without
    # adding trademarks, text, or dense high-frequency noise.
    for side in (-0.096, 0.096):
        cube("Safety selector" + (" L" if side < 0 else " R"), (0.16, side, 0.078),
             (0.30, 0.014, 0.028), steel_edge, 0.003, (0.0, -0.08, 0.0))
        cylinder("Receiver rivet" + (" L1" if side < 0 else " R1"), (-0.06, side, 0.055),
                 0.012, 0.014, brass, 8, (math.pi / 2.0, 0.0, 0.0), 0.001)
        cylinder("Receiver rivet" + (" L2" if side < 0 else " R2"), (0.27, side, 0.055),
                 0.012, 0.014, brass, 8, (math.pi / 2.0, 0.0, 0.0), 0.001)
    cube("Charging handle arm", (0.025, -0.094, 0.158), (0.085, 0.040, 0.024), parkerized, 0.003)
    cylinder("Charging handle knob", (0.070, -0.126, 0.158), 0.020, 0.062, steel_edge, 8,
             (math.pi / 2.0, 0.0, 0.0), 0.002)

    # Export only meshes.  A named scene custom property survives as glTF extras
    # when export_extras is enabled, preserving the placement contract beside the
    # separate human-readable provenance note.
    scene = bpy.context.scene
    scene["asset_purpose"] = "Original first-person AK-pattern rifle for Hood Global Offensive"
    scene["axis_contract"] = "+X length; muzzle at -X; glTF +Y up; +Z side"
    scene["source"] = "Original procedural low-poly geometry authored for this project"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        use_selection=True,
        export_materials="EXPORT",
        export_normals=True,
        export_yup=True,
        export_extras=True,
        export_cameras=False,
        export_lights=False,
    )


def render_preview(output: Path) -> None:
    """Render a quick three-quarter review without changing the exported GLB.

    The game capture remains the acceptance check for placement.  This preview is
    deliberately an asset-only inspection: it makes silhouette, proportions and
    material separation reviewable before an artist starts the game.
    """
    from mathutils import Vector

    scene = bpy.context.scene
    # Blender 5.2 exposes the real-time renderer as BLENDER_EEVEE in its Python
    # enum even though the product UI calls this generation EEVEE Next.
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output)
    scene.render.film_transparent = False
    scene.world.color = (0.018, 0.026, 0.040)

    floor_mat = material("Preview floor", (0.055, 0.070, 0.090, 1.0), 0.0, 0.82)
    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(-0.25, 0.0, -0.55))
    floor = bpy.context.object
    floor.name = "Preview floor (not exported)"
    floor.data.materials.append(floor_mat)

    camera_data = bpy.data.cameras.new("Preview camera")
    camera_data.lens = 58
    camera = bpy.data.objects.new("Preview camera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (2.70, -5.20, 1.75)
    camera.rotation_euler = (Vector((-0.35, 0.0, -0.035)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera

    key_data = bpy.data.lights.new("Preview key", "AREA")
    key_data.energy = 850
    key_data.shape = "DISK"
    key_data.size = 4.0
    key = bpy.data.objects.new("Preview key", key_data)
    scene.collection.objects.link(key)
    key.location = (-1.5, -2.2, 3.2)
    key.rotation_euler = (Vector((-0.35, 0.0, 0.02)) - key.location).to_track_quat("-Z", "Y").to_euler()

    rim_data = bpy.data.lights.new("Preview rim", "AREA")
    rim_data.energy = 450
    rim_data.size = 3.0
    rim = bpy.data.objects.new("Preview rim", rim_data)
    scene.collection.objects.link(rim)
    rim.location = (1.3, 2.0, 2.0)
    rim.rotation_euler = (Vector((-0.10, 0.0, 0.05)) - rim.location).to_track_quat("-Z", "Y").to_euler()

    bpy.ops.render.render(write_still=True)


def main() -> int:
    out, preview = output_paths()
    build(out)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    triangles = sum(len(obj.data.polygons) for obj in meshes)
    print(f"wrote {out}")
    print(f"original AK-pattern model: {len(meshes)} meshes, {triangles} triangles")
    print("axis contract: +X length, muzzle at -X, glTF +Y up, +Z side")
    if preview is not None:
        render_preview(preview)
        print(f"wrote review preview {preview}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
