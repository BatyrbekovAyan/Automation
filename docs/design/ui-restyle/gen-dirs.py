#!/usr/bin/env python3
"""«Графит» (cool dark) and «Песок» (warm light) variants.

Difference from the orange pass: these brands collide with CHANNEL IDENTITY, not just
with a status. A blue brand competes with Telegram blue; a green brand competes with
WhatsApp green AND the "bot running" switch AND order_collected. So the mock renders
FOUR statuses including the green one, and the gates measure the brand against every
fixed colour it could be confused with.

Reads dirs.json from this directory. Exits non-zero on any failure — a palette that
fails contrast, salience, a collision gate, or the accent-cluster check never ships.
"""

import os, sys, json, math, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "graphite-sand-palettes.html")
SOLO = os.environ.get("SOLO_DIR", os.path.join(HERE, "_solo"))

spec = importlib.util.spec_from_file_location("gp", os.path.join(HERE, "gen-palettes.py"))
gp = importlib.util.module_from_spec(spec)
try: spec.loader.exec_module(gp)
except SystemExit: pass
ratio, tune = gp.ratio, gp.tune

# ---------------------------------------------------------------- colour maths
def _sl(c):
    c /= 255.0
    return c/12.92 if c <= 0.04045 else ((c+0.055)/1.055)**2.4

def oklch(hx):
    r, g, b = (_sl(v) for v in gp._rgb(hx))
    l = 0.4122214708*r + 0.5363325363*g + 0.0514459929*b
    m = 0.2119034982*r + 0.6806995451*g + 0.1073969566*b
    s = 0.0883024619*r + 0.2817188376*g + 0.6299787005*b
    l_, m_, s_ = (math.copysign(abs(v)**(1/3), v) for v in (l, m, s))
    L = 0.2104542553*l_ + 0.7936177850*m_ - 0.0040720468*s_
    a = 1.9779984951*l_ - 2.4285922050*m_ + 0.4505937099*s_
    bb = 0.0259040371*l_ + 0.7827717662*m_ - 0.8086757660*s_
    return L, math.hypot(a, bb), (math.degrees(math.atan2(bb, a)) % 360)

def _lab(hx):
    L, C, H = oklch(hx); t = math.radians(H)
    return L, C*math.cos(t), C*math.sin(t)

def dE(a, b):
    la, aa, ba = _lab(a); lb, ab, bb = _lab(b)
    return math.hypot(la-lb, aa-ab, ba-bb) * 100

# ---------------------------------------------------------------- fixed colours
WA, TG = "#25D366", "#2AABEE"          # channel identity — may never move
CALM_C = max(oklch(h)[1] for h in ("#16A75C", "#3B82F6", "#9B5DE0"))

CHECKS = [
    ("accent FILL / surface",      "acc",     "sf",     3.0),
    ("body ink / surface",         "ink",     "sf",     4.5),
    ("secondary ink / surface",    "ink2",    "sf",     4.5),
    ("tertiary ink / surface",     "ink3",    "sf",     4.5),
    ("accent TEXT / surface",      "accInk",  "sf",     4.5),
    ("button label / fill",        "saveInk", "saveBg", 4.5),
    ("chip label / chip fill",     "chipInk", "chipBg", 4.5),
    ("status ink / status bg",     "goodInk", "goodBg", 4.5),
    ("input border / surface",     "bdI",     "sf",     3.0),
    ("body ink / ground",          "ink",     "bg",     4.5),
    # the OFF state of the activation switch answers "is my bot running?".
    # proposals shipped it at 1.55:1 — nearly invisible on a bright screen.
    ("switch OFF track / surface", "swOff",   "sf",     3.0),
]

