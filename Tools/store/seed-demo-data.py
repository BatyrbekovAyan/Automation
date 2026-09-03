#!/usr/bin/env python3
"""Seed fabricated demo data for App Store screenshots — Unity Editor or iOS Simulator.

Writes the app's PlayerPrefs and its on-disk caches so that the Боты / Чаты /
чат-тред / Сводка screens render fully populated with NO server reachable. Every
string is fabricated — see docs/store/screenshot-fixtures.md for the content rules
this dataset obeys (the bot never confirms a booking, appoints a time, or states
stock as fact — that is what the product actually does).

Two targets, same dataset:
    --target editor      (default) Unity Editor Play Mode on this Mac. This is the
                         cheap path: the Editor renders the REAL UI with the real
                         fonts and shaders, so no Xcode, no iOS runtime, no IL2CPP
                         build is needed to produce genuine store screenshots.
    --target simulator   a booted iOS Simulator with the app installed.

Usage:
    python3 Tools/store/seed-demo-data.py --dry-run
    python3 Tools/store/seed-demo-data.py                    # Editor
    python3 Tools/store/seed-demo-data.py --target simulator

Editor preconditions: Unity must NOT be in Play Mode (Play Mode flushes its own
PlayerPrefs on exit and would overwrite the seed). The script backs up the existing
prefs plist first — another session's Play Mode state lives in the same file.

STATUS: written 2026-08-28 from code-verified formats; the Editor path is the one
we intend to run. Verify each screen after the first run.
"""

import argparse
import json
import plistlib
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

BUNDLE = "com.synergysoft.choosereply"
TZ = timezone(timedelta(hours=5))            # Asia/Almaty — the app's GENERIC_TIMEZONE
WA_PROFILE = "b7a44f5d-1c2e-4f80-9a31-0d5e7c9a4412"   # fabricated, non-sentinel
TG_PROFILE = "3f1c9e02-7a84-4b16-b0d9-55ac1e2f6d31"

# The one number a reader actually studies: it is spoken twice inside the auto-reply thread,
# which is the screenshot that has to look real. The chat-list rows keep the obviously
# non-routable +7 700 000-00-NN block — nobody reads those, and it keeps the blast radius
# small. RESIDUAL RISK, owner's call: this is a well-formed KZ mobile, so it could belong to
# a real subscriber. Replace it with a number the owner controls before final submission.
CLIENT_PHONE = "+7 702 699-88-44"   # номер владельца (его решение 2026-09-03), не вымышленный
SHOP_PHONE = "+7 707 245-18-60"

# ---------------------------------------------------------------- PlayerPrefs

