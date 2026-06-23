"""Render every vectorized doodle (from cutvec_cl.json) labeled by ORIGINAL index,
so badly-traced doodles can be identified and added to EXCL in pack_param.py.

Renders the SAME stroke paths the wallpaper uses (black on white for clarity).
Currently-excluded indices get a red 'X' badge.
"""
import json, re, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

EXCL = {8, 20, 60, 73, 75, 77, 114}          # current excludes in pack_param.py
Vall = json.load(open("cutvec_cl.json"))
N = len(Vall)
print("total doodles in json:", N, "| currently excluded:", sorted(EXCL))

def pts_of(d):
    nums = re.findall(r'-?\d+\.?\d*', d)
    return [(float(nums[i]), float(nums[i + 1])) for i in range(0, len(nums) - 1, 2)]

COLS = 10
CELL = 210
PAD = 18
rows = math.ceil(N / COLS)
M = Image.new("RGB", (COLS * CELL, rows * CELL), (255, 255, 255))
dr = ImageDraw.Draw(M)
try:
    font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 18)
except Exception:
    font = ImageFont.load_default()

for i, v in enumerate(Vall):
    cx0 = (i % COLS) * CELL; cy0 = (i // COLS) * CELL
    vbW, vbH = v["vbW"], v["vbH"]; sc = (CELL - 2 * PAD) / max(vbW, vbH)
    ox = cx0 + (CELL - vbW * sc) / 2; oy = cy0 + (CELL - vbH * sc) / 2 + 6
    for d in v["paths"]:
        p = [(ox + x * sc, oy + y * sc) for (x, y) in pts_of(d)]
        if len(p) >= 2: dr.line(p, fill=(20, 20, 20), width=2, joint="curve")
    # index label
    dr.rectangle([cx0, cy0, cx0 + 30, cy0 + 18], fill=(255, 255, 255))
    dr.text((cx0 + 2, cy0 + 1), str(i), fill=(0, 90, 200), font=font)
    if i in EXCL:
        dr.text((cx0 + CELL - 22, cy0 + 1), "X", fill=(220, 0, 0), font=font)
    dr.rectangle([cx0, cy0, cx0 + CELL - 1, cy0 + CELL - 1], outline=(220, 220, 220))

M.save("/tmp/doodle_audit.png")
print("saved /tmp/doodle_audit.png", M.size, "| grid", COLS, "x", rows)

# quadrants for clearer inspection: left cols 0-4, right cols 5-9; top rows 0-6, bottom 7-12
midc = 5 * CELL; midr = 7 * CELL
quads = {
    "Q1_idx0-34_TL":  (0, 0, midc, midr),
    "Q2_idx5-39_TR":  (midc, 0, COLS * CELL, midr),
    "Q3_idx70-114_BL": (0, midr, midc, rows * CELL),
    "Q4_idx75-124_BR": (midc, midr, COLS * CELL, rows * CELL),
}
for name, box in quads.items():
    M.crop(box).save(f"/tmp/doodle_audit_{name}.png")
print("saved 4 quadrants")
