# Project Research Summary

**Project:** Reply Suggestions Panel (semi-auto agent-assist)
**Domain:** AI reply-suggestion panel integrated into a brownfield Unity 6 WhatsApp chat client (CIS / Russian-language small-business market)
**Researched:** 2026-06-23
**Confidence:** HIGH

## Executive Summary

This milestone adds a bottom-sheet Reply Suggestions Panel above the existing composer that presents the owner with 4 candidate replies per incoming customer message. The universal industry pattern (Gmail Smart Reply, Intercom Fin, Zendesk Auto Assist, Help Scout, Front) is draft-then-review: the human approves before anything is sent. Every serious agent-assist product studied enforces this, and none expose a numeric confidence score — the project's own core value of trust + control aligns precisely with this consensus. The recommended implementation generates suggestions server-side in n8n (which already holds per-bot context, LLM credentials, and prompt logic), using grammar-constrained structured output (OpenAI strict `json_schema` or Anthropic native JSON outputs) to guarantee the `[{text, intent_label, confidence}] × 4` contract. Re-clustering (picking one card to steer a fresh set of 4) is implemented as a plain LLM re-prompt with a `steerTowardText` field — one round-trip, no embedding infrastructure.

The hardest integration constraint is transport: the app is currently outbound-only (it fires webhooks at n8n; n8n never calls back). Suggestions require the app to POST and **wait** for the synchronous HTTP response via a new n8n Webhook + Respond-to-Webhook flow. This is a genuine new integration surface that does not exist today. The build order is deliberately sequenced to de-risk this: Phase 1 builds and polishes all UI and interaction logic against a `MockSuggestionsProvider` behind an `ISuggestionsProvider` seam, so the visual work is completely decoupled from the backend. Phase 2 swaps in `N8nSuggestionsProvider` without touching any UI code.

The main risks are concurrency discipline and trust. The concurrency risk mirrors the project's already-solved Wappi crossing bugs: rapid card picks and auto-triggers can produce out-of-order responses that overwrite the UI with stale suggestions — mitigated by the same monotonic-sequence-gate + correlationId + single-in-flight pattern already used in `QuoteResolve.cs`. The trust risk is confidence theater: showing a raw LLM confidence percentage implies precision the model does not have and is empirically shown to harm decision quality. The safe treatment — used by every shipped product — is ranking order plus an optional "Recommended" badge on the top card only, with no numeric percentage.

---

## Cross-Cutting Decisions and Tensions

**These five items cut across all research files and must be resolved before or during requirements definition.**

### 1. Confidence display: DECISION REQUIRED

**Tension:** PROJECT.md lists "text + intent label + confidence" on each card. FEATURES.md research (backed by peer-reviewed work, arXiv 2402.07632) recommends **no numeric percentage** — use ranking order plus an optional "Recommended" badge on the top card only. PITFALLS.md calls showing a raw LLM confidence % an existential UX bug for a trust-first product.

**Recommendation:** Treat "confidence" in the PROJECT.md decision as **confidence-as-ordering-signal**, not as a displayed number. The safe default is: top card gets a "Recommended" badge; remaining cards are ordered by descending confidence; no per-card %, bar, or dot in v1. Coarse tiers (dot: high/medium) can be added in v1.x only after pipeline calibration is measured against real conversations.

**Action required:** Explicitly resolve this in requirements before any card UI is built. Once a number renders in the mock it will be hard to remove.

### 2. n8n synchronous webhook: new integration surface

**Tension:** The app is currently **outbound-only** — it fires POST requests at n8n webhooks that execute and return nothing the app waits on. Suggestions require the app to POST and **block** (inside a coroutine + `yield return SendWebRequest()`) until n8n returns the full suggestion payload. This means adding a new n8n Webhook node configured with "Respond: Using Respond to Webhook node" and a Respond to Webhook node at the end of the flow. This is not a retrofit of the existing Create/Edit workflow webhooks — it is a new endpoint with different semantics.

**Action required:** The n8n side of Phase 2 must be built as a net-new synchronous webhook flow. Do not attempt to add a response body to the existing fire-and-forget webhooks. Confirm n8n cloud's Respond to Webhook timeout is sufficient for the chosen LLM round-trip time before committing to the flow design.

### 3. ISuggestionsProvider seam: what enables UI-first build order

