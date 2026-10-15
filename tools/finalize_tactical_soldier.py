#!/usr/bin/env python3
"""Turn Blender's Tactical Soldier GLB into a small Raylib-safe runtime asset.

Blender 5.2 preserves source JPEGs for ``export_image_format=AUTO``.  HGO's
Raylib build cannot decode JPEG data embedded in a GLB, so every image is
losslessly re-encoded as PNG here.  Unlike the generic weapon helper, this
script preserves alpha channels for eye, eyelash and mask materials.
"""

from __future__ import annotations

import argparse
import io
import os
from pathlib import Path

from PIL import Image

from build_weapon_asset import read_glb, write_glb


Image.MAX_IMAGE_PIXELS = None
REQUIRED_ACTIONS = {"Idle", "Run", "Reference", "Walk", "Crouch"}


def image_roles(gltf: dict) -> dict[int, set[str]]:
    roles: dict[int, set[str]] = {}

    def add(texture_ref: dict | None, role: str) -> None:
        if not texture_ref:
            return
        texture = gltf["textures"][texture_ref["index"]]
        source = texture.get("source")
        if source is not None:
            roles.setdefault(source, set()).add(role)

    for material in gltf.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        add(pbr.get("baseColorTexture"), "color")
        add(pbr.get("metallicRoughnessTexture"), "data")
        add(material.get("normalTexture"), "normal")
        add(material.get("occlusionTexture"), "data")
        add(material.get("emissiveTexture"), "color")
    return roles


def png_payload(raw: bytes, limit: int) -> bytes:
    image = Image.open(io.BytesIO(raw))
    image.load()
    if max(image.size) > limit:
        factor = limit / max(image.size)
        image = image.resize(
            (max(1, round(image.width * factor)), max(1, round(image.height * factor))),
            Image.Resampling.LANCZOS,
        )
    has_alpha = "A" in image.getbands() or "transparency" in image.info
    image = image.convert("RGBA" if has_alpha else "RGB")
    encoded = io.BytesIO()
    image.save(encoded, format="PNG", optimize=True)
    return encoded.getvalue()


def action_names(gltf: dict) -> set[str]:
    return {animation.get("name", "") for animation in gltf.get("animations", [])}


def assert_runtime_contract(gltf: dict) -> None:
    required_extensions = gltf.get("extensionsRequired", [])
    if required_extensions:
        raise SystemExit(f"runtime GLB requires unsupported extensions: {required_extensions}")
    if len(gltf.get("skins", [])) != 1:
        raise SystemExit(f"runtime GLB must have exactly one skin, found {len(gltf.get('skins', []))}")
    missing_actions = REQUIRED_ACTIONS - action_names(gltf)
    if missing_actions:
        raise SystemExit(f"runtime GLB is missing generated actions: {sorted(missing_actions)}")
    for mesh in gltf.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            attributes = primitive.get("attributes", {})
            excess_uvs = [name for name in attributes if name.startswith("TEXCOORD_") and name != "TEXCOORD_0"]
            if excess_uvs:
                raise SystemExit(f"runtime GLB contains unsupported UV attributes: {excess_uvs}")
            excess_joints = [name for name in attributes if name.startswith("JOINTS_") and name != "JOINTS_0"]
            if excess_joints:
                raise SystemExit(f"runtime GLB contains unsupported skin influences: {excess_joints}")
            if primitive.get("targets"):
                raise SystemExit("runtime GLB still contains morph targets")


def finalize(source: Path, output: Path, color_limit: int, normal_limit: int, data_limit: int) -> None:
    gltf, blob = read_glb(str(source))
    assert_runtime_contract(gltf)
    roles = image_roles(gltf)
    image_by_view = {
        image["bufferView"]: index
        for index, image in enumerate(gltf.get("images", []))
        if "bufferView" in image
    }
    if len(image_by_view) != len(gltf.get("images", [])):
        raise SystemExit("all runtime images must be embedded bufferViews")

    rebuilt = bytearray()
    offsets: dict[int, tuple[int, int]] = {}
    for index, view in enumerate(gltf.get("bufferViews", [])):
        start = view.get("byteOffset", 0)
        payload = bytes(blob[start:start + view["byteLength"]])
        image_index = image_by_view.get(index)
        if image_index is not None:
            role = roles.get(image_index, {"color"})
            if "color" in role:
                limit = color_limit
            elif "normal" in role:
                limit = normal_limit
            else:
                limit = data_limit
            before = len(payload)
            payload = png_payload(payload, limit)
            gltf["images"][image_index]["mimeType"] = "image/png"
            print(f"image {image_index:02d} ({'/'.join(sorted(role))}): "
                  f"{before / 1_000_000:.2f} MB -> {len(payload) / 1_000_000:.2f} MB PNG")

        rebuilt += b"\x00" * (-len(rebuilt) % 4)
        offsets[index] = (len(rebuilt), len(payload))
        rebuilt += payload

    for index, view in enumerate(gltf.get("bufferViews", [])):
        view["byteOffset"], view["byteLength"] = offsets[index]
    gltf["buffers"][0]["byteLength"] = len(rebuilt)

    if any(image.get("mimeType") != "image/png" for image in gltf.get("images", [])):
        raise SystemExit("PNG transcode did not cover every embedded image")
    assert_runtime_contract(gltf)
    output.parent.mkdir(parents=True, exist_ok=True)
    write_glb(str(output), gltf, bytes(rebuilt))

    print(f"TACTICAL_SOLDIER_FINAL images={len(gltf.get('images', []))} "
          f"animations={','.join(sorted(action_names(gltf)))} "
          f"skins={len(gltf.get('skins', []))} bytes={os.path.getsize(output)} output={output}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path, help="intermediate Blender GLB")
    parser.add_argument("--output", required=True, type=Path, help="runtime HGO GLB")
    parser.add_argument("--color", type=int, default=1024, help="maximum base-color/emissive edge")
    parser.add_argument("--normal", type=int, default=512, help="maximum normal-map edge")
    parser.add_argument("--data", type=int, default=512, help="maximum metallic/roughness/AO edge")
    args = parser.parse_args()
    finalize(args.input, args.output, args.color, args.normal, args.data)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
