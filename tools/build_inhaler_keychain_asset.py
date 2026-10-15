#!/usr/bin/env python3
"""Build the compact inhaler-keychain melee prop used by Hood Global Offensive.

The generated GLB is intentionally self-contained: geometry, material definitions and
the PNG label atlas live in one binary file, which is the format Raylib consumes at
runtime.  No source photo is embedded or sampled.  The script recreates the visible
reference features as low-poly game geometry instead:

* ribbed twist cap, cylindrical inhaler body, domed shoulder and pierced top loop;
* blue/white label with the visible green VICKS mark, red feature strip and copy;
* linked gunmetal chain and a visibly split key ring.

The long strike axis is +X and the impact/cap end is at -X, matching the project's
art-weapon contract. The game presents this compact melee prop side-on so its label,
links and split ring remain legible. The complete prop fits inside a 64 x 64
source-unit envelope; the renderer subsequently scales an art mesh from its measured
X length.

Run from the game checkout:

    python tools/build_inhaler_keychain_asset.py

Use --texture-size 64 only when a deliberately pixelated 64x64 atlas is desired.
The default 256px atlas is deliberate: the first-person prop is large enough on
screen that it can retain the label's readable, user-requested details.
"""

from __future__ import annotations

import argparse
import binascii
import json
import math
import struct
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable


Vec3 = tuple[float, float, float]
Vec2 = tuple[float, float]


def add(a: Vec3, b: Vec3) -> Vec3:
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a: Vec3, b: Vec3) -> Vec3:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def mul(v: Vec3, s: float) -> Vec3:
    return (v[0] * s, v[1] * s, v[2] * s)


def cross(a: Vec3, b: Vec3) -> Vec3:
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def normalized(v: Vec3) -> Vec3:
    length = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    if length < 1e-8:
        return (0.0, 0.0, 1.0)
    return (v[0] / length, v[1] / length, v[2] / length)


@dataclass
class MeshData:
    positions: list[Vec3] = field(default_factory=list)
    normals: list[Vec3] = field(default_factory=list)
    uvs: list[Vec2] = field(default_factory=list)
    indices: list[int] = field(default_factory=list)

    def vertex(self, position: Vec3, normal: Vec3, uv: Vec2 = (0.0, 0.0)) -> int:
        self.positions.append(position)
        self.normals.append(normalized(normal))
        self.uvs.append(uv)
        return len(self.positions) - 1

    def triangle(self, a: int, b: int, c: int) -> None:
        self.indices.extend((a, b, c))

    def quad(self, a: int, b: int, c: int, d: int) -> None:
        self.triangle(a, b, c)
        self.triangle(a, c, d)


@dataclass
class Part:
    name: str
    material: int
    mesh: MeshData


def append_loft(
    name: str,
    material: int,
    xs: Iterable[float],
    radii: Iterable[float],
    segments: int = 16,
    cap_start: bool = True,
    cap_end: bool = True,
) -> Part:
    """Create a cylinder/lathe surface around X with a smooth radial normal."""
    x_values = list(xs)
    radius_values = list(radii)
    if len(x_values) != len(radius_values) or len(x_values) < 2:
        raise ValueError("loft needs matching x/radius lists with at least two rings")

    mesh = MeshData()
    rings: list[list[int]] = []
    for i, (x, radius) in enumerate(zip(x_values, radius_values)):
        if i == 0:
            slope = (radius_values[1] - radius) / (x_values[1] - x)
        elif i == len(x_values) - 1:
            slope = (radius - radius_values[i - 1]) / (x - x_values[i - 1])
        else:
            slope = (radius_values[i + 1] - radius_values[i - 1]) / (x_values[i + 1] - x_values[i - 1])
        ring: list[int] = []
        for j in range(segments):
            theta = math.tau * j / segments
            radial = (0.0, math.cos(theta), math.sin(theta))
            normal = normalized((-slope, radial[1], radial[2]))
            ring.append(mesh.vertex((x, radius * radial[1], radius * radial[2]), normal, (i / (len(x_values) - 1), j / segments)))
        rings.append(ring)

    for i in range(len(rings) - 1):
        for j in range(segments):
            mesh.quad(rings[i][j], rings[i][(j + 1) % segments], rings[i + 1][(j + 1) % segments], rings[i + 1][j])

    if cap_start:
        centre = mesh.vertex((x_values[0], 0.0, 0.0), (-1.0, 0.0, 0.0), (0.5, 0.5))
        for j in range(segments):
            mesh.triangle(centre, rings[0][(j + 1) % segments], rings[0][j])
    if cap_end:
        centre = mesh.vertex((x_values[-1], 0.0, 0.0), (1.0, 0.0, 0.0), (0.5, 0.5))
        for j in range(segments):
            mesh.triangle(centre, rings[-1][j], rings[-1][(j + 1) % segments])
    return Part(name, material, mesh)


