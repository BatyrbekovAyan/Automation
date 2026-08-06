#!/usr/bin/env python3
"""Same two grounds the owner liked, ten new accents.

The pairing is forced by physics, not preference: a light accent cannot reach 3:1 as a
fill on white paper, and a dark accent disappears on a near-black ground. So —
  «Петроль» (light ice-grey paper)  gets the 5 DEEP accents
  «Графит»  (blue-black ground)     gets the 5 SHINY accents

Every gate from gen-dirs still applies, plus a within-ground accent-separation pass so
the five options on a given paper are genuinely five and not three.
"""
import os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "accent-shine.html")
SOLO = os.path.join(HERE, "_shine")

spec = importlib.util.spec_from_file_location("gc", os.path.join(HERE, "gen-chat.py"))
gc = importlib.util.module_from_spec(spec); spec.loader.exec_module(gc)
gm, gd, gp = gc.gm, gc.gd, gc.gp
dE, oklch, ratio, tune, nudge, is_stock = gd.dE, gd.oklch, gd.ratio, gd.tune, gd.nudge, gd.is_stock
WA, TG = gd.WA, gd.TG

DEEP = [   # for the light Петроль paper
 ("petrol-deep",  "Глубокий петроль", "#0B5E63", "the ground's own hue, taken to full depth"),
 ("wine",         "Бордо",            "#8A1F3D", "wine red — warmth without a single orange note"),
 ("plum",         "Слива",            "#5B2D82", "deep plum — the furthest from every channel colour"),
 ("bronze",       "Бронза",           "#7A4A1C", "burnt bronze — metal rather than pigment"),
 ("ink-navy",     "Чернильный",       "#243A7A", "ink navy — authority, and clear of Telegram by depth"),
]
SHINY = [  # for the dark Графит ground
 ("aqua",   "Аква",      "#4FE3E0", "electric aqua — the coldest and most instrument-like"),
 ("mint",   "Неон-мята", "#6FF0B0", "luminous mint — a glow rather than a fill"),
 ("coral",  "Коралл",    "#FF8A6B", "warm coral — the only one that feels human on black"),
 ("gold",   "Золото",    "#F5C24B", "lamp gold — reads as lit metal"),
 ("orchid", "Орхидея",   "#D79BFF", "light orchid — the most premium, the most fashion-dependent"),
]

def make(ground, items):
    P = []
    gm.ACCENTS = {k: dict(ru=ru, seed=seed, note=note) for k, ru, seed, note in items}
    for i, (k, ru, seed, note) in enumerate(items, 1):
        p = gm.build_palette(ground, k, i)
        p["id"] = f"{ground}-{k}"
        p["ru"] = f"«{ru}»"
        p["blurb"] = note.capitalize() + "."
        P.append(p)
    # five options on one paper must be five, not three
    for j in range(1, len(P)):
        t = P[j]["t"]; sf = t["sf"]
        earlier = [P[x]["t"]["acc"] for x in range(j)]
        if all(dE(t["acc"], e) >= 15 for e in earlier):
            continue
        t["acc"] = nudge(t["acc"], lambda c: (
            not is_stock(c) and ratio(c, sf) >= 3.0 and dE(c, t["ink2"]) >= 15
            and dE(c, WA) >= 15 and dE(c, TG) >= 15
            and all(dE(c, e) >= 15 for e in earlier)))
        t["saveBg"] = t["acc"]
        t["saveInk"] = tune(t["saveInk"], t["saveBg"], 4.5)
        # the statuses were tuned against the OLD accent — re-clear them against the new one
        pj = P[j]; acc = t["acc"]
        ok = lambda c: not is_stock(c, skip=("WhatsApp green", "Telegram blue"))
        # salience must match gd.gates exactly, or the nudge "fixes" what the audit rejects
        def salient(c, sf=sf):
            cL, cC, _ = oklch(c)
            return cC >= gd.CALM_C * 0.92 if oklch(sf)[0] > 0.6 else (
                cC >= gd.CALM_C * 0.92 or cL >= 0.75)
        pj["ownerStatus"] = nudge(pj["ownerStatus"], lambda c: (
            dE(c, acc) >= 15 and dE(c, pj["destructive"]) >= 12 and ok(c) and salient(c)))
        pj["destructive"] = nudge(pj["destructive"], lambda c: (
            dE(c, acc) >= 15 and dE(c, pj["ownerStatus"]) >= 12 and ok(c)))
        pj["orderCollected"] = nudge(pj["orderCollected"], lambda c: (
            dE(c, acc) >= 15 and dE(c, WA) >= 15 and dE(c, t["swOn"]) >= 15 and ok(c)))
        pj["inDialog"] = nudge(pj["inDialog"], lambda c: (
            dE(c, acc) >= 15 and dE(c, TG) >= 15 and dE(c, pj["orderCollected"]) >= 15 and ok(c)))
    for p in P:
        p["chat"] = gc.chat_tokens(p)
    return P

def tokens_css(p):
    c = p["chat"]
    extra = "".join(f"--{k}:{v};" for k, v in c.items()) + f'--accTx:{p["t"]["accInk"]};'
    return gd.tokens_css(p) + f'.p-{p["id"]}{{{extra}}}\n'

