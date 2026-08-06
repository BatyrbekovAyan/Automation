#!/usr/bin/env python3
"""Mixed variants: the three grounds the owner liked × the three accents they liked.

Rules of the mix:
  - the GROUND is taken verbatim from the palette they liked (that is the thing they chose);
  - the ACCENT keeps its HUE and moves only in lightness, so it stays the colour they picked
    while meeting the contrast floor on that particular ground;
  - a STATUS moves before the brand does — the brand is the thing being chosen, and statuses
    exist to be legible, not to be sacred. Channel identity and the switch green never move.

Every gate from gen-dirs applies. Exits non-zero on any failure.
"""
import os, sys, json, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "mixed-palettes.html")
SOLO = os.path.join(HERE, "_mix")

spec = importlib.util.spec_from_file_location("gd", os.path.join(HERE, "gen-dirs.py"))
gd = importlib.util.module_from_spec(spec); spec.loader.exec_module(gd)
gp, dE, oklch, ratio, tune, nudge, is_stock = gd.gp, gd.dE, gd.oklch, gd.ratio, gd.tune, gd.nudge, gd.is_stock
WA, TG = gd.WA, gd.TG

# ---------------------------------------------------------------- ingredients
GROUNDS = {
 "petrol": dict(ru="Петроль", note="cool ice-grey paper, faintly teal", fam="light",
   t=dict(bg="#F4F8F8", sf="#FFFFFF", hl="#E3EDED", bd="#C4D6D7",
          ink="#08181B", ink2="#4C6265", ink3="#728A8D", sh="8,40,44"),
   chipBg="#08181B", chipInk="#FFFFFF", swOff="#C4D6D7"),
 "graphite": dict(ru="Графит", note="cool blue-black, borders doing the separating", fam="dark",
   t=dict(bg="#0E1116", sf="#171C24", hl="#242C38", bd="#333E4E",
          ink="#ECF0F6", ink2="#9AA6B8", ink3="#79869A", sh="0,0,0"),
   chipBg="#ECF0F6", chipInk="#0E1116", swOff="#333E4E"),
 "cobalt": dict(ru="Кобальт", note="blue-biased white paper, the crispest of the three", fam="light",
   t=dict(bg="#F7F9FC", sf="#FFFFFF", hl="#E9EDF4", bd="#CFD6E3",
          ink="#0B1220", ink2="#59637A", ink3="#79839B", sh="16,24,40"),
   chipBg="#0B1220", chipInk="#FFFFFF", swOff="#CFD6E3"),
}

# SET A — Мята #0B7A4A and Тёплый камень #1E6B4F measure dE 5.1 apart: one accent, not two.
# Тёплый камень is kept: better contrast (6.42 vs 5.39) and further from order_collected.
ACCENTS_A = {
 "stone": dict(ru="Тёплый камень", seed="#1E6B4F",
   note="deep forest green — the calm one; also absorbs «Мята», which measures only ΔE 5.1 away"),
 "espresso": dict(ru="Эспрессо", seed="#E8A33D",
   note="amber — vivid on the dark ground, necessarily burnt down to gold on paper"),
 "sky": dict(ru="Графит-небо", seed="#4C8DFF",
   note="sky blue — the one that has to be pushed off Telegram and «В диалоге»"),
}

# SET B — the two Spatial palettes are taken by their KEY LIGHT, not their action colour:
# «Полночь»'s action is literally #1B5CEB, the same hex as «Кобальт», so using it would
# ship the same accent twice. The key light is what makes those palettes recognisable.
ACCENTS_B = {
 "midnight": dict(ru="Полночь", seed="#8CBEFF",
   note="the pale cobalt key light of «Полночь» — luminous rather than saturated"),
 "emerald": dict(ru="Изумрудная ночь", seed="#7CF3AC",
   note="the luminous mint key light of «Изумрудная ночь»"),
 "mint": dict(ru="Мята", seed="#0B7A4A",
   note="deep pine — the darkest, most reserved green of the set"),
 "cobalt": dict(ru="Кобальт", seed="#1B5CEB",
   note="saturated cobalt — the most conventional 'software' blue here"),
}

