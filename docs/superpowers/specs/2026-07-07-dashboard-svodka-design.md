# «Сводка» — Conversation Results Dashboard + Navigation Restructure

**Date:** 2026-07-07
**Status:** Approved design (brainstorm complete), pending implementation plan
**Mockup:** Variant B «Аналитика» — https://claude.ai/code/artifact/f1fefe9f-cc6f-4b10-81d7-b504b5a568b8

## 1. Overview

Replace the «New» bottom tab (index 2, currently the Add Bot form `Screen_New`) with a new
**«Сводка»** dashboard tab that shows per-conversation *results* — real outcomes classified
server-side by n8n from the bot transcripts already stored in Supabase. The Add Bot form moves
behind the Bots page: opened by the existing header plus button when bots exist, opened
automatically when the Bots tab is pressed with zero bots.

Approved scope decisions:

- **Real outcomes via n8n** (not client-only proxies): a new classification workflow + Supabase
  table + read webhook. No changes to the hot per-message bot-reply path.
- **WhatsApp traffic only in v1.** The app has no Telegram chat pipeline (every chat/message
  call in `ChatManager` is `wappi.pro/api/sync`; `Screen_Telegram` is a static stub), so
  Telegram outcome rows would have no names, avatars, or deep-links. The app therefore sends
  only `whatsappProfileId`s to the webhook; the server design is channel-agnostic and Telegram
  coverage becomes a follow-up once a Telegram chat pipeline exists (§8).
- **Funnel-lite taxonomy, 5 statuses** (see §3).
- **All bots aggregated by default + bot filter chips** (chips hidden with a single bot).
- **Variant B «Аналитика» layout**: period selector, hero заявки count with delta, stacked
  funnel bar with legend, tappable status rows, recent заявки list.

## 2. Navigation restructure

### 2.1 Tab 2: «New» → «Сводка»

- `BottomTabManager` TabData slot 2 keeps its position; the scene edit (via builder) changes:
  `tabName` → `Сводка`, label text, new inactive/active icon sprites (line-chart glyph, rendered
  through the existing Tools icon pipeline into `Assets/Images/`), `activeLabelColor` → #1B7CEB,
  `screenPanel` → new `Screen_Dashboard` (new top-level screen, sibling of the other `Screen_*`).
- No `BottomTabManager.cs` code changes are required for switching; the dashboard refreshes via
  its own `OnEnable` (see §5.4). `defaultTabIndex` stays 0 (WhatsApp) — launch behavior unchanged.

### 2.2 `Screen_New` (Add Bot form) presentation

- `Screen_New` **stays a top-level screen GameObject** — it simply stops being any tab's
  `screenPanel`. All `Manager` wiring (form rows, 4 selector popups, auth pages,
  `CreateBotFromForm`) is untouched. Note: `Manager.AddBotFormPage` is a *dead field* today
  (declared, used nowhere) — the new open/close code becomes its first real consumer.
- **Closing is now our job.** Today `Screen_New` is closed only as a side effect of being
  tab 2's panel (`ApplyTabState` sets every non-active tab's panel inactive). After the
  restructure, two explicit close paths replace that:
  1. **Creation success**: the `ShowAuthSuccess` path closes `Screen_New` (SetActive false)
     before/along with its existing `SwitchTab(BotsTabIndex)` — otherwise the form would stay
     active forever.
  2. **Any bottom-tab switch** while the form is open closes it instantly (no animation) —
     replicating today's `ApplyTabState` semantics exactly, including mid-wizard behavior
     (the creation coroutine and auth pages manage themselves, same as today).
- It gains a slide-in/slide-out presentation (DOTween `DOAnchorPosX`, 0.3s OutCubic in /
  0.25s InCubic out — same as `ProfileSubPages`) and a **back chevron** in its header
  (120×120 hit area, same anatomy as profile sub-page headers), plus the generic left-edge
  `SwipeToBackPanel` strip.
- Back/swipe while the wizard coroutine is mid-flight routes through the existing
  `CancelBotCreation` semantics; back from the idle form just closes the panel.
  `CancelBotCreation` is currently `private` and wired only to the two auth back buttons —
  it gets exposed via a public `CloseAddBotForm()` on `Manager` that cancels the wizard when
  one is running, then closes the panel (it does not close any panel itself today).
- Closing returns to whatever screen opened it (Bots page in both entry paths).

### 2.3 Entry points

