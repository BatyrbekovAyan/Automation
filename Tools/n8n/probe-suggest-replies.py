#!/usr/bin/env python3
"""Pre-deploy smoke probes for the Suggest Replies workflow (2026-08 audit harness).

Run BEFORE and AFTER every prompt/Prep/Assemble change to the canonical
9PTyYcelRQI7bGDb-Suggest_Replies.json, against the LOCAL dev instance:

    python3 Tools/n8n/probe-suggest-replies.py            # http://localhost:5678
    N8N_BASE_URL=... python3 Tools/n8n/probe-suggest-replies.py

Each probe POSTs a realistic payload and prints the returned cards. Checks come in two
severities: STRUCT fails the run with exit 1 — a "cards" probe must return 1–4 cards
(variable count is the 2026-08-11 contract; >4 or an unexpected abstain is structural),
an "abstain" probe must return the empty abstain envelope; heuristic checks print
OK/WARN only — LLM output is sampled at temperature 0.4, so treat a WARN as "re-run and
read the cards", not as a hard regression. The probe matrix encodes the 2026-08 audit
failure modes (docs/design/suggestions-audit-2026-08.md): fabricated contacts/hours
(B/B2/J/J2), false-negative availability (A/K), media over-confidence (D), steer echo
(H), language mirroring + greeting spam (G), filler-card padding (F), non-business
abstain (N).
Drill redesign (2026-08-18): H2 drills within a steer, F expects 4 trivial variants,
STRUCT gates move∈enum + distinct short titles.

This workflow only calls OpenAI and returns JSON — probing it never messages anyone.
"""
import json
import os
import re
import sys
import time
import urllib.request

BASE = os.environ.get("N8N_BASE_URL", "http://localhost:5678").rstrip("/")
URL = BASE + "/webhook/SuggestReplies"

MOVES = ("Ответ", "Уточнить", "Вариант", "К заказу", "Отложить", "Отказ")

KNOW = ("About Business:\nЦветочный магазин в Астане, букеты и композиции на заказ.\n\n"
        "Контакты:\nТелефон: +7 777 123 45 67\nЧасы работы: 10:00–20:00\nАдрес: ул. Абая 1, Астана")
FLOWER_CAT = "• Букет 25 роз — 25000 тг\n• Букет пионов — 18000 тг"
KASPI_CAT = "• Наушники JBL Tune 510 — 18990 тг"
REPAIR_CAT = "• Замена экрана iPhone 11 — 35000 тг\n• Замена АКБ iPhone 11-13 — 15000 тг"
AUTO_CAT = ("• Колодки тормозные передние, арт. 04465-02220 — 18500 тг\n"
            "• Фильтр масляный, арт. 90915-YZZE1 — 3200 тг")


def base(vertical, name, catalog, msgs, know="", now="", last=None, steer=None):
    return {"v": 1, "requestSeq": 1, "profileId": "probe", "chatId": "probe@c.us",
            "botWaId": "-1", "botTgId": "-1", "channel": "whatsapp",
            "businessTypeId": vertical, "businessName": name, "ownerPrompt": "",
            "catalog": catalog, "steerTowardText": steer, "lastIncomingText": last,
            "messages": msgs, "businessKnowledge": know, "now": now}


def m(role, text):
    return {"role": role, "text": text, "ts": 1754800000}


STEER = "Возьмите букет из 25 роз за 25000 тг — классика на годовщину. Могу оформить доставку на завтра 🌹"

