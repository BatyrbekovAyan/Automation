"""Outline-trace the 45 prepared cutouts (Tools/sel/s*.png, white-on-black) into FILLED
vector contours -> Tools/sel_vec.json.

Unlike centerline tracing (skeleton+spline, which distorts/erases shapes), this traces the
actual stroke boundaries with skimage.find_contours, so the doodle is reproduced exactly
and stays razor-sharp at any scale. Rendered filled (fill-rule evenodd) by pack_selection.py.
"""
import glob, json
import numpy as np
from PIL import Image
from skimage import measure

fps = sorted(glob.glob("Tools/sel/s*.png"))
manifest = json.load(open("Tools/sel/manifest.json"))
labels = {m["file"]: m["label"] for m in manifest}

TOL = 0.6           # polygon simplification tolerance (low = faithful)
vectors = []
for fp in fps:
    a = np.array(Image.open(fp).convert("L"))
    mask = a > 60
    ys, xs = np.where(mask)
    if len(xs) == 0:
        vectors.append({"vbW": 10.0, "vbH": 10.0, "paths": [], "label": labels.get(fp, ""), "fill": True}); continue
    # tight crop + 2px pad so all stroke boundaries are closed loops
    a = mask[ys.min():ys.max()+1, xs.min():xs.max()+1]
    a = np.pad(a, 2)
    h0, w0 = a.shape
    contours = measure.find_contours(a.astype(float), 0.5)
    paths = []
    for c in contours:
        c = measure.approximate_polygon(c, TOL)
        if len(c) < 3: continue
        # c is (row=y, col=x); shift back out the 2px pad
        pts = [(x - 2, y - 2) for (y, x) in c]
        d = "M" + " L".join(f"{x:.1f} {y:.1f}" for (x, y) in pts) + " Z"
        paths.append(d)
    vbW, vbH = float(w0 - 4), float(h0 - 4)
    vectors.append({"vbW": vbW, "vbH": vbH, "paths": paths, "label": labels.get(fp, ""), "fill": True})

json.dump(vectors, open("Tools/sel_vec.json", "w"))
print("outline-traced", len(vectors), "| contour counts:", [len(v["paths"]) for v in vectors])
