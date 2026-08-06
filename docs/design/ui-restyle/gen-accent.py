#!/usr/bin/env python3
"""Any accent(s) on both grounds, across all five screens.

    python3 gen-accent.py plum wine

Writes accent-<keys>.html plus one solo page per palette. Same gates as everything else.
"""
import os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
SOLO = os.path.join(HERE, "_acc")

spec = importlib.util.spec_from_file_location("gc", os.path.join(HERE, "gen-chat.py"))
gc = importlib.util.module_from_spec(spec); spec.loader.exec_module(gc)
gm, gd, gp = gc.gm, gc.gd, gc.gp
dE, oklch, ratio = gd.dE, gd.oklch, gd.ratio

SEEDS = {
 "petrol-deep": ("Глубокий петроль", "#0B5E63"),
 "wine":        ("Бордо",            "#8A1F3D"),
 "plum":        ("Слива",            "#5B2D82"),
 "bronze":      ("Бронза",           "#7A4A1C"),
 "ink-navy":    ("Чернильный",       "#243A7A"),
 "aqua":        ("Аква",             "#4FE3E0"),
 "mint-neon":   ("Неон-мята",        "#6FF0B0"),
 "coral":       ("Коралл",           "#FF8A6B"),
 "gold":        ("Золото",           "#F5C24B"),
 "orchid":      ("Орхидея",          "#D79BFF"),
}
GROUND_RU = {"petrol": "Петроль", "graphite": "Графит"}

def tokens_css(p):
    c = p["chat"]
    extra = "".join(f"--{k}:{v};" for k, v in c.items()) + f'--accTx:{p["t"]["accInk"]};'
    return gd.tokens_css(p) + f'.p-{p["id"]}{{{extra}}}\n'

def main():
    keys = [k for k in sys.argv[1:] if k in SEEDS] or ["plum", "wine"]
    gm.ACCENTS = {k: dict(ru=SEEDS[k][0], seed=SEEDS[k][1], note=SEEDS[k][0]) for k in keys}

    P, i = [], 0
    for k in keys:
        for g in ("petrol", "graphite"):
            i += 1
            p = gm.build_palette(g, k, i)
            p["id"] = f"{g}-{k}"
            p["ru"] = f'«{GROUND_RU[g]}» × «{SEEDS[k][0]}»'
            p["seed"] = SEEDS[k][1]
            P.append(p)
    for p in P:
        p["chat"] = gc.chat_tokens(p)

    rows, bad = gd.audit(P)
    for p in P:
        c = p["chat"]
        for lbl, fg, bg, need in (("bubble text", c["ink"], c["bubOut"], 4.5),
                                  ("badge label", c["badgeInk"], c["badge"], 4.5),
                                  ("bubble / wall", c["bubOut"], c["wall"], 1.18)):
            r = ratio(fg, bg)
            if r < need: bad.append(f'{p["id"]}: {lbl} = {r} (need {need})')
        L, C, H = oklch(p["t"]["acc"])
        print(f'  {p["ru"]:<32} {p["seed"]} -> fill {p["t"]["acc"]}  text {p["t"]["accInk"]}  '
              f'okL{L:.2f} okC{C:.3f} okH{H:.0f}')

    def screens(p):
        return (gp.ph(gc.chat_html(p), "Чат") + gp.ph(gc.chats_html(p), "Список чатов")
                + gp.ph(gd.DASH4, "Сводка") + gp.ph(gp.BOTS, "Боты") + gp.ph(gp.SET, "Настройки"))

    css = (gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + gc.CHAT_CSS
           + ".pal,.mast,.close,.fam{max-width:1980px}\n"
           + "".join(tokens_css(p) for p in P))
    names = " и ".join(f'«{SEEDS[k][0]}»' for k in keys)
    o = [f'<title>{names} — оба фона</title>', f'<style>{css}</style>', '<div class="page">']
    o.append(f'''<header class="mast">
 <p class="eb">Refined Modern · {names} · оба фона · все экраны</p>
 <h1>{names}, <em>light and dark.</em></h1>
 <p class="lede">All five screens for each: the chat thread, the chats list, «Сводка», «Боты» and the
 settings form. The ground is untouched; the accent keeps its hue and moves only in lightness.</p>
</header>''')
    for p in P:
        c = p["chat"]
        sw = "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>'
                     for h, l in ((p["seed"],"as picked"), (p["t"]["bg"],"ground"),
                                  (p["t"]["acc"],"accent fill"), (p["t"]["accInk"],"accent text"),
                                  (c["bubOut"],"bubble"), (c["badge"],"unread"),
                                  (p["ownerStatus"],"alarm")))
        o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
  <p class="eb">{p['idx']:02d}</p><h3>{p['ru']}</h3>
  <div class="swr">{sw}</div>{gd.collision(p)}
 </div><div>{gd.cx(rows[p['id']])}</div></div>
 <div class="row">{screens(p)}</div>
</section>''')
    o.append('''<section class="close">
 <h2>Comparing them</h2>
 <p><strong>Read the two grounds of one accent as a pair, not as rivals.</strong> If you ever ship a light
 and a dark theme, that pair is what a user switching between them will experience — the hue has to hold its
 identity across both or the app feels like two products.</p>
 <p><strong>Then compare accents on the same ground.</strong> That is the actual choice: which hue owns the
 Сохранить button, the unread badge and the «Авто» pill.</p>
</section></div>''')
    out = os.path.join(HERE, "accent-" + "-".join(keys) + ".html")
    open(out, "w", encoding="utf-8").write("".join(o))

    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0;font-weight:750;font-size:22px">'
            f'{p["ru"]}</h3></div><div class="row" style="padding:0 26px">{screens(p)}</div></section></div>')
    print(f"\nwrote {out}")
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("All contrast and collision gates PASS.")

main()
