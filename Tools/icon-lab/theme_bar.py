#!/usr/bin/env python3
"""Renders the bottom nav bar in both themes, using the real palette.

Colours are parsed straight out of Assets/Resources/Theme/Theme_*.asset rather
than retyped, so this sheet cannot drift from what the app actually resolves.
"""
import os
import re
import subprocess

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "out")
TABS = [("nav_chats", "Чаты"), ("nav_dashboard", "Сводка"),
        ("nav_bots", "Боты"), ("nav_profile", "Профиль")]


def palette(mode):
    path = os.path.join(ROOT, f"Assets/Resources/Theme/Theme_{mode}.asset")
    txt = open(path, encoding="utf-8").read()
    out = {}
    for role in ("surface", "inkTertiary", "accentText", "background"):
        m = re.search(rf"^  {role}: \{{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+)",
                      txt, re.M)
        out[role] = tuple(int(round(float(v) * 255)) for v in m.groups())
    return out


def font(px):
    for p in ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
              "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, px)
            except OSError:
                pass
    return ImageFont.load_default()


def glyph(name, size, color):
    subprocess.run(["node", os.path.join(HERE, "render.js"), name, str(size)],
                   check=True, capture_output=True)
    suffix = "" if size == 512 else f"@{size}"
    im = Image.open(os.path.join(OUT, f"{name}{suffix}.png")).convert("RGBA")
    tint = Image.new("RGBA", im.size, color + (255,))
    tint.putalpha(im.getchannel("A"))
    return tint


def bar(mode, active_index=0, width=1080, height=210):
    p = palette(mode)
    im = Image.new("RGBA", (width, height), p["surface"] + (255,))
    d = ImageDraw.Draw(im)
    d.line([0, 0, width, 0], fill=tuple(
        max(0, min(255, c + (26 if mode == "Dark" else -26))) for c in p["surface"]) + (255,), width=3)
    step = width / len(TABS)
    for i, (name, label) in enumerate(TABS):
        cx = step * (i + 0.5)
        active = i == active_index
        color = p["accentText"] if active else p["inkTertiary"]
        g = glyph(f"{name}_{'filled' if active else 'outline'}", 64, color)
        im.alpha_composite(g, (int(cx - 32), 42))
        f = font(26)
        tw = d.textlength(label, font=f)
        d.text((cx - tw / 2, 120), label, font=f, fill=color + (255,))
    return im


def main():
    pad, gap = 40, 46
    bars = [("Тёмная тема", bar("Dark")), ("Светлая тема", bar("Light"))]
    W = 1080 + pad * 2
    H = pad * 2 + sum(b.height + gap + 38 for _, b in bars)
    sheet = Image.new("RGBA", (W, H), (8, 10, 13, 255))
    d = ImageDraw.Draw(sheet)
    y = pad
    for title, b in bars:
        d.text((pad, y), title, font=font(24), fill=(226, 232, 240, 255))
        y += 38
        sheet.alpha_composite(b, (pad, y))
        y += b.height + gap
    dest = os.path.join(OUT, "sheet_theme_bar.png")
    sheet.convert("RGB").save(dest)
    print("wrote", dest)
    for mode in ("Dark", "Light"):
        p = palette(mode)
        hx = lambda c: "#%02X%02X%02X" % c
        print(f"  {mode:5s} bar={hx(p['surface'])} inactive={hx(p['inkTertiary'])} "
              f"active={hx(p['accentText'])}")


if __name__ == "__main__":
    main()