# Framework and CIS-brand values. Last round three designers shipped stock hexes and
# sold them as considered choices; this round was worse. Now it is a hard gate.
STOCK = {
 "Tailwind blue-500":"#3B82F6","Tailwind indigo-500":"#6366F1","Tailwind violet-300":"#C3BAFF",
 "Tailwind violet-500":"#8B5CF6","Tailwind fuchsia-300":"#F0ABFC","Tailwind rose-500":"#F43F5E",
 "Tailwind orange-500":"#F97316","Tailwind amber-500":"#F59E0B","Tailwind emerald-500":"#10B981",
 "Tailwind green-800":"#166534","Tailwind green-900":"#14532D","Tailwind teal-900":"#134E4A",
 "Tailwind cyan-900":"#164E63","Tailwind yellow-800":"#854D0E","Tailwind sky-500":"#0EA5E9",
 "Bootstrap primary":"#0D6EFD","Bootstrap success":"#198754","Bootstrap danger":"#DC3545",
 "Bootstrap orange":"#FD7E14","Bootstrap purple":"#6F42C1",
 "Material blue-500":"#2196F3","Material green-500":"#4CAF50","Material green-800":"#2E7D32",
 "Material orange-500":"#FF9800","Material deep-orange":"#FF5722","Material pink-500":"#E91E63",
 "Material indigo-500":"#3F51B5","Material red-500":"#F44336","Material teal-500":"#009688",
 "Kaspi red":"#F14635","Beeline yellow":"#FFCC00","Ozon blue":"#005BFF",
 "Wildberries magenta":"#CB11AB","Yandex red":"#FC3F1D",
 "WhatsApp green":"#25D366","Telegram blue":"#2AABEE",
}

def is_stock(hx, skip=()):
    for name, s in STOCK.items():
        if name in skip: continue
        if dE(hx, s) < 4: return name
    return None

def nudge(base, ok):
    """Nearest colour to `base` that satisfies predicate `ok`. Searches sRGB coarsely
    then refines, so a repair moves the value as little as the constraints allow."""
    if ok(base): return base
    best = None
    for step, span in ((12, 96), (4, 28)):
        anchor = best[1] if best else base
        r0, g0, b0 = gp._rgb(anchor)
        for dr in range(-span, span+1, step):
            for dg in range(-span, span+1, step):
                for db in range(-span, span+1, step):
                    r, g, b = r0+dr, g0+dg, b0+db
                    if not (0 <= r < 256 and 0 <= g < 256 and 0 <= b < 256): continue
                    cand = "#%02X%02X%02X" % (r, g, b)
                    if not ok(cand): continue
                    d = dE(base, cand)
                    if best is None or d < best[0]: best = (d, cand)
    return best[1] if best else base

def derive(P):
    for p in P:
        t = p["t"]
        for k in ("ink", "ink2", "ink3", "accInk"):
            t[k] = tune(t[k], t["sf"], 4.5)
        t["goodInk"] = tune(t["goodInk"], t["goodBg"], 4.5)
        t["saveInk"] = tune(t["saveInk"], t["saveBg"], 4.5)
        t["chipInk"] = tune(t["chipInk"], t["chipBg"], 4.5)
        t["bdI"]     = tune(t["bd"],      t["sf"],     3.0)
        # the OFF switch track is a control state, not decoration — make it visible
        t["swOff"]   = tune(t["swOff"],   t["sf"],     3.0)

        sf = t["sf"]
        # accent fill: off the stock palettes, clear of body text, still an affordance
        t["acc"] = nudge(t["acc"], lambda c: (not is_stock(c)
                                              and ratio(c, sf) >= 3.0
                                              and dE(c, t["ink2"]) >= 15
                                              and dE(c, WA) >= 15 and dE(c, TG) >= 15))
        t["saveBg"] = t["acc"]
        t["accInk"] = nudge(t["accInk"], lambda c: (not is_stock(c)
                                                    and ratio(c, sf) >= 4.5
                                                    and dE(c, t["ink2"]) >= 15))
        t["saveInk"] = tune(t["saveInk"], t["saveBg"], 4.5)
        # statuses: off the frameworks, still separated from the brand and each other
        p["ownerStatus"] = nudge(p["ownerStatus"], lambda c: (
            not is_stock(c, skip=("WhatsApp green","Telegram blue"))
            and dE(c, t["acc"]) >= 15 and dE(c, p["destructive"]) >= 12))
        p["destructive"] = nudge(p["destructive"], lambda c: (
            not is_stock(c, skip=("WhatsApp green","Telegram blue"))
            and dE(c, t["acc"]) >= 15 and dE(c, p["ownerStatus"]) >= 12))
        p["orderCollected"] = nudge(p["orderCollected"], lambda c: (
            not is_stock(c, skip=("WhatsApp green","Telegram blue"))
            and dE(c, t["acc"]) >= 15 and dE(c, WA) >= 15 and dE(c, t["swOn"]) >= 15))
        p["inDialog"] = nudge(p["inDialog"], lambda c: (
            not is_stock(c, skip=("WhatsApp green","Telegram blue"))
            and dE(c, t["acc"]) >= 15 and dE(c, TG) >= 15
            and dE(c, p["orderCollected"]) >= 15))

