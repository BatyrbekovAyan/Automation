#!/usr/bin/env python3
"""Refined Modern × 8 palettes. Material is FIXED; only tokens move.
Computes real WCAG ratios and refuses to ship a failing pair silently."""

import os, sys

OUT      = "/Users/ayan/Projects/Automation/docs/design/ui-restyle/refined-modern-palettes.html"
SHEETOUT = "/private/tmp/claude-501/-Users-ayan-Projects-Automation/c2a2e674-f875-4997-938b-59ea62988a49/scratchpad/solo2/contact-sheet.html"

# ----------------------------------------------------------------- WCAG
def _lin(c):
    c /= 255.0
    return c/12.92 if c <= 0.04045 else ((c+0.055)/1.055) ** 2.4

def lum(hx):
    hx = hx.lstrip('#')
    r, g, b = (int(hx[i:i+2], 16) for i in (0, 2, 4))
    return 0.2126*_lin(r) + 0.7152*_lin(g) + 0.0722*_lin(b)

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return round((hi + 0.05) / (lo + 0.05), 2)

# --- hue-preserving tuner: walk lightness until the pair clears `target` -----
def _rgb(hx):
    hx = hx.lstrip('#'); return tuple(int(hx[i:i+2], 16) for i in (0, 2, 4))

def _hex(r, g, b):
    return "#%02X%02X%02X" % (max(0,min(255,round(r))), max(0,min(255,round(g))), max(0,min(255,round(b))))

def _to_hsl(hx):
    r, g, b = (v/255 for v in _rgb(hx))
    mx, mn = max(r, g, b), min(r, g, b)
    l = (mx+mn)/2
    if mx == mn: return 0.0, 0.0, l
    d = mx-mn
    s = d/(2-mx-mn) if l > 0.5 else d/(mx+mn)
    if   mx == r: h = ((g-b)/d + (6 if g < b else 0))/6
    elif mx == g: h = ((b-r)/d + 2)/6
    else:         h = ((r-g)/d + 4)/6
    return h, s, l

def _from_hsl(h, s, l):
    if s == 0: v = l*255; return _hex(v, v, v)
    q = l*(1+s) if l < 0.5 else l+s-l*s
    p = 2*l-q
    def f(t):
        t %= 1
        if t < 1/6: return p+(q-p)*6*t
        if t < 1/2: return q
        if t < 2/3: return p+(q-p)*(2/3-t)*6
        return p
    return _hex(f(h+1/3)*255, f(h)*255, f(h-1/3)*255)

def tune(base, bg, target):
    """Return `base` shifted in lightness (hue+sat kept) until ratio(base,bg) >= target."""
    if ratio(base, bg) >= target: return base
    h, s, l = _to_hsl(base)
    darken = lum(bg) > 0.18            # light ground -> go darker, dark ground -> go lighter
    step = -0.01 if darken else 0.01
    cur = l
    for _ in range(100):
        cur += step
        if not (0.0 <= cur <= 1.0): break
        cand = _from_hsl(h, s, cur)
        if ratio(cand, bg) >= target: return cand
    return "#000000" if darken else "#FFFFFF"