**The seam is the entire reason Phase 1 can complete without any n8n work.** `SuggestionsController` depends only on `ISuggestionsProvider`. Phase 1 injects `MockSuggestionsProvider` (canned data + simulated latency + adversarial malformed payloads); Phase 2 injects `N8nSuggestionsProvider`. The contract (`SuggestionRequest` / `SuggestionResult` / `SuggestionSet` / `correlationId`) is defined in Phase 1 and shared by both implementations. If anything above the seam (panel, controller, re-cluster loop, composer hand-off, toggle gate) references `UnityWebRequest` or n8n URLs, the seam has been breached and Phase 2 will require UI edits — treat that as a defect.

### 4. Re-cluster stale-response concurrency: reuse the existing guard pattern

The re-cluster loop (tap card → new suggestion request while previous may still be in flight) is the same concurrency class as the confirmed Wappi `/messages/get` crossing bug. The fix must reuse the same pattern already in the codebase: `_chatFetchesInFlight` gate + `CrossChatResponseGuard`-style `correlationId` discard. The mock provider must simulate random latency so out-of-order responses are exercised in Phase 1 — a synchronous always-success mock hides this failure class entirely.

### 5. ChatManager.currentChatId is private: accessor required

`ChatManager.currentChatId` is currently `private`. `SuggestionsController` needs to read the open chat id to gate auto-populate events and guard stale responses. This requires adding `public string CurrentChatId => currentChatId;` (one line) to `ChatManager`. Similarly, `WaitForChatFetchesToDrain()` is private; the live provider (`N8nSuggestionsProvider`) must call it before its POST to serialize suggestion fetches against in-flight chat-open/sync calls. Exposing these two accessors is low-risk and should be done in Phase 1 to avoid a Phase-2 edit to a high-risk file.

---

## Key Findings

### Recommended Stack

The new feature adds no new Unity packages — it is built entirely from the existing stack. The compute-side additions are: a new n8n Webhook + Respond-to-Webhook synchronous workflow, an n8n AI/LLM node with "Require Specific Output Format" + Structured Output Parser (JSON Schema, no `$ref`), and a grammar-constrained provider model (OpenAI strict `json_schema` or Anthropic native JSON outputs). On the Unity side, new C# models in `Assets/Scripts/Suggestions/`, `Newtonsoft.Json` (already present) for parsing, and `DOTween` (already present) for card swap animation.

