"""Inspect a captured Crossfire frame without any third-party dependencies.

Decodes the PNG with zlib/struct and reports, for named screen regions, how much
of each region is covered by the view-model materials versus world geometry.
Used to verify the first-person weapon is framed in the lower right and never
blocks the centre of the screen where the crosshair lives.
"""
import struct
import sys
import zlib


def decode(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, idat, w, h, ct = 8, b"", 0, 0, 0
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        ctype = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        if ctype == b"IHDR":
            w, h, _bd, ct = struct.unpack(">IIBB", chunk[:10])
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"IEND":
            break
        pos += 12 + length

    raw = zlib.decompress(idat)
    bpp = 4 if ct == 6 else 3
    stride = w * bpp
    out = bytearray(stride * h)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if f == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 255
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 255
        elif f == 3:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 255
        elif f == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                c = prev[i - bpp] if i >= bpp else 0
                b = prev[i]
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, bpp, out


# Colour families produced by the renderer. Each entry is (name, predicate).
VIEWMODEL = [
    ("gun-body", lambda r, g, b: 55 <= r <= 70 and 56 <= g <= 71 and 65 <= b <= 80),
    ("gun-metal", lambda r, g, b: 132 <= r <= 150 and 136 <= g <= 154 and 146 <= b <= 164),
    ("hand-skin", lambda r, g, b: 188 <= r <= 220 and 148 <= g <= 180 and 116 <= b <= 146),
]
HUD = [
    ("hud-accent", lambda r, g, b: r >= 240 and 170 <= g <= 205 and b <= 80),
    ("crosshair", lambda r, g, b: 110 <= r <= 140 and g >= 240 and 130 <= b <= 150),
]
PANEL = [("panel-dark", lambda r, g, b: r <= 20 and g <= 22 and b <= 30)]


def coverage(w, h, bpp, px, rect, families):
    x0, y0, x1, y1 = rect
    x0, y0 = max(0, x0), max(0, y0)
    x1, y1 = min(w, x1), min(h, y1)
    counts = {name: 0 for name, _ in families}
    total = 0
    for y in range(y0, y1, 2):
        row = y * w * bpp
        for x in range(x0, x1, 2):
            i = row + x * bpp
            r, g, b = px[i], px[i + 1], px[i + 2]
            total += 1
            for name, pred in families:
                if pred(r, g, b):
                    counts[name] += 1
    if total == 0:
        return {name: 0.0 for name, _ in families}
    return {name: 100.0 * v / total for name, v in counts.items()}


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "preview.png"
    w, h, bpp, px = decode(path)
    print(f"image {w}x{h}, {bpp} channels")

    cx, cy = w // 2, h // 2
    regions = [
        ("centre (crosshair 200x120)", (cx - 100, cy - 60, cx + 100, cy + 60)),
        ("lower-right (viewmodel)", (int(w * 0.55), int(h * 0.55), w, h)),
        ("lower-left", (0, int(h * 0.55), int(w * 0.45), h)),
        ("upper strip (sky)", (0, 0, w, int(h * 0.22))),
    ]

    print()
    print("view-model material coverage by region")
    for label, rect in regions:
        cov = coverage(w, h, bpp, px, rect, VIEWMODEL)
        text = "  ".join(f"{k} {v:5.1f}%" for k, v in cov.items())
        print(f"  {label:<30} {text}")

    print()
    print("hud coverage")
    for label, rect in regions:
        cov = coverage(w, h, bpp, px, rect, HUD + PANEL)
        text = "  ".join(f"{k} {v:5.1f}%" for k, v in cov.items())
        print(f"  {label:<30} {text}")

    centre = coverage(w, h, bpp, px, regions[0][1], VIEWMODEL)
    lower_right = coverage(w, h, bpp, px, regions[1][1], VIEWMODEL)
    centre_total = sum(centre.values())
    lr_total = sum(lower_right.values())
    gun_lr = lower_right["gun-body"] + lower_right["gun-metal"]
    gun_centre = centre["gun-body"] + centre["gun-metal"]

    print()
    print("checks")
    print(f"  view-model present in lower-right : {lr_total:5.1f}%  {'OK' if lr_total > 3 else 'FAIL'}")
    print(f"  weapon does not block the centre  : {gun_centre:5.1f}%  {'OK' if gun_centre < 6 else 'FAIL'}")
    print(f"  weapon body pixels bottom right   : {gun_lr:5.1f}%")

    ok = lr_total > 3 and gun_centre < 6
    print(f"  RESULT: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
