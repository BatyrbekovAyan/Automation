#!/usr/bin/env python3
"""Compose store listing frames from the raw capture in Tools/store/screenshots/.

Each listing frame = brand background + caption block + a phone frame (bezel, rounded
screen, marketing status bar) holding the raw 1284×2778 capture. Two targets share the
raw frames and the copy:

  • iOS  — rendered once at the 6.9" size ASC asks for (1320×2868) with a Dynamic Island
           and the «9:41» status bar, then downscaled to the 6.5" fallback (1284×2778; the
           0.4 % aspect drift is invisible).
  • Play — 1080×1920, the 9:16 portrait Google recommends for phone screenshots (any other
           ratio uploads but forfeits featuring). The phone is a punch-hole Android with a
           Material status bar, sized so the whole screen fits under the caption — the
           «Вместе» cards on the first frame live at the bottom of the screen, and the usual
           bleed-off-the-bottom Play composition cut them away.

Re-run after every Tools/Store/Capture Screenshots — the raw frames are the only input:

    python3 Tools/store/compose-listing.py            # both targets
    python3 Tools/store/compose-listing.py ios        # → Tools/store/listing/{ios-6.9,ios-6.5}/ + preview-sheet.png
    python3 Tools/store/compose-listing.py play       # → Tools/store/listing/play-phone/ + play-preview-sheet.png

The order and copy of FRAMES is the listing order (docs/store/submission-checklist.md).
The iOS output is byte-identical to the pre-Play version of this script (verified by md5
on 2026-09-05) — keep it that way: the ASC upload was made from it.
"""
from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / "Tools/store/screenshots"
OUT = ROOT / "Tools/store/listing"
FONTS = ROOT / "Assets/TextMesh Pro/Fonts"
# The Android chrome (status-bar time) uses a system font: SF Pro is licensed for Apple
# platforms only, and the raw frames carry no OS chrome of their own.
SYSTEM_FONT = Path("/System/Library/Fonts/HelveticaNeue.ttc")

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
    # Prices on this frame are PlanCatalog's fallback text (the Editor has no StoreKit); they equal
    # the ASC-confirmed grid (monetization spec, 2026-08-25). The «от 9 990 ₸» below is Start/month —
    # keep it in step with PlanCatalog if the grid ever moves.
    ("05-paywall",           "07-plans",      "Все функции в каждом тарифе",       "Платите только за масштаб — от 9 990 ₸ в месяц"),
]

BG = (10, 13, 20)                     # deeper and bluer than the app's #0D0D0D so the phone reads as an object
GLOW = (74, 99, 184)                  # the app's indigo, lifted
HEADLINE = (244, 246, 250)
SUBLINE = (154, 163, 178)
BEZEL = (26, 29, 36)
BEZEL_EDGE = (48, 52, 60)


@dataclass(frozen=True)
class Spec:
    """One store target's frame geometry. Every value the iOS composition used as a module
    constant is here verbatim; the Play values are derived from the 1320 → 1080 width ratio
    and then tuned so the caption keeps its weight and the whole screen stays in frame."""
    name: str
    canvas: tuple[int, int]
    fallback: tuple[int, int] | None     # a second, downscaled export (iOS 6.5")
    screen_w: int                        # screen width inside the bezel
    bezel_px: int
    phone_top: int                       # same on every frame so the set lines up in the gallery
    margin_x: int
    caption_top: int
    headline_pt: int
    headline_lh: int
    subline_pt: int
    subline_lh: int
    caption_gap: int
    android: bool                        # punch-hole + Material status bar instead of Island + 9:41
    out_dirs: tuple[str, ...]
    sheet: str

    @property
    def k(self) -> float:
        """Effects scale (glow, shadow) relative to the iOS canvas the numbers were tuned on."""
        return self.canvas[0] / 1320.0


IOS = Spec(
    name="ios", canvas=(1320, 2868), fallback=(1284, 2778),
    screen_w=980, bezel_px=26, phone_top=600, margin_x=90, caption_top=150,
    headline_pt=92, headline_lh=106, subline_pt=46, subline_lh=60, caption_gap=22,
    android=False, out_dirs=("ios-6.9", "ios-6.5"), sheet="preview-sheet.png",
)
# The Play phone is sized so the WHOLE screen fits under the caption (only the last ~35 px —
# the home-indicator strip — leave the canvas): the first frame's payload, the «Вместе» cards,
# sits at the bottom of the screen and a bleed-off composition cut it away.
PLAY = Spec(
    name="play", canvas=(1080, 1920), fallback=None,
    screen_w=700, bezel_px=20, phone_top=392, margin_x=72, caption_top=104,
    headline_pt=72, headline_lh=84, subline_pt=38, subline_lh=50, caption_gap=16,
    android=True, out_dirs=("play-phone",), sheet="play-preview-sheet.png",
)
SPECS = {"ios": IOS, "play": PLAY}


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / f"SFProText-{name}.ttf"), size)