# ----------------------------------------------------------------- palettes
# acc     = fill colour (buttons, bars, switch-on)
# accInk  = the SAME hue darkened/lightened until it passes 4.5:1 as TEXT on surface
P = [
dict(id="slate", n=1, ru="«Сланец»", en="Slate & Cobalt", fam="light",
  blurb="The software default, done properly. Neutrals carry a blue bias so they read as chosen rather "
        "than inherited, and a single cobalt does every job an accent should.",
  risk="Safest and least distinctive — closest to what a competent SaaS looks like everywhere.",
  t=dict(bg="#F7F9FC", sf="#FFFFFF", hl="#E9EDF4", bd="#CFD6E3",
         ink="#0B1220", ink2="#59637A", ink3="#79839B",
         acc="#1B5CEB", accInk="#1550CE", accSoft="#EAF0FE",
         chipBg="#0B1220", chipInk="#FFFFFF", saveBg="#1B5CEB", saveInk="#FFFFFF",
         swOn="#0F9D58", swOff="#CFD6E3", goodBg="#E6F6EE", goodInk="#0A6B3E",
         sh="16,24,40")),

dict(id="paper", n=2, ru="«Бумага»", en="Paper & Vermilion", fam="light",
  blurb="Achromatic paper with exactly one hot colour. Nothing is tinted, nothing competes — so the single "
        "vermilion carries total authority wherever it lands. The most editorial of the eight.",
  risk="Unforgiving: with no colour to hide behind, every spacing and alignment error is visible.",
  t=dict(bg="#FAFAF9", sf="#FFFFFF", hl="#ECEAE7", bd="#D2CFCA",
         ink="#141311", ink2="#57544E", ink3="#847F77",
         acc="#D93A16", accInk="#B32E0F", accSoft="#FCEDE8",
         chipBg="#141311", chipInk="#FFFFFF", saveBg="#141311", saveInk="#FFFFFF",
         swOn="#177245", swOff="#D2CFCA", goodBg="#E8F3EC", goodInk="#136139",
         sh="30,26,20")),

dict(id="sand", n=3, ru="«Песок»", en="Sand & Sage", fam="light",
  blurb="Warm greige paper and a muted sage green. Reads human and unhurried rather than technical — the "
        "least 'software' palette here, and the one a florist would find least intimidating.",
  risk="Low energy. Urgency states have to work harder to cut through the calm.",
  t=dict(bg="#F8F6F1", sf="#FFFFFF", hl="#EDE8DE", bd="#D6CEBF",
         ink="#1F1D17", ink2="#665F52", ink3="#8F8878",
         acc="#3F6B4A", accInk="#33593D", accSoft="#EAF1EB",
         chipBg="#1F1D17", chipInk="#FDFCF9", saveBg="#3F6B4A", saveInk="#FFFFFF",
         swOn="#3F6B4A", swOff="#D6CEBF", goodBg="#E7F0E9", goodInk="#2E5537",
         sh="60,52,34")),

dict(id="crimson", n=4, ru="«Алматы»", en="Almaty Crimson", fam="light",
  blurb="Deep crimson on a warm-neutral ground — the colour of commerce in Kazakhstan, the register every "
        "Kaspi seller already reads as money and motion. The most locally native palette of the eight.",
  risk="Red is spoken for. Destructive actions must move to an outlined treatment so 'delete' never "
       "wears the same colour as 'save' — handled below.",
  t=dict(bg="#FBF8F7", sf="#FFFFFF", hl="#F0E7E5", bd="#DCCBC8",
         ink="#1C1214", ink2="#6B5A5C", ink3="#948284",
         acc="#B01E28", accInk="#961A22", accSoft="#FBEBEC",
         chipBg="#1C1214", chipInk="#FFFFFF", saveBg="#B01E28", saveInk="#FFFFFF",
         swOn="#1B7F4B", swOff="#DCCBC8", goodBg="#E7F3EB", goodInk="#12653A",
         sh="60,26,30")),

dict(id="petrol", n=5, ru="«Петроль»", en="Petrol & Ice", fam="light",
  blurb="Deep petrol teal over cool ice-grey. Sits deliberately between WhatsApp green and Telegram blue "
        "without imitating either, so the app keeps its own voice while both channels stay legible.",
  risk="Teal reads clinical to some; pair it with warm photography or it can feel cold.",
  t=dict(bg="#F4F8F8", sf="#FFFFFF", hl="#E3EDED", bd="#C4D6D7",
         ink="#08181B", ink2="#4C6265", ink3="#728A8D",
         acc="#0B6E70", accInk="#0A5F61", accSoft="#E6F3F3",
         chipBg="#08181B", chipInk="#FFFFFF", saveBg="#0B6E70", saveInk="#FFFFFF",
         swOn="#0F8A55", swOff="#C4D6D7", goodBg="#E4F3EC", goodInk="#0B6B41",
         sh="8,40,44")),

dict(id="graphite", n=6, ru="«Графит»", en="Graphite & Sky", fam="dark",
  blurb="The cool dark the token system produces for free. Borders do the separating that shadows cannot "
        "do on black, and the accent lifts to a sky blue so it still reads as text.",
  risk="Dark UI hides low-contrast mistakes in review and exposes them in sunlight.",
  t=dict(bg="#0E1116", sf="#171C24", hl="#242C38", bd="#333E4E",
         ink="#ECF0F6", ink2="#9AA6B8", ink3="#79869A",
         acc="#4C8DFF", accInk="#7CA8FF", accSoft="#16233A",
         chipBg="#ECF0F6", chipInk="#0E1116", saveBg="#2E6BFF", saveInk="#FFFFFF",
         swOn="#1DA366", swOff="#333E4E", goodBg="#123324", goodInk="#57DE95",
         sh="0,0,0")),

dict(id="espresso", n=7, ru="«Эспрессо»", en="Espresso & Amber", fam="dark",
  blurb="A warm dark — near-black with brown in it, lit by amber. Feels like a well-made instrument rather "
        "than a dashboard, and it is the only palette here that is genuinely easy on the eyes at night.",
  risk="Amber sits near the warning hue; warnings shift to a distinct red-orange to stay separable.",
  t=dict(bg="#141110", sf="#1F1B19", hl="#2E2825", bd="#433B35",
         ink="#F5EFE8", ink2="#B3A79A", ink3="#8D8175",
         acc="#E8A33D", accInk="#F0B košík", accSoft="#33261200",
         chipBg="#F5EFE8", chipInk="#141110", saveBg="#E8A33D", saveInk="#241803",
         swOn="#2E9E63", swOff="#433B35", goodBg="#12301F", goodInk="#5FD394",
         sh="0,0,0")),

dict(id="obsidian", n=8, ru="«Обсидиан»", en="Obsidian & Violet", fam="dark",
  blurb="Blue-black ground with an electric violet. The most premium-feeling dark of the three, and the "
        "furthest from both channel colours — the app clearly sits above WhatsApp and Telegram, not inside them.",
  risk="Violet carries no meaning in this product; it is pure brand, which some owners read as unserious.",
  t=dict(bg="#0B0B14", sf="#16161F", hl="#23232F", bd="#33334A",
         ink="#EFEEF7", ink2="#A3A2B8", ink3="#82819A",
         acc="#8B6DF5", accInk="#A98BFF", accSoft="#1C1830",
         chipBg="#EFEEF7", chipInk="#0B0B14", saveBg="#7C5AF0", saveInk="#FFFFFF",
         swOn="#26A96B", swOff="#33334A", goodBg="#12301F", goodInk="#5FD394",
         sh="0,0,0")),
]

