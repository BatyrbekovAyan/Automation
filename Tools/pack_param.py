"""Parameterized WhatsApp-style doodle wallpaper packer — variant explorer.

Fixes the "hollow big doodle" problem (large outline shapes look like empty halls).
Strategies, selectable per variant config:
  - smaller / more uniform / denser  (authentic WhatsApp mimicry)
  - density-gating: large sizes only allowed for "full" (busy) doodles, judged by an
    interior-fullness metric (fraction of a 6x6 bbox grid that contains stroke ink)
  - stroke-only footprints: occupancy marks only the strokes (no fill_holes), so small
    doodles nest INSIDE big outlines' interiors -> kills the hollow look

Locked colors: paper #F5F2EA, ink #E5DAC6. Stroke >=4px (washout threshold).
Renders each variant at 2x supersample -> Lanczos downscale -> {out}.png
"""
import json, random, math, subprocess, re, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

W, Hc = 1596, 2688
PAPER = "#F5F2EA"; INK = "#E5DAC6"
EXCL = {8, 20, 60, 73, 75, 77, 114,                 # original
        9, 21, 28, 45, 80, 93, 94, 95, 107, 116}    # bad traces found in audit (kept 87,89,109)
S = 3                 # occupancy downscale
SS = 2                # render supersample
REF = 140             # footprint reference max-dim
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

Vall = json.load(open("cutvec_cl.json"))
V = [v for i, v in enumerate(Vall) if i not in EXCL]

def pts_of(d):
    nums = re.findall(r'-?\d+\.?\d*', d)
    return [(float(nums[i]), float(nums[i + 1])) for i in range(0, len(nums) - 1, 2)]

# --- per-doodle base mask (REF scale) + interior-fullness metric ---
base = []        # (filled_mask, stroke_mask, fw, fh)
fullness = []    # fraction of 6x6 grid cells containing stroke ink
GRID = 6
for v in V:
    vbW, vbH = v["vbW"], v["vbH"]; sc = REF / max(vbW, vbH)
    fw, fh = max(2, int(vbW * sc)), max(2, int(vbH * sc))
    img = Image.new("L", (fw, fh), 0); dr = ImageDraw.Draw(img)
    sw = max(2, int(4.2 * sc * 0.6))
    for d in v["paths"]:
        p = [(x * sc, y * sc) for (x, y) in pts_of(d)]
        if len(p) >= 2: dr.line(p, fill=255, width=sw, joint="curve")
    stroke = np.array(img) > 40
    filled = ndimage.binary_fill_holes(ndimage.binary_dilation(stroke, iterations=max(1, int(REF * 0.03))))
    base.append((filled, stroke, fw, fh))
    # fullness: of the GRIDxGRID cells overlapping the bbox, how many contain stroke ink
    cells = 0; hit = 0
    for gy in range(GRID):
        for gx in range(GRID):
            y0, y1 = gy * fh // GRID, (gy + 1) * fh // GRID
            x0, x1 = gx * fw // GRID, (gx + 1) * fw // GRID
            cells += 1
            if stroke[y0:max(y1, y0 + 1), x0:max(x1, x0 + 1)].any(): hit += 1
    fullness.append(hit / max(1, cells))

print("doodles:", len(V), "| fullness range %.2f-%.2f" % (min(fullness), max(fullness)))


