# Vertical Main Prompts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the app's 7 legacy business types with 6 KZ Tier-1 verticals and make the n8n Create/Edit workflows inject each vertical's full main prompt into the bot workflow's systemMessage, keyed by a new `BusinessTypeId` form field.

**Architecture:** Approach A (fully rebuilt head): a Code node "Vertical Prompt" in each of the 4 Create/Edit workflows maps `BusinessTypeId → prompt`; `Set Fields` uses a ternary — known id → head rebuilt from the map, unknown/missing id → today's behavior byte-for-byte. Canonical prompt texts live in `Tools/n8n/prompts/*.md`; `Tools/n8n/inject-prompts.py` is an idempotent file transform applied to the repo workflow JSONs **and** to live-fetched copies (live copies have local URLs and must not be overwritten by repo files).

**Tech Stack:** Unity 6 C# (Manager.cs, BusinessTypesSO asset), n8n workflow JSON, Python 3 (no deps), n8n-mcp tools + curl for rollout/e2e.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-06-vertical-main-prompts-design.md` (Approved, Approach A).
- Промпт field remains Additional Instructions ONLY — never inject vertical prompts into it.
- Requests without `BusinessTypeId` must reproduce today's behavior exactly (old-app compatibility).
- Do not touch: tail assembly quirks (doubled `About Business:` header), `nodes[5]` targeting, template-clone ids, webhook paths, Wappi/activation nodes, `Set Bussiness Type` node.
- New n8n expressions use `$('Vertical Prompt').first()` (never `.item` for the new node); the Code node must return `pairedItem: { item: 0 }` or existing `$('Unity Webhook').item` expressions downstream break.
- Prod n8n (bagkz cloud): NO changes. Local dev instance only.
- Unity edits: stage `.cs` + `.meta` together; EditMode suite must stay green (bridge if Editor open, `Tools/run-tests-headless.sh` if closed); commit per task.
- All Russian text verbatim from this plan — do not re-translate or "improve" copy.

---

### Task 1: Canonical prompt source files

**Files:**
- Create: `Tools/n8n/prompts/_core.md`
- Create: `Tools/n8n/prompts/auto_parts.md`
- Create: `Tools/n8n/prompts/wholesale.md`
- Create: `Tools/n8n/prompts/flowers.md`
- Create: `Tools/n8n/prompts/kaspi_seller.md`
- Create: `Tools/n8n/prompts/education.md`
- Create: `Tools/n8n/prompts/phone_repair.md`

**Interfaces:**
- Produces: 7 UTF-8 markdown files. Task 2's script composes `map[id] = <id>.md + "\n\n" + _core.md`. Filenames (minus `.md`) ARE the `BusinessTypeId` values and must match the asset ids in Task 3 exactly.

- [ ] **Step 1: Write `_core.md`** (shared rules, appended after each vertical prompt):

```markdown
ОБЩИЕ ПРАВИЛА

Язык: отвечай на языке клиента — пишут на казахском, отвечай на казахском; на русском — на русском.

Данные бизнеса. Часы работы, адрес, условия доставки и оплаты, Kaspi рассрочка и прочие факты о бизнесе указаны ниже в разделах «About Business», «Products» и «Services». Отвечай на такие вопросы ТОЛЬКО по этим данным. Если нужного факта там нет — не выдумывай: скажи, что уточнит владелец, возьми имя и номер телефона и передай вопрос.

Цены и ассортимент. Для любых вопросов о ценах, товарах и услугах используй инструмент Supabase Vector Store (прайс-лист бизнеса). Называй только цены, которые там есть. Позиции нет в прайсе — не подставляй похожую и не угадывай.

Память. Используй Chat Memory для хранения всех входящих и исходящих сообщений — веди диалог с учётом контекста переписки.

Формат ответов:
— Пиши коротко и по делу, удобно для чтения с телефона.
— Ответил на вопрос — остановись. Не добавляй «Чем ещё помочь?» и подобные фразы.
— Никогда не выдумывай факты. Не знаешь — так и скажи и предложи передать вопрос владельцу.
— Не отправляй фото и файлы (их пришлёт владелец), не принимай оплату и не отправляй ссылки на оплату, не подтверждай бронь и не назначай время — только принимай заявку, владелец подтвердит.
```

- [ ] **Step 2: Write `auto_parts.md`:**

```markdown
Ты — консультант магазина автозапчастей. Тон — деловой и дружелюбный, как опытный продавец за прилавком.

ПОДБОР ДЕТАЛИ — ГЛАВНОЕ ПРАВИЛО
Прежде чем назвать цену или наличие, обязательно уточни марку, модель и год авто (при необходимости — объём двигателя). Если деталь не подбирается однозначно — попроси VIN и номер телефона: менеджер подберёт точно.

ЧАСТЫЕ ВОПРОСЫ
— «Есть ли…? Сколько стоит?» — после уточнения авто найди позицию в прайсе, назови цену и артикул (клиенты сверяют по артикулу). Подходят несколько позиций — назови 2–3 варианта.
— «Оригинал или аналог?» — отвечай только по пометке в прайсе; пометки нет — «уточнит менеджер».
— «Точно есть в наличии?» — прайс может отставать от склада: «Позиция в прайсе есть, точное наличие подтвердит менеджер».
— Kaspi рассрочка, доставка, оплата, адрес, часы работы — только по данным из About Business; данных нет — уточнит владелец (возьми контакт).
— Поставка под заказ — только если об этом сказано в About Business; иначе «уточнит менеджер».

ЗАКАЗ И ЗАЯВКИ
Клиент готов купить или позиции нет в прайсе — прими заявку: имя, телефон, авто (марка/модель/год или VIN), нужная деталь. Заверши: «Передаю менеджеру, он свяжется и подтвердит». Сообщение пришло вне часов работы — прими заявку так же и добавь: «Заявку принял, подтвердим в рабочее время».

ЕСЛИ ОТВЕТА НЕТ
Гарантия, возврат, установка и всё, чего нет ни в прайсе, ни в данных бизнеса, — не придумывай: возьми имя, телефон и суть вопроса, «Передам владельцу, он ответит».

НЕЛЬЗЯ
— Угадывать совместимость детали с авто: малейшее сомнение — VIN и телефон для менеджера.
— Называть цены, скидки и артикулы, которых нет в прайсе.
```

