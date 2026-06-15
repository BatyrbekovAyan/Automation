#!/usr/bin/env python3
"""Generate a WhatsApp-style doodle wallpaper matching the project's existing
chat background (#F9F5EC bg, #9A8A7A 5px-at-2x strokes) with fresh motifs.

Modes:
  python3 gen_doodles.py sheet   -> motif contact sheet for QA
  python3 gen_doodles.py final   -> full 1596x2688 wallpaper SVG
"""
import random, sys, math

BG = "#F5F2EA"
INK = "#E5DAC6"   # warm tan — #E8E1D9 read grayish at this low contrast
W, H = 1596, 2688

# ---------------------------------------------------------------- helpers
def dot(cx, cy, r=3.2):
    return f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{INK}" stroke="none"/>'

def C(cx, cy, r):
    return f'<circle cx="{cx}" cy="{cy}" r="{r}"/>'

def P(d):
    return f'<path d="{d}"/>'

def E(cx, cy, rx, ry, rot=0):
    t = f' transform="rotate({rot} {cx} {cy})"' if rot else ''
    return f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}"{t}/>'

def L(x1, y1, x2, y2):
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}"/>'

def R(x, y, w, h, rx=0):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}"/>'

def star_path(cx, cy, r_out, r_in, points=5, rot=-90):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 180.0 / points)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d = "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + " Z"
    return P(d)

def sparkle4(cx, cy, r):
    q = r * 0.18
    d = (f"M{cx} {cy-r} Q{cx+q} {cy-q} {cx+r} {cy} Q{cx+q} {cy+q} {cx} {cy+r} "
         f"Q{cx-q} {cy+q} {cx-r} {cy} Q{cx-q} {cy-q} {cx} {cy-r} Z")
    return P(d)

def heart_path(cx, cy, s):
    return P(f"M{cx} {cy+s*0.62} C{cx-s*1.0} {cy-0.05*s} {cx-s*0.62} {cy-s*0.75} {cx} {cy-s*0.28} "
             f"C{cx+s*0.62} {cy-s*0.75} {cx+s*1.0} {cy-0.05*s} {cx} {cy+s*0.62} Z")

# ---------------------------------------------------------------- motifs
# Each motif: list of svg elements drawn inside a 0..100 box, stroke applied
# at group level. 'sz' = relative footprint multiplier.
MOTIFS = {}

def motif(name, sz=1.0):
    def deco(fn):
        MOTIFS[name] = (fn(), sz)
        return fn
    return deco

@motif("balloon", 1.15)
def _():
    return [
        C(50, 34, 24),
        P("M50 10 C40 24 40 44 50 58"), P("M50 10 C60 24 60 44 50 58"),
        P("M33 50 L44 76"), P("M67 50 L56 76"),
        R(42, 76, 16, 13, 3),
    ]

@motif("paper_plane", 1.0)
def _():
    return [
        P("M12 50 L88 18 L58 82 L46 60 Z"), P("M88 18 L46 60"),
        P("M30 78 Q40 70 50 78" ), dot(58, 84, 2.6),
    ]

@motif("headphones", 1.0)
def _():
    return [
        P("M26 58 L26 50 A24 24 0 0 1 74 50 L74 58"),
        R(20, 56, 13, 22, 6.5), R(67, 56, 13, 22, 6.5),
    ]

@motif("gamepad", 1.0)
def _():
    return [
        R(22, 38, 56, 30, 15),
        P("M38 46 V60"), P("M31 53 H45"),
        dot(62, 48, 3.4), dot(69, 57, 3.4),
    ]

@motif("coffee", 0.95)
def _():
    return [
        P("M30 44 H64 V68 A10 10 0 0 1 54 78 H40 A10 10 0 0 1 30 68 Z"),
        P("M64 50 A9 9 0 0 1 64 66"),
        P("M40 18 Q44 26 40 34"), P("M53 14 Q57 22 53 30"),
    ]

@motif("donut", 0.95)
def _():
    out = [C(50, 52, 26), C(50, 52, 10)]
    for a in (15, 70, 130, 200, 260, 320):
        r1, r2 = 15, 20
        x1 = 50 + r1 * math.cos(math.radians(a)); y1 = 52 + r1 * math.sin(math.radians(a))
        x2 = 50 + r2 * math.cos(math.radians(a + 14)); y2 = 52 + r2 * math.sin(math.radians(a + 14))
        out.append(L(round(x1,1), round(y1,1), round(x2,1), round(y2,1)))
    return out

@motif("pizza", 1.0)
def _():
    return [
        P("M24 26 Q50 12 76 26"), P("M24 26 L50 84 L76 26"),
        P("M27 33 Q50 21 73 33"),
        dot(45, 42, 3.6), dot(57, 52, 3.6), dot(47, 63, 3.6),
    ]