HEAD = {
 "petrol": ("«Петроль» + 5 глубоких акцентов",
   "The ice-grey paper you liked, unchanged. On white a fill has to reach 3:1 to have an edge at all, which "
   "puts every workable accent in the deep half of the range — so these are saturated and dark rather than "
   "bright. The brightness you get on paper comes from the ground, not the accent."),
 "graphite": ("«Графит» + 5 ярких акцентов",
   "The blue-black ground you liked, unchanged. Here the maths inverts: a dark accent vanishes, so the "
   "accent has to be the lightest thing on screen. These glow — which is exactly what «Изумрудная ночь» and "
   "«Полночь» were doing right, and why those key lights only worked on a dark substrate."),
}

def main():
    LIGHT = make("petrol", DEEP)
    DARK  = make("graphite", SHINY)
    P = LIGHT + DARK
    for i, p in enumerate(P, 1): p["idx"] = i

    rows, bad = gd.audit(P)
    for grp, name in ((LIGHT, "petrol"), (DARK, "graphite")):
        for x in range(len(grp)):
            for y in range(x+1, len(grp)):
                d = dE(grp[x]["t"]["acc"], grp[y]["t"]["acc"])
                if d < 15:
                    bad.append(f'{name}: {grp[x]["ru"]} ~ {grp[y]["ru"]} accents dE {d:.1f}')
    for p in P:
        c = p["chat"]
        for lbl, fg, bg, need in (("bubble text", c["ink"], c["bubOut"], 4.5),
                                  ("badge label", c["badgeInk"], c["badge"], 4.5),
                                  ("bubble / wall", c["bubOut"], c["wall"], 1.18)):
            r = ratio(fg, bg)
            if r < need: bad.append(f'{p["ru"]}: {lbl} = {r} (need {need})')

    css = gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + gc.CHAT_CSS + "".join(tokens_css(p) for p in P)
    o = ['<title>Акценты на ваших фонах</title>', f'<style>{css}</style>', '<div class="page">']
    o.append('''<header class="mast">
 <p class="eb">Refined Modern · 10 акцентов · ваши фоны</p>
 <h1>Same paper, <em>ten different lights.</em></h1>
 <p class="lede">Both grounds are untouched — the ice-grey «Петроль» and the blue-black «Графит» exactly as
 you saw them. Only the accent moves, across the chat thread, the chats list and the settings form.</p>
 <p class="lede"><strong>Why deep on light and shiny on dark:</strong> a primary button needs 3:1 against the
 card it sits on. On white paper that rules out every bright colour — they simply have no edge. On a
 near-black ground it rules out every dark one. The pairing is not a preference, it is what the contrast
 floor allows.</p>
</header>''')
    for grp, key in ((LIGHT, "petrol"), (DARK, "graphite")):
        ttl, dsc = HEAD[key]
        o.append(f'<section class="fam"><h2>{ttl}</h2><p>{dsc}</p></section>')
        for p in grp:
            c = p["chat"]
            sw = "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>'
                         for h, l in ((p["t"]["acc"],"accent fill"), (p["t"]["accInk"],"accent text"),
                                      (c["bubOut"],"bubble"), (c["badge"],"unread"),
                                      (p["t"]["bg"],"ground")))
            o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
  <p class="eb">{p['idx']:02d}</p><h3>{p['ru']}</h3>
  <p class="blurb">{p['blurb']}</p>
  <div class="swr">{sw}</div>
 </div><div>{gd.cx(rows[p['id']])}</div></div>
 <div class="row">{gp.ph(gc.chat_html(p),"Чат")}{gp.ph(gc.chats_html(p),"Список чатов")}
 {gp.ph(gp.SET,"Настройки")}</div>
</section>''')
    o.append('''<section class="close">
 <h2>Reading these</h2>
 <p><strong>The unread badge is the tell.</strong> On the chats list it is the only place the accent appears
 at full strength against a neutral row — if a colour looks weak or shouty there, it will behave the same
 way everywhere else in the app.</p>
 <p><strong>Then the suggestion block in the thread.</strong> «Бот предлагает ответ» has to outrank the
 conversation above it. An accent that disappears into the wallpaper there is too quiet, whatever it looks
 like on a button.</p>
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
            f'</div><figcaption class="pc">{p["idx"]:02d} · {p["ru"]}</figcaption></figure>' for p in grp)
        open(os.path.join(SOLO, f"sheet-{key}.html"), "w", encoding="utf-8").write(
            f'<title>sheet</title><style>{css}.grid{{display:grid;grid-template-columns:repeat(5,auto);'
            f'gap:24px 18px;justify-content:center;padding:24px}}</style>'
            f'<div class="page"><div class="grid">{cells}</div></div>')

    print(f"wrote {OUT}\npalettes: {len(P)}\n")
    for p in P:
        L, C, H = oklch(p["t"]["acc"])
        print(f'  {p["idx"]:>2} [{p["direction"][:4]}] {p["ru"]:<20} fill {p["t"]["acc"]} '
              f'text {p["t"]["accInk"]}  okL{L:.2f} okC{C:.3f} okH{H:>5.0f}')
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("\nAll contrast, collision and separation gates PASS.")

main()