def player_prefs(now: datetime) -> dict:
    """PlayerPrefs → NSUserDefaults. int values MUST land as plist integers:
    PlayerPrefs.GetInt on a string returns its DEFAULT, and Bot{N}Active defaulting
    to 0 paints the card as the blinking «Подключение…» state (Bot.cs RefreshSubline)."""
    p = {
        "ids": 3,                                  # roster size; slots must be contiguous
        "LastSelectedBotForChats": "Bot0",
        # Trial must be STARTED and FRESH: absent → backfilled mid-capture; older than
        # PlanCatalog.TrialDays (5) → EvaluateLaunchExpiryPaywall opens the paywall over Боты.
        "TrialStartedUtc": (now - timedelta(days=1)).strftime("%Y-%m-%dT%H:%M:%SZ"),
        # Without this the «Первые шаги» card reserves 700u of list padding and covers
        # the top of the bots list (FirstStepsCard).
        "OnboardingChecklistDone": 1,
        # The onboarding carousel is raised at STARTUP (Manager.LoadBots tail) and would
        # cover every screen we are trying to photograph.
        "OnboardingSeen": 1,
        "OnboardingChannelConnectedSeen": 1,
        "OnboardingPriceListSeen": 1,
        "FirstBotReplySeen": 1,
        # Per-chat override: this ONE chat is «Вместе» while Bot0 stays Авто.
        # SemiAutoStore.Key = "{botId}_semiAuto_{chatId}"; 2 = on, 1 = explicit off.
        "Bot0_semiAuto_77000000012@c.us": 2,
    }

    bots = [
        dict(i=0, name="Авто-Деталь KZ", btype="auto_parts", wa=1, tg=1, mode=0,
             business="Магазин автозапчастей для японских авто в Астане. "
                      "Подбор по VIN, Kaspi рассрочка, самовывоз со склада.",
             prompt="Если клиент пишет ночью — прими заявку и добавь, что ответим в рабочее время.\n"
                    "Всегда уточняй марку, модель и год авто до того, как назвать цену.\n"
                    "Если позиции нет в прайсе — не отказывай, прими заявку на подбор.\n"
                    "Про доставку по городу отвечай: 1 500 ₸, в день заказа.",
             phone=SHOP_PHONE, hours="Пн–Сб 09:00–19:00, Вс выходной",
             address="Астана, ул. Бейбитшилик, 25", instagram="@avtodetal_demo",
             email="zakaz@example.com",
             # Names stay under the card's ~18-glyph column so nothing truncates; the артикул
             # moves into the description (still part of the catalogue row the bot quotes).
             # Distinct names on purpose — the monogram hue is hashed from the name, and two
             # identical names would paint two identical tiles.
             products=[("Колодки Toyota", "34 900", "Оригинал · 04465-33471"),
                       ("Колодки Nibk", "18 900", "Аналог · PN1512"),
                       ("Фильтр Toyota", "4 900", "Оригинал · 04152-YZZA1"),
                       ("Фильтр Filtron", "2 400", "Аналог · OP 570")],
             services=[]),
        dict(i=1, name="Букет Астана", btype="flowers", wa=1, tg=0, mode=0,
             business="Цветочный магазин. Доставка по Астане с 9:00 до 21:00, "
                      "оплата Kaspi или наличными.",
             prompt="", phone="+7 700 000-00-02", hours="Ежедневно 09:00–21:00",
             address="Астана, пр. Кабанбай батыра, 11", instagram="@buket_astana_demo", email="",
             products=[("Букет 25 роз", "18 000", "Красные, 60 см, лента"),
                       ("Пионовидные розы, 15 шт", "24 500", "Нежно-розовые, сезон"),
                       ("Композиция в коробке", "15 900", "Хризантема и эустома")],
             services=[]),
        dict(i=2, name="Сервис 24", btype="phone_repair", wa=0, tg=1, mode=1,
             business="Ремонт телефонов и ноутбуков. Диагностика бесплатно, гарантия 3 месяца.",
             prompt="", phone="+7 700 000-00-03", hours="Ежедневно 10:00–20:00",
             address="Астана, ул. Сарыарка, 4, ТЦ, 2 этаж", instagram="", email="",
             products=[],
             services=[("Замена экрана iPhone 12", "45 000", "Оригинальный дисплей, 1 день"),
                       ("Замена батареи iPhone 11", "18 000", "45 минут"),
                       ("Чистка после воды", "12 000", "Диагностика бесплатно")]),
    ]

    for b in bots:
        k = f"Bot{b['i']}"
        p[f"{k}Name"] = b["name"]
        p[f"{k}Active"] = 1                        # int — the connected/live gate
        p[f"{k}Status"] = "Active"
        p[f"{k}BusinessType"] = b["btype"]
        p[f"{k}Business"] = b["business"]
        p[f"{k}Prompt"] = b["prompt"]
        p[f"{k}Phone"] = b["phone"]
        p[f"{k}Hours"] = b["hours"]
        p[f"{k}Address"] = b["address"]
        p[f"{k}Instagram"] = b["instagram"]
        p[f"{k}Email"] = b["email"]
        p[f"{k}isOnWhatsapp"] = b["wa"]
        p[f"{k}isOnTelegram"] = b["tg"]
        p[f"{k}ReplyMode"] = b["mode"]             # 0 = Авто, 1 = Вместе
        # Persisted channel the chats tab shows for this bot (ChatManager.Channel.cs,
        # "<bot>ActiveChatChannel", 0 = WhatsApp / 1 = Telegram). Left unseeded, whatever a
        # previous Play Mode session persisted wins: on 2026-09-02 it was Telegram, so the
        # list loaded the empty Bot0/telegram/ cache and photographed a blank screen while
        # the seeded WhatsApp chats.json sat untouched beside it.
        p[f"{k}ActiveChatChannel"] = 0 if b["wa"] else 1
        p[f"{k}WhatsappProfileId"] = WA_PROFILE if b["wa"] else "-1"
        p[f"{k}TelegramProfileId"] = TG_PROFILE if b["tg"] else "-1"
        p[f"{k}WhatsappWorkflowId"] = "9PTyYcelRQI7bGDb" if b["wa"] else "-1"
        p[f"{k}TelegramWorkflowId"] = "4VN3gsFaC2HUYmcc" if b["tg"] else "-1"
        # Lists: count key is PLURAL+Number, item keys SINGULAR. An EMPTY name string makes
        # MigrateBotPersistence compact the list and silently shrink it on first launch.
        p[f"{k}ProductsNumber"] = len(b["products"])
        for j, (name, price, desc) in enumerate(b["products"]):
            p[f"{k}Product{j}"] = name
            p[f"{k}Product{j}Price"] = price       # digits only — ₸ is a sibling label
            p[f"{k}Product{j}Description"] = desc
        p[f"{k}ServicesNumber"] = len(b["services"])
        for j, (name, price, desc) in enumerate(b["services"]):
            p[f"{k}Service{j}"] = name
            p[f"{k}Service{j}Price"] = price
            p[f"{k}Service{j}Description"] = desc

    # Uploaded price lists for the primary bot (UploadedFilesStore): count key is
    # "<bot><Type>FilesNumber", items "<bot><Type>File<i>" + Name/Size/Date, and Size/Date
    # are STRINGS in PlayerPrefs even though they parse as longs. contentType "product"
    # → the «Продукты» tab, which is the screen that shows the price-list section.
    day_ms = 24 * 60 * 60 * 1000
    now_ms = int(now.timestamp() * 1000)
    for j, (fid, fname, size, days_ago) in enumerate([
        ("3f8a1c22-9d41-4c07-b6e5-2a7f0d51e934", "Прайс август 2026.xlsx", 48210, 2),
        ("7c1e4b90-56af-4d13-9e28-c4b6a3f70d15", "Колодки и фильтры.pdf", 315744, 9),
    ]):
        p[f"Bot0ProductFile{j}"] = fid
        p[f"Bot0ProductFile{j}Name"] = fname
        p[f"Bot0ProductFile{j}Size"] = str(size)
        p[f"Bot0ProductFile{j}Date"] = str(now_ms - days_ago * day_ms)
    p["Bot0ProductFilesNumber"] = 2
    return p

