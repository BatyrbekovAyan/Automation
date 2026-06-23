import glob, json, math
import numpy as np
from PIL import Image
from skimage.morphology import skeletonize
from scipy.interpolate import splprep, splev

cuts=[]
for fp in sorted(glob.glob("cutouts/d*.png")):
    a=np.array(Image.open(fp).convert("L")); ys,xs=np.where(a>40)
    if len(xs)==0: continue
    cuts.append(a[ys.min():ys.max()+1, xs.min():xs.max()+1])
def sig(a):
    s=np.array(Image.fromarray(a).resize((28,28),Image.BILINEAR)).astype(float); return (s>55).astype(float)
sigs=[sig(a) for a in cuts]; keep=[]; ks=[]
for i,s in enumerate(sigs):
    if not any((s*k).sum()/max(1,((s+k)>0).sum())>0.60 for k in ks): keep.append(i); ks.append(s)
print("dedup kept",len(keep))
UP=4; SF=1.2
def nbrs(p,S):
    y,x=p; return [(y+dy,x+dx) for dy in(-1,0,1) for dx in(-1,0,1) if (dy or dx) and (y+dy,x+dx) in S]
def trace(skel):
    S=set(map(tuple,np.argwhere(skel))); deg={p:len(nbrs(p,S)) for p in S}; used=set(); polys=[]
    def walk(a,b):
        path=[a,b]; used.add(frozenset((a,b))); prev,cur=a,b
        while deg.get(cur,0)==2:
            nx=[q for q in nbrs(cur,S) if q!=prev and frozenset((cur,q)) not in used]
            if not nx: break
            q=nx[0]; used.add(frozenset((cur,q))); path.append(q); prev,cur=cur,q
        return path
    for n in [p for p in S if deg[p]!=2]:
        for nb in nbrs(n,S):
            if frozenset((n,nb)) not in used: polys.append(walk(n,nb))
    for p in S:
        for nb in nbrs(p,S):
            if frozenset((p,nb)) not in used: polys.append(walk(p,nb))
    return polys
def smooth(poly):
    ys=np.array([p[0] for p in poly],float); xs=np.array([p[1] for p in poly],float); n=len(poly)
    if n<5: return "M"+" L".join(f"{x/UP:.1f} {y/UP:.1f}" for y,x in zip(ys,xs))
    closed=math.hypot(xs[0]-xs[-1],ys[0]-ys[-1])<UP*1.5 and n>6
    try:
        tck,u=splprep([xs,ys],s=n*SF,k=3,per=1 if closed else 0)
        uu=np.linspace(0,1,max(16,n//2)); xo,yo=splev(uu,tck)
    except Exception: xo,yo=xs,ys
    return "M"+" L".join(f"{x/UP:.1f} {y/UP:.1f}" for x,y in zip(xo,yo))+(" Z" if closed else "")
vectors=[]
for i in keep:
    a=cuts[i]; h0,w0=a.shape
    big=np.array(Image.fromarray(a).resize((w0*UP,h0*UP),Image.BILINEAR))>90
    polys=trace(skeletonize(big)); paths=[]
    for poly in polys:
        if len(poly)<2: continue
        length=sum(math.hypot(poly[k+1][0]-poly[k][0],poly[k+1][1]-poly[k][1]) for k in range(len(poly)-1))
        if length<UP*3: continue
        paths.append(smooth(poly))
    if paths: vectors.append({"vbW":float(w0),"vbH":float(h0),"paths":paths})
json.dump(vectors,open("cutvec_cl.json","w")); print("smooth centerline vectors:",len(vectors))
