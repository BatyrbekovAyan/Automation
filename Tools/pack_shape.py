import json, random, math, subprocess, re
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

W,Hc=1596,2688; PAPER="#F5F2EA"; INK="#E5DAC6"; TW=3.4
EXCL={8,20,60,73,75,77,114}
S=3                       # occupancy downscale
GAPpx=5.0                 # spacing between doodle silhouettes (canvas px)
MINDUP=560.0
rng=random.Random(7)
Vall=json.load(open("cutvec_cl.json"))
V=[v for i,v in enumerate(Vall) if i not in EXCL]
print("doodles:",len(V))

# --- build silhouette footprint per doodle (normalized to max-dim REF) ---
REF=140
def pts_of(d):
    nums=re.findall(r'-?\d+\.?\d*',d); return [(float(nums[i]),float(nums[i+1])) for i in range(0,len(nums)-1,2)]
foot=[]   # (mask bool HxW at REF scale, fw, fh) per doodle
for v in V:
    vbW,vbH=v["vbW"],v["vbH"]; sc=REF/max(vbW,vbH); fw,fh=max(2,int(vbW*sc)),max(2,int(vbH*sc))
    img=Image.new("L",(fw,fh),0); dr=ImageDraw.Draw(img)
    sw=max(2,int(TW*sc*0.6))
    for d in v["paths"]:
        p=[(x*sc,y*sc) for (x,y) in pts_of(d)]
        if len(p)>=2: dr.line(p,fill=255,width=sw,joint="curve")
    m=np.array(img)>40
    m=ndimage.binary_dilation(m,iterations=max(1,int(REF*0.03)))
    m=ndimage.binary_fill_holes(m)
    foot.append((m,fw,fh))

# --- occupancy packing ---
occ=np.zeros((Hc//S+2, W//S+2),bool)
cache={}
def scaled_foot(idx,size):
    key=(idx, round(size/6))
    if key in cache: return cache[key]
    m,fw,fh=foot[idx]; tgt=max(2,int(size/S))
    sm=Image.fromarray((m*255).astype(np.uint8)).resize((max(2,int(fw*tgt/max(fw,fh))),max(2,int(fh*tgt/max(fw,fh)))),Image.BILINEAR)
    a=np.array(sm)>110
    a=ndimage.binary_dilation(a,iterations=max(1,int(GAPpx/S)))
    cache[key]=a; return a
GC,GR=12,20; cw,ch=W/GC,Hc/GR; cellpool=[]
def next_pos():
    global cellpool
    if not cellpool: cellpool=[(c,r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
    c,r=cellpool.pop(); return (c+0.5)*cw+rng.uniform(-cw*0.55,cw*0.55),(r+0.5)*ch+rng.uniform(-ch*0.55,ch*0.55)
deck=[]
def pick():
    global deck
    if not deck: deck=list(range(len(V))); rng.shuffle(deck)
    return deck.pop()
byidx={}; parts=[]
def place_tier(lo,hi,stop,cap=None):
    cnt=0; miss=0
    while miss<stop:
        if cap and cnt>=cap: break
        idx=pick(); v=V[idx]; size=rng.uniform(lo,hi)
        fa=scaled_foot(idx,size); fh2,fw2=fa.shape
        cx,cy=next_pos(); ox=int(cx/S-fw2/2); oy=int(cy/S-fh2/2)
        if ox<0 or oy<0 or ox+fw2>occ.shape[1] or oy+fh2>occ.shape[0]: miss+=1; continue
        if not all((cx-px)**2+(cy-py)**2>=MINDUP*MINDUP for (px,py) in byidx.get(idx,())): miss+=1; continue
        sub=occ[oy:oy+fh2, ox:ox+fw2]
        if np.logical_and(sub,fa).any(): miss+=1; continue
        occ[oy:oy+fh2, ox:ox+fw2]|=fa
        vbW,vbH=v["vbW"],v["vbH"]; sc=size/max(vbW,vbH); rot=rng.uniform(-9,9)
        parts.append(f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({sc:.4f}) translate({-vbW/2:.1f} {-vbH/2:.1f})" stroke-width="{TW/sc:.2f}">'+"".join(f'<path d="{d}"/>' for d in v["paths"])+'</g>')
        byidx.setdefault(idx,[]).append((cx,cy)); cnt+=1; miss=0
    return cnt
place_tier(190,250,800,cap=28)
place_tier(120,175,5000,cap=240)
place_tier(82,118,20000)
place_tier(56,80,28000)
place_tier(38,55,36000)
print("total",len(parts),"| occupancy %.1f%%"%(100*occ.mean()))
svg=f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}"><rect width="{W}" height="{Hc}" fill="{PAPER}"/><g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">'+"".join(parts)+'</g></svg>'
open("shapepack.svg","w").write(svg)
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome","--headless","--disable-gpu","--force-device-scale-factor=1","--screenshot=/tmp/doodle_gen/shapepack.png",f"--window-size={W},{Hc}","file:///tmp/doodle_gen/shapepack.svg"],stderr=subprocess.DEVNULL)
g=np.array(Image.open("shapepack.png").convert("L")); H2=g.shape[0]; ink=g<238
print("ink %.1f%%"%(100*ink.mean()),"bands",[round(100*ink[i*H2//6:(i+1)*H2//6].mean(),1) for i in range(6)])
