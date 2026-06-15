# WhatsApp tab: empty-state redesign + post-creation sync screen

- **Date:** 2026-06-14
- **Status:** Approved (pending spec review)
- **Area:** WhatsApp tab (`Screen_Whatsapp` / `ChatsPanel`), bot creation flow, chat loading

## Problem

When the app launches with no bots, the WhatsApp tab shows a plain, centered "create your first bot" empty state that looks unpolished. Three issues:

1. **Empty state looks weak.** Centered icon + "No bots yet" + a button. No visual hierarchy, no warmth, no thumb-zone CTA.
2. **Stale empty state after creation.** After a bot is created, switching to the WhatsApp tab can still show the "create your first bot" screen instead of the bot's content.
3. **Chats appear empty right after creation.** Wappi needs several minutes to import a freshly connected number's chats and messages. Today the user lands on an empty/again-looking chat list with no explanation.

## Goals

- Redesign the "no bots" empty state (visual only; same action).
- Fix the WhatsApp tab so that once a bot with a WhatsApp profile exists, it never falls back to the "no bots" screen.
- Add a **Syncing** screen shown for a fixed window after a bot connects, then automatically reveal the chat list.

## Non-goals

- No Telegram syncing screen (WhatsApp only for now).
- No "no chats yet" screen for a connected number that genuinely has zero chats — after the sync window it falls through to today's normal (empty) chat list.
- No real sync-progress data from Wappi (it exposes none); the progress shown is time-based against the fixed window.

## Approved decisions

| Decision | Choice |
|---|---|
| Empty-state direction | **A · Welcoming hero** — soft green circle + bot icon, headline "Create your first bot", supportive line, green pill CTA in the thumb zone. |
| Syncing-screen direction | **A · Progress + reassurance** — spinner, status line, time-based progress bar, live countdown, "you can keep using the app" footnote. |
| Sync gate | **Fixed timer**, window = **5 minutes**. |
| Timer anchor | Window starts when the bot finishes being created/connected (after persistence in `CreateBotFromForm()`), i.e. when Wappi's sync has actually begun — not at form submit. |
| Empty connected number | Falls through to the normal empty chat list after the window (no new screen). |
| Scope of syncing screen | Covers the chat-list content area of the WhatsApp tab only. The top bar stays; other bottom tabs remain fully usable. |

## State model

The WhatsApp tab is one content area resolved (for the **active bot**) in this precedence order, evaluated whenever the tab activates, the active bot changes, or the sync window elapses:

