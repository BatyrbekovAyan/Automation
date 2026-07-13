# Semi-Auto Suppression Flag — Design

**Date:** 2026-07-13
**Status:** Approved design
**Depends on:** v1.0 Reply Suggestions (the «Авто/Вместе» toggle + `SemiAutoStore` + Suggest Replies workflow exist and are live)

## Problem

The «Авто/Вместе» reply-mode toggle is **client-side only** today. `ReplyModeToggleBinder` (per-bot default, PlayerPrefs `<botName>ReplyMode`) and `SemiAutoStore` (per-chat tri-state override, `{botId}_semiAuto_{chatId}`) only change what the Unity app *shows* — they raise a local C# event (`OnReplyModeChanged`) that nothing consumes and touch no server state. So a bot whose activation switch is «Бот работает» keeps auto-replying via its n8n workflow even in chats the owner flipped to «Вместе» to review. The bot and the owner can answer the same customer at once.

`ReplyModeToggleBinder`'s own doc comment already names this as the intended wiring point ("raises `OnReplyModeChanged` so the autonomous-vs-suggestions backend … can react when it lands") — the hook was built, the server end was never connected. This spec connects it.

## Goal

When a chat is in «Вместе» (semi-auto), the bot's autonomous n8n reply workflow **stands down for that chat** — it does not auto-reply, leaving the owner to answer via the suggestions panel. «Авто» chats are unaffected. Works identically on WhatsApp and Telegram. The «Бот работает/пауза» activation switch is untouched (it stays the real n8n activate/deactivate).

## Decisions (locked)

- **Fail-closed** on a flag-read error: a genuine Postgres error while reading the flag halts the execution → no reply. This is n8n's *natural* behavior when a node errors (no extra wiring), and it's safe here because the bot workflow already depends on this same Supabase Postgres for Chat Memory — gating on it adds no new point of failure. **Absence of a flag row is NOT an error** → it resolves to "not suppressed" → the bot replies (the never-toggled common case is never silenced).
- **Both channels** — the gate goes in the WhatsApp and Telegram bot templates. The flag table is keyed by `profile_id`, so it is channel-agnostic; each channel's workflow reads by its own profile id.
- Faithful mirror of the client tri-state: a **bot-wide default** plus **per-chat overrides**, resolved server-side exactly as `SemiAutoStore.IsOn` resolves client-side.

## Architecture

### 1 · Flag table (new, in the existing Supabase Postgres)

Same database the bot's Chat Memory and Vector Store already use — **no new credential**.

```sql
create table reply_mode_flags (
  profile_id  text        not null,
  chat_id     text        not null default '*',   -- '*' = bot-wide default row
  suppressed  boolean     not null,               -- true = «Вместе» (suppress auto-reply)
  updated_at  timestamptz not null default now(),
  primary key (profile_id, chat_id)
);
```

- `chat_id = '*'` → the bot-wide default row (written on a per-bot mode flip).
- specific `chat_id` → a per-chat override (written on a per-chat toggle).
- No row for a chat → falls back to the `'*'` default; no `'*'` row either → not suppressed (Авто).

### 2 · Server read — the gate (in each bot template)

Inserted right after the existing group-chat `If` (`from == chatId`), before any reply work:

```
group-chat If (true) → Read Reply Mode (Postgres) → Suppressed? (If)
     ├─ [false / not suppressed] → Input type … (existing reply path, unchanged)
     └─ [true / suppressed]      → dead-end: NO reply, NOT marked read
```

- **Read Reply Mode** — a Postgres `executeQuery` node on the existing `Postgres` credential. The query **always returns exactly one row** so a missing flag never starves the downstream `If`:
  ```sql
  select coalesce(
    (select suppressed from reply_mode_flags
     where profile_id = $1 and chat_id in ($2, '*')
     order by (chat_id = '*')   -- specific chat_id sorts before the '*' default
     limit 1),
    false
  ) as suppressed;
  ```
  `$1 = messages[0].profile_id`, `$2 = messages[0].from`. Precedence: a per-chat override wins over the `'*'` default; no match → `false`.
  > Gotcha (see project memory *n8n Postgres node gotchas*): pass `$1/$2` as real query parameters; the Postgres node's `queryReplacement` comma-splits list values, so keep params scalar. `largeNumbersOutput` is irrelevant here (boolean).
- **Suppressed?** — an `If` on `{{ $json.suppressed === true }}`. False branch → the existing `Input type` reply path (untouched). True branch → dead-ends (no `Mark Read`, no typing, no send — the message stays **unread** so the owner sees the unread badge and handles it in-app).
- **Fail-closed** falls out for free: if **Read Reply Mode** errors (Postgres unreachable), the node throws and the execution stops → no reply. No `continueOnFail`, no default-on-error wiring.

### 3 · App → server sync