def system_font(size: int, index: int = 10) -> ImageFont.FreeTypeFont:
    """Helvetica Neue from the .ttc: 0 Regular, 1 Bold, 10 Medium."""
    return ImageFont.truetype(str(SYSTEM_FONT), size, index=index)


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


def caption_fonts(spec: Spec) -> tuple[ImageFont.FreeTypeFont, ImageFont.FreeTypeFont]:
    """SF Pro for the App Store frames; the system Helvetica Neue for Google-facing ones —
    Apple's SF license covers Apple platforms only, and the Play captions are marketing
    material for another store."""
    if spec.android:
        return system_font(spec.headline_pt, 1), system_font(spec.subline_pt, 0)
    return font("Bold", spec.headline_pt), font("Regular", spec.subline_pt)


def draw_caption(spec: Spec, draw: ImageDraw.ImageDraw, headline: str, subline: str) -> int:
    """Returns the y where the caption block ends."""
    hf, sf = caption_fonts(spec)
    max_w = spec.canvas[0] - 2 * spec.margin_x
    y = spec.caption_top
    cx = spec.canvas[0] // 2   # centred on the phone's axis — the frames sit side by side in the gallery
    for line in wrap(headline, hf, max_w):
        draw.text((cx, y), line, font=hf, fill=HEADLINE, anchor="ma")
        y += spec.headline_lh
    y += spec.caption_gap
    for line in wrap(subline, sf, max_w):
        draw.text((cx, y), line, font=sf, fill=SUBLINE, anchor="ma")
        y += spec.subline_lh
    return y


def status_icons(draw: ImageDraw.ImageDraw, x0: int, cy: float, w: int, s: float) -> None:
    """Signal · Wi-Fi · battery, right-aligned; shared by both chromes."""
    white = (255, 255, 255)
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


def status_bar(draw: ImageDraw.ImageDraw, x0: int, y0: int, w: int) -> None:
    """Apple's marketing status bar: 9:41, full signal, Wi-Fi, full battery. Proportions
    are taken from a 430pt-wide iPhone and scaled by the screen width."""
    s = w / 430.0
    white = (255, 255, 255)
    tf = font("Semibold", int(17 * s * 1.05))
    time_cx = x0 + 61 * s
    draw.text((time_cx, y0 + 24.5 * s), "9:41", font=tf, fill=white, anchor="mm")
    status_icons(draw, x0, y0 + 24.5 * s, w, s)


def android_status_bar(draw: ImageDraw.ImageDraw, x0: int, y0: int, w: int) -> None:
    """Material status bar: time at the left, icons at the right, camera punch-hole at the
    top centre. Proportions from a 412dp-wide Pixel, scaled by the screen width."""
    s = w / 412.0
    white = (255, 255, 255)
    tf = system_font(int(15 * s * 1.05))
    draw.text((x0 + 26 * s, y0 + 22 * s), "12:30", font=tf, fill=white, anchor="lm")
    status_icons(draw, x0, y0 + 22 * s, w, s * 0.95)
    hole_r = 11 * s
    draw.ellipse((x0 + w / 2 - hole_r, y0 + 12 * s, x0 + w / 2 + hole_r, y0 + 12 * s + 2 * hole_r), fill=(0, 0, 0))


