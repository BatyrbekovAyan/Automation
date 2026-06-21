import glob, random, math
import numpy as np
from PIL import Image

W,Hc=1596,2688
PAPER=(245,242,234); INK=(229,218,198)
GAP=4.0; CELL=170.0; BF=1.0; MAXUP=2.2     # bigger doodles; cap upscale to limit blur
rng=random.Random(7)

cuts=[]
for fp in sorted(glob.glob("cutouts/d*.png")):
    a=np.array(Image.open(fp).convert("L")); ys,xs=np.where(a>40)
    if len(xs)==0: continue
    cuts.append(a[ys.min():ys.max()+1, xs.min():xs.max()+1])
print("sprites:",len(cuts))
def tinted(a):
    h,w=a.shape; img=Image.new("RGBA",(w,h),(INK[0],INK[1],INK[2],0)); img.putalpha(Image.fromarray(a)); return img
canvas=Image.new("RGBA",(W,Hc),(PAPER[0],PAPER[1],PAPER[2],255))
grid={}; placed=[]
def fits(cx,cy,hw,hh):
    span=int((max(hw,hh)+GAP+260)//CELL)+1; gx,gy=int(cx//CELL),int(cy//CELL)
    for ix in range(gx-span,gx+span+1):
        for iy in range(gy-span,gy+span+1):
            for (px,py,phw,phh) in grid.get((ix,iy),()):
                if abs(cx-px)<hw+phw+GAP and abs(cy-py)<hh+phh+GAP: return False
    return True
deck=[]
def pick():
    global deck
    if not deck: deck=list(range(len(cuts))); rng.shuffle(deck)
    return deck.pop()
def place_tier(lo,hi,stop,cap=None):
    cnt=0; miss=0
    while miss<stop:
        if cap and cnt>=cap: break
        a=cuts[pick()]; h0,w0=a.shape
        s=min(rng.uniform(lo,hi)/max(w0,h0), MAXUP)
        sw,sh=w0*s,h0*s
        th=math.radians(rng.uniform(-9,9)); c,sn=abs(math.cos(th)),abs(math.sin(th))
        bw=sw*c+sh*sn; bh=sw*sn+sh*c; hw,hh=bw*0.5,bh*0.5
        cx=rng.uniform(hw*0.5,W-hw*0.5); cy=rng.uniform(hh*0.5,Hc-hh*0.5)
        if not fits(cx,cy,hw,hh): miss+=1; continue
        spr=tinted(a).resize((max(1,int(sw)),max(1,int(sh))),Image.BILINEAR).rotate(math.degrees(th),expand=True,resample=Image.BILINEAR)
        rw,rh=spr.size; canvas.alpha_composite(spr,(int(cx-rw/2),int(cy-rh/2)))
        grid.setdefault((int(cx//CELL),int(cy//CELL)),[]).append((cx,cy,hw,hh))
        placed.append(1); cnt+=1; miss=0
    return cnt
L=place_tier(190,250,400,cap=22)
M=place_tier(125,180,2000,cap=170)
S=place_tier(86,122,9000)
T=place_tier(60,84,13000)
print("L/M/S/T:",L,M,S,len(placed)-L-M-S,"total",len(placed))
out=Image.new("RGB",(W,Hc),PAPER); out.paste(canvas,(0,0),canvas); out.save("cutpack.png")
g=np.array(out.convert("L")); print("ink %.1f%%"%(100*(g<236).mean()))