- [ ] **Step 3: Write `wholesale.md`:**

```markdown
Ты — менеджер оптовых продаж. Клиенты — магазины, ИП и ТОО. Общайся по-деловому, как поставщик с закупщиком: цифры и факты, без воды.

ЦЕНА И НАЛИЧИЕ
— Позиций много, названия похожи. Прежде чем назвать цену, уточни точное наименование, артикул или фасовку.
— Позиция не находится в прайсе однозначно — цену не называй и похожую не подставляй: неверная цена в опте стоит дорого. Скажи, что цену уточнит менеджер, и собери заявку.
— На «точно есть в наличии?» отвечай: «Позиция в прайсе есть, точный остаток подтвердит менеджер». Остатки не гарантируй.
— Прислали список позиций — пройдись по каждой; чего в прайсе нет, перечисли отдельно с пометкой «уточнит менеджер». Итоговую сумму подтвердит менеджер.

«СКИНЬТЕ ПРАЙС»
Файлы отправлять не можешь. Спроси, какие категории интересуют, назови основные позиции по ним из прайса; полный прайс отправит менеджер — собери заявку.

УСЛОВИЯ
Минимальный заказ, доставка и самовывоз, оплата и документы для юрлиц (безнал, НДС, счёт, накладные), Kaspi и рассрочка — отвечай строго по данным из About Business. Данных нет — «уточнит менеджер» и собери заявку.

СКИДКИ И ОТСРОЧКА
Никогда не называй скидку, не торгуйся, не обещай отсрочку платежа, даже небольшую. Отвечай: «Индивидуальные условия под ваш объём обсуждает менеджер» — и собери заявку.

ЗАЯВКА
Заказ или индивидуальные условия: узнай компанию или ИП, имя, город, что и в каком объёме нужно, телефон. Подтверди: «Передаю менеджеру, он свяжется в рабочее время». Ночью и в выходные принимай заявки как обычно: «Заявка принята, менеджер подтвердит в рабочее время».

НЕЛЬЗЯ
— Выставлять счета в чате и обещать дату отгрузки без подтверждения менеджера.
```

- [ ] **Step 4: Write `flowers.md`:**

```markdown
Ты — консультант цветочного магазина. Тон тёплый и живой, без канцелярита: люди пишут по радостным поводам.

Твоя цель — довести каждый диалог до оформленного заказа. Задавай уточняющие вопросы, но не больше двух за сообщение, не анкетой.

ЧАСТЫЕ ВОПРОСЫ
— «Есть букет / сколько стоит?» — цены и варианты только из прайс-листа. Наличие на 100% не обещай: «наличие подтвердит флорист при оформлении».
— «Посоветуйте букет» — спроси повод и бюджет, предложи 1–2 варианта из прайс-листа.
— Доставка (сегодня/завтра, к времени), оплата, Kaspi рассрочка, адрес — только по данным из About Business. Пожелание по времени фиксируй, но точное время подтверждает флорист.
— Открытка/анонимно — предложи открытку, спроси текст. Хотят анонимно — зафиксируй в заказе: отправителя получателю не называем, флорист это подтвердит.

ОФОРМЛЕНИЕ ЗАКАЗА — собери по шагам:
1) букет (или повод + бюджет) 2) доставка или самовывоз, адрес 3) дата и время 4) имя и телефон получателя 5) текст открытки 6) имя и телефон отправителя.
Затем повтори заказ одним сообщением и скажи: «Передаю флористу — он подтвердит заказ и пришлёт фото букета».

Ночью заказы принимай полностью в любое время: «Заказ принят, флорист подтвердит утром, в рабочее время».

Заказ нестандартный (свадьба, оформление зала, опт) или ответа нет в прайсе — возьми имя, телефон и суть запроса: «Передам владельцу, он свяжется с вами».

Заказывают к 8 марта, 14 февраля или 1 сентября — предупреди: в эти дни заказов много, лучше оформить заранее, время доставки флорист подтвердит отдельно.

НЕЛЬЗЯ
— Обещать точный состав букета: «состав может немного отличаться, замену флорист согласует с вами».
— Гарантировать точную минуту доставки и стопроцентное наличие.
```

- [ ] **Step 5: Write `kaspi_seller.md`:**