def gates(p):
    """Every way this brand could be mistaken for something that already has meaning."""
    f = []
    t = p["t"]; acc = t["acc"]; alarm = p["ownerStatus"]
    aL, aC, _ = oklch(alarm); sL, _, _ = oklch(t["sf"])

    if sL > 0.6 and aC < CALM_C * 0.92:
        f.append(f"alarm {alarm} recedes on light ground (okC {aC:.3f} < {CALM_C:.3f}) — reads disabled")
    if sL <= 0.6 and aC < CALM_C * 0.92 and aL < 0.75:
        f.append(f"alarm {alarm} recedes on dark ground (okC {aC:.3f}, okL {aL:.2f})")

    for label, other, floor in (
        ("alarm",            alarm,               15),
        ("destructive",      p["destructive"],    15),
        ("in_dialog",        p["inDialog"],       15),
        ("order_collected",  p["orderCollected"], 15),
        ("switch green",     t["swOn"],           15),
        ("WhatsApp green",   WA,                  15),
        ("Telegram blue",    TG,                  15),
    ):
        d = dE(acc, other)
        if d < floor:
            f.append(f"brand {acc} vs {label} {other}: dE {d:.1f} (need {floor})")

    d = dE(alarm, p["destructive"])
    if d < 12:
        f.append(f'alarm vs destructive dE {d:.1f} (need 12) — "needs you" and "delete" are one colour')
    if dE(p["inDialog"], p["orderCollected"]) < 15:
        f.append("in_dialog vs order_collected under dE 15")

    # the brand must not be perceptually the same colour as the body text beside it
    d = dE(acc, t["ink2"])
    if d < 15:
        f.append(f'brand {acc} vs secondary ink {t["ink2"]}: dE {d:.1f} (need 15) — '
                 f'the accent reads as body text')

    # no framework defaults, no CIS brand colours
    for role in ("acc", "accInk"):
        for name, hx in STOCK.items():
            if dE(t[role], hx) < 4:
                f.append(f'{role} {t[role]} is dE {dE(t[role],hx):.1f} from {name} {hx} — stock hex')
    for role in ("ownerStatus", "inDialog", "orderCollected", "destructive"):
        for name, hx in STOCK.items():
            if name in ("WhatsApp green", "Telegram blue"):
                continue          # statuses are allowed to be near-green/near-blue by nature
            if dE(p[role], hx) < 4:
                f.append(f'{role} {p[role]} is dE {dE(p[role],hx):.1f} from {name} {hx} — stock hex')
    return f

def audit(P):
    bad, rows = [], {}
    for p in P:
        t, res = p["t"], []
        for label, fg, bg, need in CHECKS:
            r = ratio(t[fg], t[bg]); ok = r >= need
            if not ok: bad.append(f'{p["en"]}: {label} = {r} (need {need}) [{t[fg]} on {t[bg]}]')
            res.append((label, r, need, ok))
        for m in gates(p): bad.append(f'{p["en"]}: {m}')
        rows[p["id"]] = res
    return rows, bad

def separate_accents(P):
    """Repairing one constraint can drag two accents together. Push later palettes
    apart from earlier ones while keeping every constraint they already satisfy."""
    for j in range(1, len(P)):
        pj = P[j]; t = pj["t"]; sf = t["sf"]
        earlier = [P[i]["t"]["acc"] for i in range(j)]
        if all(dE(t["acc"], e) >= 15 for e in earlier):
            continue
        t["acc"] = nudge(t["acc"], lambda c: (
            not is_stock(c) and ratio(c, sf) >= 3.0
            and dE(c, t["ink2"]) >= 15 and dE(c, WA) >= 15 and dE(c, TG) >= 15
            and all(dE(c, e) >= 15 for e in earlier)))
        t["saveBg"] = t["acc"]
        t["saveInk"] = tune(t["saveInk"], t["saveBg"], 4.5)
        # statuses were tuned against the old accent — re-check the ones that matter
        for role, extra in (("ownerStatus", lambda c: dE(c, pj["destructive"]) >= 12),
                            ("destructive", lambda c: dE(c, pj["ownerStatus"]) >= 12),
                            ("orderCollected", lambda c: dE(c, WA) >= 15 and dE(c, t["swOn"]) >= 15),
                            ("inDialog", lambda c: dE(c, TG) >= 15 and dE(c, pj["orderCollected"]) >= 15)):
            pj[role] = nudge(pj[role], lambda c: (
                not is_stock(c, skip=("WhatsApp green","Telegram blue"))
                and dE(c, t["acc"]) >= 15 and extra(c)))