SETS = {"a": ("mixed-palettes.html", "_mix", ACCENTS_A, ("stone","espresso","sky")),
        "b": ("mixed-palettes-b.html", "_mixb", ACCENTS_B, ("midnight","emerald","mint","cobalt"))}
SET = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] in SETS else "a"
OUT  = os.path.join(HERE, SETS[SET][0])
SOLO = os.path.join(HERE, SETS[SET][1])
ACCENTS = SETS[SET][2]
ORDER   = SETS[SET][3]

DEFAULTS = dict(order="#16A75C", owner="#E07612", dialog="#3B82F6", destructive="#A01B12")
SWITCH_ON = "#34C759"

def adapt(seed, sf, floor, extra=lambda c: True):
    """Keep the hue, move lightness to clear `floor`; only if that still fails a
    constraint do we let the colour leave its hue."""
    cand = tune(seed, sf, floor)
    if ratio(cand, sf) >= floor and extra(cand) and not is_stock(cand):
        return cand
    return nudge(cand, lambda c: ratio(c, sf) >= floor and extra(c) and not is_stock(c))

def build_palette(gk, ak, idx):
    G, A = GROUNDS[gk], ACCENTS[ak]
    t = dict(G["t"])
    sf = t["sf"]
    seed = A["seed"]
    # the ground's ink was tuned against ITS original surface — re-tune for this one
    for k in ("ink", "ink2", "ink3"):
        t[k] = tune(t[k], sf, 4.5)

    # accent: hue preserved, lightness adapted to this ground
    t["acc"] = adapt(seed, sf, 3.0, lambda c: (
        dE(c, WA) >= 15 and dE(c, TG) >= 15 and dE(c, SWITCH_ON) >= 15 and dE(c, t["ink2"]) >= 15))
    t["accInk"] = adapt(seed, sf, 4.5, lambda c: dE(c, t["ink2"]) >= 15)
    acc = t["acc"]

    t["chipBg"], t["chipInk"] = G["chipBg"], G["chipInk"]
    t["saveBg"] = acc
    t["saveInk"] = "#FFFFFF" if oklch(acc)[0] < 0.62 else "#12100A"
    t["saveInk"] = tune(t["saveInk"], t["saveBg"], 4.5)
    t["swOn"] = SWITCH_ON
    t["swOff"] = tune(G["swOff"], sf, 3.0)
    t["goodBg"] = "#123324" if G["fam"] == "dark" else "#E6F6EE"
    t["goodInk"] = tune("#57DE95" if G["fam"] == "dark" else "#0A6B3E", t["goodBg"], 4.5)
    t["bdI"] = tune(t["bd"], sf, 3.0)

    # statuses move around the brand, never the other way
    # the alarm must out-shout the calm statuses — same ground-aware rule the gate uses,
    # or the nudge "fixes" a palette the audit then rejects
    def salient(c):
        cL, cC, _ = oklch(c)
        if oklch(sf)[0] > 0.6:
            return cC >= gd.CALM_C * 0.92
        return cC >= gd.CALM_C * 0.92 or cL >= 0.75

    owner = nudge(DEFAULTS["owner"], lambda c: (
        dE(c, acc) >= 15 and not is_stock(c, skip=("WhatsApp green","Telegram blue"))
        and salient(c)))
    dest = nudge(DEFAULTS["destructive"], lambda c: (
        dE(c, acc) >= 15 and dE(c, owner) >= 12
        and not is_stock(c, skip=("WhatsApp green","Telegram blue"))))
    order = nudge(DEFAULTS["order"], lambda c: (
        dE(c, acc) >= 15 and dE(c, WA) >= 15 and dE(c, SWITCH_ON) >= 15
        and not is_stock(c, skip=("WhatsApp green","Telegram blue"))))
    dialog = nudge(DEFAULTS["dialog"], lambda c: (
        dE(c, acc) >= 15 and dE(c, TG) >= 15 and dE(c, order) >= 15
        and not is_stock(c, skip=("WhatsApp green","Telegram blue"))))

    moved = [n for n, a, b in (("Нужен владелец", owner, DEFAULTS["owner"]),
                               ("Заказ собран", order, DEFAULTS["order"]),
                               ("В диалоге", dialog, DEFAULTS["dialog"])) if a != b]
    solve = (f'Ground taken verbatim from «{G["ru"]}»; the accent keeps its hue and moves only in '
             f'lightness to clear this ground. ' +
             (f'Moved to clear the brand: {", ".join(moved)}.' if moved
              else 'No status needed moving — this accent clears everything as-is.'))
    return dict(id=f"{gk}-{ak}", idx=idx, ru=f'«{G["ru"]}» × «{A["ru"]}»',
                en=f'{G["ru"]} ground · {A["ru"]} accent', fam=G["fam"], direction=gk,
                family=A["note"], blurb=f'{G["note"].capitalize()}, carrying {A["note"]}.',
                risk="", collisionSolve=solve, ownerStatus=owner, inDialog=dialog,
                orderCollected=order, destructive=dest, t=t)

