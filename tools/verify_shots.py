"""Verify the shipped Hood Global Offensive screenshot artifacts.

Four classes of check, all measured on the current, real PNGs in shots/ rather
than on intermediate files:

  * iron-sight framing - the AK's real dark sight geometry must occupy a compact
    centre area without a black scope mask;
  * the title screen - the accent bar is drawn and the game title renders without
    being clipped by the right edge of the window.
  * the Buy Station overlay - the production panel has its amber command header,
    readable labels and the green equipped-state treatment; and
  * Industrial Zone, HGO Capsule Combatant and combat-feedback proof frames - each is a full-size,
    non-empty capture rather than a placeholder.

Exits non-zero if any check fails.
"""
import os
import sys

# Allow the script to be run from the project root as `python tools/verify_shots.py`.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from inspect_png import decode  # noqa: E402

GUN_BODY = lambda r, g, b: 55 <= r <= 70 and 56 <= g <= 71 and 65 <= b <= 80
GUN_METAL = lambda r, g, b: 132 <= r <= 150 and 136 <= g <= 154 and 146 <= b <= 164
# The art-mesh weapon, by shape: dark and near-neutral, minus the box body colour.
# The 85 cap is below the world's darkest flat surface (a metal brush skirt at
# (75, 81, 88)), so level geometry cannot be mistaken for the weapon.
GUN_DARK = lambda r, g, b: max(r, g, b) <= 85 and max(r, g, b) - min(r, g, b) <= 20 \
    and not GUN_BODY(r, g, b)
GUN = lambda r, g, b: GUN_BODY(r, g, b) or GUN_METAL(r, g, b) or GUN_DARK(r, g, b)

def coverage(w, h, bpp, px, rect, pred, step=2):
    x0, y0, x1, y1 = rect
    x0, y0 = max(0, x0), max(0, y0)
    x1, y1 = min(w, x1), min(h, y1)
    total = hit = 0
    for y in range(y0, y1, step):
        row = y * w * bpp
        for x in range(x0, x1, step):
            i = row + x * bpp
            total += 1
            if pred(px[i], px[i + 1], px[i + 2]):
                hit += 1
    return 100.0 * hit / max(1, total)


def mean_luma(w, h, bpp, px, rect, step=3):
    x0, y0, x1, y1 = rect
    total = acc = 0
    for y in range(y0, y1, step):
        row = y * w * bpp
        for x in range(x0, x1, step):
            i = row + x * bpp
            acc += 0.299 * px[i] + 0.587 * px[i + 1] + 0.114 * px[i + 2]
            total += 1
    return acc / max(1, total)


def check_iron_sights(path):
    w, h, bpp, px = decode(path)
    cx, cy = w // 2, h // 2
    gun = lambda a, b, c: GUN(a, b, c)

    sight = coverage(w, h, bpp, px, (cx - 90, cy - 85, cx + 90, cy + 85), gun)
    central_luma = mean_luma(w, h, bpp, px, (cx - 90, cy - 85, cx + 90, cy + 85))
    corner_luma = sum((
        mean_luma(w, h, bpp, px, (0, 0, 100, 80)),
        mean_luma(w, h, bpp, px, (w - 100, 0, w, 80)),
        mean_luma(w, h, bpp, px, (0, h - 80, 100, h)),
        mean_luma(w, h, bpp, px, (w - 100, h - 80, w, h)),
    )) / 4.0

    # A real front-post/rear-notch sight must be visible, but the centre cannot
    # be blanketed by a hip-fire-sized receiver.  Bright corners prove this is
    # iron-sight ADS, not a magnified scope mask.
    ok = 2.0 < sight < 42.0 and central_luma > 10.0 and corner_luma > 18.0
    print(f"  {path}  (AK iron sights)")
    print(f"    dark sight geometry at centre : {sight:5.1f}%  (want 2..42)")
    print(f"    central scene luma            : {central_luma:5.1f}  (want > 10)")
    print(f"    corner scene luma             : {corner_luma:5.1f}  (want > 18, no scope mask)")
    print(f"    -> {'PASS' if ok else 'FAIL'}")
    return ok