@motif("icecream", 1.0)
def _():
    return [
        P("M31 46 A20 20 0 0 1 69 46"),
        P("M31 46 Q36 52 41 46 Q45 52 50 46 Q55 52 60 46 Q64 52 69 46"),
        P("M33 50 L50 86 L67 50"),
        P("M41 54 L58 62"), P("M59 54 L44 68"),
    ]

@motif("robot", 0.95)
def _():
    return [
        R(28, 34, 44, 36, 8),
        L(50, 34, 50, 22), dot(50, 19, 3.4),
        dot(40, 48, 3.6), dot(60, 48, 3.6),
        P("M41 59 H59"),
        L(28, 44, 20, 44), L(72, 44, 80, 44),
    ]

@motif("ufo", 1.05)
def _():
    return [
        E(50, 54, 31, 11),
        P("M31 49 A19 16 0 0 1 69 49"),
        dot(36, 54, 2.6), dot(50, 57, 2.6), dot(64, 54, 2.6),
        L(40, 70, 36, 78), L(60, 70, 64, 78), L(50, 71, 50, 80),
    ]

@motif("kite", 1.05)
def _():
    return [
        P("M50 12 L70 40 L50 72 L30 40 Z"),
        L(50, 12, 50, 72), L(30, 40, 70, 40),
        P("M50 72 Q58 80 50 88 Q42 94 50 100" ),
    ]

@motif("skateboard", 0.95)
def _():
    return [
        P("M16 48 Q22 58 32 58 L68 58 Q78 58 84 48"),
        C(34, 68, 6), C(66, 68, 6),
    ]

@motif("umbrella", 1.05)
def _():
    return [
        P("M20 50 A30 30 0 0 1 80 50"),
        P("M20 50 Q27.5 58 35 50 Q42.5 58 50 50 Q57.5 58 65 50 Q72.5 58 80 50"),
        L(50, 20, 50, 14), P("M50 54 V76 A7 7 0 0 1 36 76"),
    ]

@motif("sailboat", 1.1)
def _():
    return [
        P("M24 66 H76 L68 80 H32 Z"),
        L(50, 66, 50, 20),
        P("M50 24 L76 62 H50 Z"), P("M46 34 L30 62 H46 Z"),
    ]

@motif("owl", 1.1)
def _():
    return [
        P("M50 20 C32 20 26 38 26 55 C26 74 36 84 50 84 C64 84 74 74 74 55 C74 38 68 20 50 20 Z"),
        P("M33 24 L28 13"), P("M67 24 L72 13"),
        C(40, 43, 8), C(60, 43, 8), dot(40, 43, 2.8), dot(60, 43, 2.8),
        P("M46 54 H54 L50 61 Z"),
        P("M42 68 L46 72 L50 68 L54 72 L58 68"),
    ]

@motif("fish", 0.95)
def _():
    return [
        P("M20 50 Q44 26 66 44 Q74 50 66 56 Q44 74 20 50 Z"),
        P("M66 44 L82 36 L78 50 L82 64 L66 56"),
        dot(34, 46, 2.8), P("M46 40 Q52 50 46 60"),
        C(78, 22, 3.5), C(86, 14, 2.5),
    ]

@motif("jellyfish", 1.0)
def _():
    return [
        P("M28 50 A22 22 0 0 1 72 50 Q66 56 60 50 Q55 56 50 50 Q45 56 40 50 Q34 56 28 50 Z"),
        P("M36 56 Q32 66 38 74 Q42 82 36 90"),
        P("M50 58 Q46 68 52 76 Q56 84 50 92"),
        P("M64 56 Q68 66 62 74 Q58 82 64 90"),
        dot(42, 40, 2.6), dot(58, 40, 2.6),
    ]

@motif("butterfly", 1.05)
def _():
    return [
        L(50, 38, 50, 66),
        P("M50 38 Q44 28 38 24"), dot(38, 24, 2.4),
        P("M50 38 Q56 28 62 24"), dot(62, 24, 2.4),
        P("M48 44 C32 26 16 36 24 50 C28 58 42 56 48 50 Z"),
        P("M52 44 C68 26 84 36 76 50 C72 58 58 56 52 50 Z"),
        P("M48 54 C36 52 26 60 32 70 C36 77 46 72 48 64 Z"),
        P("M52 54 C64 52 74 60 68 70 C64 77 54 72 52 64 Z"),
    ]

@motif("daisy", 1.0)
def _():
    out = [C(50, 38, 8)]
    for a in range(0, 360, 45):
        out.append(E(50, 21, 6, 10, a))
    # fix petals: place around center (50,38) radius 17
    out = [C(50, 38, 8)]
    for a in range(0, 360, 45):
        px = 50 + 17 * math.cos(math.radians(a))
        py = 38 + 17 * math.sin(math.radians(a))
        out.append(E(round(px,1), round(py,1), 5.5, 9.5, a + 90))
    out.append(P("M50 46 V84"))
    out.append(P("M50 70 Q36 68 33 57 Q46 58 50 68 Z"))
    return out