def phone(spec: Spec, canvas: Image.Image, raw: Image.Image) -> None:
    sw = spec.screen_w
    sh = round(sw * raw.height / raw.width)
    bz = spec.bezel_px
    ox = (spec.canvas[0] - sw) // 2 - bz
    oy = spec.phone_top
    outer = (ox, oy, ox + sw + 2 * bz, oy + sh + 2 * bz)
    r_in = round(sw * 0.135)
    r_out = r_in + bz
    k = spec.k

    # glow + shadow live on their own layers so the blur never touches the caption
    fx = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(fx)
    gcx, gcy = spec.canvas[0] // 2, oy + sh * 0.72
    d.ellipse((gcx - round(820 * k), gcy - round(640 * k), gcx + round(820 * k), gcy + round(640 * k)), fill=GLOW + (92,))
    fx = fx.filter(ImageFilter.GaussianBlur(round(210 * k)))
    canvas.alpha_composite(fx)
    sh_layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh_layer).rounded_rectangle(
        (outer[0], outer[1] + round(46 * k), outer[2], outer[3] + round(46 * k)), radius=r_out, fill=(0, 0, 0, 150))
    canvas.alpha_composite(sh_layer.filter(ImageFilter.GaussianBlur(round(48 * k))))

    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(outer, radius=r_out, fill=BEZEL, outline=BEZEL_EDGE, width=2)

    screen = raw.resize((sw, sh), Image.LANCZOS).convert("RGBA")
    mask = Image.new("L", (sw, sh), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, sw - 1, sh - 1), radius=r_in, fill=255)
    sx, sy = ox + bz, oy + bz
    canvas.paste(screen, (sx, sy), mask)

    if spec.android:
        android_status_bar(draw, sx, sy, sw)
        return

    # Dynamic Island + status bar sit in the header's empty safe-area strip.
    s = sw / 430.0
    iw, ih = 126 * s, 37 * s
    icx, itop = sx + sw / 2, sy + 11 * s
    draw.rounded_rectangle((icx - iw / 2, itop, icx + iw / 2, itop + ih), radius=ih / 2, fill=(0, 0, 0))
    status_bar(draw, sx, sy, sw)


def compose(spec: Spec, raw_name: str, headline: str, subline: str) -> Image.Image:
    src = RAW / f"{raw_name}.png"
    if not src.exists():
        sys.exit(f"нет сырого кадра {src} — сначала Tools/Store/Capture Screenshots")
    raw = Image.open(src).convert("RGB")
    canvas = Image.new("RGBA", spec.canvas, BG + (255,))
    phone(spec, canvas, raw)
    end = draw_caption(spec, ImageDraw.Draw(canvas), headline, subline)
    if end > spec.phone_top - 40:
        print(f"  ! подпись «{headline}» доходит до y={end}, телефон с y={spec.phone_top} — перекрытие")
    return canvas.convert("RGB")


def preview_sheet(spec: Spec, frames: list[tuple[str, Image.Image]]) -> Image.Image:
    thumb_w, gap, label_h = 330, 28, 54
    thumb_h = round(thumb_w * spec.canvas[1] / spec.canvas[0])
    sheet = Image.new("RGB", (gap + len(frames) * (thumb_w + gap), gap + thumb_h + label_h), (24, 27, 34))
    lf = font("Medium", 24)
    d = ImageDraw.Draw(sheet)
    for i, (slug, im) in enumerate(frames):
        x = gap + i * (thumb_w + gap)
        sheet.paste(im.resize((thumb_w, thumb_h), Image.LANCZOS), (x, gap))
        d.text((x + thumb_w / 2, gap + thumb_h + 30), slug, font=lf, fill=(200, 205, 214), anchor="mm")
    return sheet


def run(spec: Spec) -> None:
    dirs = [OUT / d for d in spec.out_dirs]
    for d in dirs:
        d.mkdir(parents=True, exist_ok=True)
    done = []
    for raw_name, slug, headline, subline in FRAMES:
        im = compose(spec, headline=headline, subline=subline, raw_name=raw_name)
        im.save(dirs[0] / f"{slug}.png", optimize=True)
        if spec.fallback is not None:
            im.resize(spec.fallback, Image.LANCZOS).save(dirs[1] / f"{slug}.png", optimize=True)
        done.append((slug, im))
        print(f"  [{spec.name}] {slug}.png  ← {raw_name}  «{headline}»")
    preview_sheet(spec, done).save(OUT / spec.sheet, optimize=True)
    print(f"готово [{spec.name}]: {len(done)} кадров → {', '.join(str(d) for d in dirs)}")


def main() -> None:
    wanted = sys.argv[1:] or list(SPECS)
    unknown = [w for w in wanted if w not in SPECS]
    if unknown:
        sys.exit(f"неизвестная цель {unknown}; допустимо: {', '.join(SPECS)}")
    for name in wanted:
        run(SPECS[name])


if __name__ == "__main__":
    main()
