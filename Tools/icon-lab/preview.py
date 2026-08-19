#!/usr/bin/env python3
"""Renders glyphs onto a dark background so they can actually be looked at.

This exists because the glyphs are white on transparency: opening one
directly shows white-on-white and reads as an empty image. Every visual
check must go through this script, never through out/<name>.png.

Each glyph is shown at 64px (nav size on device), 52px (business tile glyph
size), and 224px for craft. If it does not read at the two small sizes, it
is not finished, however good the big one looks.

Usage:  python3 preview.py <name> [<name> ...]
        python3 preview.py --all
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
GLYPHS = os.path.join(HERE, "glyphs")
OUT = os.path.join(HERE, "out")
BG = (18, 21, 27, 255)
INK = (232, 236, 242, 255)
MUTED = (125, 133, 145, 255)
SIZES = [224, 64, 52]


def font(px):
    for p in ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
              "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, px)
            except OSError:
                pass
    return ImageFont.load_default()


def glyph(name, size):
    subprocess.run(["node", os.path.join(HERE, "render.js"), name, str(size)],
                   check=True, capture_output=True)
    suffix = "" if size == 512 else f"@{size}"
    return Image.open(os.path.join(OUT, f"{name}{suffix}.png")).convert("RGBA")


def main():
    args = sys.argv[1:]
    if not args or args[0] == "--all":
        names = sorted(f[:-4] for f in os.listdir(GLYPHS) if f.endswith(".svg"))
    else:
        names = [a.replace(".svg", "") for a in args]

    cell_w, rowh, pad = 320, 268, 28
    cols = min(3, len(names))
    rows = (len(names) + cols - 1) // cols
    im = Image.new("RGBA", (pad * 2 + cell_w * cols, pad * 2 + rowh * rows), BG)
    d = ImageDraw.Draw(im)

    for i, name in enumerate(names):
        cx0 = pad + (i % cols) * cell_w
        cy0 = pad + (i // cols) * rowh
        d.text((cx0 + 8, cy0), name, font=font(18), fill=INK)
        # big one on the left, the two true device sizes stacked on the right
        big = glyph(name, SIZES[0])
        im.alpha_composite(big, (cx0 + 8, cy0 + 26))
        y = cy0 + 40
        for s in SIZES[1:]:
            g = glyph(name, s)
            im.alpha_composite(g, (cx0 + 250, y))
            d.text((cx0 + 250 + s + 6, y + s // 2 - 8), f"{s}px", font=font(14),
                   fill=MUTED)
            y += s + 34
    # Name the sheet after its first glyph: several drafters run at once and a
    # single shared preview.png would have them overwriting each other's proof.
    out = os.path.join(OUT, f"preview_{names[0]}.png")
    im.convert("RGB").save(out)
    print("wrote", out)


if __name__ == "__main__":
    main()