def check_menu(path):
    """The title screen: accent bar, a title that rendered, and no clipping.

    The title became 22 characters when the game was renamed, so "does it fit" is
    measured on real pixels instead of assumed from font metrics: the right-most
    ink pixel in the title band must sit well inside the right edge. A font or size
    change that overflows the window will fail this.
    """
    w, h, bpp, px = decode(path)
    accent = lambda r, g, b: r >= 240 and 170 <= g <= 205 and b <= 80
    ink = lambda r, g, b: 0.299 * r + 0.587 * g + 0.114 * b > 150

    bar = coverage(w, h, bpp, px, (0, 0, w, 6), accent, step=1)

    # The title is drawn at y=54 with size 52, so its band is y 54..106.
    # The subtitle sits at y=118 and is deliberately excluded.
    rightmost, ink_pixels = -1, 0
    for y in range(52, 110, 2):
        row = y * w * bpp
        for x in range(0, w):
            i = row + x * bpp
            if ink(px[i], px[i + 1], px[i + 2]):
                ink_pixels += 1
                if x > rightmost:
                    rightmost = x

    rendered = ink_pixels > 200
    substantial = rightmost > 250
    inside = rightmost < w - 40
    ok = bar > 80.0 and rendered and substantial and inside

    print(f"  {path}  (title screen)")
    print(f"    accent top bar                     : {bar:5.1f}%  (want > 80)")
    print(f"    title ink pixels                   : {ink_pixels:5d}    (want > 200)")
    print(f"    title right-most pixel             : {rightmost:5d}    (want > 250)")
    print(f"    clear of the right edge ({w - 40})     : {'yes' if inside else 'NO - title clipped'}")
    print(f"    -> {'PASS' if ok else 'FAIL'}")
    return ok


def check_overlay(path, label, samples):
    w, h, bpp, px = decode(path)
    print(f"  {path}  ({label})")
    ok = True
    for name, rect, pred, want in samples:
        value = coverage(w, h, bpp, px, rect, pred, step=1)
        passed = value >= want
        ok = ok and passed
        print(f"    {name:<34} {value:5.2f}%  (want >= {want})  {'ok' if passed else 'FAIL'}")
    print(f"    -> {'PASS' if ok else 'FAIL'}")
    return ok


def check_readable_frame(path, label):
    """Guard against an accidentally blank, wrong-sized public PNG."""
    w, h, bpp, px = decode(path)
    luma = mean_luma(w, h, bpp, px, (0, 0, w, h), step=4)
    ok = w == 1280 and h == 720 and luma > 12.0
    print(f"  {path}  ({label})")
    print(f"    dimensions                       : {w}x{h}  (want 1280x720)")
    print(f"    mean image luma                  : {luma:5.1f}  (want > 12)")
    print(f"    -> {'PASS' if ok else 'FAIL'}")
    return ok


def main():
    results = []

    print("iron-sight framing")
    results.append(check_iron_sights("shots/01-ak47.png"))

    print()
    print("title screen")
    results.append(check_menu("shots/20-sunset-menu.png"))

    print()
    print("full-screen overlays")
    accent = lambda r, g, b: r >= 240 and 170 <= g <= 205 and b <= 80
    equipped = lambda r, g, b: 90 <= r <= 160 and 175 <= g <= 225 and 90 <= b <= 160
    bright = lambda r, g, b: r > 140 and g > 140 and b > 140
    results.append(check_overlay("shots/04-buy-menu.png", "buy menu", [
        ("amber command header", (130, 110, 1150, 216), accent, 5.0),
        ("equipped protection state", (150, 230, 635, 305), equipped, 1.0),
        ("menu label text", (130, 110, 1150, 610), bright, 0.7),
    ]))

    print()
    print("current-world proof frames")
    results.append(check_readable_frame("shots/07-industrial-zone.png", "Industrial Zone"))
    results.append(check_readable_frame("shots/21-capsule-sunset-showcase.png", "HGO Capsule Combatant"))
    results.append(check_readable_frame("shots/22-capsule-impact-sunset.png", "Combat feedback"))

    print()
    print(f"ARTIFACTS: {'ALL PASS' if all(results) else 'FAILURES PRESENT'}")
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
