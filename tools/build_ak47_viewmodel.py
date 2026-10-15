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
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy


def output_path() -> Path:
    """Read a single output path after Blender's ``--`` separator."""
    try:
        args = sys.argv[sys.argv.index("--") + 1 :]
    except ValueError as exc:
        raise SystemExit("expected: -- <output.glb>") from exc
    if len(args) != 1:
        raise SystemExit("expected: -- <output.glb>")
    path = Path(args[0]).resolve()
    if path.suffix.lower() != ".glb":
        raise SystemExit("output must have a .glb extension")
    path.parent.mkdir(parents=True, exist_ok=True)
    return path


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
    """Make a curved stamped-magazine body from linked, tapering rectangular rings."""
    rings = 11
    vertices: list[tuple[float, float, float]] = []
    for index in range(rings):
        t = index / (rings - 1)
        # It drops from the receiver then sweeps toward the muzzle (-X), the
        # unmistakable, functional AK-type magazine curve rather than a slab.
        centre_x = 0.14 - 0.40 * (t ** 1.42)
        centre_z = -0.055 - 0.66 * t
        half_x = 0.125 - 0.020 * t
        half_y = 0.105 - 0.012 * t
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
    for index, t in enumerate((0.23, 0.48, 0.73), start=1):
        centre_x = 0.14 - 0.40 * (t ** 1.42)
        centre_z = -0.055 - 0.66 * t
        cube(f"Magazine rib {index}", (centre_x, -0.112, centre_z), (0.19, 0.018, 0.038), accent, 0.006)
        cube(f"Magazine rib mirrored {index}", (centre_x, 0.112, centre_z), (0.19, 0.018, 0.038), accent, 0.006)
    return obj