# repair the one corrupted token before anything reads it
P[6]["t"]["accInk"]  = "#F0B65C"
P[6]["t"]["accSoft"] = "#332612"

# ----------------------------------------------------------------- audit
# WCAG 1.4.11 applies to a boundary that IS the affordance (input wells, icon buttons).
# A card outline is decorative — forcing it to 3:1 would make the whole style look boxed.
# So the two are different tokens, held to different bars.
CHECKS = [
    ("body ink / surface",        "ink",    "sf",     4.5),
    ("secondary ink / surface",   "ink2",   "sf",     4.5),
    ("tertiary ink / surface",    "ink3",   "sf",     4.5),
    ("accent text / surface",     "accInk", "sf",     4.5),
    ("save label / save fill",    "saveInk","saveBg", 4.5),
    ("chip label / chip fill",    "chipInk","chipBg", 4.5),
    ("status ink / status bg",    "goodInk","goodBg", 4.5),
    ("input border / surface",    "bdI",    "sf",     3.0),
    ("card border / surface  (decor)", "bd", "sf",  0.0),
    ("body ink / ground",         "ink",    "bg",     4.5),
]

def derive():
    """Auto-tune every token that has a floor, so no value is asserted by hand."""
    for p in P:
        t = p["t"]
        t["ink"]    = tune(t["ink"],    t["sf"], 4.5)
        t["ink2"]   = tune(t["ink2"],   t["sf"], 4.5)
        t["ink3"]   = tune(t["ink3"],   t["sf"], 4.5)
        t["accInk"] = tune(t["accInk"], t["sf"], 4.5)
        t["goodInk"]= tune(t["goodInk"],t["goodBg"], 4.5)
        # interactive border: start from the decorative border's hue, push to 3:1
        t["bdI"]    = tune(t["bd"],     t["sf"], 3.0)

def audit():
    bad, rows = [], {}
    for p in P:
        t, res = p["t"], []
        for label, fg, bg, need in CHECKS:
            r = ratio(t[fg], t[bg])
            ok = r >= need
            if not ok:
                bad.append(f'{p["en"]}: {label} = {r} (need {need}) [{t[fg]} on {t[bg]}]')
            res.append((label, r, need, ok))
        rows[p["id"]] = res
    return rows, bad