The app never touches Supabase directly (matches UploadFile / DeleteFile / DashboardOutcomes). A new shared always-active workflow **`Set Reply Mode`** (`/webhook/SetReplyMode`, canonical export in `Tools/n8n/workflows/`) upserts the flag:

- Request: `{ profileIds: ["…"], chatId: "*" | "<chatId>", suppressed: bool }`
- Node graph: Webhook → Code (validate) → Postgres (upsert one row per profileId, `on conflict (profile_id, chat_id) do update set suppressed = excluded.suppressed, updated_at = now()`) → Respond `{ success, written }`.

Two client hooks — both already single choke points, both fire-and-forget coroutines on `Manager` (mirroring `DeleteBotFilesOnServer`):

- **Bot-default flip** — subscribe to `ReplyModeToggleBinder.OnReplyModeChanged(botId, mode)`. POST `{ profileIds: <all authed profiles of the bot>, chatId: "*", suppressed: (mode == Semi) }`. A bot may have both WhatsApp and Telegram profiles authed; write the `'*'` row for each. Skip sentinel profile ids (`""` / `"-1"`).
- **Per-chat toggle** — at the `SemiAutoStore.Set(botId, chatId, on)` call site (in `SuggestionsController.HandleToggle`). POST `{ profileIds: [<active channel's profile id>], chatId: <chatId>, suppressed: on }`. Both an explicit ON and an explicit OFF write a row (both are explicit overrides).

**Identifier normalization (must-verify):** the server keys on `profile_id + from`. `from` for WhatsApp is `…@c.us`. The app must send the **same `chatId` string the bot workflow sees in `messages[0].from`** — verify `ChatManager.CurrentChatId`'s format matches (normalize if it differs) or the override silently never matches. This is the single highest-risk integration detail.

### 4 · Fail-closed robustness — re-assert on chat open

A *lost* write is the one real risk of fail-closed: if a «switch back to Авто» POST is dropped, the server keeps `suppressed=true` and that chat stays silent. Cheap heal: when a chat is opened, the app **re-asserts** the effective flag for that chat (it is already issuing a suggestions request there, so this is one more fire-and-forget POST). Reopening any chat self-corrects drift. Writes are idempotent upserts, so re-assertion is always safe.

### 5 · Interplay with Suggest Replies (unchanged, stated for clarity)

Suppression only affects the **autonomous** per-bot reply workflow. The shared **Suggest Replies** workflow is separate and the app calls it directly — so a suppressed («Вместе») chat gets *no auto-reply* while the panel still shows suggestions. The two behaviors compose correctly precisely because they are separate workflows; nothing here changes Suggest Replies.

## Propagation (the real scope cost)

The gate is per-bot-template code, so it must land in:
- `Tools/n8n/workflows/…-WhatsApp_Bot.json` and `…-Telegram_Bot.json` (the templates)
- both **Create orchestrators** (`CreateWhatsappWorkflow`, `CreateTelegramWorkflow`) so new bots get the gate
- existing **dev clones** — recreated (standard template-change cost; matches the vertical-prompts and Telegram-parity rollouts)

Prod bagkz stays dormant; this folds into the existing one-shot bulk replication. (See project memory *n8n dev setup* / *Vertical prompts rollout* for the propagation playbook.)

## Testing

- **Unity EditMode** (headless bridge): a pure payload-builder for `Set Reply Mode` (profileIds resolution incl. both-channels and sentinel skip; `chatId="*"` for default vs the real chatId; tri-state → `suppressed`), plus verification that the two hooks fire the correct POST on a mode flip and a per-chat toggle. Mirrors the Suggest Replies `BuildPayloadJson` pure-parts pattern.
- **n8n curl (dev):** upsert a `'*'` default; upsert a per-chat override; resolve precedence (override beats default); absence → `suppressed=false` (replies); malformed body → clean error, no partial write.
- **Human dev e2e** (real profile, like the other bot-workflow gates): flip a chat to «Вместе» → send a message → **no reply, stays unread**; flip back to «Авто» → send → replies. Confirm on both WhatsApp and Telegram. Confirm the `'*'` default suppresses a never-opened chat.

## Out of scope

- **Message batching / debounce** (combine a customer's multi-fragment message into one reply, on both the auto-reply and suggestions sides) — a real, related gap in the same trigger region, but a distinct and larger feature. Its own design pass, sequenced right after this. Pipeline order when both exist: group-chat `If` → **suppression gate** → **debounce + combine** → agent.
- Clearing a per-chat override back to "inherit the default" (the client `SemiAutoStore` never does this today — it only sets explicit ON/OFF).
- Any change to the «Бот работает/пауза» activation switch (stays the real n8n activate/deactivate).
- Server-*generated* suggestions (the app already makes those on-demand via Suggest Replies).
- Authenticating `/webhook/SetReplyMode` — left unauthenticated, consistent with every other app `/webhook/*` (accepted-risk posture R-02-01); to be recorded in the phase threat model when this becomes a GSD phase.