# ---------------------------------------------------------------- chat caches

def at(now: datetime, day_offset: int, hh: int, mm: int) -> datetime:
    return (now - timedelta(days=day_offset)).replace(hour=hh, minute=mm, second=0, microsecond=0)

def chats_json(now: datetime) -> dict:
    """Raw server payload shape (ChatsResponse). WhatsApp ids MUST carry '@'
    (ChatIdFormat.IsForeignToChannel drops a bare numeric id as a bled Telegram dialog)."""
    def row(cid, name, group, unread, preview, when, mine, sender=""):
        d = {"id": cid, "name": name, "isGroup": group, "unread_count": unread,
             "last_message_data": preview, "last_timestamp": when.isoformat(),
             "last_message_type": "chat",
             "last_message_sender": {"isMe": mine, "pushname": sender}}
        if mine:
            d["last_message_delivery_status"] = "read"
        return d

    return {"status": "done", "dialogs": [
        row("77000000011@c.us", "Ерлан Сапаров", False, 0,
            "Передаю менеджеру, он свяжется и подтвердит.", at(now, 0, 10, 9), True),
        row("77000000012@c.us", "Айгерим Нурланова", False, 2,
            "И сколько будет с заменой?", at(now, 0, 9, 47), False, "Айгерим Нурланова"),
        row("77000000013@c.us", "Данияр Оспанов", False, 0,
            "VIN скину вечером", at(now, 0, 8, 54), False, "Данияр Оспанов"),
        row("120363000000000001@g.us", "СТО Партнёры", True, 5,
            "нужны колодки на Camry, 5 комплектов", at(now, 1, 18, 40), False, "Тимур"),
        row("77000000014@c.us", "Мадина Ахметова", False, 0,
            "Точное наличие подтвердит менеджер.", at(now, 1, 18, 22), True),
        row("77000000015@c.us", "Азамат Жумабек", False, 1,
            "Спасибо, заеду завтра", at(now, 1, 16, 40), False, "Азамат Жумабек"),
        row("77000000016@c.us", "Сауле Кенжебаева", False, 3,
            "Kaspi рассрочка есть?", at(now, 1, 12, 15), False, "Сауле Кенжебаева"),
        row("77000000017@c.us", "Нурлан Абдиров", False, 0,
            "Записал: Prado 2018, фильтр воздушный.", at(now, 2, 11, 5), True),
    ] + [
        # Older rows: below the fold on the chat-list shot, but Сводка resolves its titles
        # and avatars from this list, so every dashboard outcome needs a chat here.
        row(cid, name, False, 0, preview, at(now, days, hh, mm), mine, "" if mine else name)
        for cid, name, preview, days, hh, mm, mine in EXTRA_CHATS
    ]}

