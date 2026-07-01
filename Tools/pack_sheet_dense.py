"""Pack extracted Gemini doodle sprites (/tmp/sprites/) into a DENSE wallpaper.
Clean pre-drawn doodles placed at varied SMALL sizes (downscale=crisp), random scatter,
small gaps, no same-doodle adjacency. Output (dark doodles on cream) -> finalize_gemini_doodle.py.
Optimized: footprints cached so failed placement attempts are cheap. -> /tmp/doodle_gen/packed.png

Usage: python3 pack_sheet_dense.py [seed] [gap] [heroCap] [midCap] [smallCap]
       defaults (original look): seed 7, gap 6, heroes 14, mid 110, small unbounded
"""
import glob, random, sys
import numpy as np
from PIL import Image
from scipy import ndimage

def arg(i, d): return float(sys.argv[i]) if len(sys.argv) > i else d
SEED = int(arg(1, 7)); GAP = arg(2, 6.0)
HERO_CAP = int(arg(3, 14)); MID_CAP = int(arg(4, 110))
SMALL_CAP = int(arg(5, 0)) or None
W, Hc = 1596, 2688; S = 3; MINDUP = 300.0; rng = random.Random(SEED)
CREAM = (245, 242, 234)
fps = sorted(glob.glob("/tmp/sprites/*.png"))
sprites = []; masks = []
for fp in fps:
    im = Image.open(fp).convert("RGB"); lum = np.array(im.convert("L")).astype(float)
    alpha = np.clip((242 - lum) / (242 - 60), 0, 1)
    rgba = im.convert("RGBA"); rgba.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    sprites.append(rgba); masks.append(alpha > 0.12)
N = len(sprites); print("sprites:", N)

canvas = Image.new("RGBA", (W, Hc), CREAM + (255,))
occ = np.zeros((Hc // S + 2, W // S + 2), bool); byidx = {}; cache = {}
def scaled_fp(idx, size):
    key = (idx, round(size / 5))
    if key in cache: return cache[key]
    m = masks[idx]; h, w = m.shape; sc = size / max(w, h)
    sm = np.array(Image.fromarray((m * 255).astype(np.uint8)).resize(
        (max(2, int(w * sc / S)), max(2, int(h * sc / S))), Image.BILINEAR)) > 110
    sm = ndimage.binary_dilation(sm, iterations=max(1, int(GAP / S)))
    cache[key] = sm; return sm
def place(idx, cx, cy, size, rot):
    fp = scaled_fp(idx, size); fh, fw = fp.shape
    ox = int(cx / S - fw / 2); oy = int(cy / S - fh / 2)
    if ox < 0 or oy < 0 or ox + fw > occ.shape[1] or oy + fh > occ.shape[0]: return False
    if not all((cx - px) ** 2 + (cy - py) ** 2 >= MINDUP * MINDUP for px, py in byidx.get(idx, ())): return False
    if np.logical_and(occ[oy:oy + fh, ox:ox + fw], fp).any(): return False
    occ[oy:oy + fh, ox:ox + fw] |= fp
    sp = sprites[idx]; w, h = sp.size; sc = size / max(w, h)
    sp2 = sp.resize((max(1, int(w * sc)), max(1, int(h * sc))), Image.LANCZOS).rotate(rot, expand=True, resample=Image.BICUBIC)
    canvas.alpha_composite(sp2, (int(cx - sp2.width / 2), int(cy - sp2.height / 2)))
    byidx.setdefault(idx, []).append((cx, cy)); return True

GC, GR = 13, 22; cw, ch = W / GC, Hc / GR; cellpool = []
def nextpos():
    global cellpool
    if not cellpool: cellpool = [(c, r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
    c, r = cellpool.pop(); return (c + .5) * cw + rng.uniform(-cw * .5, cw * .5), (r + .5) * ch + rng.uniform(-ch * .5, ch * .5)
deck = []
def pick():
    global deck
    if not deck: deck = list(range(N)); rng.shuffle(deck)
    return deck.pop()
def tier(lo, hi, stop, cap=None):
    cnt = miss = 0
    while miss < stop:
        if cap and cnt >= cap: break
        cx, cy = nextpos(); cx = min(max(cx, 28), W - 28); cy = min(max(cy, 28), Hc - 28)
        if place(pick(), cx, cy, rng.uniform(lo, hi), rng.uniform(-12, 12)): cnt += 1; miss = 0
        else: miss += 1
    return cnt
n1 = tier(140, 176, 1200, HERO_CAP)
n2 = tier(104, 134, 3500, MID_CAP)
n3 = tier(74, 100, 4000, SMALL_CAP)
print("placed:", n1, n2, n3, "= ", n1 + n2 + n3, " occ %.1f%%" % (100 * occ.mean()))
canvas.convert("RGB").save("/tmp/doodle_gen/packed.png")
print("saved /tmp/doodle_gen/packed.png")