def cluster_check(P):
    """Last round nine palettes were really three accents. Refuse to ship duplicates."""
    out = []
    for i in range(len(P)):
        for j in range(i+1, len(P)):
            d = dE(P[i]["t"]["acc"], P[j]["t"]["acc"])
            if d < 15:
                out.append(f'{P[i]["en"]} ~ {P[j]["en"]}: accents only dE {d:.1f} apart')
    return out

# ---------------------------------------------------------------- css / markup
EXTRA_CSS = r"""
.coll{margin-top:14px;padding:11px 13px;border-radius:9px;background:var(--g2);max-width:62ch}
.coll .h{font-family:var(--mono);font-size:10px;letter-spacing:.12em;text-transform:uppercase;
color:var(--k3);margin:0 0 7px}
.coll p.sv{margin:0 0 9px;font-size:13.5px;color:var(--k2)}
.coll .pair{display:flex;gap:14px;flex-wrap:wrap}
.coll .one{display:flex;align-items:center;gap:7px;font-size:12px;color:var(--k2)}
.coll .one i{width:22px;height:22px;border-radius:6px;display:block;flex-shrink:0;
border:1px solid rgba(128,128,128,.3)}
.fmly{display:inline-block;font-family:var(--mono);font-size:10px;letter-spacing:.1em;
text-transform:uppercase;color:var(--k3);border:1px solid var(--rl);border-radius:999px;
padding:3px 9px;margin-left:8px;vertical-align:middle}
"""

def tokens_css(p):
    t, sh = p["t"], p["t"]["sh"]
    if p["fam"] == "dark":
        e1 = "0 1px 2px rgba(0,0,0,.5)"
        e2 = "0 1px 2px rgba(0,0,0,.55),0 10px 24px -10px rgba(0,0,0,.6)"
    else:
        e1 = f"0 1px 2px rgba({sh},.055)"
        e2 = (f"0 1px 2px rgba({sh},.055),0 4px 8px -2px rgba({sh},.05),"
              f"0 16px 28px -12px rgba({sh},.095)")
    m = {"--bgS":t["bg"],"--sf":t["sf"],"--hl":t["hl"],"--bd":t["bd"],"--bdI":t["bdI"],
         "--ink":t["ink"],"--ink2":t["ink2"],"--ink3":t["ink3"],"--acc":t["accInk"],
         "--chipBg":t["chipBg"],"--chipInk":t["chipInk"],"--saveBg":t["saveBg"],
         "--saveInk":t["saveInk"],"--swOn":t["swOn"],"--swOff":t["swOff"],
         "--goodBg":t["goodBg"],"--goodInk":t["goodInk"],"--e1":e1,"--e2":e2}
    body = "".join(f"{k}:{v};" for k, v in m.items())
    return (f".p-{p['id']}{{{body}}}\n"
            f".p-{p['id']} .spk i:nth-child(n+6){{background:{t['acc']}}}\n"
            f".p-{p['id']} .d-owner{{background:{p['ownerStatus']}}}\n"
            f".p-{p['id']} .d-dialog{{background:{p['inDialog']}}}\n"
            f".p-{p['id']} .d-order{{background:{p['orderCollected']}}}\n")