# ----------------------------------------------------------------- css
PAGE_CSS = r"""
:root{--g:#F4F6FA;--g2:#E8ECF3;--rl:#D5DBE6;--k:#0D1119;--k2:#4C5568;--k3:#7D8698;
--ac:#1B5CEB;--acs:#DFE8FC;--mono:ui-monospace,SFMono-Regular,"SF Mono",Menlo,Consolas,monospace;
--pad:clamp(20px,4vw,60px);}
@media (prefers-color-scheme:dark){:root{--g:#0B0E14;--g2:#131822;--rl:#222937;--k:#EDF0F6;
--k2:#A0AABD;--k3:#6B7587;--ac:#7AA5FF;--acs:#16223A;}}
:root[data-theme="dark"]{--g:#0B0E14;--g2:#131822;--rl:#222937;--k:#EDF0F6;--k2:#A0AABD;
--k3:#6B7587;--ac:#7AA5FF;--acs:#16223A;}
:root[data-theme="light"]{--g:#F4F6FA;--g2:#E8ECF3;--rl:#D5DBE6;--k:#0D1119;--k2:#4C5568;
--k3:#7D8698;--ac:#1B5CEB;--acs:#DFE8FC;}
*{box-sizing:border-box}
.page{background:var(--g);color:var(--k);font:16px/1.6 -apple-system,BlinkMacSystemFont,"Segoe UI",system-ui,sans-serif;
-webkit-font-smoothing:antialiased;min-height:100vh}
.mast{max-width:1460px;margin:0 auto;padding:clamp(44px,7vw,92px) var(--pad) clamp(26px,4vw,42px);
border-bottom:1px solid var(--rl)}
.eb{font-family:var(--mono);font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--k3);margin:0 0 16px}
.mast h1{font-weight:800;font-size:clamp(29px,5.2vw,54px);line-height:1.04;letter-spacing:-.035em;
text-wrap:balance;margin:0 0 18px;max-width:19ch}
.mast h1 em{font-style:normal;color:var(--k3)}
.lede{max-width:66ch;font-size:clamp(15.5px,1.75vw,18px);color:var(--k2);margin:0 0 12px}
.lede strong{color:var(--k);font-weight:600}
.fam{max-width:1460px;margin:0 auto;padding:clamp(38px,5vw,62px) var(--pad) 0}
.fam h2{font-weight:800;font-size:clamp(19px,2.3vw,25px);letter-spacing:-.025em;margin:0 0 6px}
.fam p{color:var(--k2);font-size:15.5px;margin:0;max-width:68ch}
.pal{max-width:1460px;margin:0 auto;padding:clamp(24px,3vw,38px) var(--pad) clamp(32px,4vw,50px);
border-bottom:1px solid var(--rl)}
.ph{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,340px);gap:clamp(20px,3vw,52px);
align-items:start;margin-bottom:24px}
@media(max-width:1000px){.ph{grid-template-columns:minmax(0,1fr)}}
.ph h3{font-weight:750;font-size:clamp(20px,2.4vw,27px);letter-spacing:-.025em;margin:0 0 8px}
.ph h3 span{color:var(--k3);font-weight:500}
.ph .blurb{color:var(--k2);font-size:15.5px;margin:0 0 12px;max-width:60ch}
.ph .rk{font-size:14px;color:var(--k2);margin:0;padding:9px 13px;border-radius:8px;background:var(--g2);
max-width:60ch;border-left:2px solid var(--k3)}
.ph .rk b{font-family:var(--mono);font-size:10px;letter-spacing:.12em;text-transform:uppercase;color:var(--k3)}
.swr{display:flex;flex-wrap:wrap;gap:7px;margin:14px 0 0}
.sw{display:flex;flex-direction:column;gap:5px;font-family:var(--mono);font-size:9.5px;
letter-spacing:.03em;color:var(--k3);line-height:1.4}
.sw i{display:block;width:54px;height:32px;border-radius:6px;border:1px solid rgba(128,128,128,.28)}
table.cx{width:100%;border-collapse:collapse;font-family:var(--mono);font-size:11px}
table.cx caption{text-align:left;font-family:var(--mono);font-size:10px;letter-spacing:.12em;
text-transform:uppercase;color:var(--k3);padding-bottom:7px}
table.cx td{padding:4px 0;border-bottom:1px solid var(--rl);color:var(--k2)}
table.cx td:last-child{text-align:right;font-variant-numeric:tabular-nums;color:var(--k)}
table.cx td.ok:last-child::after{content:" ✓";color:#12A150}
.row{display:flex;gap:22px;flex-wrap:wrap;align-items:flex-start}
.pw{display:flex;flex-direction:column;gap:11px;align-items:center;margin:0}
.pc{font-family:var(--mono);font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:var(--k3)}
.close{max-width:1460px;margin:0 auto;padding:clamp(38px,5vw,68px) var(--pad) clamp(54px,7vw,96px)}
.close h2{font-weight:800;font-size:clamp(21px,2.8vw,30px);letter-spacing:-.03em;margin:0 0 14px}
.close p{max-width:68ch;color:var(--k2);font-size:16px;margin:0 0 13px}
.close p strong{color:var(--k);font-weight:600}
.close code{font-family:var(--mono);font-size:.88em}
@media(prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
"""