EXTRA_CHATS = [
    ("77000000018@c.us", "Бауыржан Сейтказы", "Записал: Camry 40, рулевые наконечники.", 2, 17, 20, True),
    ("77000000019@c.us", "Динара Мукашева", "Спасибо, буду ждать звонка", 2, 15, 5, False),
    ("77000000020@c.us", "Арман Тулегенов", "Есть амортизаторы на RAV4 2019?", 3, 11, 42, False),
    ("77000000021@c.us", "Жанар Сулейменова", "Позиция в прайсе есть, точное наличие подтвердит менеджер.", 3, 10, 15, True),
    ("77000000022@c.us", "Ерасыл Бекмуратов", "Ок, тогда позже напишу", 4, 19, 30, False),
    ("77000000023@c.us", "Гульнара Досанова", "По гарантии уточнит менеджер — передаю.", 4, 14, 50, True),
    ("77000000024@c.us", "Нурсултан Кайыров", "Принял заявку: Prado 150, стойки стабилизатора.", 5, 12, 10, True),
    ("77000000025@c.us", "Асель Жаксыбекова", "Хорошо, спасибо!", 5, 9, 35, False),
    ("77000000026@c.us", "Ринат Ахмедов", "А доставка до Косшы есть?", 6, 18, 5, False),
    ("77000000027@c.us", "Айдана Серикова", "Записал: Corolla 2014, свечи, 4 шт.", 6, 13, 40, True),
    ("77000000028@c.us", "Тимур Есимов", "Прислал VIN, жду подбор", 6, 11, 20, False),
    ("77000000029@c.us", "Камила Оразбаева", "Понятно, подумаю", 8, 16, 0, False),
    ("77000000030@c.us", "Бекзат Нуртаев", "Записал: Camry 70, масло 5W-30, 4 л.", 9, 10, 25, True),
    ("77000000031@c.us", "Алия Жумагулова", "Спасибо, всё получила", 10, 15, 45, False),
    ("77000000032@c.us", "Даулет Исаев", "Записал: Hilux 2018, ремень ГРМ.", 11, 12, 0, True),
]

def thread(chat_id: str, sender: str, rows, now: datetime) -> dict:
    """MessageViewModel list under {"messages":[…]} (JsonUtility). type 0 = Chat,
    deliveryStatus 4 = Read. StripForeignMessages deletes any row whose chatId
    differs from the file's — so chatId is stamped from one place here."""
    out = []
    for n, (incoming, hh, mm, text) in enumerate(rows, start=1):
        ts = at(now, 0, hh, mm)
        out.append({
            "messageId": f"{'false' if incoming else 'true'}_{chat_id}_DEMO{n:04d}",
            "chatId": chat_id, "type": 0, "text": text,
            "isIncoming": incoming, "timestamp": int(ts.timestamp()),
            "sequence": n, "senderName": sender if incoming else "",
            "deliveryStatus": 0 if incoming else 4,
        })
    return {"messages": out}