# four statuses — the green one must be visible so a green brand's collision shows
DASH4 = ('<div class="sb"><span>9:41</span><span class="sg"><i></i><i></i><i></i><i></i>'
 '<span class="bt"><i></i></span></span></div>'
 '<div class="ah"><h3>Сводка</h3><span class="hb">+</span></div>'
 '<div class="bd">'
 '<div class="cs"><span class="cp on">7 дней</span><span class="cp">30 дней</span>'
 '<span class="cp">Всё время</span></div>'
 '<div class="hero"><span class="l">Заказов собрано</span><span class="n">24<small>+8</small></span>'
 '<span class="s">за неделю · 62 диалога</span>'
 '<span class="spk"><i style="height:38%"></i><i style="height:52%"></i><i style="height:31%"></i>'
 '<i style="height:64%"></i><i style="height:45%"></i><i style="height:78%"></i>'
 '<i style="height:100%"></i></span></div>'
 '<div class="rws">'
 '<div class="rw"><span class="d d-order"></span><span class="m">Заказ собран</span><span class="c">24</span></div>'
 '<div class="rw"><span class="d d-owner"></span><span class="m">Нужен владелец</span><span class="c">3</span></div>'
 '<div class="rw"><span class="d d-dialog"></span><span class="m">В диалоге</span><span class="c">11</span></div>'
 '<div class="rw"><span class="d d-closed"></span><span class="m">Вопрос закрыт</span><span class="c">18</span></div>'
 '</div>'
 '<div class="bc"><span class="av wa">Ц</span><span class="bm"><b>Цветы Алматы</b>'
 '<em><i class="cd wa"></i>WhatsApp · Работает</em></span><span class="sx"><b></b></span></div>'
 '</div>'
 '<div class="tb"><a><i></i>Чаты</a><a><i></i>Боты</a><a class="on"><i></i>Сводка</a>'
 '<a><i></i>Профиль</a></div>')

def swatches(p):
    t = p["t"]
    picks = [(t["bg"],"ground"),(t["sf"],"surface"),(t["bdI"],"input bd"),
             (t["acc"],"accent fill"),(t["accInk"],"accent text"),(t["ink"],"ink")]
    return "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>' for h,l in picks)

def collision(p):
    acc = p["t"]["acc"]
    items = [("бренд", acc), ("Нужен владелец", p["ownerStatus"]), ("В диалоге", p["inDialog"]),
             ("Заказ собран", p["orderCollected"]), ("Удалить", p["destructive"])]
    chips = "".join(f'<span class="one"><i style="background:{h}"></i>{n}</span>' for n, h in items)
    return (f'<div class="coll"><p class="h">Collision solve</p>'
            f'<p class="sv">{p["collisionSolve"]}</p><div class="pair">{chips}</div>'
            f'<p class="sv" style="margin:9px 0 0;font-family:var(--mono);font-size:11px">'
            f'vs WhatsApp {dE(acc,WA):.0f}ΔE · vs Telegram {dE(acc,TG):.0f}ΔE</p></div>')

def cx(res):
    return ('<table class="cx"><caption>Measured contrast</caption><tbody>'
            + "".join(f'<tr><td>{l}</td><td class="{"ok" if ok else ""}">{r}</td></tr>'
                      for l,r,n,ok in res) + '</tbody></table>')

DIRS = {
 "graphite": ("«Графит» — прохладная тёмная",
   "The cool dark you liked, pushed across its real range. The ground temperature does as much work as the "
   "accent: blue-black, slate and violet-black are three different products before the accent lands. Note "
   "that a blue brand has to fight Telegram blue and the «В диалоге» status — the non-blue accents sidestep "
   "that fight entirely."),
 "sand": ("«Песок» — тёплая светлая",
   "The warm paper you liked. The hard constraint here is green: WhatsApp green, the «бот работает» switch "
   "and «Заказ собран» are all green and none of them may move, so a green brand has to be unmistakably "
   "deeper and duller than all three. The non-green options test whether the feeling you liked came from "
   "the sage or from the paper and the restraint."),
}

