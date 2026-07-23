# UI Redesign Prompt — for external AI platforms

Two ready-to-paste prompts for generating a fresh UI take on the app:
**Prompt 1** for chat/code AIs (Claude, ChatGPT, Gemini, v0, Lovable, Bolt, DeepSeek) → one interactive HTML mockup.
**Prompt 2** for image generators (Midjourney, GPT-image, Imagen) → visual-direction boards.

Both are self-contained — the target AI needs zero knowledge of this repo.
Sizes in the prompts are mockup dp; port the winning design to Unity later via
`unity-ui-builder` (dp × 3 = canvas reference units).

---

## Prompt 1 — interactive HTML mockup (main)

```
# Design brief: mobile UI for an AI sales assistant that lives in the owner's WhatsApp

You are a world-class senior mobile product designer with strong opinions and impeccable craft. Design a complete, production-grade mobile UI from this brief and deliver it as ONE self-contained interactive HTML file (output spec at the end). Do not ask clarifying questions — make senior-designer decisions and ship the full result in one response. Invent your own visual identity: this is a fresh reimagining, not a restyle.

## The product
A mobile app (Android-first) for small-business owners in Kazakhstan and the CIS. It connects an AI sales assistant to the owner's OWN WhatsApp and Telegram numbers: the bot answers customers 24/7, collects orders, and knows the owner's products, services, and uploaded price lists. The owner's phone is mission control — there is no desktop version. The app has no final brand name: use a tasteful neutral wordmark placeholder or propose one.

The signature concept is the automation spectrum, always one tap away:
- «Авто» — the bot replies to customers fully autonomously.
- «Вместе» — co-pilot mode: the bot proposes ready reply options, the owner taps one to send.
The active mode must be glanceable everywhere it matters, and switching must feel safe and reversible (confirmation: «Сменить режим?» — «Бот перестанет отвечать сам — он будет предлагать варианты ответа, а вы выберете.»).

## The user
Non-technical business owners, 25–55: flower shops, auto-parts stores, phone-repair shops, education centers, wholesalers, Kaspi marketplace sellers. Mid-range Android phones, they live inside WhatsApp all day. They are entrusting the app with their main sales channel — their personal WhatsApp number — so the emotional job of every screen is TRUST + CONTROL: "I see everything, I can step in anytime, nothing happens behind my back." The design must feel like a reliable professional assistant — calm and warm, never a toy, never a crypto app.

## Information architecture
Bottom navigation, 4 tabs: «Боты» · «Чаты» · «Сводка» · «Профиль». Design the first three tabs fully, plus two drill-in screens (open conversation and bot settings). «Профиль» may be a visual stub. Six views total:

### 1. «Боты» — home: the owner's bots
Cards, one per bot. Each card: bot name, business-type chip, channel badges (WhatsApp / Telegram) with connection state, an unmissable running state — «Бот работает» (positive) / «Бот на паузе» (clearly dormant, not alarming) — with a pause/resume switch, and the reply-mode indicator («Авто» / «Вместе»). Primary CTA «Создать бота». Also design the zero-bots empty state: headline «Создайте первого бота», subline «Бот-ассистент отвечает клиентам в WhatsApp круглосуточно», CTA «Создать бота».

### 2. «Чаты» — conversations of the selected bot
Top: compact switcher between the owner's bots. List rows: avatar, client name, last-message preview (prefix «Бот: » when the assistant wrote it), time, unread badge. The bot-wide Авто/Вместе toggle lives here too.

### 3. Open conversation (drill-in from Чаты)
Messaging mechanics must read instantly as familiar (incoming left, outgoing right, date separators, sent/delivered/read ticks, image message, voice message with duration) — familiar mechanics, but YOUR visual skin, not a WhatsApp clone. Include a reply-quote card on one message. The hero moment: the «Вместе» panel above the input — 3 AI-proposed reply cards, top suggestion visually favored, tap-to-send; plus a normal text input. Make this panel feel native to your design language, obviously AI-powered but calm.

### 4. «Сводка» — outcomes dashboard
Period filter (Сегодня / Неделя / Месяц) and a per-bot filter. Headline stats with deltas vs the previous period. Distribution of the 5 conversation outcomes — keep them semantically colored and harmonized with your palette (current reference: «Заявка» green #34C759, «Нужен владелец» orange #F57C00, «В диалоге» blue #007AFF, «Клиент замолчал» gray #8E8E93, «Вопрос закрыт» #65676B — you may re-tune the hues, not the meanings). Below: recent conversations list — avatar, client name, one-line AI summary of the outcome, status pill, relative time; a row taps through to its chat. «Заявка» rows are the money moment — let them feel quietly rewarding.

### 5. Bot settings (drill-in from a bot card)
Grouped sections, your structure: Основное (name, business type, per-channel connection with reconnect affordance) · О бизнесе · Товары и услуги (item cards with ₸ prices, add button) · Прайс-листы (uploaded file rows: name, size, date, delete ✕; add-file affordance) · Промпт (additional-instructions textarea) · danger zone «Удалить бота» (deliberately hard to hit).

### 6. One trust moment
Show the mode-switch confirmation dialog (copy above) or the pause confirmation — somewhere reachable in the mockup.

## Realistic sample data — use exactly this flavor, never lorem ipsum
- Bots: «Гульдер» (цветочный магазин, WhatsApp + Telegram, работает, Авто) · «Автозапчасти Алмас» (WhatsApp, на паузе, Вместе) · «Sapa Education» (языковые курсы, WhatsApp, работает, Вместе).
- Clients: Айгерим, Данияр, Мадина, Ерлан, Асель, +7 707 555 12 34.
- Flower-shop chat: client «Здравствуйте! Есть букет из роз на завтра? И сколько доставка по Алматы?» → bot «Здравствуйте! Да, есть 🌹 15 роз — 12 500 ₸, 25 роз — 19 900 ₸. Доставка по Алматы — 1 500 ₸. Какой букет оформим?» → client «Давайте 25 роз к 14:00». «Вместе» suggestions: «Отлично! 25 роз к 14:00 ✅ Подскажите адрес доставки?» · «Принято! Оплата Kaspi переводом или наличными курьеру?» · «Добавить открытку к букету? (+500 ₸)».
- Dashboard (Неделя): Заявки 14 (+3), Диалогов 47, Нужен владелец 3, средний ответ 28 сек. Recent: Айгерим — «Заказ: букет 25 роз, доставка завтра к 14:00» — Заявка — 5 мин назад · Данияр — «Колодки на Camry 70 — нет в базе, нужен ответ владельца» — Нужен владелец — 12 мин назад · Мадина — «Уточнила расписание IELTS, обещала подумать» — Клиент замолчал — 1 ч назад · Ерлан — «Узнал график работы» — Вопрос закрыт — вчера.
- Products: «Букет 15 роз — 12 500 ₸» · «Букет 25 роз — 19 900 ₸» · «Доставка по Алматы — 1 500 ₸». Files: «прайс_март.xlsx · 48 КБ · 12 июля».

## Design rules
- Distinctive identity, no defaults: no purple-gradient SaaS, no cookie-cutter component-library look, no WhatsApp skin. If a screen is coming out generic, make it more opinionated.
- One font family with full Cyrillic support (Inter / Manrope / Golos Text / Onest — pick one; load the cyrillic subset). Max 4 sizes + 2 weights. Tabular numerals for stats.
- 60/30/10 color: calm neutral base, dark text, ONE accent reserved for primary actions and the «работает» state. Semantic status colors stay distinguishable.
- 8pt spacing grid, generous whitespace, soft tinted shadows (never harsh gray on colored backgrounds), consistent radii.
- Thumb zone: primary actions in the bottom third; destructive actions never easy-reach; touch targets ≥ 44pt.
- All UI text in Russian. Cyrillic runs ~15% longer than English — demonstrate correct truncation/wrapping with the provided strings.
- Include at least one skeleton/loading state and the empty state; subtle micro-delight on the peak moments (new «Заявка», bot going live) — micro, not confetti.
- Light theme primary; a dark variant only if it costs no quality.

## Output requirements
- ONE self-contained HTML file: all CSS in <style>, all behavior in vanilla JS, all icons inline SVG. No external libraries; Google Fonts only (cyrillic subset).
- Phone frame 390×844 centered on a neutral page backdrop; content scrolls inside the frame.
- Navigation must work: bottom tabs switch screens, a chat row opens the conversation, a bot card opens settings, back affordances return. Simple show/hide is fine.
- Micro-interactions in CSS: pressed states, the Авто/Вместе thumb sliding, smooth screen transitions.
- End the file with an HTML comment block of design tokens: palette (hex + role), type scale, spacing scale, radii, shadow recipe — so the design can be ported to native code.
- Before finishing, self-check: all 6 views reachable · zero lorem ipsum and zero English UI strings · no Cyrillic overflow · one accent color, clear hierarchy on every screen · looks like a flagship App Store screenshot, not a wireframe · tokens comment present.
```