THREAD_A = [  # Ерлан — the «Авто» payoff thread
    (True,  10, 2, "Здравствуйте! Нужны передние колодки. Есть?"),
    (False, 10, 2, "Здравствуйте! Подскажите марку, модель и год авто — так подберу точно."),
    (True,  10, 3, "Toyota Camry, 2015 год, 2.5"),
    (False, 10, 4, "По Camry 2015 2.5 в прайсе есть два варианта:\n"
                   "Колодки передние, арт. 04465-33471 — 34 900 ₸\n"
                   "Колодки передние, арт. PN1512 — 18 900 ₸"),
    (True,  10, 5, "А вторые точно есть в наличии?"),
    (False, 10, 5, "Позиция в прайсе есть, точное наличие подтвердит менеджер."),
    (True,  10, 6, "Это оригинал или аналог?"),
    (False, 10, 6, "PN1512 в прайсе помечен как аналог Nibk. Оригинал — 04465-33471."),
    (True,  10, 7, "Понял. Беру за 18 900, заберу сегодня после 18:00"),
    (False, 10, 8, "Принял заявку. Напишите, пожалуйста, имя и номер телефона — передам менеджеру."),
    (True,  10, 8, f"Ерлан, {CLIENT_PHONE}"),
    (False, 10, 9, f"Записал: Ерлан, {CLIENT_PHONE}, Camry 2015 2.5, колодки PN1512. "
                   "Передаю менеджеру, он свяжется и подтвердит."),
]

THREAD_B = [  # Айгерим — «Вместе»: last message must be INCOMING and unanswered
    (True,  9, 41, "Здравствуйте! Масляный фильтр на RAV4 есть?"),
    (False, 9, 42, "Здравствуйте! Подскажите год и объём двигателя — подберу точно."),
    (True,  9, 46, "2019, 2.0 бензин"),
    (True,  9, 47, "И сколько будет с заменой?"),
]

# «Вместе» cards for thread B, rendered by StoreDemoSuggestionsProvider. Authored against
# Tools/n8n/prompts/panel/auto_parts.md — the panel speaks in the OWNER's voice (first person,
# «уточню и напишу»), never the auto-mode assistant's «передам менеджеру». Prices are verbatim
# Bot0Product2 / Bot0Product3. Installation is absent from the catalogue and from About Business,
# so card 3 defers instead of inventing a price. Labels ≤18 chars, all four distinct.
SUGGESTION_CARDS = [
    {"label": "Цена и артикул", "move": "Ответ",
     "text": "На RAV4 2019 2.0 подходит фильтр, арт. 04152-YZZA1 — 4 900 ₸."},
    {"label": "Дешевле", "move": "Вариант",
     "text": "Есть аналог подешевле — OP 570, 2 400 ₸. В прайсе он помечен как аналог Filtron."},
    {"label": "Про замену", "move": "Отложить",
     "text": "По замене уточню и напишу вам — не хочу называть условия наугад."},
    {"label": "Оформить?", "move": "К заказу",
     "text": "Если берём — напишите имя и телефон, проверю остаток на складе и наберу вас."},
]