1. **No bots at all** (`BotsRoot.childCount == 0`) → redesigned "create your first bot" empty state.
2. **Active bot has no valid WhatsApp profile** → existing "WhatsApp not connected" card (`EmptyStateReason.BotHasNoWhatsApp`, unchanged).
3. **Active bot is within its sync window** (`now < SyncUntil`) → **Syncing** screen.
4. **Otherwise** → chat list (today's behavior).

## Component design

### 4.1 Empty state (Direction A)

Reuse the existing `EmptyState` GameObject and `EmptyStateView` (`Assets/Scripts/UI/EmptyStateView.cs`). Visual rebuild in `Main.unity` only:

- Soft green circle (e.g. tinted fill ~`#DFF3EA`) containing a bot/chat icon.
- Headline "Create your first bot".
- Body "An AI assistant that answers your customers on WhatsApp, day and night."
- Green pill CTA "Create a bot" pinned in the thumb zone, wired to the existing `OpenCreateBotFlow()` (unchanged behavior).

No change to the `BotHasNoWhatsApp` variant's wiring.

### 4.2 Auto-switch fix

In `ChatManager.BotState.cs`, the resolver currently returns `EmptyStateReason.NoBotsExist` based on `BotsRoot.childCount == 0`, which is correct — the bug is that the syncing case isn't modeled, so a just-created bot with an empty chat list reads as "nothing here." Changes:

- Keep `NoBotsExist` returned **only** when `BotsRoot.childCount == 0`.
- Add the syncing branch (4.4) so a connected-but-syncing bot routes to the Syncing screen instead of looking empty.
- `EmptyStateView.OnEnable()` already re-runs `ComputeCurrentEmptyState()` as a catch-up on tab activation; it continues to drive the two empty-state cards and to `Hide()` when chats exist or syncing owns the screen.

### 4.3 Syncing screen (Direction A)

New GameObject `SyncingState` under `ChatsPanel`, sibling of `EmptyState`, same rect footprint. New controller `Assets/Scripts/UI/SyncingView.cs`:

- **Serialized refs:** root `CanvasGroup`, spinner `RectTransform` (or `Image`), title `TextMeshProUGUI`, body `TextMeshProUGUI`, progress fill `Image` (`Image.type = Filled`, horizontal), countdown `TextMeshProUGUI`, footnote `TextMeshProUGUI`.
- **Lifecycle:** `OnEnable` subscribes to `ChatManager.OnWhatsAppSyncing` / `OnWhatsAppSyncReady`, then catches up by querying `ChatManager.Instance.IsWhatsAppSyncing(activeBotId, out long untilMs)` (so it resumes after a tab switch or app restart). `OnDisable` unsubscribes and stops its coroutine.
- **Show:** start a 1s-tick coroutine (mirrors `WhatsappCodeTimer` using `WaitForSecondsRealtime`). Each tick:
  - `remaining = untilMs - nowMs`; `elapsed = window - remaining`.
  - progress fill `fillAmount = clamp01(elapsed / window)`.
  - countdown label: `> 60s` → "about N min left"; `<= 60s` → "less than a minute left".
- **Spinner:** continuous rotation via DOTween (`.DORotate(... , RotateMode.FastBeyond360).SetLoops(-1)`).
- **Hide:** on `OnWhatsAppSyncReady` (or when the tick reaches zero).

Copy: title "Setting things up", body "We're importing your chats and messages from WhatsApp.", footnote "You can keep using the app. Chats appear here when ready."

### 4.4 Sync gate (fixed 5-minute window)

- **Single source of truth:** `public const int WhatsAppSyncWindowSeconds = 300;` on `ChatManager`.
- **Persistence key (per bot):** `Bot{N}WhatsappSyncUntil` — a Unix-ms epoch string, written via bot-persistence conventions. Missing/`"0"` ⇒ not syncing (existing bots default to synced; no migration).
- **Write (anchor):** in `Manager.CreateBotFromForm()`, immediately after the bot's PlayerPrefs are persisted (~line 1218), gated on `useWhatsapp`:
  `PlayerPrefs.SetString(newBot.name + "WhatsappSyncUntil", DateTimeOffset.UtcNow.AddSeconds(ChatManager.WhatsAppSyncWindowSeconds).ToUnixTimeMilliseconds().ToString());`
  At this point WhatsApp auth has already completed in the wizard, so the window aligns with Wappi's actual sync start.
- **Read:** new `ChatManager.BotState.cs` helper `bool IsWhatsAppSyncing(string botId, out long untilUnixMs)` reading the key and comparing to now.
- **Gate in load path:** in `BeginLoadForActiveBot()`, after the active bot + profile resolve and before rendering chats, if `IsWhatsAppSyncing(activeBot)` → invoke `OnWhatsAppSyncing(untilMs)` and start `WaitForWhatsAppSyncRoutine(untilMs)`; return without loading chats. Otherwise load as today.
- **Wait routine:** `WaitForWhatsAppSyncRoutine(long untilMs)` polls each second until `now >= untilMs`, then invokes `OnWhatsAppSyncReady` and proceeds with the normal cache-load + `SyncAllChats()`. The coroutine handle is stored and stopped on active-bot switch / teardown so a stale window can't reveal the wrong bot's chats.
- **New events on `ChatManager`:** `event Action<long> OnWhatsAppSyncing;` (epoch ms until) and `event Action OnWhatsAppSyncReady;`.
- **Cleanup:** `Bot.DeleteBot()` adds `PlayerPrefs.DeleteKey(transform.name + "WhatsappSyncUntil")`.

## Edge cases

- **Existing bots** (no key) → not syncing → chats load normally.
- **App restart mid-window** → absolute epoch is persisted; `SyncingView` catch-up + `WaitForWhatsAppSyncRoutine` resume the correct remaining time.
- **Switch active bot mid-window** → resolver recomputes for the newly active bot; the prior wait coroutine is stopped.
- **Window elapses while off the WhatsApp tab** → no active work needed; next tab open resolves to chats.
- **Telegram-only bot** → no WhatsApp profile → "WhatsApp not connected" card; no syncing screen.
- **Connected number with zero chats** → after the window, normal empty chat list (no new screen).

## Files touched

- `Assets/Scripts/Main/Manager.cs` — write `Bot{N}WhatsappSyncUntil` after persistence in `CreateBotFromForm()`.
- `Assets/Scripts/Main/ChatManager.cs` — `WhatsAppSyncWindowSeconds` constant; `OnWhatsAppSyncing` / `OnWhatsAppSyncReady` events.
- `Assets/Scripts/Main/ChatManager.BotState.cs` — `IsWhatsAppSyncing()`; syncing branch + `WaitForWhatsAppSyncRoutine()` in `BeginLoadForActiveBot()`; resolver precedence.
- `Assets/Scripts/UI/EmptyStateView.cs` — confirm `NoBotsExist` only on `childCount == 0` (likely no functional change).
- `Assets/Scripts/UI/SyncingView.cs` — **new** syncing-screen controller + countdown coroutine.
- `Assets/Scripts/Main/Bot.cs` — clear `WhatsappSyncUntil` in `DeleteBot()`.
- `Assets/Scenes/Main.unity` — restyle `EmptyState` (Direction A); add `SyncingState` GameObject + wire `SyncingView` references.

## Visual reference

Mockups for both screens (Direction A in each) were reviewed and approved during brainstorming. The countdown/progress in the syncing mockup is accurate because the window is a fixed 5 minutes anchored to a known timestamp.