@motif("mountains", 1.15)
def _():
    return [
        P("M14 74 L40 34 L54 56 L67 36 L88 74"),
        P("M34 43 L40 50 L45 42"),
        C(78, 24, 7),
    ]

@motif("tent", 1.0)
def _():
    return [
        P("M24 74 L50 28 L76 74 Z"),
        P("M42 74 L50 56 L58 74"),
        L(18, 74, 82, 74), L(50, 28, 44, 20),
    ]

@motif("compass", 0.95)
def _():
    return [
        C(50, 50, 25),
        P("M50 32 L57 50 L50 68 L43 50 Z"), dot(50, 50, 3),
        L(50, 25, 50, 20), L(50, 80, 50, 75), L(20, 50, 25, 50), L(80, 50, 75, 50),
    ]

@motif("book", 1.05)
def _():
    return [
        P("M50 34 C42 27 28 27 22 31 V67 C28 63 42 63 50 69 C58 63 72 63 78 67 V31 C72 27 58 27 50 34 Z"),
        P("M50 34 V69"),
        P("M28 38 Q38 35 44 39"), P("M28 46 Q38 43 44 47"),
        P("M56 39 Q62 35 72 38"), P("M56 47 Q62 43 72 46"),
    ]

@motif("pencil", 1.0)
def _():
    return [
        f'<g transform="rotate(42 50 50)">' ,
        R(43, 14, 14, 50, 2),
        P("M43 64 L50 82 L57 64"), L(43, 24, 57, 24), L(50, 64, 50, 72),
        '</g>',
    ]

@motif("palette", 1.05)
def _():
    return [
        P("M50 24 C28 24 16 42 21 58 C26 71 41 78 53 73 C60 70 56 62 63 59 C72 56 79 52 79 43 C79 31 66 24 50 24 Z"),
        dot(38, 40, 4), dot(54, 36, 4), dot(33, 54, 4),
    ]

@motif("wifi", 0.85)
def _():
    return [
        P("M28 46 A30 30 0 0 1 72 46"),
        P("M37 56 A18 18 0 0 1 63 56"),
        dot(50, 67, 4),
    ]

@motif("clock", 1.0)
def _():
    return [
        C(50, 52, 23),
        P("M50 52 V37"), P("M50 52 H61"),
        P("M30 34 A11 11 0 0 1 42 26"), P("M70 34 A11 11 0 0 0 58 26"),
        L(36, 72, 31, 79), L(64, 72, 69, 79),
    ]

@motif("gift", 1.0)
def _():
    return [
        R(28, 46, 44, 32), R(24, 36, 52, 10, 2),
        L(50, 36, 50, 78),
        P("M50 35 C40 18 24 26 42 35"), P("M50 35 C60 18 76 26 58 35"),
    ]

@motif("party_balloon", 0.95)
def _():
    return [
        E(50, 38, 18, 22),
        P("M46 60 H54 L50 67 Z"),
        P("M50 67 C42 77 58 85 50 94"),
    ]

@motif("watermelon", 1.0)
def _():
    return [
        P("M20 44 A30 30 0 0 0 80 44 Z"),
        P("M26 44 A24 24 0 0 0 74 44"),
        E(44, 56, 2.2, 4, -20), E(56, 56, 2.2, 4, 20), E(50, 64, 2.2, 4, 0),
    ]

@motif("boombox", 1.1)
def _():
    return [
        R(22, 40, 56, 30, 6),
        P("M38 40 V32 H62 V40"),
        C(34, 55, 8), C(66, 55, 8), dot(34, 55, 2.2), dot(66, 55, 2.2),
        R(46, 47, 8, 5, 1.5),
    ]

@motif("laptop", 1.0)
def _():
    return [
        R(31, 30, 38, 27, 3),
        P("M23 66 H77 L70 57 H30 Z"),
    ]

@motif("dice", 0.9)
def _():
    return [
        R(31, 31, 38, 38, 8),
        dot(41, 41, 3), dot(59, 41, 3), dot(50, 50, 3), dot(41, 59, 3), dot(59, 59, 3),
    ]

@motif("crown", 0.95)
def _():
    return [
        P("M28 64 L23 38 L39 50 L50 31 L61 50 L77 38 L72 64 Z"),
        P("M30 71 H70"),
        dot(50, 44, 2.4),
    ]

@motif("key", 0.9)
def _():
    return [
        C(36, 38, 11),
        P("M44 46 L72 74"), P("M62 64 L69 57"), P("M69 71 L76 64"),
    ]

@motif("rainbow", 1.1)
def _():
    return [
        P("M24 64 A26 26 0 0 1 76 64"),
        P("M33 64 A17 17 0 0 1 67 64"),
        P("M42 64 A8 8 0 0 1 58 64"),
        C(22, 68, 5), C(30, 70, 4), C(78, 68, 5), C(70, 70, 4),
    ]

@motif("raincloud", 1.0)
def _():
    return [
        P("M30 56 A12 12 0 0 1 33 33 A15 15 0 0 1 61 29 A12 12 0 0 1 71 56 Z"),
        L(36, 64, 32, 74), L(50, 64, 46, 74), L(64, 64, 60, 74),
    ]

