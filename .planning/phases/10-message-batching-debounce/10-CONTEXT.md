# Phase 10: Message Batching / Debounce - Context

**Gathered:** 2026-07-15
**Status:** Ready for planning (v1.2 milestone — after Phase 9 suppression)
**Source:** PRD Express Path (docs/superpowers/specs/2026-07-15-message-batching-debounce-design.md — approved design)

<domain>
## Phase Boundary

Make a multi-fragment customer message produce ONE combined reply instead of one-per-fragment, on both the autonomous auto-reply path and the «Вместе» suggestions path. Auto side: a pre-generation debounce + is-latest dedupe + text-combine stage inserted in BOTH bot templates (WhatsApp + Telegram), before `Input type`/AI Agent. Suggestions side: a debounce timer in `SuggestionsController.HandleLive`. No change to the humanizer pauses, the suppression gate (Phase 9), or the activation switch.

</domain>

<decisions>
## Implementation Decisions

### Requirement definitions (BATCH ids referenced by ROADMAP Phase 10)
- **BATCH-01** (auto combine): a pre-generation stage `Debounce Wait (~8s) → Fetch Recent (messages/get) → Latest+Combine (Code) → Is Latest? (If)` inserted after the group `If` (and after the Phase-9 gate), before `Input type`. Only the last fragment's execution proceeds; it feeds the AI Agent the concatenation of the trailing run of consecutive incoming TEXT messages since the last bot/owner reply. Earlier fragments abort (dead-end) — never generate.
- **BATCH-02** (auto correctness): one Wappi fetch serves both the is-latest check (newest incoming id == this execution's `messages[0].id`?) and the combine (gather incoming since last `fromMe`, bounded by fetch limit ~15). Single complete message → one reply after the window. Media-latest → processed alone (combinedText=null, existing behavior). Both channels (channel base already correct post-Phase-4; id/body field names per Phase-3 SHAPES.md).
- **BATCH-03** (suggestions coalesce): debounce timer (~2.5s) in `SuggestionsController.HandleLive` — reset on each incoming, fire `IssueRequest` when it settles; cancel on chat close / bot switch. Manual refresh (INT-03) and card-pick re-cluster (INT-04) are NOT debounced (fire immediately). Combine is free (payload already ships last ≤12 messages); the seq guard already prevents flicker.

### Locked technical decisions (from the approved spec)
- **The AI Agent generates BEFORE the humanizer pauses** — so the debounce MUST be pre-generation (a new Wait), not a repurposed pause. This is the load-bearing finding.
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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design (source of truth)
- `docs/superpowers/specs/2026-07-15-message-batching-debounce-design.md` — approved design: the pre-generation finding, auto stage, suggestions timer, media boundary rule, testing, out-of-scope

### n8n reply-path integration points
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` — reply path: `group If → Input type → Text/Audio → AI Agent → Pause Before Reading → Mark Read → Reading Pause → Typing → Typing Pause → send`; the `Text` set node is where `combinedText` plugs in; `Pause Before Reading`/`Reading Pause`/`Typing Pause` are POST-generation humanizer waits (do NOT repurpose)
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` — tapi'd template (Phase 4); confirm its reply-entry + text-set node names; tapi messages/get shape + id/body fields per SHAPES.md
- `Tools/n8n/build-suggest-replies.py` — REST deployer/exporter pattern (creds by NAME)
- `.planning/phases/03-*/…SHAPES.md` (or `.planning/research/telegram-parity/tapi-shapes.md`) — tapi message id/body/media field verdicts for the Telegram fetch/combine
- WappiAuthToken credential (id `EuhhqAaV56DpoqAN`) already in the bot templates for the messages/get fetch

### Client integration points
- `Assets/Scripts/Chat/SuggestionsController.cs` — `HandleLive` (the incoming-triggered `IssueRequest`, to debounce), `HandleManualRefresh` + `HandleCardTapped` (must stay immediate), `OnDisable`/`ResetForNoOpenChat` (cancel the pending timer)
- `Assets/Scripts/UI/MessageViewModel.cs` / chat-data-flow — `isIncoming`, `text`, message type for the client (combine is free, but the timer reads incoming-ness)

### Phase 9 dependency
- `.planning/phases/09-semi-auto-suppression/09-CONTEXT.md` + `docs/superpowers/specs/2026-07-13-semi-auto-suppression-flag-design.md` — the suppression gate this phase sits AFTER

</canonical_refs>

<specifics>
## Specific Ideas

- Worked example: customer sends «есть колодки» then «на камри 70?» ~1s apart → fragment 1's execution waits 8s, fetches, sees fragment 2 newer → aborts; fragment 2 waits 8s, fetches, is latest → combines «есть колодки\nна камри 70?» → one grounded reply.
- n8n curl matrix: two fragments ~1s apart → ONE reply (runData: first aborts at Is Latest?, second combines); one message → one reply after window; fragment + genuine bot reply between → run bounded at the bot reply.
- Client EditMode: three rapid `HandleLive` incomings → exactly one `IssueRequest` after the window; a manual refresh in the middle fires immediately.

</specifics>

<deferred>
## Deferred Ideas

- Adaptive windows (punctuation-aware "message looks complete → reply now / shorten wait")
- Combining across media types (transcribe voice into the text run; interleaved media+text bursts) — v1 boundary rule handles pure-text; this is the v2 refinement
- Trimming the existing humanizer `Pause Before Reading` now that the debounce already waited (future latency tune)

</deferred>

---

*Phase: 10-message-batching-debounce*
*Context gathered: 2026-07-15 via PRD Express Path*