def dashboard_json(now: datetime) -> dict:
    """DashboardStore.Payload. lastFetchMs is set to NOW on purpose: DashboardPage.OnEnable
    refetches when now-lastFetchMs >= 60s, and a success response carrying an empty
    outcomes array for these fabricated profileIds would CLEAR the seeded rows and
    rewrite this file."""
    now_ms = int(now.timestamp() * 1000)
    def o(chat, outcome, summary, mins_ago):
        t = now_ms - mins_ago * 60_000
        return {"profileId": WA_PROFILE, "chatId": chat, "outcome": outcome,
                "summary": summary, "outcomeAt": t, "lastMessageAt": t}
    return {"lastFetchMs": now_ms, "outcomes": [
        o("77000000011@c.us", "order_collected",
          "Колодки PN1512, Camry 2015. Имя и телефон взяты, ждёт менеджера.", 12),
        o("77000000012@c.us", "in_dialog",
          "Спрашивает цену фильтра на RAV4 2019 и про замену.", 34),
        o("77000000017@c.us", "order_collected",
          "Prado 2018, фильтр воздушный — позиции нет в прайсе, заявка принята.", 2_900),
        o("77000000016@c.us", "owner_needed",
          "Вопрос по Kaspi рассрочке — условий нет в данных бизнеса.", 1_180),
        o("77000000013@c.us", "client_silent", "Обещал прислать VIN, ответа пока нет.", 96),
        o("77000000014@c.us", "question_closed", "Уточняла наличие колодок, ответ дан.", 1_120),
        o("77000000015@c.us", "in_dialog", "Написал, что заедет завтра.", 1_030),
        # Rest of the 7-day window — the default period showed 1/0/1/1/0 and undersold the
        # board (2026-09-02). Current window: 5 заявок / 3 / 4 / 3 / 3 = 18 dialogs …
        o("77000000018@c.us", "order_collected", "Рулевые наконечники на Camry 40 — заявка принята.", 2 * 1440 + 400),
        o("77000000019@c.us", "client_silent", "Ждёт звонка менеджера, ответа нет.", 2 * 1440 + 540),
        o("77000000020@c.us", "in_dialog", "Спрашивает амортизаторы на RAV4 2019.", 3 * 1440 + 740),
        o("77000000021@c.us", "question_closed", "Уточнила наличие, ответ дан.", 3 * 1440 + 830),
        o("77000000022@c.us", "client_silent", "Обещал написать позже.", 4 * 1440 + 270),
        o("77000000023@c.us", "owner_needed", "Вопрос по гарантии — условий нет в данных бизнеса.", 4 * 1440 + 550),
        o("77000000024@c.us", "order_collected", "Стойки стабилизатора, Prado 150. Контакты взяты.", 5 * 1440 + 710),
        o("77000000025@c.us", "question_closed", "Поблагодарила, вопрос закрыт.", 5 * 1440 + 865),
        o("77000000026@c.us", "owner_needed", "Доставка до Косшы — зоны нет в данных бизнеса.", 6 * 1440 + 355),
        o("77000000027@c.us", "order_collected", "Свечи на Corolla 2014, 4 шт. — заявка принята.", 6 * 1440 + 620),
        o("77000000028@c.us", "in_dialog", "Прислал VIN, ждёт подбор.", 6 * 1440 + 760),
        # … and the previous window (8–14 days), so «к пред.» has something to compare with:
        # 2 заявки there → «+3 к пред.».
        o("77000000029@c.us", "client_silent", "Взяла паузу, ответа нет.", 8 * 1440 + 480),
        o("77000000030@c.us", "order_collected", "Масло 5W-30 4 л, Camry 70 — заявка принята.", 9 * 1440 + 815),
        o("77000000031@c.us", "question_closed", "Получила заказ, вопрос закрыт.", 10 * 1440 + 495),
        o("77000000032@c.us", "order_collected", "Ремень ГРМ, Hilux 2018 — заявка принята.", 11 * 1440 + 720),
    ]}

# ---------------------------------------------------------------- simulator IO

def sh(args: list[str]) -> str:
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit(f"FAILED: {' '.join(args)}\n{r.stderr.strip()}")
    return r.stdout.strip()

EDITOR_DOMAIN = "unity.SynergySoft.Choose Reply"          # companyName / productName
EDITOR_PREFS = Path.home() / "Library/Preferences" / f"{EDITOR_DOMAIN}.plist"
EDITOR_DOCS = Path.home() / "Library/Application Support/SynergySoft/Choose Reply"