@motif("sun", 0.9)
def _():
    out = [C(50, 50, 14)]
    for a in range(0, 360, 45):
        x1 = 50 + 20 * math.cos(math.radians(a)); y1 = 50 + 20 * math.sin(math.radians(a))
        x2 = 50 + 27 * math.cos(math.radians(a)); y2 = 50 + 27 * math.sin(math.radians(a))
        out.append(L(round(x1,1), round(y1,1), round(x2,1), round(y2,1)))
    return out

@motif("moon", 0.95)
def _():
    return [
        P("M60 20 A27 27 0 1 0 60 80 A22 22 0 1 1 60 20 Z"),
        sparkle4(76, 36, 7),
    ]

@motif("microphone", 0.95)
def _():
    return [
        R(42, 20, 16, 32, 8),
        P("M34 46 A16 16 0 0 0 66 46"),
        L(50, 62, 50, 74), L(40, 74, 60, 74),
        L(42, 31, 58, 31), L(42, 39, 58, 39),
    ]

@motif("lightning", 0.9)
def _():
    return [P("M55 16 L33 52 H47 L41 84 L67 44 H52 Z")]

@motif("anchor", 1.0)
def _():
    return [
        C(50, 23, 6),
        L(50, 29, 50, 72), L(36, 40, 64, 40),
        P("M28 56 A22 22 0 0 0 72 56"),
        P("M28 56 L21 53"), P("M28 56 L30 49"),
        P("M72 56 L79 53"), P("M72 56 L70 49"),
    ]

@motif("snail", 1.0)
def _():
    return [
        C(56, 49, 15), C(56, 49, 6),
        P("M20 64 H76"),
        P("M28 64 C26 52 28 44 34 41"),
        P("M34 41 L29 31"), dot(29, 30, 2.4),
        P("M34 41 L40 32"), dot(40, 31, 2.4),
    ]

@motif("bee", 0.9)
def _():
    return [
        E(50, 56, 15, 10.5, -12),
        P("M45 47 L48 66"), P("M53 45 L56 64"),
        E(43, 37, 7, 11, -30), E(59, 36, 7, 11, 25),
        P("M64 50 L71 46"), dot(36, 50, 2.2),
        P("M20 70 Q26 76 34 72" ),
    ]

@motif("cherries", 1.0)
def _():
    return [
        P("M52 18 Q38 36 34 52"), P("M52 18 Q62 34 64 50"),
        C(33, 61, 10), C(65, 59, 10),
        P("M52 18 Q62 8 70 12 Q66 22 52 18 Z"),
    ]

@motif("lollipop", 0.9)
def _():
    return [
        C(50, 36, 15),
        P("M50 28 A8 8 0 0 1 58 36 A8 8 0 0 1 42 36"),
        L(50, 51, 50, 84),
    ]

@motif("magnifier", 0.9)
def _():
    return [
        C(42, 42, 17),
        P("M55 55 L75 75"),
    ]

@motif("popcorn", 1.0)
def _():
    return [
        P("M30 48 L36 82 H64 L70 48"),
        P("M40 50 L43 80"), P("M50 49 V81"), P("M60 50 L57 80"),
        P("M30 48 A8 8 0 0 1 38 36 A9 9 0 0 1 53 31 A9 9 0 0 1 65 38 A8 8 0 0 1 70 48"),
    ]

@motif("burger", 1.0)
def _():
    return [
        P("M28 46 A22 15 0 0 1 72 46"),
        L(28, 46, 72, 46),
        P("M28 53 Q33 59 39 53 Q44 59 50 53 Q55 59 61 53 Q66 59 72 53"),
        P("M28 60 H72 V63 A8 8 0 0 1 64 70 H36 A8 8 0 0 1 28 63 Z"),
        L(42, 38, 45, 36), L(53, 35, 56, 37),
    ]

@motif("sprout", 0.95)
def _():
    return [
        P("M36 62 H64 L60 82 H40 Z"), L(33, 62, 67, 62),
        L(50, 62, 50, 44),
        P("M50 48 Q36 46 33 33 Q47 35 50 48 Z"),
        P("M50 48 Q64 46 67 33 Q53 35 50 48 Z"),
    ]

@motif("music_notes", 0.9)
def _():
    return [
        L(42, 30, 42, 62), L(64, 26, 64, 58),
        P("M42 30 L64 26"),
        E(37, 64, 6, 4.5, -20), E(59, 60, 6, 4.5, -20),
    ]

@motif("smiley_wink", 0.8)
def _():
    return [
        C(50, 50, 17),
        dot(43, 45, 2.6), P("M54 45 H62"),
        P("M42 56 Q50 63 58 56"),
    ]

@motif("smiley_laugh", 0.8)
def _():
    return [
        C(50, 50, 17),
        P("M40 44 Q43 40 46 44"), P("M54 44 Q57 40 60 44"),
        P("M41 53 Q50 64 59 53 Z"),
    ]

