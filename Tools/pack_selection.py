"""Pack the 45 selected doodles (outline-traced, Tools/sel_vec.json) into the chat wallpaper
as a COLLAGE: each doodle ONCE, big varied sizes, no overlap, even spread, then a SPARSE
decorative pass (bubbles + rounded stars) in the empty space. Locked colors, sharp 3x render.
Outputs /tmp/doodle_gen/SEL.png (1596x2688).
"""
import json, random, math, subprocess, re, glob
import numpy as np
from PIL import Image
from scipy import ndimage

W, Hc = 1596, 2688
PAPER = "#F5F2EA"; INK = "#E5DAC6"
S = 3; SS = 3; DECO_W = 3.8
GAP = 10.0
BIG_SIZE = 252; MIN_FULL = 0.50
rng = random.Random(7)
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

Vall = json.load(open("Tools/sel_vec.json"))
CUTall = sorted(glob.glob("Tools/sel/s*.png"))
pairs = [(v, c) for v, c in zip(Vall, CUTall) if v["paths"]]
V = [p[0] for p in pairs]; CUT = [p[1] for p in pairs]
N = len(V); print("doodles:", N)

# --- footprint (filled silhouette) + fullness from the actual cutout raster ---
foot = []; fullness = []; GR = 6
for cf in CUT:
    m = np.array(Image.open(cf).convert("L")) > 60
    fh, fw = m.shape
    sil = ndimage.binary_fill_holes(ndimage.binary_dilation(m, iterations=2))
    foot.append((sil, fw, fh))
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

parts = []
def emit(idx, cx, cy, size):
    if size > BIG_SIZE and fullness[idx] < MIN_FULL: return False
    fa = scaled_foot(idx, size); fh2, fw2 = fa.shape
    ox = int(cx/S-fw2/2); oy = int(cy/S-fh2/2)
    if ox < 0 or oy < 0 or ox+fw2 > occ.shape[1] or oy+fh2 > occ.shape[0]: return False
    if np.logical_and(occ[oy:oy+fh2, ox:ox+fw2], fa).any(): return False
    occ[oy:oy+fh2, ox:ox+fw2] |= fa
    v = V[idx]; vbW, vbH = v["vbW"], v["vbH"]; sc = size/max(vbW, vbH); rot = rng.uniform(-8, 8)
    inner = f'<g transform="{v["gt"]}">' + "".join(f'<path d="{d}"/>' for d in v["paths"]) + '</g>'
    parts.append(f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({sc:.4f}) translate({-vbW/2:.1f} {-vbH/2:.1f})">'
                 + inner + '</g>')   # potrace path (smooth HD), nonzero winding
    return True

# --- collage placement: each doodle exactly once, big varied sizes, no overlap ---
COLS, ROWS = 5, 9; cw, ch = W/COLS, Hc/ROWS
cells = [(c, r) for r in range(ROWS) for c in range(COLS)]; rng.shuffle(cells)
doodles = list(range(N)); rng.shuffle(doodles)
# wide size variation: small -> big
pool = ([rng.uniform(248, 304) for _ in range(max(1, N*22//100))] +    # big
        [rng.uniform(168, 240) for _ in range(max(1, N*40//100))] +    # medium
        [rng.uniform(104, 158) for _ in range(max(1, N*40//100))])     # small
rng.shuffle(pool)
while len(pool) < N: pool.append(rng.uniform(150, 210))
pool = pool[:N]
placed = 0
for k, idx in enumerate(doodles):
    c, r = cells[k % len(cells)]; cx0, cy0 = (c+0.5)*cw, (r+0.5)*ch
    size = pool[k]; done = False
    for _ in range(70):
        jx = min(max(cx0+rng.uniform(-cw*0.24, cw*0.24), 34), W-34)
        jy = min(max(cy0+rng.uniform(-ch*0.24, ch*0.24), 34), Hc-34)
        if emit(idx, jx, jy, size): done = True; break
        size *= 0.94
        if size < 100: size = 100
    if not done:
        for s in (140, 118, 102):
            if emit(idx, min(max(cx0, 34), W-34), min(max(cy0, 34), Hc-34), s): done = True; break
    placed += done
print("placed", placed, "of", N, "| occ %.1f%%" % (100*occ.mean()))

# --- sparse decorative fill: bubbles + rounded stars in empty space (do not over-add) ---
decos = []
def free(cx, cy, r):
    rr = max(1, int((r+10)/S)); oy, ox = int(cy/S), int(cx/S)
    sub = occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr]
    return not sub.any()
def mark(cx, cy, r):
    rr = max(1, int((r+6)/S)); oy, ox = int(cy/S), int(cx/S)
    occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr] = True
def star(cx, cy, R, rot):
    pts = []
    for i in range(10):
        ang = rot + i*math.pi/5; rad = R if i % 2 == 0 else R*0.42
        pts.append((cx+rad*math.cos(ang), cy+rad*math.sin(ang)))
    return "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + " Z"

cand = [(x, y) for y in range(70, Hc-70, 76) for x in range(70, W-70, 76)]; rng.shuffle(cand)
TARGET = 40; ndeco = 0
for (gx, gy) in cand:
    if ndeco >= TARGET: break
    x = gx+rng.uniform(-26, 26); y = gy+rng.uniform(-26, 26)
    t = rng.random()
    if t < 0.60:                                  # bubble
        rr = rng.uniform(7, 18)
        if not free(x, y, rr): continue
        if rng.random() < 0.32:
            decos.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{rr*0.45:.1f}" fill="{INK}" stroke="none"/>')
        else:
            decos.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{rr:.1f}" fill="none" stroke="{INK}" stroke-width="{DECO_W}"/>')
        mark(x, y, rr); ndeco += 1
    else:                                         # rounded star
        R = rng.uniform(13, 23)
        if not free(x, y, R): continue
        decos.append(f'<path d="{star(x, y, R, rng.uniform(0, 1.2))}" fill="none" stroke="{INK}" stroke-width="{DECO_W}" stroke-linejoin="round" stroke-linecap="round"/>')
        mark(x, y, R); ndeco += 1
print("decorations:", ndeco)

svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}">'
       f'<rect width="{W}" height="{Hc}" fill="{PAPER}"/>'
       f'<g fill="{INK}" stroke="none">' + "".join(parts) + '</g>'
       f'<g>' + "".join(decos) + '</g></svg>')
open("/tmp/doodle_gen/SEL.svg", "w").write(svg)
subprocess.run([CHROME, "--headless", "--disable-gpu", f"--force-device-scale-factor={SS}",
                "--screenshot=/tmp/doodle_gen/SEL_2x.png", f"--window-size={W},{Hc}", "file:///tmp/doodle_gen/SEL.svg"], stderr=subprocess.DEVNULL)
Image.open("/tmp/doodle_gen/SEL_2x.png").convert("RGB").resize((W, Hc), Image.LANCZOS).save("/tmp/doodle_gen/SEL.png")
print("saved /tmp/doodle_gen/SEL.png")
