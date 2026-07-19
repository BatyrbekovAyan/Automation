# Phase 9: Semi-Auto Suppression Flag - Research

**Researched:** 2026-07-19
**Domain:** n8n workflow gating + Supabase Postgres flag table + Unity client sync (cross-channel WhatsApp/Telegram)
**Confidence:** HIGH (the approved design + 7-task breakdown exist; this research re-validated every integration point against the live tree — corrections below)

## Summary

This phase wires the already-shipped client-side «Авто/Вместе» toggle through to the server so a bot's autonomous n8n reply workflow stands down for chats the owner flipped to «Вместе». Three moving parts: (1) a new `reply_mode_flags` table in the existing Supabase Postgres, (2) a shared always-active `/webhook/SetReplyMode` workflow the app POSTs to, and (3) a fail-closed gate node inserted after the group-chat `If` in BOTH bot templates (WhatsApp + Telegram). The «Бот работает/пауза» activation switch and the separate Suggest Replies workflow are untouched.

The design and a near-executable 7-task plan already exist (both dated 2026-07-13) and are the source of truth. The value this research adds is **re-validation against ~40 commits of drift** (Phase 8 rounds 2-3 + all of Phase 11). The good news: the architecture is sound and every landed seam the plan depends on still exists. The bad news: the plan carries **five concrete code-level errors** that will fail compilation or silently misbehave if lifted verbatim — a wrong sentinel constant name, an assumption that `Manager` is a partial class (it is not), a `private` accessor the client hook needs to call, an ambiguous credential-by-name lookup, and an untyped boolean SQL parameter. All five are cheap to fix and documented below.

