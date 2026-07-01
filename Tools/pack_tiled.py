"""Tiled doodle wallpaper from the 45 HD potrace doodles (Tools/sel_vec.json).
Repeats allowed, even coverage, gentle size variation, medium density (LESS dense than the
reference) + more spacing. Sparse bubble/star decorations. Locked colors, sharp 3x render.
EXCL_UGLY drops doodle indices the user flags as ugly. Outputs /tmp/doodle_gen/SEL.png.
"""
import json, random, math, subprocess, glob, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

W, Hc = 1596, 2688
PAPER = "#F5F2EA"; INK = "#E5DAC6"
S = 3; SS = 3; GAP = 11.0; MINDUP = 470.0; DECO_W = 4.4
TW = 5.0                              # uniform stroke width (final px) for EVERY doodle
# locked 3 sizes
BIG, MED, SMALL = 300, 200, 130; JIT = 8
BIG_SIZE = 245; MIN_FULL = 0.45       # gate: only detailed doodles allowed at BIG
LAYER = sys.argv[1] if len(sys.argv) > 1 else "all"   # big | bigmed | all
CAP_BIG = 18
EXCL_UGLY = set()                      # doodle indices to drop (filled in once user flags them)
rng = random.Random(7)
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

Vall = json.load(open("Tools/sel_vec.json"))
CUTall = sorted(glob.glob("Tools/sel/s*.png"))
pairs = [(i, v, c) for i, (v, c) in enumerate(zip(Vall, CUTall)) if v["paths"] and i not in EXCL_UGLY]
IDX = [p[0] for p in pairs]; V = [p[1] for p in pairs]; CUT = [p[2] for p in pairs]
N = len(V); print("doodles:", N, "(excluded:", sorted(EXCL_UGLY), ")")