PHONE_CSS = r"""
.phone{width:320px;height:665px;border-radius:44px;padding:10px;
background:linear-gradient(160deg,#3A3F4A,#16181D 40%,#2A2E36);
box-shadow:0 2px 4px rgba(0,0,0,.3),0 20px 40px -12px rgba(10,14,24,.45),0 48px 76px -30px rgba(10,14,24,.3);
flex-shrink:0}
.scr{width:100%;height:100%;border-radius:35px;overflow:hidden;display:flex;flex-direction:column;
font-size:12.5px;isolation:isolate;color:var(--ink);background:var(--bgS)}
.sb{display:flex;justify-content:space-between;align-items:center;padding:12px 21px 3px;
font-size:11px;font-weight:600;flex-shrink:0}
.sg{display:flex;gap:3px;align-items:flex-end}
.sg i{display:block;width:3px;border-radius:1px;background:currentColor}
.sg i:nth-child(1){height:4px}.sg i:nth-child(2){height:6px}.sg i:nth-child(3){height:8px}.sg i:nth-child(4){height:10px}
.bt{width:19px;height:10px;border:1.2px solid currentColor;border-radius:3px;padding:1.4px;margin-left:5px}
.bt i{display:block;height:100%;width:72%;border-radius:1px;background:currentColor}
.ah{display:flex;align-items:center;justify-content:space-between;padding:9px 18px 11px;flex-shrink:0}
.ah h3{margin:0;font-size:20px;font-weight:700;letter-spacing:-.025em}
.ah.s h3{font-size:16px;flex:1;margin:0 10px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.hb{width:32px;height:32px;border-radius:50%;display:grid;place-items:center;font-size:17px;line-height:1;
flex-shrink:0;background:var(--sf);border:1px solid var(--bdI);color:var(--ink2);box-shadow:var(--e1)}
.bd{flex:1;overflow:hidden;padding:2px 15px 8px;display:flex;flex-direction:column;gap:12px}
.cs{display:flex;gap:7px}
.cp{padding:6px 12px;border-radius:999px;font-size:11px;font-weight:600;white-space:nowrap;
background:var(--sf);color:var(--ink2);border:1px solid var(--bd);box-shadow:var(--e1)}
.cp.on{background:var(--chipBg);border-color:var(--chipBg);color:var(--chipInk);box-shadow:var(--e2)}
.hero{padding:15px 17px 14px;display:flex;flex-direction:column;gap:2px;border-radius:16px;
background:var(--sf);border:1px solid var(--bd);box-shadow:var(--e2)}
.hero .l{font-size:10px;font-weight:600;letter-spacing:.09em;text-transform:uppercase;color:var(--ink3)}
.hero .n{font-size:38px;font-weight:800;line-height:1.04;letter-spacing:-.045em;
font-variant-numeric:tabular-nums;display:flex;align-items:baseline;gap:9px;color:var(--ink)}
.hero .n small{font-size:12px;font-weight:700;padding:3px 8px;border-radius:999px;
background:var(--goodBg);color:var(--goodInk)}
.hero .s{font-size:12px;color:var(--ink2)}
.spk{display:flex;align-items:flex-end;gap:4px;height:28px;margin-top:9px}
.spk i{flex:1;border-radius:2px 2px 1px 1px;display:block;background:var(--hl)}
.spk i:nth-child(n+6){background:var(--acc)}
.rws{display:flex;flex-direction:column;gap:7px}
.rw{display:flex;align-items:center;gap:10px;padding:10px 13px;border-radius:11px;
background:var(--sf);border:1px solid var(--hl);box-shadow:var(--e1)}
.rw .d{width:9px;height:9px;border-radius:50%;flex-shrink:0}
.rw .m{flex:1;font-size:12.5px;font-weight:500;color:var(--ink)}
.rw .c{font-size:13.5px;font-weight:700;font-variant-numeric:tabular-nums;color:var(--ink)}
.av{width:38px;height:38px;border-radius:12px;display:grid;place-items:center;font-size:14px;
font-weight:700;flex-shrink:0;color:#FFF;box-shadow:inset 0 1px 0 rgba(255,255,255,.35)}
.av.wa{background:linear-gradient(150deg,#2FE07E,#14A85C)}
.av.tg{background:linear-gradient(150deg,#54C8F5,#1E96D6)}
.bm{flex:1;min-width:0}
.bm b{display:block;font-size:13px;font-weight:650;letter-spacing:-.01em;color:var(--ink);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.bm em{display:block;font-size:11px;margin-top:2px;font-style:normal;color:var(--ink2);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.cd{width:6px;height:6px;border-radius:50%;display:inline-block;vertical-align:middle;margin:-1px 4px 0 0}
.cd.wa{background:#25D366}.cd.tg{background:#2AABEE}
.sx{width:42px;height:25px;border-radius:999px;position:relative;flex-shrink:0;background:var(--swOn);
box-shadow:inset 0 1px 2px rgba(0,0,0,.14)}
.sx b{position:absolute;top:3px;right:3px;width:19px;height:19px;border-radius:50%;display:block;
background:#FFF;box-shadow:0 1px 3px rgba(0,0,0,.3)}
.sx.off{background:var(--swOff)}.sx.off b{left:3px;right:auto}
.bc{padding:12px 13px;display:flex;align-items:center;gap:11px;border-radius:14px;
background:var(--sf);border:1px solid var(--bd);box-shadow:var(--e1)}
.card{border-radius:14px;background:var(--sf);border:1px solid var(--bd);box-shadow:var(--e1)}
.ct{display:flex;align-items:center;gap:11px;padding:12px 13px}
.cv{height:1px;margin:0 13px;background:var(--hl)}
.cf{display:flex;justify-content:space-between;align-items:center;padding:9px 13px 11px;
font-size:11.5px;font-weight:600}
.cf .on{color:var(--goodInk)}.cf .of{color:var(--ink3)}
.ch{width:7px;height:7px;border-right:1.8px solid currentColor;border-bottom:1.8px solid currentColor;
display:inline-block;opacity:.5;flex-shrink:0;color:var(--ink2)}
.ch.r{transform:rotate(-45deg)}.ch.d{transform:rotate(45deg);margin-top:-3px}.ch.l{transform:rotate(135deg);margin-left:2px}
.on-pill{font-size:9.5px;font-weight:700;padding:4px 9px;border-radius:999px;background:var(--goodBg);
color:var(--goodInk);flex-shrink:0}
.seg{display:flex;border-bottom:1px solid var(--hl)}
.sg1{flex:1;text-align:center;font-size:9px;font-weight:600;padding:7px 0 8px;white-space:nowrap;
color:var(--ink3);border-bottom:2px solid transparent;margin-bottom:-1px}
.sg1.on{color:var(--ink);border-bottom-color:var(--acc);font-weight:700}
.fg{display:flex;flex-direction:column;gap:6px}
.fl{font-size:9.5px;font-weight:600;letter-spacing:.07em;text-transform:uppercase;padding:0 2px;color:var(--ink3)}
.wl{padding:11px 13px;border-radius:10px;font-size:12px;min-height:40px;display:flex;align-items:center;
justify-content:space-between;gap:8px;color:var(--ink);background:var(--sf);border:1px solid var(--bdI);box-shadow:var(--e1)}
.wl.a{align-items:flex-start;font-size:11.5px;line-height:1.45;min-height:58px}
.tr{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:10px 13px;
border-radius:14px;font-size:12px;font-weight:550;color:var(--ink);background:var(--sf);
border:1px solid var(--bd);box-shadow:var(--e1)}
.sv{margin-top:auto;text-align:center;padding:12px;border-radius:11px;font-size:13px;font-weight:700;
background:var(--saveBg);color:var(--saveInk);box-shadow:var(--e2)}
.tb{display:flex;padding:8px 8px 15px;flex-shrink:0;background:var(--sf);border-top:1px solid var(--hl)}
.tb a{flex:1;display:flex;flex-direction:column;align-items:center;gap:4px;font-size:9px;
font-weight:600;color:var(--ink3);text-decoration:none}
.tb a i{width:20px;height:20px;border-radius:6px;border:2px solid currentColor;display:block}
.tb a.on{color:var(--acc)}.tb a.on i{border-radius:7px}
.d-order{background:#16A75C}.d-owner{background:#E07612}.d-dialog{background:#3B82F6}
.d-silent{background:#8A94A6}.d-closed{background:#9B5DE0}
"""