@motif("heart_big", 0.85)
def _():
    return [heart_path(50, 50, 26)]

@motif("star_big", 0.85)
def _():
    return [star_path(50, 50, 24, 10)]

@motif("txt_lol", 0.8)
def _():
    return ['<text x="50" y="62" text-anchor="middle" font-family="Chalkboard SE, Comic Sans MS" '
            f'font-size="38" font-weight="bold" fill="none" stroke="{INK}">LOL</text>']

@motif("txt_hey", 0.8)
def _():
    return ['<text x="50" y="62" text-anchor="middle" font-family="Chalkboard SE, Comic Sans MS" '
            f'font-size="36" font-weight="bold" fill="none" stroke="{INK}">HEY!</text>']

@motif("txt_gg", 0.75)
def _():
    return ['<text x="50" y="62" text-anchor="middle" font-family="Chalkboard SE, Comic Sans MS" '
            f'font-size="40" font-weight="bold" fill="none" stroke="{INK}">GG</text>']

@motif("cat", 1.0)
def _():
    return [
        C(50, 52, 25),
        P("M31 33 L27 14 L45 27"), P("M69 33 L73 14 L55 27"),
        dot(41, 49, 3), dot(59, 49, 3),
        P("M47 56 L53 56 L50 60 Z"),
        L(20, 55, 38, 55), L(20, 61, 38, 58), L(62, 55, 80, 55), L(62, 58, 80, 61),
        P("M50 60 Q44 66 39 62"), P("M50 60 Q56 66 61 62"),
    ]

@motif("ghost", 1.05)
def _():
    return [
        P("M26 86 V46 A24 24 0 0 1 74 46 V86 L66 79 L58 86 L50 79 L42 86 L34 79 Z"),
        E(41, 48, 4.5, 6.5), E(59, 48, 4.5, 6.5),   # big round eyes
        E(50, 65, 4.5, 6),                            # open "boo" mouth
    ]

@motif("mushroom", 1.0)
def _():
    return [
        P("M22 50 A28 21 0 0 1 78 50 Z"), L(24, 50, 76, 50),
        P("M41 50 V74 A9 7 0 0 0 59 74 V50"),
        dot(38, 39, 4), dot(57, 35, 5), dot(64, 45, 3),
    ]

@motif("cactus", 1.0)
def _():
    return [
        P("M44 82 V40 A6 6 0 0 1 56 40 V82"),
        P("M44 60 H35 A5 5 0 0 0 30 65 V55"),
        P("M56 54 H65 A5 5 0 0 1 70 59 V48"),
        P("M36 82 H64 L60 94 H40 Z"), L(34, 82, 66, 82),
    ]

@motif("rocket", 1.05)
def _():
    return [
        P("M50 15 C40 25 38 50 41 64 H59 C62 50 60 25 50 15 Z"),
        C(50, 39, 7),
        P("M41 56 L30 72 L42 66"), P("M59 56 L70 72 L58 66"),
        P("M45 64 Q50 80 55 64"),
    ]

@motif("camera", 1.05)
def _():
    return [
        R(23, 39, 54, 35, 6),
        P("M39 39 L43 30 H57 L61 39"),
        C(50, 57, 12), C(50, 57, 5),
        dot(67, 47, 3),
    ]

@motif("cassette", 1.0)
def _():
    return [
        R(22, 34, 56, 33, 5),
        R(31, 39, 38, 10, 2),
        C(38, 57, 6), C(62, 57, 6), dot(38, 57, 2), dot(62, 57, 2),
        L(44, 57, 56, 57),
    ]

@motif("planet", 1.1)
def _():
    return [
        C(50, 50, 19),
        E(50, 50, 33, 11, -22),
        dot(44, 45, 3), dot(57, 53, 2.5),
    ]

@motif("snowman", 1.0)
def _():
    return [
        C(50, 67, 17), C(50, 39, 12),
        dot(46, 37, 2), dot(54, 37, 2),
        P("M50 41 L59 43 L50 45"),
        dot(50, 61, 2.5), dot(50, 70, 2.5),
        L(33, 58, 18, 49), L(67, 58, 82, 49),
    ]

@motif("star_eyes", 0.85)
def _():
    return [
        C(50, 50, 18),
        star_path(43, 46, 4.5, 1.9), star_path(57, 46, 4.5, 1.9),
        P("M39 55 Q50 65 61 55"),
    ]

@motif("bird", 0.95)
def _():
    return [
        C(50, 54, 22),
        P("M41 33 Q45 24 50 31"),
        P("M28 50 L14 54 L28 58 Z"),
        dot(40, 47, 2.8),
        P("M50 56 Q63 50 70 61 Q60 65 50 61"),
        P("M72 54 L88 50 L85 63"),
        L(44, 76, 44, 85), L(56, 76, 56, 85),
    ]

