# Phase 9: Semi-Auto Suppression Flag - Context

**Gathered:** 2026-07-13
**Status:** Ready for planning (v1.2 milestone — starts after v1.1 closes)
**Source:** PRD Express Path (docs/superpowers/specs/2026-07-13-semi-auto-suppression-flag-design.md — approved design; near-executable task breakdown in docs/superpowers/plans/2026-07-13-semi-auto-suppression-flag.md)

<domain>
## Phase Boundary

Wire the client-side «Авто/Вместе» toggle through to the server so a bot's autonomous n8n reply workflow stands down for semi-auto chats. Three pieces: a `reply_mode_flags` Postgres table (existing Supabase DB), a shared `/webhook/SetReplyMode` sync workflow the app calls, and a fail-closed gate node in BOTH bot templates (WhatsApp + Telegram). The «Бот работает/пауза» activation switch and the Suggest Replies workflow are untouched.

</domain>

<decisions>
## Implementation Decisions

### Requirement definitions (SUP ids referenced by ROADMAP Phase 9)
- **SUP-01**: `reply_mode_flags(profile_id, chat_id default '*', suppressed, updated_at, pk(profile_id, chat_id))` in the existing Supabase Postgres (same DB/credential as Chat Memory — no new credential); RLS enabled (service-role only)
- **SUP-02**: Shared always-active `/webhook/SetReplyMode` workflow upserts flags (`{ profileIds:[], chatId:"*"|"<id>", suppressed:bool }` → one row per profileId); the app writes on (a) bot-default flip via `ReplyModeToggleBinder.OnReplyModeChanged` (all authed profiles, chatId `"*"`), (b) per-chat toggle at the `SemiAutoStore.Set` call site in `SuggestionsController.HandleToggle` (active channel's profile), (c) re-assert-on-chat-open heal (ON state only, in `RestoreForActiveChat`)
- **SUP-03**: Gate in BOTH bot templates after the group-chat `If`: Read Reply Mode (Postgres) → Suppressed? (If); suppressed → dead-end (NO reply, NOT marked read — stays unread for the owner); not suppressed → existing reply path unchanged
- **SUP-04**: Resolve precedence: per-chat override beats the `'*'` default; the query ALWAYS returns one row (`coalesce(…, false)`) so absence → `suppressed=false` → bot replies. FAIL-CLOSED: a genuine Postgres error halts the execution (n8n default — do NOT set continueOnFail/onError on the read node)
- **SUP-05**: Propagation: new bots inherit the gate via template cloning (Create orchestrators clone the template verbatim — verify on a freshly created bot, no orchestrator surgery expected); existing dev clones recreated; prod bagkz stays dormant (folds into the bulk copy)

### Locked technical decisions (from the approved spec)
- Resolve query (verbatim — the always-one-row shape is load-bearing for fail-closed):
  `select coalesce((select suppressed from reply_mode_flags where profile_id = $1 and chat_id in ($2,'*') order by (chat_id='*') limit 1), false) as suppressed;`
  with `$1 = messages[0].profile_id`, `$2 = messages[0].from` — scalar params only (Postgres node comma-splits lists)
- App → server only via the webhook (never direct Supabase; matches UploadFile/DeleteFile/DashboardOutcomes)
- Sync calls are fire-and-forget coroutines on `Manager` (mirrors `DeleteBotFilesOnServer`); pure static `Manager.BuildReplyModePayload(profileIds, chatId, suppressed)` + `Manager.AuthedProfileIds(bot)` (skips `""`/`"-1"` sentinels) for EditMode testability
- Both an explicit per-chat ON and OFF write a row (both are explicit overrides; clearing back to "inherit" is out of scope — client never does it)
- **Identifier normalization (highest-risk integration detail, must-verify during execution)**: the app must send the exact `chatId` string the bot workflow reads at `body.messages[0].from` (`…@c.us` on WhatsApp; tapi shape per Phase 3 SHAPES.md) — verify `ChatManager.CurrentChatId` format equality per channel or the override silently never matches
- Unauthenticated webhook: accepted, consistent with every other app `/webhook/*` (v1.0 accepted-risk posture R-02-01) — record in this phase's threat model

### Claude's Discretion
- n8n node naming/layout inside the new workflow; deployer script structure (mirror `Tools/n8n/build-suggest-replies.py`, resolve credentials by NAME)
- Exact test file naming under `Assets/Tests/Editor/Chat/`
- How the Manager partial hooks lifecycle (own OnEnable/OnDisable vs calls added to the primary file's existing methods)
- Whether Task-level structural verification uses execution runData introspection (02-03 pattern) or workflow-JSON grep — prefer runData for the live gates

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design + task breakdown (source of truth)
- `docs/superpowers/specs/2026-07-13-semi-auto-suppression-flag-design.md` — approved design: problem, decisions, table, gate, sync, heal, testing, out-of-scope
- `docs/superpowers/plans/2026-07-13-semi-auto-suppression-flag.md` — 7-task near-executable breakdown with code/SQL/curl (GSD plans should lift from it, not re-derive)

### Client integration points
- `Assets/Scripts/UI/ReplyModeToggleBinder.cs` — `OnReplyModeChanged` static event (the built-but-unconsumed hook), `GetMode`, `ReplyMode` enum
- `Assets/Scripts/Chat/SemiAutoStore.cs` — tri-state resolve the server must mirror
- `Assets/Scripts/Chat/SuggestionsController.cs` — `HandleToggle` (~L105, per-chat write site) + `RestoreForActiveChat` (~L90, re-assert site)
- `Assets/Scripts/Main/ChatManager.BotState.cs` — `ActiveChannel`, `ProfileIdForChannel(bot, channel)` (the landed channel seam)
- `Assets/Scripts/Main/Bot.cs` — `whatsappProfileId`/`telegramProfileId` + sentinel semantics (L64-70)
- `Assets/Scripts/Main/Manager.cs` — `n8nBaseUrl` resolution, `FindBotByName`, fire-and-forget coroutine precedent (`DeleteBotFilesOnServer`)

### n8n patterns to mirror
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` — group-chat `If` (gate insertion point), `Postgres` credential (id 1H5xlpFSESU4w6JH), `messages[0].profile_id`/`from` expressions
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` — tapi'd template (Phase 4); confirm its reply entry node name
- `Tools/n8n/build-suggest-replies.py` — REST deployer/exporter pattern (creds by NAME)
- `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json` — "Get Sample Workflow" → "Create Workflow" cloning (why new bots inherit the gate)

</canonical_refs>

<specifics>
## Specific Ideas

- Gate pipeline position (locked, composes with the future batching design): group-chat `If` → suppression gate → (future: debounce+combine) → agent
- Owner e2e checklist (5 scenarios) is already drafted in the implementation plan Task 7 — reuse it as this phase's HUMAN-UAT content
- n8n curl matrix: upsert default; upsert override; precedence (override beats `'*'`); absence → `suppressed=false`; malformed body → clean error, no partial write

</specifics>

<deferred>
## Deferred Ideas

- **Message batching/debounce** (combine a customer's multi-fragment message into one reply — auto-reply side needs wait+latest-check+combine in the workflow; suggestions side needs a ~2-3s debounce in `SuggestionsController.HandleLive`) — its own design pass, sequenced after this phase
- Clearing a per-chat override back to "inherit the bot default" (client `SemiAutoStore` never writes state 0 today)
- Authenticating `/webhook/SetReplyMode` (accepted-risk posture R-02-01)
- Any change to the «Бот работает/пауза» activation switch (stays the real n8n activate/deactivate)

</deferred>

---

*Phase: 09-semi-auto-suppression*
*Context gathered: 2026-07-13 via PRD Express Path*