- `BotsPage.StartNewBot()` is rewritten to open `Screen_New` directly instead of invoking the
  old bottom-nav button (`BottomNavNewButton` field is removed; it is referenced nowhere
  else). Activation is **synchronous** — `SetActive(true)` immediately, then the slide
  animation — because the chats-screen `EmptyStateView.OpenCreateBotFlow` calls
  `Manager.SelectPlatform(1)` right after `StartNewBot()` returns and relies on the form
  being active. `StartNewBot()` is idempotent: a no-op when the form is already open.
  `StartNewBot()` first ensures the Bots tab is active (`SwitchTab(BotsTabIndex)`, a no-op
  when already there), *then* opens the form — so closing the form always lands on the Bots
  page, and the switch-closes-form rule (§2.2) can't fight the open because the switch
  precedes it. The two existing callers keep working unchanged through this one method:
  the BotsPage header plus button, and the chats-screen `EmptyStateView` CTA.
- **Zero bots**: when `Screen_Bots` activates and `BotsParent` has no bot children,
  `BotsPage` auto-opens the Add Bot form via `StartNewBot()` (deferred one frame so the tab
  switch settles; idempotent, so paths that already opened the form don't double-open).
- **Bots page empty state**: a proper empty state is added to the Bots page itself
  (bot hero image + «Создайте первого бота» + CTA → `StartNewBot`) so backing out of the
  auto-opened form does not leave a blank list.
- After successful creation, `ShowAuthSuccess` → `SwitchTab(BotsTabIndex)` — unchanged.

## 3. Outcome taxonomy

| enum value        | RU label (pill)   | Semantics                                                        | Pill colors (bg / fg) |
|-------------------|-------------------|------------------------------------------------------------------|-----------------------|
| `order_collected` | Заявка            | Bot captured an order/booking/contact per the «заявка» model      | #E8F8EE / #34C759     |
| `owner_needed`    | Нужен владелец    | Handoff, complaint, refund, or bot explicitly stuck               | #FFF3E0 / #F57C00     |
| `in_dialog`       | В диалоге         | Active conversation, bot handling                                 | #E3F2FF / #007AFF     |
| `client_silent`   | Клиент замолчал   | Was in dialog; customer stopped replying (rule-based, see §4.4)   | #F2F2F7 / #8E8E93     |
| `question_closed` | Вопрос закрыт     | Info question answered; no purchase intent                        | #E4E6EB / #65676B     |

`order_collected` and `owner_needed` are near-terminal: once assigned, only new customer
messages can change them (re-classification is triggered by the watermark, §4.3).

## 4. Server side (Supabase + n8n)

### 4.1 Migration: timestamps on `n8n_chat_histories`

`n8n_chat_histories` (LangChain Postgres memory: `id serial, session_id, message jsonb`) has no
timestamps. Migration `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql` adds:

```sql
ALTER TABLE public.n8n_chat_histories
  ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
```

n8n's memory node inserts without naming the column, so the default applies to new rows.
Pre-existing rows get the migration timestamp — acceptable: it only skews `last_message_at`
for old sessions and ages out naturally.

### 4.2 New table `conversation_outcomes`

```sql
CREATE TABLE public.conversation_outcomes (
  session_id      text PRIMARY KEY,          -- profile_id || ':' || chat_id (memory sessionKey)
  profile_id      text NOT NULL,
  chat_id         text NOT NULL,
  outcome         text NOT NULL CHECK (outcome IN
    ('order_collected','owner_needed','in_dialog','client_silent','question_closed')),
  summary         text NOT NULL DEFAULT '',  -- one short RU line for row subtitles (≤120 chars)
  last_history_id bigint NOT NULL,           -- watermark: max n8n_chat_histories.id classified
  last_message_at timestamptz NOT NULL,      -- max created_at of the session's rows
  outcome_at      timestamptz NOT NULL,      -- when outcome last CHANGED (drives period counts)
  updated_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ON public.conversation_outcomes (profile_id);
```

RLS: enable + service-role-only, same pattern as `2026-07-02-harden-rag-store.sql`.

### 4.3 New workflow `DashboardOutcomes` (sync webhook)

Single new n8n workflow, canonical JSON in `Tools/n8n/workflows/`, classifier prompt in
`Tools/n8n/prompts/dashboard-classifier.md`. Webhook + Respond-to-Webhook, like the existing
Upload/Delete File workflows. **No scheduled executions; no changes to the bot templates.**

Request: `POST /webhook/DashboardOutcomes` body `{ "profileIds": ["...", "..."] }`
(v1: the app sends the authed `whatsappProfileId` of every bot; `"-1"` sentinels are never
sent, Telegram profile ids are deliberately not sent — see §1. The workflow itself is
channel-agnostic).

Steps:

1. **Find changed sessions**: for the given profiles, `GROUP BY session_id` over
   `n8n_chat_histories` where `max(id) > coalesce(outcomes.last_history_id, 0)`.
   Skip group chats (session chat_id part ending `@g.us`) — the bot templates already drop
   group traffic; this guards legacy WhatsApp rows. (Legacy pre-guard Telegram clones can
   hold numeric group-id sessions that this check won't catch; irrelevant while v1 only
   queries WhatsApp profiles, noted for the Telegram follow-up.)
2. **Classify** (cap: 20 sessions per call, oldest-watermark first; remainder picked up on the
   next open): per changed session, one gpt-4o-mini structured-output call over the last N
   (≈30) messages → `{ outcome, summary }`, schema-validated, enum-constrained; on validation
   failure retry once, then keep the previous outcome (or `in_dialog` for brand-new sessions)
   — never write an invalid label.
3. **Silence rule pass** (code node, not LLM): any stored `in_dialog` session for these
   profiles with last message role `ai` and `now() - last_message_at > 12h` →
   `client_silent`. Re-evaluated on every call; a later customer message re-enters
   classification via the watermark and can move it back.
4. **Upsert** `conversation_outcomes` (set `outcome_at` only when the outcome value changed).
5. **Respond** with all rows for the requested profiles:
   `{ "success": true, "classified": N, "truncated": bool, "outcomes": [ { "profileId", "chatId",
   "outcome", "summary", "outcomeAt", "lastMessageAt" } ] }` (timestamps unix ms).

Failure response follows the Upload File convention: `{ "success": false, "error": "..." }`.

Dev-first on local n8n (tunnel), e2e test script alongside the existing Tools test scripts;
prod bagkz replication happens later in the one dormant-prod bulk copy, per the standing
process. Retroactive classification of existing history works immediately (transcripts are
already stored).

### 4.4 Session → chat mapping

Memory `sessionKey = profile_id + ':' + from`; for direct chats `from == chatId` (the group
guard drops the rest), so `chat_id` = the suffix after the first `:`. This holds for both the
WhatsApp and Telegram templates; the workflow splits on the *first* colon only.

## 5. App side

### 5.1 New screen `Screen_Dashboard` (Variant B)

Built by an idempotent `Assets/Editor/DashboardPageBuilder.cs` (delete-and-rebuild, scene
save, headless entry — per `.claude/rules/editor-scripts.md`), reusing ProfileSubPagesBuilder
tokens (gutter 44, card radius 40, hairline #E4E6EB, fonts by GUID, type scale). Top→bottom:

1. **Header** — «Сводка», standard 300-height top bar (safe zone baked in).
2. **Period segmented control** — Сегодня / 7 дней / 30 дней. Client-side only; switching
   periods never refetches.
3. **Bot filter chips** — «Все боты» + one chip per bot; hidden entirely with ≤1 bot.
   Client-side filter.
4. **Hero card** — «Заявки собраны»: big count for the selected period, delta pill vs the
   previous equal-length period, subtitle («N диалогов · M бота»), stacked funnel bar of the
   5 statuses with a legend + counts.
5. **Status rows card** — 5 rows (dot, label, count, chevron), each opens the drill-down list.
6. **«Последние заявки»** — up to 5 most recent `order_collected` rows (respecting the
   period + bot filter), each row: avatar, name, bot tag (only in «Все» mode), summary line,
   time, pill. Tap → deep-link (§5.5).

### 5.2 Metric definitions (client-side, from returned rows)

- **Periods**: Сегодня = local midnight→now; 7/30 дней = last 7/30 × 24h. **Delta** compares
  an equal-length window shifted back by one period unit: for «Сегодня», yesterday
  midnight→same time of day (not full yesterday — a partial day must compare against a
  partial day); for 7/30 дней, the previous full 7/30-day window.
- **Заявки count** = rows with `outcome == order_collected` and `outcomeAt` in period.
- **Funnel bar / legend / status-row counts** = current outcome of rows with `lastMessageAt`
  in period (i.e. "conversations active in the period").
- The webhook returns *all* rows per profile (small-business volume; hundreds at most), so
  30-day deltas need no extra call.

### 5.3 Data flow & models

- New folder `Assets/Scripts/Main/Dashboard/`:
  - `DashboardOutcome` (+ response wrapper) — serializable models, parsed with `JsonConvert`.
  - `DashboardStore` — disk cache `persistentDataPath/dashboard_cache.json` (all bots, one
    file), written after each successful fetch, loaded for instant paint.
  - `DashboardPage` — MonoBehaviour controller on `Screen_Dashboard`: fetch, filter state
    (period/bot), rendering into the built hierarchy, row spawning.
- Fetch = `UnityWebRequest` POST coroutine to `/webhook/DashboardOutcomes`, following the
  `Manager.DeleteBotFilesRoutine` convention exactly: no auth header, raw JSON upload body,
  explicit `Content-Type: application/json`, result check, `JsonConvert` parse.
- Names/avatars resolve from the local per-bot chat caches (`BotCache/{botId}/chats.json` /
  live `ChatManager.Chats`) by `chatId`; fallback = phone number derived from chatId. Row
  times display `lastMessageAt`, but if the local chat list has a *newer* `last_timestamp`
  (owner replied manually — bot store is stale), the local time wins (see §6).

### 5.4 Refresh policy

- On `Screen_Dashboard` activation (`OnEnable`): paint from `DashboardStore` immediately,
  then fetch if the last successful fetch is older than 60s. The throttle is a new
  pure-static, unit-testable gate helper *in the mold of* `TabRefreshGate` — not
  `TabRefreshGate` itself, which is a tab-index gate with no time component.
- **`truncated: true`** in a response bypasses the throttle: the client refetches
  immediately and repeatedly until `truncated` is false (hard cap of 5 consecutive calls),
  so a large backlog of unclassified sessions drains in one tab visit instead of one batch
  per visit.
- Quiet failure: keep cached data, show nothing intrusive (matches the chats-screen
  cache-then-diff pattern). First-ever open with no cache shows a lightweight loading state.
- Empty states: zero bots → hero + «Создать бота» CTA (→ `BotsPage.StartNewBot`);
  bots exist but no outcomes → «Бот пока не вёл диалогов».

### 5.5 Drill-down + deep-link

- Tapping a status row (or a legend row) opens a slide-in list panel *inside*
  `Screen_Dashboard` (profile sub-page shell: header + back + swipe strip), titled with the
  status label, listing that status's conversations sorted by `lastMessageAt` desc.
- Tapping any conversation row (drill-down or «Последние заявки»): resolve the owning bot by
  `profileId` (matches `Bot.whatsappProfileId`; v1 is WhatsApp-only, §1) → `SetActiveBot` if
  needed → `SwitchTab(WhatsAppTabIndex)` → open the chat via the existing public
  `ChatManager.SelectChat(chatId)`. If the chat is absent from the local list after the sync
  settles, fall back to just landing on that bot's chat list (no error popup).

## 6. Honest limitations (accepted)

- **WhatsApp only in v1**: Telegram bot conversations produce transcripts server-side, but
  the app cannot render or open them (no Telegram chat pipeline exists), so they are
  excluded until that pipeline is built (§8).
- **Bot-handled traffic only**: `n8n_chat_histories` contains only exchanges that flowed
  through the bot workflow. Chats where the owner replied manually look stale server-side;
  mitigated by preferring local last-activity time in rows (§5.3). Full-transcript
  classification via Wappi is a possible later upgrade.
- **Вместе mode has no server enforcement** (bot may auto-reply while the owner believes it's
  paused), so outcomes can be produced by a "paused" bot. Known pre-existing gap, explicitly
  out of scope; follow-up = per-chat mute flag checked by the bot workflow.
- Pre-migration history rows share one `created_at`, so old sessions' `last_message_at` is
  approximate until they see new activity.
- «Сегодня» counts include outcome transitions, not confirmed деньги — the bot never confirms
  deals; copy says «Заявки», never «Продажи».

## 7. Testing

- **EditMode** (`Assets/Tests/Editor/Chat/` conventions): period bucketing + delta math,
  funnel/status count aggregation, response JSON parsing (incl. malformed), session→chat/bot
  resolution and deep-link routing decisions, bot-chip filtering. Pure C# — no scene deps.
- **n8n e2e script** in `Tools/`: seed fake `n8n_chat_histories` rows → call webhook → assert
  classifications, watermark advance, silence rule, group-session skip, cap/truncated flag.
- **Device pass** per the standard loop (Editor GREEN via test bridge first).

## 8. Out of scope / follow-ups

- **Telegram chat pipeline** (tapi chat/message endpoints, Telegram chat list UI) and, once
  it exists, Telegram coverage in the dashboard (send `telegramProfileId`s too; add a
  numeric-group-id guard for legacy Telegram sessions).
- Вместе-mode server enforcement (per-chat mute flag in bot templates).
- Prod bagkz replication (rides the existing dormant-prod bulk copy).
- Trend charts over time, CSV export, push notifications for `owner_needed`.
- Wappi-transcript-based classification of owner-handled chats.
- Making «Сводка» the default launch tab (revisit after the feature proves itself).
