#!/usr/bin/env python3
"""Before/after sheet: the icons this set replaced, beside the ones that shipped.

"Before" is read from git HEAD for the business glyphs (they were published
over the same filenames) and from the stock PNGs still on disk for the nav bar.

Usage:  python3 compare.py
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "out")
BG = (14, 17, 22, 255)
INK = (232, 236, 242, 255)
MUTED = (122, 130, 142, 255)
OLD_TINT = (150, 158, 170)

BUSINESS = [
    ("BT_AutoParts", "bt_auto_parts", (142, 142, 147)),
    ("BT_Wholesale", "bt_wholesale", (88, 86, 214)),
    ("BT_Flowers", "bt_flowers", (255, 45, 85)),
    ("BT_KaspiSeller", "bt_kaspi_seller", (255, 149, 0)),
    ("BT_Education", "bt_education", (48, 176, 199)),
    ("BT_PhoneRepair", "bt_phone_repair", (50, 173, 230)),
]
# Each old entry carries the rect the scene actually drew it into. The source
# PNGs were square, so those non-square rects were stretching every tab icon --
# which is why the "before" column is drawn at the rect, not at the PNG.
NAV_OLD = [
    ("Assets/Images/Icons/chat (1) copy.png", "nav_chats_outline", (80, 64)),
    ("Assets/Images/Nav/dashboard_inactive.png", "nav_dashboard_outline", (52, 64)),
    ("Assets/Images/Chat/bot4 copy.png", "nav_bots_outline", (80, 64)),
    ("Assets/Images/Chat/pngegg (1) copy.png", "nav_profile_outline", (80, 64)),
]


def font(px):
    for p in ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
              "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, px)
            except OSError:
                pass
    return ImageFont.load_default()


def old_business(name):
    """Previous artwork, straight out of git HEAD."""
    # Textures live in Git LFS, so `git show` hands back a pointer file rather
    # than pixels; smudge turns it back into the real PNG.
    pointer = subprocess.run(
        ["git", "-C", ROOT, "show", f"HEAD:Assets/Images/BusinessIcons/{name}.png"],
        capture_output=True, check=True).stdout
    blob = subprocess.run(["git", "-C", ROOT, "lfs", "smudge"],
                          input=pointer, capture_output=True, check=True).stdout
    tmp = os.path.join(OUT, f"_old_{name}.png")
    with open(tmp, "wb") as fh:
        fh.write(blob)
    return Image.open(tmp).convert("RGBA")


def glyph(name, size):
    subprocess.run(["node", os.path.join(HERE, "render.js"), name, str(size)],
                   check=True, capture_output=True)
    suffix = "" if size == 512 else f"@{size}"
    return Image.open(os.path.join(OUT, f"{name}{suffix}.png")).convert("RGBA")


def tint(im, color):
    out = Image.new("RGBA", im.size, color + (255,))
    out.putalpha(im.getchannel("A"))
    return out


def squircle(size, color):
    ss = 4
    im = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle(
        [0, 0, size * ss - 1, size * ss - 1], radius=int(size * ss * 0.28),
        fill=color + (255,))
    return im.resize((size, size), Image.LANCZOS)


def paste(dst, src, cx, cy):
    dst.alpha_composite(src, (int(cx - src.width / 2), int(cy - src.height / 2)))


def main():
    rowh, pad = 132, 40
    W = 980
    H = pad * 2 + 96 + rowh * len(BUSINESS) + 110 + rowh * len(NAV_OLD)
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((pad, pad), "БЫЛО  →  СТАЛО", font=font(28), fill=INK)
    d.text((pad, pad + 42), "всё показано в реальном размере отрисовки: "
                            "плитка бизнеса 100px, иконка навбара 64px",
           font=font(17), fill=MUTED)

    y = pad + 96
    d.text((pad, y - 16), "Бизнес-иконки", font=font(20), fill=MUTED)
    for old_name, new_name, color in BUSINESS:
        cy = y + rowh // 2
        t = squircle(100, color)
        paste(t, old_business(old_name).resize((52, 52), Image.LANCZOS), 50, 50)
        paste(im, t, pad + 90, cy)
        d.text((pad + 165, cy - 10), "→", font=font(26), fill=MUTED)
        t2 = squircle(100, color)
        paste(t2, glyph(new_name, 52), 50, 50)
        paste(im, t2, pad + 250, cy)
        d.text((pad + 330, cy - 11), new_name.replace("bt_", ""), font=font(20),
               fill=INK)
        y += rowh

    y += 60
    d.text((pad, y - 16), "Навбар — стоковые PNG, растянутые неквадратными рамками",
           font=font(20), fill=MUTED)
    for old_path, new_name, rect in NAV_OLD:
        cy = y + rowh // 2
        full = os.path.join(ROOT, old_path)
        if os.path.exists(full):
            o = Image.open(full).convert("RGBA").resize(rect, Image.LANCZOS)
            paste(im, tint(o, OLD_TINT), pad + 90, cy)
            d.text((pad + 120, cy + 22), f"{rect[0]}×{rect[1]}", font=font(14),
                   fill=(214, 106, 106, 255))
        d.text((pad + 165, cy - 10), "→", font=font(26), fill=MUTED)
        paste(im, tint(glyph(new_name, 64), OLD_TINT), pad + 250, cy)
        d.text((pad + 288, cy + 22), "64×64", font=font(14), fill=MUTED)
        d.text((pad + 330, cy - 11), new_name.replace("nav_", "").replace("_outline", ""),
               font=font(20), fill=INK)
        y += rowh

    dest = os.path.join(OUT, "sheet_before_after.png")
    im.convert("RGB").save(dest)
    for f in os.listdir(OUT):
        if f.startswith("_old_"):
            os.remove(os.path.join(OUT, f))
    print("wrote", dest)


if __name__ == "__main__":
    sys.exit(main())
