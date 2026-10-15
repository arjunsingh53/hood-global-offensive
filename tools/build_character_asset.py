#!/usr/bin/env python3
"""Prepare the Three.js/Mixamo Soldier source model for raylib.

The unmodified Soldier.glb is a valid glTF, but it is not a safe runtime asset
for this game:

* its two embedded JPEGs decode to 1x1 white textures in the Raylib-cs build;
* it uses two skins even though raylib supports one armature per model;
* it exposes TEXCOORD_0 through TEXCOORD_6 although raylib accepts only two
  texture-coordinate attributes.

This conversion keeps the visible body and visor, retains every animation clip,
and produces a raylib-compatible GLB by:

* converting the embedded images to PNG without downscaling their 1024 px source;
* remapping the visor's two joints into the body skin, whose matching inverse bind
  matrices are verified before anything is changed;
* retaining TEXCOORD_0 and removing unused extra texture-coordinate attributes.

The script deliberately does not run gltfpack. Repacking a skinned, animated
model adds a separate compatibility variable, and this source is already small
enough for the game's current bot count.

Usage:

    python tools/build_character_asset.py \
        --input /path/to/Soldier.glb \
        --output assets/characters/soldier.glb

Re-running the script on the same input produces the same GLB bytes.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import struct
from typing import Dict, Tuple

from build_weapon_asset import image_classes, read_glb, resized_png, write_glb


DEFAULT_LIMITS = {"color": 1024, "normal": 1024, "mr": 1024}
EXPECTED_SOURCE_SHA256 = "dfb230fc1f942f259dd00281a1186953ad602fc5d69067ce63e24b2aa439736b"
COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
COMPONENT_FORMATS = {
    5121: ("B", 1),  # UNSIGNED_BYTE
    5123: ("H", 2),  # UNSIGNED_SHORT
    5125: ("I", 4),  # UNSIGNED_INT
}


def accessor_layout(gltf: dict, accessor_index: int) -> Tuple[dict, int, int, str, int]:
    """Return the layout needed to safely read/write an accessor's values."""
    accessor = gltf["accessors"][accessor_index]
    if accessor.get("sparse"):
        raise SystemExit("sparse joint accessors are not supported by this asset recipe")

    component_type = accessor.get("componentType")
    if component_type not in COMPONENT_FORMATS:
        raise SystemExit(f"unsupported joint component type {component_type}")
    if accessor.get("type") not in COMPONENTS:
        raise SystemExit(f"unsupported joint accessor type {accessor.get('type')}")

    view = gltf["bufferViews"][accessor["bufferView"]]
    code, component_size = COMPONENT_FORMATS[component_type]
    components = COMPONENTS[accessor["type"]]
    stride = view.get("byteStride", component_size * components)
    if stride < component_size * components:
        raise SystemExit("joint accessor byte stride is smaller than its element")

    offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    return accessor, offset, stride, code, components


def read_element(blob: bytes, offset: int, code: str, components: int) -> Tuple[int, ...]:
    return struct.unpack_from("<" + code * components, blob, offset)


def remap_joint_accessor(gltf: dict, blob: bytearray, accessor_index: int,
                         joint_map: Dict[int, int]) -> None:
    """Rewrite one JOINTS accessor from a secondary skin into the primary skin."""
    accessor, offset, stride, code, components = accessor_layout(gltf, accessor_index)
    if accessor.get("type") != "VEC4":
        raise SystemExit("joint accessor must use VEC4 values")

    for item in range(accessor["count"]):
        item_offset = offset + item * stride
        old = read_element(blob, item_offset, code, components)
        try:
            new = tuple(joint_map[value] for value in old)
        except KeyError as exc:
            raise SystemExit(f"joint {exc.args[0]} is not present in the primary skin") from exc
        struct.pack_into("<" + code * components, blob, item_offset, *new)


def inverse_bind_matrix(blob: bytes, gltf: dict, accessor_index: int, index: int) -> bytes:
    """Read a MAT4 inverse-bind matrix as source bytes for exact comparison."""
    accessor = gltf["accessors"][accessor_index]
    if accessor.get("componentType") != 5126 or accessor.get("type") != "MAT4":
        raise SystemExit("inverse bind matrices must be FLOAT MAT4 values")
    view = gltf["bufferViews"][accessor["bufferView"]]
    offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride", 64)
    return bytes(blob[offset + index * stride:offset + index * stride + 64])