---

## Prompt 2 — image generators (visual direction only)

```
UI/UX design presentation board, four modern smartphone app screens side by side on a soft neutral studio background with gentle shadows. The app: an AI sales assistant for small businesses that runs on the owner's own WhatsApp. Screen 1 — home with bot cards, status pills «работает» in green, pause switches. Screen 2 — chat with messaging bubbles and an AI suggestion panel of three tappable reply cards above the input. Screen 3 — analytics dashboard with colored outcome chips (green, orange, blue, gray) and a recent-orders list with avatars. Screen 4 — bot settings with product cards showing prices in tenge and a file list. Clean contemporary mobile UI, trustworthy calm professional style with warmth, one restrained accent color, soft tinted shadows, 8pt grid, rounded cards, light theme, minimal Russian interface labels, high fidelity, top-tier portfolio quality.
```

Midjourney: append `--ar 7:4`. Expect garbled Cyrillic from any image model — judge layout, mood, and color, not the text.

---

## How to use

- **Claude (claude.ai)** — paste Prompt 1 as-is; it will build the artifact directly.
- **ChatGPT / Gemini / DeepSeek** — paste as-is; if it tries to split output, add: "Output the complete HTML file in a single code block."
- **v0 / Lovable / Bolt** — paste as-is; they may produce React instead of one file — fine for judging visuals.
- **Google Stitch** — takes short briefs: paste only "The product", "The user", and the six view descriptions.
- Run each platform **twice** — variance between runs is large.
- Compare the **open conversation** and **Сводка** screens first; they degrade first on weaker platforms.
- Judge: hierarchy at a squint, Cyrillic handling, and whether the «Вместе» panel feels native or bolted-on.
- The winner's design-tokens comment (palette/type/spacing) is the porting spec for Unity (`unity-ui-builder`, dp × 3).
