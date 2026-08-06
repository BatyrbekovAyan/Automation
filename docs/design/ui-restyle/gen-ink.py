#!/usr/bin/env python3
"""«Чернильный» (ink navy) on both grounds, across all five screens:
Чат · Список чатов · Сводка · Боты · Настройки бота."""
import os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "ink-navy.html")
SOLO = os.path.join(HERE, "_ink")

spec = importlib.util.spec_from_file_location("gc", os.path.join(HERE, "gen-chat.py"))
gc = importlib.util.module_from_spec(spec); spec.loader.exec_module(gc)
gm, gd, gp = gc.gm, gc.gd, gc.gp
dE, oklch, ratio = gd.dE, gd.oklch, gd.ratio

SEED = "#243A7A"

def tokens_css(p):
    c = p["chat"]
    extra = "".join(f"--{k}:{v};" for k, v in c.items()) + f'--accTx:{p["t"]["accInk"]};'
    return gd.tokens_css(p) + f'.p-{p["id"]}{{{extra}}}\n'

NOTE = {
 "petrol": ("«Петроль» × «Чернильный»",
   "Ink navy sits almost unchanged on this paper — the seed already cleared the floor, so what you see is "
   "the colour as picked. It is the deepest, most reserved of the ten: closer to ink than to a brand, which "
   "is either restraint or invisibility depending on your taste."),
 "graphite": ("«Графит» × «Чернильный»",
   "On the dark ground the same hue has to lift to separate from the substrate, so the ink becomes a "
   "genuine blue. It gains energy and loses the inkiness — the one screen where this accent stops being "
   "quiet."),
}

def main():
    gm.ACCENTS = {"ink": dict(ru="Чернильный", seed=SEED, note="ink navy")}
    P = []
    for i, g in enumerate(("petrol", "graphite"), 1):
        p = gm.build_palette(g, "ink", i)
        p["id"] = f"{g}-ink"; p["ru"] = NOTE[g][0]; p["blurb"] = NOTE[g][1]
        p["chat"] = gc.chat_tokens(p)
        P.append(p)

    rows, bad = gd.audit(P)
    for p in P:
        c = p["chat"]
        for lbl, fg, bg, need in (("bubble text", c["ink"], c["bubOut"], 4.5),
                                  ("badge label", c["badgeInk"], c["badge"], 4.5),
                                  ("bubble / wall", c["bubOut"], c["wall"], 1.18)):
            r = ratio(fg, bg)
            if r < need: bad.append(f'{p["id"]}: {lbl} = {r} (need {need})')
        L, C, H = oklch(p["t"]["acc"])
        print(f'  {p["ru"]:<30} {SEED} -> fill {p["t"]["acc"]}  text {p["t"]["accInk"]}  '
              f'okL{L:.2f} okC{C:.3f} okH{H:.0f}   vs TG {dE(p["t"]["acc"], gd.TG):.0f}ΔE')

    def screens(p):
        return (gp.ph(gc.chat_html(p), "Чат") + gp.ph(gc.chats_html(p), "Список чатов")
                + gp.ph(gd.DASH4, "Сводка") + gp.ph(gp.BOTS, "Боты") + gp.ph(gp.SET, "Настройки"))

    css = (gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + gc.CHAT_CSS
           # five phones need more than the shared 1460px column
           + ".pal,.mast,.close,.fam{max-width:1980px}\n"
           + "".join(tokens_css(p) for p in P))
    o = ['<title>«Чернильный» — оба фона</title>', f'<style>{css}</style>', '<div class="page">']
    o.append('''<header class="mast">
 <p class="eb">Refined Modern · «Чернильный» · оба фона · все экраны</p>
 <h1>Ink navy, <em>light and dark.</em></h1>
 <p class="lede">All five screens: the chat thread, the chats list, «Сводка», «Боты» and the settings form.
 Same accent, same hue, only the ground changes.</p>
 <p class="lede"><strong>Worth knowing before you fall for it:</strong> on both grounds this accent measured
 as a near-duplicate of «Слива» and «Глубокий петроль» — ΔE 9–15 apart. It is a distinct colour to look at,
 but it occupies the same slot in the palette as those two, so choosing it rules them out.</p>
</header>''')
    for p in P:
        c = p["chat"]
        sw = "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>'
                     for h, l in ((p["t"]["bg"],"ground"), (p["t"]["acc"],"accent fill"),
                                  (p["t"]["accInk"],"accent text"), (c["bubOut"],"bubble"),
                                  (c["badge"],"unread"), (p["ownerStatus"],"alarm")))
        o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
  <p class="eb">{p['idx']:02d}</p><h3>{p['ru']}</h3>
  <p class="blurb">{p['blurb']}</p>
  <div class="swr">{sw}</div>{gd.collision(p)}
 </div><div>{gd.cx(rows[p['id']])}</div></div>
 <div class="row">{screens(p)}</div>
</section>''')
    o.append('''<section class="close">
 <h2>The two readings</h2>
 <p><strong>On paper it is ink, not a brand.</strong> At okL 0.37 with the ground at 0.97, the accent is the
 darkest thing on screen — the Сохранить button reads as authority rather than as invitation, and the unread
 badges are quiet. That suits a tool handling someone's money; it does not suit a product trying to feel
 friendly.</p>
 <p><strong>On the dark ground it becomes an ordinary blue.</strong> Lifting it to clear the substrate takes
 it to okL 0.52 and full chroma, which is energetic but also the closest this palette ever gets to Telegram's
 territory — check the ΔE line above each set before deciding.</p>
</section></div>''')
    open(OUT, "w", encoding="utf-8").write("".join(o))

    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0;font-weight:750;font-size:22px">'
            f'{p["ru"]}</h3></div><div class="row" style="padding:0 26px">{screens(p)}</div></section></div>')
    print(f"\nwrote {OUT}")
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("All contrast and collision gates PASS.")

main()
