# Milestones

## v1.0 Reply Suggestions (Shipped: 2026-07-11)

**Phases completed:** 2 phases, 8 plans, 17 tasks
**Known deferred items at close:** 3 (deferred device-UAT details — see STATE.md Deferred Items); security 14/14 threats closed (`02-SECURITY.md`); code review 0 critical, WR-01..04 fixed

**Key accomplishments:**

- Pure-C# reply-suggestions seam (`ISuggestionsProvider`) with a Russian-language `MockSuggestionsProvider` (ranked replies, simulated latency, steered re-cluster, error/empty/out-of-order paths) and a `SuggestionSequenceGuard` discard predicate — 13/13 EditMode tests green.
- Additive `ChatManager` partial exposing `CurrentChatId` + a public chat-fetch drain hook (DATA-04), plus `SemiAutoStore` persisting per-chat semi-auto state keyed `{botId}_semiAuto_{chatId}` (default OFF, bot/chat isolated) — `SemiAutoStoreTests` 5/5 green.
- The visual layer — `SuggestionCard`, `SuggestionsPanel` (5-state machine + DOTween), `SemiAutoToggle`, and a `Tools/UI/Build Suggestions Panel` builder that constructs the wired panel (above the composer) + top-bar toggle with RoundedCorners and RU copy. Compiles clean; built and verified in-Editor.
- `SuggestionsController` — the MonoBehaviour mediator that makes Phase 1 live on mock data: toggle → persist + show/hide, card tap → composer hand-off + steered re-cluster (never auto-sends), incoming → auto-populate cards (never the composer), manual refresh, and a monotonic-seq + captured-chat guard discarding stale/superseded results. Wired via a `[MenuItem]`; verified end-to-end in Play Mode.
- Shared always-active dev n8n workflow (`/webhook/SuggestReplies`) that turns the frozen v1 request into 4 ranked distinct enum-labeled reply moves via one gpt-4o-mini strict-json_schema call, tenant-scoped RAG pre-retrieval, and Code-node validation with a one-shot retry — echoing requestSeq, never leaking raw model text.
- `N8nSuggestionsProvider` consumes the live `/webhook/SuggestReplies` flow behind the `ISuggestionsProvider` seam via a single Awake-line swap — pure static `BuildPayloadJson`/`MapResponse` (v1 contract), a `ChatManager.TryGetRecentMessages` accessor, and 26 green EditMode tests — with zero other Phase-1 edits.
- Adversarial e2e matrix (11 curl cases) proves the Suggest Replies dev workflow holds the frozen v1 contract under injection, grounding, missing-data, steer, trivial, sentinel, and malformed-input load — with ZERO prompt or validation fixes required; the Plan-01 workflow was already hardened, so the committed canonical JSON stands byte-identical as the final.
- The live suggestions path is proven client-side end-to-end — the seam invariant held at the git level (only `SuggestionsController.cs` L31 swapped, exactly 1 ins/1 del; no other Phase-1 file touched), the dev workflow returns 4 distinct grounded moves over both localhost and the Cloudflare tunnel the app points at, and the owner confirmed live suggestions render on device (smoke pass) — with the detailed 5-scenario device UAT deferred by the owner and persisted in 02-HUMAN-UAT.md.

---
