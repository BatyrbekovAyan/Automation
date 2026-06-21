import sys, random, subprocess
sys.path.insert(0,'/tmp/doodle_gen')
import numpy as np
from scipy import ndimage
from PIL import Image, ImageChops
import gen_doodles as G

CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
SRC="/Users/ayan/Projects/Automation/Assets/Images/Chat/Gemini_Generated_Image_s6fk0ts6fk0ts6fk.png"
W,Hc=1596,2688
SEAM=1344

# 1. recolor reference to our palette
src=Image.open(SRC).convert("RGB").resize((W,Hc),Image.LANCZOS)
P1=np.array([0xF5,0xF2,0xEA]); I1=np.array([0xE5,0xDA,0xC6])
arr=np.asarray(src).astype(float)
lum=(arr[:,:,0]*299+arr[:,:,1]*587+arr[:,:,2]*114)/1000
t=np.clip((244.0-lum)/(244.0-150.0),0,1)[:,:,None]
base=(P1+t*(I1-P1)).astype(np.uint8)
baseimg=Image.fromarray(base)

# 2. components
g=np.array(baseimg.convert("L")); ink=g<236
dil=ndimage.binary_dilation(ink,iterations=2); lbl,n=ndimage.label(dil)
cent=ndimage.center_of_mass(np.ones_like(lbl),lbl,range(1,n+1))  # list of (y,x)

# 3. brand ellipses (top half) + bottom rule
yy,xx=np.ogrid[0:Hc,0:W]
brand=np.zeros((Hc,W),bool)
for cx,cy,rx,ry in [(1112,945,98,110),(648,1010,86,118),(1120,1345,188,248)]:
    brand|=(((xx-cx)/rx)**2+((yy-cy)/ry)**2)<=1
brand_labels=set(int(v) for v in np.unique(lbl[brand&(lbl>0)]))
erase_labels=set(brand_labels)
for i,(cy,cx) in enumerate(cent,start=1):
    if cy>SEAM: erase_labels.add(i)
erase_labels.discard(0)
erased=np.isin(lbl,list(erase_labels))
erased_fat=ndimage.binary_dilation(erased,iterations=3)

# 4. erase -> paper
b=base.copy(); b[erased_fat]=P1; baseimg=Image.fromarray(b)

# 5. kept ink (top, non-brand) -> keep-out for refill
kept_ink = ink & ~erased_fat
keepout = ndimage.binary_dilation(kept_ink, iterations=5)
# fill region: bottom half OR inside brand ellipses
fill=np.zeros((Hc,W),bool); fill[SEAM:,:]=True; fill|=brand
print("erased comps:",len(erase_labels),"| kept ink px:",int(kept_ink.sum()))

# 6. collision-pack generated doodles into fill, avoiding keepout + each other
rng=random.Random(7)
EFAC,GAP=0.32,1.0; CELL=180.0; grid={}
names=[nm for nm in G.MOTIFS.keys() if nm not in {"penguin","frog","bird"}]
deck=[]
def draw_name():
    global deck
    if not deck: deck=names[:]; rng.shuffle(deck)
    return deck.pop()
placed=[]
def fits(cx,cy,he):
    span=int((he+GAP+200)//CELL)+1; gx,gy=int(cx//CELL),int(cy//CELL)
    for ix in range(gx-span,gx+span+1):
        for iy in range(gy-span,gy+span+1):
            for (px,py,phe) in grid.get((ix,iy),()):
                lim=he+phe+GAP
                if abs(cx-px)<lim and abs(cy-py)<lim: return False
    return True
def keepout_clear(cx,cy,he):
    x0,x1=max(0,int(cx-he)),min(W,int(cx+he)); y0,y1=max(0,int(cy-he)),min(Hc,int(cy+he))
    if x0>=x1 or y0>=y1: return False
    return not keepout[y0:y1,x0:x1].any()
svg=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hc}" viewBox="0 0 {W} {Hc}">',
     '<g fill="none" stroke="%s" stroke-linecap="round" stroke-linejoin="round">'%G.INK]
def place_tier(lo,hi,stop):
    miss=0
    while miss<stop:
        nm=draw_name(); elems,szmul=G.MOTIFS[nm]; size=rng.uniform(lo,hi)*szmul; he=size*EFAC
        cx=rng.uniform(he*0.5,W-he*0.5); cy=rng.uniform(SEAM-40,Hc-he*0.5)
        # allow brand-hole placement too (top): 25% chance try a brand-zone point
        if rng.random()<0.22:
            cx=rng.uniform(40,W-40); cy=rng.uniform(40,Hc-40)
        iy,ix=int(cy),int(cx)
        if not (0<=iy<Hc and 0<=ix<W) or not fill[iy,ix]:
            miss+=1; continue
        if not keepout_clear(cx,cy,he) or not fits(cx,cy,he):
            miss+=1; continue
        f=max(0.0,min(1.0,(size-30)/(290-30))); stroke=2.6+f*(4.1-2.6)
        svg.append(G.instance(elems,cx,cy,size,rng.uniform(-20,20),stroke_final=stroke,text=nm.startswith("txt_")))
        grid.setdefault((int(cx//CELL),int(cy//CELL)),[]).append((cx,cy,he)); placed.append(1); miss=0
place_tier(225,285,300); place_tier(135,178,1400); place_tier(85,126,6000); place_tier(52,82,8500); place_tier(26,50,12000)
svg.append('</g></svg>'); open("/tmp/doodle_gen/genfill.svg","w").write("".join(svg))
print("generated fill doodles:",len(placed))

# 7. render generated fill, darken-composite onto base (paper bg -> only doodles add)
subprocess.run([CHROME,"--headless","--disable-gpu","--force-device-scale-factor=1",
  "--screenshot=/tmp/doodle_gen/genfill.png",f"--window-size={W},{Hc}",
  "file:///tmp/doodle_gen/genfill.svg"],stderr=subprocess.DEVNULL)
gen=Image.open("/tmp/doodle_gen/genfill.png").convert("RGB")
# genfill has transparent->white bg? no bg rect, chrome renders white. Make white->paper, then darken.
gp=np.array(gen)
white=(gp[:,:,0]>250)&(gp[:,:,1]>250)&(gp[:,:,2]>250)
gp[white]=P1; gen=Image.fromarray(gp)
final=ImageChops.darker(baseimg,gen)
final.save("/tmp/doodle_gen/hybrid.png")
print("saved hybrid.png")