def merge_secondary_skins(gltf: dict, blob: bytearray) -> None:
    """Map compatible secondary skins into the primary armature.

    Soldier's visor is a second two-joint skin using the same Neck and Head nodes
    as the body's 49-joint skin. Raylib accepts only one skin per model. The
    comparison below ensures this shortcut stays correct if the source changes.
    """
    skins = gltf.get("skins", [])
    if not skins:
        raise SystemExit("character source has no skin")
    if len(skins) == 1:
        return

    primary = skins[0]
    primary_joints = primary.get("joints", [])
    primary_by_node = {node: index for index, node in enumerate(primary_joints)}
    primary_ibm = primary.get("inverseBindMatrices")
    if primary_ibm is None:
        raise SystemExit("primary skin has no inverse bind matrices")

    remapped_accessors: Dict[int, Dict[int, int]] = {}
    for node in gltf.get("nodes", []):
        skin_index = node.get("skin")
        if skin_index is None or skin_index == 0:
            continue
        if skin_index >= len(skins):
            raise SystemExit(f"node references missing skin {skin_index}")

        secondary = skins[skin_index]
        secondary_joints = secondary.get("joints", [])
        secondary_ibm = secondary.get("inverseBindMatrices")
        if secondary_ibm is None:
            raise SystemExit("secondary skin has no inverse bind matrices")

        joint_map: Dict[int, int] = {}
        for secondary_index, joint_node in enumerate(secondary_joints):
            if joint_node not in primary_by_node:
                raise SystemExit(
                    f"secondary joint node {joint_node} is not in the primary armature")
            primary_index = primary_by_node[joint_node]
            primary_matrix = inverse_bind_matrix(blob, gltf, primary_ibm, primary_index)
            secondary_matrix = inverse_bind_matrix(blob, gltf, secondary_ibm, secondary_index)
            if primary_matrix != secondary_matrix:
                raise SystemExit(
                    "secondary skin has different inverse bind matrices; export it through a "
                    "DCC instead of guessing a remap")
            joint_map[secondary_index] = primary_index

        mesh_index = node.get("mesh")
        if mesh_index is None:
            raise SystemExit("secondary skin node has no mesh to remap")
        for primitive in gltf["meshes"][mesh_index].get("primitives", []):
            for attribute, accessor_index in primitive.get("attributes", {}).items():
                if not attribute.startswith("JOINTS_"):
                    continue
                prior = remapped_accessors.get(accessor_index)
                if prior is not None and prior != joint_map:
                    raise SystemExit("one joint accessor is shared by incompatible skins")
                if prior is None:
                    remap_joint_accessor(gltf, blob, accessor_index, joint_map)
                    remapped_accessors[accessor_index] = joint_map

        node["skin"] = 0

    gltf["skins"] = [primary]


def keep_primary_texcoords(gltf: dict) -> int:
    """Remove attributes raylib cannot consume; materials only use TEXCOORD_0."""
    removed = 0
    for mesh in gltf.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            attributes = primitive.get("attributes", {})
            for name in list(attributes):
                if name.startswith("TEXCOORD_") and name != "TEXCOORD_0":
                    del attributes[name]
                    removed += 1
    return removed


def transcode_images(gltf: dict, blob: bytes, limits: Dict[str, int]) -> bytes:
    """Rebuild the binary blob while converting every embedded image to PNG."""
    classes = image_classes(gltf)
    image_by_view = {
        image["bufferView"]: index
        for index, image in enumerate(gltf.get("images", []))
        if "bufferView" in image
    }

    rebuilt = bytearray()
    offsets: Dict[int, Tuple[int, int]] = {}
    for index, view in enumerate(gltf.get("bufferViews", [])):
        start = view.get("byteOffset", 0)
        end = start + view["byteLength"]
        payload = bytes(blob[start:end])

        image_index = image_by_view.get(index)
        if image_index is not None:
            kind = classes.get(image_index, "other")
            before = len(payload)
            payload = resized_png(payload, limits.get(kind, limits["basecolor"]))
            gltf["images"][image_index]["mimeType"] = "image/png"
            print(
                f"image {image_index} ({kind}): {before / 1e6:.2f} MB "
                f"-> {len(payload) / 1e6:.2f} MB PNG")

        rebuilt += b"\x00" * (-len(rebuilt) % 4)
        offsets[index] = (len(rebuilt), len(payload))
        rebuilt += payload

    for index, view in enumerate(gltf.get("bufferViews", [])):
        view["byteOffset"], view["byteLength"] = offsets[index]
    if gltf.get("buffers"):
        gltf["buffers"][0]["byteLength"] = len(rebuilt)
    return bytes(rebuilt)


