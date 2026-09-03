#!/usr/bin/env python3
"""Compose App Store listing frames from the raw capture in Tools/store/screenshots/.

Each listing frame = brand background + caption block + a phone frame (bezel, rounded
screen, Dynamic Island, marketing status bar «9:41») holding the raw 1284×2778 capture.
Rendered once at the 6.9" size ASC asks for (1320×2868) and downscaled to the 6.5"
fallback (1284×2778; the 0.4 % aspect drift is invisible). Re-run after every
Tools/Store/Capture Screenshots — the raw frames are the only input.

    python3 Tools/store/compose-listing.py            # → Tools/store/listing/{ios-6.9,ios-6.5}/ + preview-sheet.png

The order and copy of FRAMES is the listing order (docs/store/submission-checklist.md).
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / "Tools/store/screenshots"
OUT = ROOT / "Tools/store/listing"
FONTS = ROOT / "Assets/TextMesh Pro/Fonts"

# Listing order: the first three are what App Store search results show; the rest live on
# the product page. Differentiator first («Вместе»), the payoff second, the familiar chat
# list third, then depth (bots, price-lists, dashboard).
FRAMES = [
    ("03-suggestions",       "01-vmeste",     "ИИ предлагает — вы выбираете",     "Готовые ответы под каждое сообщение клиента"),
    ("02-thread-auto",       "02-auto",       "Отвечает клиентам за вас",          "Цены, наличие, заявка — по вашему прайсу, 24/7"),
    ("01-chats",             "03-chats",      "Все чаты на одном экране",          "WhatsApp и Telegram на вашем номере"),
    ("04-tab2",              "04-bots",       "Бот под каждый бизнес",             "Включайте «Авто» одним тапом"),
    ("06-settings-products", "05-pricelist",  "Загрузите прайс — бот знает цены",  "PDF, Excel, фото — разберёт сам"),
    ("04-tab1",              "06-dashboard",  "Сводка за неделю",                  "Сколько заявок собрал бот и где нужны вы"),
]

CANVAS = (1320, 2868)                 # iPhone 6.9" portrait
FALLBACK = (1284, 2778)               # iPhone 6.5" portrait
BG = (10, 13, 20)                     # deeper and bluer than the app's #0D0D0D so the phone reads as an object
GLOW = (74, 99, 184)                  # the app's indigo, lifted
HEADLINE = (244, 246, 250)
SUBLINE = (154, 163, 178)
BEZEL = (26, 29, 36)
BEZEL_EDGE = (48, 52, 60)

SCREEN_W = 980                        # screen width inside the bezel
BEZEL_PX = 26
PHONE_TOP = 600                       # same on every frame so the set lines up in the gallery
MARGIN_X = 90
CAPTION_TOP = 150


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / f"SFProText-{name}.ttf"), size)


def wrap(text: str, f: ImageFont.FreeTypeFont, max_w: int) -> list[str]:
    """Greedy wrap, then — for the two-line case — the split that balances the lines:
    greedy leaves an orphan («Отвечает клиентам за / вас»), balance gives
    «Отвечает клиентам / за вас»."""
    # An em dash never opens a line in Russian typography (that is the dialogue dash), so it
    # travels with the word before it: «ИИ предлагает — / вы выбираете».
    words: list[str] = []
    for word in text.split():
        if word == "—" and words:
            words[-1] += " —"
        else:
            words.append(word)
    lines, cur = [], ""
    for word in words:
        cand = f"{cur} {word}".strip()
        if cur and f.getlength(cand) > max_w:
            lines.append(cur)
            cur = word
        else:
            cur = cand
    lines.append(cur)
    if len(lines) != 2:
        return lines
    best, best_span = lines, None
    for i in range(1, len(words)):
        a, b = " ".join(words[:i]), " ".join(words[i:])
        wa, wb = f.getlength(a), f.getlength(b)
        if max(wa, wb) > max_w:
            continue
        span = abs(wa - wb)
        if best_span is None or span < best_span:
            best, best_span = [a, b], span
    return best


def draw_caption(draw: ImageDraw.ImageDraw, headline: str, subline: str) -> int:
    """Returns the y where the caption block ends."""
    hf, sf = font("Bold", 92), font("Regular", 46)
    max_w = CANVAS[0] - 2 * MARGIN_X
    y = CAPTION_TOP
    cx = CANVAS[0] // 2   # centred on the phone's axis — the frames sit side by side in the gallery
    for line in wrap(headline, hf, max_w):
        draw.text((cx, y), line, font=hf, fill=HEADLINE, anchor="ma")
        y += 106
    y += 22
    for line in wrap(subline, sf, max_w):
        draw.text((cx, y), line, font=sf, fill=SUBLINE, anchor="ma")
        y += 60
    return y


def status_bar(draw: ImageDraw.ImageDraw, x0: int, y0: int, w: int) -> None:
    """Apple's marketing status bar: 9:41, full signal, Wi-Fi, full battery. Proportions
    are taken from a 430pt-wide iPhone and scaled by the screen width."""
    s = w / 430.0
    white = (255, 255, 255)
    tf = font("Semibold", int(17 * s * 1.05))
    time_cx = x0 + 61 * s
    draw.text((time_cx, y0 + 24.5 * s), "9:41", font=tf, fill=white, anchor="mm")
    cy = y0 + 24.5 * s
    # signal: four bars, right-aligned block
    bx = x0 + w - 106 * s
    for i, h in enumerate((4, 6, 8, 10)):
        bw, gap = 3.2 * s, 1.6 * s
        left = bx + i * (bw + gap)
        draw.rounded_rectangle((left, cy + 5 * s - h * s, left + bw, cy + 5 * s), radius=1 * s, fill=white)
    # wi-fi: three arcs + dot
    wcx = x0 + w - 78 * s
    for r, width in ((8.5 * s, 2.2 * s), (5.5 * s, 2.2 * s)):
        draw.arc((wcx - r, cy - r + 2 * s, wcx + r, cy + r + 2 * s), start=225, end=315, fill=white, width=int(width))
    draw.ellipse((wcx - 1.6 * s, cy + 1.6 * s, wcx + 1.6 * s, cy + 4.8 * s), fill=white)
    # battery
    bl, bt, bw_, bh = x0 + w - 55 * s, cy - 6 * s, 25 * s, 12 * s
    draw.rounded_rectangle((bl, bt, bl + bw_, bt + bh), radius=3.5 * s, outline=(255, 255, 255, 110), width=int(1.4 * s))
    draw.rounded_rectangle((bl + 2 * s, bt + 2 * s, bl + bw_ - 2 * s, bt + bh - 2 * s), radius=2 * s, fill=white)
    draw.rounded_rectangle((bl + bw_ + 1 * s, bt + 4 * s, bl + bw_ + 2.6 * s, bt + bh - 4 * s), radius=1 * s, fill=(255, 255, 255, 110))


def phone(canvas: Image.Image, raw: Image.Image, top: int) -> None:
    sw = SCREEN_W
    sh = round(sw * raw.height / raw.width)
    ox = (CANVAS[0] - sw) // 2 - BEZEL_PX
    oy = top
    outer = (ox, oy, ox + sw + 2 * BEZEL_PX, oy + sh + 2 * BEZEL_PX)
    r_in = round(sw * 0.135)
    r_out = r_in + BEZEL_PX

    # glow + shadow live on their own layers so the blur never touches the caption
    fx = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(fx)
    gcx, gcy = CANVAS[0] // 2, oy + sh * 0.72
    d.ellipse((gcx - 820, gcy - 640, gcx + 820, gcy + 640), fill=GLOW + (92,))
    fx = fx.filter(ImageFilter.GaussianBlur(210))
    canvas.alpha_composite(fx)
    sh_layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh_layer).rounded_rectangle(
        (outer[0], outer[1] + 46, outer[2], outer[3] + 46), radius=r_out, fill=(0, 0, 0, 150))
    canvas.alpha_composite(sh_layer.filter(ImageFilter.GaussianBlur(48)))

    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(outer, radius=r_out, fill=BEZEL, outline=BEZEL_EDGE, width=2)

    screen = raw.resize((sw, sh), Image.LANCZOS).convert("RGBA")
    mask = Image.new("L", (sw, sh), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, sw - 1, sh - 1), radius=r_in, fill=255)
    sx, sy = ox + BEZEL_PX, oy + BEZEL_PX
    canvas.paste(screen, (sx, sy), mask)

    # Dynamic Island + status bar sit in the header's empty safe-area strip.
    s = sw / 430.0
    iw, ih = 126 * s, 37 * s
    icx, itop = sx + sw / 2, sy + 11 * s
    draw.rounded_rectangle((icx - iw / 2, itop, icx + iw / 2, itop + ih), radius=ih / 2, fill=(0, 0, 0))
    status_bar(draw, sx, sy, sw)


def compose(raw_name: str, headline: str, subline: str) -> Image.Image:
    src = RAW / f"{raw_name}.png"
    if not src.exists():
        sys.exit(f"нет сырого кадра {src} — сначала Tools/Store/Capture Screenshots")
    raw = Image.open(src).convert("RGB")
    canvas = Image.new("RGBA", CANVAS, BG + (255,))
    phone(canvas, raw, PHONE_TOP)
    end = draw_caption(ImageDraw.Draw(canvas), headline, subline)
    if end > PHONE_TOP - 40:
        print(f"  ! подпись «{headline}» доходит до y={end}, телефон с y={PHONE_TOP} — перекрытие")
    return canvas.convert("RGB")


def preview_sheet(frames: list[tuple[str, Image.Image]]) -> Image.Image:
    thumb_w, gap, label_h = 330, 28, 54
    thumb_h = round(thumb_w * CANVAS[1] / CANVAS[0])
    sheet = Image.new("RGB", (gap + len(frames) * (thumb_w + gap), gap + thumb_h + label_h), (24, 27, 34))
    lf = font("Medium", 24)
    d = ImageDraw.Draw(sheet)
    for i, (slug, im) in enumerate(frames):
        x = gap + i * (thumb_w + gap)
        sheet.paste(im.resize((thumb_w, thumb_h), Image.LANCZOS), (x, gap))
        d.text((x + thumb_w / 2, gap + thumb_h + 30), slug, font=lf, fill=(200, 205, 214), anchor="mm")
    return sheet


def main() -> None:
    big, small = OUT / "ios-6.9", OUT / "ios-6.5"
    big.mkdir(parents=True, exist_ok=True)
    small.mkdir(parents=True, exist_ok=True)
    done = []
    for raw_name, slug, headline, subline in FRAMES:
        im = compose(raw_name, headline, subline)
        im.save(big / f"{slug}.png", optimize=True)
        im.resize(FALLBACK, Image.LANCZOS).save(small / f"{slug}.png", optimize=True)
        done.append((slug, im))
        print(f"  {slug}.png  ← {raw_name}  «{headline}»")
    preview_sheet(done).save(OUT / "preview-sheet.png", optimize=True)
    print(f"готово: {len(done)} кадров × 2 размера → {OUT}")


if __name__ == "__main__":
    main()