@motif("whale", 1.1)
def _():
    return [
        P("M16 54 Q20 38 46 38 Q74 38 80 54 Q80 62 68 62 L30 62 Q16 62 16 54 Z"),
        P("M80 50 L94 42 L90 55 L96 62 L80 58"),
        dot(34, 49, 2.6),
        P("M40 33 Q40 24 35 18"), P("M46 33 Q48 24 53 19"),
        P("M28 60 Q44 66 62 60"),
    ]

@motif("ladybug", 0.9)
def _():
    return [
        E(50, 57, 22, 25),
        C(50, 32, 9),
        L(50, 41, 50, 82),
        dot(40, 55, 4), dot(60, 55, 4), dot(43, 69, 3.5), dot(57, 69, 3.5),
        L(46, 26, 42, 17), dot(42, 16, 2), L(54, 26, 58, 17), dot(58, 16, 2),
    ]

@motif("gem", 0.95)
def _():
    return [
        P("M36 26 H64 L78 40 H22 Z"),
        P("M22 40 L50 82 L78 40"),
        L(36, 26, 44, 40), L(64, 26, 56, 40),
        L(44, 40, 50, 82), L(56, 40, 50, 82),
        L(44, 40, 56, 40),
    ]

@motif("alien", 0.95)
def _():
    return [
        P("M50 18 C30 18 24 38 28 56 C31 70 40 80 50 80 C60 80 69 70 72 56 C76 38 70 18 50 18 Z"),
        E(40, 50, 6.5, 10, 20), E(60, 50, 6.5, 10, -20),
        P("M44 68 Q50 72 56 68"),
        L(43, 21, 39, 9), dot(39, 8, 2.6), L(57, 21, 61, 9), dot(61, 8, 2.6),
    ]

@motif("dog", 0.95)
def _():
    return [
        C(50, 54, 21),                                   # round head
        P("M33 37 Q21 36 23 52 Q25 61 35 57"),           # left floppy ear
        P("M67 37 Q79 36 77 52 Q75 61 65 57"),           # right floppy ear
        dot(43, 51, 3), dot(57, 51, 3),                  # eyes
        dot(50, 60, 3.6),                                # nose
        P("M50 63 Q45 67 41 64"), P("M50 63 Q55 67 59 64"),  # smile
    ]

@motif("rabbit", 0.95)
def _():
    return [
        C(50, 58, 19),
        E(42, 28, 6.5, 17, -8), E(58, 28, 6.5, 17, 8),
        dot(43, 56, 2.8), dot(57, 56, 2.8),
        P("M47 63 H53 L50 66 Z"),
        L(30, 62, 42, 63), L(58, 63, 70, 62),
    ]

@motif("penguin", 0.95)
def _():
    return [
        P("M50 18 C35 18 31 40 33 60 C35 80 42 88 50 88 C58 88 65 80 67 60 C69 40 65 18 50 18 Z"),
        P("M50 32 C43 32 41 50 43 64 C45 77 50 80 50 80 C50 80 55 77 57 64 C59 50 57 32 50 32 Z"),
        dot(44, 37, 2.4), dot(56, 37, 2.4),
        P("M46 43 L54 43 L50 49 Z"),
        L(40, 88, 36, 93), L(40, 88, 45, 93), L(60, 88, 64, 93), L(60, 88, 55, 93),
    ]

@motif("frog", 0.95)
def _():
    return [
        P("M28 62 A22 18 0 0 1 72 62 Q72 74 60 74 H40 Q28 74 28 62 Z"),
        C(37, 48, 9), C(63, 48, 9), dot(37, 48, 2.6), dot(63, 48, 2.6),
        P("M41 64 Q50 70 59 64"),
        P("M30 72 L24 80 L33 80"), P("M70 72 L76 80 L67 80"),
    ]

@motif("tree", 1.0)
def _():
    return [
        C(50, 38, 24),
        R(45, 60, 10, 26, 2),
        P("M40 40 Q50 48 60 40"),
    ]

@motif("leaf", 0.9)
def _():
    return [
        P("M28 72 Q28 30 72 28 Q70 70 28 72 Z"),
        P("M34 66 Q52 48 66 34"),
        P("M44 60 L50 50"), P("M40 54 Q48 54 50 50"),
    ]

@motif("cupcake", 0.95)
def _():
    return [
        P("M30 50 Q30 32 50 32 Q70 32 70 50 Z"),
        P("M33 50 H67 L61 80 H39 Z"),
        L(43, 50, 39, 80), L(50, 50, 50, 80), L(57, 50, 61, 80),
        C(50, 27, 4),
    ]

@motif("lightbulb", 0.9)
def _():
    return [
        C(50, 42, 20),
        P("M44 44 L48 52 L52 44 L56 52"),
        R(42, 60, 16, 7), L(44, 67, 56, 67), L(45, 71, 55, 71),
    ]

@motif("scissors", 0.9)
def _():
    return [
        C(33, 66, 8), C(67, 66, 8),
        P("M39 62 L70 30"), P("M61 62 L30 30"),
        dot(50, 48, 2.2),
    ]

