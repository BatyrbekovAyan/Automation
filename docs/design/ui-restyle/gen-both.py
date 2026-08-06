#!/usr/bin/env python3
"""All ten accents on BOTH grounds — 20 palettes.

Deliberately NO accent-separation repair here. Forcing ten accents to sit ΔE 15 apart on
one ground would distort them into colours the owner never picked, which defeats the point
of showing all ten. Instead each accent keeps its HUE and moves only in lightness to clear
that ground's contrast floor, and any pair that lands in the same place is REPORTED and
labelled rather than repaired.

Contrast and collision gates still bind: a button with no edge is a defect, not a finding.
"""
import os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "accent-both.html")
SOLO = os.path.join(HERE, "_both")

spec = importlib.util.spec_from_file_location("gs", os.path.join(HERE, "gen-shine.py"))
# gen-shine runs on import; let it, then borrow its pieces
try:
    gs = importlib.util.module_from_spec(spec); spec.loader.exec_module(gs)
except SystemExit:
    pass
gc, gm, gd, gp = gs.gc, gs.gm, gs.gd, gs.gp
dE, oklch, ratio, tune = gd.dE, gd.oklch, gd.ratio, gd.tune

ALL = gs.DEEP + gs.SHINY          # 5 deep + 5 shiny, in the owner's order

def make(ground):
    gm.ACCENTS = {k: dict(ru=ru, seed=seed, note=note) for k, ru, seed, note in ALL}
    P = []
    for i, (k, ru, seed, note) in enumerate(ALL, 1):
        p = gm.build_palette(ground, k, i)
        p["id"] = f"{ground}-{k}"
        p["ru"] = f"«{ru}»"
        p["seed"] = seed
        p["blurb"] = note.capitalize() + "."
        p["chat"] = gc.chat_tokens(p)
        P.append(p)
    return P

def converged(P):
    """Which of these ten landed in the same place on this ground."""
    out = []
    for i in range(len(P)):
        for j in range(i+1, len(P)):
            d = dE(P[i]["t"]["acc"], P[j]["t"]["acc"])
            if d < 15:
                out.append((P[i], P[j], d))
                P[j]["dupe"] = f'На этом фоне почти неотличим от {P[i]["ru"]} — ΔE {d:.1f}'
    return out

HEAD = {
 "petrol": ("«Петроль» — все десять акцентов",
   "Ice-grey paper, untouched. A fill needs 3:1 here, so every shiny accent is pushed down in lightness "
   "until it clears — which is why the five bright ones arrive looking like the five deep ones. Hue is "
   "preserved throughout; only lightness moves."),
 "graphite": ("«Графит» — все десять акцентов",
   "Blue-black ground, untouched. The maths inverts: every deep accent is lifted until it separates from "
   "the substrate. The five deep colours arrive bright, and the five bright ones stay roughly themselves."),
}

