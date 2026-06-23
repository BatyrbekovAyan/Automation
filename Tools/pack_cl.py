import json, random, math, subprocess
import numpy as np
from PIL import Image
W,Hc=1596,2688; PAPER="#F5F2EA"; INK="#E5DAC6"
TW=3.4              # FIXED stroke width (canvas px) for ALL doodles, any size
GAP=6.0; CELL=170.0; MINDUP=560.0
rng=random.Random(7)
V=json.load(open("cutvec_cl.json")); EXCL={8,20,60,73,75,77,114}; V=[v for i,v in enumerate(V) if i not in EXCL]; print("vectors:",len(V))
GC,GR=12,20; cw,ch=W/GC,Hc/GR; cellpool=[]
def next_pos():
    global cellpool
    if not cellpool:
        cellpool=[(c,r) for c in range(GC) for r in range(GR)]; rng.shuffle(cellpool)
    c,r=cellpool.pop()
    return (c+0.5)*cw+rng.uniform(-cw*0.55,cw*0.55),(r+0.5)*ch+rng.uniform(-ch*0.55,ch*0.55)
grid={}; placed=[]; byidx={}
def fits(cx,cy,hw,hh):
    span=int((max(hw,hh)+GAP+260)//CELL)+1; gx,gy=int(cx//CELL),int(cy//CELL)
    for ix in range(gx-span,gx+span+1):
        for iy in range(gy-span,gy+span+1):
            for (px,py,phw,phh) in grid.get((ix,iy),()):
                if abs(cx-px)<hw+phw+GAP and abs(cy-py)<hh+phh+GAP: return False
    return True
def dup_ok(idx,cx,cy):
    return all((cx-px)**2+(cy-py)**2>=MINDUP*MINDUP for (px,py) in byidx.get(idx,()))
deck=[]
def pick():
    global deck
    if not deck: deck=list(range(len(V))); rng.shuffle(deck)
    return deck.pop()
parts=[]
def place_tier(lo,hi,stop,cap=None):
    cnt=0; miss=0
    while miss<stop:
        if cap and cnt>=cap: break
        idx=pick(); v=V[idx]; vbW,vbH=v["vbW"],v["vbH"]
        sc=rng.uniform(lo,hi)/max(vbW,vbH); sw,sh=vbW*sc+TW,vbH*sc+TW
        rot=rng.uniform(-9,9); th=math.radians(rot); c,sn=abs(math.cos(th)),abs(math.sin(th))
        hw=(sw*c+sh*sn)/2; hh=(sw*sn+sh*c)/2
        cx,cy=next_pos(); cx=min(max(cx,hw*0.5),W-hw*0.5); cy=min(max(cy,hh*0.5),Hc-hh*0.5)
        if not fits(cx,cy,hw,hh) or not dup_ok(idx,cx,cy): miss+=1; continue
        g=f'<g transform="translate({cx:.1f} {cy:.1f}) rotate({rot:.1f}) scale({sc:.4f}) translate({-vbW/2:.1f} {-vbH/2:.1f})" stroke-width="{TW/sc:.2f}">'+"".join(f'<path d="{d}"/>' for d in v["paths"])+'</g>'
        parts.append(g); grid.setdefault((int(cx//CELL),int(cy//CELL)),[]).append((cx,cy,hw,hh))
        byidx.setdefault(idx,[]).append((cx,cy)); placed.append(1); cnt+=1; miss=0
    return cnt
place_tier(200,265,700,cap=26)
place_tier(128,182,3500,cap=240)
place_tier(88,126,16000)
place_tier(60,86,22000)
place_tier(42,60,30000)
print("total",len(placed))
svg=f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}"><rect width="{W}" height="{Hc}" fill="{PAPER}"/><g fill="none" stroke="{INK}" stroke-linecap="round" stroke-linejoin="round">'+"".join(parts)+'</g></svg>'
open("clpack.svg","w").write(svg)
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome","--headless","--disable-gpu","--force-device-scale-factor=1","--screenshot=/tmp/doodle_gen/clpack.png",f"--window-size={W},{Hc}","file:///tmp/doodle_gen/clpack.svg"],stderr=subprocess.DEVNULL)
g=np.array(Image.open("clpack.png").convert("L")); H2=g.shape[0]; ink=g<238
print("ink %.1f%%"%(100*ink.mean()),"bands",[round(100*ink[i*H2//6:(i+1)*H2//6].mean(),1) for i in range(6)])
