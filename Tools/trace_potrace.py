"""HD vectorize the 45 cutouts with potrace (smooth Beziers) -> Tools/sel_vec.json.

Pipeline per cutout (white-on-black): invert -> LANCZOS 8x upscale -> Gaussian blur ->
threshold -> potrace (alphamax 1.4, curve-opt) -> smooth filled SVG. Faithful AND razor-sharp
at any size (fixes both the centerline distortion and the low-res raster softness).
Stores potrace's viewBox + inner <g> transform + path data per doodle.
"""
import glob, json, subprocess, re, os
from PIL import Image, ImageOps, ImageFilter

fps = sorted(glob.glob("Tools/sel/s*.png"))
manifest = json.load(open("Tools/sel/manifest.json"))
labels = {m["file"]: m["label"] for m in manifest}
os.makedirs("/tmp/pt", exist_ok=True)

vectors = []
for fp in fps:
    im = Image.open(fp).convert("L")                       # white strokes on black
    inv = ImageOps.invert(im)                               # black strokes on white
    w, h = inv.size
    big = inv.resize((w*8, h*8), Image.LANCZOS).filter(ImageFilter.GaussianBlur(2.4))
    bw = big.point(lambda p: 0 if p < 128 else 255).convert("1")
    pbm = "/tmp/pt/_t.pbm"; svg = "/tmp/pt/_t.svg"
    bw.save(pbm)
    subprocess.run(["potrace", pbm, "-s", "-a", "1.4", "-O", "0.45", "-o", svg], stderr=subprocess.DEVNULL)
    s = open(svg).read()
    vb = re.search(r'viewBox="0 0 ([\d.]+) ([\d.]+)"', s)
    gt = re.search(r'<g transform="([^"]+)"', s)
    paths = re.findall(r'<path d="([^"]+)"', s)
    if not (vb and gt and paths):
        vectors.append({"vbW": 10.0, "vbH": 10.0, "gt": "", "paths": [], "label": labels.get(fp, "")}); continue
    vectors.append({"vbW": float(vb.group(1)), "vbH": float(vb.group(2)),
                    "gt": gt.group(1), "paths": paths, "label": labels.get(fp, "")})

json.dump(vectors, open("Tools/sel_vec.json", "w"))
print("potrace HD:", len(vectors), "| path counts:", [len(v["paths"]) for v in vectors])