**Core technologies:**
- **n8n Structured Output Parser (Webhook + LLM node):** generates and re-clusters suggestions server-side where bot context and LLM credentials already live — HIGH
- **OpenAI strict `json_schema` or Anthropic native JSON outputs:** grammar-constrained decoding guarantees the `{text, intent_label, confidence}[]` shape; `intent_label` enum enforced at decode time — HIGH (mechanism confirmed; specific model IDs must be verified in the live console at build time)
- **New n8n synchronous Webhook + Respond to Webhook node:** the one genuinely new integration surface; app POSTs and waits on the HTTP response — HIGH on approach; timeout budget must be measured
- **`ISuggestionsProvider` seam (new C# interface):** the mock-to-live swap point; the entire UI-first build order depends on it existing from day one — HIGH
- **Newtonsoft.Json:** existing; required for arrays-of-objects with optional fields (`JsonUtility` cannot handle this shape reliably) — HIGH
- **Re-cluster via LLM re-prompt (`steerTowardText`):** same webhook, one round-trip, no embedding infra, controllable diversity — MEDIUM (preferred over embedding cluster for v1)

### Expected Features

**Must have (table stakes) — Phase 1:**
- Tap card loads text into composer, fully editable, never auto-sends — the trust contract
- 4 cards: reply text as hero, intent label as small pill — enables fast scanning
- Per-chat semi-auto toggle, default OFF, persisted in PlayerPrefs
- Loading skeleton (4 shimmer cards), empty state, error state with retry — panel must never read as silently broken
- Card text truncation (~2–3 lines + ellipsis; full text loads into composer on tap)
- Manual refresh / regenerate (one-tap, always visible)
- Auto-populate on incoming customer message without stomping an in-progress composer draft
- Collapse / dismiss the panel
- Confidence as **ranking order + optional "Recommended" badge on top card only** — no numeric percentage (see Decision 1)

**Should have (competitive differentiators) — Phase 2:**
- Re-cluster on pick: choosing one card fires a fresh steered set of 4 toward the pick (the headline differentiator; no studied product does this)
- Live n8n wiring via `N8nSuggestionsProvider`
- Tone/length quick-adjust chips before regenerate (v1.x, after core loop validated)

**Defer (v2+):**
- Coarse confidence tiers (dot indicator) — only after pipeline calibration measured
- Per-card thumbs feedback — only if a model training loop is built
- Telegram chat surface — explicitly out of scope this milestone
- Numeric confidence percentage — avoid unless calibration is verified

### Architecture Approach

The feature is structured around three layers: a `SuggestionsController` (MonoBehaviour singleton), a `SuggestionsPanel` view (pure view modeled on `QuickReplyPanel`), and `ISuggestionsProvider` (the seam). Everything above the seam is built in Phase 1; only `N8nSuggestionsProvider` and the n8n workflow are Phase 2 additions. The feature plugs into the existing event model (`ChatManager.OnLiveMessagesReceived`, `OnChatSelected`) and feeds the existing composer via a new `MessagesBottomPanel.LoadDraft(string)` method. No new Wappi calls are added.

**Major components:**
1. **`SuggestionsController`** — subscribes to `ChatManager` events, manages `_activeCorrelationId` and in-flight gate, routes picks to composer and re-cluster requests, reads `SemiAutoStore` toggle
2. **`SuggestionsPanel` + `SuggestionCardView`** — 4-card bottom sheet view, refresh button, loading/empty/error states; modeled on `QuickReplyPanel`
3. **`ISuggestionsProvider` / `MockSuggestionsProvider` / `N8nSuggestionsProvider`** — the provider seam; mock in Phase 1, live n8n PULL in Phase 2
4. **`SemiAutoStore`** — static PlayerPrefs helper; key shape `{botId}_semiAuto_{chatId}`
5. **`SuggestionSet` / `SuggestionItem` / `SuggestionRequest` / `SuggestionResult`** — serializable C# data models
6. **`MessagesBottomPanel.LoadDraft(string)`** — one new method on the existing composer
7. **`ChatManager.CurrentChatId` + `WaitForChatFetchesToDrain()`** — two new public accessors (see Decision 5)
8. **n8n `/webhook/SuggestReplies` workflow** — new synchronous webhook; AI node with structured output; Respond to Webhook node; handles both initial generation and re-cluster via `steerTowardText`

### Critical Pitfalls

1. **Re-cluster concurrency crossing (Critical, Phase 1)** — Rapid picks and burst incoming messages produce out-of-order LLM responses; stale responses overwrite the current card set. Prevent with monotonic `_suggestionRequestSeq` gate + `correlationId` echo + single-in-flight discipline mirroring `CrossChatResponseGuard`. Mock must use randomized latency to exercise this in Phase 1.

2. **Stale-response after chat switch (Critical, Phase 1)** — Slow in-flight suggestion for chat A renders in chat B after switch. Capture `(chatId, botId, correlationId)` at request time; discard on mismatch; cancel on `OnChatSelected` and bot switch.

3. **Confidence theater (Critical, Phase 1 decision)** — Raw LLM confidence % implies calibration the model does not have; empirically harms decision quality and erodes trust. Lock the confidence display decision (ranking + badge only) before building card UI.

4. **Mock-to-live contract divergence (High, Phase 1 prevention)** — Phase-1 mock uses clean hand-authored data; Phase-2 live n8n output differs in shape, latency, and error modes. Prevent by defining a versioned shared schema in Phase 1 and making the mock adversarial (random latency, occasional malformed payloads, wrong card counts). Phase 2 must not require any UI code changes.

5. **Unity UI: RectMask2D culling, async resize, keyboard layout (High, Phase 1)** — Three project-documented recurring failures: `m_Maskable: 0` children overflow the viewport; partial layout rebuild leaves stale `LayoutElement.preferredWidth`; `KeyboardAwarePanel` repositions every frame. Mitigate: grep new prefabs for `m_Maskable: 0` == 0; run full rebuild on content change; test at 1080×2400 with keyboard open; model on `QuickReplyPanel`.

6. **Malformed / out-of-contract LLM output (High, Phase 2)** — LLM can return invalid JSON, wrong card count, out-of-set labels. In n8n: use strict `json_schema`. On client: validate fully, discard the whole set on any failure, show empty/retry state. Never render a partially-valid set. Note: n8n Structured Output Parser does not support `$ref` — inline the schema.

7. **Accidental auto-send (Critical, all phases)** — Card tap has exactly one action: `MessagesBottomPanel.LoadDraft(picked.text)`. The existing Send button handles sending. Add an interaction test asserting picking a card never calls `SendTextMessage`.

---

## Implications for Roadmap

### Phase 1: Polished UI on mock data

**Rationale:** The provider seam makes this phase fully independent of n8n. All UI, interaction, and re-cluster loop work completes before the backend exists. This is the explicit project build order from PROJECT.md. The hardest integration problem (synchronous n8n webhook) does not block any of this work.

**Delivers:** A fully functional, visually polished Reply Suggestions Panel running against `MockSuggestionsProvider` — demoable end-to-end with no backend.

**Addresses (FEATURES.md P1):** Per-chat toggle, 4 cards, loading/empty/error states, card truncation, manual refresh, auto-populate on incoming, no-draft-stomp, collapse/dismiss, confidence as ranking + "Recommended" badge.

**Internal ordering (dependencies strict top-to-bottom):**
1. Data models + `ISuggestionsProvider` seam + `SuggestionTrigger` enum — unblocks everything
2. `MockSuggestionsProvider` with adversarial behavior (random latency, occasional malformed payloads, `correlationId` echo)
3. `ChatManager` accessors: `public string CurrentChatId` + `public IEnumerator WaitForChatFetchesToDrain()` — low-risk, avoid Phase-2 edits to a high-risk file
4. `SuggestionsPanel` + `SuggestionCardView` — 4 cards, refresh button, loading/empty/error; `unity-ui-builder` quality bar; modeled on `QuickReplyPanel`
5. `SemiAutoStore` + per-chat toggle UI in chat top bar
6. `SuggestionsController` event wiring, `_activeCorrelationId` gate, debounce, discard guards
7. `MessagesBottomPanel.LoadDraft(string)` + composer hand-off + re-cluster loop (pick → draft + steered request)

**Avoids (PITFALLS.md):** Pitfalls 1, 2, 3, 5, 8, 10, 11 — all are Phase-1 prevention responsibilities.

**Research flag:** Standard patterns — use `unity-ui-builder` skill for card layout + keyboard interaction. Lock the confidence-display decision (cross-cutting Decision 1) before building card UI.

### Phase 2: n8n live wiring

**Rationale:** The seam from Phase 1 makes this phase entirely additive. Only `N8nSuggestionsProvider` and the n8n workflow are new. No Phase-1 UI code is touched. If any UI edits are required, the seam was breached — treat as a defect.

**Delivers:** End-to-end live suggestions from n8n/LLM, re-cluster via `steerTowardText`, production-grade transport with timeout, correlation, and serial-fetch discipline.

**Uses (STACK.md):** New n8n synchronous Webhook + Respond to Webhook flow; AI/LLM node with strict `json_schema` structured output; fast Russian-capable model (GPT-5.x-mini or Claude Haiku class — verify exact IDs in live console); `UnityWebRequest`; `Newtonsoft.Json`.

**Implements (ARCHITECTURE.md):** `N8nSuggestionsProvider` behind the seam; serial guarded PULL mirroring `QuoteResolve.cs` (gate via `WaitForChatFetchesToDrain`, supersede via correlationId, abort on bot switch); seam flip.

**Avoids (PITFALLS.md):** Pitfalls 4, 6, 7, 9 — Phase-2 prevention (structured output constraints, prompt injection via role separation, n8n timeout budget, correlation round-trip, acceptance instrumentation).

**Research flag:** Needs attention. Measure n8n round-trip latency early — determines whether auto-populate-on-every-message needs debouncing. Verify the AI Agent node `output` key double-nesting behavior (known bug: the node can wrap the payload in an extra `output` key; test the real payload and unwrap defensively). Confirm the Respond to Webhook timeout ceiling against actual LLM latency.

### Phase Ordering Rationale

- Phase 1 first because the seam enables it to complete with zero n8n dependency; the majority of user-visible work is UI polish and should not be gated on backend availability.
- `ChatManager` accessor additions go in Phase 1 (step 3 above) to avoid modifying a high-risk file under Phase-2 time pressure.
- The adversarial mock is Phase-1 work because the concurrency guards (seq gate, discard logic, stale-response handling) must be wired and tested before Phase 2 — adding them after the seam is live carries high regression risk.
- Phase 2 is cleanly isolated: one new C# class + one n8n workflow + one flag flip. Any Phase-2 item requiring Phase-1 file edits is scope creep.

### Research Flags

**Needs deeper research during planning:**
- **Phase 2 — n8n latency budget:** measure real cloud round-trip time with the selected model before designing the timeout/debounce/skeleton strategy; community reports range 1–8s; the exact number determines whether 30s `UnityWebRequest` timeout is enough and whether auto-populate needs debouncing.
- **Phase 2 — exact model IDs:** secondary sources hallucinated model names; verify the current fast-tier Russian-capable model in the live provider console at build time.
- **Phase 2 — n8n AI Agent `output` key double-nesting:** real-world bug (github.com/n8n-io/n8n/issues/20029); test the actual response shape before parsing.

**Standard patterns (skip research-phase):**
- **Phase 1 — Unity UI:** `QuickReplyPanel` / `MessagesBottomPanel` / `KeyboardAwarePanel` patterns are established in the codebase; follow `unity-ui-builder` skill.
- **Phase 1 — provider seam / coroutine pattern:** standard codebase conventions; `QuoteResolve.cs` is the canonical serial-drain template.
- **Phase 1 — PlayerPrefs persistence:** established `SemiAutoStore` key shape documented in ARCHITECTURE.md; no research needed.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | No new Unity packages; n8n structured output mechanism confirmed by official docs; specific model IDs need live-console verification |
| Features | HIGH | Multiple converging sources (Gmail, Intercom, Zendesk, Help Scout, Front) + peer-reviewed confidence-display research (arXiv 2402.07632) |
| Architecture | HIGH | Grounded in direct source reads of ChatManager.cs, QuoteResolve.cs, MessagesBottomPanel.cs, QuickReplyPanel.cs; all seam and guard patterns traceable to existing code |
| Pitfalls | HIGH | Project-specific failure modes drawn from confirmed bugs in auto-memory; n8n/LLM reliability from 2026 sources |

**Overall confidence:** HIGH

### Gaps to Address

- **n8n round-trip latency on cloud with chosen model** — measure in Phase 2 spike before committing the debounce / skeleton / timeout design.
- **Confidence spread behavior of the chosen model** — empirically check whether it bunches all four values near 1.0; if so, the ranking-order treatment is doubly justified.
- **n8n Respond to Webhook timeout ceiling** — confirm the workflow can complete within it for realistic LLM latency; if not, a more complex ack + polling design is needed (significant architecture impact).
- **Re-cluster "away from rejected" signal** — decide whether to pass the 3 non-picked texts as negative steering; the request schema has a slot for it but it is not required; decide before Phase-2 prompt design.
- **PlayerPrefs orphan cleanup on bot delete** — `{botId}_semiAuto_*` keys are unbounded in count and cannot be enumerated reliably on all platforms; acceptable for this milestone, flag for a future cleanup pass.

---

## Sources

### Primary (HIGH confidence)

- `Assets/Scripts/Main/ChatManager.cs` + `ChatManager.QuoteResolve.cs` — `OnLiveMessagesReceived`, `currentChatId`, `_chatFetchesInFlight`, `WaitForChatFetchesToDrain`, serial-drain template
- `Assets/Scripts/Chat/MessagesBottomPanel.cs`, `ExpandableInput.cs`, `QuickReplyPanel.cs` — composer and closest UI analog
- `Assets/Scripts/Main/Bot.cs` — PlayerPrefs per-entity key convention
- https://developers.openai.com/api/docs/guides/structured-outputs — strict json_schema, enum support, schema caching
- https://platform.claude.com/docs/en/build-with-claude/structured-outputs — Anthropic native JSON outputs, GA status
- https://docs.n8n.io/integrations/builtin/cluster-nodes/sub-nodes/n8n-nodes-langchain.outputparserstructured/ — Structured Output Parser, no $ref support
- https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-base.respondtowebhook/ — Respond to Webhook node
- https://arxiv.org/html/2402.07632v4 — miscalibrated confidence harms decisions; peer-reviewed
- Auto-memory: project_wappi_messages_get_crossing, project_wappi_media_download_crossing, project_bubble_graphics_maskable, project_live_status_bubble_resize, project_static_safe_zones, project_swipeback_strip_raycast

### Secondary (MEDIUM confidence)

- https://support.google.com/mail/answer/9116836 — Gmail Smart Reply patterns
- https://www.intercom.com/blog/announcing-fin-ai-copilot/ — Intercom Fin Copilot
- https://support.zendesk.com/hc/en-us/articles/10140103140122 — Zendesk Auto Assist
- https://docs.helpscout.com/article/1570-ai-drafts — Help Scout AI Drafts
- https://help.front.com/en/articles/2344960 — Front Copilot
- https://arxiv.org/html/2510.20460v1 — self-reported LLM confidence calibration (ECE ~0.166)
- https://github.com/n8n-io/n8n/issues/20029 — AI Agent node double-nests output key
- https://tokenmix.ai/blog/prompt-injection-defense-techniques-2026 — role separation, layered defense
- https://www.aiuxdesign.guide/patterns/confidence-visualization — confidence-display UI patterns
- https://www.shapeof.ai/patterns/regenerate — regenerate UX pattern

### Tertiary (LOW confidence)

- https://arxiv.org/html/2510.03777v2 — GuidedSampling diversity steering (supports re-prompt diversity over embedding collapse)
- https://app.ailog.fr/en/blog/guides/choosing-embedding-models — multilingual embeddings (only relevant if Hybrid re-cluster pursued in v2)

---
*Research completed: 2026-06-23*
*Ready for roadmap: yes*
