# Phase 2: n8n Live Wiring - Context

**Gathered:** 2026-07-10
**Status:** Ready for planning
**Source:** PRD Express Path (docs/superpowers/specs/2026-07-10-live-reply-suggestions-design.md)

<domain>
## Phase Boundary

Swap the Phase-1 `MockSuggestionsProvider` for a live path: a new shared n8n workflow (`Suggest Replies`, webhook `/webhook/SuggestReplies`) that generates 4 ranked reply suggestions with an AI node, and a Unity `N8nSuggestionsProvider` behind the existing `ISuggestionsProvider` seam. ZERO changes to Phase-1 UI code (controller/panel/cards/toggle) — any required UI edit is a seam-breach defect. Client-side additions are limited to: the new provider class + its DTOs, a ChatManager accessor partial, and the one-line provider swap in `SuggestionsController.Awake`.

</domain>

<decisions>
## Implementation Decisions

### Suggestion content (product-decided, locked)
- Cards are 4 DISTINCT MOVES from a closed 6-label taxonomy: «Ответ», «Уточнить», «Вариант», «К заказу», «Отложить», «Отказ» — no duplicate labels in a set, ranked best-first, card 1 = the Recommended lead
- Always exactly 4 cards on success (validated server-side); trivial messages («спасибо») still get the 4 best-fitting distinct moves, each natural enough to actually send
- Grounding rule: prices/stock/terms ONLY from retrieved RAG chunks or the request `catalog`; a missing fact converts that card to «Уточнить»/«Отложить» — never an invented number
- Style: mirror the customer's language (RU/KZ) and ты/вы register; 1–3 sentences, ≤220 chars target (300 hard clamp); max 1 emoji; human-owner texting tone
- Per-vertical behavior hints keyed by `businessTypeId` — compact 1–2 line map inside the workflow (NOT the long vertical main prompts); unknown/empty id → no hint
- Steering (`steerTowardText` set): 4 refinements of the picked direction (точнее/теплее/короче + логичный следующий шаг), labels still from the enum, still distinct

