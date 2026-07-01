"""Centerline-trace the 45 cutouts into SMOOTH paths rendered at UNIFORM stroke width.
Fixes 'drunk' wobbly lines (pre-blur before skeletonize + spline smoothing) and varying ink
thickness (packer strokes every doodle at one constant width). -> Tools/sel_vec.json (stroke mode).
"""
import glob, json, math
import numpy as np
from PIL import Image, ImageFilter
from skimage.morphology import skeletonize
from scipy.interpolate import splprep, splev

UP = 6; BLUR = 2.2; SF = 2.2
KEEP = 6        # normal strokes: keep if length >= UP*KEEP
SPUR = 5        # junction whiskers shorter than UP*SPUR are dropped
MIN = 1.6       # standalone marks shorter than UP*MIN are noise
fps = sorted(glob.glob("Tools/sel/s*.png"))
manifest = json.load(open("Tools/sel/manifest.json"))
labels = {m["file"]: m["label"] for m in manifest}

def nbrs(p, S):
    y, x = p; return [(y+dy, x+dx) for dy in (-1,0,1) for dx in (-1,0,1) if (dy or dx) and (y+dy, x+dx) in S]
def trace(skel):
    S = set(map(tuple, np.argwhere(skel))); deg = {p: len(nbrs(p, S)) for p in S}; used = set(); polys = []
    def walk(a, b):
        path = [a, b]; used.add(frozenset((a, b))); prev, cur = a, b
        while deg.get(cur, 0) == 2:
            nx = [q for q in nbrs(cur, S) if q != prev and frozenset((cur, q)) not in used]
            if not nx: break
            q = nx[0]; used.add(frozenset((cur, q))); path.append(q); prev, cur = cur, q
        return path
    for n in [p for p in S if deg[p] != 2]:
        for nb in nbrs(n, S):
            if frozenset((n, nb)) not in used:
                pth = walk(n, nb); polys.append((pth, deg.get(pth[0], 0), deg.get(pth[-1], 0)))
    for p in S:
        for nb in nbrs(p, S):
            if frozenset((p, nb)) not in used:
                pth = walk(p, nb); polys.append((pth, deg.get(pth[0], 0), deg.get(pth[-1], 0)))
    return polys
def smooth(poly):
    ys = np.array([p[0] for p in poly], float); xs = np.array([p[1] for p in poly], float); n = len(poly)
    if n < 5: return "M" + " L".join(f"{x/UP:.2f} {y/UP:.2f}" for y, x in zip(ys, xs))
    closed = math.hypot(xs[0]-xs[-1], ys[0]-ys[-1]) < UP*2 and n > 8
    try:
        tck, u = splprep([xs, ys], s=n*SF, k=3, per=1 if closed else 0)
        uu = np.linspace(0, 1, max(24, n//3)); xo, yo = splev(uu, tck)
    except Exception:
        xo, yo = xs, ys
    return "M" + " L".join(f"{x/UP:.2f} {y/UP:.2f}" for x, y in zip(xo, yo)) + (" Z" if closed else "")

vectors = []
for fp in fps:
    a = np.array(Image.open(fp).convert("L")); m = a > 60
    ys, xs = np.where(m)
    if len(xs) == 0:
        vectors.append({"vbW": 10.0, "vbH": 10.0, "paths": [], "label": labels.get(fp, "")}); continue
    sub = Image.fromarray((m[ys.min():ys.max()+1, xs.min():xs.max()+1]*255).astype(np.uint8))
    w0, h0 = sub.size
    big = sub.resize((w0*UP, h0*UP), Image.LANCZOS).filter(ImageFilter.GaussianBlur(BLUR))
    skel = skeletonize(np.array(big) > 90)
    paths = []
    for poly, da, db in trace(skel):
        if len(poly) < 2: continue
        L = sum(math.hypot(poly[k+1][0]-poly[k][0], poly[k+1][1]-poly[k][1]) for k in range(len(poly)-1))
        closed = math.hypot(poly[0][0]-poly[-1][0], poly[0][1]-poly[-1][1]) < UP*2
        if L < UP*MIN:
            continue                                  # tiny noise
        if closed or L >= UP*KEEP:
            pass                                      # loops (lens rings) + normal strokes -> keep
        elif da <= 1 and db <= 1:
            pass                                      # standalone mark (dot/tick/home-button) -> keep
        elif (da >= 3 or db >= 3) and L < UP*SPUR:
            continue                                  # short junction whisker -> drop
        paths.append(smooth(poly))
    vectors.append({"vbW": float(w0), "vbH": float(h0), "paths": paths, "label": labels.get(fp, "")})

json.dump(vectors, open("Tools/sel_vec.json", "w"))
print("centerline HD:", len(vectors), "| path counts:", [len(v["paths"]) for v in vectors])
