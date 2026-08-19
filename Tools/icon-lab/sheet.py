#!/usr/bin/env python3
"""Builds review sheets for icon-lab glyphs.

The point of this script is to show each glyph at the size it is actually
drawn on device -- 52px for a business tile glyph, 64px for a nav icon --
next to a large version for craft inspection. An icon that only looks good
at 512px is not finished.

Usage:  python3 sheet.py business | nav | all
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

BG = (14, 17, 22, 255)          # app dark surface
PANEL = (26, 30, 37, 255)
INK = (235, 238, 242, 255)
MUTED = (128, 136, 148, 255)

# tileColor per business id, straight out of BusinessTypes.asset
TILE = {
    "auto_parts": (142, 142, 147),
    "wholesale": (88, 86, 214),
    "flowers": (255, 45, 85),
    "kaspi_seller": (255, 149, 0),
    "education": (48, 176, 199),
    "phone_repair": (50, 173, 230),
}
BUSINESS = [
    ("bt_auto_parts", "auto_parts", "Автозапчасти"),
    ("bt_wholesale", "wholesale", "Оптовый поставщик"),
    ("bt_flowers", "flowers", "Цветочный магазин"),
    ("bt_kaspi_seller", "kaspi_seller", "Продавец на Kaspi"),
    ("bt_education", "education", "Учебный центр"),
    ("bt_phone_repair", "phone_repair", "Ремонт телефонов"),
]
NAV = [
    ("nav_chats", "Чаты", (34, 150, 243)),
    ("nav_dashboard", "Сводка", (27, 124, 235)),
    ("nav_bots", "Боты", (54, 65, 254)),
    ("nav_profile", "Профиль", (97, 229, 113)),
]


def font(size):
    for p in ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
              "/System/Library/Fonts/Helvetica.ttc",
              "/Library/Fonts/Arial Unicode.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default()


def glyph(name, size, color=(255, 255, 255)):
    subprocess.run(["node", os.path.join(HERE, "render.js"), name, str(size)],
                   check=True, capture_output=True)
    suffix = "" if size == 512 else f"@{size}"
    im = Image.open(os.path.join(OUT, f"{name}{suffix}.png")).convert("RGBA")
    if color != (255, 255, 255):
        tint = Image.new("RGBA", im.size, color + (255,))
        tint.putalpha(im.getchannel("A"))
        return tint
    return im


def squircle(size, color, radius_ratio=0.28):
    """The rounded tile the business glyph sits on (IconRadius 28 of 100)."""
    ss = 4
    im = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle(
        [0, 0, size * ss - 1, size * ss - 1],
        radius=int(size * ss * radius_ratio), fill=color + (255,))
    return im.resize((size, size), Image.LANCZOS)


def paste(dst, src, cx, cy):
    dst.alpha_composite(src, (int(cx - src.width / 2), int(cy - src.height / 2)))


def business_sheet():
    """Business tiles: on-device size, 2x, and a large craft view."""
    rowh, pad = 200, 40
    W = 1180
    H = pad * 2 + 70 + rowh * len(BUSINESS)
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((pad, pad), "БИЗНЕС-ИКОНКИ — глиф 52px в плитке 100px (реальный размер на устройстве)",
           font=font(24), fill=INK)
    d.text((pad + 40, pad + 38), "100px (на устройстве)      200px                     "
                                 "глиф крупно                      без плитки",
           font=font(17), fill=MUTED)

    for i, (name, bid, label) in enumerate(BUSINESS):
        y = pad + 78 + i * rowh + rowh // 2
        color = TILE[bid]
        # on-device: 100px tile, 52px glyph
        t = squircle(100, color)
        paste(t, glyph(name, 52), 50, 50)
        paste(im, t, pad + 110, y)
        # 2x
        t2 = squircle(200, color)
        paste(t2, glyph(name, 104), 100, 100)
        paste(im, t2, pad + 300, y)
        # glyph large on tile colour, for craft
        t3 = squircle(200, color)
        paste(t3, glyph(name, 150), 100, 100)
        paste(im, t3, pad + 530, y)
        # bare glyph on dark
        paste(im, glyph(name, 150), pad + 760, y)
        d.text((pad + 860, y - 12), label, font=font(22), fill=INK)
    im.convert("RGB").save(os.path.join(OUT, "sheet_business.png"))
    print("wrote", os.path.join(OUT, "sheet_business.png"))


def nav_sheet():
    """Nav pairs: inactive outline vs active filled, plus a real tab bar."""
    rowh, pad = 190, 40
    W = 1180
    H = pad * 2 + 80 + rowh * len(NAV) + 260
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((pad, pad), "ИКОНКИ НАВБАРА — 64px (реальный размер), outline = неактив, filled = актив",
           font=font(24), fill=INK)
    d.text((pad + 40, pad + 38), "64px неактив        64px актив         крупно: outline / filled",
           font=font(17), fill=MUTED)

    for i, (name, label, accent) in enumerate(NAV):
        y = pad + 88 + i * rowh + rowh // 2
        paste(im, glyph(f"{name}_outline", 64, MUTED[:3]), pad + 90, y)
        paste(im, glyph(f"{name}_filled", 64, accent), pad + 240, y)
        paste(im, glyph(f"{name}_outline", 130, (255, 255, 255)), pad + 430, y)
        paste(im, glyph(f"{name}_filled", 130, accent), pad + 590, y)
        d.text((pad + 700, y - 12), label, font=font(22), fill=INK)

    # a real tab bar strip: one tab active, the rest inactive
    by = pad + 88 + len(NAV) * rowh + 40
    d.text((pad, by), "В сборе — как это выглядит в навбаре:", font=font(20), fill=MUTED)
    bar_y = by + 44
    d.rounded_rectangle([pad, bar_y, W - pad, bar_y + 150], radius=0, fill=PANEL)
    d.line([pad, bar_y, W - pad, bar_y], fill=(52, 58, 68, 255), width=2)
    step = (W - pad * 2) / len(NAV)
    for i, (name, label, accent) in enumerate(NAV):
        cx = pad + step * (i + 0.5)
        active = (i == 0)
        paste(im, glyph(f"{name}_{'filled' if active else 'outline'}", 64,
                        accent if active else MUTED[:3]), cx, bar_y + 58)
        f = font(21)
        tw = d.textlength(label, font=f)
        d.text((cx - tw / 2, bar_y + 98), label, font=f,
               fill=(accent + (255,)) if active else MUTED)
    im.convert("RGB").save(os.path.join(OUT, "sheet_nav.png"))
    print("wrote", os.path.join(OUT, "sheet_nav.png"))


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    if what in ("business", "all"):
        business_sheet()
    if what in ("nav", "all"):
        nav_sheet()