SET_INTRO = {
 "a": ('<p class="lede"><strong>You named four accents but chose three.</strong> «Мята» #0B7A4A and '
       '«Тёплый камень» #1E6B4F measure ΔE 5.1 apart — under the 15 needed to tell two colours apart, so '
       'they are one green. «Тёплый камень» carries it: better contrast, further from «Заказ собран».</p>'
       '<p class="lede"><strong>And one accent does not survive daylight.</strong> The Эспрессо amber is '
       '2.16:1 on white — no visible button edge — so on the two light grounds it is burnt down to a darker '
       'gold. Only on «Графит» does it stay the amber you liked.</p>'),
 "b": ('<p class="lede"><strong>Two of these were the same colour.</strong> «Полночь»\'s action colour is '
       'literally #1B5CEB — the identical hex to «Кобальт». So the two Spatial palettes are taken by their '
       'signature <em>key light</em> instead (#8CBEFF and #7CF3AC), which is what actually makes them '
       'recognisable, and gives you four accents rather than three plus a duplicate.</p>'
       '<p class="lede"><strong>Watch the light grounds.</strong> Two of these accents are luminous glows '
       'built for a dark substrate. Meeting a 3:1 floor on white forces them down in lightness, and colours '
       'that differed mainly in lightness converge once they land on the same paper — every pair is checked '
       'for that below, per ground.</p>'),
}

GROUND_HEAD = {
 "petrol":   ("«Петроль» — светлая прохладная",
   "Ice-grey paper with a faint teal cast. The coolest of the three light grounds, and the one that makes a "
   "warm accent look most deliberate."),
 "graphite": ("«Графит» — тёмная прохладная",
   "Blue-black, borders doing the separating that shadows cannot do on near-black. The only ground here that "
   "lets the Эспрессо amber stay at full strength."),
 "cobalt":   ("«Кобальт» — светлая синеватая",
   "Blue-biased white — the crispest and most conventionally 'software' of the three. Accents read at their "
   "most saturated here because the ground gives them nothing."),
}

def build(P, rows):
    css = gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + "".join(gd.tokens_css(p) for p in P)
    o = ['<title>Смешанные варианты</title>', f'<style>{css}</style>', '<div class="page">']
    o.append(f'''<header class="mast">
 <p class="eb">Refined Modern · смешанные варианты · {len(P)}</p>
 <h1>Your grounds, <em>your accents, every combination.</em></h1>
 <p class="lede">Three grounds you liked — <strong>Петроль, Графит, Кобальт</strong> — each carrying the
 three accents you liked. The ground is taken verbatim; the accent keeps its hue and moves only in lightness,
 so it stays the colour you picked while clearing the contrast floor on that particular paper.</p>
 {SET_INTRO[SET]}
</header>''')
    seen = set()
    for p in P:
        if p["direction"] not in seen:
            seen.add(p["direction"]); ttl, dsc = GROUND_HEAD[p["direction"]]
            o.append(f'<section class="fam"><h2>{ttl}</h2><p>{dsc}</p></section>')
        row = gp.ph(gd.DASH4,"Сводка") + gp.ph(gp.BOTS,"Боты") + gp.ph(gp.SET,"Настройки")
        o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
   <p class="eb">{p['idx']:02d}</p>
   <h3>{p['ru']}<span class="fmly">{p['fam']}</span></h3>
   <p class="blurb">{p['blurb']}</p>
   {'<p class="rk"><b>Duplicate</b><br>' + p['dupe'] + '</p>' if p.get('dupe') else ''}
   <div class="swr">{gd.swatches(p)}</div>{gd.collision(p)}
  </div><div>{gd.cx(rows[p['id']])}</div></div>
 <div class="row">{row}</div></section>''')
    o.append('''<section class="close">
 <h2>Reading the grid</h2>
 <p><strong>Compare down a column, not across a row.</strong> Holding the accent fixed and changing the
 ground is the comparison that actually tells you something — the ground decides whether an accent reads
 premium or loud, far more than the accent itself does.</p>
 <p><strong>The amber row is the honest one.</strong> On «Графит» it is the colour you liked. On the two
 papers it is a different, darker colour wearing the same name, because orange has the narrowest safe
 contrast band of any hue. If you love it on paper too, the answer is a warmer ground rather than a
 brighter amber.</p>
 <p>Whichever wins becomes one token block in <code>Assets/Scripts/Theme/</code>, read by every
 <code>[MenuItem]</code> builder.</p>