PROBES = [
    ("B_addr_grounded", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "какой у вас адрес и до скольки вы сегодня работаете?")],
        know=KNOW, now="2026-08-10 14:00, воскресенье"), [
        ("real_addr_used", lambda c: any("Абая" in x["text"] for x in c)),
        ("real_hours_used", lambda c: any("20:00" in x["text"] for x in c)),
    ]),
    ("B2_addr_ungrounded", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "какой у вас адрес и до скольки вы работаете?")]), [
        ("no_fabricated_addr_or_hours", lambda c: not any(
            re.search(r"ул\.|улица|проспект|до \d{1,2}:\d{2}|работаем до", x["text"].lower()) for x in c)),
    ]),
    ("J_hours_open_now", base("phone_repair", "RemPhone", REPAIR_CAT,
        [m("client", "вы сейчас работаете? можно подъехать?")],
        know=KNOW.replace("Цветочный магазин в Астане, букеты и композиции на заказ.", "Ремонт телефонов."),
        now="2026-08-10 14:00, воскресенье"), [
        ("grounded_open_answer", lambda c: any("20:00" in x["text"] or "работаем" in x["text"].lower() for x in c)),
    ]),
    ("J2_hours_closed_now", base("phone_repair", "RemPhone", REPAIR_CAT,
        [m("client", "вы сейчас работаете? можно подъехать?")],
        know=KNOW, now="2026-08-10 23:40, воскресенье"), [
        ("not_claiming_open", lambda c: not re.search(r"да, (мы )?работаем|можете подъехать сейчас", c[0]["text"].lower())),
        ("mentions_reopen", lambda c: any(re.search(r"завтра|с 10|10:00|закрыт", x["text"].lower()) for x in c)),
    ]),
    ("K_kaspi_installment", base("kaspi_seller", "TechStore KZ", KASPI_CAT,
        [m("client", "наушники jbl можно в рассрочку на 12 мес?")]), [
        ("no_installment_denial", lambda c: not any(
            re.search(r"нет возможности|нет рассрочки|не оформ", x["text"].lower()) for x in c)),
        ("routes_via_kaspi", lambda c: any("Kaspi" in x["text"] for x in c)),
    ]),
    # P: the niche-prompt probe (2026-08-11). A bare part name matches a catalog line, so
    # without the auto_parts niche block the model happily quotes 18500 тг. The vertical's
    # hard rule is: no price before марка/модель/год is known.
    ("P_autoparts_intake", base("auto_parts", "АвтоЗапчасти KZ", AUTO_CAT,
        [m("client", "колодки передние сколько стоят?")]), [
        ("card1_clarifies", lambda c: c[0].get("move") == "Уточнить"),
        ("card1_asks_car", lambda c: re.search(r"марк|модел|год|vin", c[0]["text"].lower()) is not None),
        ("no_price_before_car_known", lambda c: "18500" not in c[0]["text"]),
    ]),
    ("A_absent_item", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "здравствуйте"), m("business", "Добрый день!"),
         m("client", "а тюльпаны есть? сколько стоят?")]), [
        ("no_flat_stock_denial", lambda c: not any(
            re.search(r"тюльпан\w*[^.]{0,20}нет в наличии", x["text"].lower()) for x in c)),
    ]),
    ("D_photo_no_text", base("kaspi_seller", "TechStore KZ", KASPI_CAT,
        [m("client", "[фото] такой есть в наличии?")]), [
        ("card1_clarifies", lambda c: c[0].get("move") == "Уточнить"),
        ("no_yes_claim_about_photo", lambda c: not re.search(r"^да, ", c[0]["text"].lower())),
    ]),
    ("H_steer_recluster", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "нужен букет жене на годовщину, что посоветуете?")], steer=STEER), [
        ("card1_not_verbatim_echo", lambda c: c[0]["text"].strip() != STEER),
    ]),
    ("H2_drill_within_direction", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "нужен букет жене на годовщину, что посоветуете?")], steer=STEER), [
        ("four_cards", lambda c: len(c) == 4),
        ("titles_not_move_names", lambda c: not any(x["label"] in MOVES for x in c)),
        ("stays_on_the_roses_offer", lambda c: sum(
            1 for x in c if re.search(r"роз|букет|годовщин|доставк", x["text"].lower())) >= 3),
    ]),
    ("G_kazakh_mirror", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "сәлеметсіз бе, 25 раушан гүлінен жасалған букет қанша тұрады?")]), [
        ("whole_cards_kazakh", lambda c: sum(
            1 for x in c if re.search(r"[әіңғүұқөһ]", x["text"].lower())) >= 3),
        ("max_one_greeting", lambda c: sum(
            1 for x in c if re.match(r"^(сәлем|салем|здравствуй|добрый)", x["text"].strip().lower())) <= 1),
    ]),
    ("E_angry_where_order", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "заказала букет 25 роз на сегодня к 14:00"),
         m("business", "Приняли! Курьер будет к 14:00."),
         m("client", "уже 15:30!!! где мой заказ??? я вам два раза писала!!")]), [
        ("no_upsell_to_angry", lambda c: not re.search(r"могу предложить|как вам такая идея", c[0]["text"].lower())),
    ]),
    ("F_trivial_thanks_variants", base("education", "SmartKids", "• Английский, группа (мес) — 20000 тг",
        [m("client", "сколько стоит английский для ребенка?"),
         m("business", "Группа — 20000 тг/мес."),
         m("client", "спасибо")]), [
        ("four_variant_cards", lambda c: len(c) == 4),
        ("distinct_titles", lambda c: len({x["label"].lower() for x in c}) == len(c)),
    ]),
]