def run_variant(cfg):
    rng = random.Random(cfg.get("seed", 7))
    GAP = cfg.get("gap", 4.0)
    TW = cfg.get("stroke_w", 4.2)
    MINDUP = cfg.get("dup_min", 520.0)
    foot_mode = cfg.get("footprint", "fill")
    gate = cfg.get("gate_large")          # {"above":px, "min_fullness":f} or None
    occ = np.zeros((Hc // S + 2, W // S + 2), bool)
    cache = {}

    def scaled_foot(idx, size):
        key = (idx, round(size / 6), foot_mode)
        if key in cache: return cache[key]
        filled, stroke, fw, fh = base[idx]
        m = stroke if foot_mode == "stroke" else filled
        tgt = max(2, int(size / S))
        sm = Image.fromarray((m * 255).astype(np.uint8)).resize(
            (max(2, int(fw * tgt / max(fw, fh))), max(2, int(fh * tgt / max(fw, fh)))), Image.BILINEAR)
        a = np.array(sm) > 110
        a = ndimage.binary_dilation(a, iterations=max(1, int(GAP / S)))
        cache[key] = a; return a

    byidx = {}; parts = []
    def emit(idx, cx, cy, size):
        if gate and size > gate["above"] and fullness[idx] < gate["min_fullness"]:
            return False
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

    GC, GR = 12, 20; cw, ch = W / GC, Hc / GR; cellpool = []
    def next_pos():
        nonlocal cellpool
        if not cellpool:
            cellpool = [(c, r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
        c, r = cellpool.pop()
        return (c + 0.5) * cw + rng.uniform(-cw * 0.55, cw * 0.55), (r + 0.5) * ch + rng.uniform(-ch * 0.55, ch * 0.55)
    deck = []
    def pick():
        nonlocal deck
        if not deck: deck = list(range(len(V))); rng.shuffle(deck)
        return deck.pop()

    def place_tier(lo, hi, stop, cap=None):
        cnt = 0; miss = 0
        while miss < stop:
            if cap and cnt >= cap: break
            idx = pick(); cx, cy = next_pos()
            cx = min(max(cx, 28), W - 28); cy = min(max(cy, 28), Hc - 28)
            if emit(idx, cx, cy, rng.uniform(lo, hi)): cnt += 1; miss = 0
            else: miss += 1
        return cnt

    for t in cfg["tiers"]:
        lo, hi, stop = t[0], t[1], t[2]; cap = t[3] if len(t) > 3 else None
        place_tier(lo, hi, stop, cap)

    gf = cfg.get("gapfill")
    if gf:
        STEP = gf["step"]; PROB = gf.get("prob", 1.0)
        order = [(gx, gy) for gy in range(0, Hc, STEP) for gx in range(0, W, STEP)]
        rng.shuffle(order)
        for (gx, gy) in order:
            if PROB < 1.0 and rng.random() > PROB: continue
            if occ[min(gy // S, occ.shape[0] - 1), min(gx // S, occ.shape[1] - 1)]: continue
            for _ in range(6):
                idx = pick(); size = rng.uniform(gf["lo"], gf["hi"])
                jx = min(max(gx + rng.uniform(-STEP * 0.4, STEP * 0.4), 26), W - 26)
                jy = min(max(gy + rng.uniform(-STEP * 0.4, STEP * 0.4), 26), Hc - 26)
                if emit(idx, jx, jy, size): break

    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}">'
           f'<rect width="{W}" height="{Hc}" fill="{PAPER}"/>'
           f'<g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">'
           + "".join(parts) + '</g></svg>')
    out = cfg["out"]
    open(out + ".svg", "w").write(svg)
    subprocess.run([CHROME, "--headless", "--disable-gpu", f"--force-device-scale-factor={SS}",
                    f"--screenshot={out}_2x.png", f"--window-size={W},{Hc}", f"file://{out}.svg"],
                   stderr=subprocess.DEVNULL)
    big = Image.open(out + "_2x.png").convert("RGB")
    fin = big.resize((W, Hc), Image.LANCZOS); fin.save(out + ".png")
    print("%-26s doodles=%-4d occ=%.1f%%" % (cfg["name"], len(parts), 100 * occ.mean()))
    return len(parts)


CONFIGS = {
 "V1_uniform_dense": dict(
    name="V1_uniform_dense", out="/tmp/doodle_gen/V1", gap=3.6, dup_min=480,
    tiers=[[100,122,1500,40],[80,100,9000],[60,84,32000]],
    gapfill=dict(step=42, lo=46, hi=66)),
 "V2_gentle_gate": dict(
    name="V2_gentle_gate", out="/tmp/doodle_gen/V2", gap=3.6, dup_min=500,
    tiers=[[120,150,1600,50],[82,118,10000],[56,82,32000]],
    gate_large=dict(above=112, min_fullness=0.72),
    gapfill=dict(step=42, lo=44, hi=64)),
 "V3_nested": dict(
    name="V3_nested", out="/tmp/doodle_gen/V3", gap=3.4, dup_min=520, footprint="stroke",
    tiers=[[145,178,1200,24],[98,142,2500,150],[64,98,3500,520],[44,66,3500,950]],
    gapfill=dict(step=40, lo=40, hi=60)),
 "V4_gate_nested": dict(
    name="V4_gate_nested", out="/tmp/doodle_gen/V4", gap=3.4, dup_min=500, footprint="stroke",
    tiers=[[122,160,1500,40],[84,122,3000,320],[56,84,4000,1050]],
    gate_large=dict(above=110, min_fullness=0.68),
    gapfill=dict(step=40, lo=40, hi=60)),
 "V5_tight_xdense": dict(
    name="V5_tight_xdense", out="/tmp/doodle_gen/V5", gap=3.4, dup_min=440,
    tiers=[[90,106,1500,40],[72,92,3500,420],[54,74,4500,1250]],
    gapfill=dict(step=38, lo=44, hi=60)),
 # --- lower-density (fewer doodles / more breathing room), cleaned doodle set ---
 "MED": dict(
    name="MED", out="/tmp/doodle_gen/MED", gap=7.0, dup_min=500,
    tiers=[[112,140,1100,28],[82,112,3500,300],[60,86,4500,720]],
    gapfill=dict(step=54, lo=50, hi=72, prob=0.72)),
 "LIGHT": dict(
    name="LIGHT", out="/tmp/doodle_gen/LIGHT", gap=11.0, dup_min=470,
    tiers=[[116,146,950,24],[84,116,2600,230],[62,90,3200,500]],
    gapfill=dict(step=72, lo=54, hi=78, prob=0.5)),
}

if __name__ == "__main__":
    sel = sys.argv[1:] or list(CONFIGS.keys())
    for name in sel:
        run_variant(CONFIGS[name])
    print("done:", sel)
