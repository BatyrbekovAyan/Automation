"""Batch-segment the 8 Gemini doodle sheets -> per-sheet cuts + catalogs.

Based on Tools/segment_sheet.py, with one addition: a CONTAINMENT filter that
drops fragment blobs whose bbox lies (almost) inside a bigger blob's bbox —
fixes doodles whose detached strokes split into multiple components (kiwi case)
without needing per-sheet dilation tuning.
"""
import os, glob, math, subprocess
import numpy as np
from PIL import Image
from scipy import ndimage

SHEETS = sorted(glob.glob("Tools/doodle-sheets/*.png"))
THRESH, DIL, MININK, MAXFRAC = 128, 6, 300, 0.10
MARGIN = 10          # containment slack in px
IOU_DUP = 0.62

def segment(path, name):
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
        if bw * bh > MAXFRAC * H * W: continue
        if bw < 10 or bh < 10: continue
        cand.append([xs.start, ys.start, xs.stop, ys.stop, inkpix, i + 1])

    # containment: blob A inside blob B's bbox -> A is B's inner detail (mug heart,
    # kiwi seeds). Merge A's label into B so it renders in B's crop; drop A's own crop.
    drop = set(); children = {}
    for i, A in enumerate(cand):
        for j, B in enumerate(cand):
            if i == j or j in drop: continue
            if A[4] <= B[4] and \
               A[0] >= B[0] - MARGIN and A[1] >= B[1] - MARGIN and \
               A[2] <= B[2] + MARGIN and A[3] <= B[3] + MARGIN:
                drop.add(i); children.setdefault(B[5], []).append(A[5]); break
    contained = len(drop)
    cand = [c for i, c in enumerate(cand) if i not in drop]

    cand.sort(key=lambda c: (round(c[1] / 60), c[0]))

    # dedup near-identical crops (24x24 ink-mask IoU)
    def sig(box):
        c = im.crop((box[0], box[1], box[2], box[3])).resize((24, 24))
        return (np.array(c) < THRESH).astype(float)
    kept, sigs, dups = [], [], 0
    for c in cand:
        s = sig(c)
        if any(((s * ks).sum() / max(((s + ks) > 0).sum(), 1)) > IOU_DUP for ks in sigs):
            dups += 1; continue
        sigs.append(s); kept.append(c)

    outdir = f"Tools/sheets/{name}_cuts"
    os.makedirs(outdir, exist_ok=True)
    for f in glob.glob(outdir + "/*.png"): os.remove(f)
    PAD = 8
    for idx, (x0, y0, x1, y1, _ip, li) in enumerate(kept):
        cx0, cy0 = max(0, x0 - PAD), max(0, y0 - PAD)
        cx1, cy1 = min(W, x1 + PAD), min(H, y1 + PAD)
        crop = np.array(im.crop((cx0, cy0, cx1, cy1)))
        sub = lbl[cy0:cy1, cx0:cx1]
        own = sub == li                             # this blob's dilated footprint
        # + ANY blob (even sub-MININK: faces, sparkles) whose bbox sits inside this box
        for other in np.unique(sub):
            if other == 0 or other == li: continue
            oys, oxs = slices[other - 1]
            if oxs.start >= x0 - MARGIN and oys.start >= y0 - MARGIN and \
               oxs.stop <= x1 + MARGIN and oys.stop <= y1 + MARGIN:
                own |= sub == other
        crop[~own] = 255                            # white-out neighbor ink bleed
        Image.fromarray(crop).save(f"{outdir}/c{idx:03d}.png")
    print(f"{name}: {len(kept)} kept  (contained-dropped {contained}, dup-dropped {dups})")
    return outdir

def catalog(outdir, name):
    fps = sorted(glob.glob(outdir + "/c*.png"))
    N = len(fps); COLS = 10; CELL = 150
    rows = math.ceil(N / COLS); Wp = COLS * CELL; Hp = max(CELL, rows * CELL)
    cells = [f'<div class=c><span class=n>{i+1}</span><img src="file://{os.path.abspath(fp)}"></div>'
             for i, fp in enumerate(fps)]
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

total = 0
for i, fp in enumerate(SHEETS):
    name = f"sheet{i+1}"
    outdir = segment(fp, name)
    catalog(outdir, name)
    total += len(glob.glob(outdir + "/c*.png"))
print("TOTAL sprites:", total)
