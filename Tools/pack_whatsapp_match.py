"""Pack doodle sprites (/tmp/sprites/) matching the REAL WhatsApp tile structure.

Measured from Tools/whatsapp-doodle-tile-original.png (550x999, scaled x2.9 to our
1596x2688 canvas): size pyramid hero~19 @232+, large~54 @145-232, mid~82 @87-145,
small~200 @43-87, micro~475 @<43 (simple chunky shapes in every gap), rotation to
~+/-28deg, tight gaps, ink ~17%. Output -> /tmp/doodle_gen/packed.png (then finalize).

Usage: python3 pack_whatsapp_match.py [seed]
"""
import glob, random, sys
import numpy as np
from PIL import Image
from scipy import ndimage

SEED = int(sys.argv[1]) if len(sys.argv) > 1 else 7
W, Hc = 1596, 2688; S = 3; GAP = 6.0; MINDUP = 300.0; ROT = 28
rng = random.Random(SEED)
CREAM = (245, 242, 234)

# micro tier uses only SIMPLE shapes (detailed doodles turn to mush below ~45px)
MICRO_KEYS = ["s7_c001", "s7_c002", "s7_c003", "s7_c005", "s7_c008", "s7_c007",
              "s3_c001", "s3_c002", "s3_c004", "s3_c014", "s5_c006", "s5_c013", "s2_c011"]
# moon, star, cloud, raindrop, sparkle, lightning, heart, bow, note, balloon, clover, leaf, candy

fps = sorted(glob.glob("/tmp/sprites/*.png"))
sprites = []; masks = []; micro_idx = []
for fp in fps:
    im = Image.open(fp).convert("RGB"); lum = np.array(im.convert("L")).astype(float)
    alpha = np.clip((242 - lum) / (242 - 60), 0, 1)
    rgba = im.convert("RGBA"); rgba.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    sprites.append(rgba); masks.append(alpha > 0.12)
    if any(k in fp for k in MICRO_KEYS): micro_idx.append(len(sprites) - 1)
N = len(sprites); print("sprites:", N, " micro pool:", len(micro_idx))

# boldened copies for the micro tier: thicken strokes so they survive the downscale
bold = {}
for i in micro_idx:
    m = ndimage.binary_dilation(masks[i], iterations=2)
    lum = np.array(sprites[i].convert("L")).astype(float)
    lum2 = np.where(ndimage.grey_erosion(lum, size=5) < 200, 40.0, lum)  # thicken dark cores
    a2 = np.clip((242 - lum2) / (242 - 60), 0, 1)
    im2 = Image.fromarray(np.stack([lum2.astype(np.uint8)] * 3, -1))
    rgba = im2.convert("RGBA"); rgba.putalpha(Image.fromarray((a2 * 255).astype(np.uint8)))
    bold[i] = (rgba, m)

canvas = Image.new("RGBA", (W, Hc), CREAM + (255,))
occ = np.zeros((Hc // S + 2, W // S + 2), bool); byidx = {}; cache = {}

def scaled_fp(idx, size, use_bold):
    key = (idx, round(size / 5), use_bold)
    if key in cache: return cache[key]
    m = bold[idx][1] if use_bold else masks[idx]
    h, w = m.shape; sc = size / max(w, h)
    sm = np.array(Image.fromarray((m * 255).astype(np.uint8)).resize(
        (max(2, int(w * sc / S)), max(2, int(h * sc / S))), Image.BILINEAR)) > 110
    sm = ndimage.binary_dilation(sm, iterations=max(1, int(GAP / S)))
    cache[key] = sm; return sm

def place(idx, cx, cy, size, rot, mindup=MINDUP, use_bold=False):
    fp = scaled_fp(idx, size, use_bold); fh, fw = fp.shape
    ox = int(cx / S - fw / 2); oy = int(cy / S - fh / 2)
    if ox < 0 or oy < 0 or ox + fw > occ.shape[1] or oy + fh > occ.shape[0]: return False
    if not all((cx - px) ** 2 + (cy - py) ** 2 >= mindup * mindup for px, py in byidx.get(idx, ())): return False
    if np.logical_and(occ[oy:oy + fh, ox:ox + fw], fp).any(): return False
    occ[oy:oy + fh, ox:ox + fw] |= fp
    sp = bold[idx][0] if use_bold else sprites[idx]
    w, h = sp.size; sc = size / max(w, h)
    sp2 = sp.resize((max(1, int(w * sc)), max(1, int(h * sc))), Image.LANCZOS).rotate(rot, expand=True, resample=Image.BICUBIC)
    canvas.alpha_composite(sp2, (int(cx - sp2.width / 2), int(cy - sp2.height / 2)))
    byidx.setdefault(idx, []).append((cx, cy)); return True

GC, GR = 16, 27; cw, ch = W / GC, Hc / GR; cellpool = []
def nextpos():
    global cellpool
    if not cellpool: cellpool = [(c, r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
    c, r = cellpool.pop(); return (c + .5) * cw + rng.uniform(-cw * .5, cw * .5), (r + .5) * ch + rng.uniform(-ch * .5, ch * .5)

deck = []
def pick(pool=None):
    global deck
    if pool is not None: return rng.choice(pool)
    if not deck: deck = list(range(N)); rng.shuffle(deck)
    return deck.pop()

def tier(lo, hi, stop, cap=None, pool=None, mindup=MINDUP, use_bold=False):
    cnt = miss = 0
    while miss < stop:
        if cap and cnt >= cap: break
        cx, cy = nextpos(); cx = min(max(cx, 16), W - 16); cy = min(max(cy, 16), Hc - 16)
        if place(pick(pool), cx, cy, rng.uniform(lo, hi), rng.uniform(-ROT, ROT),
                 mindup=mindup, use_bold=use_bold): cnt += 1; miss = 0
        else: miss += 1
    return cnt

n1 = tier(240, 330, 1500, 16)                                    # hero
n2 = tier(150, 230, 2500, 54)                                    # large
n3 = tier(90, 145, 3500, 82)                                     # mid
n4 = tier(46, 88, 5000, 200)                                     # small
n5 = tier(23, 44, 6000, 440, pool=micro_idx, mindup=190.0, use_bold=True)  # micro filler
tot = n1 + n2 + n3 + n4 + n5
print("placed:", n1, n2, n3, n4, n5, "=", tot, " occ %.1f%%" % (100 * occ.mean()))
canvas.convert("RGB").save("/tmp/doodle_gen/packed.png")
lum = np.array(canvas.convert("L"))
print("ink%%: %.1f  (whatsapp ref: 17.3)" % (100 * (lum < 180).mean()))
print("saved /tmp/doodle_gen/packed.png")
