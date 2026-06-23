"""WhatsApp-style doodle wallpaper packer (final).

Improves on pack_shape.py:
 - Even, dense edge-to-edge coverage (systematic gap-fill pass, no empty bands).
 - Good size variation across 5 tiers.
 - Crisper 4.2px strokes (above the <4px washout threshold).
 - 2x supersampled render -> Lanczos downscale for clean anti-aliased lines.
 - Locked colors: paper #F5F2EA, ink #E5DAC6 (do NOT change).
"""
import json, random, math, subprocess, re
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

W, Hc = 1596, 2688
PAPER = "#F5F2EA"; INK = "#E5DAC6"
TW = 4.2                     # stroke width in canvas px (>=4 to avoid washout)
EXCL = {8, 20, 60, 73, 75, 77, 114}
S = 3                        # occupancy downscale factor
GAPpx = 4.0                  # spacing between silhouettes (canvas px)
MINDUP = 540.0               # min distance between repeats of the same doodle
SS = 2                       # render supersample factor
rng = random.Random(7)

Vall = json.load(open("cutvec_cl.json"))
V = [v for i, v in enumerate(Vall) if i not in EXCL]
print("doodles:", len(V))

# --- silhouette footprint per doodle (normalized to REF max-dim) ---
REF = 140
def pts_of(d):
    nums = re.findall(r'-?\d+\.?\d*', d)
    return [(float(nums[i]), float(nums[i + 1])) for i in range(0, len(nums) - 1, 2)]

foot = []
for v in V:
    vbW, vbH = v["vbW"], v["vbH"]; sc = REF / max(vbW, vbH)
    fw, fh = max(2, int(vbW * sc)), max(2, int(vbH * sc))
    img = Image.new("L", (fw, fh), 0); dr = ImageDraw.Draw(img)
    sw = max(2, int(TW * sc * 0.6))
    for d in v["paths"]:
        p = [(x * sc, y * sc) for (x, y) in pts_of(d)]
        if len(p) >= 2: dr.line(p, fill=255, width=sw, joint="curve")
    m = np.array(img) > 40
    m = ndimage.binary_dilation(m, iterations=max(1, int(REF * 0.03)))
    m = ndimage.binary_fill_holes(m)
    foot.append((m, fw, fh))

occ = np.zeros((Hc // S + 2, W // S + 2), bool)
cache = {}
def scaled_foot(idx, size):
    key = (idx, round(size / 6))
    if key in cache: return cache[key]
    m, fw, fh = foot[idx]; tgt = max(2, int(size / S))
    sm = Image.fromarray((m * 255).astype(np.uint8)).resize(
        (max(2, int(fw * tgt / max(fw, fh))), max(2, int(fh * tgt / max(fw, fh)))), Image.BILINEAR)
    a = np.array(sm) > 110
    a = ndimage.binary_dilation(a, iterations=max(1, int(GAPpx / S)))
    cache[key] = a; return a

deck = []
def pick():
    global deck
    if not deck: deck = list(range(len(V))); rng.shuffle(deck)
    return deck.pop()

byidx = {}; parts = []
def emit(idx, cx, cy, size):
    fa = scaled_foot(idx, size); fh2, fw2 = fa.shape
    ox = int(cx / S - fw2 / 2); oy = int(cy / S - fh2 / 2)
    if ox < 0 or oy < 0 or ox + fw2 > occ.shape[1] or oy + fh2 > occ.shape[0]: return False
    if not all((cx - px) ** 2 + (cy - py) ** 2 >= MINDUP * MINDUP for (px, py) in byidx.get(idx, ())): return False
    sub = occ[oy:oy + fh2, ox:ox + fw2]
    if np.logical_and(sub, fa).any(): return False
    occ[oy:oy + fh2, ox:ox + fw2] |= fa
    v = V[idx]; vbW, vbH = v["vbW"], v["vbH"]; sc = size / max(vbW, vbH); rot = rng.uniform(-9, 9)
    parts.append(
        f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({sc:.4f}) '
        f'translate({-vbW / 2:.1f} {-vbH / 2:.1f})" stroke-width="{TW / sc:.2f}">'
        + "".join(f'<path d="{d}"/>' for d in v["paths"]) + '</g>')
    byidx.setdefault(idx, []).append((cx, cy)); return True

# Phase 1: random size-tiered placement (jittered grid) for organic variation.
GC, GR = 12, 20; cw, ch = W / GC, Hc / GR; cellpool = []
def next_pos():
    global cellpool
    if not cellpool:
        cellpool = [(c, r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
    c, r = cellpool.pop()
    return (c + 0.5) * cw + rng.uniform(-cw * 0.55, cw * 0.55), (r + 0.5) * ch + rng.uniform(-ch * 0.55, ch * 0.55)

def place_tier(lo, hi, stop, cap=None):
    cnt = 0; miss = 0
    while miss < stop:
        if cap and cnt >= cap: break
        idx = pick(); cx, cy = next_pos()
        cx = min(max(cx, 30), W - 30); cy = min(max(cy, 30), Hc - 30)
        if emit(idx, cx, cy, rng.uniform(lo, hi)): cnt += 1; miss = 0
        else: miss += 1
    return cnt

n1 = place_tier(195, 255, 900, cap=28)
n2 = place_tier(120, 175, 6000, cap=240)
n3 = place_tier(84, 118, 24000)
n4 = place_tier(58, 82, 32000)
print("after random tiers:", len(parts))

# Phase 2: systematic gap-fill. Scan a fine grid; drop the smallest doodles
# into every still-empty cell so coverage is even edge-to-edge (incl. top band).
STEP = 46
fill = 0
order = [(gx, gy) for gy in range(0, Hc, STEP) for gx in range(0, W, STEP)]
rng.shuffle(order)
for (gx, gy) in order:
    if occ[min(gy // S, occ.shape[0] - 1), min(gx // S, occ.shape[1] - 1)]:
        continue
    for _ in range(6):
        idx = pick(); size = rng.uniform(40, 66)
        jx = gx + rng.uniform(-STEP * 0.4, STEP * 0.4); jy = gy + rng.uniform(-STEP * 0.4, STEP * 0.4)
        jx = min(max(jx, 28), W - 28); jy = min(max(jy, 28), Hc - 28)
        if emit(idx, jx, jy, size): fill += 1; break
print("gap-fill added:", fill, "| total:", len(parts), "| occupancy %.1f%%" % (100 * occ.mean()))

svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}">'
       f'<rect width="{W}" height="{Hc}" fill="{PAPER}"/>'
       f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">'
       + "".join(parts) + '</g></svg>')
open("/tmp/doodle_gen/final.svg", "w").write(svg)

# 2x supersampled render -> Lanczos downscale.
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "--headless",
                "--disable-gpu", f"--force-device-scale-factor={SS}",
                "--screenshot=/tmp/doodle_gen/final_2x.png", f"--window-size={W},{Hc}",
                "file:///tmp/doodle_gen/final.svg"], stderr=subprocess.DEVNULL)
big = Image.open("/tmp/doodle_gen/final_2x.png").convert("RGB")
out = big.resize((W, Hc), Image.LANCZOS)
out.save("/tmp/doodle_gen/final.png")

g = np.array(out.convert("L")); H2 = g.shape[0]; ink = g < 244
print("ink %.1f%%" % (100 * ink.mean()),
      "bands", [round(100 * ink[i * H2 // 8:(i + 1) * H2 // 8].mean(), 1) for i in range(8)])
print("saved /tmp/doodle_gen/final.png", out.size)
