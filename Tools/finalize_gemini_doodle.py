"""LOCKED finalizer for Gemini-generated doodle wallpapers.

Turns any raw Gemini doodle image into the approved chat-background style:
  1. recolor to the LOCKED palette  (ink #E5DAC6 on paper #F5F2EA)
  2. normalize every line to ONE uniform thickness via centerline + re-stroke
     (skeletonize -> dilate disk(2) at 3x) — NOT erosion (erosion eats lines unevenly)
  3. crop to the asset aspect and size to 1596x2688

LOCKED parameters (do not change without Ayan's OK): ink/paper hex, stroke ~4.5px uniform.

Usage: python3 Tools/finalize_gemini_doodle.py <input.png> [output.png]
       default output = Assets/Images/Chat/ChatDoodleBackground.png
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
from skimage.morphology import skeletonize, disk, binary_dilation

PAPER = np.array([245, 242, 234.])   # #F5F2EA  LOCKED
INK = np.array([232, 224, 211.])     # #E8E0D3  LOCKED v2 (lightened + less warm to match WhatsApp)
W, Hc = 1596, 2688
UPSCALE = 3
INK_THR = 200          # detect Gemini lines (any darker stroke color)
DILATE_R = 1           # disk radius at 3x -> thin uniform stroke (matched to WhatsApp)
SIGMA = 0.6
BOOST = 1.05           # near-1 so the line stays thin/delicate (no re-widening)

def finalize(src, dst):
    im = Image.open(src).convert("RGB")
    up = im.resize((im.width * UPSCALE, im.height * UPSCALE), Image.LANCZOS)
    lum = np.array(up.convert("L")).astype(float)
    sk = skeletonize(lum < INK_THR)                    # centerline of every stroke
    body = binary_dilation(sk, disk(DILATE_R))         # re-stroke at one uniform width
    alpha = np.clip(ndimage.gaussian_filter(body.astype(float), sigma=SIGMA) * BOOST, 0, 1)[..., None]
    out = (PAPER * (1 - alpha) + INK * alpha).clip(0, 255).astype(np.uint8)
    res = Image.fromarray(out)
    w, h = res.size; R = W / Hc
    if w / float(h) > R:                               # crop to asset aspect (centered)
        nw = int(h * R); x = (w - nw) // 2; res = res.crop((x, 0, x + nw, h))
    else:
        nh = int(w / R); y = (h - nh) // 2; res = res.crop((0, y, w, y + nh))
    final = res.resize((W, Hc), Image.LANCZOS)
    final.save(dst)
    a = np.array(final).astype(float); l = a @ [0.299, 0.587, 0.114]
    core = a[(l >= 210) & (l <= 226)]
    print(f"saved {dst}  ({W}x{Hc})  ink={core.mean(0).round().astype(int)}  (locked #E8E0D3)")

if __name__ == "__main__":
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else "Assets/Images/Chat/ChatDoodleBackground.png"
    finalize(src, dst)
