"""Objective metrics + judge-friendly previews for doodle wallpaper variants.

Per variant computes:
  - density      : % of pixels that are ink (paper #F5F2EA -> not ink)
  - max_gap      : radius (px) of the largest empty (paper) disk -> directly measures
                   "hollow hall" feeling. Lower is better.
  - evenness     : 1 - (std/mean) of local ink density over a coarse grid. Higher=more even.
Also writes a contrast-boosted full preview + a native-res crop per variant so visual
judges can clearly see the (otherwise faint) strokes.
"""
import glob, os, json
import numpy as np
from PIL import Image, ImageEnhance
from scipy import ndimage

variants = sorted(glob.glob("/tmp/doodle_gen/V?.png"))
PAPER_L = 242  # grayscale of #F5F2EA
out = {}
for fp in variants:
    name = os.path.splitext(os.path.basename(fp))[0]
    im = Image.open(fp).convert("RGB")
    g = np.array(im.convert("L"))
    ink = g < (PAPER_L - 3)                       # darker than paper => a stroke
    density = 100 * ink.mean()
    # largest empty disk: distance transform of paper, at 1/4 scale for speed
    small = np.array(Image.fromarray((ink * 255).astype(np.uint8)).resize(
        (im.width // 4, im.height // 4), Image.BILINEAR)) > 80
    dt = ndimage.distance_transform_edt(~small)
    max_gap = float(dt.max() * 4)                 # back to full-res px
    # evenness over 8x6 grid of local ink density
    GX, GY = 8, 6; H, Wd = ink.shape; dens = []
    for gy in range(GY):
        for gx in range(GX):
            blk = ink[gy * H // GY:(gy + 1) * H // GY, gx * Wd // GX:(gx + 1) * Wd // GX]
            dens.append(blk.mean())
    dens = np.array(dens); evenness = 1 - (dens.std() / max(1e-6, dens.mean()))
    out[name] = dict(density=round(density, 2), max_gap=round(max_gap, 1),
                     evenness=round(float(evenness), 3))
    # previews for judges
    prev = ImageEnhance.Contrast(im).enhance(3.4)
    prev.resize((im.width // 3, im.height // 3)).save(f"/tmp/doodle_gen/{name}_preview.png")
    im.crop((420, 900, 1100, 1580)).save(f"/tmp/doodle_gen/{name}_crop.png")

# rank: low max_gap (hollowness) + high evenness + reasonable density
print(json.dumps(out, indent=2))
print("\nranked by max_gap (lower=less hollow):")
for n, m in sorted(out.items(), key=lambda kv: kv[1]["max_gap"]):
    print("  %-18s max_gap=%-6.1f density=%-6.2f evenness=%.3f" %
          (n, m["max_gap"], m["density"], m["evenness"]))
json.dump(out, open("/tmp/doodle_gen/metrics.json", "w"))
