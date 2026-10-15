"""Capture every weapon at several points in a match and report the worst case.

A single screenshot only proves the view model was framed correctly at one
instant. This sweeps every weapon across multiple moments (including reloads and
sprinting) so a bad animation pose cannot hide behind a lucky capture.

Usage: python tools/sweep_weapons.py [--exe PATH]
"""
import argparse
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from inspect_png import decode  # noqa: E402

# Box weapons are flat-shaded, so exact colours identify them. The Glock 17 is an
# art mesh with a photo texture, so it is identified by shape instead: dark and
# near-neutral, minus the box body colour. The 85 cap sits below the world's
# darkest flat surface (a metal brush skirt at (75, 81, 88)), and the radar panel
# in the bottom-right corner is dark grey as well, so every region below stops at
# 82% of the width. Measured on a control frame with the view model suppressed:
# 0.0% inside those regions, against 11.6% with the weapon drawn.
GUN_BODY = lambda r, g, b: 55 <= r <= 70 and 56 <= g <= 71 and 65 <= b <= 80
GUN_METAL = lambda r, g, b: 132 <= r <= 150 and 136 <= g <= 154 and 146 <= b <= 164
GUN_DARK = lambda r, g, b: max(r, g, b) <= 85 and max(r, g, b) - min(r, g, b) <= 20 \
    and not GUN_BODY(r, g, b)
GUN = lambda r, g, b: GUN_BODY(r, g, b) or GUN_METAL(r, g, b) or GUN_DARK(r, g, b)

CHECKED_RIGHT = 0.82


def coverage(w, h, bpp, px, rect, pred, step=2):
    x0, y0, x1, y1 = rect
    total = hit = 0
    for y in range(max(0, y0), min(h, y1), step):
        row = y * w * bpp
        for x in range(max(0, x0), min(w, x1), step):
            i = row + x * bpp
            total += 1
            if pred(px[i], px[i + 1], px[i + 2]):
                hit += 1
    return 100.0 * hit / max(1, total)


def measure(path):
    w, h, bpp, px = decode(path)
    cx, cy = w // 2, h // 2
    lower_right = coverage(w, h, bpp, px,
                           (int(w * 0.55), int(h * 0.55), int(w * CHECKED_RIGHT), h), GUN)
    centre = coverage(w, h, bpp, px, (cx - 100, cy - 60, cx + 100, cy + 60), GUN)
    left = coverage(w, h, bpp, px, (0, int(h * 0.55), int(w * 0.4), h), GUN)
    return lower_right, centre, left


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", default="bin/Debug/net10.0/HoodGlobalOffensive.exe")
    args = parser.parse_args()

    exe = os.path.abspath(args.exe)
    if not os.path.exists(exe):
        print(f"executable not found: {exe}")
        return 1

    env = dict(os.environ)
    env.setdefault("DOTNET_ROOT", os.path.join(os.path.expanduser("~"), ".dotnet"))

    weapons = ["Knife", "Glock 17", "Desert Eagle", "MP5-SD", "AK-47", "M4A4", "Nova", "AWP"]
    moments = [3, 5, 7, 9, 11]
    tmp = os.path.join("shots", "_sweep.png")

    print(f"{'weapon':<14} {'min lower-right':>16} {'max centre':>12} {'max lower-left':>16}   verdict")
    failures = 0

    for weapon in weapons:
        worst_lr, worst_centre, worst_left = 100.0, 0.0, 0.0
        for seconds in moments:
            cmd = [exe, "--screenshot", tmp, "--seconds", str(seconds),
                   "--bots", "8", "--skill", "Normal", "--seed", str(100 + seconds),
                   "--weapon", weapon, "--width", "1600", "--height", "900"]
            subprocess.run(cmd, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if not os.path.exists(tmp):
                continue
            lr, centre, left = measure(tmp)
            worst_lr = min(worst_lr, lr)
            worst_centre = max(worst_centre, centre)
            worst_left = max(worst_left, left)

        ok = worst_lr > 3.0 and worst_centre < 6.0 and worst_left < 1.0
        if not ok:
            failures += 1
        print(f"{weapon:<14} {worst_lr:15.1f}% {worst_centre:11.1f}% {worst_left:15.1f}%   "
              f"{'PASS' if ok else 'FAIL'}")

    if os.path.exists(tmp):
        os.remove(tmp)

    print()
    print(f"WEAPON SWEEP: {'ALL PASS' if failures == 0 else f'{failures} FAILED'}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
