"""Render a clean, globally-numbered catalog from a keep-list of crops.

Usage: python3 render_catalog.py <cuts_dir> <start_num> <name> <idx:label,idx:label,...>
Copies kept crops to Tools/sheets/<name>_final/, renders /tmp/<name>_final.png,
and appends a mapping (global# -> source, label) to Tools/sheets/<name>_map.json
"""
import sys, os, glob, math, subprocess, json, shutil

cuts_dir, start, name, spec = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4]
pairs = []
for tok in spec.split(","):
    tok = tok.strip()
    if not tok: continue
    if ":" in tok:
        i, lab = tok.split(":", 1)
    else:
        i, lab = tok, ""
    pairs.append((int(i), lab))

final = f"Tools/sheets/{name}_final"
os.makedirs(final, exist_ok=True)
for f in glob.glob(final + "/*.png"): os.remove(f)

mapping = {}
items = []
for n, (src_idx, label) in enumerate(pairs):
    gnum = start + n
    src = f"{cuts_dir}/c{src_idx:03d}.png"
    if not os.path.exists(src):
        print("MISSING", src); continue
    dst = f"{final}/g{gnum:03d}.png"
    shutil.copy(src, dst)
    mapping[gnum] = {"source": src, "label": label}
    items.append((gnum, dst, label))

COLS = 8; CELL = 168
rows = math.ceil(len(items) / COLS); Wp = COLS * CELL; Hp = max(CELL, rows * CELL)
cells = []
for gnum, dst, label in items:
    cells.append(
        f'<div class=c><span class=n>{gnum}</span>'
        f'<img src="file://{os.path.abspath(dst)}">'
        f'<span class=l>{label}</span></div>')
html = f"""<html><head><meta charset=utf8><style>
*{{margin:0;box-sizing:border-box}} body{{background:#fff}}
.g{{display:grid;grid-template-columns:repeat({COLS},{CELL}px);width:{Wp}px}}
.c{{position:relative;height:{CELL}px;border:1px solid #e6e6e6;display:flex;align-items:center;justify-content:center}}
.c img{{max-width:74%;max-height:64%;margin-top:2px}}
.n{{position:absolute;top:2px;left:5px;color:#c00;font:bold 19px Arial}}
.l{{position:absolute;bottom:3px;left:0;right:0;text-align:center;color:#555;font:11px Arial}}
</style></head><body><div class=g>{''.join(cells)}</div></body></html>"""
open(f"/tmp/{name}_final.html", "w").write(html)
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "--headless",
                "--disable-gpu", "--force-device-scale-factor=2", "--hide-scrollbars",
                f"--screenshot=/tmp/{name}_final.png", f"--window-size={Wp},{Hp}",
                f"file:///tmp/{name}_final.html"], stderr=subprocess.DEVNULL)
json.dump(mapping, open(f"Tools/sheets/{name}_map.json", "w"), indent=1)
print(f"{name}: {len(items)} doodles, #{start}..#{start+len(items)-1} -> /tmp/{name}_final.png")
