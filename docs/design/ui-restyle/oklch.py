import math, importlib.util
spec=importlib.util.spec_from_file_location("gp","/Users/ayan/Projects/Automation/docs/design/ui-restyle/gen-palettes.py")
gp=importlib.util.module_from_spec(spec)
try: spec.loader.exec_module(gp)
except SystemExit: pass
def _sl(c):
    c/=255.0
    return c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4
def oklch(hx):
    r,g,b=(_sl(v) for v in gp._rgb(hx))
    l=0.4122214708*r+0.5363325363*g+0.0514459929*b
    m=0.2119034982*r+0.6806995451*g+0.1073969566*b
    s=0.0883024619*r+0.2817188376*g+0.6299787005*b
    l_,m_,s_=(math.copysign(abs(v)**(1/3),v) for v in (l,m,s))
    L=0.2104542553*l_+0.7936177850*m_-0.0040720468*s_
    a=1.9779984951*l_-2.4285922050*m_+0.4505937099*s_
    bb=0.0259040371*l_+0.7827717662*m_-0.8086757660*s_
    return L, math.hypot(a,bb), (math.degrees(math.atan2(bb,a))%360)
def oklab(hx):
    L,C,H=oklch(hx); t=math.radians(H); return L, C*math.cos(t), C*math.sin(t)
def dE(a,b):
    la,aa,ba=oklab(a); lb,ab,bb=oklab(b)
    return math.hypot(la-lb, aa-ab, ba-bb)*100
def find(tL,tC,tH):
    best=None
    for r in range(0,256,1):
        for g in range(0,256,3):
            for b in range(0,256,3):
                hx="#%02X%02X%02X"%(r,g,b)
                L,C,H=oklch(hx)
                if abs(L-tL)>.02 or abs(C-tC)>.012: continue
                d=min(abs(H-tH),360-abs(H-tH))
                if d>3: continue
                sc=abs(L-tL)*4+abs(C-tC)*8+d/60
                if best is None or sc<best[0]: best=(sc,hx,L,C,H)
    return best
