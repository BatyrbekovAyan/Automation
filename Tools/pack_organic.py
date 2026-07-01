"""Organic, evenly-spread doodle packing (fixes clump/void artifacts of random scatter).

Three placement engines, all using the WhatsApp-measured tier pyramid
(16 hero / 54 large / 82 mid / 200 small / 440 micro, rot +/-20):
  A bluenoise — Mitchell best-candidate: k random spots, keep the one farthest
                (normalized by radii) from everything placed.
  B gapfill   — largest-first; each element placed in the deepest pocket of free
                space (distance transform), jittered among the top-decile pockets.
  C hybrid    — heroes on a jittered 4x4 lattice, everything else via gapfill.

Usage: python3 pack_organic.py <A|B|C> [seed] [out.png]
"""
import glob, random, sys
import numpy as np
from PIL import Image
from scipy import ndimage

VARIANT = sys.argv[1].upper() if len(sys.argv) > 1 else "B"
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 7
OUT = sys.argv[3] if len(sys.argv) > 3 else "/tmp/doodle_gen/packed.png"
W, Hc = 1596, 2688; S = 3; GAP = 6.0; MINDUP = 300.0; ROT = 20
rng = random.Random(SEED)
CREAM = (245, 242, 234)

MICRO_KEYS = ["s7_c001", "s7_c002", "s7_c003", "s7_c005", "s7_c008", "s7_c007",
              "s3_c001", "s3_c002", "s3_c004", "s3_c014", "s5_c006", "s5_c013", "s2_c011"]

fps = sorted(glob.glob("/tmp/sprites/*.png"))
sprites = []; masks = []; micro_idx = []
for fp in fps:
    im = Image.open(fp).convert("RGB"); lum = np.array(im.convert("L")).astype(float)
    alpha = np.clip((242 - lum) / (242 - 60), 0, 1)
    rgba = im.convert("RGBA"); rgba.putalpha(Image.fromarray((alpha * 255).astype(np.uint8)))
    sprites.append(rgba); masks.append(alpha > 0.12)
    if any(k in fp for k in MICRO_KEYS): micro_idx.append(len(sprites) - 1)
N = len(sprites)

bold = {}
for i in micro_idx:
    lum = np.array(sprites[i].convert("L")).astype(float)
    lum2 = np.where(ndimage.grey_erosion(lum, size=5) < 200, 40.0, lum)
    a2 = np.clip((242 - lum2) / (242 - 60), 0, 1)
    im2 = Image.fromarray(np.stack([lum2.astype(np.uint8)] * 3, -1))
    rgba = im2.convert("RGBA"); rgba.putalpha(Image.fromarray((a2 * 255).astype(np.uint8)))
    bold[i] = (rgba, ndimage.binary_dilation(masks[i], iterations=2))

canvas = Image.new("RGBA", (W, Hc), CREAM + (255,))
GH, GW = Hc // S + 2, W // S + 2
occ = np.zeros((GH, GW), bool); byidx = {}; cache = {}
placed_pts = []          # (cx, cy, r) for bluenoise scoring

def scaled_fp(idx, size, use_bold):
    key = (idx, round(size / 5), use_bold)
    if key in cache: return cache[key]
    m = bold[idx][1] if use_bold else masks[idx]
    h, w = m.shape; sc = size / max(w, h)
    sm = np.array(Image.fromarray((m * 255).astype(np.uint8)).resize(
        (max(2, int(w * sc / S)), max(2, int(h * sc / S))), Image.BILINEAR)) > 110
    sm = ndimage.binary_dilation(sm, iterations=max(1, int(GAP / S)))
    cache[key] = sm; return sm

