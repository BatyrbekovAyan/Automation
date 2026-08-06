#!/usr/bin/env python3
"""Chat thread (two outgoing-bubble treatments) + chats list, for
«Петроль» × «Мята» and «Графит» × «Мята».

The A/B: outgoing bubble derived from the BRAND vs kept at WHATSAPP GREEN.
Brand-derived means the app owns the colour at the centre of its busiest screen.
WhatsApp green means every Kazakh user already reads it as "my message".
"""
import os, sys, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
OUT  = os.path.join(HERE, "chat-preview.html")
SOLO = os.path.join(HERE, "_chat")

spec = importlib.util.spec_from_file_location("gm", os.path.join(HERE, "gen-mix.py"))
gm = importlib.util.module_from_spec(spec); spec.loader.exec_module(gm)
gd, gp = gm.gd, gm.gp
dE, oklch, ratio, tune = gd.dE, gd.oklch, gd.ratio, gd.tune

# what the app / WhatsApp actually use today
WA_BUBBLE_LIGHT = "#C5EEB6"     # the project's current outgoing bubble
WA_BUBBLE_DARK  = "#005C4B"     # WhatsApp's own dark-theme outgoing bubble

def mix(a, b, t):
    ra, ga, ba = gp._rgb(a); rb, gb, bb = gp._rgb(b)
    return "#%02X%02X%02X" % (round(ra+(rb-ra)*t), round(ga+(gb-ga)*t), round(ba+(bb-ba)*t))

def chat_tokens(p):
    t = p["t"]; dark = p["fam"] == "dark"; acc = t["acc"]
    if dark:
        wall   = mix(t["bg"], "#000000", 0.35)
        bubIn  = mix(t["sf"], "#FFFFFF", 0.06)
        bubOut = mix(acc, t["bg"], 0.62)
        bubWa  = WA_BUBBLE_DARK
    else:
        wall   = mix(t["bg"], "#EFEADF", 0.55)
        bubIn  = "#FFFFFF"
        bubOut = mix(acc, "#FFFFFF", 0.86)
        bubWa  = WA_BUBBLE_LIGHT
    # bubbles must read as objects ON the paper; darken the paper, never wash the bubble
    for _ in range(40):
        if all(ratio(b, wall) >= 1.18 for b in (bubIn, bubOut, bubWa)): break
        wall = mix(wall, "#000000", 0.04)
    d = dict(wall=wall, bubIn=bubIn, bubOut=bubOut, bubWa=bubWa,
             ink=tune(t["ink"], bubOut, 4.5),   inkWa=tune(t["ink"], bubWa, 4.5),
             inkIn=tune(t["ink"], bubIn, 4.5),
             meta=tune(t["ink3"], bubOut, 4.5), metaWa=tune(t["ink3"], bubWa, 4.5),
             metaIn=tune(t["ink3"], bubIn, 4.5),
             tick=tune("#2AABEE", bubOut, 3.0), tickWa=tune("#2AABEE", bubWa, 3.0),
             quoteBar=acc, quoteBg=mix(bubIn, acc, 0.16 if dark else 0.10),
             sugBg=mix(t["sf"], acc, 0.18 if dark else 0.10),
             sugBd=mix(t["bd"], acc, 0.35),
             badge=acc, badgeInk=tune("#FFFFFF" if oklch(acc)[0] < 0.62 else "#12100A", acc, 4.5),
             segOn=acc, segOnInk=tune("#FFFFFF" if oklch(acc)[0] < 0.62 else "#12100A", acc, 4.5))
    return d