def seed_editor(prefs: dict, files: dict) -> None:
    """Unity Editor Play Mode on macOS. PlayerPrefs live in a plist under the
    unity.<company>.<product> domain; persistentDataPath is Application Support."""
    if EDITOR_PREFS.exists():
        backup = EDITOR_PREFS.with_suffix(".plist.pre-seed")
        backup.write_bytes(EDITOR_PREFS.read_bytes())
        existing = plistlib.loads(EDITOR_PREFS.read_bytes())
        print(f"бэкап прежних настроек: {backup.name} ({len(existing)} ключей)")

    for key, value in prefs.items():
        flag = "-int" if isinstance(value, int) else "-string"
        sh(["defaults", "write", EDITOR_DOMAIN, key, flag, str(value)])
    print(f"PlayerPrefs записано: {len(prefs)}  → {EDITOR_PREFS}")

    write_files(EDITOR_DOCS, files)

def seed_simulator(udid: str, bundle: str, prefs: dict, files: dict) -> None:
    container = Path(sh(["xcrun", "simctl", "get_app_container", udid, bundle, "data"]))
    print(f"контейнер: {container}")
    for key, value in prefs.items():
        flag = "-int" if isinstance(value, int) else "-string"
        sh(["xcrun", "simctl", "spawn", udid, "defaults", "write", bundle, key, flag, str(value)])
    print(f"PlayerPrefs записано: {len(prefs)}")
    write_files(container / "Documents", files)

def write_files(docs: Path, files: dict) -> None:
    for rel, payload in files.items():
        target = docs / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
        print(f"  ✓ {rel}")

def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--target", choices=["fixture", "editor", "simulator"], default="fixture",
                    help="fixture (default): write Tools/store/fixtures/demo-data.json for the "
                         "in-Editor seeder — the ONLY reliable path while Unity is open, because "
                         "a running Editor caches PlayerPrefs and flushes its own copy over any "
                         "external `defaults write` when Play Mode exits (measured 2026-08-28: "
                         "99 seeded keys reduced to 20). 'editor' writes the plist directly and "
                         "is only safe with Unity CLOSED.")
    ap.add_argument("--udid", default="booted", help="simulator udid (default: booted)")
    ap.add_argument("--bundle", default=BUNDLE)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    now = datetime.now(TZ)
    prefs = player_prefs(now)
    files = {
        f"BotCache/Bot0/chats.json": chats_json(now),
        f"BotCache/Bot0/messages/77000000011@c.us.json":
            thread("77000000011@c.us", "Ерлан Сапаров", THREAD_A, now),
        f"BotCache/Bot0/messages/77000000012@c.us.json":
            thread("77000000012@c.us", "Айгерим Нурланова", THREAD_B, now),
        "dashboard_cache.json": dashboard_json(now),
    }

    if args.dry_run:
        dest = EDITOR_DOCS if args.target == "editor" else "<app container>/Documents"
        print(f"цель: {args.target}   → {dest}")
        print(f"PlayerPrefs: {len(prefs)} ключей")
        for k in sorted(prefs):
            v = prefs[k]
            print(f"  {k:38} = {v!r} ({'int' if isinstance(v, int) else 'str'})")
        print(f"\nФайлы: {len(files)}")
        for path, payload in files.items():
            body = json.dumps(payload, ensure_ascii=False)
            print(f"  {path:52} {len(body):6d} байт")
        return

    if args.target == "fixture":
        out = Path("Tools/store/fixtures/demo-data.json")
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps({"prefs": prefs, "files": files,
                                   "suggestions": SUGGESTION_CARDS},
                                  ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"фикстура записана: {out}  ({len(prefs)} ключей, {len(files)} файлов)")
        print("Применить: Tools/Store/Capture Screenshots в Unity (сеет сам перед съёмкой).")
        return
    if args.target == "editor":
        seed_editor(prefs, files)
    else:
        seed_simulator(args.udid, args.bundle, prefs, files)

    print("\nГотово. Дальше: Tools/Store/Capture Screenshots в Unity.")
    print("Порядок важен: сначала кадр списка чатов (открытие чата гасит его бейдж),")
    print("потом тред, Боты, Сводка.")

if __name__ == "__main__":
    main()
