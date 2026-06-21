import glob, os, re, json, subprocess, sys
import numpy as np
from scipy import ndimage
from PIL import Image

cuts=[]
for fp in sorted(glob.glob("cutouts/d*.png")):
    a=np.array(Image.open(fp).convert("L")); ys,xs=np.where(a>40)
    if len(xs)==0: continue
    cuts.append(a[ys.min():ys.max()+1, xs.min():xs.max()+1])

# perceptual dedup (shape IoU on 28x28 binary)
def sig(a):
    s=np.array(Image.fromarray(a).resize((28,28),Image.BILINEAR)).astype(float)
    return (s>55).astype(float)
sigs=[sig(a) for a in cuts]; keep=[]; ks=[]
for i,s in enumerate(sigs):
    dup=False
    for k in ks:
        iou=(s*k).sum()/max(1,((s+k)>0).sum())
        if iou>0.60: dup=True; break
    if not dup: keep.append(i); ks.append(s)
print("dedup: kept",len(keep),"of",len(cuts))

TARGET_HALF=2.2   # at 4x -> ~1.1px native uniform stroke
os.makedirs("vec",exist_ok=True); vectors=[]
for i in keep:
    a=cuts[i].astype(float)
    big=np.array(Image.fromarray(cuts[i]).resize((a.shape[1]*4,a.shape[0]*4),Image.BILINEAR))
    mask=big>110
    if mask.sum()<40: continue
    dist=ndimage.distance_transform_edt(mask)
    sh=np.percentile(dist[mask],78)
    er=int(max(0,min(9,round(sh-TARGET_HALF))))
    thin=ndimage.binary_erosion(mask,iterations=er) if er>0 else mask
    if thin.sum()<40: thin=mask
    Image.fromarray((255-thin.astype(np.uint8)*255)).save("vec/t.pgm")
    subprocess.run(["potrace","--svg","--turdsize","3","--alphamax","1.3","vec/t.pgm","-o","vec/t.svg"],stderr=subprocess.DEVNULL)
    svg=open("vec/t.svg").read()
    m=re.search(r'viewBox="([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+)"',svg)
    gm=re.search(r'(<g transform=.*?</g>)',svg,re.S)
    if not m or not gm: continue
    inner=re.sub(r'fill="#[0-9a-fA-F]+"','',gm.group(1))
    vectors.append({"vbW":float(m.group(3)),"vbH":float(m.group(4)),"inner":inner})
json.dump(vectors,open("cutvec.json","w"))
print("normalized vectors:",len(vectors))