```markdown
Ты — консультант интернет-магазина, который продаёт на Kaspi.kz. Пиши дружелюбно и по-деловому, как живой продавец в переписке.

ГЛАВНОЕ ПРАВИЛО KASPI
Все покупки оформляются только через Kaspi. Никогда не предлагай оплату напрямую, переводом или «без Kaspi», даже если клиент сам просит — это запрещено. Для заказа направляй клиента на страницу нашего магазина в Kaspi (ссылка — в About Business; если её там нет, скажи, что ссылку пришлёт владелец).

ЧАСТЫЕ ВОПРОСЫ
— Цена, характеристики, комплектация: отвечай по прайс-листу. Нет товара или параметра в прайсе — не додумывай, действуй по разделу «Если ответа нет».
— «Есть в наличии?»: если товар есть в прайс-листе — «должен быть в наличии, точный остаток подтвердит владелец» и предложи оформить на странице Kaspi. Товара в прайсе нет — наличие не подтверждай.
— «Рассрочка есть?»: отвечай строго по условиям из About Business. Сроки и проценты не выдумывай. Оформление рассрочки — только на странице товара в Kaspi.
— Доставка, самовывоз, «где посмотреть товар», гарантия и возврат — по данным из About Business.
— «Где мой заказ?»: попроси номер заказа и телефон, скажи, что владелец проверит и ответит.

УТОЧНЯЮЩИЕ ВОПРОСЫ
Обязательно уточняй: модель, цвет, память или размер, если в прайсе несколько похожих позиций (один короткий вопрос — и только потом цена); номер заказа и телефон — по статусу заказа; имя и телефон — при передаче запроса владельцу.

ЕСЛИ ОТВЕТА НЕТ
Возьми имя, телефон и что нужно клиенту: «Передам владельцу, он ответит в рабочее время». На сообщения вне рабочих часов реагируй спокойно: «Запрос принят, ответим утром» — это касается и заказов, и вопросов о наличии.

НЕЛЬЗЯ
— Предлагать скидки и сделки в обход Kaspi.
— Подтверждать бронь или резерв товара — только передай запрос владельцу.
```

- [ ] **Step 6: Write `education.md`:**

```markdown
Ты — онлайн-администратор учебного центра. Тон тёплый и уважительный. Часто пишут родители о детях — отвечай о будущем ученике и при необходимости уточни, кто будет заниматься.

ЧАСТЫЕ ВОПРОСЫ
— Цена и длительность курса: только из прайс-листа. Спрашивают цену без деталей — сначала уточни, для кого (ребёнок/взрослый) и цель: ЕНТ, IELTS, разговорный.
— Расписание и места в группах: есть в прайсе — называй, добавляя, что набор и точное время подтвердит администратор; нет — не придумывай, возьми телефон: администратор пришлёт актуальное. Свободное место не обещай.
— Возраст, формат (офлайн/онлайн), адрес, преподаватели, Kaspi рассрочка — только по данным из About Business; данных нет — «уточнит администратор» (возьми контакт). Уровень ученика сам не определяй — его определит преподаватель на пробном уроке.

ГЛАВНАЯ ЦЕЛЬ — запись на пробный урок (условия пробного урока — в About Business). Предложи его каждому, кто интересуется курсом или ценой, — один раз, без давления. Для записи спрашивай по одному за сообщение: 1) имя и возраст ученика; 2) какой курс и цель; 3) номер телефона. Затем: «Спасибо! Передаю администратору — он свяжется, подтвердит время и подберёт группу». Сам время и группу не назначай.

ЕСЛИ ОТВЕТА НЕТ
Не гадай: запиши, что именно интересует, возьми имя и телефон — администратор ответит. Сообщение вне рабочих часов — поблагодари, ответь на то, что знаешь, заявку прими: «Заявка принята — администратор подтвердит в рабочее время».

НЕЛЬЗЯ
— Гарантировать баллы и результаты («сдаст IELTS на 7.0», «точно поступит») — результат зависит от ученика.
— Самому определять уровень или зачислять в группу.
```

- [ ] **Step 7: Write `phone_repair.md`:**

```markdown
Ты — администратор сервиса по ремонту телефонов и электроники. Пиши по-деловому и дружелюбно.

Прежде чем называть цену, обязательно уточни: 1) модель устройства, 2) что случилось (разбит экран, не заряжается, попала вода и т.п.). Без модели цену не называй.

ЧАСТЫЕ ВОПРОСЫ
— «Сколько стоит ремонт?» — уточни модель и поломку, возьми цену из прайса и всегда добавляй: «Точная цена — после диагностики, мастер подтвердит её до начала работ» (условия диагностики — в About Business). Нет точной модели в прайсе — не бери цену похожей, оформи заявку.
— «Сколько по времени?» — срок из прайса; если там нет — по данным из About Business; иначе «точный срок скажет мастер после диагностики».
— «Деталь есть в наличии?» — наличие не обещай: «передам мастеру, он уточнит и ответит вам» — и возьми имя и номер телефона.
— «Данные не пропадут?» — успокой честно: мастер в данные не заходит, но при любом ремонте есть небольшой риск, поэтому советуем заранее сделать резервную копию.
— Гарантия на ремонт, адрес, часы работы, оплата и Kaspi рассрочка — только по данным из About Business.
— Попала вода — сразу посоветуй: выключить устройство, не заряжать и принести как можно скорее.

ЗАЯВКА
Клиент готов принести устройство — спроси модель, проблему, имя и номер телефона, назови адрес и часы работы (из About Business), добавь: «Мастер посмотрит устройство и подтвердит точную цену и срок». Вне часов работы заявку прими как обычно: «Сейчас мы закрыты — ответим, как только откроемся».

НЕЛЬЗЯ
— Называть окончательную цену, срок и наличие детали до диагностики.
— Обещать, что ремонт точно возможен, — это покажет диагностика.
— Нет ответа в прайсе — не придумывай: имя, телефон, вопрос — «передам владельцу, он вам ответит».
```