def tokens_css(p):
    t = p["t"]
    sh = t["sh"]
    if p["fam"] == "dark":
        e1 = "0 1px 2px rgba(0,0,0,.5)"
        e2 = "0 1px 2px rgba(0,0,0,.55),0 10px 24px -10px rgba(0,0,0,.6)"
    else:
        e1 = f"0 1px 2px rgba({sh},.055)"
        e2 = (f"0 1px 2px rgba({sh},.055),0 4px 8px -2px rgba({sh},.05),"
              f"0 16px 28px -12px rgba({sh},.095)")
    m = {"--bgS": t["bg"], "--sf": t["sf"], "--hl": t["hl"], "--bd": t["bd"], "--bdI": t["bdI"],
         "--ink": t["ink"], "--ink2": t["ink2"], "--ink3": t["ink3"],
         "--acc": t["accInk"], "--accFill": t["acc"],
         "--chipBg": t["chipBg"], "--chipInk": t["chipInk"],
         "--saveBg": t["saveBg"], "--saveInk": t["saveInk"],
         "--swOn": t["swOn"], "--swOff": t["swOff"],
         "--goodBg": t["goodBg"], "--goodInk": t["goodInk"],
         "--e1": e1, "--e2": e2}
    body = "".join(f"{k}:{v};" for k, v in m.items())
    # spark + switch use the FILL colour, text/underline use the AA-verified ink
    return (f".p-{p['id']}{{{body}}}\n"
            f".p-{p['id']} .spk i:nth-child(n+6){{background:{t['acc']}}}\n")