def append_oriented_box(
    name: str,
    material: int,
    centre: Vec3,
    half_x: float,
    half_radial: float,
    half_tangent: float,
    radial: Vec3,
    tangent: Vec3,
) -> Part:
    """A box whose long side follows X, used for the moulded cap ribs."""
    mesh = MeshData()
    x_axis = (1.0, 0.0, 0.0)

    def point(sx: float, sr: float, st: float) -> Vec3:
        return add(add(add(centre, mul(x_axis, sx * half_x)), mul(radial, sr * half_radial)), mul(tangent, st * half_tangent))

    def face(corners: list[tuple[float, float, float]], normal: Vec3) -> None:
        ids = [mesh.vertex(point(*corner), normal) for corner in corners]
        mesh.quad(*ids)

    # These faces assume (X, radial, tangent) is right-handed, which it is for
    # tangent = cross(X, radial).
    face([(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)], x_axis)
    face([(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)], mul(x_axis, -1.0))
    face([(-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)], radial)
    face([(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], mul(radial, -1.0))
    face([(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)], tangent)
    face([(-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)], mul(tangent, -1.0))
    return Part(name, material, mesh)


def append_label_panel(name: str, material: int) -> Part:
    """A lightly offset, curved front label rather than a flat decal."""
    mesh = MeshData()
    x_steps = 14
    arc_steps = 12
    x0, x1 = -19.7, 18.5
    radius = 8.07
    theta0, theta1 = math.pi * 0.18, math.pi * 0.82
    rings: list[list[int]] = []
    for ix in range(x_steps + 1):
        x = x0 + (x1 - x0) * ix / x_steps
        row: list[int] = []
        for ia in range(arc_steps + 1):
            theta = theta0 + (theta1 - theta0) * ia / arc_steps
            radial = (0.0, math.cos(theta), math.sin(theta))
            row.append(mesh.vertex((x, radius * radial[1], radius * radial[2]), radial, (ix / x_steps, ia / arc_steps)))
        rings.append(row)
    for ix in range(x_steps):
        for ia in range(arc_steps):
            mesh.quad(rings[ix][ia], rings[ix][ia + 1], rings[ix + 1][ia + 1], rings[ix + 1][ia])
    return Part(name, material, mesh)


def append_ellipse_tube(
    name: str,
    material: int,
    centre: Vec3,
    radius_u: float,
    radius_v: float,
    tube_radius: float,
    u_axis: Vec3,
    v_axis: Vec3,
    path_segments: int = 20,
    tube_segments: int = 6,
    gap_radians: float = 0.0,
) -> Part:
    """Create a low-poly oval tube for loops, chain links and the split ring."""
    mesh = MeshData()
    u_axis = normalized(u_axis)
    v_axis = normalized(v_axis)
    plane_normal = normalized(cross(u_axis, v_axis))
    closed = gap_radians <= 1e-5
    count = path_segments if closed else path_segments + 1
    start = gap_radians * 0.5
    sweep = math.tau - gap_radians
    rings: list[list[int]] = []

    for i in range(count):
        t = start + sweep * i / (path_segments if not closed else path_segments)
        cosine, sine = math.cos(t), math.sin(t)
        path_point = add(centre, add(mul(u_axis, radius_u * cosine), mul(v_axis, radius_v * sine)))
        tangent = normalized(add(mul(u_axis, -radius_u * sine), mul(v_axis, radius_v * cosine)))
        outward = normalized(cross(tangent, plane_normal))
        ring: list[int] = []
        for j in range(tube_segments):
            phi = math.tau * j / tube_segments
            normal = add(mul(outward, math.cos(phi)), mul(plane_normal, math.sin(phi)))
            ring.append(mesh.vertex(add(path_point, mul(normal, tube_radius)), normal, (i / max(1, count - 1), j / tube_segments)))
        rings.append(ring)

    links = path_segments if closed else path_segments
    for i in range(links):
        next_i = (i + 1) % count
        for j in range(tube_segments):
            mesh.quad(rings[i][j], rings[i][(j + 1) % tube_segments], rings[next_i][(j + 1) % tube_segments], rings[next_i][j])

    # An open split ring has solid little cut faces at its two ends.
    if not closed:
        first_centre = mesh.vertex(
            tuple(sum(mesh.positions[index][axis] for index in rings[0]) / tube_segments for axis in range(3)),
            mul(normalized(sub(mesh.positions[rings[0][1]], mesh.positions[rings[0][0]])), -1.0),
        )
        last_centre = mesh.vertex(
            tuple(sum(mesh.positions[index][axis] for index in rings[-1]) / tube_segments for axis in range(3)),
            normalized(sub(mesh.positions[rings[-1][1]], mesh.positions[rings[-1][0]])),
        )
        for j in range(tube_segments):
            mesh.triangle(first_centre, rings[0][j], rings[0][(j + 1) % tube_segments])
            mesh.triangle(last_centre, rings[-1][(j + 1) % tube_segments], rings[-1][j])
    return Part(name, material, mesh)


# Five-by-seven uppercase glyphs keep the generated PNG deterministic without a
# platform-specific system font.  It is intentionally robust at both 64 and 256px.
FONT: dict[str, tuple[str, ...]] = {
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "C": ("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "I": ("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
    "K": ("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
    "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
    "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
    "O": ("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
    "P": ("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
    "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
    "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
    "T": ("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
    "U": ("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
    "V": ("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
    "X": ("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
    "0": ("01110", "10011", "10101", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
}


def make_png(width: int, height: int, rgb: bytearray) -> bytes:
    """Encode an opaque RGB image as a dependency-free PNG."""
    if len(rgb) != width * height * 3:
        raise ValueError("RGB buffer size does not match texture dimensions")

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", binascii.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + bytes(rgb[row * width * 3:(row + 1) * width * 3]) for row in range(height))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def make_label_texture(size: int) -> bytes:
    """Make the front-wrap label texture without copying the reference photo."""
    if size < 64:
        raise ValueError("texture size must be at least 64px")
    pixels = bytearray((17, 92, 184) * (size * size))

    def set_pixel(x: int, y: int, color: tuple[int, int, int]) -> None:
        if 0 <= x < size and 0 <= y < size:
            offset = (y * size + x) * 3
            pixels[offset:offset + 3] = bytes(color)

    def rect(x0: int, y0: int, x1: int, y1: int, color: tuple[int, int, int]) -> None:
        for y in range(max(0, y0), min(size, y1)):
            start = (y * size + max(0, x0)) * 3
            end = (y * size + min(size, x1)) * 3
            pixels[start:end] = bytes(color) * max(0, min(size, x1) - max(0, x0))

    def ellipse(cx: float, cy: float, rx: float, ry: float, color: tuple[int, int, int]) -> None:
        for y in range(max(0, int(cy - ry)), min(size, int(cy + ry) + 1)):
            for x in range(max(0, int(cx - rx)), min(size, int(cx + rx) + 1)):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    set_pixel(x, y, color)

    def polygon(points: list[tuple[float, float]], color: tuple[int, int, int]) -> None:
        # Compact even/odd scan conversion is enough for the label's graphic swoosh.
        for y in range(size):
            scan_y = y + 0.5
            crossings: list[float] = []
            for a, b in zip(points, points[1:] + points[:1]):
                if (a[1] <= scan_y < b[1]) or (b[1] <= scan_y < a[1]):
                    crossings.append(a[0] + (scan_y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]))
            crossings.sort()
            for left, right in zip(crossings[::2], crossings[1::2]):
                rect(math.ceil(left), y, math.floor(right) + 1, y + 1, color)

    def text(value: str, x: int, y: int, scale: int, color: tuple[int, int, int]) -> None:
        cursor = x
        for char in value:
            if char == " ":
                cursor += scale * 3
                continue
            glyph = FONT.get(char)
            if glyph is None:
                continue
            for row, pixels_row in enumerate(glyph):
                for column, bit in enumerate(pixels_row):
                    if bit == "1":
                        rect(cursor + column * scale, y + row * scale, cursor + (column + 1) * scale, y + (row + 1) * scale, color)
            cursor += scale * 6

    blue_deep = (11, 70, 151)
    white = (241, 248, 250)
    green_dark = (4, 77, 63)
    green_light = (12, 168, 136)
    red = (193, 39, 49)
    cyan = (115, 214, 232)

    # Blue edge panels and the white wrap/swoosh visible on the product label.
    rect(0, 0, size, int(size * 0.11), blue_deep)
    rect(0, int(size * 0.84), size, size, white)
    polygon(
        [
            (0, size * 0.09), (size * 0.31, size * 0.08), (size * 0.78, size * 0.44),
            (size, size * 0.62), (size, size), (size * 0.74, size), (size * 0.30, size * 0.58), (0, size * 0.31),
        ],
        white,
    )
    # Green shield/teardrop logo, shaded with a smaller highlight.
    ellipse(size * 0.52, size * 0.39, size * 0.18, size * 0.18, green_dark)
    polygon(
        [(size * 0.34, size * 0.39), (size * 0.52, size * 0.20), (size * 0.71, size * 0.39), (size * 0.52, size * 0.58)],
        green_dark,
    )
    ellipse(size * 0.57, size * 0.34, size * 0.08, size * 0.10, green_light)
    # The red quick-relief stripe and simple molded side copy blocks.
    rect(int(size * 0.11), int(size * 0.31), int(size * 0.19), int(size * 0.72), red)
    for y in range(int(size * 0.14), int(size * 0.79), max(2, size // 30)):
        rect(int(size * 0.025), y, int(size * 0.065), y + max(1, size // 80), cyan)

    logo_scale = max(1, size // 56)
    text("VICKS", int(size * 0.39), int(size * 0.34), logo_scale, white)
    text("INHALER", int(size * 0.34), int(size * 0.62), max(1, size // 62), white)
    text("TURN TO OPEN", int(size * 0.15), int(size * 0.89), max(1, size // 68), blue_deep)
    text("RELIEF", int(size * 0.115), int(size * 0.38), max(1, size // 96), white)
    return make_png(size, size, pixels)


def pack_floats(values: Iterable[Iterable[float]]) -> bytes:
    return b"".join(struct.pack("<" + "f" * len(tuple(value)), *tuple(value)) for value in values)


def build_glb(parts: list[Part], texture_png: bytes) -> tuple[bytes, int]:
    """Return a standards-compliant GLB v2 and the model's triangle count."""
    binary = bytearray()
    views: list[dict] = []
    accessors: list[dict] = []

    def append_payload(payload: bytes, target: int | None = None) -> int:
        while len(binary) % 4:
            binary.append(0)
        offset = len(binary)
        binary.extend(payload)
        view: dict[str, int] = {"buffer": 0, "byteOffset": offset, "byteLength": len(payload)}
        if target is not None:
            view["target"] = target
        views.append(view)
        return len(views) - 1

    def append_accessor(view: int, component_type: int, data_type: str, count: int, minimum: list[float] | None = None, maximum: list[float] | None = None) -> int:
        accessor: dict[str, object] = {"bufferView": view, "componentType": component_type, "count": count, "type": data_type}
        if minimum is not None:
            accessor["min"] = minimum
        if maximum is not None:
            accessor["max"] = maximum
        accessors.append(accessor)
        return len(accessors) - 1

    image_view = append_payload(texture_png)
    meshes: list[dict] = []
    nodes: list[dict] = []
    triangles = 0

    for part in parts:
        data = part.mesh
        if not data.positions or not data.indices:
            raise ValueError(f"{part.name} has no geometry")
        pos_view = append_payload(pack_floats(data.positions), 34962)
        normal_view = append_payload(pack_floats(data.normals), 34962)
        uv_view = append_payload(pack_floats(data.uvs), 34962)
        if len(data.positions) > 65535:
            index_bytes = b"".join(struct.pack("<I", index) for index in data.indices)
            index_component = 5125
        else:
            index_bytes = b"".join(struct.pack("<H", index) for index in data.indices)
            index_component = 5123
        index_view = append_payload(index_bytes, 34963)
        minimum = [min(position[axis] for position in data.positions) for axis in range(3)]
        maximum = [max(position[axis] for position in data.positions) for axis in range(3)]
        primitive = {
            "attributes": {
                "POSITION": append_accessor(pos_view, 5126, "VEC3", len(data.positions), minimum, maximum),
                "NORMAL": append_accessor(normal_view, 5126, "VEC3", len(data.normals)),
                "TEXCOORD_0": append_accessor(uv_view, 5126, "VEC2", len(data.uvs)),
            },
            "indices": append_accessor(index_view, index_component, "SCALAR", len(data.indices)),
            "material": part.material,
            "mode": 4,
        }
        meshes.append({"name": part.name, "primitives": [primitive]})
        nodes.append({"name": part.name, "mesh": len(meshes) - 1})
        triangles += len(data.indices) // 3

    materials = [
        {
            "name": "Satin off-white plastic",
            "pbrMetallicRoughness": {"baseColorFactor": [0.82, 0.87, 0.89, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.43},
        },
        {
            "name": "Milky cyan shoulder",
            "pbrMetallicRoughness": {"baseColorFactor": [0.62, 0.91, 0.94, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.25},
        },
        {
            "name": "Gunmetal keychain",
            "pbrMetallicRoughness": {"baseColorFactor": [0.35, 0.45, 0.58, 1.0], "metallicFactor": 0.15, "roughnessFactor": 0.23},
        },
        {
            "name": "Printed blue and white label",
            "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.38},
            "doubleSided": True,
        },
    ]
    document = {
        "asset": {"version": "2.0", "generator": "build_inhaler_keychain_asset.py"},
        "scene": 0,
        "scenes": [{"name": "Inhaler keychain melee prop", "nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "materials": materials,
        "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 33071, "wrapT": 33071}],
        "textures": [{"sampler": 0, "source": 0}],
        "images": [{"name": "Inhaler label atlas", "bufferView": image_view, "mimeType": "image/png"}],
        "bufferViews": views,
        "accessors": accessors,
        "buffers": [{"byteLength": len(binary)}],
        "extras": {
            "assetPurpose": "Static first-person melee prop",
            "sourceDimensions": "approximately 64 x 50 x 18 units",
            "axisContract": "+X long strike axis; -X cap/impact end; +Y up; +Z side",
            "collisionRecommendation": "Use a simple capsule around the bottle body; do not collide against individual chain links.",
            "referenceBoundary": "Visible front details are recreated; reverse-side label layout is a symmetric inference from the single supplied image.",
        },
    }
    json_chunk = json.dumps(document, separators=(",", ":")).encode("utf-8")
    json_chunk += b" " * (-len(json_chunk) % 4)
    binary += b"\x00" * (-len(binary) % 4)
    output = bytearray(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(json_chunk) + 8 + len(binary)))
    output.extend(struct.pack("<II", len(json_chunk), 0x4E4F534A))
    output.extend(json_chunk)
    output.extend(struct.pack("<II", len(binary), 0x004E4942))
    output.extend(binary)
    return bytes(output), triangles


def validate_glb(data: bytes) -> dict[str, int]:
    """Perform the small set of binary checks that matter to this Raylib asset path."""
    if len(data) < 20:
        raise ValueError("GLB is too short")
    magic, version, declared_length = struct.unpack_from("<III", data, 0)
    if (magic, version, declared_length) != (0x46546C67, 2, len(data)):
        raise ValueError("invalid GLB header")
    json_length, json_type = struct.unpack_from("<II", data, 12)
    if json_type != 0x4E4F534A:
        raise ValueError("GLB has no JSON chunk")
    json_start = 20
    document = json.loads(data[json_start:json_start + json_length])
    bin_start = json_start + json_length
    bin_length, bin_type = struct.unpack_from("<II", data, bin_start)
    if bin_type != 0x004E4942:
        raise ValueError("GLB has no binary chunk")
    binary = data[bin_start + 8:bin_start + 8 + bin_length]
    if document["buffers"][0]["byteLength"] != len(binary):
        raise ValueError("GLB buffer byte length does not match binary chunk")
    for view in document["bufferViews"]:
        start = view.get("byteOffset", 0)
        if start < 0 or start + view["byteLength"] > len(binary):
            raise ValueError("GLB buffer view points outside binary chunk")
    image = document["images"][0]
    view = document["bufferViews"][image["bufferView"]]
    png = binary[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
    if image.get("mimeType") != "image/png" or png[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("embedded label is not a PNG")
    texture_width, texture_height = struct.unpack_from(">II", png, 16)
    if document.get("extensionsRequired"):
        raise ValueError("asset unexpectedly requires glTF extensions")
    return {
        "meshes": len(document["meshes"]),
        "materials": len(document["materials"]),
        "texture_width": texture_width,
        "texture_height": texture_height,
        "binary_bytes": len(binary),
    }


def create_parts() -> list[Part]:
    # Material indices are defined in build_glb().
    plastic, cyan, metal, label = 0, 1, 2, 3
    parts: list[Part] = []

    # Main reference silhouette: ribbed twist cap -> collar -> long bottle -> dome.
    parts.append(append_loft("Cap base", plastic, [-32.0, -20.7], [8.45, 8.45], 16))
    for index in range(14):
        theta = math.tau * index / 14
        radial = (0.0, math.cos(theta), math.sin(theta))
        tangent = cross((1.0, 0.0, 0.0), radial)
        parts.append(append_oriented_box(
            f"Cap rib {index + 1:02d}", plastic, (-26.35, radial[1] * 8.65, radial[2] * 8.65),
            5.2, 0.58, 0.72, radial, tangent,
        ))
    parts.append(append_loft("Cap collar", plastic, [-20.9, -19.6], [8.52, 8.08], 16))
    parts.append(append_loft("Inhaler body", plastic, [-19.8, 17.6], [8.0, 8.0], 16))
    parts.append(append_loft("Domed shoulder", cyan, [17.55, 20.0, 22.4, 24.6, 26.15], [8.02, 7.6, 6.0, 3.9, 2.65], 16, False, False))
    parts.append(append_loft("Top neck", cyan, [25.8, 27.7], [2.7, 2.7], 14, True, True))
    parts.append(append_label_panel("Curved front product label", label))

    # Pierced top tab.  An oval low-poly tube gives an actual opening instead of a painted circle.
    parts.append(append_ellipse_tube(
        "Pierced top loop", cyan, (29.2, 0.0, 0.0), 4.25, 3.75, 1.08,
        (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), 16, 6,
    ))
    parts.append(append_ellipse_tube(
        "Top loop connector", metal, (32.5, -1.6, 0.1), 2.15, 1.35, 0.63,
        (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), 14, 6,
    ))

    # The chain is static, inexpensive silhouette geometry. It hangs below the pierced
    # loop at the bottle's far end. Each alternate link twists toward depth so it reads
    # as interlocked hardware rather than flat stamped art.
    link_data = [
        ((29.6, -7.8, 0.0), 2.8, 5.2, 0.78, 0.00),
        ((29.6, -14.7, -0.25), 2.8, 5.1, 0.78, 0.78),
        ((29.6, -21.1, 0.1), 2.75, 5.0, 0.78, 0.12),
        ((29.6, -26.8, -0.2), 2.6, 4.4, 0.76, 0.74),
    ]
    for index, (centre, ru, rv, tube, tilt) in enumerate(link_data, start=1):
        v_axis = (0.0, math.cos(tilt), math.sin(tilt))
        parts.append(append_ellipse_tube(
            f"Chain link {index:02d}", metal, centre, ru, rv, tube,
            (1.0, 0.0, 0.0), v_axis, 16, 6,
        ))
    parts.append(append_ellipse_tube(
        "Split key ring", metal, (29.6, -33.1, 0.0), 5.2, 8.35, 0.70,
        (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), 26, 7, gap_radians=0.22,
    ))
    return parts


def inspect_bounds(parts: Iterable[Part]) -> tuple[Vec3, Vec3]:
    positions = [position for part in parts for position in part.mesh.positions]
    return (
        tuple(min(position[axis] for position in positions) for axis in range(3)),
        tuple(max(position[axis] for position in positions) for axis in range(3)),
    )


def scale_parts(parts: Iterable[Part], scale: float) -> None:
    """Uniformly fit the whole visible prop within the requested 64-unit envelope."""
    for part in parts:
        part.mesh.positions[:] = [mul(position, scale) for position in part.mesh.positions]


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", type=Path, default=root / "assets" / "weapons" / "inhaler-keychain.glb", help="GLB destination")
    parser.add_argument("--texture-size", type=int, default=256, help="square embedded label atlas size; default: 256")
    args = parser.parse_args()

    parts = create_parts()
    before_minimum, before_maximum = inspect_bounds(parts)
    before_dimensions = tuple(before_maximum[axis] - before_minimum[axis] for axis in range(3))
    scale_parts(parts, 64.0 / max(before_dimensions))
    texture = make_label_texture(args.texture_size)
    glb, triangles = build_glb(parts, texture)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temp_output = args.output.with_suffix(args.output.suffix + ".tmp")
    temp_output.write_bytes(glb)
    temp_output.replace(args.output)
    minimum, maximum = inspect_bounds(parts)
    dimensions = tuple(maximum[axis] - minimum[axis] for axis in range(3))
    validation = validate_glb(glb)
    print(f"wrote {args.output}")
    print(f"triangles: {triangles}; meshes: {len(parts)}; label atlas: {args.texture_size}x{args.texture_size} PNG")
    print("bounds: " + " x ".join(f"{value:.2f}" for value in dimensions) + f" (min={minimum}, max={maximum})")
    print("validated: GLB v2, {meshes} meshes, {materials} materials, embedded {texture_width}x{texture_height} PNG, {binary_bytes} binary bytes".format(**validation))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
