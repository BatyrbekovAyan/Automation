"""Segment a doodle sheet into individual doodle crops + render a numbered catalog.

Approach: binarize ink (dark strokes) -> dilate so the disconnected strokes of one
doodle merge into a single blob -> connected-component label -> crop each blob from the
original. Filter by ink area. Optional dedup of near-identical crops (for tiled sheets).

Usage: python3 segment_sheet.py <sheet.png> <name> <thresh> <dilate> <min_ink> <dedup0/1> [maxfrac]
Outputs: Tools/sheets/<name>_cuts/c###.png  and  /tmp/<name>_catalog.png (via Chrome)
"""
import sys, os, glob, math, subprocess
import numpy as np
from PIL import Image
from scipy import ndimage

path, name = sys.argv[1], sys.argv[2]
THRESH = int(sys.argv[3]); DIL = int(sys.argv[4]); MININK = int(sys.argv[5])
DEDUP = sys.argv[6] == "1"
MAXFRAC = float(sys.argv[7]) if len(sys.argv) > 7 else 0.18

im = Image.open(path).convert("L"); a = np.array(im); H, W = a.shape
ink = a < THRESH
d = ndimage.binary_dilation(ink, iterations=DIL)
lbl, n = ndimage.label(d)
slices = ndimage.find_objects(lbl)
cand = []
for i, sl in enumerate(slices):
    if sl is None: continue
    ys, xs = sl
    mask = lbl[sl] == (i + 1)
    inkpix = int((ink[sl] & mask).sum())
    bw, bh = xs.stop - xs.start, ys.stop - ys.start
    if inkpix < MININK: continue
    if bw * bh > MAXFRAC * H * W: continue          # skip giant merged blobs
    if bw < 10 or bh < 10: continue
    cand.append((xs.start, ys.start, xs.stop, ys.stop, inkpix))

# sort reading order (top->bottom, left->right) by row bands
cand.sort(key=lambda c: (round(c[1] / 60), c[0]))

outdir = f"Tools/sheets/{name}_cuts"
os.makedirs(outdir, exist_ok=True)
for f in glob.glob(outdir + "/*.png"):
    os.remove(f)

def sig(box):
    x0, y0, x1, y1 = box[:4]
    c = im.crop((x0, y0, x1, y1)).resize((24, 24))
    return (np.array(c) < THRESH).astype(float)

kept = []
sigs = []
for c in cand:
    if DEDUP:
        s = sig(c)
        dup = False
        for ks in sigs:
            inter = (s * ks).sum(); uni = ((s + ks) > 0).sum()
            if uni and inter / uni > 0.62:
                dup = True; break
        if dup: continue
        sigs.append(s)
    kept.append(c)

PAD = 8
for idx, (x0, y0, x1, y1, _ip) in enumerate(kept):
    cx0 = max(0, x0 - PAD); cy0 = max(0, y0 - PAD); cx1 = min(W, x1 + PAD); cy1 = min(H, y1 + PAD)
    im.crop((cx0, cy0, cx1, cy1)).save(f"{outdir}/c{idx:03d}.png")
print(f"{name}: {len(cand)} candidates -> {len(kept)} kept (dedup={DEDUP})")

# ---- render numbered catalog via HTML + Chrome ----
fps = sorted(glob.glob(outdir + "/c*.png"))
N = len(fps); COLS = 10; CELL = 150
rows = math.ceil(N / COLS); Wp = COLS * CELL; Hp = max(CELL, rows * CELL)
cells = []
for i, fp in enumerate(fps):
    cells.append(f'<div class=c><span class=n>{i+1}</span><img src="file://{os.path.abspath(fp)}"></div>')
html = f"""<html><head><meta charset=utf8><style>
*{{margin:0;box-sizing:border-box}} body{{background:#fff}}
.g{{display:grid;grid-template-columns:repeat({COLS},{CELL}px);width:{Wp}px}}
.c{{position:relative;height:{CELL}px;border:1px solid #e8e8e8;display:flex;align-items:center;justify-content:center}}
.c img{{max-width:80%;max-height:76%;margin-top:6px}}
.n{{position:absolute;top:2px;left:5px;color:#c00;font:bold 18px Arial}}
</style></head><body><div class=g>{''.join(cells)}</div></body></html>"""
open(f"/tmp/{name}_cat.html", "w").write(html)
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "--headless",
                "--disable-gpu", "--force-device-scale-factor=2", "--hide-scrollbars",
                f"--screenshot=/tmp/{name}_catalog.png", f"--window-size={Wp},{Hp}",
                f"file:///tmp/{name}_cat.html"], stderr=subprocess.DEVNULL)
print(f"catalog -> /tmp/{name}_catalog.png  ({COLS}x{rows})")
