# Choose Reply Landing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Продающий одностраничник на choosereply.com с приёмом заявок в Supabase через n8n + юр-страницы на вшитых путях.

**Architecture:** Статика (1 HTML-файл, инлайн CSS/JS, робот-SVG инлайном) на уже работающем Caddy; форма шлёт form-encoded POST на новый n8n-воркфлоу `LandingLead` (webhook → валидация → INSERT в `landing_leads`). Источник страницы живёт в репо `Tools/landing/`, деплой = scp в `~/choosereply/site/`.

**Tech Stack:** HTML/CSS/vanilla JS, Manrope (Google Fonts), n8n 2.36 (prod `https://n8n.choosereply.com`, API-ключ `Tools/n8n/.secrets/prod-api-key.txt`), Supabase Postgres (n8n-credential `vvRrFiEXzLVqKjOx`), VPS по ssh-алиасу `choosereply`.

## Global Constraints

- Спека: `docs/superpowers/specs/2026-08-27-landing-page-design.md` — копирайт и цены оттуда, вербатим.
- Вся страница RU-only. Цены: Старт 9 990 ₸/мес (год 99 000), Бизнес 19 990 ₸/мес (год 198 990, «популярный»), Сеть 39 900 ₸/мес (год 399 990); триал 5 дней/150 диалогов; докупка 500 диалогов 3 900 ₸.
- Юр-тексты `docs/legal/{privacy,terms}.html` НЕ редактируются — деплой как есть на `/privacy.html` `/terms.html`.
- Палитра: bg `#0B0E14`, surface `#121722`, border `#1E2633`, ink `#E8EAF0`, ink-2 `#9AA3B2`, accent `#22D3EE` (текст на accent-кнопках `#06252C`). Радиусы 16–20, max-width 1080, Manrope 400/600/800 + системный fallback.
- Форма: fetch, `Content-Type: application/x-www-form-urlencoded` (без CORS-preflight), поля `name`, `phone`, `website` (honeypot, скрыт), `source=landing`.
- Классификатор может блокировать DDL/записи и крон: fallback-паттерн — готовый скрипт в `Tools/n8n/.secrets/` и одна команда владельцу (как `fix-subscriber.py`).
- Git: коммитить только свои файлы по путям (параллельные сессии — общий worktree).

---

### Task 1: Таблица заявок `landing_leads`

**Files:**
- Create: `Tools/n8n/sql/2026-08-27-landing-leads.sql`

**Interfaces:**
- Produces: таблица `landing_leads(id, name, phone, source, user_agent, created_at)` — Task 2 вставляет в неё INSERT'ом с 4 параметрами.

- [ ] **Step 1: миграция-файл**

```sql
-- Landing lead intake (spec docs/superpowers/specs/2026-08-27-landing-page-design.md).
-- Applied to prod Supabase via one-off n8n harness on 2026-08-27.
create table if not exists landing_leads (
  id bigint generated always as identity primary key,
  name text,
  phone text not null,
  source text,
  user_agent text,
  created_at timestamptz not null default now()
);
alter table landing_leads enable row level security;
revoke all on landing_leads from anon, authenticated;
```

- [ ] **Step 2: применить на проде** — одноразовый harness (webhook→Postgres, как диагностика 2026-08-27): создать через `POST /api/v1/workflows`, activate, дёрнуть, удалить. При блокировке классификатором — записать скрипт `Tools/n8n/.secrets/apply-landing-leads.py` (тот же код) и отдать владельцу командой.

- [ ] **Step 3: проверить** — read-only harness/запрос `select count(*) c from landing_leads;` → `{"c": 0}`.

- [ ] **Step 4: commit**

```bash
git add Tools/n8n/sql/2026-08-27-landing-leads.sql && git commit -m "feat(landing): landing_leads table migration"
```

### Task 2: n8n-воркфлоу `LandingLead` + канонический экспорт

**Files:**
- Create: `Tools/n8n/workflows/<prod-id>-Landing_Lead.json` (id придёт от прода)
- Modify: `Tools/n8n/README.md` (Layout: «the 16 workflows» → 17; строка в таблицу канона; упоминание в Prod-секции)

**Interfaces:**
- Consumes: `landing_leads` из Task 1; Postgres-credential `vvRrFiEXzLVqKjOx`.
- Produces: публичный `POST https://n8n.choosereply.com/webhook/LandingLead`, ответы `{success:true}` | `{success:false,error:"bad_request"}` | `{success:false,error:"db"}` — Task 3 зовёт его из формы.

- [ ] **Step 1: собрать и задеплоить воркфлоу** (POST /api/v1/workflows + activate). Скелет (полные параметры — по образцу диагностического harness и Set Reply Mode):

