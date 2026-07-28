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

## v1.1 Telegram Parity

**Shipped:** 2026-07-21 | **Phases:** 3-8 | **Plans:** 58 | **Archived:** 2026-07-28

**Delivered:** A Telegram-authed bot works end-to-end exactly like a WhatsApp one — chat client, n8n auto-replies, «Вместе» suggestions and dashboard — on the Wappi tapi API.

**Key accomplishments:**

- Channel-aware chat pipeline: the `ChatChannel` seam, `SetActiveChannel`, a `WappiEndpoints` builder (api/sync ↔ tapi/sync), per-channel caches, and every tapi parser/send divergence — with WhatsApp behavior byte-identical throughout.
- In-screen WhatsApp|Telegram segmented switcher on the chats screen (Telegram bottom tab removed), with muted/connect affordances and per-bot channel persistence.
- The n8n `Telegram_Bot` template ported onto tapi bases (outbound URLs, `type:"text"`, sessionKey, voice duration) plus a RAG re-stamp on late channel auth, proven e2e against a real dev Telegram profile through the tunnel.
- «Вместе» suggestions made channel-aware (client payload v1.1 + channel-branched RAG filter) and «Сводка» extended to Telegram profiles (bot-level chips, channel-aware deep-link).
- Telegram 2FA auth: `detail:"2fa"` now routes to the `auth/2fa` step via `TelegramAuthResponseParser`.
- Device-UAT closeout: **GATE A PASSED at round 7** after a six-round D2-view saga, resolved by displaced-emoji discrimination plus a Reconcile always-adopt seam.

**Deferred at close:** prod bagkz replication PARKED indefinitely per owner (not pending work). D15 (WhatsApp in-app reaction removal) open-deferred — the probe returned `reactionsKey=True`, so absence-based reconcile is possible once the `reactions[]` shape is captured. Two v1.0-era UAT gaps (phases 01/02) and `01-VERIFICATION` carried forward, not closed here.

---

## v1.2 Reply-Trigger Discipline

**Shipped:** 2026-07-27 | **Phases:** 9-10 | **Plans:** 9 | **Archived:** 2026-07-28

**Delivered:** The bot now answers at the right moment and only when it should — a semi-auto chat gets no auto-reply, and a burst of rapid fragments produces ONE combined reply instead of one per fragment.

**Key accomplishments:**

- Server-side «Вместе» suppression (Phase 9): the `reply_mode_flags` table, `/webhook/SetReplyMode` sync, and a **fail-closed** gate spliced into both bot templates — a Postgres error halts the run rather than replying.
- A pre-generation debounce+dedupe stage (Phase 10): `Debounce Wait (8s) → Fetch Recent → Latest+Combine → Is Latest?` spliced onto the suppression gate's FALSE branch in both templates, so earlier fragments abort and only the last one generates — one combined reply, proven on both channels by execution runData.
- Client-side coalesce: `IncomingDebounceGate` (pure, injectable clock) plus burst accumulation in `SuggestionsController`, so rapid incomings trigger ONE «Вместе» suggestions request covering the whole un-replied run.
- Composition proven on device: the Phase-9 gate runs BEFORE the debounce, so a suppressed chat skips the entire reply path — no wait, no reply, chat stays unread, while suggestions still populate.

**Deferred at close:** WR-02 mixed-type bursts (voice+text, text+image) drop the earlier fragment — accepted v1 scope (text-only by design). IN-05 Telegram losing-fragment read receipts — deferred to the prod pass (tapi `mark/read` has no `mark_all`). IN-04 pre-wait suppression read — accepted; the gate-before-debounce ordering is a locked security property.

---

## v1.3 First-Run Onboarding

**Shipped:** 2026-07-23 | **Phases:** 11 | **Plans:** 10 | **Archived:** 2026-07-28

**Delivered:** A first-time owner is guided from install to a working bot — welcome carousel, trust reassurance at auth, a success moment, and a checklist of first steps.

**Key accomplishments:**

- One-time 3-slide welcome carousel (no skip) on first launch, as a standalone Canvas overlay.
- «Это безопасно» trust blocks in both the WhatsApp and Telegram auth flows.
- Standalone «Бот подключён!» success overlay leading straight into price-list upload.
- Derived-state «Первые шаги» checklist on BotsPage.
- All Wappi response parsing routed through bounds-checked seams (`WappiStatusParser` 6 readers / `ExtractDetail`) — no hand-rolled `Substring` scans of a Wappi body.

**Deferred at close:** none recorded. Verification 10/10, security 34/34 (threats_open 0), code review 12/12 findings closed, UAT Round-2 owner-approved after a gap round (11-08..11-10) closed defects D1–D3.

---
