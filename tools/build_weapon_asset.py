#!/usr/bin/env python3
"""Turn a hi-poly weapon GLB into a repo-sized asset the game can load.

The source models these weapon assets come from are 100+ MB, scan-grade meshes.
The Glock 17 was 1,169,840 triangles with an 8K albedo, which is 256 MB of VRAM
for a gun you see in the corner of the screen. This script produces the file that
actually ships:

  * geometry simplified with gltfpack (meshoptimizer) - quadric simplification,
    vertex welding and reordering;
  * embedded textures downscaled and re-encoded as PNG.

Two constraints come from raylib, both measured rather than assumed on this
machine (raylib 5.5 as shipped by Raylib-cs 8.1.0):

  * Textures must be **PNG**. raylib's model loader hands embedded glTF images to
    LoadImageFromMemory, and this build has no JPEG decoder at all: a JPEG comes
    back as 0x0 with "IMAGE: Data format not supported", and even LoadImage() on a
    .jpg file fails. A JPEG-textured GLB therefore loads with a 1x1 white texture
    in every slot.
  * gltfpack's vertex quantization is switched off (`-noq`): it packs UVs into
    normalized 16-bit and compensates with a KHR_texture_transform scale factor of
    ~16, which raylib's loader does not apply, so the mesh would sample a
    sixteenth of its albedo. `--quantize` re-enables it for loaders that do apply
    the extension. Uncompressed vertices cost about 350 KB extra on this asset.

Usage (gltfpack is a single binary from github.com/zeux/meshoptimizer/releases):

    python tools/build_weapon_asset.py \
        --input Glock17.glb --output assets/weapons/glock17.glb \
        --gltfpack /path/to/gltfpack.exe \
        --simplify 0.015 --color 1024 --normal 512 --mr 256

Every texture limit is a maximum edge length in pixels; textures already smaller
than the limit are left alone. Re-running the script on the same input is
deterministic and produces the same bytes.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile

try:
    from PIL import Image
except ImportError:  # pragma: no cover - dependency hint
    sys.exit("Pillow is required: python -m pip install --user pillow")

# 8K sources are legitimate input here.
Image.MAX_IMAGE_PIXELS = None

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942

# Default maximum edge per texture class. The weapon is a first-person view model:
# it covers roughly 400-600 px on a 1280x720 screen, so a 1024 albedo already has
# more detail than the display can show. The normal and metallic-roughness maps are
# carried for a future PBR shader - raylib's default shader ignores them today - so
# they stay small.
DEFAULT_LIMITS = {"color": 1024, "normal": 512, "mr": 256}


def read_glb(path: str):
    """Return (json_dict, bin_bytes) for a .glb file."""
    with open(path, "rb") as f:
        data = f.read()

    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC:
        raise SystemExit(f"{path}: not a binary glTF (bad magic)")
    if version != 2:
        raise SystemExit(f"{path}: unsupported glTF version {version}")
    if length != len(data):
        print(f"warning: header says {length} bytes, file is {len(data)}")

    offset, json_chunk, bin_chunk = 12, None, b""
    while offset + 8 <= len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        payload = data[offset + 8: offset + 8 + chunk_len]
        if chunk_type == CHUNK_JSON:
            json_chunk = json.loads(payload)
        elif chunk_type == CHUNK_BIN:
            bin_chunk = payload
        offset += 8 + chunk_len

    if json_chunk is None:
        raise SystemExit(f"{path}: no JSON chunk")
    return json_chunk, bin_chunk


def write_glb(path: str, json_chunk: dict, bin_chunk: bytes) -> None:
    """Serialize a GLB, padding both chunks to their required 4-byte alignment."""
    json_bytes = json.dumps(json_chunk, separators=(",", ":")).encode("utf-8")
    json_bytes += b" " * (-len(json_bytes) % 4)
    bin_chunk += b"\x00" * (-len(bin_chunk) % 4)

    total = 12 + 8 + len(json_bytes) + (8 + len(bin_chunk) if bin_chunk else 0)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", GLB_MAGIC, 2, total))
        f.write(struct.pack("<II", len(json_bytes), CHUNK_JSON))
        f.write(json_bytes)
        if bin_chunk:
            f.write(struct.pack("<II", len(bin_chunk), CHUNK_BIN))
            f.write(bin_chunk)


def image_classes(gltf: dict) -> dict[int, str]:
    """Map image index -> texture class, read from the materials that use them."""
    classes: dict[int, str] = {}
    for material in gltf.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        for key, slot in (
            ("baseColorTexture", "basecolor"),
            ("metallicRoughnessTexture", "mr"),
            ("normalTexture", "normal"),
            ("emissiveTexture", "emissive"),
        ):
            ref = pbr.get(key) if key in pbr else material.get(key)
            if not ref:
                continue
            texture = gltf["textures"][ref["index"]]
            if "source" in texture:
                classes.setdefault(texture["source"], slot)
    return classes


def resized_png(raw: bytes, limit: int) -> bytes:
    """Downscale an embedded image so neither edge exceeds `limit`, as PNG.

    Lossless: the source is already a compressed scan, and re-encoding to JPEG
    would both cost detail and produce a file raylib cannot read back.
    """
    image = Image.open(io.BytesIO(raw))
    if max(image.size) > limit:
        scale = limit / max(image.size)
        size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
        image = image.resize(size, Image.LANCZOS)
    if image.mode != "RGB":
        image = image.convert("RGB")

    out = io.BytesIO()
    image.save(out, format="PNG", optimize=True)
    return out.getvalue()


def simplify(source: str, gltfpack: str, ratio: float, workdir: str, quantize: bool) -> str:
    """Run gltfpack, returning the path to the simplified GLB."""
    target = os.path.join(workdir, "simplified.glb")
    report = os.path.join(workdir, "gltfpack.json")
    cmd = [gltfpack, "-i", source, "-o", target, "-si", str(ratio), "-r", report, "-v"]
    if not quantize:
        cmd.append("-noq")
    print("running:", " ".join(cmd))
    result = subprocess.run(cmd, capture_output=True, text=True)
    sys.stdout.write(result.stdout)
    sys.stderr.write(result.stderr)
    if result.returncode != 0:
        raise SystemExit(f"gltfpack failed with exit code {result.returncode}")

    with open(report) as f:
        stats = json.load(f)
    render = stats.get("render", {})
    print(f"triangles: {render.get('triangleCount')}, draws: {render.get('drawCount')}")
    return target


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", required=True, help="source .glb")
    parser.add_argument("--output", required=True, help="destination .glb")
    parser.add_argument("--gltfpack", default=shutil.which("gltfpack"),
                        help="path to the gltfpack binary (omit to skip simplification)")
    parser.add_argument("--simplify", type=float, default=0.015,
                        help="target triangle ratio for gltfpack (default 0.015)")
    parser.add_argument("--color", type=int, default=DEFAULT_LIMITS["color"],
                        help="max albedo edge in px")
    parser.add_argument("--normal", type=int, default=DEFAULT_LIMITS["normal"],
                        help="max normal map edge in px")
    parser.add_argument("--mr", type=int, default=DEFAULT_LIMITS["mr"],
                        help="max metallic-roughness edge in px")
    parser.add_argument("--quantize", action="store_true",
                        help="keep gltfpack vertex quantization (needs KHR_mesh_quantization "
                             "and KHR_texture_transform support in the engine)")
    args = parser.parse_args()

    limits = {"basecolor": args.color, "normal": args.normal,
              "mr": args.mr, "emissive": args.color, "other": args.color}

    workdir = tempfile.mkdtemp(prefix="weapon-asset-")
    try:
        source = args.input
        if args.gltfpack:
            source = simplify(args.input, args.gltfpack, args.simplify, workdir, args.quantize)
        else:
            print("no gltfpack given: copying geometry through unchanged")

        gltf, bin_chunk = read_glb(source)
        classes = image_classes(gltf)

        # Rebuild the binary chunk. Image bufferViews get re-encoded bytes; every
        # other bufferView (vertices, indices) is copied verbatim.
        rebuilt = bytearray()
        rebuild_map: dict[int, tuple[int, int]] = {}
        for index, view in enumerate(gltf["bufferViews"]):
            start = view.get("byteOffset", 0)
            end = start + view["byteLength"]
            payload = bytes(bin_chunk[start:end])

            image_index = next(
                (i for i, im in enumerate(gltf.get("images", []))
                 if im.get("bufferView") == index), None)
            if image_index is not None:
                kind = classes.get(image_index, "other")
                before = len(payload)
                payload = resized_png(payload, limits[kind])
                gltf["images"][image_index]["mimeType"] = "image/png"
                print(f"image {image_index} ({kind}): {before / 1e6:.1f} MB -> {len(payload) / 1e6:.2f} MB PNG")

            rebuilt += b"\x00" * (-len(rebuilt) % 4)
            rebuild_map[index] = (len(rebuilt), len(payload))
            rebuilt += payload

        for index, view in enumerate(gltf["bufferViews"]):
            offset, length = rebuild_map[index]
            view["byteOffset"] = offset
            view["byteLength"] = length

        if gltf.get("buffers"):
            gltf["buffers"][0]["byteLength"] = len(rebuilt)

        extensions = gltf.get("extensionsRequired", [])
        if extensions:
            print(f"note: output requires {extensions}; the game's loader must support them")

        os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
        write_glb(args.output, gltf, bytes(rebuilt))

        size = os.path.getsize(args.output)
        print(f"wrote {args.output} ({size / 1e6:.2f} MB)")
        return 0
    finally:
        shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