```
Webhook (POST, path "LandingLead", responseMode "responseNode")
→ Validate (Code):
    b = $input.first().json.body или {}; trim всех полей;
    website непустой → action="silent";
    phone отсутствует, длина <6 или >32, или не соответствует /^[0-9+()\-\s]+$/ → action="bad";
    иначе action="insert", name=name.slice(0,80), phone, source=(source||"landing").slice(0,40),
    ua=(headers["user-agent"]||"").slice(0,200)
→ Switch по action:
    insert → Insert Lead (Postgres executeQuery, onError: continueErrorOutput):
        insert into landing_leads (name, phone, source, user_agent) values ($1,$2,$3,$4);
        queryReplacement = "={{ [ $json.name, $json.phone, $json.source, $json.ua ] }}"  // ОДНО выражение-массив (гочта RevenueCat)
        main → Respond OK {success:true} ; error-выход → Respond DB {success:false,error:"db"}
    silent → Respond OK (боту не сообщаем, вставки нет)
    bad → Respond Bad {success:false,error:"bad_request"}
```

- [ ] **Step 2: пробы** (curl, form-encoded): валидный → `{success:true}` и `count=1` (read-harness); honeypot (`website=x`) → `{success:true}`, count не вырос; кривой телефон (`phone=abc`) → `bad_request`, count не вырос; `GET /webhook/LandingLead` → 404 c GET-подсказкой.

- [ ] **Step 3: канонизировать** — `GET /api/v1/workflows/<id>` → сохранить в `Tools/n8n/workflows/<id>-Landing_Lead.json` с тем же набором полей, что у существующих файлов (сверить ключи с `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json`; top-level `id` обязателен). README: счётчик и таблица. Проверить `grep -n "16" Tools/n8n/verify-telegram-parity.py Tools/n8n/README.md` — гейты не должны пинить число воркфлоу (если пинят — поправить).

- [ ] **Step 4: commit**

```bash
git add Tools/n8n/workflows/*Landing_Lead.json Tools/n8n/README.md && git commit -m "feat(landing): LandingLead intake workflow (prod) + canonical export"
```

### Task 3: Страница `Tools/landing/index.html` + юр-файлы + favicon/OG

**Files:**
- Create: `Tools/landing/index.html`, `Tools/landing/favicon.svg` (+ `favicon.png`, `og.png` если qlmanage отрендерит), копии `Tools/landing/{privacy,terms}.html` из `docs/legal/` (байт-в-байт)

**Interfaces:**
- Consumes: вебхук из Task 2 (form-encoded POST, поля `name/phone/website/source`).
- Produces: статические файлы для Task 4.

- [ ] **Step 1: загрузить скилл frontend-design** (домовое правило перед UI-работой), затем сверстать страницу по копирайт-деку ниже — секции из спеки, один файл, инлайн-CSS/JS, робот `Tools/hero_robot.svg` инлайном, плавные якоря, форма с disabled-состоянием на время отправки.