CHAT_CSS = r"""
.chatwall{flex:1;overflow:hidden;padding:8px 12px 6px;display:flex;flex-direction:column;
gap:6px;background:var(--wall)}
.dsep{align-self:center;font-size:9.5px;font-weight:600;padding:3px 11px;border-radius:999px;
background:var(--bubIn);color:var(--metaIn);box-shadow:var(--e1);margin:2px 0 4px}
.msg{max-width:78%;padding:7px 10px 5px;border-radius:13px;font-size:12px;line-height:1.38;
box-shadow:var(--e1);position:relative}
.msg.in{align-self:flex-start;background:var(--bubIn);color:var(--inkIn);border-bottom-left-radius:4px}
.msg.out{align-self:flex-end;background:var(--bubOut);color:var(--ink);border-bottom-right-radius:4px}
.wa .msg.out{background:var(--bubWa);color:var(--inkWa)}
.msg .mt{display:flex;align-items:center;justify-content:flex-end;gap:3px;font-size:9px;
margin-top:2px;line-height:1}
.msg.in .mt{color:var(--metaIn)}
.msg.out .mt{color:var(--meta)}
.wa .msg.out .mt{color:var(--metaWa)}
.msg .tk{display:inline-flex;gap:1px}
.msg .tk i{width:7px;height:4px;border-left:1.4px solid var(--tick);border-bottom:1.4px solid var(--tick);
transform:rotate(-45deg);display:block}
.wa .msg .tk i{border-color:var(--tickWa)}
.quo{border-left:2.5px solid var(--quoteBar);background:var(--quoteBg);border-radius:5px;
padding:4px 7px;margin-bottom:4px}
.quo b{display:block;font-size:10px;font-weight:700;color:var(--quoteBar);margin-bottom:1px}
.quo span{font-size:10.5px;opacity:.75}
.rct{position:absolute;bottom:-9px;left:9px;background:var(--bubIn);border:1px solid var(--hl);
border-radius:999px;padding:1px 6px;font-size:9px;box-shadow:var(--e1);white-space:nowrap}
.sugwrap{display:flex;flex-direction:column;gap:5px;margin:8px 0 2px}
.sughd{font-size:9px;font-weight:700;letter-spacing:.09em;text-transform:uppercase;
color:var(--accTx);padding-left:2px}
.sug{background:var(--sugBg);border:1px solid var(--sugBd);border-radius:11px;padding:7px 10px;
font-size:11.5px;line-height:1.35;color:var(--inkIn)}
.sug.top{border-color:var(--accTx);box-shadow:var(--e1)}
.comp{display:flex;align-items:center;gap:8px;padding:8px 11px 14px;flex-shrink:0;
background:var(--sf);border-top:1px solid var(--hl)}
.comp .att{width:26px;height:26px;flex-shrink:0;border-radius:50%;border:1.5px solid var(--ink3);
position:relative}
.comp .att::before,.comp .att::after{content:"";position:absolute;background:var(--ink3);
left:50%;top:50%;transform:translate(-50%,-50%)}
.comp .att::before{width:11px;height:1.5px}.comp .att::after{width:1.5px;height:11px}
.comp .fld{flex:1;min-width:0;padding:7px 12px;border-radius:999px;background:var(--bgS);
border:1px solid var(--bdI);font-size:11.5px;color:var(--ink3)}
.comp .snd{width:30px;height:30px;flex-shrink:0;border-radius:50%;background:var(--saveBg);
display:grid;place-items:center}
.comp .snd i{width:0;height:0;border-left:9px solid var(--saveInk);border-top:5.5px solid transparent;
border-bottom:5.5px solid transparent;margin-left:2px;display:block}
.chathd{display:flex;align-items:center;gap:9px;padding:8px 14px 10px;flex-shrink:0;
background:var(--sf);border-bottom:1px solid var(--hl)}
.chathd .bk{width:8px;height:8px;border-left:1.8px solid var(--ink2);border-bottom:1.8px solid var(--ink2);
transform:rotate(45deg);flex-shrink:0;margin-right:2px}
.chathd .av{width:30px;height:30px;border-radius:50%;flex-shrink:0;display:grid;place-items:center;
font-size:12px;font-weight:700;color:#FFF;background:linear-gradient(150deg,#2FE07E,#14A85C)}
.chathd .who{flex:1;min-width:0}
.chathd .who b{display:block;font-size:13px;font-weight:650;color:var(--ink);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.chathd .who span{font-size:10px;color:var(--accTx)}

/* ---- chats list ---- */
.botsw{display:flex;align-items:center;gap:8px;margin:0 14px 9px;padding:8px 11px;border-radius:12px;
background:var(--sf);border:1px solid var(--bd);box-shadow:var(--e1)}
.botsw .bav{width:26px;height:26px;border-radius:8px;flex-shrink:0;display:grid;place-items:center;
font-size:11px;font-weight:700;color:#FFF;background:linear-gradient(150deg,#2FE07E,#14A85C)}
.botsw .bn{flex:1;min-width:0;font-size:12.5px;font-weight:650;color:var(--ink);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.botsw .bn em{display:block;font-style:normal;font-size:10px;font-weight:500;color:var(--ink3);margin-top:1px}
.rmode{display:flex;gap:3px;margin:0 14px 9px;padding:3px;border-radius:11px;background:var(--bgS);
border:1px solid var(--hl)}
.rmode span{flex:1;text-align:center;font-size:10px;font-weight:650;padding:5px 0;border-radius:8px;
color:var(--ink3)}
.rmode span.on{background:var(--segOn);color:var(--segOnInk)}
.csearch{margin:0 14px 8px;padding:7px 12px;border-radius:999px;background:var(--bgS);
border:1px solid var(--bdI);font-size:11.5px;color:var(--ink3)}
.clist{flex:1;overflow:hidden;display:flex;flex-direction:column}
.crow{display:flex;align-items:center;gap:10px;padding:9px 14px}
.crow+.crow{border-top:1px solid var(--hl)}
.crow .cav{width:38px;height:38px;border-radius:50%;flex-shrink:0;display:grid;place-items:center;
font-size:14px;font-weight:700;color:#FFF}
.crow .cmain{flex:1;min-width:0}
.crow .cmain b{display:block;font-size:12.5px;font-weight:650;color:var(--ink);
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.crow .cmain span{display:block;font-size:11px;color:var(--ink3);margin-top:2px;
white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.crow .cmeta{display:flex;flex-direction:column;align-items:flex-end;gap:4px;flex-shrink:0}
.crow .cmeta i{font-size:9.5px;font-style:normal;color:var(--ink3)}
.crow .cbadge{min-width:17px;height:17px;padding:0 5px;border-radius:999px;background:var(--badge);
color:var(--badgeInk);font-size:9.5px;font-weight:700;display:grid;place-items:center}
.crow .ctick{display:inline-flex;gap:1px;margin-right:3px;vertical-align:middle}
.crow .ctick i{width:6px;height:3.5px;border-left:1.3px solid var(--accTx);
border-bottom:1.3px solid var(--accTx);transform:rotate(-45deg);display:block}
"""