def verify(gltf: dict, blob: bytes) -> None:
    """Fail early for the exact model features the runtime cannot accept."""
    if len(gltf.get("skins", [])) != 1:
        raise SystemExit("output must contain exactly one skin")
    if any(image.get("mimeType") != "image/png" for image in gltf.get("images", [])):
        raise SystemExit("output contains a non-PNG embedded image")

    for mesh in gltf.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            extras = [name for name in primitive.get("attributes", {})
                      if name.startswith("TEXCOORD_") and name != "TEXCOORD_0"]
            if extras:
                raise SystemExit(f"output still exposes unsupported texture coordinates: {extras}")

    skin_joint_count = len(gltf["skins"][0].get("joints", []))
    for mesh in gltf.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            for name, accessor_index in primitive.get("attributes", {}).items():
                if not name.startswith("JOINTS_"):
                    continue
                accessor, offset, stride, code, components = accessor_layout(gltf, accessor_index)
                for item in range(accessor["count"]):
                    values = read_element(blob, offset + item * stride, code, components)
                    if any(value >= skin_joint_count for value in values):
                        raise SystemExit("output contains a joint index outside the primary skin")


def sha256_file(path: str) -> str:
    """Stream a source hash so the recipe stays usable on Python 3.8 and newer."""
    digest = hashlib.sha256()
    with open(path, "rb") as source_file:
        while True:
            chunk = source_file.read(1024 * 1024)
            if not chunk:
                return digest.hexdigest()
            digest.update(chunk)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", required=True, help="source Soldier.glb")
    parser.add_argument("--output", required=True, help="raylib-compatible destination GLB")
    parser.add_argument("--color", type=int, default=DEFAULT_LIMITS["color"],
                        help="maximum diffuse/emissive edge (default: 1024)")
    parser.add_argument("--normal", type=int, default=DEFAULT_LIMITS["normal"],
                        help="maximum normal-map edge (default: 1024)")
    parser.add_argument("--mr", type=int, default=DEFAULT_LIMITS["mr"],
                        help="maximum metallic-roughness edge (default: 1024)")
    parser.add_argument("--allow-source-change", action="store_true",
                        help="allow a Soldier source whose SHA-256 differs from the recorded recipe")
    args = parser.parse_args()

    source_hash = sha256_file(args.input)
    if source_hash != EXPECTED_SOURCE_SHA256 and not args.allow_source_change:
        raise SystemExit(
            "source SHA-256 does not match this verified recipe; inspect the new asset and run "
            "again with --allow-source-change only after updating the attribution record")

    gltf, source_blob = read_glb(args.input)
    mutable_blob = bytearray(source_blob)
    merge_secondary_skins(gltf, mutable_blob)
    removed_texcoords = keep_primary_texcoords(gltf)
    limits = {"basecolor": args.color, "normal": args.normal, "mr": args.mr,
              "emissive": args.color, "other": args.color}
    output_blob = transcode_images(gltf, bytes(mutable_blob), limits)
    verify(gltf, output_blob)

    directory = os.path.dirname(os.path.abspath(args.output))
    os.makedirs(directory, exist_ok=True)
    write_glb(args.output, gltf, output_blob)
    size = os.path.getsize(args.output)
    print(f"source SHA-256 {source_hash}")
    print(f"removed {removed_texcoords} extra TEXCOORD attributes")
    print(f"wrote {args.output} ({size / 1e6:.2f} MB, {len(gltf['skins'])} skin)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