def build(output: Path) -> None:
    clear_scene()

    parkerized = material("Parkerized steel", (0.055, 0.067, 0.076, 1.0), 0.78, 0.34)
    steel_edge = material("Worn steel edges", (0.19, 0.225, 0.245, 1.0), 0.86, 0.28)
    wood_dark = material("Oiled walnut shadow", (0.105, 0.034, 0.010, 1.0), 0.0, 0.44)
    wood_warm = material("Oiled walnut furniture", (0.31, 0.105, 0.025, 1.0), 0.0, 0.39)
    polymer = material("Dark polymer grip", (0.028, 0.033, 0.036, 1.0), 0.0, 0.52)
    brass = material("Brass hardware", (0.35, 0.19, 0.055, 1.0), 0.68, 0.31)

    # Receiver and top cover: a faceted cover over a separate, deeper receiver
    # creates the recognisable stepped profile instead of a single generic block.
    prism("Stamped receiver", [(-0.28, 0.04), (-0.23, -0.12), (0.42, -0.12), (0.47, 0.02), (0.42, 0.22), (-0.20, 0.22)], 0.105, parkerized, 0.022)
    prism("Ribbed top cover", [(-0.25, 0.22), (-0.16, 0.33), (0.32, 0.33), (0.44, 0.22)], 0.102, steel_edge, 0.018)
    cube("Receiver lower rail", (0.06, 0.0, -0.105), (0.66, 0.19, 0.055), parkerized, 0.010)

    # Barrel group, muzzle device and gas system.  The extreme negative X end is
    # intentionally the muzzle so WeaponModels can place flashes from its measured
    # front slice without a per-weapon magic coordinate.
    cylinder("Barrel", (-0.86, 0.0, 0.245), 0.034, 1.08, parkerized, 16, bevel=0.004)
    cylinder("Front gas tube", (-0.59, 0.0, 0.365), 0.052, 0.70, steel_edge, 14, bevel=0.006)
    cylinder("Muzzle brake", (-1.43, 0.0, 0.245), 0.064, 0.135, steel_edge, 12, bevel=0.008)
    cube("Muzzle slot upper", (-1.43, -0.068, 0.27), (0.075, 0.015, 0.025), polymer, 0.002)
    cube("Muzzle slot lower", (-1.43, 0.068, 0.27), (0.075, 0.015, 0.025), polymer, 0.002)
    cube("Gas block", (-0.99, 0.0, 0.305), (0.115, 0.17, 0.20), parkerized, 0.016)
    cube("Front sight tower", (-1.10, 0.0, 0.355), (0.070, 0.115, 0.30), parkerized, 0.012)
    cylinder("Front sight post", (-1.10, 0.0, 0.515), 0.016, 0.045, brass, 8, bevel=0.002)
    cube("Rear sight left", (-0.10, -0.068, 0.405), (0.12, 0.035, 0.08), steel_edge, 0.007)
    cube("Rear sight right", (-0.10, 0.068, 0.405), (0.12, 0.035, 0.08), steel_edge, 0.007)

    # Handguard and furniture use two distinct wood tones.  Their offsets make a
    # layered wood-and-metal silhouette visible from the lowered FPS view.
    prism("Lower wooden handguard", [(-0.82, 0.02), (-0.75, -0.11), (-0.35, -0.11), (-0.27, 0.01), (-0.34, 0.15), (-0.76, 0.15)], 0.145, wood_warm, 0.026)
    prism("Upper wooden handguard", [(-0.77, 0.23), (-0.69, 0.31), (-0.36, 0.31), (-0.31, 0.225)], 0.115, wood_dark, 0.022)
    cube("Handguard retaining band", (-0.76, 0.0, 0.10), (0.050, 0.31, 0.25), steel_edge, 0.006)
    cube("Handguard forward band", (-0.35, 0.0, 0.10), (0.045, 0.30, 0.23), steel_edge, 0.006)

    # A full stock, pistol grip, hinged trigger guard and tapered curved magazine.
    # Keep the rear furniture compact.  A first-person barrel must extend toward
    # the target, while the butt moves toward the camera; a full real-world stock
    # at this projection becomes a distracting screen-sized wedge.  This shortened
    # stock still reads as wood furniture but preserves a clean, usable sightline.
    prism("Wooden stock", [(0.33, 0.105), (0.45, -0.01), (0.78, -0.01), (0.88, 0.055), (0.88, 0.21), (0.65, 0.25), (0.52, 0.20)], 0.13, wood_warm, 0.026)
    cube("Butt plate", (0.885, 0.0, 0.105), (0.035, 0.285, 0.225), steel_edge, 0.008)
    cube("Pistol grip", (0.28, 0.0, -0.285), (0.17, 0.16, 0.42), polymer, 0.020, (0.0, -0.28, 0.0))
    torus("Trigger guard", (0.13, 0.0, -0.14), 0.115, 0.014, steel_edge, (math.pi / 2.0, 0.0, 0.0))
    cube("Trigger", (0.09, 0.0, -0.15), (0.025, 0.026, 0.12), parkerized, 0.006, (0.0, -0.38, 0.0))
    banana_magazine("Curved steel magazine", parkerized, steel_edge)

    # Selector, charging handle and small rivets give the receiver a purposeful
    # side profile without tying the presentation to a trademarked real-world make.
    for side in (-0.122, 0.122):
        cube("Safety selector" + (" L" if side < 0 else " R"), (0.17, side, 0.125), (0.42, 0.020, 0.040), steel_edge, 0.005, (0.0, -0.10, 0.0))
        cylinder("Receiver rivet" + (" L1" if side < 0 else " R1"), (-0.06, side, 0.085), 0.018, 0.020, brass, 8, (math.pi / 2.0, 0.0, 0.0), 0.001)
        cylinder("Receiver rivet" + (" L2" if side < 0 else " R2"), (0.30, side, 0.080), 0.018, 0.020, brass, 8, (math.pi / 2.0, 0.0, 0.0), 0.001)
    cube("Charging handle arm", (0.03, -0.14, 0.225), (0.16, 0.10, 0.045), parkerized, 0.008)
    cylinder("Charging handle knob", (0.10, -0.22, 0.225), 0.040, 0.13, steel_edge, 10, (math.pi / 2.0, 0.0, 0.0), 0.005)

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


def main() -> int:
    out = output_path()
    build(out)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    triangles = sum(len(obj.data.polygons) for obj in meshes)
    print(f"wrote {out}")
    print(f"original AK-pattern model: {len(meshes)} meshes, {triangles} triangles")
    print("axis contract: +X length, muzzle at -X, glTF +Y up, +Z side")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