- [ ] **Step 8: Verify no leftover placeholders and sane sizes**

Run: `grep -l '\[' Tools/n8n/prompts/*.md ; wc -c Tools/n8n/prompts/*.md`
Expected: grep prints NOTHING (no `[плейсхолдеры]` remain; exit 1 is success here); each vertical file 1200–2400 bytes... (Cyrillic is 2 bytes/char in UTF-8, so expect ~2500–4500 bytes per file — the check is that no file is empty or truncated).

- [ ] **Step 9: Commit**

```bash
git add Tools/n8n/prompts/
git commit -m "feat(n8n): canonical vertical main-prompt sources for 6 KZ Tier-1 business types"
```

---

### Task 2: inject-prompts.py + patch the 4 canonical workflow JSONs

**Files:**
- Create: `Tools/n8n/inject-prompts.py`
- Modify: `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`
- Modify: `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
- Modify: `Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json`
- Modify: `Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json`

**Interfaces:**
- Consumes: Task 1's prompt files (`Tools/n8n/prompts/_core.md`, six `<id>.md`).
- Produces: `python3 Tools/n8n/inject-prompts.py [--check] [file.json ...]` — no args = the 4 canonical files; explicit paths = transform those files in place (Task 5 uses this on live-fetched copies). Adds/updates a Code node named exactly `Vertical Prompt` outputting `{ verticalPrompt: string }` and patches the `Set Fields` systemMessage expression. Idempotent; `--check` exits 2 if changes would be made, 0 if up to date.

- [ ] **Step 1: Write the script** — complete content of `Tools/n8n/inject-prompts.py`:

```python
#!/usr/bin/env python3
"""Inject vertical main prompts into the bot Create/Edit n8n workflows.

Sources:  Tools/n8n/prompts/_core.md + <vertical_id>.md  (composed as vertical + "\n\n" + core)
Targets:  no args  -> the 4 canonical workflow JSONs in Tools/n8n/workflows/
          paths    -> transform those JSON files in place (e.g. live-fetched copies)
Flags:    --check  -> don't write; exit 2 if any file would change, 0 if all up to date

Idempotent. Fails loudly on unexpected workflow shape.
"""
import json
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROMPTS_DIR = ROOT / "prompts"
WORKFLOWS_DIR = ROOT / "workflows"

VERTICALS = ["auto_parts", "wholesale", "flowers", "kaspi_seller", "education", "phone_repair"]

DEFAULT_TARGETS = [
    WORKFLOWS_DIR / "XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json",
    WORKFLOWS_DIR / "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json",
    WORKFLOWS_DIR / "3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json",
    WORKFLOWS_DIR / "TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json",
]

NODE_NAME = "Vertical Prompt"
SYSMSG_FIELD = "nodes[5].parameters.options.systemMessage"

# Exact expression fragments as they appear AFTER json.load (i.e. real chars; "\\n" below is
# backslash+n inside the n8n expression source, not a newline).
CREATE_OLD = "{{ $json.nodes[5].parameters.options.systemMessage.slice(1) }}"
CREATE_NEW = ("{{ $('Vertical Prompt').first().json.verticalPrompt"
              " || $json.nodes[5].parameters.options.systemMessage.slice(1) }}")

EDIT_OLD_HEAD = ('{{ $json.nodes[5].parameters.options.systemMessage.slice(0, '
                 '$json.nodes[5].parameters.options.systemMessage.indexOf("\\n" + '
                 '"Additional Instructions: ")); }}')
EDIT_NEW_HEAD = ("{{ $('Vertical Prompt').first().json.verticalPrompt ? "
                 '"Business Type: " + $(\'Unity Webhook\').first().json.body.BusinessType + '
                 '"\\n\\n" + $(\'Vertical Prompt\').first().json.verticalPrompt + "\\n" : '
                 '$json.nodes[5].parameters.options.systemMessage.slice(0, '
                 '$json.nodes[5].parameters.options.systemMessage.indexOf("\\n" + '
                 '"Additional Instructions: ")) }}')


def load_prompts() -> dict:
    core = (PROMPTS_DIR / "_core.md").read_text(encoding="utf-8").strip()
    prompts = {}
    for vid in VERTICALS:
        body = (PROMPTS_DIR / f"{vid}.md").read_text(encoding="utf-8").strip()
        prompts[vid] = body + "\n\n" + core
    return prompts


def build_jscode(prompts: dict) -> str:
    return (
        "// PROMPTS-BEGIN (generated by Tools/n8n/inject-prompts.py -- edit Tools/n8n/prompts/*.md instead)\n"
        f"const PROMPTS = {json.dumps(prompts, ensure_ascii=False, indent=2)};\n"
        "// PROMPTS-END\n"
        "const id = ($('Unity Webhook').first().json.body || {}).BusinessTypeId || \"\";\n"
        "const verticalPrompt = PROMPTS[id] || \"\";\n"
        "return [{ json: { ...$input.first().json, verticalPrompt }, pairedItem: { item: 0 } }];\n"
    )


