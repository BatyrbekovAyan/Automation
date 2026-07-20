# Phase 10: Message Batching / Debounce - Research

**Researched:** 2026-07-20
**Domain:** n8n workflow orchestration (Wait/HTTP/Code node splice) + Unity client debounce timer
**Confidence:** HIGH (integration points verified by direct inspection of the two committed templates + the client controller; n8n runtime semantics verified via official docs)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions (do NOT re-litigate)

**Requirement definitions (BATCH ids):**
- **BATCH-01** (auto combine): a pre-generation stage `Debounce Wait (~8s) → Fetch Recent (messages/get) → Latest+Combine (Code) → Is Latest? (If)` inserted after the group `If` (and after the Phase-9 gate), before `Input type`. Only the last fragment's execution proceeds; it feeds the AI Agent the concatenation of the trailing run of consecutive incoming TEXT messages since the last bot/owner reply. Earlier fragments abort (dead-end) — never generate.
- **BATCH-02** (auto correctness): one Wappi fetch serves both the is-latest check (newest incoming id == this execution's `messages[0].id`?) and the combine (gather incoming since last `fromMe`, bounded by fetch limit ~15). Single complete message → one reply after the window. Media-latest → processed alone (combinedText=null, existing behavior). Both channels (channel base already correct post-Phase-4; id/body field names per Phase-3 SHAPES.md).
- **BATCH-03** (suggestions coalesce): debounce timer (~2.5s) in `SuggestionsController.HandleLive` — reset on each incoming, fire `IssueRequest` when it settles; cancel on chat close / bot switch. Manual refresh (INT-03) and card-pick re-cluster (INT-04) are NOT debounced (fire immediately). Combine is free (payload already ships last ≤12 messages); the seq guard already prevents flicker.

**Locked technical decisions (from the approved spec):**
- The AI Agent generates BEFORE the humanizer pauses — so the debounce MUST be pre-generation (a new Wait), not a repurposed pause. This is the load-bearing finding.
- Windows: ~8s auto / ~2.5s suggestions — single tunable constants. Every auto-reply (even a single complete message) waits the auto window before generating — the accepted latency cost.
- Combine boundary rule (v1): trailing run of consecutive incoming TEXT messages; a media message OR a `fromMe` message bounds the run. Latest is media → process alone. Known v1 limitation: media interleaved with text in one burst can drop an earlier text fragment (rare; v2).
- Composition: suppression gate (Phase 9) runs BEFORE the debounce (semi-auto chat skips the whole path — no wait). Pipeline: `group If → suppression gate → debounce+combine → Input type → AI Agent → humanizer → send`.
- Short Wait node = in-memory resume (no webhook-resume URL); one waiting execution per fragment (n8n concurrent).
- The winning execution sets the agent text input from `combinedText` (when non-null) instead of the single `messages[0].body`; the audio branch is unchanged.

### Claude's Discretion
- Exact window constant values within the stated ranges (start 8s / 2.5s; tune at e2e)
- Code-node parse structure for Latest+Combine; join delimiter (default `"\n"`)
- The injectable-scheduler shape for the client debounce helper (for EditMode testability)
- Whether structural verification uses execution runData introspection (preferred — proves abort vs combine) or JSON grep

### Deferred Ideas (OUT OF SCOPE)
- Adaptive windows (punctuation-aware "message looks complete → reply now / shorten wait")
- Combining across media types (transcribe voice into the text run; interleaved media+text bursts) — v1 boundary rule handles pure-text; this is the v2 refinement
- Trimming the existing humanizer `Pause Before Reading` now that the debounce already waited (future latency tune)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BATCH-01 | Pre-generation debounce+dedupe+combine stage in BOTH bot templates; only the last fragment's execution proceeds, earlier fragments dead-end | Verified splice point (`Suppressed?` main#1 → new nodes → `Input type`), Wait in-memory resume confirmed, concurrent-execution model confirmed. See Architecture Pattern 1 + Code Examples. |
| BATCH-02 | One `messages/get` fetch serves is-latest + combine; single message → one reply; media-latest alone; both channels | Verified fetch pattern (existing `Mark Read`/`HTTP Request` nodes, WappiAuthToken cred `EuhhqAaV56DpoqAN`), tapi vs api field verdicts (id/body/fromMe/type). See Pattern 1 + Pitfalls 1–4. |
| BATCH-03 | ~2.5s debounce timer in `SuggestionsController.HandleLive`; manual refresh + card-pick stay immediate; cancel on close/switch | Verified `HandleLive`/`HandleManualRefresh`/`HandleCardTapped`/`OnDisable`/`ResetForNoOpenChat` signatures + the established injectable-clock pure-gate precedent (`OpenChatLivePollGate`). See Pattern 2 + Code Examples. |
</phase_requirements>

## Summary

This phase splices a **pre-generation debounce stage** into two per-channel n8n bot templates and adds a **client-side debounce timer** to the suggestions controller. Both integration points were verified by direct inspection. The n8n side is the harder half: insert `Debounce Wait → Fetch Recent → Latest+Combine (Code) → Is Latest? (If)` on the `Suppressed?` node's FALSE output (main#1), before `Input type`, in BOTH `4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `4VN3gsFaC2HUYmcc-Telegram_Bot.json`. The client side is a small, well-precedented change: wrap the existing `HandleLive → IssueRequest` call in a debounce that resets on each incoming and cancels on chat-close/bot-switch, leaving manual refresh and card-pick immediate.

Two load-bearing runtime facts were **verified against n8n's official docs**: (1) a Wait node under 65 seconds does NOT offload execution data to the database — the process stays in memory and resumes after the interval `[CITED: n8n-docs wait.md]`, which confirms the design's "short Wait = in-memory resume, one waiting execution per fragment, n8n runs them concurrently" claim; (2) the existing template Wait nodes are `n8n-nodes-base.wait` typeVersion 1.1 using a plain `amount` (seconds) — so the new `Debounce Wait` is a trivial `{ "amount": 8 }`.

The **single most important finding** is a data-flow trap the design does not call out: in both templates, `Input type` (switch) reads `$json.body.messages[0].type` and `Download Audio` reads `$json.body.messages[0].file_link` — bare `$json.body`. After inserting an HTTP fetch (which replaces the item with its response) and a Code node, `$json.body` is gone. The **Latest+Combine Code node MUST re-emit the webhook body** (`return [{ json: { ...$('Webhook').first().json, abort, combinedText } }]`) so the unchanged `Input type`/`Download Audio`/`Text` nodes keep resolving. This is verified below (both nodes really do read `$json.body`) and it also self-heals a latent question about whether the Phase-9 Postgres gate currently passes `body` through at all (Phase-9's live runData verify, 09-04, has not run yet).

**Primary recommendation:** Author both template edits with an idempotent by-node-name Python migration (`apply-*.py` pattern, like `apply-rag-fixes.py`), have the Latest+Combine Code node re-emit `body`, sort the fetched messages by `time` (present on both channels) to find "newest" deterministically, and NEVER send `mark_all` on the debounce fetch. On the client, add a pure `IncomingDebounceGate` (injectable clock, in the mold of `OpenChatLivePollGate`) driven by a thin coroutine. Split the phase like Phase 9: structural/TDD plans (autonomous) + owner-run live gates (`autonomous: false`) for the curl matrix and both-channel e2e.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Auto-reply debounce + dedupe + combine | n8n workflow (server orchestration) | Wappi API (`messages/get` fetch) | The autonomous reply path lives entirely in the bot template; the combine needs a server-side fetch of recent messages. Client never sees this. |
| Is-latest / abort decision | n8n Code node | — | Pure per-execution decision over the fetched window + the triggering webhook id. |
| Suggestions coalesce (debounce) | Unity client (`SuggestionsController`) | — | The «Вместе» request is issued from the client; the LLM already sees all fragments in the payload, so combine is free — only firing cadence changes. Zero server change for BATCH-03. |
| Recent-message fetch | Wappi/tapi API (`messages/get`) | n8n HTTP node | Reuses the existing `WappiAuthToken` credential already bound in both templates. No new secret, no new endpoint. |
| Window tuning knobs | n8n Wait `amount` (server) + client gate constant | — | Two independent tunables (~8s server / ~2.5s client). |

## Standard Stack

This phase adds NO new libraries. It composes existing n8n core nodes and existing Unity patterns.

### Core (n8n side — all already present in the templates)
| Node type | Version | Purpose | Why standard |
|-----------|---------|---------|--------------|
| `n8n-nodes-base.wait` | 1.1 | The ~8s `Debounce Wait` (in-memory resume) | Template already uses 4 of these (`Pause Before Reading`, `Reading Pause`, `Typing Pause`, `Listening Pause`); `amount` in seconds `[VERIFIED: template inspection]` |
| `n8n-nodes-base.httpRequest` | 4.2 | `Fetch Recent` → `messages/get` | Same shape as existing `Mark Read`/`Typing`/`HTTP Request` send nodes; `genericCredentialType` + `httpHeaderAuth` cred `EuhhqAaV56DpoqAN` (WappiAuthToken) `[VERIFIED: template inspection]` |
| `n8n-nodes-base.code` | (as template) | `Latest+Combine` decision | Template already has `Count Input Words`/`Count Output Words` Code nodes using `$('Webhook').first().json.body...` `[VERIFIED: template inspection]` |
| `n8n-nodes-base.if` | 2.2 | `Is Latest?` gate | Same node type/version as the existing `If` (group) and `Suppressed?` gate `[VERIFIED: template inspection]` |

### Core (Unity client side — all existing patterns)
| Element | Purpose | Why standard |
|---------|---------|--------------|
| Pure static/`class` gate with injectable clock | `IncomingDebounceGate` (the ~2.5s debounce logic) | Direct precedent: `OpenChatLivePollGate`, `DashboardRefreshGate`, `WhatsAppSyncGate`, `TabRefreshGate` — all pure, UnityEngine-free, EditMode-tested `[VERIFIED: codebase grep]` |
| DOTween / coroutine | Thin MonoBehaviour driver for the gate | `SuggestionsController` already uses DOTween + coroutine idioms; the gate is the tested unit, the driver is glue `[VERIFIED: SuggestionsController.cs]` |
| NUnit EditMode test | `IncomingDebounceGateTests` | 40+ existing pure-seam tests in `Assets/Tests/Editor/Chat/` (e.g. `OpenChatLivePollGateTests`) `[VERIFIED: codebase]` |

### Supporting (deploy / verify tooling — existing patterns to extend)
| Tool | Purpose | When to use |
|------|---------|-------------|
| `apply-*.py` idempotent migration | Edit both template JSONs in-place by node name | Author the debounce splice; re-runnable, preserves `indent=2, ensure_ascii=False` `[VERIFIED: apply-rag-fixes.py]` |
| `verify-telegram-parity.py`-style script | Structural verify the splice landed in both templates | Optional CI-style guard before owner deploy `[CITED: Tools/n8n/verify-telegram-parity.py]` |
| n8n execution `runData` introspection | Prove abort-vs-combine per fragment | Owner-run live gate (preferred over JSON grep per CONTEXT discretion) `[CITED: 09-04-PLAN.md]` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| New `Debounce Wait` node | Repurpose an existing humanizer pause | REJECTED (locked): humanizer pauses run AFTER the AI Agent generates — too late to debounce. The design's load-bearing finding. |
| Latest+Combine re-emitting `body` | Repoint `Input type` + `Download Audio` to `$('Webhook')` | More node edits, touches nodes the design wanted unchanged, and must be done identically in both templates. Re-emitting `body` in one Code node is the smaller, self-healing change. |
| Client pure gate + coroutine poll | Plain restart-a-coroutine debounce (StopCoroutine/StartCoroutine on each incoming) | Simpler glue but the "3 rapid incomings → 1 fire" assertion is only unit-testable via a pure, clock-injected gate (design explicitly wants this). Keep the decision pure. |

**Installation:** none — no `npm install`, no NuGet, no new n8n community nodes. All node types are n8n core and already in both templates.

## Architecture Patterns

### System Architecture Diagram

**Auto-reply side (per bot template — WhatsApp and Telegram identical structure):**

```
Wappi/tapi webhook (one HTTP call PER incoming fragment)
   │  each fragment → its own n8n execution (concurrent, in-memory)
   ▼
[Webhook] → [If] group filter (from == chatId; drops groups)
              │ TRUE
              ▼
        [Read Reply Mode] (Postgres: suppressed?)   ← Phase 9 gate
              ▼
        [Suppressed?] (If: $json.suppressed == true)
              │ main#0 TRUE  → DEAD-END (semi-auto chat: no reply, no wait)
              │ main#1 FALSE
              ▼
   ┌─────────  NEW Phase-10 debounce stage  ─────────┐
   │ [Debounce Wait] (~8s, in-memory resume)          │
   │       ▼                                          │
   │ [Fetch Recent] (HTTP GET messages/get,           │
   │       limit ~15, WappiAuthToken, NO mark_all)    │
   │       ▼                                          │
   │ [Latest+Combine] (Code):                         │
   │   triggeringId = $('Webhook').first().json       │
   │                    .body.messages[0].id          │
   │   msgs = fetch.messages, sort by time desc       │
   │   newestIncoming = first fromMe==false           │
   │   abort = newestIncoming.id !== triggeringId     │
   │   if !abort && latest is text:                   │
   │     walk newest→oldest, collect incoming text    │
   │     until first fromMe OR media → combinedText   │
   │   else combinedText = null                       │
   │   return {...webhookBody, abort, combinedText}   │  ← RE-EMIT body (critical)
   │       ▼                                          │
   │ [Is Latest?] (If: $json.abort == true)           │
   │       │ TRUE  → DEAD-END (a newer fragment wins)  │
   │       │ FALSE                                     │
   └───────┼──────────────────────────────────────────┘
           ▼
     [Input type] (switch on $json.body.messages[0].type)  ← unchanged; body restored
           │ text → [Text] (value = combinedText ?? webhook body)
           │ ptt  → [Download Audio] → [Transcribe Audio] → [Audio]   ← unchanged
           ▼
     [AI Agent]  ← GENERATES HERE (once, on the combined text)
           ▼
   [Pause Before Reading] → [Mark Read] → … humanizer pauses … → [send]   ← unchanged
```

**Suggestions side (client — one file):**

```
ChatManager.OnLiveMessagesReceived (fires on the 3s open-chat LivePoll + live sync)
   ▼
SuggestionsController.HandleLive(msgs)
   │  (guards unchanged: !_semiAutoOn → return; no incoming → return)
   ▼
IncomingDebounceGate.Poke(now)     ← reset the ~2.5s window on EACH incoming batch
   ▼  (thin coroutine polls gate.ShouldFire(now))
IssueRequest(steerTowardText: null, lastIncomingText: LastIncomingText(msgs))   ← fires ONCE

HandleManualRefresh  → IssueRequest immediately (NOT gated)   ← unchanged
HandleCardTapped     → IssueRequest immediately (NOT gated)   ← unchanged
OnDisable / ResetForNoOpenChat → gate.Cancel()                ← cancel pending window
```

### Component Responsibilities

| Component | File | Responsibility |
|-----------|------|----------------|
| `Debounce Wait` node | both `*-Bot.json` | ~8s in-memory pause; one waiting execution per fragment |
| `Fetch Recent` node | both `*-Bot.json` | single `messages/get` (limit ~15) for this chat; reused for is-latest + combine |
| `Latest+Combine` Code node | both `*-Bot.json` | compute `abort` + `combinedText`; **re-emit webhook `body`** |
| `Is Latest?` If node | both `*-Bot.json` | dead-end aborted fragments; proceed the winner |
| `Text` set node (edit) | both `*-Bot.json` | `combinedText ?? $('Webhook')...body` |
| `IncomingDebounceGate` (new) | `Assets/Scripts/Chat/` | pure ~2.5s debounce decision (injectable clock) |
| `SuggestionsController` (edit) | `Assets/Scripts/Chat/SuggestionsController.cs` | drive the gate from `HandleLive`; cancel in `OnDisable`/`ResetForNoOpenChat` |
| `apply-message-batching.py` (new) | `Tools/n8n/` | idempotent by-node-name splice into both templates |

### Recommended file layout
```
Tools/n8n/
├── workflows/
│   ├── 4wYitz5ek30SVNlT-WhatsApp_Bot.json   # edited: +4 nodes, Text value change, rewire
│   └── 4VN3gsFaC2HUYmcc-Telegram_Bot.json   # edited: same, channel-agnostic Code
├── apply-message-batching.py                 # NEW: idempotent splice migration
└── README.md                                 # update template descriptions

Assets/Scripts/Chat/
├── IncomingDebounceGate.cs                    # NEW: pure gate (injectable clock)
└── SuggestionsController.cs                   # edited: HandleLive drives the gate

Assets/Tests/Editor/Chat/
└── IncomingDebounceGateTests.cs               # NEW: 3-rapid→1-fire; manual/card immediate
```

### Pattern 1: Splice on the FALSE branch, re-emit body
**What:** Insert the 4 debounce nodes on `Suppressed?` main#1 (FALSE) → … → `Input type`. Delete the existing `Suppressed? [main#1] → Input type` edge and rewire through the new chain.
**When to use:** Both templates; identical wiring.
**Verified splice coordinates (both templates):**
- Current edge to remove: `Suppressed?` `main[1][0]` → `Input type`
- New edges: `Suppressed?` main#1 → `Debounce Wait` → `Fetch Recent` → `Latest+Combine` → `Is Latest?`; `Is Latest?` main#1 (FALSE) → `Input type`; `Is Latest?` main#0 (TRUE) → (unconnected, dead-end).
- `[VERIFIED: template inspection]` — n8n `If` outputs: index 0 = TRUE, index 1 = FALSE (the `Suppressed?` gate already dead-ends TRUE and proceeds FALSE on main#1).

### Pattern 2: Client debounce via a pure injectable-clock gate
**What:** A pure `IncomingDebounceGate` holding a deadline + armed flag; `Poke(now)` re-arms to `now + Window`, `ShouldFire(now)` returns true once when `armed && now >= deadline` then disarms, `Cancel()` disarms.
**When to use:** Drive it from `HandleLive`; poll `ShouldFire` from a thin coroutine; `Cancel()` in `OnDisable`/`ResetForNoOpenChat`.
**Why pure:** the "3 rapid incomings → exactly 1 fire after the window" assertion is unit-testable with synthetic time, exactly like `OpenChatLivePollGateTests`.

### Anti-Patterns to Avoid
- **Repurposing a humanizer Wait as the debounce** — they run post-generation; each fragment already generated. (Locked.)
- **Reading `$json.body` in the new Code node** — after the HTTP fetch, `$json` is the fetch response. Use `$('Webhook').first().json.body...`.
- **Using `$('Webhook').item` inside the Code node** — paired-item linkage can break across Wait+HTTP → "Can't get data for expression under 'item'". The template's own Code nodes use `.first()`. Match that.
- **Sending `mark_all=true` on the debounce fetch** — would mark the chat read before the bot decides to reply and before the deliberate humanizer `Mark Read` step. Fetch with `limit` only.
- **Trusting `messages/get` array order** — sort by `time` (unix sec, present on both channels) to find "newest" deterministically.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Server-side "wait for user to finish typing" | A custom polling loop / setTimeout in a Code node / external queue | n8n `Wait` node (<65s = in-memory) + concurrent executions | Native, no DB offload, one waiter per fragment; n8n handles concurrency `[CITED: n8n-docs wait.md]` |
| Fetch recent messages | A new UnityWebRequest or bespoke HTTP client | The existing `httpRequest` node + `WappiAuthToken` cred already in the template | Zero new secret, zero new endpoint, same auth pattern as `Mark Read` |
| Client debounce timer | Ad-hoc `float` timers scattered in `Update`, or `Invoke`/`CancelInvoke` string calls | A pure `IncomingDebounceGate` + one coroutine | Matches 4 existing pure gates; the ONLY testable-without-real-time shape |
| Stale-suggestion suppression | New guard logic | The existing `_requestSeq` monotonic guard + `SuggestionSequenceGuard` | Already discards superseded renders; combine is free (payload ships last ≤12) |
| Deploying the edited templates | Hand-editing JSON in the n8n UI | An idempotent `apply-*.py` migration + owner re-import by literal id | UI download strips the top-level `id`; literal-id clone source must be preserved `[CITED: Tools/n8n/README.md]` |

**Key insight:** Every moving part of this phase already exists in the codebase in a nearly-identical form. The risk is not "can we build it" — it's data-flow correctness (body passthrough, id equality, message order, mark_all) and the both-template + existing-clone propagation story.

## Runtime State Inventory

> This is a workflow/config edit, not a rename. But the "existing deployed state" question is load-bearing here, so it is answered explicitly.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None. The debounce introduces no new persisted state (Wait <65s stays in memory; no new DB table). `[VERIFIED: n8n-docs — sub-65s waits are not offloaded]` | None |
| Live service config | **Both bot templates** (`4wYitz5ek30SVNlT`, `4VN3gsFaC2HUYmcc`) live in dev n8n's SQLite and must be re-imported after the JSON edit. **Existing per-bot clones** (created by the Create orchestrators) are FROZEN copies of the pre-debounce template — they do NOT auto-update. | Owner re-imports both templates by literal id; **recreate any existing dev bot clone** so it carries the debounce (same as Phase-9 Pitfall 6). `[CITED: 09-04-PLAN.md, README.md]` |
| OS-registered state | None. | None |
| Secrets/env vars | None new. `Fetch Recent` reuses cred `EuhhqAaV56DpoqAN` (WappiAuthToken) already bound in both templates. `secrets.json` (n8nAPIKey/DB) remains DENY-RULED for Claude → all live deploy is owner-run. | None (owner runs deploy) |
| Build artifacts | New Unity `.cs` files (`IncomingDebounceGate.cs`, `IncomingDebounceGateTests.cs`) need Unity to import — brand-new `.cs` can be silently excluded from compile until an Assets/Refresh. `[CITED: memory project_unity_new_file_import]` | Refresh + verify `.meta` before running the EditMode suite |

**Prod:** bagkz is DORMANT — the debounce folds into the future one-shot bulk copy, exactly like the Phase-9 gate. Do NOT target prod. `[CITED: STATE.md, memory n8n_dev_setup]`

## Common Pitfalls

### Pitfall 1: `$json.body` evaporates after the fetch (THE big one)
**What goes wrong:** `Input type` reads `$json.body.messages[0].type` and `Download Audio` reads `$json.body.messages[0].file_link` — bare `$json.body`. Insert an HTTP fetch (replaces the item with the messages/get response) and a Code node between `Suppressed?` and `Input type`, and `$json.body` is `undefined` → `Input type` misroutes to the `Ask to Send Text` fallback (via `fallbackOutput: "extra"`) → every message answered wrong, or the media path breaks.
**Why it happens:** n8n nodes pass forward their OWN output; HTTP and Code nodes replace the item. Only `$('NodeName')` reaches back to an earlier node.
**How to avoid:** The `Latest+Combine` Code node must re-emit the webhook body: `return [{ json: { ...$('Webhook').first().json, abort, combinedText } }]`. Then `$json.body` is restored for the unchanged `Input type`/`Download Audio`/`Text` nodes.
**Warning signs:** In runData, `Input type` takes the "extra"/`Ask to Send Text` output on a plain text message; or a `ptt` message errors on a missing `file_link`.
**Bonus:** This also side-steps an unverified question — whether the Phase-9 Postgres `Read Reply Mode` node currently passes `body` through to `Input type` at all (Phase-9 live runData verify, 09-04, has NOT run). Re-emitting body makes the whole gated segment robust regardless. `[VERIFIED: both templates read $json.body downstream of the Postgres gate]`

### Pitfall 2: webhook `messages[0].id` must equal the `messages/get` id for the same message
**What goes wrong:** The is-latest check compares the trigger's `messages[0].id` against the newest incoming id in the fetch. If the two id representations differ (webhook id vs sync id for the same message), then EITHER every fragment aborts (no reply at all) OR none abort (duplicate replies).
**Why it happens:** WhatsApp webhook ids and sync ids have historically matched (Mark Read posts the webhook id to Wappi and it works; `messages/id/get` uses the same id). Telegram sync ids are bare numeric strings (`"2910"`); the webhook id must be the same form. This has not been proven for the debounce path.
**How to avoid:** Make id equality an explicit, first-class check in the owner-run e2e (BATCH-02). String-compare; do not normalize.
**Warning signs:** Two fragments → zero replies (all abort) or two replies (no dedup). `[CITED: tapi-shapes.md §2, §5]`

### Pitfall 3: message order from `messages/get` is not guaranteed
**What goes wrong:** The Code node assumes array[0] is newest (or oldest) and picks the wrong "latest," breaking both the is-latest decision and the combine walk.
**Why it happens:** The app calls `messages/get?...&limit=N&offset=0` with NO `order` param `[VERIFIED: ChatManager.cs:585]`; the default order is Wappi's choice and is not documented per-channel.
**How to avoid:** Sort the fetched messages by `time` (unix seconds — present on both channels, `RawMessage.time`) descending inside the Code node; derive newest and walk from there. Don't trust position.
**Warning signs:** Combine grabs the wrong fragments; is-latest flips intermittently. `[VERIFIED: RawMessage.time; tapi-shapes.md line 77]`

### Pitfall 4: text-type divergence WhatsApp `"chat"` vs Telegram `"text"`
**What goes wrong:** The combine's "is this a text message?" check hard-codes one channel's type string and drops the other channel's fragments (treats them as media → combinedText=null → no combine).
**Why it happens:** WhatsApp text `type == "chat"`; Telegram text `type == "text"`. (This is exactly the Phase-4 `Input type` fix, which matches BOTH via an `or`.)
**How to avoid:** Make the Code node channel-agnostic: treat `type === "chat" || type === "text"` as text (media = anything else). One Code body works in both templates → simpler migration. `[VERIFIED: Telegram Input type uses or(chat,text); tapi-shapes.md §77, line 110]`

### Pitfall 5: don't wake the humanizer / don't double-mark-read
**What goes wrong:** Adding `mark_all=true` to the fetch (copying `Mark Read`) marks the chat read during the wait — before the bot decides to reply, defeating the deliberate `Mark Read` humanizer node.
**How to avoid:** `Fetch Recent` uses `profile_id`, `chat_id`, and `limit` only — NO `mark_all`. `[VERIFIED: Mark Read uses mark_all=true — Fetch must not]`

### Pitfall 6: both templates + existing clones (propagation)
**What goes wrong:** The edit lands in one template, or in the committed JSON but not the dev instance, or in the templates but not the already-created clones → inconsistent behavior; e2e passes on a fresh bot but a real bot still double-replies.
**How to avoid:** Edit BOTH templates identically (idempotent script), owner re-imports BOTH by literal id, and RECREATE existing dev clones (frozen). Verify a freshly-created bot inherits the debounce via the Create orchestrators' verbatim clone. `[CITED: 09-04-PLAN.md Task 3, README.md]`

### Pitfall 7: client debounce must not touch manual refresh / card-pick, and must cancel on close
**What goes wrong:** Debouncing `IssueRequest` globally delays the owner's explicit refresh/card-pick (feels broken), or a pending timer fires after the chat closed / bot switched (renders stale suggestions into the wrong chat).
**How to avoid:** Only `HandleLive` pokes the gate. `HandleManualRefresh` and `HandleCardTapped` call `IssueRequest` directly (unchanged). `OnDisable` and `ResetForNoOpenChat` call `gate.Cancel()`. The existing `_requestSeq++` in those methods already supersedes in-flight requests — keep it. `[VERIFIED: SuggestionsController.cs]`

## Code Examples

### `Latest+Combine` Code node (channel-agnostic; re-emits body)
```javascript
// Source: pattern verified against template Code nodes (Count Input Words uses $('Webhook').first())
// n8n Code node — "Run Once for All Items"
const wh = $('Webhook').first().json;            // .first() NOT .item (paired-item safety across Wait+HTTP)
const triggeringId = wh.body.messages[0].id;

const fetched = ($json.messages || []).slice();  // messages/get response envelope { status, messages:[] }
fetched.sort((a, b) => (b.time || 0) - (a.time || 0));   // newest first, deterministic (Pitfall 3)

const isText = (m) => m && (m.type === 'chat' || m.type === 'text');   // WA + TG (Pitfall 4)
const incoming = fetched.filter(m => m && m.fromMe === false);
const newestIncoming = incoming[0];

let abort = false;
let combinedText = null;

if (!newestIncoming || newestIncoming.id !== triggeringId) {
  abort = true;                                  // a newer fragment arrived during my wait — its run wins
} else if (isText(newestIncoming)) {
  const parts = [];
  for (const m of fetched) {                     // newest → oldest
    if (m.fromMe === true) break;                // bounded by the last bot/owner reply
    if (!isText(m)) break;                        // bounded by media
    parts.push(typeof m.body === 'string' ? m.body : '');
  }
  parts.reverse();                                // chronological
  combinedText = parts.join('\n');                // join delimiter is Claude's discretion
}
// else: latest is media → combinedText stays null → process that one message normally

return [{ json: { ...wh, abort, combinedText } }];   // RE-EMIT body (Pitfall 1)
```

### `Fetch Recent` HTTP node (WhatsApp; Telegram swaps base to tapi/sync)
```jsonc
// Source: shape verified against the template's Mark Read / HTTP Request send nodes
{
  "method": "GET",
  "url": "https://wappi.pro/api/sync/messages/get",   // Telegram: https://wappi.pro/tapi/sync/messages/get
  "authentication": "genericCredentialType",
  "genericAuthType": "httpHeaderAuth",
  "sendQuery": true,
  "queryParameters": { "parameters": [
    { "name": "profile_id", "value": "={{ $('Webhook').item.json.body.messages[0].profile_id }}" },
    { "name": "chat_id",    "value": "={{ $('Webhook').item.json.body.messages[0].chatId }}" },
    { "name": "limit",      "value": "15" }
    // NO mark_all (Pitfall 5)
  ] }
  // credentials.httpHeaderAuth = { id: "EuhhqAaV56DpoqAN", name: "WappiAuthToken" }
}
```

### `Text` set node edit (inject combinedText, keep single-message fallback)
```jsonc
// was: "={{ $('Webhook').item.json.body.messages[0].body }}"
"value": "={{ $json.combinedText ?? $('Webhook').item.json.body.messages[0].body }}"
```

### `Debounce Wait` node
```jsonc
// Source: template Wait nodes are n8n-nodes-base.wait typeVersion 1.1, amount in seconds
{ "amount": 8 }   // < 65s → in-memory resume, no webhook-resume URL
```

### `IncomingDebounceGate` (pure, injectable clock — mirrors OpenChatLivePollGate)
```csharp
// Source: pattern mirrors Assets/Scripts/Chat/OpenChatLivePollGate.cs (pure, UnityEngine-free)
/// <summary>Pure debounce decision for coalescing rapid incoming batches into one suggestions
/// request (BATCH-03). Reset on each incoming; fires once when the window settles. Clock is
/// injected (seconds) so "3 rapid pokes -> 1 fire after the window" is EditMode-testable.</summary>
public sealed class IncomingDebounceGate
{
    public const float WindowSeconds = 2.5f;     // ~2.5s per CONTEXT (Claude's discretion to tune)
    private float _deadline;
    private bool _armed;

    public void Poke(float now) { _deadline = now + WindowSeconds; _armed = true; }  // reset on each incoming
    public void Cancel() => _armed = false;      // chat close / bot switch

    /// <summary>True exactly once when the window has settled; disarms so it fires only once.</summary>
    public bool ShouldFire(float now)
    {
        if (!_armed || now < _deadline) return false;
        _armed = false;
        return true;
    }
}
```

### `SuggestionsController.HandleLive` edit (drive the gate)
```csharp
// HandleManualRefresh + HandleCardTapped stay UNCHANGED (immediate). Only HandleLive is debounced.
private void HandleLive(List<MessageViewModel> msgs)
{
    if (!_semiAutoOn) return;
    if (msgs == null || !msgs.Exists(m => m != null && m.isIncoming)) return;
    _pendingIncomingText = LastIncomingText(msgs);        // capture latest for the eventual fire
    _debounce.Poke(Time.time);                            // reset the ~2.5s window
    // a single always-running coroutine polls _debounce.ShouldFire(Time.time) and, when true,
    // calls IssueRequest(steerTowardText: null, lastIncomingText: _pendingIncomingText)
}
// OnDisable() and ResetForNoOpenChat(): add _debounce.Cancel();  (keep the existing _requestSeq++)
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| One reply per incoming fragment (webhook → execution → reply) | Pre-generation debounce → combine → one reply | This phase | Every auto-reply waits ~8s; multi-fragment thoughts get one grounded reply |
| Suggestions: `HandleLive` → `IssueRequest` per fragment | `HandleLive` → debounced single `IssueRequest` | This phase | N per-fragment LLM calls → 1 on the combined message |
| (n8n) long waits offloaded to DB + webhook-resume | Sub-65s waits stay in memory | n8n behavior (stable) | Enables one lightweight waiter per fragment with no resume-URL plumbing `[CITED: n8n-docs wait.md]` |

**Not deprecated / still current:** `n8n-nodes-base.wait` 1.1, `httpRequest` 4.2, `if`/`switch` 2.2/3.2 are the versions in the live templates — reuse them for byte-consistency; do not "upgrade" node versions as a side effect.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The Phase-9 Postgres `Read Reply Mode` node does NOT pass `body` through, so `Input type`/`Download Audio` currently rely on something to re-supply it (unverified — 09-04 live runData not yet run). | Pitfall 1 | Low for THIS phase — re-emitting body in the Code node is correct either way; but if Phase 9's gate is currently broken on the live clone, the owner should catch it during the same runData session. |
| A2 | Wappi/tapi `messages/get` includes the just-arrived triggering fragment within the ~8s window (propagation lag < window). | Pattern 1 | If the fetch lags, the latest fragment may be missing → is-latest never matches for it. Mitigated by the 8s window; confirm at e2e. |
| A3 | The webhook `messages[0].id` and the sync `messages/get` id for the same message are string-equal on BOTH channels. | Pitfall 2 | High if wrong: zero replies (all abort) or duplicate replies (no dedup). Make it an explicit e2e assertion. |
| A4 | `messages/get` returns the trailing burst within `limit=15` in the common case (no bot reply for >15 incoming is rare). | BATCH-02 | Accepted v1 limitation (design: run bounded by fetch limit). Raise limit if long silent bursts appear. |
| A5 | n8n dev runs in `main` mode with effectively unbounded concurrent webhook executions (no queue-mode concurrency cap throttling the concurrent waiters). | Pattern 1 | If a concurrency limit exists, later fragments queue behind earlier waiters and the abort/win timing skews. Default local n8n is main-mode/unbounded; confirm if the dev instance was reconfigured. |

## Open Questions

1. **`messages/get` default sort order per channel**
   - What we know: the app calls it with no `order` param; `time` (unix sec) is present on both channels.
   - What's unclear: whether array[0] is newest or oldest on each base.
   - Recommendation: sort by `time` desc in the Code node (Pitfall 3) — sidesteps the question entirely.

2. **Exact window values (8s / 2.5s)**
   - What we know: CONTEXT locks the ranges and marks exact values as Claude's discretion, to tune at e2e.
   - Recommendation: ship 8.0 / 2.5, expose each as a single named constant, tune during the owner e2e for perceived single-message latency.

3. **Does the Phase-9 gate currently function on live clones?**
   - What we know: 09-01..03 are structural/client; 09-04 (live redeploy + runData) has NOT run per STATE.md.
   - Recommendation: fold a quick "does the pre-debounce gate route correctly" check into the same owner runData session; the body re-emit makes Phase 10 robust regardless.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| dev n8n (localhost:5678) | Live deploy + runData + curl matrix | Owner-run (deny-ruled for Claude) | local `~/.n8n` SQLite | none — blocks the live gates only, not the structural/TDD plans |
| Cloudflare tunnel (`Tools/n8n/rotate-tunnel.py`) | Webhook reachability for e2e | Owner-run | — | none |
| Real authorized WA + TG dev profiles | Both-channel e2e (BATCH-01/02) | Owner-run | — | none — USER-ASSISTED gate |
| n8n `wait`/`httpRequest`/`code`/`if` core nodes | The splice | ✓ (already in both templates) | 1.1 / 4.2 / core / 2.2 | — |
| `WappiAuthToken` cred `EuhhqAaV56DpoqAN` | `Fetch Recent` | ✓ (already bound in both templates) | — | — |
| Unity 6000.3.9f1 + EditMode test bridge | `IncomingDebounceGate` TDD | ✓ | 6000.3.9f1 | headless `Tools/run-tests-headless.sh` if Editor closed |

**Missing dependencies with no fallback:** dev n8n + tunnel + real profiles — all owner-run and gated behind the deny-ruled `secrets.json`. These bound the live plans only; the structural JSON edits and the client TDD proceed without them.

## Security Domain

Low new surface: no new secret, no new endpoint, no new persisted data. `Fetch Recent` reuses the existing authed `WappiAuthToken` credential; the client change is a timer.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No new auth surface; reuses existing cred |
| V3 Session Management | no | — |
| V4 Access Control | no | Suppression/access already enforced by the Phase-9 gate that runs BEFORE the debounce |
| V5 Input Validation | partial | `combinedText` is customer-controlled text — but it already flows to the AI Agent today; concatenation adds no new injection channel. The Suggest Replies workflow already hardens prompt-injection separately (unchanged here). |
| V6 Cryptography | no | — |

### Known Threat Patterns for this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Rapid-fragment flood → many concurrent waiting executions | Denial of Service | Each fragment aborts after one fetch; sub-65s waits stay in memory (no DB growth); bounded by the (dev-only) instance. Accepted for dev; prod dormant. |
| Combined text as prompt-injection vector | Tampering | No new channel — the AI Agent already receives customer text; combine only concatenates the same fragments the LLM would have seen sequentially. |
| Test clone left ACTIVE against real contacts | Elevation / Safety | INACTIVE-clone constraint: clone ACTIVE only during the runData/e2e window, DEACTIVATED in the same step (mirror Phase-9 acceptance). `[CITED: STATE.md, memory bot_activation_policy]` |

## Project Constraints (from CLAUDE.md + memory)

- **Bot workflow clones stay INACTIVE except during an active test window** (real contacts) — activate only for the gate, deactivate in the same step. `[CITED: CLAUDE.md, memory feedback_bot_activation_policy]`
- **Prod bagkz is DORMANT** — dev only; the debounce folds into the future one-shot bulk copy. `[CITED: STATE.md, memory n8n_dev_setup]`
- **`secrets.json` is DENY-RULED for Claude** — all live n8n/DB/tunnel work is owner-run (`autonomous: false` plans). `[CITED: 09-04-PLAN.md]`
- **Template clone by literal id; UI download strips the top-level `id`** — edit the canonical JSON, re-import by literal id, never rename `4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc`. `[CITED: README.md, memory n8n_dev_setup]`
- **New `.cs` files can be silently excluded from compile** until an Assets/Refresh — verify `.meta` appears before running the suite. `[CITED: memory project_unity_new_file_import]`
- **Unity networking rules** (if any client web request were added — here none): coroutines + `UnityWebRequest`, `timeout = 30`, Newtonsoft parse, models in `Assets/Scripts/Chat/`. The client change adds NO network call (BATCH-03 is timer-only). `[CITED: .claude/rules/networking.md]`
- **Test verification:** EditMode via the test bridge (Editor open: `Temp/claude/run-tests.trigger`) or headless (`Tools/run-tests-headless.sh`); baseline ~1170 tests at 09-03. `[CITED: CLAUDE.md, STATE.md]`

## Sources

### Primary (HIGH confidence)
- Direct inspection: `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` — full node list, connections, Wait/If/Switch/Postgres/HTTP/Set/Code node parameters + credentials
- Direct inspection: `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` — confirmed identical reply-path structure; `Input type` matches `chat` OR `text`; tapi/sync bases; Mark Read drops `mark_all`
- Direct inspection: `Assets/Scripts/Chat/SuggestionsController.cs` — `HandleLive`/`HandleManualRefresh`/`HandleCardTapped`/`OnDisable`/`ResetForNoOpenChat`/`IssueRequest` signatures + the `_requestSeq` guard
- Direct inspection: `Assets/Scripts/Chat/OpenChatLivePollGate.cs` + `OpenChatLivePollGateTests.cs`, `Assets/Scripts/Main/Dashboard/DashboardRefreshGate.cs` — the pure injectable-clock gate precedent
- Direct inspection: `Assets/Scripts/Chat/RawMessage.cs` — `id`/`type`/`fromMe`/`time`/`body` field types
- `.planning/research/telegram-parity/tapi-shapes.md` §1/§2/§5 — `messages/get` shape + params, `id`/`body`/`fromMe`/`type`/`time` verdicts, api vs tapi divergences
- Context7 `/n8n-io/n8n-docs` (wait.md): "For wait times less than 65 seconds, the workflow does not offload execution data to the database" — verifies in-memory resume
- `Tools/n8n/apply-rag-fixes.py`, `Tools/n8n/build-suggest-replies.py`, `Tools/n8n/build-set-reply-mode.py`, `Tools/n8n/README.md` — deploy/migration patterns + literal-id clone rules
- `.planning/phases/09-semi-auto-suppression/09-04-PLAN.md` — the owner-run live-gate pattern (DDL apply, deploy, curl matrix, runData verify, `autonomous: false`)

### Secondary (MEDIUM confidence)
- `.planning/STATE.md` accumulated context — Phase-9 splice history (Read Reply Mode + Suppressed? on If.main[0]), test baseline (~1170), INACTIVE-clone + dormant-prod constraints
- `docs/superpowers/specs/2026-07-15-message-batching-debounce-design.md` — the approved design (pre-generation finding, combine boundary rule, testing plan)

### Tertiary (LOW confidence / flagged)
- id-equality (webhook vs sync) on both channels — inferred from Mark Read/messages-id-get precedent, NOT proven for the debounce path (A3; make it an e2e assertion)
- `messages/get` default sort order — undocumented per channel (Open Q1; mitigated by explicit time-sort)

## Metadata

**Confidence breakdown:**
- Standard stack (nodes/patterns): HIGH — every element verified present in the live templates + client
- Architecture (splice coordinates, client gate): HIGH — connections and signatures read directly
- Pitfalls: HIGH for body-passthrough / mark_all / type-divergence (verified); MEDIUM for id-equality / message-order (require e2e confirmation)
- n8n runtime semantics (Wait in-memory, concurrency): HIGH for the <65s claim (official docs); MEDIUM for the unbounded-concurrency assumption (A5, main-mode default)

**Research date:** 2026-07-20
**Valid until:** ~2026-08-20 (stable — n8n core node behavior and the committed templates are slow-moving; re-verify if the templates are re-imported/edited or n8n is upgraded)