def main():
    LIGHT, DARK = make("petrol"), make("graphite")
    cl, cd = converged(LIGHT), converged(DARK)
    P = LIGHT + DARK
    for i, p in enumerate(P, 1): p["idx"] = i

    rows, bad = gd.audit(P)
    for p in P:
        c = p["chat"]
        for lbl, fg, bg, need in (("bubble text", c["ink"], c["bubOut"], 4.5),
                                  ("badge label", c["badgeInk"], c["badge"], 4.5),
                                  ("bubble / wall", c["bubOut"], c["wall"], 1.18)):
            r = ratio(fg, bg)
            if r < need: bad.append(f'{p["id"]}: {lbl} = {r} (need {need})')

    css = gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + gc.CHAT_CSS + "".join(gs.tokens_css(p) for p in P)
    o = ['<title>Все акценты на обоих фонах</title>', f'<style>{css}</style>', '<div class="page">']
    o.append(f'''<header class="mast">
 <p class="eb">Refined Modern · 10 акцентов × 2 фона · 20 вариантов</p>
 <h1>All ten, <em>on both grounds.</em></h1>
 <p class="lede">Each accent keeps its <strong>hue</strong> and moves only in lightness to clear the
 contrast floor on that ground. Nothing has been forced apart — pushing ten accents to sit ΔE 15 from each
 other would turn them into colours you never picked.</p>
 <p class="lede"><strong>So some of them arrive at the same place, and those are labelled.</strong> On the
 light paper {len(cl)} pairs converge; on the dark ground {len(cd)}. That is not a bug in the palette —
 it is what happens when you normalise lightness and only hue is left to tell colours apart.</p>
</header>''')
    for grp, key, conv in ((LIGHT, "petrol", cl), (DARK, "graphite", cd)):
        ttl, dsc = HEAD[key]
        o.append(f'<section class="fam"><h2>{ttl}</h2><p>{dsc}</p></section>')
        for p in grp:
            c = p["chat"]
            sw = "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>'
                         for h, l in ((p["seed"],"as picked"), (p["t"]["acc"],"adapted fill"),
                                      (p["t"]["accInk"],"accent text"), (c["bubOut"],"bubble"),
                                      (c["badge"],"unread")))
            dupe = (f'<p class="rk"><b>Duplicate</b><br>{p["dupe"]}</p>' if p.get("dupe") else "")
            o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
  <p class="eb">{p['idx']:02d}</p><h3>{p['ru']}</h3>
  <p class="blurb">{p['blurb']}</p>{dupe}
  <div class="swr">{sw}</div>
 </div><div>{gd.cx(rows[p['id']])}</div></div>
 <div class="row">{gp.ph(gc.chat_html(p),"Чат")}{gp.ph(gc.chats_html(p),"Список чатов")}
 {gp.ph(gp.SET,"Настройки")}</div>
</section>''')
    o.append('''<section class="close">
 <h2>What the duplicates tell you</h2>
 <p>The ten colours are really <strong>six hue families</strong>: teal, red, violet, gold, blue and mint.
 «Глубокий петроль» and «Аква» are the same hue at different lightness; so are «Слива» and «Орхидея»,
 and «Бронза» and «Золото». Put them on one ground, normalise lightness to the contrast floor, and each
 pair collapses into one colour.</p>
 <p><strong>Which means the real choice is the hue, and the ground decides the mood.</strong> Pick the
 family you want; the ground you already chose will make it deep or make it glow.</p>
</section></div>''')
    open(OUT, "w", encoding="utf-8").write("".join(o))

    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0;font-weight:750;font-size:22px">'
            f'{p["ru"]}</h3></div><div class="row" style="padding:0 26px">'
            f'{gp.ph(gc.chat_html(p),"Чат")}{gp.ph(gc.chats_html(p),"Список чатов")}'
            f'{gp.ph(gp.SET,"Настройки")}</div></section></div>')
    for grp, key in ((LIGHT, "petrol"), (DARK, "graphite")):
        cells = "".join(
            f'<figure class="pw"><div class="phone p-{p["id"]}"><div class="scr">{gc.chats_html(p)}</div>'
            f'</div><figcaption class="pc">{p["ru"]}{" · дубль" if p.get("dupe") else ""}</figcaption>'
            f'</figure>' for p in grp)
        open(os.path.join(SOLO, f"sheet-{key}.html"), "w", encoding="utf-8").write(
            f'<title>sheet</title><style>{css}.grid{{display:grid;grid-template-columns:repeat(5,auto);'
            f'gap:24px 18px;justify-content:center;padding:24px}}</style>'
            f'<div class="page"><div class="grid">{cells}</div></div>')

    print(f"wrote {OUT}\npalettes: {len(P)}\n")
    for grp, key in ((LIGHT, "petrol"), (DARK, "graphite")):
        print(f"  -- {key}")
        for p in grp:
            L, C, H = oklch(p["t"]["acc"])
            tag = "  <-- dup" if p.get("dupe") else ""
            print(f'     {p["ru"]:<20} {p["seed"]} -> {p["t"]["acc"]}  okL{L:.2f} okH{H:>5.0f}{tag}')
    for name, conv in (("petrol", cl), ("graphite", cd)):
        if conv:
            print(f"\n  CONVERGED on {name}:")
            for a, b, d in conv:
                print(f'     {a["ru"]} ~ {b["ru"]}  dE {d:.1f}')
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("\nAll contrast and collision gates PASS.")

main()