# ----------------------------------------------------------------- screens
SB = ('<div class="sb"><span>9:41</span><span class="sg"><i></i><i></i><i></i><i></i>'
      '<span class="bt"><i></i></span></span></div>')

def tabs(active):
    out = []
    for k in ("Чаты", "Боты", "Сводка", "Профиль"):
        out.append(f'<a class="{"on" if k==active else ""}"><i></i>{k}</a>')
    return '<div class="tb">' + "".join(out) + '</div>'

DASH = SB + '''
<div class="ah"><h3>Сводка</h3><span class="hb">+</span></div>
<div class="bd">
 <div class="cs"><span class="cp on">7 дней</span><span class="cp">30 дней</span><span class="cp">Всё время</span></div>
 <div class="hero"><span class="l">Заказов собрано</span><span class="n">24<small>+8</small></span>
  <span class="s">за неделю · 62 диалога</span>
  <span class="spk"><i style="height:38%"></i><i style="height:52%"></i><i style="height:31%"></i><i style="height:64%"></i><i style="height:45%"></i><i style="height:78%"></i><i style="height:100%"></i></span></div>
 <div class="rws">
  <div class="rw"><span class="d d-owner"></span><span class="m">Нужен владелец</span><span class="c">3</span></div>
  <div class="rw"><span class="d d-dialog"></span><span class="m">В диалоге</span><span class="c">11</span></div>
  <div class="rw"><span class="d d-closed"></span><span class="m">Вопрос закрыт</span><span class="c">18</span></div>
 </div>
 <div class="bc"><span class="av wa">Ц</span>
  <span class="bm"><b>Цветы Алматы</b><em><i class="cd wa"></i>WhatsApp · Работает</em></span>
  <span class="sx"><b></b></span></div>
</div>''' + tabs("Сводка")

def bcard(av, ltr, name, chans, working):
    ch = '&nbsp;&nbsp;'.join(
        f'<i class="cd {c}"></i>{"WhatsApp" if c=="wa" else "Telegram"}' for c in chans)
    f = ('<span class="on">Бот работает</span><span class="sx"><b></b></span>' if working
         else '<span class="of">Бот на паузе</span><span class="sx off"><b></b></span>')
    return (f'<div class="card"><div class="ct"><span class="av {av}">{ltr}</span>'
            f'<span class="bm"><b>{name}</b><em>{ch}</em></span><i class="ch r"></i></div>'
            f'<div class="cv"></div><div class="cf">{f}</div></div>')

BOTS = SB + '<div class="ah"><h3>Боты</h3><span class="hb">+</span></div><div class="bd">' \
     + bcard("wa", "Ц", "Цветы Алматы", ["wa"], True) \
     + bcard("wa", "А", "Автозапчасти 4х4", ["wa", "tg"], True) \
     + bcard("tg", "У", "Учебный центр", ["tg"], False) \
     + '</div>' + tabs("Боты")

SET = SB + '''
<div class="ah s"><span class="hb"><i class="ch l"></i></span><h3>Цветы Алматы</h3>
 <span class="on-pill">Онлайн</span></div>
<div class="bd">
 <div class="seg"><span class="sg1 on">Общие</span><span class="sg1">Бизнес</span><span class="sg1">Товары</span><span class="sg1">Услуги</span><span class="sg1">Промпты</span></div>
 <div class="fg"><span class="fl">Название бота</span><div class="wl">Цветы Алматы</div></div>
 <div class="fg"><span class="fl">Тип бизнеса</span><div class="wl">Цветы и подарки<i class="ch d"></i></div></div>
 <div class="fg"><span class="fl">Приветствие</span><div class="wl a">Здравствуйте! Это «Цветы Алматы» 🌸 Подскажу букет, цену и доставку.</div></div>
 <div class="tr">Уведомления о заказах<span class="sx"><b></b></span></div>
 <div class="sv">Сохранить</div>
</div>'''

def ph(html, cap):
    return f'<figure class="pw"><div class="phone"><div class="scr">{html}</div></div><figcaption class="pc">{cap}</figcaption></figure>'

FAMS = {
 "light": ("Светлые палитры", "Five light grounds. The neutral itself is the biggest lever here — "
           "cool, achromatic and warm neutrals feel like different products before the accent even lands."),
 "dark":  ("Тёмные палитры", "Three dark grounds. On black, borders do the separating that shadows "
           "cannot, and every accent has to lift in luminance to survive as text."),
}