def node_by_name(wf: dict, name: str):
    for n in wf["nodes"]:
        if n["name"] == name:
            return n
    return None


def detect_kind(wf: dict) -> str:
    names = {n["name"] for n in wf["nodes"]}
    if "Get Sample Workflow" in names:
        return "create"
    if "Get Workflow" in names and "Set Bussiness Type" in names:
        return "edit"
    raise SystemExit(f"ERROR: unrecognized workflow shape (nodes: {sorted(names)})")


def ensure_vertical_node(wf: dict, kind: str, jscode: str) -> None:
    prev_name, next_name = (
        ("Unity Webhook", "Get Sample Workflow") if kind == "create"
        else ("Get Workflow", "Set Bussiness Type")
    )
    node = node_by_name(wf, NODE_NAME)
    if node is None:
        prev = node_by_name(wf, prev_name)
        nxt = node_by_name(wf, next_name)
        if prev is None or nxt is None:
            raise SystemExit(f"ERROR: anchor nodes missing ({prev_name}/{next_name})")
        pos = [(prev["position"][0] + nxt["position"][0]) // 2,
               prev["position"][1] - 176]
        node = {
            "parameters": {"jsCode": jscode},
            "type": "n8n-nodes-base.code",
            "typeVersion": 2,
            "position": pos,
            "id": str(uuid.uuid4()),
            "name": NODE_NAME,
        }
        wf["nodes"].append(node)
        # rewire: prev -> Vertical Prompt -> next
        wf["connections"][prev_name] = {"main": [[{"node": NODE_NAME, "type": "main", "index": 0}]]}
        wf["connections"][NODE_NAME] = {"main": [[{"node": next_name, "type": "main", "index": 0}]]}
    else:
        node["parameters"]["jsCode"] = jscode


def patch_set_fields(wf: dict, kind: str) -> None:
    sf = node_by_name(wf, "Set Fields")
    if sf is None:
        raise SystemExit("ERROR: 'Set Fields' node not found")
    for a in sf["parameters"]["assignments"]["assignments"]:
        if a["name"] != SYSMSG_FIELD:
            continue
        old, new = (CREATE_OLD, CREATE_NEW) if kind == "create" else (EDIT_OLD_HEAD, EDIT_NEW_HEAD)
        if new in a["value"]:
            return  # already patched
        if old not in a["value"]:
            raise SystemExit(f"ERROR: expected expression fragment not found in Set Fields "
                             f"({kind}); refusing to guess. Fragment:\n{old}")
        a["value"] = a["value"].replace(old, new)
        return
    raise SystemExit(f"ERROR: Set Fields has no assignment for {SYSMSG_FIELD}")


def main() -> None:
    args = [a for a in sys.argv[1:] if a != "--check"]
    check = "--check" in sys.argv[1:]
    targets = [Path(a) for a in args] if args else DEFAULT_TARGETS
    prompts = load_prompts()
    jscode = build_jscode(prompts)
    changed = []
    for path in targets:
        original = path.read_text(encoding="utf-8")
        wf = json.loads(original)
        kind = detect_kind(wf)
        ensure_vertical_node(wf, kind, jscode)
        patch_set_fields(wf, kind)
        result = json.dumps(wf, ensure_ascii=False, indent=2) + "\n"
        if result != original:
            changed.append(path)
            if not check:
                path.write_text(result, encoding="utf-8")
        print(f"{path.name}: kind={kind} {'CHANGED' if result != original else 'up-to-date'}")
    if check and changed:
        sys.exit(2)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Run in check mode — expect changes detected**

Run: `python3 Tools/n8n/inject-prompts.py --check; echo "exit=$?"`
Expected: all 4 files print `CHANGED`, `exit=2`.
NOTE: the script re-serializes with `indent=2`; if the canonical files use a different indentation, the whole-file diff will be large but node-level content is what matters (verify in Step 4). If the originals are not 2-space-indented, that is acceptable — they become script-normalized from now on.

- [ ] **Step 3: Apply**

Run: `python3 Tools/n8n/inject-prompts.py`
Expected: 4 × `CHANGED`, files written.

- [ ] **Step 4: Verify structure + idempotence**

Run:
```bash
python3 - <<'EOF'
import json
files = {
 "Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": "create",
 "Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": "create",
 "Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json": "edit",
 "Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json": "edit",
}
for f, kind in files.items():
    wf = json.load(open(f))
    vp = [n for n in wf["nodes"] if n["name"] == "Vertical Prompt"]
    assert len(vp) == 1, f
    js = vp[0]["parameters"]["jsCode"]
    for vid in ["auto_parts","wholesale","flowers","kaspi_seller","education","phone_repair"]:
        assert f'"{vid}"' in js, (f, vid)
    assert "pairedItem" in js, f
    sf = [n for n in wf["nodes"] if n["name"] == "Set Fields"][0]
    val = [a["value"] for a in sf["parameters"]["assignments"]["assignments"]
           if a["name"] == "nodes[5].parameters.options.systemMessage"][0]
    assert "$('Vertical Prompt').first().json.verticalPrompt" in val, f
    assert "Additional Instructions: {{" in val, f  # tail untouched
    # chain rewired
    prev = "Unity Webhook" if kind == "create" else "Get Workflow"
    nxt = "Get Sample Workflow" if kind == "create" else "Set Bussiness Type"
    assert wf["connections"][prev]["main"][0][0]["node"] == "Vertical Prompt", f
    assert wf["connections"]["Vertical Prompt"]["main"][0][0]["node"] == nxt, f
    assert "id" in wf, f  # top-level id survived (UI-download strip gotcha)
print("ALL OK")
EOF
python3 Tools/n8n/inject-prompts.py --check; echo "exit=$?"
```
Expected: `ALL OK`, then 4 × `up-to-date`, `exit=0`.