# Non-business messages must return the deliberate abstain envelope (empty suggestions,
# abstain=true, no error) — the client renders the quiet «Нет предложений» state.
ABSTAIN_PROBES = [
    ("N_personal_abstain", base("flowers", "Цветы Астана", FLOWER_CAT,
        [m("client", "братан ну что, идём сегодня на футбол вечером? все наши собираются")])),
]


def post(payload):
    req = urllib.request.Request(URL, json.dumps(payload).encode("utf-8"),
                                 {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=40) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main():
    struct_fails = 0
    warns = 0
    for name, payload, checks in PROBES:
        t0 = time.time()
        try:
            body = post(payload)
        except Exception as e:
            print(f"\n### {name} REQUEST-ERROR {e}")
            struct_fails += 1
            continue
        cards = body.get("suggestions") or []
        print(f"\n### {name} ({time.time() - t0:.1f}s)" + ("" if cards else f"  ERR:{body.get('error')} abstain:{body.get('abstain')}"))
        for c in cards:
            print(f"  [{c.get('label', '?')}/{c.get('move', '?')}] {c.get('text', '')}")
        if not 1 <= len(cards) <= 4 or body.get("abstain"):
            print("  !! STRUCT-FAIL: expected 1-4 cards without abstain")
            struct_fails += 1
            continue
        bad_move = [c for c in cards if c.get("move") not in MOVES]
        if bad_move:
            print(f"  !! STRUCT-FAIL: move outside the enum: {[c.get('move') for c in bad_move]}")
            struct_fails += 1
            continue
        titles = [str(c.get("label", "")).strip() for c in cards]
        if any(not t or len(t) > 24 for t in titles) or len({t.lower() for t in titles}) != len(titles):
            print(f"  !! STRUCT-FAIL: labels must be non-empty, <=24 chars, distinct: {titles}")
            struct_fails += 1
            continue
        for label, fn in checks:
            try:
                ok = bool(fn(cards))
            except Exception:
                ok = False
            print(f"  {'OK  ' if ok else 'WARN'} {label}")
            warns += 0 if ok else 1
    for name, payload in ABSTAIN_PROBES:
        t0 = time.time()
        try:
            body = post(payload)
        except Exception as e:
            print(f"\n### {name} REQUEST-ERROR {e}")
            struct_fails += 1
            continue
        cards = body.get("suggestions") or []
        print(f"\n### {name} ({time.time() - t0:.1f}s) abstain:{body.get('abstain')} cards:{len(cards)}")
        for c in cards:
            print(f"  [{c.get('label', '?')}/{c.get('move', '?')}] {c.get('text', '')}")
        ok = body.get("abstain") is True and len(cards) == 0 and not body.get("error")
        print(f"  {'OK  ' if ok else 'WARN'} abstain_envelope")
        warns += 0 if ok else 1
    print(f"\nprobes done: struct_fails={struct_fails}, heuristic_warns={warns}")
    sys.exit(1 if struct_fails else 0)


if __name__ == "__main__":
    main()