SB = ('<div class="sb"><span>9:41</span><span class="sg"><i></i><i></i><i></i><i></i>'
      '<span class="bt"><i></i></span></span></div>')

def chat_html(p, wa=False):
    return (SB +
     '<div class="chathd"><span class="bk"></span><span class="av">А</span>'
     '<span class="who"><b>Айгерим</b><span>бот отвечает</span></span></div>'
     f'<div class="chatwall{" wa" if wa else ""}">'
       '<span class="dsep">Сегодня</span>'
       '<div class="msg in">Здравствуйте! Букет из 25 роз есть в наличии?'
         '<span class="mt">10:24</span></div>'
       '<div class="msg out">Здравствуйте! Да, есть — красные и розовые. '
         '25 роз — 18 500 ₸, доставка по городу 1 500 ₸.'
         '<span class="mt">10:24<span class="tk"><i></i><i></i></span></span></div>'
       '<div class="msg in"><div class="quo"><b>Цветы Алматы</b>'
         '<span>25 роз — 18 500 ₸, доставка…</span></div>'
         'А можно сегодня к 18:00 на Абая?<span class="mt">10:26</span>'
         '<span class="rct">👍 1</span></div>'
       '<div class="sugwrap"><span class="sughd">Бот предлагает ответ</span>'
         '<div class="sug top">Да, успеем к 18:00. Уточните номер дома на Абая?</div>'
         '<div class="sug">К 18:00 доставим. Оплата картой или наличными?</div></div>'
     '</div>'
     '<div class="comp"><span class="att"></span>'
       '<span class="fld">Сообщение</span><span class="snd"><i></i></span></div>')