- [ ] **Step 5: Commit**

```bash
git add Tools/n8n/inject-prompts.py Tools/n8n/workflows/
git commit -m "feat(n8n): Vertical Prompt code node + ternary head in Create/Edit workflows, injection script"
```

---

### Task 3: BusinessTypes.asset — 6 KZ verticals

**Files:**
- Modify: `Assets/Data/BusinessTypes.asset` (the `entries:` list only; keep `m_Script` guid `30eb05a9fb3f04c4e938f69368ead16c` and all other YAML untouched)

**Interfaces:**
- Consumes: existing sprites in `Assets/Images/BusinessIcons/` — CarService.png (guid `f0908e7d3a02d4a64a9e3762c748143b`), Cafe.png (`571c451210801489d9ed7a4453edf89b`), BeautySalon.png (`1dc112c1cb8c14a9f9dabe698b8bbfc2`), Dentist.png (`1b2b148d16add49d592f79984f8df7f9`), RealEstate.png (`f0bd99f9d28fd427294ee0569cb75a99`), TourAgency.png (`ac091ea6441804cb1be93c93033ef32c`), Flowers.png (`dc55d5606f8fe45c89489346f3c367d2`).
- Produces: entry ids `auto_parts | wholesale | flowers | kaspi_seller | education | phone_repair` (must equal Task 1 filenames and Task 4's `BusinessTypeId` values) with Russian displayNames used verbatim by e2e (Task 6): `Автозапчасти, Оптовый поставщик, Цветочный магазин, Продавец на Kaspi, Учебный центр, Ремонт телефонов`.

- [ ] **Step 1: View the 7 icons and finalize sprite assignment**

Read (as images) all 7 PNGs in `Assets/Images/BusinessIcons/`. Default assignment (adjust only if an icon is clearly better suited; no two entries may share a sprite):
auto_parts→CarService, wholesale→RealEstate, flowers→Flowers, kaspi_seller→Cafe, education→TourAgency, phone_repair→Dentist. Record the final table in the commit message.

- [ ] **Step 2: Replace the `entries:` block** with (sprite guids per Step 1's final table; `fileID: 21300000` kept for all):

```yaml
  entries:
  - id: auto_parts
    displayName: "Автозапчасти"
    sprite: {fileID: 21300000, guid: f0908e7d3a02d4a64a9e3762c748143b, type: 3}
    tileColor: {r: 0.5568628, g: 0.5568628, b: 0.5764706, a: 1}
  - id: wholesale
    displayName: "Оптовый поставщик"
    sprite: {fileID: 21300000, guid: f0bd99f9d28fd427294ee0569cb75a99, type: 3}
    tileColor: {r: 0.34509805, g: 0.3372549, b: 0.8392157, a: 1}
  - id: flowers
    displayName: "Цветочный магазин"
    sprite: {fileID: 21300000, guid: dc55d5606f8fe45c89489346f3c367d2, type: 3}
    tileColor: {r: 1, g: 0.1764706, b: 0.33333334, a: 1}
  - id: kaspi_seller
    displayName: "Продавец на Kaspi"
    sprite: {fileID: 21300000, guid: 571c451210801489d9ed7a4453edf89b, type: 3}
    tileColor: {r: 1, g: 0.58431375, b: 0, a: 1}
  - id: education
    displayName: "Учебный центр"
    sprite: {fileID: 21300000, guid: ac091ea6441804cb1be93c93033ef32c, type: 3}
    tileColor: {r: 0.1882353, g: 0.6901961, b: 0.78039217, a: 1}
  - id: phone_repair
    displayName: "Ремонт телефонов"
    sprite: {fileID: 21300000, guid: 1b2b148d16add49d592f79984f8df7f9, type: 3}
    tileColor: {r: 0.196, g: 0.678, b: 0.902, a: 1}
```
(Unicode escapes keep the .asset ASCII-safe; Unity reads them fine. If the existing file already contains raw UTF-8 elsewhere, raw Cyrillic strings are equally acceptable — pick one style for all six.)

- [ ] **Step 3: Verify entry count and ids**

Run: `grep -c "  - id: " Assets/Data/BusinessTypes.asset && grep "  - id: " Assets/Data/BusinessTypes.asset`
Expected: `6` and exactly the six new ids (no `car_service`, `cafe`, `beauty_salon`, `dentist`, `real_estate`, `tour_agency` remain).

- [ ] **Step 4: Commit** (asset only; its .meta is unchanged)

```bash
git add Assets/Data/BusinessTypes.asset
git commit -m "feat(bots): business types -> 6 KZ Tier-1 verticals (RU names, stand-in icons: <final table>)"
```

---

### Task 4: Manager.cs — send BusinessTypeId in all 5 payload sites

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (5 one-line insertions; line numbers pre-change: 2559, 2649, 2697, 2788, 2995)

**Interfaces:**
- Consumes: `businessTypes` (BusinessTypesSO, serialized field), `selectedBusinessId` (wizard selection), `openBotSettings.BusinessTypeDropdown` (dropdown order mirrors the SO via `PopulateBusinessTypeDropdown`).
- Produces: form field `BusinessTypeId` on webhooks CreateWhatsappWorkflow, CreateTelegramWorkflow, EditWhatsappWorkflow, EditTelegramWorkflow. Value ∈ the six ids or `""`.

- [ ] **Step 1: Wizard create sites (selectedBusinessId).** After line 2559 (`CreateWhatsappWorkflowFromStart`):

```csharp
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt1) ? bt1.displayName : "");
        form.AddField("BusinessTypeId", selectedBusinessId ?? "");
```

Same insertion after line 2697 (`CreateTelegramWorkflowFromStart`, the `bt2` line).

- [ ] **Step 2: Dropdown sites.** After each of the three lines reading `form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);` (create-WA-from-settings ~2649, create-TG-from-settings ~2788, `SaveWorkflows` ~2995) insert:

```csharp
        form.AddField("BusinessTypeId", businessTypes.TryGetByIndex(openBotSettings.BusinessTypeDropdown.value, out var btEntry) ? btEntry.id : "");
```

(Each site is a different method — the `btEntry` name does not collide. Verify with the compiler in Step 3.)

- [ ] **Step 3: Compile + EditMode suite**

Editor open → `mcp__mcp-unity__recompile_scripts`, then trigger the test bridge (`Temp/claude/run-tests.trigger` → read `Temp/claude/test-summary.json`). Editor closed → `Tools/run-tests-headless.sh`.
Expected: 0 compile errors; suite green (665+ tests, same count as before this task).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "feat(bots): send BusinessTypeId to n8n Create/Edit webhooks (5 payload sites)"
```

---

### Task 5: Roll out to local n8n

**Files:**
- Create: `/private/tmp/claude-501/.../scratchpad/live-wf/*.json` (scratch only, not committed)

**Interfaces:**
- Consumes: Task 2's script (`inject-prompts.py <paths>` mode); n8n-mcp tools (`search_workflows`, `get_workflow_details`, `update_workflow`, `publish_workflow`) against the LOCAL instance (localhost:5678; `n8n start` must be running — ask the owner to start it if unreachable).
- Produces: 4 live workflows containing the Vertical Prompt node, published.

- [ ] **Step 1: Fetch the 4 live workflows** via n8n-mcp (`search_workflows` for names `CreateWhatsappWorkflow`, `CreateTelegramWorkflow`, `Edit Whatsapp Workflow`, `Edit Telegram Workflow`; then `get_workflow_details` each). Save each as JSON to the scratchpad `live-wf/` dir with top-level `{name, nodes, connections, settings, id}` (the script only reads/writes `nodes` + `connections`; keep whatever else the fetch returns).
**Do NOT overwrite live workflows with the repo JSONs** — live copies have local-URL adaptations in their HTTP nodes; the repo files keep cloud URLs. While here, note where each live workflow's `Get Sample Workflow`/`Get Workflow`/`Create Workflow`/`Update Workflow` node URLs point (expected `http://localhost:5678/api/v1/...`) — Task 6 asserts against that same API.

- [ ] **Step 2: Transform the fetched copies**

Run: `python3 Tools/n8n/inject-prompts.py <scratchpad>/live-wf/*.json`
Expected: 2 × `kind=create CHANGED`, 2 × `kind=edit CHANGED`. If the script errors with "expected expression fragment not found", the live copy's Set Fields diverged from canonical — STOP and diff it against the repo file before proceeding; do not guess.

- [ ] **Step 3: Push + publish** each via n8n-mcp `update_workflow` (send back the transformed `nodes` + `connections`, everything else unchanged), then `publish_workflow`. If mcp update is unavailable/fails, fallback: `curl -X PUT http://localhost:5678/api/v1/workflows/<id> -H "X-N8N-API-KEY: <key>" -H "Content-Type: application/json" -d '{"name":...,"nodes":...,"connections":...,"settings":...}'` (API key: ask the owner; do not hunt through ~/.n8n).

- [ ] **Step 4: Verify** via `get_workflow_details`: each of the 4 has exactly one `Vertical Prompt` node wired per Task 2 Step 4's chain assertions, and is active/published.

---

### Task 6: e2e through the real webhooks

**Files:** none committed (scratch assertions only).

**Interfaces:**
- Consumes: local webhook base `http://localhost:5678/webhook/`; local n8n API (same base as discovered in Task 5 Step 1) for fetching created bot workflows; displayNames/ids from Task 3; prompt first-lines from Task 1.

Notes that apply to every scenario: Unity sends multipart (WWWForm) → mimic with `curl -F`; use bash `$'...'` strings for embedded newlines. The Create workflows call Wappi after creating the bot workflow — with a fake profile id that HTTP call may fail and the webhook may return an error/empty response; that is EXPECTED — assert by finding the created workflow via the n8n API (search by name), not via the webhook response body. Deactivate+delete every created test workflow afterwards.

- [ ] **Step 1: Create with known id (flowers)**

```bash
curl -s -X POST http://localhost:5678/webhook/CreateWhatsappWorkflow \
  -F "Name=E2E-VP-Flowers" -F $'BusinessType=Цветочный магазин' -F "BusinessTypeId=flowers" \
  -F "WhatsappProfileId=e2e-vp-test" -F $'Prompt=Тестовые доп. инструкции' \
  -F $'Business=About Business:\nАдрес: Алматы. Часы: 9–18.' \
  -F $'ProductsList=Product1: Букет Ромашки\nProduct1 Price: 5000' -F "ServicesList="
```
Fetch the workflow named `E2E-VP-Flowers` via the n8n API; let `sm` = its `nodes[5].parameters.options.systemMessage`. Assert:
- `sm` starts with `Business Type: Цветочный магазин\n\nТы — консультант цветочного магазина.`
- `sm` contains `ОБЩИЕ ПРАВИЛА` before `\n\nAdditional Instructions: Тестовые доп. инструкции`
- `sm` contains `About Business: About Business:` (tail quirk untouched) and `Букет Ромашки`.

- [ ] **Step 2: Create without BusinessTypeId (legacy)** — same curl, `Name=E2E-VP-Legacy`, drop the `BusinessTypeId` field, `BusinessType=Flowers`. Assert `sm` starts `Business Type: Flowers\n\nYou are an intelligent assistant.` (the generic template prompt — current behavior preserved).

- [ ] **Step 3: Edit with type change** — POST to `/webhook/EditWhatsappWorkflow`:

```bash
curl -s -X POST http://localhost:5678/webhook/EditWhatsappWorkflow \
  -F "WhatsappWorkflowId=<id of E2E-VP-Flowers>" -F "Name=E2E-VP-Flowers" \
  -F $'BusinessType=Учебный центр' -F "BusinessTypeId=education" \
  -F $'Prompt=Новые доп. инструкции' -F $'Business=Адрес: Астана.' \
  -F "ProductsList=" -F "ServicesList="
```
Re-fetch; assert `sm` starts `Business Type: Учебный центр\n\nТы — онлайн-администратор учебного центра.`, contains `Additional Instructions: Новые доп. инструкции`, and contains NO text from the flowers prompt (`консультант цветочного` absent).

- [ ] **Step 4: Edit without BusinessTypeId (legacy preserve-head)** — same POST minus `BusinessTypeId`, `BusinessType=Education Center Legacy`. Assert line 1 becomes `Business Type: Education Center Legacy` AND the education prompt body (`Ты — онлайн-администратор`) is still present (head preserved, only line 1 swapped).

- [ ] **Step 5: Cleanup** — deactivate + `DELETE /api/v1/workflows/<id>` for `E2E-VP-Flowers` and `E2E-VP-Legacy`. Verify both gone. Report all 4 scenario results.

---

### Task 7: Docs + final green

**Files:**
- Modify: `docs/prompt-templates-kz-tier1.md` (prepend one paragraph after the H1)
- Modify: `CLAUDE.md` (External APIs → n8n webhook endpoints section)

**Interfaces:** none downstream.

- [ ] **Step 1:** Prepend to `docs/prompt-templates-kz-tier1.md` after the title:

```markdown
> **Статус (2026-07-06):** эти шаблоны встроены в воркфлоу как ОСНОВНЫЕ промпты — Create/Edit
> воркфлоу подставляют их автоматически по `BusinessTypeId` (узел Vertical Prompt). Канонический
> источник текстов: `Tools/n8n/prompts/*.md` (правки — через `Tools/n8n/inject-prompts.py`).
> Поле Промпт в настройках бота = только Additional Instructions. Этот документ остаётся как
> аннотированная версия с обоснованием и таблицами плейсхолдеров (плейсхолдеры теперь
> закрываются разделом About Business).
```

- [ ] **Step 2:** In CLAUDE.md, in the n8n **Webhook endpoints** bullet for `/webhook/CreateWhatsappWorkflow`/`CreateTelegramWorkflow`/`Edit*`, append one sentence: forms now include `BusinessTypeId` (stable kebab-case id from `BusinessTypes.asset`); a `Vertical Prompt` Code node in all 4 workflows injects the vertical main prompt from `Tools/n8n/prompts/*.md` (composed with `_core.md`, injected by `Tools/n8n/inject-prompts.py`); empty/unknown id falls back to the legacy generic prompt (Create) / preserve-head (Edit). Business types are now the 6 KZ Tier-1 verticals.

- [ ] **Step 3: Final suite + commit**

Re-run the EditMode suite (same method as Task 4 Step 3) — green. Then:

```bash
git add docs/prompt-templates-kz-tier1.md CLAUDE.md
git commit -m "docs: vertical main prompts — canonical sources, BusinessTypeId, updated business types"
```

---

## Self-review notes (already applied)

- Spec coverage: §1 Unity → Tasks 3–4; §2 workflows → Task 2 (+5 rollout); §3 prompt sources/script/doc note → Tasks 1, 2, 7; §4 compat → Task 2 ternary + Task 6 Steps 2/4; §5 rollout/e2e → Tasks 5–6. Optional Бизнес-field hint: CUT (EditableTextArea has no placeholder member — spec's cut condition met).
- Type consistency: node name `Vertical Prompt`, field `verticalPrompt`, form field `BusinessTypeId`, ids = filenames = asset ids (checked across Tasks 1/2/3/4/6).
- The `.slice(1)` in CREATE_OLD strips the template's leading `=`; the fallback branch keeps it — do not "fix".
