"""Prepare the 45 selected doodles as normalized white-on-black cutouts for tracing.

 - classic (n<=147): Tools/doodle-cutouts/d{n-1}.png are already white-on-black -> copy.
 - bold/food: dark-on-white crops -> invert.
 - dense (owl/balloon/guitar): faint dark-on-cream -> invert + threshold + keep only the
   main central connected component(s) (strip neighbour fragments).
Outputs Tools/sel/s##.png + Tools/sel/manifest.json
"""
import os, json, glob
import numpy as np
from PIL import Image
from scipy import ndimage

CLASSIC = [8,14,15,22,23,30,31,40,43,45,48,52,54,58,59,64,66,70,76,77,81,90,93,94,95,99,101,118,121,123,124,125,126,128,141,142,143]
LAB = {8:"chat",14:"phone",15:"wifi",22:"leaf",23:"bulb",30:"rain-cloud",31:"coffee",40:"dog",43:"ice-cream",45:"pizza",48:"pizza-slice",52:"donut",54:"tree",58:"burger",59:"cone",64:"clock",66:"headphones",70:"plane",76:"book",77:"paper-plane",81:"cloud",90:"camera",93:"music",94:"globe",95:"palette",99:"brush",101:"backpack",118:"sailboat",121:"handshake",123:"rocket",124:"moon",125:"glasses",126:"flag",128:"gift",141:"palette-brush",142:"megaphone",143:"pencil"}
BOLD = {156:"ghost",180:"bow"}
FOOD = {190:"candy",192:"watermelon",200:"ice-cream2"}
DENSE = ["owl","balloon","guitar"]

out = "Tools/sel"; os.makedirs(out, exist_ok=True)
for f in glob.glob(out+"/*.png"): os.remove(f)
manifest = []
sid = 0

def save_wob(arr_white_on_black, label, srctag):
    """arr: bool mask of strokes -> save white strokes on black, tight-cropped with pad."""
    global sid
    ys, xs = np.where(arr_white_on_black)
    if len(xs) == 0: return
    pad = 6
    y0,y1,x0,x1 = max(0,ys.min()-pad),ys.max()+pad, max(0,xs.min()-pad),xs.max()+pad
    sub = arr_white_on_black[y0:y1, x0:x1]
    img = Image.fromarray((sub*255).astype(np.uint8), "L")
    p = f"{out}/s{sid:02d}.png"; img.save(p)
    manifest.append({"sid": sid, "label": label, "src": srctag, "file": p})
    sid += 1

# classic: already white-on-black
for n in CLASSIC:
    a = np.array(Image.open(f"Tools/doodle-cutouts/d{n-1:03d}.png").convert("L"))
    save_wob(a > 60, LAB[n], f"classic#{n}")

# bold + food: invert, then keep only ink inside the MAIN doodle's bbox
# (removes side neighbour fragments, preserves internal details like eyes/seeds)
for n,lab in {**BOLD, **FOOD}.items():
    folder = "bold_final" if n in BOLD else "food_final"
    a = np.array(Image.open(f"Tools/sheets/{folder}/g{n:03d}.png").convert("L"))
    ink = a < 150
    d = ndimage.binary_dilation(ink, iterations=3)
    lbl,nc = ndimage.label(d)
    if nc == 0: continue
    areas = ndimage.sum(np.ones_like(lbl), lbl, range(1, nc+1))
    biggest = int(np.argmax(areas)) + 1
    main = (lbl == biggest)
    # solid silhouette of the main doodle; keep ink inside it (internal details),
    # drop everything outside (separate neighbour fragments)
    sil = ndimage.binary_fill_holes(ndimage.binary_dilation(main, iterations=2))
    save_wob(ink & sil, lab, f"sheet#{n}")

# dense: re-crop tighter from the full sheet, invert+threshold, keep ONLY largest blob
sheet2 = np.array(Image.open("Tools/sheets/sheet_2.png").convert("L"))
DBOX = {"owl": (452,548,624,752), "balloon": (66,726,210,966), "guitar": (628,430,766,546)}
for k in DENSE:
    x0,y0,x1,y1 = DBOX[k]
    a = sheet2[y0:y1, x0:x1]
    ink = a < 178
    Hc2,Wc2 = ink.shape
    if k == "guitar":
        # zero neighbour fragments around the guitar body
        ink[:, :int(0.20*Wc2)] = False                              # lightning (left)
        ink[:int(0.42*Hc2), int(0.74*Wc2):] = False                 # star (top-right)
        ink[int(0.80*Hc2):, :] = False                              # squiggle (bottom)
        ink[int(0.45*Hc2):int(0.80*Hc2), int(0.82*Wc2):] = False    # flame (right)
    d = ndimage.binary_dilation(ink, iterations=4)
    lbl,nc = ndimage.label(d)
    areas = ndimage.sum(np.ones_like(lbl), lbl, range(1, nc+1))
    biggest = int(np.argmax(areas)) + 1
    main = ink & (lbl == biggest)
    save_wob(main, k, f"dense:{k}")

json.dump(manifest, open(f"{out}/manifest.json","w"), indent=1)
print("prepared", sid, "cutouts")