AV = ("linear-gradient(150deg,#F3A26D,#D9722F)", "linear-gradient(150deg,#7FC4F5,#3B82C4)",
      "linear-gradient(150deg,#B79BE8,#7B57C9)", "linear-gradient(150deg,#8FD8A8,#3E9B62)",
      "linear-gradient(150deg,#F0A0B4,#CC5C7B)")

ROWS = [("А","Айгерим","Да, успеем к 18:00. Уточните…","10:26","2",False),
        ("Д","Даурен","Спасибо! Заеду завтра","09:41","",True),
        ("М","Марат Ж.","Какая цена на 51 розу?","Вчера","1",False),
        ("Г","Гүлнұр","Отправила адрес доставки","Вчера","",True),
        ("С","Сауле","Добрый день! Можно счёт?","Пн","",False)]

def chats_html(p):
    rows = ""
    for i, (ltr, name, prev, tm, unread, own) in enumerate(ROWS):
        tick = '<span class="ctick"><i></i><i></i></span>' if own else ""
        badge = f'<span class="cbadge">{unread}</span>' if unread else '<span></span>'
        rows += (f'<div class="crow"><span class="cav" style="background:{AV[i]}">{ltr}</span>'
                 f'<span class="cmain"><b>{name}</b><span>{tick}{prev}</span></span>'
                 f'<span class="cmeta"><i>{tm}</i>{badge}</span></div>')
    return (SB +
     '<div class="ah"><h3>Чаты</h3><span class="hb">+</span></div>'
     '<div class="botsw"><span class="bav">Ц</span>'
       '<span class="bn">Цветы Алматы<em>WhatsApp · 62 диалога</em></span>'
       '<i class="ch d"></i></div>'
     '<div class="rmode"><span class="on">Авто</span><span>Вместе</span></div>'
     '<div class="csearch">Поиск</div>'
     f'<div class="clist">{rows}</div>'
     '<div class="tb"><a class="on"><i></i>Чаты</a><a><i></i>Боты</a>'
     '<a><i></i>Сводка</a><a><i></i>Профиль</a></div>')

def tokens_css(p):
    c = p["chat"]
    extra = "".join(f"--{k}:{v};" for k, v in c.items()) + f'--accTx:{p["t"]["accInk"]};'
    return gd.tokens_css(p) + f'.p-{p["id"]}{{{extra}}}\n'