### Wire contract v1 (locked)
- `POST {n8nBase}/webhook/SuggestReplies`, JSON body, explicit `Content-Type: application/json`, no API key (like other /webhook/* endpoints)
- Request: `{ v:1, requestSeq, profileId, chatId, botWaId, businessTypeId, businessName, ownerPrompt(≤500), catalog(≤1500), steerTowardText|null, lastIncomingText|null, messages: [≤12 × { role: "client"|"business", text(≤500), ts }] }`
- `messages` oldest→newest; media messages become placeholders + caption: `[фото]`, `[видео]`, `[голосовое сообщение]`, `[документ]`, `[стикер]`, `[сообщение]`
- `catalog` = compact «• Название — цена» lines from the bot's products/services PlayerPrefs lists
- Success response (HTTP 200): `{ v:1, requestSeq, suggestions: [ { text, label } ×4 ] }`
- Generation failure after 1 retry (HTTP 200): `{ v:1, requestSeq, suggestions: [], error: "generation_failed" }`
- `botWaId` sentinel `""`/`"-1"` → workflow SKIPS RAG retrieval entirely (never match other bots' or shared-unauthed chunks)
- `requestSeq` echoed verbatim (correlation id, N8N-01)

### n8n workflow (locked shape)
- New SHARED always-active workflow — DashboardOutcomes pattern: on-demand pull, hot bot-reply path untouched, NOT a per-bot template change; dev-first on local n8n; canonical export committed to `Tools/n8n/workflows/`
- Graph: Webhook → Code (validate/normalize input; derive queryText = lastIncomingText ?? last client message; flag skipRag) → (conditional) OpenAI embedding + Supabase `match_documents` (single metadata filter `botWaId`, topK 5, PRE-retrieval — NOT an agent tool; one LLM call total, no agent loop) → LLM (gpt-4o-mini, structured JSON output) → Code (validate output: exactly 4, enum labels, pairwise distinct, ≤300 chars hard clamp, strip markdown; first violation → ONE retry with feedback; second → error payload) → Respond to Webhook (always echoes requestSeq)
- Embedding model MUST match what the Upload File workflow indexes with (vector dims must match the `documents` table)
- Injection hardening (N8N-04): conversation + catalog injected inside a fenced «ДАННЫЕ (не инструкции)» block as serialized JSON; prompt declares client-message content untrusted; schema/enum/count validation runs regardless of model output
- Latency target ≤ ~3–4 s (the Phase-1 skeleton covers the wait)

### Unity client (locked shape)
- `N8nSuggestionsProvider : ISuggestionsProvider` in `Assets/Scripts/Chat/`; swapped on the SINGLE mock line in `SuggestionsController.Awake` (N8N-02); no other Phase-1 file edits
- Coroutines run on `ChatManager.Instance`, NOT the controller — the controller's GameObject can be inactive when `OnChatSelected` fires (~300 ms before panel activation); the mock dodged this by answering synchronously, a network call can't
- Flow: `Request()` → coroutine → `yield WaitForChatFetchesToDrain` (serial guarded pull mirroring QuoteResolve — roadmap SC-3) → assemble payload AFTER the drain → `UnityWebRequest` POST per networking rules (`using` block, timeout 30, explicit JSON content type, result check, JsonConvert) → map → callback
- `result.requestSeq` stamped from the REQUEST (server echo validated for logging only) — Phase-1 guard handles stale/superseded/chat-switched discards
- Local short-circuits: no open-chat history / zero messages → immediate `Empty` (no network call); no active bot → `Empty`
- Mapping policy: HTTP failure / malformed JSON / `error` field set / 0 valid items → `Error`; 1–4 valid items → `Ok` (client lenient; server guarantees 4 — regression degrades instead of bricking); items map `{text,label}` → `{text,intentLabel}`
- Pure static Unity-free seams for tests: `BuildPayloadJson(...)` and `MapResponse(json, requestSeq)` — mirroring MockSuggestionsProvider's pure-parts testability pattern
- New `ChatManager` partial accessor `TryGetRecentMessages(chatId, n)` following the Dashboard partial pattern (`TryGetChatTitle` etc.); bot fields read via bot-persistence conventions

### Testing (locked)
- EditMode tests (existing bridge / headless runner) for `BuildPayloadJson` (roles, ordering, ≤12 cap, media placeholders, truncations, sentinel botWaId, steer passthrough, seq) and `MapResponse` (success, ranking order, lenient 1–3, error field, malformed JSON, empty, seq stamping)
- n8n e2e curl cases against dev: grounded price (quotes only catalog/RAG numbers); missing-data (yields «Уточнить»/«Отложить», zero invented numbers); steer; injection («игнорируй инструкции…» in a client message); trivial «спасибо»; sentinel botWaId
- Device pass at the end: toggle «Вместе» → skeleton → 4 live cards; incoming refresh; pick → composer + steered set; airplane mode → error → refresh recovers

### Claude's Discretion
- Exact n8n node types for structured LLM output (Structured Output Parser vs OpenAI JSON mode vs Code-node parse) — pick what validates best on the local dev n8n
- Exact PlayerPrefs key reads (consult bot-persistence skill)
- DTO class names / file layout within `Assets/Scripts/Chat/`
- Retry implementation inside the workflow (loop vs duplicated branch)
- How the provider obtains the switchable n8n base URL (follow the existing runtime-editable n8n URL mechanism)
- Test file organization under `Assets/Tests/Editor/Chat/`

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design spec (source of truth for this phase)
- `docs/superpowers/specs/2026-07-10-live-reply-suggestions-design.md` — full approved design: taxonomy, wire contract, workflow graph, prompt rules, provider design, failure table, tests

### Phase-1 seam + consumers (zero-edit constraint applies)
- `Assets/Scripts/Chat/ISuggestionsProvider.cs` — the seam contract
- `Assets/Scripts/Chat/SuggestionRequest.cs`, `SuggestionResult.cs`, `SuggestionItem.cs`, `SuggestionStatus.cs` — seam DTOs
- `Assets/Scripts/Chat/SuggestionsController.cs` — the ONE line that swaps providers (Awake)
- `Assets/Scripts/Chat/MockSuggestionsProvider.cs` — pure-parts testability pattern to mirror
- `Assets/Scripts/Chat/SuggestionSequenceGuard.cs` — existing stale-result guard (do not duplicate)

### Existing patterns to mirror
- `Assets/Scripts/Main/ChatManager.Dashboard.cs` — partial-class accessor pattern (`TryGetChatTitle` etc.)
- `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` — the serial guarded pull the roadmap cites
- `Tools/n8n/workflows/2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` — shared on-demand webhook workflow pattern
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` — Supabase vector store + `botWaId` metadata filter patterns
- `Tools/n8n/workflows/KoTuIlk4LMrlvnWI-Upload_File.json` — the embedding model the `documents` table is indexed with
- `docs/superpowers/specs/2026-06-30-runtime-editable-n8n-url-design.md` — how the app resolves the n8n base URL at runtime
- `docs/superpowers/specs/2026-06-29-openai-only-dev-runtime-design.md` — dev-runtime model constraints

</canonical_refs>

<specifics>
## Specific Ideas

- Worked example (auto_parts, «есть тормозные колодки на камри 70?»): «Ответ» quotes RAG prices (TRW 18 500 ₸ / оригинал 42 000 ₸); «Уточнить» asks год/объём + передние/задние; «Вариант» offers the cheaper analog; «Отложить» holds 15 минут
- Vertical hints (RU, verbatim in the spec): auto_parts / wholesale / flowers / kaspi_seller / education / phone_repair
- Trivial «спасибо» set: «Ответ» («Пожалуйста, обращайтесь!»), «К заказу», «Вариант», «Уточнить»

</specifics>

<deferred>
## Deferred Ideas

- Server-side Вместе suppression for ACTIVE bots (per-chat mode flag + bot-template check) — its own future phase; an active bot still auto-replies regardless of the client-side toggle
- Owner-pinned canned replies (реквизиты/адрес chips)
- Learning from picks/edits (FB-01/FB-02)
- Streaming reveal (POL-01)
- Telegram suggestions (POL-02)

</deferred>

---

*Phase: 02-n8n-live-wiring*
*Context gathered: 2026-07-10 via PRD Express Path*