@motif("candle", 0.9)
def _():
    return [
        R(42, 42, 16, 44, 2),
        P("M42 42 Q50 46 58 42"),
        L(50, 42, 50, 36),
        P("M50 22 Q43 31 50 38 Q57 31 50 22 Z"),
    ]

@motif("trophy", 0.95)
def _():
    return [
        P("M37 28 H63 V40 A13 13 0 0 1 37 40 Z"),
        P("M37 31 Q27 33 31 44"), P("M63 31 Q73 33 69 44"),
        L(50, 53, 50, 64), P("M40 74 H60 L57 64 H43 Z"), L(35, 74, 65, 74),
    ]

@motif("shoe", 0.9)
def _():
    return [
        P("M18 66 L22 50 Q29 46 35 52 L52 58 Q67 60 80 60 Q84 60 84 67 L84 70 L18 70 Z"),
        L(18, 70, 84, 70),
        L(33, 54, 39, 50), L(37, 57, 43, 53),
    ]

@motif("snowflake", 0.9)
def _():
    out = []
    for a in (0, 60, 120):
        import math as _m
        dx, dy = _m.cos(_m.radians(a)), _m.sin(_m.radians(a))
        out.append(L(round(50-34*dx,1), round(50-34*dy,1), round(50+34*dx,1), round(50+34*dy,1)))
    out += [P("M50 20 L45 27"), P("M50 20 L55 27"), P("M50 80 L45 73"), P("M50 80 L55 73")]
    return out

@motif("apple", 0.95)
def _():
    return [
        P("M50 38 C39 30 27 38 29 53 C31 68 42 77 50 73 C58 77 69 68 71 53 C73 38 61 30 50 38 Z"),
        L(50, 38, 52, 27),
        P("M52 31 Q61 27 60 35 Q53 37 52 31 Z"),
    ]

@motif("grapes", 0.95)
def _():
    return [
        C(43, 50, 7), C(57, 50, 7), C(50, 60, 7), C(36, 60, 7), C(64, 60, 7), C(50, 72, 7),
        L(50, 36, 50, 44),
        P("M50 36 Q60 30 64 34 Q58 42 50 36 Z"),
    ]

# ---------------------------------------------------------------- fillers
def F_sparkle(): return [sparkle4(50, 50, 42)]
def F_star():    return [star_path(50, 50, 40, 17)]
def F_heart():   return [heart_path(50, 52, 38)]
def F_dot():     return [dot(50, 50, 26)]
def F_circle():  return [C(50, 50, 34)]
def F_plus():    return [L(50, 14, 50, 86), L(14, 50, 86, 50)]
def F_aster():   return [L(50, 10, 50, 90), L(15, 30, 85, 70), L(15, 70, 85, 30)]
def F_note():    return [L(60, 12, 60, 74), E(48, 78, 13, 9.5, -20), P("M60 12 Q80 20 74 40")]
def F_squig():   return [P("M8 50 Q24 24 40 50 Q56 76 72 50 Q84 32 94 44")]
def F_spiral():  return [P("M50 50 m-30 0 A30 30 0 1 1 50 80 A20 20 0 1 1 70 50 A10 10 0 1 1 50 60")]
def F_diamond(): return [P("M50 12 L82 50 L50 88 L18 50 Z")]
def F_flower():  return [dot(50, 50, 9)] + [E(round(50+24*math.cos(math.radians(a)),1), round(50+24*math.sin(math.radians(a)),1), 11, 15, a+90) for a in range(0, 360, 72)]
def F_dashes():  return [L(14, 30, 50, 30), L(26, 52, 62, 52), L(14, 74, 50, 74)]
def F_moon():    return [P("M58 16 A36 36 0 1 0 58 84 A28 28 0 1 1 58 16 Z")]

FILLERS = [F_sparkle, F_star, F_heart, F_dot, F_circle, F_plus, F_aster, F_note,
           F_squig, F_spiral, F_diamond, F_flower, F_dashes, F_moon]

# ---------------------------------------------------------------- assembly
def instance(elems, cx, cy, size, rot, stroke_final=5.0, text=False):
    s = size / 100.0
    sw = round(stroke_final / s, 2)
    body = "".join(elems)
    if text:
        body = body.replace('stroke="%s">' % INK, 'stroke="%s" stroke-width="%.2f">' % (INK, 2.3 / s))
        sw_attr = ""
    else:
        sw_attr = f' stroke-width="{sw}"'
    return (f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({s:.3f}) translate(-50 -50)"'
            f'{sw_attr}>{body}</g>')

