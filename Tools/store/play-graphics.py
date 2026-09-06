#!/usr/bin/env python3
"""Google Play listing graphics that have no App Store counterpart.

    node Tools/icon-lab/appicon/android.js      # first: renders the icon masters
    python3 Tools/store/play-graphics.py        # → Tools/store/listing/play/{feature-graphic.png,icon-512.png}

  • feature-graphic.png — 1024×500, 24-bit PNG without alpha (Play rejects transparency).
    Required for every listing; shown above the screenshots on the store page and in
    promotions. Composition: the app icon as an object on the same indigo-lit ink ground the
    screenshot frames use, the product name, one line of positioning. No prices, no third-party
    marks — the same restraint as the ASC copy (trademark + 3.1.2 exposure), and Google's own
    guidance is «minimal text».
  • icon-512.png — the master concept full-bleed at 512×512 (Play applies its own mask).

Type is Helvetica Neue from the system: the project's SF Pro files are licensed for Apple
platforms only and must not appear in Google-facing material.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ICONS = ROOT / "Tools/icon-lab/appicon/out/android"
OUT = ROOT / "Tools/store/listing/play"
SYSTEM_FONT = Path("/System/Library/Fonts/HelveticaNeue.ttc")

W, H = 1024, 500
BG_TOP = (24, 32, 54)       # palette «ink» bg[0]
BG_BOTTOM = (10, 13, 22)    # palette «ink» bg[1]
GLOW = (74, 99, 184)
TITLE = (244, 246, 250)
TAGLINE = (154, 163, 178)

PRODUCT = "Choose Reply"
TAGLINE_TEXT = "ИИ отвечает вашим клиентам\nв мессенджерах"


def font(size: int, index: int) -> ImageFont.FreeTypeFont:
    """Helvetica Neue .ttc faces: 0 Regular, 1 Bold, 10 Medium."""
    return ImageFont.truetype(str(SYSTEM_FONT), size, index=index)


def gradient(size: tuple[int, int], top: tuple, bottom: tuple) -> Image.Image:
    """Diagonal top-left → bottom-right, like the icon's own ground."""
    w, h = size
    im = Image.new("RGB", size)
    px = im.load()
    for y in range(h):
        for x in range(w):
            t = (x / w + y / h) / 2
            px[x, y] = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return im


def rounded(im: Image.Image, radius: int) -> Image.Image:
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius=radius, fill=255)
    out = im.convert("RGBA")
    out.putalpha(mask)
    return out


def feature_graphic(icon: Image.Image) -> Image.Image:
    canvas = gradient((W, H), BG_TOP, BG_BOTTOM).convert("RGBA")

    # indigo light behind the icon, on its own layer so the blur never reaches the text
    fx = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(fx).ellipse((-60, -40, 480, 540), fill=GLOW + (110,))
    canvas.alpha_composite(fx.filter(ImageFilter.GaussianBlur(110)))

    size = 280
    icon_im = rounded(icon.resize((size, size), Image.LANCZOS), radius=round(size * 0.2225))
    ix, iy = 84, (H - size) // 2
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((ix, iy + 18, ix + size, iy + size + 18), radius=round(size * 0.2225), fill=(0, 0, 0, 140))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(26)))
    canvas.alpha_composite(icon_im, (ix, iy))

    draw = ImageDraw.Draw(canvas)
    tx = ix + size + 60
    tf = font(78, 1)
    assert tx + tf.getlength(PRODUCT) < W - 40, "title must not touch the right edge"
    draw.text((tx, 172), PRODUCT, font=tf, fill=TITLE, anchor="ls")
    sf = font(37, 0)
    y = 222
    for line in TAGLINE_TEXT.split("\n"):
        draw.text((tx, y), line, font=sf, fill=TAGLINE, anchor="la")
        y += 48
    return canvas.convert("RGB")


def main() -> None:
    src = ICONS / "play_icon_512.png"
    if not src.exists():
        raise SystemExit(f"нет {src} — сначала: node Tools/icon-lab/appicon/android.js")
    OUT.mkdir(parents=True, exist_ok=True)
    icon = Image.open(src).convert("RGB")
    icon.save(OUT / "icon-512.png", optimize=True)
    feature_graphic(icon).save(OUT / "feature-graphic.png", optimize=True)
    print(f"готово: {OUT}/feature-graphic.png (1024×500, RGB) + icon-512.png")


if __name__ == "__main__":
    main()