Копирайт-дек (вербатим в страницу):
- H1 «ИИ-продавец в вашем WhatsApp»; sub «Choose Reply отвечает клиентам за вас — круглосуточно, с вашего номера, по вашим ценам. Каждый ответ под контролем: бот пишет сам или предлагает варианты, а выбираете вы.»; бейджи «Скоро в App Store» / «Скоро в Google Play»; CTA «Получить ранний доступ», микротекст «Бесплатный пробный период — 5 дней».
- Шаги: «Подключите номер» / «WhatsApp или Telegram — по QR-коду за пару минут. Отдельная SIM не нужна.»; «Загрузите прайс» / «Фото, Excel или PDF — бот выучит ваши товары, цены и услуги.»; «Бот продаёт» / «Отвечает на вопросы, называет цены, собирает заказы. Сам — или с вашего одобрения.»
- Карточки: «Авто и Вместе» / «Бот отвечает сам — или предлагает четыре готовых ответа, а вы выбираете одним касанием.»; «Знает ваши цены» / «Загрузите прайс-лист фото или файлом — бот отвечает по вашим товарам, а не выдумывает.»; «WhatsApp + Telegram» / «Один бот на оба мессенджера. Все переписки — в одном приложении.»; «Сводка диалогов» / «Кто оформил заказ, кто ждёт ответа, где нужен владелец — итоги без чтения переписок.»; «С вашего номера» / «Клиенты пишут на привычный номер бизнеса — для них ничего не меняется.»; «Живой русский язык» / «Отвечает естественно и вежливо, как хороший продавец, — без роботных шаблонов.»
- Ниши: «Автозапчасти · Оптовая торговля · Цветы · Kaspi-продавцы · Обучение · Ремонт телефонов — и любой бизнес, где клиенты пишут в мессенджеры.»
- Тарифы: плашка «Начните бесплатно: 5 дней и 150 диалогов — без карты»; карточки Старт «Для одной точки» 9 990 ₸/мес · 1 бот · 1 канал · 300 диалогов/мес; Бизнес «Для растущего бизнеса» 19 990 ₸/мес · 3 бота · 3 канала · 1 000 диалогов/мес, бейдж «Популярный»; Сеть «Для сети точек» 39 900 ₸/мес · 5 ботов · 5 каналов · 3 000 диалогов/мес; сноска «При оплате за год — около двух месяцев в подарок. Закончились диалоги — докупите 500 за 3 900 ₸. Оплата — подпиской в приложении.»
- Форма: H2 «Получите ранний доступ»; sub «Оставьте номер — напишем в WhatsApp, когда откроем доступ, и поможем настроить бота под ваш бизнес.»; поля «Как к вам обращаться» (name, опц.), «+7 ___ ___-__-__» (phone, required); кнопка «Оставить заявку»; успех «Спасибо! Мы напишем вам в WhatsApp.»; ошибка «Не получилось отправить. Попробуйте ещё раз чуть позже.»
- Футер: «© 2026 Choose Reply» · «Политика конфиденциальности» → `/privacy.html` · «Условия использования» → `/terms.html`.
- Мета: `<title>Choose Reply — ИИ-продавец в WhatsApp и Telegram</title>`; description «ИИ-бот отвечает клиентам с вашего номера WhatsApp: знает ваш прайс, собирает заказы, работает 24/7. Для малого бизнеса Казахстана и СНГ.»; og:title/og:description те же; lang="ru".

- [ ] **Step 2: favicon/OG best-effort** — `qlmanage -t -s 64` по `favicon.svg` (упрощённая голова робота на циановом круге) → `favicon.png`; композитный `og.svg` 1200×630 (тёмный фон, робот, wordmark) → `og.png`. Не вышло — остаёмся на SVG-favicon без og:image, не блокер.

- [ ] **Step 3: прочитать юр-файлы целиком** (обязательное чтение перед публикацией), убедиться в самодостаточности (без внешних ссылок на ассеты), скопировать в `Tools/landing/` без изменений.

- [ ] **Step 4: локальная проверка** — `python3 -m http.server 8123` из `Tools/landing/` (run_in_background) + браузер-панель: скриншоты десктоп и 375px; проверить якоря, контраст, переносы заголовков; сервер остановить.

- [ ] **Step 5: commit**

```bash
git add Tools/landing && git commit -m "feat(landing): choosereply.com page + legal copies + icons"
```

### Task 4: Деплой, живые проверки, докование

**Files:**
- Modify: память `project_choosereply_vps.md` (лендинг + лиды), `Tools/n8n/README.md` уже из Task 2
- Commit untracked: `Tools/n8n/server-backup.sh` (отдельным ops-коммитом)

**Interfaces:**
- Consumes: файлы Task 3, вебхук Task 2.

- [ ] **Step 1: деплой** — `scp Tools/landing/*.html Tools/landing/favicon.* [og.png] choosereply:~/choosereply/site/` (индекс перезаписывает заглушку).

- [ ] **Step 2: живые проверки** — `curl -s -o /dev/null -w "%{http_code}"` = 200 для `/`, `/privacy.html`, `/terms.html`; тестовая заявка с телефоном `+7 700 000-00-00`, именем `test-claude` → `{success:true}`; браузер-панель на живом URL (десктоп + mobile 375) — скриншоты владельцу.

- [ ] **Step 3: доки/память** — в память VPS: лендинг живой, форма → `landing_leads`, канонический воркфлоу; напоминание владельцу в финальном сообщении: жёлтые реквизиты в юр-доках, просмотр заявок (через меня / n8n executions; Telegram-уведомления — fast-follow), бэкап-команда если не запускал.

- [ ] **Step 4: commits**

```bash
git add Tools/n8n/server-backup.sh && git commit -m "ops(server): nightly n8n backup script (installed on VPS via owner command)"
```

## Self-Review

- Покрытие спеки: страница (T3), юр-пути (T3/T4), таблица (T1), воркфлоу+канон+README (T2), деплой+проверки (T4), fallback на классификатор (T1/Global) — всё закрыто.
- Плейсхолдеров нет; типы/имена согласованы (`landing_leads` 4-поля INSERT = Validate-выход; form-поля = вебхук-поля).
- Скоуп одного плана — ок.