def build(P, rows):
    css = gp.PAGE_CSS + gp.PHONE_CSS + EXTRA_CSS + "".join(tokens_css(p) for p in P)
    o = ['<title>Графит и Песок — варианты</title>', f'<style>{css}</style>', '<div class="page">']
    o.append(f'''<header class="mast">
 <p class="eb">Refined Modern · два направления · {len(P)} вариантов</p>
 <h1>Cool dark and warm paper, <em>past the obvious version.</em></h1>
 <p class="lede">Both directions you liked, extended — same Refined Modern material, same three screens.
 The dashboard now shows <strong>four statuses instead of three</strong>, because both of these accents
 collide with a colour that already means something, and a mock that hides the collision is useless.</p>
 <p class="lede"><strong>«Графит» has a blue problem:</strong> Telegram blue and «В диалоге» are both blue.
 <strong>«Песок» has a green problem, and it is worse:</strong> WhatsApp green, the «бот работает» switch and
 «Заказ собран» are three greens that may never move. Every palette states how it separated the brand from
 them, and the distance to both channel colours is measured in ΔE.</p>
</header>''')
    seen = set()
    for p in P:
        if p["direction"] not in seen:
            seen.add(p["direction"]); t, d = DIRS[p["direction"]]
            o.append(f'<section class="fam"><h2>{t}</h2><p>{d}</p></section>')
        row = gp.ph(DASH4,"Сводка") + gp.ph(gp.BOTS,"Боты") + gp.ph(gp.SET,"Настройки")
        o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
   <p class="eb">{p['idx']:02d} — палитра</p>
   <h3>{p['ru']} <span>— {p['en']}</span><span class="fmly">{p['family']}</span></h3>
   <p class="blurb">{p['blurb']}</p>
   <p class="rk"><b>Risk</b><br>{p['risk']}</p>
   <div class="swr">{swatches(p)}</div>{collision(p)}
  </div><div>{cx(rows[p['id']])}</div></div>
 <div class="row">{row}</div></section>''')
    o.append('''<section class="close">
 <h2>How to choose</h2>
 <p><strong>Judge the Настройки screen and the four status dots together.</strong> The dashboard is where a
 brand that competes with a channel colour or a status gives itself away — if you have to look twice to tell
 the brand from «Заказ собран», that palette is out no matter how good the form looks.</p>
 <p><strong>For «Песок», the real question is whether you liked the sage or the paper.</strong> Half the
 options below keep the green and fight for separation; half drop it entirely and keep the warm ground. Both
 preserve the calm — only one of them keeps the brand out of WhatsApp's territory.</p>
 <p>Whichever wins becomes one token block in <code>Assets/Scripts/Theme/</code>, read by every
 <code>[MenuItem]</code> builder.</p>
</section></div>''')
    open(OUT, "w", encoding="utf-8").write("".join(o))
    return css

def solo_and_sheet(P, css):
    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        row = gp.ph(DASH4,"Сводка") + gp.ph(gp.BOTS,"Боты") + gp.ph(gp.SET,"Настройки")
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0 0 8px;font-weight:750;font-size:22px;'
            f'letter-spacing:-.025em">{p["ru"]} — {p["en"]}</h3><div class="swr">{swatches(p)}</div></div>'
            f'<div class="row" style="padding:0 26px">{row}</div></section></div>')
    cells = "".join(
        f'<figure class="pw"><div class="phone p-{p["id"]}"><div class="scr">{DASH4}</div></div>'
        f'<figcaption class="pc">{p["idx"]:02d} · {p["en"]}</figcaption></figure>' for p in P)
    open(os.path.join(SOLO, "contact-sheet.html"), "w", encoding="utf-8").write(
        f'<title>sheet</title><style>{css}.grid{{display:grid;grid-template-columns:repeat(4,auto);'
        f'gap:26px 22px;justify-content:center;padding:26px}}</style>'
        f'<div class="page"><div class="grid">{cells}</div></div>')

def main():
    P = json.load(open(os.path.join(HERE, "dirs.json"), encoding="utf-8"))
    for i, p in enumerate(P, 1): p["idx"] = i
    derive(P)
    separate_accents(P)
    rows, bad = audit(P)
    dupes = cluster_check(P)
    css = build(P, rows)
    solo_and_sheet(P, css)
    print(f"wrote {OUT}\npalettes: {len(P)}   checks: {len(P)*len(CHECKS)}\n")
    for p in P:
        L, C, H = oklch(p["t"]["acc"])
        print(f'  {p["idx"]:>2} [{p["direction"][:4]}] {p["en"]:<24}{p["fam"]:<6}'
              f'{p["t"]["acc"]}  okL{L:.2f} okC{C:.3f} okH{H:>5.0f}   '
              f'WA {dE(p["t"]["acc"],WA):>4.0f}  TG {dE(p["t"]["acc"],TG):>4.0f}')
    if dupes:
        print("\n*** ACCENT CLUSTERS ***")
        for d in dupes: print("  ", d)
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
    if bad or dupes: sys.exit(1)
    print("\nAll contrast checks and collision gates PASS.")

if __name__ == "__main__":
    main()