</section></div>''')
    open(OUT, "w", encoding="utf-8").write("".join(o))
    return css

def main():
    P, i = [], 0
    for gk in ("petrol", "graphite", "cobalt"):
        for ak in ORDER:
            i += 1; P.append(build_palette(gk, ak, i))
    rows, bad = gd.audit(P)
    converged = []
    # accents only have to differ WITHIN a ground — that is where they sit side by side.
    # Adapting to a contrast floor flattens lightness, so palettes that differed only in
    # lightness can converge once they land on the same paper.
    for gk in ("petrol", "graphite", "cobalt"):
        grp = [p for p in P if p["direction"] == gk]
        for x in range(len(grp)):
            for y in range(x+1, len(grp)):
                d = dE(grp[x]["t"]["acc"], grp[y]["t"]["acc"])
                if d < 15:
                    converged.append((gk, grp[x], grp[y], d))
                    grp[y]["dupe"] = f'На этом фоне почти неотличим от {grp[x]["ru"]} — ΔE {d:.1f}'
                    grp[x].setdefault("dupe", "")
    css = build(P, rows)
    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        row = gp.ph(gd.DASH4,"Сводка") + gp.ph(gp.BOTS,"Боты") + gp.ph(gp.SET,"Настройки")
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0 0 8px;font-weight:750;font-size:22px">'
            f'{p["ru"]}</h3><div class="swr">{gd.swatches(p)}</div></div>'
            f'<div class="row" style="padding:0 26px">{row}</div></section></div>')
    cells = "".join(
        f'<figure class="pw"><div class="phone p-{p["id"]}"><div class="scr">{gd.DASH4}</div></div>'
        f'<figcaption class="pc">{p["idx"]:02d} · {p["ru"]}</figcaption></figure>' for p in P)
    open(os.path.join(SOLO, "contact-sheet.html"), "w", encoding="utf-8").write(
        f'<title>sheet</title><style>{css}.grid{{display:grid;grid-template-columns:repeat(3,auto);'
        f'gap:26px 22px;justify-content:center;padding:26px}}</style>'
        f'<div class="page"><div class="grid">{cells}</div></div>')

    print(f"wrote {OUT}\npalettes: {len(P)}   checks: {len(P)*len(gd.CHECKS)}\n")
    for p in P:
        L, C, H = oklch(p["t"]["acc"])
        print(f'  {p["idx"]:>2} {p["ru"]:<34}{p["fam"]:<6}fill {p["t"]["acc"]} text {p["t"]["accInk"]}  '
              f'okL{L:.2f} okH{H:>5.0f}')
    if converged:
        print(f"\n*** {len(converged)} CONVERGED PAIRS (reported, not blocking) ***")
        for gk, a, b, d in converged:
            print(f'   on {gk}: {a["ru"]} ~ {b["ru"]} — dE {d:.1f}')
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("\nAll contrast checks and collision gates PASS.")

if __name__ == "__main__":
    main()
