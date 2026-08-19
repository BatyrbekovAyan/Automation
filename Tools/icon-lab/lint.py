#!/usr/bin/env python3
"""Style + optical linter for icon-lab glyphs.

Two kinds of check, both mechanical:

  STYLE   regex over the SVG source -- stroke width, caps, joins, colour.
          Catches the drift that makes a set look hand-assembled.

  OPTICAL renders the glyph and measures the actual ink: bounding box in
          viewBox units, ink coverage (% of live area covered by non-
          transparent pixels), and centroid offset from centre. A set reads
          as uniform when these numbers cluster -- that is the whole point
          of measuring instead of eyeballing.

Usage:  python3 lint.py [name ...]      (no args = every glyph)
"""
import os
import re
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
GLYPHS = os.path.join(HERE, "glyphs")
OUT = os.path.join(HERE, "out")

VIEWBOX = 24.0
LIVE_MIN, LIVE_MAX = 2.0, 22.0  # 20x20 live area inside the 24 grid
RENDER = 512

# Ink bands, calibrated by measuring real glyphs rather than guessed: a
# 2-wide outline in the 20-unit live area lands around 15-22%, a solid filled
# variant of the same silhouette roughly doubles that. A glyph outside its
# band reads as noticeably heavier or lighter than the rest of its row.
COVERAGE_OUTLINE = (13.0, 26.0)  # % of the 24x24 frame that is ink
COVERAGE_FILLED = (26.0, 50.0)
EXTENT_MIN = 17.0                # longest bbox side, viewBox units (of 20 live)


def render(name, size=RENDER):
    subprocess.run(
        ["node", os.path.join(HERE, "render.js"), name, str(size)],
        check=True, capture_output=True,
    )
    suffix = "" if size == 512 else f"@{size}"
    return os.path.join(OUT, f"{name}{suffix}.png")


def style_issues(svg):
    issues = []
    # Stroke width is a statement about visible ink, so measure it outside
    # <mask> only: a mask's shapes are construction, and a cutout is
    # deliberately stroked wider than the glyph to open a clearance gap
    # around whatever crosses it.
    visible = re.sub(r"<mask\b.*?</mask>", "", svg, flags=re.S)
    widths = set(re.findall(r'stroke-width="([\d.]+)"', visible))
    off_grid = {w for w in widths if abs(float(w) - 2.0) > 1e-6}
    if off_grid:
        issues.append(f"stroke-width {sorted(off_grid)} (set uses 2)")

    # As with fill, #000000 is legal only inside a <mask>: a glyph that crosses
    # another (a tool over a phone) punches its clearance gap by stroking the
    # cutout shape in black.
    strokes = set(re.findall(r'stroke="(#[0-9A-Fa-f]{6}|none)"', svg))
    bad = {s for s in strokes if s.lower() not in ("#ffffff", "none", "#000000")}
    if bad:
        issues.append(f"non-white stroke {sorted(bad)}")

    # Every stroked element needs round terminals; the file-level default on
    # <svg> or a <g> counts, so only flag when nothing declares them.
    if 'stroke-linecap="round"' not in svg:
        issues.append("no stroke-linecap=round")
    if 'stroke-linejoin="round"' not in svg:
        issues.append("no stroke-linejoin=round")

    # #000000 is legal only as a <mask> cutout, which is how the filled nav
    # variants punch their negative space.
    fills = set(re.findall(r'fill="(#[0-9A-Fa-f]{6}|none)"', svg))
    bad_fill = {f for f in fills if f.lower() not in ("#ffffff", "none", "#000000")}
    if bad_fill:
        issues.append(f"non-white fill {sorted(bad_fill)}")

    vb = re.search(r'viewBox="0 0 24 24"', svg)
    if not vb:
        issues.append("viewBox is not '0 0 24 24'")
    return issues


def optical(png):
    im = Image.open(png).convert("RGBA")
    alpha = im.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        return None
    scale = VIEWBOX / im.width
    x0, y0, x1, y1 = (v * scale for v in bbox)

    px = alpha.load()
    total = 0.0
    sx = sy = 0.0
    for y in range(im.height):
        for x in range(im.width):
            a = px[x, y]
            if a:
                w = a / 255.0
                total += w
                sx += x * w
                sy += y * w
    coverage = total / (im.width * im.height) * 100.0
    cx = sx / total * scale
    cy = sy / total * scale
    return dict(x0=x0, y0=y0, x1=x1, y1=y1, w=x1 - x0, h=y1 - y0,
                coverage=coverage, cx=cx, cy=cy)


def main():
    names = sys.argv[1:] or sorted(
        f[:-4] for f in os.listdir(GLYPHS) if f.endswith(".svg")
    )
    print(f"{'glyph':<22} {'bbox (x0,y0)-(x1,y1)':<28} {'w x h':<13} "
          f"{'ink%':>6} {'centre off':>11}  notes")
    print("-" * 108)
    fail = 0
    for name in names:
        svg = open(os.path.join(GLYPHS, f"{name}.svg"), encoding="utf-8").read()
        notes = style_issues(svg)
        m = optical(render(name))
        if m is None:
            print(f"{name:<22} EMPTY RENDER")
            fail += 1
            continue

        if m["x0"] < LIVE_MIN - 0.05 or m["y0"] < LIVE_MIN - 0.05 \
           or m["x1"] > LIVE_MAX + 0.05 or m["y1"] > LIVE_MAX + 0.05:
            notes.append("outside 20x20 live area")
        band = COVERAGE_FILLED if name.endswith("_filled") else COVERAGE_OUTLINE
        allow = re.search(r"<!--\s*lint:allow-ink\s+(.*?)-->", svg, re.S)
        if allow:
            reason = " ".join(allow.group(1).split())
            notes.append(f"ink {m['coverage']:.1f}%, allowed: {reason}")
        elif not (band[0] <= m["coverage"] <= band[1]):
            notes.append(f"ink {m['coverage']:.1f}% outside {band}")
        if max(m["w"], m["h"]) < EXTENT_MIN:
            notes.append(f"extent {max(m['w'], m['h']):.1f} < {EXTENT_MIN}")
        # Advisory: a glyph with a tail or another deliberately asymmetric
        # limb carries its mass off-centre by design, so this flags only the
        # gross cases and the eye settles the rest.
        off = max(abs(m["cx"] - 12.0), abs(m["cy"] - 12.0))
        if off > 1.5:
            notes.append(f"centroid off by {off:.2f}")

        if notes:
            fail += 1
        print(f"{name:<22} ({m['x0']:5.2f},{m['y0']:5.2f})-({m['x1']:5.2f},{m['y1']:5.2f}) "
              f"{m['w']:5.2f} x {m['h']:5.2f} {m['coverage']:6.2f} "
              f"{m['cx'] - 12:+5.2f},{m['cy'] - 12:+5.2f}  {'; '.join(notes)}")
    print("-" * 108)
    print(f"{len(names) - fail}/{len(names)} clean")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