foot = []; fullness = []; GR = 6
for cf in CUT:
    m = np.array(Image.open(cf).convert("L")) > 60; fh, fw = m.shape
    foot.append((ndimage.binary_fill_holes(ndimage.binary_dilation(m, iterations=2)), fw, fh))
    hit = sum(1 for gy in range(GR) for gx in range(GR)
              if m[gy*fh//GR:max(gy*fh//GR+1,(gy+1)*fh//GR), gx*fw//GR:max(gx*fw//GR+1,(gx+1)*fw//GR)].any())
    fullness.append(hit/(GR*GR))

occ = np.zeros((Hc//S+2, W//S+2), bool); cache = {}
def scaled_foot(idx, size):
    key = (idx, round(size/6))
    if key in cache: return cache[key]
    m, fw, fh = foot[idx]; tgt = max(2, int(size/S))
    sm = np.array(Image.fromarray((m*255).astype(np.uint8)).resize(
        (max(2, int(fw*tgt/max(fw, fh))), max(2, int(fh*tgt/max(fw, fh)))), Image.BILINEAR)) > 110
    a = ndimage.binary_dilation(sm, iterations=max(1, int(GAP/S)))
    cache[key] = a; return a

byidx = {}; parts = []
def emit(idx, cx, cy, size):
    if size > BIG_SIZE and fullness[idx] < MIN_FULL: return False
    fa = scaled_foot(idx, size); fh2, fw2 = fa.shape
    ox = int(cx/S-fw2/2); oy = int(cy/S-fh2/2)
    if ox < 0 or oy < 0 or ox+fw2 > occ.shape[1] or oy+fh2 > occ.shape[0]: return False
    if not all((cx-px)**2+(cy-py)**2 >= MINDUP*MINDUP for (px, py) in byidx.get(idx, ())): return False
    if np.logical_and(occ[oy:oy+fh2, ox:ox+fw2], fa).any(): return False
    occ[oy:oy+fh2, ox:ox+fw2] |= fa
    v = V[idx]; vbW, vbH = v["vbW"], v["vbH"]; sc = size/max(vbW, vbH); rot = rng.uniform(-8, 8)
    # centerline strokes, uniform absolute width (TW/sc keeps it constant across all sizes)
    parts.append(f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({sc:.4f}) translate({-vbW/2:.1f} {-vbH/2:.1f})" stroke-width="{TW/sc:.2f}">'
                 + "".join(f'<path d="{d}"/>' for d in v["paths"]) + '</g>')
    byidx.setdefault(idx, []).append((cx, cy)); return True

GC, GR2 = 11, 19; cw, ch = W/GC, Hc/GR2; cellpool = []
def next_pos():
    global cellpool
    if not cellpool: cellpool = [(c, r) for c in range(GC) for r in range(GR2)]; rng.shuffle(cellpool)
    c, r = cellpool.pop()
    return (c+0.5)*cw+rng.uniform(-cw*0.5, cw*0.5), (r+0.5)*ch+rng.uniform(-ch*0.5, ch*0.5)
deck = []
def pick():
    global deck
    if not deck: deck = list(range(N)); rng.shuffle(deck)
    return deck.pop()
def place_tier(lo, hi, stop, cap=None):
    cnt = miss = 0
    while miss < stop:
        if cap and cnt >= cap: break
        cx, cy = next_pos(); cx = min(max(cx, 30), W-30); cy = min(max(cy, 30), Hc-30)
        if emit(pick(), cx, cy, rng.uniform(lo, hi)): cnt += 1; miss = 0
        else: miss += 1
    return cnt

# LAYERED build: big first, then medium, then small last
nb = place_tier(BIG-JIT, BIG+JIT, 4000, CAP_BIG)
print("BIG layer:", nb)
nm = ns = 0
if LAYER in ("bigmed", "all"):
    nm = place_tier(MED-JIT, MED+JIT, 8000, 120)
    print("MEDIUM layer:", nm)
if LAYER == "all":
    ns = place_tier(SMALL-JIT, SMALL+JIT, 12000, 420)
    # gap-fill at SMALL size only (keeps the 3-size look)
    STEP = 58
    order = [(gx, gy) for gy in range(0, Hc, STEP) for gx in range(0, W, STEP)]; rng.shuffle(order)
    for (gx, gy) in order:
        if rng.random() > 0.6: continue
        if occ[min(gy//S, occ.shape[0]-1), min(gx//S, occ.shape[1]-1)]: continue
        for _ in range(6):
            if emit(pick(), min(max(gx+rng.uniform(-22, 22), 28), W-28), min(max(gy+rng.uniform(-22, 22), 28), Hc-28), SMALL+rng.uniform(-JIT, JIT)): break
    print("SMALL layer + fill:", len(parts)-nb-nm)
print("placed", len(parts), "| occ %.1f%%" % (100*occ.mean()))

# sparse decorations
decos = []
def free(cx, cy, r):
    rr = max(1, int((r+9)/S)); oy, ox = int(cy/S), int(cx/S)
    return not occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr].any()
def mark(cx, cy, r):
    rr = max(1, int((r+5)/S)); oy, ox = int(cy/S), int(cx/S)
    occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr] = True
def star(cx, cy, R, rot):
    pts = []
    for i in range(10):
        ang = rot + i*math.pi/5; rad = R if i % 2 == 0 else R*0.42
        pts.append((cx+rad*math.cos(ang), cy+rad*math.sin(ang)))
    return "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + " Z"
cand = ([(x, y) for y in range(80, Hc-80, 88) for x in range(80, W-80, 88)] if LAYER == "all" else [])
rng.shuffle(cand)
ndeco = 0
for (gx, gy) in cand:
    if ndeco >= 30: break
    x = gx+rng.uniform(-28, 28); y = gy+rng.uniform(-28, 28); t = rng.random()
    if t < 0.6:
        r = rng.uniform(7, 16)
        if not free(x, y, r): continue
        if rng.random() < 0.3: decos.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r*0.45:.1f}" fill="{INK}"/>')
        else: decos.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.1f}" fill="none" stroke="{INK}" stroke-width="{DECO_W}"/>')
        mark(x, y, r); ndeco += 1
    else:
        R = rng.uniform(12, 20)
        if not free(x, y, R): continue
        decos.append(f'<path d="{star(x, y, R, rng.uniform(0, 1.2))}" fill="none" stroke="{INK}" stroke-width="{DECO_W}" stroke-linejoin="round" stroke-linecap="round"/>')
        mark(x, y, R); ndeco += 1
print("decorations:", ndeco)

svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}">'
       f'<rect width="{W}" height="{Hc}" fill="{PAPER}"/>'
       f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">' + "".join(parts) + '</g>'
       f'<g>' + "".join(decos) + '</g></svg>')
open("/tmp/doodle_gen/SEL.svg", "w").write(svg)
subprocess.run([CHROME, "--headless", "--disable-gpu", f"--force-device-scale-factor={SS}",
                "--screenshot=/tmp/doodle_gen/SEL_2x.png", f"--window-size={W},{Hc}", "file:///tmp/doodle_gen/SEL.svg"], stderr=subprocess.DEVNULL)
out = Image.open("/tmp/doodle_gen/SEL_2x.png").convert("RGB").resize((W, Hc), Image.LANCZOS); out.save("/tmp/doodle_gen/SEL.png")
print("ink%%(<238)=%.1f" % (100*(np.array(out.convert("L")) < 238).mean()))
print("saved /tmp/doodle_gen/SEL.png")