**Primary recommendation:** Lift the 2026-07-13 plan's task structure verbatim (it maps cleanly to 3 waves + a human gate), but apply the seven corrections in the "Staleness Re-Validation" table before writing task actions. The single highest-risk item — identifier equality between what the app sends and what the gate reads — was verified from captured tapi shapes and independently confirmed by the shipped group-chat `If`: it holds for the only case that reaches the gate (1:1 chats).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Requirement definitions (SUP ids referenced by ROADMAP Phase 9):**
- **SUP-01**: `reply_mode_flags(profile_id, chat_id default '*', suppressed, updated_at, pk(profile_id, chat_id))` in the existing Supabase Postgres (same DB/credential as Chat Memory — no new credential); RLS enabled (service-role only)
- **SUP-02**: Shared always-active `/webhook/SetReplyMode` workflow upserts flags (`{ profileIds:[], chatId:"*"|"<id>", suppressed:bool }` → one row per profileId); the app writes on (a) bot-default flip via `ReplyModeToggleBinder.OnReplyModeChanged` (all authed profiles, chatId `"*"`), (b) per-chat toggle at the `SemiAutoStore.Set` call site in `SuggestionsController.HandleToggle` (active channel's profile), (c) re-assert-on-chat-open heal (ON state only, in `RestoreForActiveChat`)
- **SUP-03**: Gate in BOTH bot templates after the group-chat `If`: Read Reply Mode (Postgres) → Suppressed? (If); suppressed → dead-end (NO reply, NOT marked read — stays unread for the owner); not suppressed → existing reply path unchanged
- **SUP-04**: Resolve precedence: per-chat override beats the `'*'` default; the query ALWAYS returns one row (`coalesce(…, false)`) so absence → `suppressed=false` → bot replies. FAIL-CLOSED: a genuine Postgres error halts the execution (n8n default — do NOT set continueOnFail/onError on the read node)
- **SUP-05**: Propagation: new bots inherit the gate via template cloning (Create orchestrators clone the template verbatim — verify on a freshly created bot, no orchestrator surgery expected); existing dev clones recreated; prod bagkz stays dormant (folds into the bulk copy)

**Locked technical decisions (from the approved spec):**
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

### Deferred Ideas (OUT OF SCOPE)
- **Message batching/debounce** (combine a customer's multi-fragment message into one reply) — its own design pass, sequenced after this phase
- Clearing a per-chat override back to "inherit the bot default" (client `SemiAutoStore` never writes state 0 today)
- Authenticating `/webhook/SetReplyMode` (accepted-risk posture R-02-01)
- Any change to the «Бот работает/пауза» activation switch (stays the real n8n activate/deactivate)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SUP-01 | `reply_mode_flags` table in existing Supabase Postgres, RLS service-role only | DDL pattern verified against `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql` (default-deny RLS + `revoke all from anon,authenticated`, owner cred exempt). Use cred `1H5xlpFSESU4w6JH` (bot-template Chat Memory DB) so the gate can read it. |
| SUP-02 | Shared `/webhook/SetReplyMode` workflow + 3 client write sites | All 3 client seams verified live: `ReplyModeToggleBinder.OnReplyModeChanged` (still built-but-unconsumed, `ReplyModeToggleBinder.cs:43/155`), `SuggestionsController.HandleToggle` `SemiAutoStore.Set` at `SuggestionsController.cs:109`, `RestoreForActiveChat` `if(_semiAutoOn)` branch at `SuggestionsController.cs:95-99`. Deployer pattern = `build-suggest-replies.py` (REST + `resolve_cred`). |
| SUP-03 | Fail-closed gate after group-chat `If` in BOTH templates | Both templates structurally identical: `If.main[0] → Input type`; group dead-ends at `If.main[1]` (unconnected); `Mark Read` is downstream of `Input type` → a suppressed dead-end correctly leaves the message unread. Verified in both workflow JSONs. |
| SUP-04 | Precedence (`chat_id='*'` sort) + always-one-row `coalesce` + fail-closed | Locked resolve query is correct. Postgres param binding = `options.queryReplacement` positional `$1,$2` (typeVersion 2.6) — verified from `Delete_File` + `Dashboard_Outcomes` nodes. Do NOT set `continueOnFail`/`onError`. |
| SUP-05 | New bots inherit gate via clone; dev clones recreated; prod dormant | Create orchestrators (`XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`, `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`) clone the template via "Get Sample Workflow" → "Create Workflow". Verify on a fresh bot (Task 7 grep). |
</phase_requirements>

## Staleness Re-Validation (the core deliverable)

The 2026-07-13 plan is 6 days stale (~40 commits: Phase 8 rounds 2-3 + all of Phase 11). Every integration point below was re-checked against the live tree. **Seven concrete corrections** — apply these before writing task actions:

| # | Plan says / assumes | Live reality (VERIFIED) | Fix |
|---|---------------------|--------------------------|-----|
| C1 | `IsRealProfileId` guards on `Bot.ProfileSentinel` (Task 5 Step 3) | The constant is **`Bot.UnauthedProfileSentinel = "-1"`** (`Bot.cs:67`, public const). `Bot.ProfileSentinel` does not exist → **compile error**. | Reference `Bot.UnauthedProfileSentinel` (or the literal `"-1"`). |
| C2 | `Manager.ReplyModeSync.cs` is `public partial class Manager` (Task 5) | `Manager` is `public class Manager : MonoBehaviour` — **NOT partial** (`Manager.cs:14`). A partial in a second file **won't compile**. | Add `partial` to `Manager.cs:14` (`public partial class Manager : MonoBehaviour`) as an explicit step. One-word edit; low risk. |
| C3 | `SuggestionsController.PushReplyModeForActiveChat` calls `cm.ProfileIdForChannel(bot, cm.ActiveChannel)` (Task 6 Step 2) | `ProfileIdForChannel` is **`private static`** (`ChatManager.Channel.cs:41`) → not callable from `SuggestionsController` → **compile error**. (`ActiveChannel` IS public — that part is fine.) | Add a thin public accessor on `ChatManager` (e.g. `public string ActiveChannelProfileId()` wrapping `GetActiveProfileId()`), or promote `ProfileIdForChannel` to `internal`/`public`. Prefer the accessor (smaller surface). |
| C4 | `Manager` hook lifecycle via `Manager.OnEnable`/`OnDisable` (Task 6 Step 1) | Manager has **no OnEnable/OnDisable** — only `public void Awake()` (`:248`), `public void Start()` (`:259`), `private void OnApplicationQuit()` (`:494`); no OnDestroy. | Once C2 makes Manager partial, the new partial may declare its own `OnEnable`/`OnDisable` (no conflict). Or subscribe in `Start`/unsub in a new `OnDestroy`. Discretion area — the static event + always-alive singleton makes either safe. |
| C5 | Deployer resolves the `Postgres` credential "by NAME" (Task 2) | **Two** credentials are both named `"Postgres"**: `1H5xlpFSESU4w6JH` (bot-template Chat Memory) and `vvRrFiEXzLVqKjOx` (Dashboard/RAG). `resolve_cred` does `... AND name=? LIMIT 1` → **ambiguous / arbitrary pick**. | For the gate nodes in the bot templates, hardcode `1H5xlpFSESU4w6JH` (matches each template's existing Chat Memory). For the new Set Reply Mode workflow, pass an explicit cred-id override rather than by-name. Both point at the same Supabase DB (see A3), so functionally either works — but be explicit. |
| C6 | Upsert binds `$3 = {{ $json.suppressed }}` (boolean) | n8n Postgres `options.queryReplacement` passes params as **text** ("true"/"false"). Column is `boolean not null`. Implicit text→boolean cast usually works but is fragile. | Cast in SQL: `values ($1, $2, $3::boolean, now())`. Same defensiveness for the resolve read isn't needed (it returns, not binds, a boolean — but see A1/P4 for the read-side If). |
| C7 | SQL file at `Tools/n8n/supabase/reply_mode_flags.sql` (Task 1) | Established convention is **date-prefixed**: `2026-07-02-harden-rag-store.sql`, `2026-07-07-conversation-outcomes.sql`. | Name it `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` (or the execution date). Cosmetic but keeps the directory consistent. |

**Things the plan got right (re-confirmed, no change needed):**
- The three client write seams all still exist at (near) the claimed anchors: `SemiAutoStore.Set` call is at `SuggestionsController.cs:109`; `RestoreForActiveChat` `if(_semiAutoOn)` block at `:95-99`; `OnReplyModeChanged` static event fires at `ReplyModeToggleBinder.cs:155`.
- `Manager.n8nBaseUrl` (`public static`, `Manager.cs:188`) resolves dev-override → secrets → `bagkz` default; every webhook uses `$"{n8nBaseUrl}/webhook/…"`. `FindBotByName` is `public` (`Manager.cs:32`). `DeleteBotFilesOnServer` (`public`, `Manager.cs:2834`) is the fire-and-forget precedent.
- Gate insertion point is correct: `If.main[0]` (TRUE, 1:1) currently → `Input type`; the group (FALSE) branch is unconnected in both templates. The Telegram reply-entry node is **also named `Input type`** (the plan's "confirm its name" resolves).
- The tri-state `SemiAutoStore` (0=inherit, 1=OFF, 2=ON) and its injectable seams are unchanged (`SemiAutoStore.cs`); `SemiAutoStoreTests.cs` shows the seam-swap test idiom.
- Test infra: `Assets/Tests/Editor/Chat/` (no asmdef → `Assembly-CSharp-Editor`), `JObject.Parse(...)` pure-payload pattern proven in `SuggestRepliesPayloadTests.cs`. Suite baseline is **1165/1165** (STATE.md).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Persist the suppression flag (bot-wide `'*'` + per-chat) | Supabase Postgres (`reply_mode_flags`) | — | Single source of truth both the app-write path and the server-read gate share; keyed by `profile_id` so it is channel-agnostic. |
| Decide whether the bot replies for this message | n8n bot template (gate node) | Supabase Postgres (read) | The reply decision must happen server-side, in the autonomous workflow, before any reply work — the client isn't in that loop. |
| Write the flag on user intent | Unity client (`Manager.SyncReplyMode`) | n8n `/webhook/SetReplyMode` (upsert) | The app is the only place that knows the toggle changed; it never touches Supabase directly (matches every other `/webhook/*`). |
| Upsert one row per profile id | n8n `/webhook/SetReplyMode` | Supabase Postgres | Server owns the DB write + validation; the app posts a small JSON body. |
| Resolve precedence (per-chat beats `'*'`, absence → reply) | Supabase Postgres (the `coalesce`/`order by` query) | — | Encoding precedence + fail-open-on-absence in one always-one-row query keeps the downstream `If` from ever starving. |
| Fail-closed on DB error | n8n runtime (natural node-error halt) | — | No extra wiring — a Postgres error throws and the execution stops → no reply. Do NOT add `continueOnFail`. |
| Heal a lost "back to Авто" write | Unity client (`RestoreForActiveChat` re-assert) | — | Re-open self-corrects drift; idempotent upsert makes re-assertion always safe. |

**Note (not a rename/refactor phase → no formal Runtime State Inventory):** the only new runtime state is the `reply_mode_flags` table (created once) and the requirement to **recreate existing dev bot clones** so they inherit the gate (standard template-change cost — see Propagation). Orphaned flag rows on bot-delete are a minor hygiene gap (Open Question Q3).

## Standard Stack

This phase adds no new libraries — it composes the project's existing stack. Verified components and the exact versions/ids to use:

### Core
| Component | Version / id | Purpose | Why standard |
|-----------|-------------|---------|--------------|
| n8n Postgres node | `n8n-nodes-base.postgres` typeVersion **2.6** | `Read Reply Mode` (gate) + `Upsert` (Set Reply Mode) | Already used by `Delete_File`, `Dashboard_Outcomes`, `Delete_Bot_Files`; `executeQuery` + `options.queryReplacement`. [VERIFIED: workflow JSONs] |
| Postgres credential | id **`1H5xlpFSESU4w6JH`** (name "Postgres") | The DB behind the bot templates' `Chat Memory` — where `reply_mode_flags` must live | The gate reads with this cred; create the table on the same DB. [VERIFIED: both bot templates] |
| Supabase Postgres | Session pooler `:5432` (per memory `n8n Supabase+Postgres creds`) | Hosts `reply_mode_flags` alongside `n8n_chat_histories` / `documents` / `conversation_outcomes` | "Same DB the bot's Chat Memory and Vector Store already use — no new credential" (design). [CITED: MEMORY.md] |
| UnityWebRequest + coroutine | Unity 6000.3.9f1 | Client POST to `/webhook/SetReplyMode` | Mandatory project pattern (`networking.md`, `unity-api-integration`). No async/await. |
| Newtonsoft.Json (`JsonConvert`) | via NuGetForUnity | Serialize the flag payload; parse in EditMode tests (`JObject`) | Project standard for JSON with bodies; `JsonUtility` can't. |
| n8n Webhook / Code / If / Respond nodes | current dev n8n | Set Reply Mode graph: Webhook → Validate(Code) → If-invalid → Upsert(Postgres) → Respond | Mirrors `Suggest_Replies` + `Delete_File` shapes. |

### Supporting
| Component | Purpose | When to use |
|-----------|---------|-------------|
| `Tools/n8n/build-suggest-replies.py` | REST deploy/export + `resolve_cred` template to clone | Model for the new `build-set-reply-mode.py` (see C5 for the cred caveat). |
| `Tools/run-tests-headless.sh` | EditMode runner (Editor closed) | Payload-builder tests; `-testFilter 'ReplyModeSync'`. |
| Test bridge `Temp/claude/run-tests.trigger` | EditMode runner (Editor open) | If the Editor is already open (see memory `Unity Test Bridge`). |
| `verify-telegram-parity.py` | Structural workflow verifier precedent | Optional model for a curl/JSON structural check of the gate. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `$2 = messages[0].from` (locked) | `$2 = messages[0].chatId` | `chatId` is what Telegram memory keys on and what the app sends as `CurrentChatId` — strictly more defensive. But `from == chatId` for every 1:1 (the only case reaching the gate), so the locked `from` is verified-equivalent. Keep `from` (locked); note `chatId` as the drop-in if a live mismatch ever appears (P1). |
| n8n Postgres node | Direct Supabase REST from the app | Rejected by design — the app never touches Supabase directly (auth-surface + consistency with every other `/webhook/*`). |
| Fold suppression into Suggest Replies | Separate `SetReplyMode` workflow | Rejected — suppression gates the **autonomous** reply workflow; Suggest Replies is a separate always-active workflow the app calls directly. Keeping them separate is why «Вместе» gets no auto-reply while the panel still populates. |

**Installation:** none (no new packages). DDL + workflow deploy only — see Environment Availability.

**Version verification:** n8n Postgres node typeVersion **2.6** confirmed on the live `Delete_File` and `Dashboard_Outcomes` nodes [VERIFIED: workflow JSONs, 2026-07-19]. No npm registry lookups apply (server-side n8n + Unity client).

## Architecture Patterns

### System Architecture Diagram

```
  OWNER ACTION (Unity app)                          INCOMING CUSTOMER MESSAGE (wappi/tapi → n8n)
  ────────────────────────                          ────────────────────────────────────────────
  bot-default flip                                   Webhook (body.messages[0])
    └─ ReplyModeToggleBinder.OnReplyModeChanged            │
         │  (all authed profiles, chatId "*")              ▼
  per-chat toggle                                    group-chat If  (from == chatId ?)
    └─ SuggestionsController.HandleToggle                  │ true (1:1)      │ false (group)
         │  (active channel profile, real chatId)          ▼                 └─► dead-end (drop)
  chat-open re-assert (ON only)                    ┌──────────────────────┐
    └─ RestoreForActiveChat                        │ Read Reply Mode (PG) │  ← NEW gate
         │                                          │  coalesce(...,false) │     cred 1H5xlpFSESU4w6JH
         ▼                                          └──────────┬───────────┘        (fail-closed:
  Manager.SyncReplyMode  (fire-and-forget)                     ▼                      error → halt)
         │  POST {profileIds[],chatId,suppressed}    Suppressed?  (If json.suppressed)
         ▼                                            │ false            │ true
  /webhook/SetReplyMode                               ▼                  └─► dead-end
    Webhook → Validate(Code, drop sentinels,     Input type (switch)          NO reply
      1 item/profileId) → If-invalid?             …existing reply path…       NOT Mark Read
        → Upsert(PG, on conflict do update)       (Mark Read, Typing,          → stays UNREAD
        → Respond {success,written}                Agent, send)
         │                                                  ▲
         ▼                                                  │
  ┌─────────────────────────────────────────────┐          │
  │  Supabase Postgres: reply_mode_flags         │──────────┘  (both channels read by profile_id)
  │  pk(profile_id, chat_id)  chat_id='*'=default │
  └─────────────────────────────────────────────┘
```

Trace the primary use case: owner flips a chat to «Вместе» → app POSTs `{profileIds:[<active>], chatId:<id>, suppressed:true}` → row upserted. Customer sends a message → bot Webhook → group-chat If (1:1, passes) → Read Reply Mode returns `true` → Suppressed? true → dead-end → no reply, message stays unread. Owner sees the unread badge, answers from the suggestions panel (unaffected).

### Recommended file/artifact layout
```
Tools/n8n/
├── supabase/2026-07-19-reply-mode-flags.sql   # DDL (C7 naming)
├── build-set-reply-mode.py                     # deployer (mirror build-suggest-replies.py; C5 cred)
└── workflows/
    ├── <id>-Set_Reply_Mode.json                # new canonical export (13th workflow)
    ├── 4wYitz5ek30SVNlT-WhatsApp_Bot.json      # + gate (2 nodes) on If.main[0]
    └── 4VN3gsFaC2HUYmcc-Telegram_Bot.json      # + identical gate
Assets/Scripts/Main/
├── Manager.cs                                  # add `partial` to line 14 (C2)
└── Manager.ReplyModeSync.cs                     # new partial: BuildReplyModePayload/AuthedProfileIds/SyncReplyMode + hook
Assets/Scripts/Main/ChatManager.Channel.cs      # add public ActiveChannelProfileId() accessor (C3)
Assets/Scripts/Chat/SuggestionsController.cs     # per-chat write + re-assert
Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs   # pure payload/profile tests
```

### Pattern 1: The gate node pair (both templates, identical)
**What:** Insert two nodes on the group-chat `If` TRUE output, before `Input type`.
**When to use:** Both `…-WhatsApp_Bot.json` and `…-Telegram_Bot.json`.
```
// Rewire: If.main[0]  →  Read Reply Mode  →  Suppressed?
//         Suppressed? (false) → Input type      (existing reply path, unchanged)
//         Suppressed? (true)  → <unconnected>   (dead-end: no Mark Read → stays unread)

// Read Reply Mode — Postgres executeQuery, cred 1H5xlpFSESU4w6JH, NO continueOnFail/onError:
select coalesce(
  (select suppressed from reply_mode_flags
   where profile_id = $1 and chat_id in ($2, '*')
   order by (chat_id = '*')   -- specific chat_id sorts before the '*' default
   limit 1),
  false
) as suppressed;
// options.queryReplacement (positional, comma-separated — mirrors Chat Memory's node reference):
//   ={{ $('Webhook').item.json.body.messages[0].profile_id }},{{ $('Webhook').item.json.body.messages[0].from }}

// Suppressed? — If node. See P4: use a BOOLEAN-typed condition on {{ $json.suppressed }},
// then confirm the branch taken via runData in the structural test.
```
Source: locked resolve query (CONTEXT) + param mechanism [VERIFIED: `Delete_File` node, `options.queryReplacement="={{ $json.body.fileId }}"`, typeVersion 2.6].

### Pattern 2: Set Reply Mode upsert (per-profileId item)
**What:** Validate emits one item per surviving profileId; Postgres upserts once per item.
```sql
insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at)
values ($1, $2, $3::boolean, now())        -- $3::boolean (C6): queryReplacement passes text
on conflict (profile_id, chat_id) do update
  set suppressed = excluded.suppressed, updated_at = now();
-- options.queryReplacement: ={{ $json.profileId }},{{ $json.chatId }},{{ $json.suppressed }}
```
Source: design §3 + [VERIFIED: list-vs-scalar gotcha in `Dashboard_Outcomes` `Find Changed Sessions` uses base64 for a LIST param; scalars go straight through comma-separated].

### Pattern 3: Unity fire-and-forget sync (mirror `DeleteBotFilesOnServer`)
**What:** Pure static builder + coroutine on the always-alive `Manager` singleton.
```csharp
// Manager.ReplyModeSync.cs  (requires `partial` on Manager.cs:14 — C2)
public static string BuildReplyModePayload(IReadOnlyList<string> profileIds, string chatId, bool suppressed) { … }  // JsonConvert
public static string[] AuthedProfileIds(Bot bot) {   // skip sentinels — C1
    var ids = new List<string>(2);
    if (IsReal(bot.whatsappProfileId)) ids.Add(bot.whatsappProfileId);
    if (IsReal(bot.telegramProfileId)) ids.Add(bot.telegramProfileId);
    return ids.ToArray();
}
private static bool IsReal(string id) =>
    !string.IsNullOrEmpty(id) && id != Bot.UnauthedProfileSentinel;   // NOT Bot.ProfileSentinel
public void SyncReplyMode(string[] profileIds, string chatId, bool suppressed) {
    if (profileIds == null || profileIds.Length == 0) return;
    StartCoroutine(SyncReplyModeRoutine(BuildReplyModePayload(profileIds, chatId, suppressed)));
}
// SyncReplyModeRoutine: POST {n8nBaseUrl}/webhook/SetReplyMode, Content-Type application/json,
//   request.timeout = 30, using-block, log on !Success. NO auth header (matches all /webhook/*).
```
Source: `unity-api-integration` SKILL + `Manager.DeleteBotFilesOnServer` precedent [VERIFIED].

### Anti-Patterns to Avoid
- **Setting `continueOnFail`/`onError:'continueRegularOutput'` on Read Reply Mode** — breaks fail-closed. A DB error MUST halt (design SUP-04).
- **Comparing message text to decide anything** — irrelevant here; the gate keys on ids only.
- **Sending an auth header to `/webhook/SetReplyMode`** — every app `/webhook/*` is unauthenticated (R-02-01). Don't invent one.
- **Activating a bot-template clone outside a test window** — real contacts (STATE.md constraint). Deactivate immediately after structural verify.
- **Re-asserting on every live poll** — the re-assert belongs ONLY in `RestoreForActiveChat` (chat open). The 3s LivePoll fires `OnLiveMessagesReceived`, never `OnChatSelected`, so it does not (and must not) trigger a flag write (P3).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Precedence + absence-default | An `If`-chain testing default vs override | The single locked `coalesce(... order by (chat_id='*') limit 1, false)` query | One round-trip, always-one-row, encodes precedence AND fail-open-on-absence; the downstream If never starves. |
| Fail-closed on DB error | A try/catch or error-branch that "defaults to reply" | n8n's natural node-error halt (no wiring) | Any error-tolerance here inverts the safety property. Do nothing. |
| List param into Postgres | `profileIds.join(',')` into one `queryReplacement` slot | One item per profileId + scalar `$1,$2,$3` | `queryReplacement` comma-splits values (memory `n8n Postgres node gotchas`); a joined list corrupts binding. |
| Profile-id sentinel filtering | Ad-hoc `!= "-1"` scattered | `Manager.AuthedProfileIds` centralizing `Bot.UnauthedProfileSentinel` | One tested seam; matches `ChatManager.IsValidProfileId`. |
| JSON payload assembly | `string.Format`/concatenation | `JsonConvert.SerializeObject` of a `[Serializable]` DTO | Escapes `@c.us`, quotes booleans correctly; unit-testable via `JObject.Parse`. |
| App→server flag write | Direct Supabase client in Unity | `/webhook/SetReplyMode` | Consistency + auth surface (matches UploadFile/DeleteFile/DashboardOutcomes). |

**Key insight:** the entire safety model is "let the natural failure modes fall closed." The locked query and the no-error-wiring posture are load-bearing — the biggest risk is *adding* cleverness (error branches, list joins, precedence If-chains) that quietly defeats it.

## Common Pitfalls

### Pitfall 1: Identifier mismatch between app-write and server-read (the flagged highest risk)
**What goes wrong:** The per-chat override row is keyed `(profile_id, chat_id)`. If the `chat_id` the app writes (`CurrentChatId`) differs by even a suffix from what the gate reads (`messages[0].from`), the override silently never matches → the bot keeps replying in a «Вместе» chat.
**Why it happens:** WhatsApp ids carry `@c.us`; Telegram ids are bare numeric and were canonicalized client-side in 08-05 (`ChatIdFormat.CanonicalKey` strips a spurious `@c.us` twin; `vm.ChatId`/`CurrentChatId` = bare tapi id on TG, byte-identical `…@c.us` on WA).
**How to avoid / evidence it holds:** VERIFIED from captured tapi shapes — for every 1:1 sample `from == chatId == <bare numeric>` (`Tools/tapi/samples/message_type_text.json`: `from=chatId="1038376805"`; `messages_6062310939.json`: `from=chatId="6062310939"`). WhatsApp: the group-chat `If` compares `from == chatId` and 1:1 bots demonstrably reply → the webhook carries `…@c.us` for both. **The gate only ever runs on 1:1 chats** (groups dead-end at the `If` before the gate), and for 1:1 `from == chatId == CurrentChatId` on both channels. So the locked `$2 = from` is verified-correct.
**Warning signs:** a «Вместе» chat still auto-replies in the live e2e. If ever seen, swap the gate's `$2` to `messages[0].chatId` (equivalent for 1:1, and what memory keys on) — a one-token change.

### Pitfall 2: The gate reads a stale/other credential's DB
**What goes wrong:** `Read Reply Mode` returns "no such table" or always-false because it points at a different Postgres cred than the one the table was created on.
**Why it happens:** two credentials share the name "Postgres" (`1H5xlpFSESU4w6JH` vs `vvRrFiEXzLVqKjOx`); `resolve_cred`/by-name is ambiguous (C5).
**How to avoid:** create `reply_mode_flags` on the **same DB the gate uses** (cred `1H5xlpFSESU4w6JH`, the bot-template Chat Memory DB). Apply the DDL through that cred (a one-off Postgres `executeQuery` on it, or the Supabase SQL editor on that project). Hardcode `1H5xlpFSESU4w6JH` in the gate nodes; don't rely on by-name.
**Warning signs:** structural verify shows `Read Reply Mode` erroring (which fail-closes → NO reply everywhere) or returning false for a known-suppressed chat.

### Pitfall 3: Re-assert spam / unintended writes from the open-chat live poll
**What goes wrong:** a naive re-assert could fire a POST every 3 seconds.
**Why it happens:** Phase-8 added `ChatManager.LivePoll.cs` — a 3s open-chat poll that re-issues `SyncLatestMessages` and fires `OnLiveMessagesReceived`.
**How to avoid:** put the re-assert ONLY in `RestoreForActiveChat` (fires on `OnChatSelected`, i.e., once per chat open). VERIFIED: the LivePoll fires `OnLiveMessagesReceived` → `SuggestionsController.HandleLive` (suggestions only, no flag write); it never calls `RestoreForActiveChat`. So the re-assert stays once-per-open with no code effort — just don't add a write to `HandleLive`.
**Warning signs:** n8n execution log shows repeated `SetReplyMode` hits while a chat sits open.

### Pitfall 4: Boolean round-trip through the n8n Postgres node
**What goes wrong:** the `Suppressed?` If takes the wrong branch because `$json.suppressed` is a string `"true"` while the condition tests `=== true` (strict).
**Why it happens:** the Postgres node may surface a boolean column as a JS string depending on typeVersion/driver behavior (the `largeNumbersOutput=text` default is the bigint sibling of this class of issue).
**How to avoid:** author `Suppressed?` as a **boolean-type** condition reading `{{ $json.suppressed }}` (let n8n coerce), and **confirm the branch via runData** in the structural test (a suppressed message must show the reply-path nodes ABSENT after `Suppressed?`). Cast the read explicitly if needed (`suppressed::boolean`). This is A1 — confirm during execution.
**Warning signs:** a suppressed chat replies, or a non-suppressed chat goes silent, in the structural runData check.

### Pitfall 5: New-file import + non-partial Manager (Unity gotchas)
**What goes wrong:** (a) the new `Manager.ReplyModeSync.cs` won't compile (Manager not partial — C2); (b) a brand-new `.cs` is silently excluded from compile until imported (memory `Unity new-file import quirk`).
**How to avoid:** add `partial` to `Manager.cs:14` first; after creating new files, confirm the `.meta` was generated (Assets/Refresh) before running tests. Use the headless runner or the in-Editor bridge per the current Editor state (memory `Unity Test Bridge`).
**Warning signs:** "type Manager already defines"/"partial modifier" compile errors; tests report 0/0 (new file not compiled).

### Pitfall 6: Dev clones carry the OLD (gate-less) template
**What goes wrong:** an existing dev bot keeps auto-replying in «Вместе» because its clone predates the gate.
**Why it happens:** the gate is per-bot-template code; existing clones are frozen copies.
**How to avoid:** recreate existing dev clones after the template fix (standard cost — matches vertical-prompts + Telegram-parity rollouts). Verify a freshly-created bot inherits the gate (Task 7 grep). Prod bagkz stays dormant (folds into the bulk copy).

## Code Examples

### Per-chat write + re-assert (SuggestionsController) — with C3 accessor
```csharp
// SuggestionsController.HandleToggle — immediately after SemiAutoStore.Set (currently :109):
SemiAutoStore.Set(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, desiredOn);
PushReplyModeForActiveChat(desiredOn);   // NEW: mirror to server

// RestoreForActiveChat — inside the existing `if (_semiAutoOn)` block (:95-99):
if (_semiAutoOn) { ShowPanel(); IssueRequest(null, null); PushReplyModeForActiveChat(true); }  // heal lost "back to Авто"

private void PushReplyModeForActiveChat(bool suppressed)
{
    var cm = ChatManager.Instance;
    if (cm == null || Manager.Instance == null) return;
    Bot bot = Manager.Instance.FindBotByName(cm.CurrentBotId);
    if (bot == null) return;
    string profileId = cm.ActiveChannelProfileId();   // NEW public accessor (C3), wraps GetActiveProfileId()
    if (string.IsNullOrEmpty(profileId) || profileId == Bot.UnauthedProfileSentinel) return;
    Manager.Instance.SyncReplyMode(new[] { profileId }, cm.CurrentChatId, suppressed);
}
```
Source: plan Task 6 + C3 correction [VERIFIED: `ProfileIdForChannel` private @ ChatManager.Channel.cs:41; `ActiveChannel` public @ :18].

### Bot-default hook (Manager partial)
```csharp
// Subscribe once on the always-alive singleton (lifecycle is discretion — C4).
private void OnBotReplyModeChanged(string botId, ReplyModeToggleBinder.ReplyMode mode)
{
    Bot bot = FindBotByName(botId);
    if (bot == null) return;
    SyncReplyMode(AuthedProfileIds(bot), "*", mode == ReplyModeToggleBinder.ReplyMode.Semi);
}
```
Source: `ReplyModeToggleBinder.OnReplyModeChanged` [VERIFIED: still built-but-unconsumed, `ReplyModeToggleBinder.cs:43`, fires @ `:155`].

### EditMode test (mirror SuggestRepliesPayloadTests)
```csharp
// Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs — pure, no PlayerPrefs.
var o = JObject.Parse(Manager.BuildReplyModePayload(new[] {"pWA","pTG"}, "*", true));
Assert.AreEqual("*", (string)o["chatId"]); Assert.IsTrue((bool)o["suppressed"]);
// AuthedProfileIds: new GameObject("Bot9").AddComponent<Bot>(); set whatsappProfileId/telegramProfileId="-1";
//   Assert CollectionAssert.AreEqual(new[]{"pWA"}, Manager.AuthedProfileIds(bot)); DestroyImmediate after.
```
Source: `SuggestRepliesPayloadTests.cs:34` pattern [VERIFIED].

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Telegram `chatId` sliced/`@c.us`-twinned | `ChatIdFormat.CanonicalKey` → bare numeric `CurrentChatId` on TG | Phase 8 (08-05) | The app's per-chat `chatId` is now canonical → matches the gate's numeric `from` for TG 1:1. |
| No open-chat live refresh | `ChatManager.LivePoll.cs` 3s poll (`OnLiveMessagesReceived`) | Phase 8 (08-04) | Confirms suggestions still refresh in a suppressed chat; does NOT trigger re-assert (P3). |
| Manager monolith | still `class Manager` (non-partial) | — | Must add `partial` for the new file (C2). |
| Suite 957→1036→1105 | **1165/1165** EditMode | Phase 11 close | New tests add to 1165 baseline. |

**Deprecated/outdated in the 2026-07-13 plan:** `Bot.ProfileSentinel` (never existed — it's `Bot.UnauthedProfileSentinel`); "resolve the Postgres credential by NAME" (ambiguous — two "Postgres" creds); the implicit assumption that `Manager` is partial and that `ProfileIdForChannel` is callable from `SuggestionsController`.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | n8n Postgres node (v2.6) surfaces the resolved `suppressed` boolean such that a boolean-typed `Suppressed?` If branches correctly | Pitfall 4 / Pattern 1 | Gate inverts (suppressed chat replies, or normal chat silenced). **Mitigation: confirm via runData in the structural test — cheap, in-scope for Task 3/4.** |
| A2 | The incoming bot **webhook** payload carries `body.messages[0].from`/`.chatId`/`.profile_id` with the same shape as the captured `messages/get` samples | Pitfall 1 | Gate reads wrong/empty ids → fail-closes everywhere or never matches. **Low risk: the shipped group-chat `If` already reads these fields and 1:1 bots reply, independently confirming the shape.** |
| A3 | Both "Postgres"-named creds (`1H5xlpFSESU4w6JH`, `vvRrFiEXzLVqKjOx`) target the **same** Supabase Postgres DB, so `reply_mode_flags` is visible to the gate regardless of which the DDL ran through | Pitfall 2 / C5 | If different DBs, the gate can't see a table created via the other cred. **Mitigation: apply DDL through cred `1H5xlpFSESU4w6JH` (the gate's cred) — removes the assumption.** |

**If any assumption is wrong, the fix is local and cheap.** None blocks the architecture; all resolve inside the existing task structure (structural runData check + "use the gate's own cred for DDL").

## Open Questions

1. **Boolean surfacing from Postgres (A1)** — What we know: the resolve query returns a real `boolean`; the If must branch on it. What's unclear: exact JS type at the If node. Recommendation: author the If as a boolean-typed condition and confirm via runData during Task 3's structural verify; cast `suppressed::boolean` in the read if the runData shows a string.
2. **DDL application mechanism ([BLOCKING] — plan this explicitly)** — `secrets.json` is deny-ruled for Claude, so the DDL + live e2e need the dev-n8n path or owner assistance. Recommendation: make "apply `reply_mode_flags` DDL on the dev Supabase (via a one-off Postgres `executeQuery` on cred `1H5xlpFSESU4w6JH`, or the Supabase SQL editor) and verify with a probe row" a **[BLOCKING] human/dev task** at the top of the plan, gating the gate-structural-verify tasks.
3. **Orphaned flag rows on bot delete (hygiene)** — `Bot.DeleteBot` deletes the `ReplyMode` PlayerPrefs key but NOT the per-chat `_semiAuto_` keys (explicitly accepted, `SemiAutoStore` doc) and there is no server sweep of `reply_mode_flags` rows keyed by a deleted bot's profile ids. What's unclear: whether to add a sweep (mirroring `DeleteBotFilesOnServer`). Recommendation: OUT OF SCOPE for this phase (matches the client's accepted-orphan posture; profile ids are unique so stale rows are inert), but record it as a deferred hygiene item.
4. **`from` vs `chatId` for the gate param (P1)** — locked to `from`; verified-equivalent to `chatId` for 1:1. Recommendation: keep `from` (locked), but note `chatId` as the drop-in equivalent so the executor has an immediate fallback if the live e2e surfaces a mismatch.

## Environment Availability

| Dependency | Required By | Available | Version/id | Fallback |
|------------|------------|-----------|-----------|----------|
| Dev n8n (`localhost:5678`) | Deploy Set Reply Mode + edit bot templates + structural verify | Assumed per project setup | dev instance | Prod bagkz is DORMANT — do NOT target it this phase. |
| Cloudflare tunnel | Live e2e webhook delivery to dev n8n | Owner-run (`rotate-tunnel.py` present) | — | e2e blocks without it (human gate). |
| Supabase Postgres (cred `1H5xlpFSESU4w6JH`) | `reply_mode_flags` table + gate read | Yes (behind Chat Memory) | Session pooler :5432 | none — this IS the DB. |
| `secrets.json` (n8nAPIKey, DB creds) | Deployer REST auth + DDL | **Deny-ruled for Claude** | — | Owner-assisted or dev-n8n path (Q2). |
| `python3` + `urllib`/`sqlite3` | `build-set-reply-mode.py` deployer | Yes (build-suggest-replies.py runs) | system | — |
| Unity 6000.3.9f1 + EditMode runner | Payload tests | Yes | headless script + bridge | — |
| Real WhatsApp + Telegram dev profiles | Human e2e (SUP-03/05 proof) | Owner-run | — | clone active only during the window, then deactivate. |

**Missing/gated dependencies with no code fallback:**
- DDL application + live e2e require `secrets.json`/dev-n8n access → **[BLOCKING] human/dev task** (Q2). Everything else (payload builder + tests, workflow JSON authoring, template gate edits as JSON) is doable without secrets.

**Propagation (the real scope cost):** the gate is per-template code → land it in both `…-WhatsApp_Bot.json` and `…-Telegram_Bot.json`; new bots inherit it automatically via the Create orchestrators' clone (verify, no orchestrator surgery expected); recreate existing dev clones; prod bagkz stays dormant. README workflow count **12 → 13**.

## Security Domain

`security_enforcement` is absent from `.planning/config.json` → treated as enabled.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Webhook intentionally unauthenticated (R-02-01, accepted-risk, consistent with every app `/webhook/*`). Record in the phase threat model. |
| V3 Session Management | no | No sessions; stateless webhook. |
| V4 Access Control | partial | RLS default-deny on `reply_mode_flags` (service-role/owner only); `revoke all from anon, authenticated` (the app's anon key ships in the mobile binary). Mirrors `conversation_outcomes`. |
| V5 Input Validation | **yes** | The `Validate` Code node: require `Array.isArray(profileIds)&&length`, `typeof chatId==='string'`, `typeof suppressed==='boolean'`; drop sentinel ids; malformed → `{success:false,error:"bad_request"}` with NO DB write. |
| V6 Cryptography | no | No new secrets, no crypto; DB creds already managed via n8n credential store. |

### Known Threat Patterns for this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| SQL injection via chatId/profileId | Tampering | Parameterized `$1/$2/$3` via `options.queryReplacement` — never string-concat into the query. |
| Unauthenticated flag write (anyone who learns the URL could set a bot silent/loud) | Tampering / DoS | Accepted-risk (R-02-01); blast radius = suppress/unsuppress a bot's replies (no data exfil, no LLM cost — it's a small DB upsert). Validate node rejects malformed bodies. Document in threat model; authentication is a deferred item. |
| Malformed body → partial write | Tampering | `If-invalid?` routes to Respond BEFORE the upsert → no partial write (curl matrix asserts it). |
| Fail-open on DB error (silent bot answering a «Вместе» chat) | Repudiation/Safety | Fail-CLOSED by design — Postgres error halts the execution (no `continueOnFail`). |
| RLS bypass via anon key in the mobile app | Info Disclosure / Tampering | Table is never exposed to the Data API; default-deny RLS + `revoke` (the app never reads/writes it directly — only the server cred does). |

**Note (supabase skill):** enable RLS on the new `public` table even though only the service-role/owner cred touches it (defense in depth; the anon key is public). Do NOT add permissive policies — the n8n Postgres credential is the table owner and is exempt from non-FORCE RLS, exactly as `conversation_outcomes` works today.

## Sources

### Primary (HIGH confidence — live codebase, this session)
- `Assets/Scripts/UI/ReplyModeToggleBinder.cs` — `OnReplyModeChanged` event (:43), fires @ :155; `GetMode` (:63); `ReplyMode{Auto=0,Semi=1}`
- `Assets/Scripts/Chat/SemiAutoStore.cs` — tri-state resolve + injectable seams
- `Assets/Scripts/Chat/SuggestionsController.cs` — `HandleToggle`/`SemiAutoStore.Set` @ :109; `RestoreForActiveChat` @ :90-101; `HandleLive` @ :166
- `Assets/Scripts/Main/ChatManager.Channel.cs` — `ActiveChannel` public @ :18; `ProfileIdForChannel` **private static** @ :41; `OnActiveChannelChanged` @ :27
- `Assets/Scripts/Main/ChatManager.BotState.cs` — `CurrentBotId`, `GetActiveProfileId` (private), `IsValidProfileId`, `FindBotByName` usage
- `Assets/Scripts/Main/ChatManager.LivePoll.cs` — 3s poll fires `OnLiveMessagesReceived`, not `OnChatSelected`
- `Assets/Scripts/Main/ChatManager.Suggestions.cs` — `public string CurrentChatId => currentChatId` (:11)
- `Assets/Scripts/Main/Bot.cs` — `UnauthedProfileSentinel="-1"` (:67); `whatsapp/telegramProfileId` (:69-70); `DeleteBot` deletes `ReplyMode` not `_semiAuto_`
- `Assets/Scripts/Main/Manager.cs` — `class Manager` NOT partial (:14); `n8nBaseUrl` (:188); `FindBotByName` (:32); `DeleteBotFilesOnServer` (:2834); Awake/Start public, OnApplicationQuit private
- `Assets/Scripts/Chat/ChatIdFormat.cs` — `CanonicalKey`/`Recipient`/`DisplayFallback` (canonicalization semantics)
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` + `4VN3gsFaC2HUYmcc-Telegram_Bot.json` — node lists, group-chat `If` (`from==chatId`), `If.main[0]→Input type`, group dead-ends, `Mark Read` downstream, Chat Memory cred `1H5xlpFSESU4w6JH`, Telegram sessionKey uses `chatId`
- `Tools/n8n/workflows/ZTqpumOpL1rNDOp6-Delete_File.json` + `2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` — Postgres `executeQuery` + `options.queryReplacement` positional binding, typeVersion 2.6, base64 list workaround, cred `vvRrFiEXzLVqKjOx`
- `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql` — default-deny RLS + `revoke` precedent; date-prefixed naming
- `Tools/n8n/build-suggest-replies.py` — REST deploy/export + `resolve_cred` (exact-NAME, `LIMIT 1` → ambiguity)
- `Tools/tapi/samples/message_type_text.json`, `messages_6062310939.json` — `from == chatId == <bare numeric>` for TG 1:1
- `Assets/Tests/Editor/Chat/SemiAutoStoreTests.cs`, `SuggestRepliesPayloadTests.cs` — seam-swap + `JObject.Parse` test idioms
- `.planning/config.json` — `nyquist_validation:false` (Validation Architecture omitted); `security_enforcement` absent (Security Domain included)

### Secondary (MEDIUM — project memory + docs, cross-referenced with code)
- MEMORY.md `n8n Supabase+Postgres creds` (Session pooler :5432), `n8n Postgres node gotchas` (queryReplacement comma-split, largeNumbersOutput=text, flat `{type,content}`), `RAG scoping architecture` (single-key filter), `Unity Test Bridge`, `Unity new-file import quirk`, `Bot reply modes`
- `docs/superpowers/specs/2026-07-13-semi-auto-suppression-flag-design.md` (approved design)
- `docs/superpowers/plans/2026-07-13-semi-auto-suppression-flag.md` (7-task breakdown — corrected above)
- `.planning/STATE.md` (Phase 8/11 accumulated context; 1165/1165; INACTIVE-clone constraint)

### Tertiary (LOW — none load-bearing)
- None. Every claim is code- or artifact-verified except A1-A3, which are flagged and resolve inside the task structure.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all components already in-repo; versions/ids read from live JSON.
- Architecture: HIGH — design approved; both templates structurally verified identical; gate insertion point + unread semantics confirmed by `Mark Read` placement.
- Identifier contract (highest-risk): HIGH for 1:1 (the only case reaching the gate) — captured tapi shapes + independent group-chat-If confirmation.
- Pitfalls/corrections: HIGH — each of the 7 corrections is a direct grep/read finding with file:line.
- Boolean surfacing (A1): MEDIUM — resolves via a cheap in-scope runData check.

**Research date:** 2026-07-19
**Valid until:** ~2026-08-02 (stable — but re-verify anchor line numbers if more Phase-8/11 gap rounds land on `SuggestionsController.cs`/`ChatManager.*`/`Manager.cs` before planning).