def build(svg_w, svg_h, seed=42):
    rng = random.Random(seed)
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{svg_w}" height="{svg_h}" viewBox="0 0 {svg_w} {svg_h}">',
           f'<rect width="{svg_w}" height="{svg_h}" fill="{BG}"/>',
           f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">']
    # Exclude motifs whose drawings read poorly (ugly faces).
    EXCLUDE = {"penguin", "frog", "bird"}
    names = [n for n in MOTIFS.keys() if n not in EXCLUDE]
    # Deal motifs from a shuffled deck so duplicates are spread out and every
    # motif gets used roughly evenly across the wallpaper.
    deck = []

    def draw_name():
        nonlocal deck
        if not deck:
            deck = names[:]
            rng.shuffle(deck)
        return deck.pop()

    # DENSE but NON-OVERLAPPING packing. Place motifs largest-first with
    # collision avoidance; smaller motifs then pack into the gaps between the
    # big ones, so the field is tightly filled yet nothing crosses. Varied sizes
    # + rotation keep it organic and hand-drawn. Original motifs only — no brand
    # art, no dot/sparkle filler tier.
    # Bounding-BOX collision (boxes tile tightly -> dense; no box overlap ->
    # strokes don't cross). he = half-extent of a motif's square box. EFAC<0.5
    # because the line art sits inside its box with padding, so boxes may abut
    # closely without the drawings touching.
    EFAC, GAP = 0.36, 1.0
    CELL, HEMAX = 180.0, 200.0
    grid = {}
    placed = []                    # (cx, cy, he, name)
    tier = {"L": 0, "M": 0, "S": 0, "F": 0, "T": 0}

    def fits(cx, cy, he):
        span = int((he + GAP + HEMAX) // CELL) + 1
        gx, gy = int(cx // CELL), int(cy // CELL)
        for ix in range(gx - span, gx + span + 1):
            for iy in range(gy - span, gy + span + 1):
                for (px, py, phe, _n) in grid.get((ix, iy), ()):
                    lim = he + phe + GAP
                    if abs(cx - px) < lim and abs(cy - py) < lim:
                        return False
        return True

    def add(cx, cy, he, name):
        grid.setdefault((int(cx // CELL), int(cy // CELL)), []).append((cx, cy, he, name))
        placed.append((cx, cy, he, name))

    def place_tier(lo, hi, key, stop_miss, cap=None):
        miss = 0
        while miss < stop_miss:
            if cap is not None and tier[key] >= cap:
                break
            name = draw_name()
            elems, szmul = MOTIFS[name]
            size = rng.uniform(lo, hi) * szmul
            he = size * EFAC
            cx = rng.uniform(he * 0.5, svg_w - he * 0.5)
            cy = rng.uniform(he * 0.5, svg_h - he * 0.5)
            if fits(cx, cy, he):
                # Thicker strokes so the ink reaches its true #E8E1D9 core (thin
                # strokes are all anti-aliasing and render washed-out). Scaled by
                # size for even visual weight; large motifs match the approved ~5.4.
                f = max(0.0, min(1.0, (size - 30) / (290 - 30)))
                stroke = 3.0 + f * (5.4 - 3.0)
                out.append(instance(elems, cx, cy, size, rng.uniform(-20, 20),
                                    stroke_final=stroke, text=name.startswith("txt_")))
                add(cx, cy, he, name)
                tier[key] += 1
                miss = 0
            else:
                miss += 1

    place_tier(230, 290, "L", 300, cap=13)    # a few big accents only
    place_tier(138, 182, "M", 1200, cap=200)  # medium, capped to leave room
    place_tier(88, 130, "S", 4500)            # small saturates the gaps
    place_tier(54, 84, "F", 6500)             # tiny real motifs pack tighter
    place_tier(28, 52, "T", 9000)             # micro motifs fill remaining slivers
    out.append('</g></svg>')
    return "".join(out), (len(placed), tier)

def build_sheet():
    names = list(MOTIFS.keys())
    cols = 7
    rows = (len(names) + len(FILLERS) + cols - 1) // cols
    cell = 170
    w, h = cols * cell, rows * cell
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">',
           f'<rect width="{w}" height="{h}" fill="{BG}"/>',
           f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">']
    items = [(n, MOTIFS[n][0], MOTIFS[n][1], n.startswith("txt_")) for n in names]
    items += [(f.__name__, f(), 0.45, False) for f in FILLERS]
    for i, (name, elems, szmul, is_text) in enumerate(items):
        c, r = i % cols, i // cols
        cx, cy = (c + 0.5) * cell, (r + 0.5) * cell - 10
        out.append(instance(elems, cx, cy, 120 * szmul, 0, text=is_text))
        out.append(f'<text x="{cx}" y="{(r+1)*cell - 14}" text-anchor="middle" font-family="Helvetica" '
                   f'font-size="13" fill="#888" stroke="none">{name}</text>')
    out.append('</g></svg>')
    return "".join(out)

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "final"
    if mode == "sheet":
        svg = build_sheet()
        open("/tmp/doodle_gen/sheet.svg", "w").write(svg)
        print("wrote sheet.svg")
    else:
        seed = int(sys.argv[2]) if len(sys.argv) > 2 else 42
        svg, nf = build(W, H, seed)
        open("/tmp/doodle_gen/final.svg", "w").write(svg)
        print(f"wrote final.svg ({nf} fillers)")
