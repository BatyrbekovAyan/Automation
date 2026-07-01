"""Pack the 45 selected doodles in their INITIAL (raster) state — NO tracing/vectorizing.
Each original cutout (Tools/sel/s*.png, white-on-black) is recolored to the locked ink and
composited directly, so the doodle shapes are exactly the source artwork (no distortion).
Collage layout: each doodle once, varied sizes (not too small), no overlap, sparse
bubble+star decorations. Locked colors. Outputs /tmp/doodle_gen/SEL.png (1596x2688).
"""
import random, math, glob
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

W, Hc = 1596, 2688
PAPER = (245, 242, 234)      # #F5F2EA
INK = (229, 218, 198)        # #E5DAC6
SS = 2; S = 3; GAP = 10.0; DECO_W = 3.6
BIG_SIZE = 235; MIN_FULL = 0.50
rng = random.Random(7)

CUT = sorted(glob.glob("Tools/sel/s*.png"))
masks = [np.array(Image.open(cf).convert("L")) for cf in CUT]
N = len(masks); print("doodles:", N)

foot = []; fullness = []; GR = 6
for m in masks:
    mb = m > 60; fh, fw = mb.shape
    foot.append((ndimage.binary_fill_holes(ndimage.binary_dilation(mb, iterations=2)), fw, fh))
    hit = sum(1 for gy in range(GR) for gx in range(GR)
              if mb[gy*fh//GR:max(gy*fh//GR+1,(gy+1)*fh//GR), gx*fw//GR:max(gx*fw//GR+1,(gx+1)*fw//GR)].any())
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

placements = []
def emit(idx, cx, cy, size):
    if size > BIG_SIZE and fullness[idx] < MIN_FULL: return False
    fa = scaled_foot(idx, size); fh2, fw2 = fa.shape
    ox = int(cx/S-fw2/2); oy = int(cy/S-fh2/2)
    if ox < 0 or oy < 0 or ox+fw2 > occ.shape[1] or oy+fh2 > occ.shape[0]: return False
    if np.logical_and(occ[oy:oy+fh2, ox:ox+fw2], fa).any(): return False
    occ[oy:oy+fh2, ox:ox+fw2] |= fa
    placements.append((idx, cx, cy, size, rng.uniform(-8, 8))); return True

# collage placement: each doodle exactly once
COLS, ROWS = 5, 9; cw, ch = W/COLS, Hc/ROWS
cells = [(c, r) for r in range(ROWS) for c in range(COLS)]; rng.shuffle(cells)
doodles = list(range(N)); rng.shuffle(doodles)
pool = ([rng.uniform(200, 234) for _ in range(max(1, N*24//100))] +
        [rng.uniform(172, 200) for _ in range(max(1, N*44//100))] +
        [rng.uniform(150, 172) for _ in range(max(1, N*40//100))])
rng.shuffle(pool)
while len(pool) < N: pool.append(rng.uniform(170, 195))
pool = pool[:N]
placed = 0
for k, idx in enumerate(doodles):
    c, r = cells[k % len(cells)]; cx0, cy0 = (c+0.5)*cw, (r+0.5)*ch
    size = pool[k]; done = False
    for _ in range(70):
        jx = min(max(cx0+rng.uniform(-cw*0.24, cw*0.24), 34), W-34)
        jy = min(max(cy0+rng.uniform(-ch*0.24, ch*0.24), 34), Hc-34)
        if emit(idx, jx, jy, size): done = True; break
        size *= 0.95
        if size < 142: size = 142
    if not done:
        for s in (160, 148, 138):
            if emit(idx, min(max(cx0, 34), W-34), min(max(cy0, 34), Hc-34), s): done = True; break
    placed += done
print("placed", placed, "of", N, "| occ %.1f%%" % (100*occ.mean()))

# sparse decorations
decos = []
def free(cx, cy, r):
    rr = max(1, int((r+10)/S)); oy, ox = int(cy/S), int(cx/S)
    return not occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr].any()
def mark(cx, cy, r):
    rr = max(1, int((r+6)/S)); oy, ox = int(cy/S), int(cx/S)
    occ[max(0, oy-rr):oy+rr, max(0, ox-rr):ox+rr] = True
cand = [(x, y) for y in range(70, Hc-70, 76) for x in range(70, W-70, 76)]; rng.shuffle(cand)
ndeco = 0
for (gx, gy) in cand:
    if ndeco >= 40: break
    x = gx+rng.uniform(-26, 26); y = gy+rng.uniform(-26, 26); t = rng.random()
    if t < 0.60:
        r = rng.uniform(7, 18)
        if not free(x, y, r): continue
        decos.append(("dot" if rng.random() < 0.32 else "ring", x, y, r)); mark(x, y, r); ndeco += 1
    else:
        R = rng.uniform(13, 23)
        if not free(x, y, R): continue
        decos.append(("star", x, y, R, rng.uniform(0, 1.2))); mark(x, y, R); ndeco += 1
print("decorations:", ndeco)

# ---- render: raster composite + drawn decorations at SS, then downscale ----
canvas = Image.new("RGBA", (W*SS, Hc*SS), PAPER+(255,))
for idx, cx, cy, size, rot in placements:
    m = masks[idx]; h, w = m.shape; sc = size*SS/max(w, h)
    tw, th = max(1, int(w*sc)), max(1, int(h*sc))
    al = Image.fromarray(m, "L").resize((tw, th), Image.LANCZOS)
    sprite = Image.new("RGBA", (tw, th), INK+(0,)); sprite.putalpha(al)
    sprite = sprite.rotate(rot, expand=True, resample=Image.BICUBIC)
    canvas.alpha_composite(sprite, (int(cx*SS-sprite.width/2), int(cy*SS-sprite.height/2)))
dr = ImageDraw.Draw(canvas); wpx = max(2, int(DECO_W*SS))
for d in decos:
    if d[0] == "dot":
        _, x, y, r = d; rr = r*0.45*SS
        dr.ellipse([x*SS-rr, y*SS-rr, x*SS+rr, y*SS+rr], fill=INK+(255,))
    elif d[0] == "ring":
        _, x, y, r = d; rr = r*SS
        dr.ellipse([x*SS-rr, y*SS-rr, x*SS+rr, y*SS+rr], outline=INK+(255,), width=wpx)
    else:
        _, x, y, R, rot = d
        pts = []
        for i in range(10):
            ang = rot + i*math.pi/5; rad = (R if i % 2 == 0 else R*0.42)*SS
            pts.append((x*SS+rad*math.cos(ang), y*SS+rad*math.sin(ang)))
        dr.line(pts+[pts[0], pts[1]], fill=INK+(255,), width=wpx, joint="curve")
canvas.convert("RGB").resize((W, Hc), Image.LANCZOS).save("/tmp/doodle_gen/SEL.png")
print("saved /tmp/doodle_gen/SEL.png")
