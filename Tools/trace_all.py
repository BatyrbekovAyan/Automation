import glob, os, re, json, subprocess
import numpy as np
from PIL import Image
os.makedirs("vec",exist_ok=True)
vectors=[]
for fp in sorted(glob.glob("cutouts/d*.png")):
    a=np.array(Image.open(fp).convert("L"))
    ys,xs=np.where(a>40)
    if len(xs)==0: continue
    a=a[ys.min():ys.max()+1, xs.min():xs.max()+1]
    base=os.path.splitext(os.path.basename(fp))[0]
    Image.fromarray((255-a).astype(np.uint8)).save(f"vec/{base}.pgm")
    subprocess.run(["mkbitmap","-n","-s","4","-t","0.42",f"vec/{base}.pgm","-o",f"vec/{base}.pbm"],stderr=subprocess.DEVNULL)
    subprocess.run(["potrace","--svg","--turdsize","2","--alphamax","1.3",f"vec/{base}.pbm","-o",f"vec/{base}.svg"],stderr=subprocess.DEVNULL)
    svg=open(f"vec/{base}.svg").read()
    m=re.search(r'viewBox="([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+)"',svg)
    vbW,vbH=float(m.group(3)),float(m.group(4))
    gm=re.search(r'(<g transform=.*?</g>)',svg,re.S)
    inner=gm.group(1) if gm else ""
    inner=re.sub(r'fill="#[0-9a-fA-F]+"','',inner)  # strip fill -> inherit
    if vbW<1 or vbH<1 or not inner: continue
    vectors.append({"vbW":vbW,"vbH":vbH,"inner":inner})
json.dump(vectors,open("cutvec.json","w"))
print("traced vectors:",len(vectors))