def main():
    gm.ACCENTS = gm.ACCENTS_B
    P = [gm.build_palette("petrol", "mint", 1), gm.build_palette("graphite", "mint", 2)]
    for p in P: p["chat"] = chat_tokens(p)

    bad = []
    for p in P:
        c = p["chat"]
        for lbl, fg, bg, need in (
            ("brand-bubble text",  c["ink"],   c["bubOut"], 4.5),
            ("WA-bubble text",     c["inkWa"], c["bubWa"],  4.5),
            ("incoming text",      c["inkIn"], c["bubIn"],  4.5),
            ("timestamp / brand",  c["meta"],  c["bubOut"], 4.5),
            ("timestamp / WA",     c["metaWa"],c["bubWa"],  4.5),
            ("unread badge label", c["badgeInk"], c["badge"], 4.5),
            ("brand bubble / wall",c["bubOut"],c["wall"],   1.18),
            ("WA bubble / wall",   c["bubWa"], c["wall"],   1.18),
            ("in bubble / wall",   c["bubIn"], c["wall"],   1.18)):
            r = ratio(fg, bg)
            if r < need: bad.append(f'{p["ru"]}: {lbl} = {r} (need {need})')
        print(f'  {p["ru"]:<26} brand-bubble {c["bubOut"]}  WA-bubble {c["bubWa"]}  '
              f'dE {dE(c["bubOut"], c["bubWa"]):.1f}')

    css = gp.PAGE_CSS + gp.PHONE_CSS + gd.EXTRA_CSS + CHAT_CSS + "".join(tokens_css(p) for p in P)
    o = ['<title>Чат и список чатов — «Мята»</title>', f'<style>{css}</style>', '<div class="page">']
    o.append('''<header class="mast">
 <p class="eb">Refined Modern · чат · список чатов · «Мята»</p>
 <h1>Whose green is the message?</h1>
 <p class="lede">Left, the outgoing bubble derived from <strong>your brand</strong>: the app owns the colour
 at the centre of its busiest screen. Middle, the bubble kept at <strong>WhatsApp green</strong> — the colour
 every Kazakh user already reads as "my message", at the cost of your product looking like a WhatsApp skin.
 Right, the chats list, where the unread badge is the only place the brand appears at all.</p>
</header>''')
    for p in P:
        c = p["chat"]
        sw = "".join(f'<span class="sw"><i style="background:{h}"></i>{h}<br>{l}</span>'
                     for h, l in ((c["wall"],"wallpaper"), (c["bubOut"],"brand bubble"),
                                  (c["bubWa"],"WhatsApp bubble"), (c["bubIn"],"incoming"),
                                  (p["t"]["acc"],"brand"), (c["badge"],"unread")))
        o.append(f'''<section class="pal p-{p['id']}">
 <div class="ph"><div>
  <p class="eb">{p['idx']:02d}</p><h3>{p['ru']}</h3>
  <p class="blurb">{p['blurb']}</p>
  <div class="swr">{sw}</div>
 </div><div></div></div>
 <div class="row">{gp.ph(chat_html(p), "Чат · бренд")}{gp.ph(chat_html(p, wa=True), "Чат · WhatsApp")}
 {gp.ph(chats_html(p), "Список чатов")}</div>
</section>''')
    o.append('''<section class="close">
 <h2>The trade</h2>
 <p><strong>Brand bubble</strong> — the app looks like itself, the outgoing colour ties the thread to the
 rest of the product, and on the dark ground it reads as a filled shape rather than a colour. Cost: you give
 up an instantly-recognised convention on the screen where a nervous owner most wants familiarity.</p>
 <p><strong>WhatsApp bubble</strong> — zero learning, total familiarity, and the thread is unmistakably a
 WhatsApp conversation. Cost: the busiest screen in your product is wearing someone else's colour, and on a
 green-accented palette the two greens sit side by side and compete.</p>
 <p><strong>On the chats list the question mostly disappears</strong> — the brand appears only in the unread
 badge and the «Авто» mode pill, so the screen stays calm whichever way the bubble goes. That is an argument
 for deciding the bubble on the thread alone.</p>
</section></div>''')
    open(OUT, "w", encoding="utf-8").write("".join(o))

    os.makedirs(SOLO, exist_ok=True)
    for p in P:
        open(os.path.join(SOLO, f'{p["id"]}.html'), "w", encoding="utf-8").write(
            f'<title>{p["id"]}</title><style>{css}</style><div class="page" style="padding:14px 0">'
            f'<section class="pal p-{p["id"]}" style="border:none">'
            f'<div style="padding:0 26px 12px"><h3 style="margin:0;font-weight:750;font-size:22px">'
            f'{p["ru"]}</h3></div><div class="row" style="padding:0 26px">'
            f'{gp.ph(chat_html(p),"Чат · бренд")}{gp.ph(chat_html(p,wa=True),"Чат · WhatsApp")}'
            f'{gp.ph(chats_html(p),"Список чатов")}</div></section></div>')
    print(f"\nwrote {OUT}")
    if bad:
        print(f"\n*** {len(bad)} FAILURES ***")
        for b in bad: print("  ", b)
        sys.exit(1)
    print("All chat + list surface checks PASS.")

if __name__ == "__main__":
    main()