def swatch_row(p):
    t = p["t"]
    picks = [(t["bg"], "ground"), (t["sf"], "surface"), (t["bd"], "border"),
             (t["acc"], "accent"), (t["ink"], "ink")]
    return "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>' for h, l in picks)

def cx_table(res):
    body = "".join(
        f'<tr><td>{lbl}</td><td class="{"ok" if ok else ""}">{r}</td></tr>'
        for lbl, r, need, ok in res)
    return f'<table class="cx"><caption>Measured contrast</caption><tbody>{body}</tbody></table>'

def build(rows):
    css = PAGE_CSS + PHONE_CSS + "".join(tokens_css(p) for p in P)
    out = ['<title>Refined Modern — Eight Palettes</title>', f'<style>{css}</style>', '<div class="page">']
    out.append('''<header class="mast">
 <p class="eb">Refined Modern · восемь палитр · 2026</p>
 <h1>One material. <em>Eight ways to mean something.</em></h1>
 <p class="lede">Every screen below is the <strong>same Refined Modern build</strong> — identical hairlines,
 identical three-layer elevation, identical radius and type. Only the token file changes. That is the whole
 promise of the style: a palette is a swap, not a rewrite.</p>
 <p class="lede">Contrast is <strong>computed, not claimed</strong>. Each palette lists its measured WCAG
 ratios; every accent carries a separate verified <em>text</em> shade so it can be a link and a button fill
 without failing either job. WhatsApp green, Telegram blue and the five outcome-status hues are held constant
 across all eight — identity and meaning never follow fashion.</p>
</header>''')

    fam_seen = set()
    for p in P:
        if p["fam"] not in fam_seen:
            fam_seen.add(p["fam"])
            ttl, dsc = FAMS[p["fam"]]
            out.append(f'<section class="fam"><h2>{ttl}</h2><p>{dsc}</p></section>')
        out.append(f'''<section class="pal p-{p['id']}">
 <div class="ph">
  <div>
   <p class="eb">{p['n']:02d} — палитра</p>
   <h3>{p['ru']} <span>— {p['en']}</span></h3>
   <p class="blurb">{p['blurb']}</p>
   <p class="rk"><b>Risk</b><br>{p['risk']}</p>
   <div class="swr">{swatch_row(p)}</div>
  </div>
  <div>{cx_table(rows[p['id']])}</div>
 </div>
 <div class="row">{ph(DASH,"Сводка")}{ph(BOTS,"Боты")}{ph(SET,"Настройки")}</div>
</section>''')

    out.append('''<section class="close">
 <h2>How to pick</h2>
 <p>Judge the <strong>Настройки</strong> phone hardest. A dashboard flatters any palette — big numbers, lots of
 white space. A dense form is where a neutral turns muddy, where a border either reads or disappears, and where
 an accent has to survive being small.</p>
 <p><strong>The neutral matters more than the accent.</strong> Cool, achromatic and warm grounds already feel
 like three different companies before you notice the colour on the button. Pick the ground first, then the accent.</p>
 <p><strong>On «Алматы»:</strong> if crimson is the brand colour, destructive actions cannot also be red. That
 palette moves delete to an outlined treatment with a darker maroon, so «Удалить» never wears the same skin as
 «Сохранить». Any palette whose accent collides with a semantic colour needs this kind of ruling written down.</p>
 <p>Once chosen, the palette becomes one token block in <code>Assets/Scripts/Theme/</code>, and every
 <code>[MenuItem]</code> builder reads it. Swapping later costs one file, not fifty-five.</p>
</section></div>''')
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("".join(out))
    return css

def contact_sheet(css):
    os.makedirs(os.path.dirname(SHEETOUT), exist_ok=True)
    cells = "".join(
        f'<figure class="pw"><div class="phone p-{p["id"]}"><div class="scr">{DASH}</div></div>'
        f'<figcaption class="pc">{p["n"]:02d} · {p["en"]}</figcaption></figure>'
        for p in P)
    html = (f'<title>sheet</title><style>{css}'
            '.grid{display:grid;grid-template-columns:repeat(4,auto);gap:26px 22px;'
            'justify-content:center;padding:26px}</style>'
            f'<div class="page"><div class="grid">{cells}</div></div>')
    with open(SHEETOUT, "w", encoding="utf-8") as f:
        f.write(html)

derive()
rows, bad = audit()
css = build(rows)
contact_sheet(css)
print(f"wrote {OUT}")
print(f"palettes: {len(P)}  checks/palette: {len(CHECKS)}  total: {len(P)*len(CHECKS)}")
if bad:
    print(f"\n*** {len(bad)} CONTRAST FAILURES ***")
    for b in bad: print("  ", b)
    sys.exit(1)
print("\nAll contrast checks PASS.")