def try_place(idx, cx, cy, size, rot, mindup, use_bold):
    fp = scaled_fp(idx, size, use_bold); fh, fw = fp.shape
    ox = int(cx / S - fw / 2); oy = int(cy / S - fh / 2)
    if ox < 0 or oy < 0 or ox + fw > GW or oy + fh > GH: return False
    if not all((cx - px) ** 2 + (cy - py) ** 2 >= mindup * mindup for px, py in byidx.get(idx, ())): return False
    if np.logical_and(occ[oy:oy + fh, ox:ox + fw], fp).any(): return False
    occ[oy:oy + fh, ox:ox + fw] |= fp
    sp = bold[idx][0] if use_bold else sprites[idx]
    w, h = sp.size; sc = size / max(w, h)
    sp2 = sp.resize((max(1, int(w * sc)), max(1, int(h * sc))), Image.LANCZOS).rotate(rot, expand=True, resample=Image.BICUBIC)
    canvas.alpha_composite(sp2, (int(cx - sp2.width / 2), int(cy - sp2.height / 2)))
    byidx.setdefault(idx, []).append((cx, cy))
    placed_pts.append((cx, cy, size / 2)); return True

deck = []
def pick(pool=None):
    global deck
    if pool is not None: return rng.choice(pool)
    if not deck: deck = list(range(N)); rng.shuffle(deck)
    return deck.pop()

MARG = 20
def bluenoise_pos(r, k=24):
    best = None; best_s = -1
    for _ in range(k):
        cx = rng.uniform(MARG, W - MARG); cy = rng.uniform(MARG, Hc - MARG)
        s = min(((cx - px) ** 2 + (cy - py) ** 2) ** .5 / (r + pr + 1) for px, py, pr in placed_pts) if placed_pts else 1e9
        if s > best_s: best_s = s; best = (cx, cy)
    return best

def gapfill_pos(r):
    # deepest pockets of free space; jitter among the top decile so it stays organic
    edt = ndimage.distance_transform_edt(~occ) * S
    need = r + GAP
    ys, xs = np.where(edt >= need)
    if len(ys) == 0:
        ys, xs = np.where(edt >= edt.max() * 0.95)
        if len(ys) == 0: return None
    d = edt[ys, xs]
    thr = np.percentile(d, 90)
    sel = np.where(d >= thr)[0]
    j = sel[rng.randrange(len(sel))]
    return xs[j] * S + rng.uniform(-S, S), ys[j] * S + rng.uniform(-S, S)

def run_tier(count, lo, hi, pool=None, mindup=MINDUP, use_bold=False, mode="B", tries=40):
    cnt = 0; fails = 0
    while cnt < count and fails < tries * 3:
        size = rng.uniform(lo, hi); r = size / 2
        ok = False
        for _ in range(tries):
            pos = bluenoise_pos(r) if mode == "A" else gapfill_pos(r)
            if pos is None: break
            if try_place(pick(pool), pos[0], pos[1], size, rng.uniform(-ROT, ROT), mindup, use_bold):
                ok = True; break
        if ok: cnt += 1; fails = 0
        else: fails += 1
    return cnt

def hero_lattice(count, lo, hi):
    cols, rows = 4, 4
    cells = [(c, r) for c in range(cols) for r in range(rows)]; rng.shuffle(cells)
    cw, ch = W / cols, Hc / rows; cnt = 0
    for c, r in cells[:count]:
        for _ in range(60):
            cx = (c + .5) * cw + rng.uniform(-.3, .3) * cw
            cy = (r + .5) * ch + rng.uniform(-.3, .3) * ch
            if try_place(pick(), cx, cy, rng.uniform(lo, hi), rng.uniform(-ROT, ROT), MINDUP, False):
                cnt += 1; break
    return cnt

mode = "A" if VARIANT == "A" else "B"
if VARIANT == "C":
    n1 = hero_lattice(16, 240, 330)
else:
    n1 = run_tier(16, 240, 330, mode=mode)
n2 = run_tier(54, 150, 230, mode=mode)
n3 = run_tier(82, 90, 145, mode=mode)
n4 = run_tier(200, 46, 88, mode=mode)
n5 = run_tier(440, 23, 44, pool=micro_idx, mindup=190.0, use_bold=True, mode=mode)
print(f"variant {VARIANT} seed {SEED} placed: {n1} {n2} {n3} {n4} {n5} = {n1+n2+n3+n4+n5}")
canvas.convert("RGB").save(OUT)
lum = np.array(canvas.convert("L"))
print("ink%%: %.1f  -> %s" % (100 * (lum < 180).mean(), OUT))
