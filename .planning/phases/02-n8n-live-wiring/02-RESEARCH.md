# Phase 2: n8n Live Wiring - Research

**Researched:** 2026-07-10
**Domain:** Unity coroutine networking (provider behind an existing seam) + n8n synchronous webhook workflow with structured LLM output and pre-retrieval RAG
**Confidence:** HIGH (client patterns + LLM/webhook shape proven in-repo; one MEDIUM item: the Supabase Vector Store *plain-retrieval* mode is not yet used in-repo — confirm node param keys via n8n MCP at build time)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Suggestion content**
- Cards are 4 DISTINCT MOVES from a closed 6-label taxonomy: «Ответ», «Уточнить», «Вариант», «К заказу», «Отложить», «Отказ» — no duplicate labels in a set, ranked best-first, card 1 = the Recommended lead.
- Always exactly 4 cards on success (validated server-side); trivial messages («спасибо») still get 4 best-fitting distinct moves.
- Grounding rule: prices/stock/terms ONLY from retrieved RAG chunks or the request `catalog`; a missing fact converts that card to «Уточнить»/«Отложить» — never an invented number.
- Style: mirror customer language (RU/KZ) and ты/вы register; 1–3 sentences, ≤220 chars target (300 hard clamp); max 1 emoji.
- Per-vertical behavior hints keyed by `businessTypeId` — compact 1–2 line map inside the workflow (NOT the long vertical main prompts); unknown/empty id → no hint.
- Steering (`steerTowardText` set): 4 refinements of the picked direction; labels still from the enum, still distinct.

**Wire contract v1 (locked)**
- `POST {n8nBase}/webhook/SuggestReplies`, JSON body, explicit `Content-Type: application/json`, no API key.
- Request: `{ v:1, requestSeq, profileId, chatId, botWaId, businessTypeId, businessName, ownerPrompt(≤500), catalog(≤1500), steerTowardText|null, lastIncomingText|null, messages: [≤12 × { role:"client"|"business", text(≤500), ts }] }` (messages oldest→newest; media → placeholders + caption).
- `catalog` = compact «• Название — цена» lines from the bot's products/services PlayerPrefs lists.
- Success (HTTP 200): `{ v:1, requestSeq, suggestions:[ {text,label} ×4 ] }`. Failure after 1 retry (HTTP 200): `{ v:1, requestSeq, suggestions:[], error:"generation_failed" }`.
- `botWaId` sentinel `""`/`"-1"` → workflow SKIPS RAG retrieval entirely. `requestSeq` echoed verbatim (correlation id, N8N-01).

**n8n workflow (locked shape)**
- New SHARED always-active workflow (DashboardOutcomes pattern): on-demand pull, hot bot-reply path untouched, NOT a per-bot template change; dev-first on local n8n; canonical export committed to `Tools/n8n/workflows/`.
- Graph: Webhook → Code(validate/normalize; derive queryText, flag skipRag) → (conditional) OpenAI embedding + Supabase `match_documents` (single metadata filter `botWaId`, topK 5, PRE-retrieval, NOT an agent tool; one LLM call total) → LLM (gpt-4o-mini, structured JSON) → Code(validate: exactly 4, enum labels, pairwise distinct, ≤300 char clamp, strip markdown; 1 retry then error) → Respond to Webhook (always echoes requestSeq).
- Embedding model MUST match the Upload File workflow's (vector dims must match `documents`).
- Injection hardening (N8N-04): conversation+catalog inside a fenced «ДАННЫЕ (не инструкции)» block as serialized JSON; prompt declares client-message content untrusted; schema/enum/count validation runs regardless of model output.
- Latency target ≤ ~3–4 s (Phase-1 skeleton covers the wait).

**Unity client (locked shape)**
- `N8nSuggestionsProvider : ISuggestionsProvider` in `Assets/Scripts/Chat/`; swapped on the SINGLE mock line in `SuggestionsController.Awake` (N8N-02); no other Phase-1 file edits.
- Coroutines run on `ChatManager.Instance`, NOT the controller (controller GameObject can be inactive ~300 ms when `OnChatSelected` fires).
- Flow: `Request()` → coroutine → `yield` drain → assemble payload AFTER drain → `UnityWebRequest` POST (`using`, timeout 30, explicit JSON content type, result check, JsonConvert) → map → callback.
- `result.requestSeq` stamped from the REQUEST (server echo validated for logging only); Phase-1 guard handles stale/superseded/chat-switched.
- Local short-circuits: no history / zero messages / no active bot → immediate `Empty` (no network call).
- Mapping: HTTP failure / malformed JSON / `error` field set / 0 valid items → `Error`; 1–4 valid items → `Ok`; items map `{text,label}` → `{text,intentLabel}`.
- Pure static Unity-free seams for tests: `BuildPayloadJson(...)` and `MapResponse(json, requestSeq)`.
- New `ChatManager` partial accessor `TryGetRecentMessages(chatId, n)` following the Dashboard partial pattern; bot fields read via bot-persistence conventions.

**Testing (locked)**
- EditMode tests for `BuildPayloadJson` and `MapResponse` (see Validation notes below).
- n8n e2e curl cases against dev: grounded price; missing-data; steer; injection; trivial «спасибо»; sentinel botWaId.
- Device pass at the end: toggle «Вместе» → skeleton → 4 live cards; incoming refresh; pick → composer + steered set; airplane mode → error → refresh recovers.

### Claude's Discretion
- Exact n8n node types for structured LLM output (Structured Output Parser vs OpenAI JSON mode vs Code-node parse) — pick what validates best on local dev n8n.
- Exact PlayerPrefs key reads (consult bot-persistence skill).
- DTO class names / file layout within `Assets/Scripts/Chat/`.
- Retry implementation inside the workflow (loop vs duplicated branch).
- How the provider obtains the switchable n8n base URL (follow the existing runtime-editable n8n URL mechanism).
- Test file organization under `Assets/Tests/Editor/Chat/`.

### Deferred Ideas (OUT OF SCOPE)
- Server-side «Вместе» suppression for ACTIVE bots (an active bot still auto-replies regardless of the client toggle) — its own future phase.
- Owner-pinned canned replies (реквизиты/адрес chips).
- Learning from picks/edits (FB-01/FB-02).
- Streaming reveal (POL-01).
- Telegram suggestions (POL-02).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| **N8N-01** | Synchronous Webhook + Respond-to-Webhook flow returning a versioned `{text,label}[]` payload ranked best-first + correlation id | Mirror `Dashboard Outcomes` (`2htWSV5IHO8E2CgB`): `webhook` tv 2.1 `responseMode:"responseNode"` + `respondToWebhook` tv 1.5 `respondWith:"json"`, `responseBody:"={{ $json }}"`. `requestSeq` echoed by a Code node that assembles the response object (Dashboard's "Build Response" pattern). See Architecture Pattern 1. |
| **N8N-02** | `N8nSuggestionsProvider` consumes the live flow end-to-end with ZERO Phase-1 UI edits | Single swap line: `SuggestionsController.cs` L31 (`Assets/Scripts/Chat/SuggestionsController.cs`). Provider is a plain C# class implementing `ISuggestionsProvider.Request(SuggestionRequest, Action<SuggestionResult>)`. Mock referenced only in the controller + its own test → surface is clean. See Architecture Pattern 2. |
| **N8N-03** | Live flow supports re-clustering via a "steer toward" field carrying the picked reply | Seam DTO `SuggestionRequest.steerTowardText` already exists and is passed by the controller on card-tap (`SuggestionsController.HandleCardTapped` → `IssueRequest(steerTowardText: replyText, …)`). Provider forwards it into the wire payload; workflow branches on non-null `steerTowardText` in the prompt. |
| **N8N-04** | Validate structured output against a schema + harden against prompt injection; labels constrained to a known set; malformed output handled, not surfaced raw | Two-layer defense proven in-repo: OpenAI `response_format:{type:"json_schema",strict:true}` with `enum`-constrained `label` (Dashboard "Classify" node), PLUS a Code node doing count/distinct/clamp/strip (Dashboard "Parse" node). Data-fencing via `{{ JSON.stringify(...) }}` of untrusted content. See Security Domain + Pattern 3. |
</phase_requirements>

## Summary

Phase 1 already shipped the entire suggestions seam and UI on mock data. Every client-side artifact the planner needs to mirror **already exists in the repo** and was read directly: the seam (`ISuggestionsProvider` + 5 DTOs), the controller with its single mock-instantiation line, the stale-result guard, the `ChatManager.Suggestions.cs` partial exposing `CurrentChatId` + the drain hook, the QuoteResolve serial-guarded-pull pattern, the Dashboard partial-accessor pattern, and a proven webhook-POST-returning-JSON coroutine (`DashboardPage.FetchRoutine`). This is a **brownfield wiring phase**, not a greenfield build — the highest-value research output is the exact names, signatures, and file locations of the patterns to copy, plus the two seams the planner must NOT touch.

On the n8n side, the closest template — `Dashboard Outcomes` — is a shared always-active webhook that calls gpt-4o-mini via a **plain `httpRequest` node** (not a LangChain agent) with `response_format: json_schema` (strict + enum) and parses the result in a Code node. That single pattern answers the biggest open design question (how to get schema-conforming JSON reliably on this n8n version) and simultaneously sidesteps the AI-Agent `output`-key double-nesting bug flagged in STATE.md. Embedding parity is settled: both `Upload File` and `WhatsApp Bot` index/query the `documents` table with **`text-embedding-3-small`** (pinned `vector(1536)`), and `match_documents(query_embedding, filter jsonb, match_count)` uses OR filter semantics (single-key `botWaId` filter is correct and required).

The one genuinely new n8n mechanic is **plain pre-retrieval** (the design forbids an agent tool). The Supabase Vector Store node has a "Get Many" mode (`mode:"load"` in JSON) that does exactly this and supports a metadata filter — but the repo has only ever used the node in `insert` and `retrieve-as-tool` modes, so its exact param keys must be confirmed via the n8n MCP `get_node_types` at build time (established project practice). A fully plain-HTTP fallback (OpenAI `/v1/embeddings` → Supabase PostgREST `/rpc/match_documents`) is documented as a robust alternative that stays entirely inside the repo's proven plain-HTTP idiom.

**Primary recommendation:** Build the workflow by cloning the `Dashboard Outcomes` skeleton (Webhook `responseNode` → Prep Code → LLM via plain `httpRequest` with `json_schema` strict+enum → Parse/validate Code → Respond), inserting the conditional RAG pre-retrieval (Supabase Vector Store `load` mode, `text-embedding-3-small`, `match_documents`, single `botWaId` filter, topK 5) before the LLM. On the client, add `N8nSuggestionsProvider` mirroring `DashboardPage.FetchRoutine`, run its coroutine on `ChatManager.Instance`, wait on the public `WaitForChatFetchesDrain()` hook, and swap exactly one line in `SuggestionsController.Awake`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Trigger a suggestion request (toggle/incoming/pick/refresh) | Unity client (`SuggestionsController`, Phase-1, untouched) | — | Already owns the seq guard, panel states, composer hand-off. Phase 2 adds nothing here except the provider swap. |
| Gather chat context + bot fields, assemble payload | Unity client (`N8nSuggestionsProvider` + `ChatManager` partial) | — | Bot data lives in PlayerPrefs / `Bot` component; recent messages live in `ChatManager._activeChatCache`. Only the client can read these. |
| Serial-guarded network call + drain | Unity client (coroutine on `ChatManager.Instance`) | — | ChatManager is the always-active network hub and owns `_chatFetchesInFlight`. |
| Input validation / normalization / skipRag decision | n8n workflow (Code node) | — | Untrusted input must be normalized server-side before it reaches the LLM. |
| Query embedding | OpenAI (`text-embedding-3-small`) | n8n orchestrates | Must match the model that indexed `documents`. |
| Vector similarity search (tenant-scoped) | Supabase (`match_documents` RPC, `documents` table) | n8n orchestrates via Vector Store node | Data + pgvector HNSW index live in Supabase; `botWaId` filter enforces per-bot isolation. |
| Generate ranked reply options (structured JSON) | OpenAI (gpt-4o-mini, `json_schema` strict) | n8n orchestrates | LLM reasoning tier; `enum` label constraint enforced at the model. |
| Output schema/count/distinct validation + retry | n8n workflow (Code node) | — | Defense-in-depth: count/distinct/clamp are NOT expressible in strict json_schema (see State of the Art) — must run in code. |
| Map response → `SuggestionResult`, stamp requestSeq | Unity client (`MapResponse`, pure static) | — | Client stays lenient (renders 1–4); server guarantees 4. |
| Discard stale/superseded/chat-switched results | Unity client (`SuggestionSequenceGuard`, Phase-1, untouched) | — | Already implemented; provider adds nothing stateful. |

## Standard Stack

### Core (client — all already present, versions VERIFIED in repo)
| Library / API | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Newtonsoft.Json | 13.0.4 | `JsonConvert.SerializeObject/DeserializeObject` for request/response DTOs | `[VERIFIED: Packages/nuget-packages/packages.config]`. Mandated by `.claude/rules/networking.md`; used by every existing webhook caller. |
| UnityWebRequest (UnityEngine.Networking) | Unity 6000.3.9f1 | POST coroutine transport | `[VERIFIED: repo]` Project-wide network pattern; `.claude/rules/networking.md` POST block is the exact template. |
| NUnit (Unity Test Framework, EditMode) | bundled | `BuildPayloadJson` / `MapResponse` pure-part tests | `[VERIFIED: Assets/Tests/Editor/Chat/*]` No asmdef — compiles into `Assembly-CSharp-Editor`. |

### Core (n8n workflow nodes — types + typeVersions VERIFIED from the committed JSONs)
| Node type | typeVersion | Purpose | Source of truth |
|-----------|-------------|---------|-----------------|
| `n8n-nodes-base.webhook` | 2.1 | Entry point, `httpMethod:"POST"`, `path:"SuggestReplies"`, `responseMode:"responseNode"` | `[VERIFIED: Dashboard_Outcomes.json]` |
| `n8n-nodes-base.code` | 2 | Input validate/normalize; output validate/retry-gate; response assembly | `[VERIFIED: Dashboard "Prep"/"Parse"/"Build Response"]` |
| `n8n-nodes-base.httpRequest` | 4.2 | LLM call → `https://api.openai.com/v1/chat/completions` with `response_format` json_schema | `[VERIFIED: Dashboard "Classify"]` |
| `@n8n/n8n-nodes-langchain.vectorStoreSupabase` | 1.3 | RAG pre-retrieval in **`load`** ("Get Many") mode | `[VERIFIED: WhatsApp_Bot/Upload_File use tv 1.3 in other modes]` `[CITED: n8n-docs vectorstoresupabase]` |
| `@n8n/n8n-nodes-langchain.embeddingsOpenAi` | 1.2 | Query embedding sub-node (`ai_embedding`) feeding the Vector Store | `[VERIFIED: WhatsApp_Bot "OpenAI Embedding" / Upload_File "Embeddings OpenAI"]` |
| `n8n-nodes-base.if` | 2.2 | Branch on `skipRag` | `[VERIFIED: Dashboard "Has Sessions?"]` |
| `n8n-nodes-base.respondToWebhook` | 1.5 | Return payload, always echoing `requestSeq` | `[VERIFIED: Dashboard "Respond"]` |

### Models (VERIFIED — the repo itself is the registry)
| Model | Purpose | Parity requirement | Source |
|-------|---------|--------------------|--------|
| `text-embedding-3-small` | Embed `queryText` for retrieval | MUST match the index model; `documents.embedding` is `vector(1536)` pinned on both sides | `[VERIFIED: Upload_File + WhatsApp_Bot "model":"text-embedding-3-small"; schema.sql vector(1536)]` |
| `gpt-4o-mini` | Generate the 4 ranked replies (structured JSON) | Supports `response_format json_schema` (gpt-4o-mini-2024-07-18+); already used in prod for RU classification | `[VERIFIED: Dashboard "Classify" + Upload_File image OCR]` `[VERIFIED: web — structured outputs model support]` |

### Supporting
| Component | Purpose | When to Use |
|-----------|---------|-------------|
| `Manager.n8nBaseUrl` (static) | Resolves runtime override (PlayerPrefs `DevN8nBaseUrl`) → `secrets.json` → Cloud default | Always — build the URL as `$"{Manager.n8nBaseUrl}/webhook/SuggestReplies"`. `[VERIFIED: Manager.cs L165]` |
| `Supabase` credential (`supabaseApi`, service_role) | Vector Store node auth for `match_documents` | Required by the RAG node — `match_documents` EXECUTE is granted ONLY to service_role. `[VERIFIED: harden-rag-store.sql]` |
| `OpenAi account` credential (`openAiApi`) | LLM httpRequest + embedding node | Both use `nodeCredentialType:"openAiApi"` / `ai_embedding` cred. `[VERIFIED: Dashboard + Upload_File]` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| LLM via plain `httpRequest` + `json_schema` | LangChain LLM Chain + Structured Output Parser, or AI Agent | Rejected: the plain-HTTP path is already proven in Dashboard, is a single call (no agent loop → meets the "one LLM call" constraint), and **avoids the AI-Agent `output`-key double-nesting bug** flagged in STATE.md. The agent/parser path re-introduces that risk for no benefit. |
| Supabase Vector Store `load` mode | Two plain-HTTP nodes: OpenAI `/v1/embeddings` → Supabase PostgREST `POST /rest/v1/rpc/match_documents` (`{query_embedding, filter, match_count}`) | Viable fallback that stays in the plain-HTTP idiom and gives exact filter/topK control. Use if `load`-mode metadata filtering proves awkward on tv 1.3. **Do NOT** route the 1536-float vector through the Postgres node's `queryReplacement` — it comma-splits list params (memory: n8n Postgres gotchas); send the vector as a JSON array in an httpRequest body instead. |
| `topK 5` | `topK 10` (WhatsApp_Bot uses 10) | Design locked topK 5 for latency; both are valid. |

**Installation / setup:** No new npm/NuGet packages. The workflow needs three credentials present on the target n8n instance: `OpenAi account`, `Supabase` (service_role), and none for the webhook itself (public `/webhook/*`, consistent with all other app webhooks). **Local dev caveat** — see Environment Availability: the OpenAI-only dev runtime intentionally dropped Supabase; the `documents` table on local dev may be empty and the `Supabase` credential may not exist locally.

**Version verification:** Model IDs and node typeVersions above are verified against the committed workflow JSONs and the live `documents` schema (strongest possible verification — the app runs on exactly these). The only value to re-confirm at build time is the `vectorStoreSupabase` **`load`-mode parameter keys** (query/prompt field name, limit/topK key) via n8n MCP `get_node_types` — the repo has not exercised that mode.

## Architecture Patterns

### System Architecture Diagram

```
[Unity: SuggestionsController]  (Phase-1, UNTOUCHED except 1 Awake line)
   │  toggle-on / incoming / card-pick / manual-refresh → IssueRequest()
   │  builds seam SuggestionRequest { chatId, lastIncomingText, steerTowardText, requestSeq }
   ▼
[ISuggestionsProvider.Request(req, cb)]  ── seam (UNTOUCHED) ──
   ▼
[N8nSuggestionsProvider]  (NEW, plain C# class)
   │  short-circuit: no active bot / no open chat / 0 messages → cb(Empty), no network
   │  ChatManager.Instance.StartCoroutine(...)   ← runs on the always-active hub, NOT the controller
   ▼
   yield ChatManager.Instance.WaitForChatFetchesDrain()   ← serial guard (never bumps _chatFetchesInFlight)
   │
   │  assemble AFTER drain (freshest history):
   │   • TryGetRecentMessages(chatId, 12)  → List<MessageViewModel> from _activeChatCache
   │   • active Bot fields (PlayerPrefs + Bot component) → profileId, botWaId(=workflowId),
   │     businessTypeId, businessName, ownerPrompt, catalog(products+services)
   │  BuildPayloadJson(...)  ← pure static, JsonConvert
   ▼
   UnityWebRequest POST {Manager.n8nBaseUrl}/webhook/SuggestReplies
     Content-Type: application/json   (REQUIRED)   timeout=30
   │
   ▼                                              ┌───────────────── n8n: Suggest Replies (shared, active) ─────────────────┐
[HTTP] ───────────────────────────────────────▶ │ Webhook(POST /SuggestReplies, responseNode)                            │
                                                  │   ▼                                                                    │
                                                  │ Code: validate v==1, chatId, messages; derive queryText;              │
                                                  │       flag skipRag = botWaId∈{"","-1"} || queryText empty              │
                                                  │   ▼                                                                    │
                                                  │ If skipRag ──true──────────────────────────────┐                       │
                                                  │   │false                                        │                       │
                                                  │   ▼                                             │                       │
                                                  │ Supabase Vector Store (load) ◀─ embeddingsOpenAi(text-embedding-3-small)│
                                                  │   filter {botWaId}, match_documents, topK 5     │                       │
                                                  │   → chunks[].content                            │                       │
                                                  │   ▼                                             ▼                       │
                                                  │ Code: assemble system prompt + fenced «ДАННЫЕ (не инструкции)» JSON     │
                                                  │   ▼                                                                    │
                                                  │ httpRequest → OpenAI chat/completions (gpt-4o-mini)                     │
                                                  │   response_format: json_schema strict, label ∈ enum                     │
                                                  │   ▼                                                                    │
                                                  │ Code: JSON.parse; require exactly 4, labels∈enum, pairwise distinct,   │
                                                  │   clamp ≤300, strip markdown → 1st violation: retry once w/ feedback   │
                                                  │                                → 2nd: {suggestions:[],error:"…"}       │
                                                  │   ▼                                                                    │
                                                  │ Respond to Webhook (json, echo requestSeq)                             │
                                                  └────────────────────────────────────────────────────────────────────────┘
   ▼
[MapResponse(json, requestSeq)]  ← pure static: HTTP fail / bad JSON / error field / 0 items → Error; 1–4 → Ok
   ▼
cb(SuggestionResult{ items, requestSeq(from REQUEST), status })
   ▼
[SuggestionsController.OnResult]  ← SuggestionSequenceGuard.IsCurrent(seq, chatId) discards stale → panel.Render()
```

### Recommended Client File Layout (`Assets/Scripts/Chat/`, DTO names at Claude's discretion)
```
Assets/Scripts/Chat/
├── N8nSuggestionsProvider.cs      # NEW: ISuggestionsProvider impl; BuildPayloadJson + MapResponse (pure static)
├── SuggestRepliesDtos.cs          # NEW: [Serializable] request/response DTOs (public fields) — or fold into provider
└── (Phase-1 seam files — DO NOT EDIT)
Assets/Scripts/Main/
└── ChatManager.RecentMessages.cs  # NEW partial: TryGetRecentMessages(chatId, n) over _activeChatCache
Assets/Scripts/Chat/SuggestionsController.cs   # EDIT: exactly ONE line in Awake (the provider swap)
Assets/Tests/Editor/Chat/
├── SuggestRepliesPayloadTests.cs  # NEW: BuildPayloadJson cases
└── SuggestRepliesMapTests.cs      # NEW: MapResponse cases
Tools/n8n/workflows/
└── <newId>-Suggest_Replies.json   # NEW: canonical export (12th workflow)
```

### Pattern 1: Shared synchronous webhook (mirror Dashboard Outcomes)
**What:** A standalone always-active workflow that responds in-band via a Respond node.
**When:** N8N-01. This is the whole server side.
```jsonc
// Webhook node — Source: Dashboard_Outcomes.json "Webhook" [VERIFIED]
{ "httpMethod": "POST", "path": "SuggestReplies", "responseMode": "responseNode", "options": {} }
// Respond node — Source: Dashboard_Outcomes.json "Respond" [VERIFIED]
{ "respondWith": "json", "responseBody": "={{ $json }}", "options": {} }
// Workflow top-level: "active": true, "settings": { "executionOrder": "v1" }
```
Request arrives as `$json.body.*` inside the first Code node (`[VERIFIED: Dashboard "Prep" reads $json.body.profileIds]`).

### Pattern 2: Provider mirrors DashboardPage.FetchRoutine, runs on ChatManager.Instance
**What:** The exact webhook-POST-returning-JSON coroutine to copy.
**When:** N8N-02.
```csharp
// Source: Assets/Scripts/Main/Dashboard/DashboardPage.cs FetchRoutine (L127-148) [VERIFIED]
string url  = $"{Manager.n8nBaseUrl}/webhook/DashboardOutcomes";
string body = JsonConvert.SerializeObject(new { profileIds = profiles });
using var req = new UnityWebRequest(url, "POST");
req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
req.downloadHandler = new DownloadHandlerBuffer();
req.SetRequestHeader("Content-Type", "application/json");   // REQUIRED
req.timeout = 30;
yield return req.SendWebRequest();
if (req.result != UnityWebRequest.Result.Success) { /* → Error */ }
var parsed = DashboardResponse.Parse(req.downloadHandler.text);   // static Parse — mirror as MapResponse
```
**Provider skeleton (NEW):**
```csharp
public class N8nSuggestionsProvider : ISuggestionsProvider
{
    public void Request(SuggestionRequest req, Action<SuggestionResult> cb)
    {
        var cm = ChatManager.Instance;
        if (cm == null) { cb?.Invoke(Empty(req)); return; }
        // local short-circuits (no active bot / no history / 0 messages) → cb(Empty), no network
        cm.StartCoroutine(Run(req, cb));   // ChatManager is ALWAYS active — controller may be inactive
    }
    private IEnumerator Run(SuggestionRequest req, Action<SuggestionResult> cb)
    {
        yield return ChatManager.Instance.WaitForChatFetchesDrain();   // ← EXACT public hook name
        string json = BuildPayloadJson(req, /* gathered messages + bot fields */);
        using var www = new UnityWebRequest($"{Manager.n8nBaseUrl}/webhook/SuggestReplies", "POST");
        www.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = 30;
        yield return www.SendWebRequest();
        cb?.Invoke(www.result == UnityWebRequest.Result.Success
            ? MapResponse(www.downloadHandler.text, req.requestSeq)
            : Error(req));
    }
}
```
**The single swap line — Source: `SuggestionsController.cs` L31 `[VERIFIED]`:**
```csharp
// BEFORE: _provider = new MockSuggestionsProvider(this, _mockLatencySeconds);
// AFTER:  _provider = new N8nSuggestionsProvider();
```
`_mockLatencySeconds` becomes an unused serialized field (no compile error; keep it or leave it). `MockSuggestionsProvider` stays in the repo — its EditMode tests still reference it.

### Pattern 3: Structured JSON out of gpt-4o-mini via plain httpRequest (mirror Dashboard "Classify")
**What:** OpenAI Structured Outputs (strict json_schema) with an `enum`-constrained label + data-fenced untrusted input.
**When:** N8N-04 (defense layer 1). See Security Domain.
```jsonc
// Source: Dashboard_Outcomes.json "Classify" [VERIFIED] — adapt schema to suggestions
{
  "method": "POST", "url": "https://api.openai.com/v1/chat/completions",
  "authentication": "predefinedCredentialType", "nodeCredentialType": "openAiApi",
  "sendBody": true, "specifyBody": "json",
  "jsonBody": "={ \"model\":\"gpt-4o-mini\", \"temperature\":0.4, \"max_tokens\":700,
    \"messages\":[ {\"role\":\"system\",\"content\": <RU system prompt + vertical hint> },
                   {\"role\":\"user\",\"content\": {{ JSON.stringify($json.fencedData) }} } ],
    \"response_format\":{ \"type\":\"json_schema\", \"json_schema\":{ \"name\":\"reply_suggestions\",
      \"strict\":true, \"schema\":{ \"type\":\"object\", \"additionalProperties\":false,
        \"required\":[\"suggestions\"], \"properties\":{ \"suggestions\":{ \"type\":\"array\",
          \"items\":{ \"type\":\"object\", \"additionalProperties\":false,
            \"required\":[\"text\",\"label\"], \"properties\":{
              \"text\":{ \"type\":\"string\" },
              \"label\":{ \"type\":\"string\", \"enum\":[\"Ответ\",\"Уточнить\",\"Вариант\",\"К заказу\",\"Отложить\",\"Отказ\"] }
        }}}}}}}}"
}
```
**Output-validation Code node (defense layer 2) — mirror Dashboard "Parse":**
```javascript
// Source idiom: Dashboard_Outcomes.json "Parse" [VERIFIED]
const content = $json.choices?.[0]?.message?.content;      // OpenAI response shape
let items = []; try { items = JSON.parse(content).suggestions || []; } catch(e) {}
const ENUM = ['Ответ','Уточнить','Вариант','К заказу','Отложить','Отказ'];
items = items.map(x => ({ text: String(x.text||'').replace(/[*_`#]/g,'').slice(0,300), label: String(x.label||'').trim() }));
const distinct = new Set(items.map(i => i.label)).size === items.length;
const ok = items.length === 4 && items.every(i => ENUM.includes(i.label)) && distinct;
// ok=false → route to retry-once (with the violation as feedback) → then error payload
```
> **Critical (see State of the Art):** strict json_schema enforces `label ∈ enum` but **cannot** enforce "exactly 4", "pairwise distinct", or "≤300 chars" — those keywords are ignored in strict mode. The Code node is therefore **mandatory**, not belt-and-suspenders.

### Pattern 4: Plain pre-retrieval RAG (Supabase Vector Store `load` mode)
**What:** Query-time retrieval as regular items (not an agent tool), tenant-scoped by `botWaId`.
**When:** the conditional RAG branch (skipped when `skipRag`).
```jsonc
// Adapt WhatsApp_Bot "Supabase Vector Store" [VERIFIED tv 1.3], changing mode retrieve-as-tool → load
{
  "mode": "load",                                  // UI: "Get Many" — CONFIRM exact param keys via n8n MCP get_node_types
  "tableName": { "__rl": true, "value": "documents", "mode": "list" },
  "topK": 5,
  "options": {
    "queryName": "match_documents",
    "metadata": { "metadataValues": [ { "name": "botWaId", "value": "={{ $json.botWaId }}" } ] }
  }
}
// Sub-node (ai_embedding): embeddingsOpenAi { "model": "text-embedding-3-small" }  [VERIFIED parity]
// match_documents(query_embedding vector, filter jsonb DEFAULT '{}', match_count int DEFAULT 5)
//   → TABLE(id, content, metadata, similarity); multi-key filter = OR (keep SINGLE key).  [VERIFIED: schema.sql]
```
Downstream: concatenate the retrieved `content` fields into the fenced data block for the LLM.

### Anti-Patterns to Avoid
- **Running the provider coroutine on the controller (`this`).** The controller GameObject is inactive ~300 ms when `OnChatSelected` fires → `StartCoroutine` throws "GameObject is inactive". Run on `ChatManager.Instance`. `[VERIFIED: SuggestionsController comment L34; MockSuggestionsProvider L48 dodges it synchronously]`
- **Calling the private `WaitForChatFetchesToDrain()` from the provider.** That's internal to ChatManager partials (QuoteResolve uses it directly). The provider is a *separate class* and must call the **public** `ChatManager.Instance.WaitForChatFetchesDrain()` (note: no "To"). `[VERIFIED: ChatManager.Suggestions.cs L18]`
- **Incrementing `_chatFetchesInFlight`.** The suggestion pull is NOT a `messages/get` caller; it only *waits* on the drain. Bumping the counter would deadlock the real chat fetches. `[VERIFIED: ChatManager.Suggestions.cs L16-18 comment]`
- **Editing any Phase-1 UI file** (`SuggestionsPanel`, `SuggestionCard`, `SemiAutoToggle`, the 5 seam DTOs, `SuggestionSequenceGuard`, or `SuggestionsController` beyond the 1 Awake line). Any such edit is a seam-breach defect (N8N-02).
- **Using an AI Agent / LLM Chain for generation.** Re-introduces the `output`-key double-nesting bug and an agent loop; the design mandates one LLM call. Use the plain httpRequest pattern.
- **Multi-key metadata filter.** `match_documents` ORs multiple keys — a second key would silently widen the tenant scope. Keep the single `botWaId` key. `[VERIFIED: schema.sql comment]`

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| n8n base URL resolution | A new secrets read / string concat | `Manager.n8nBaseUrl` (static) | Already resolves runtime override → secrets → Cloud, and is device-switchable without a rebuild. `[VERIFIED: Manager.cs L165]` |
| Stale/superseded/chat-switched result rejection | New guard state in the provider | `SuggestionSequenceGuard` in the controller (Phase-1) | The controller already stamps seq + captured chat and discards. Provider "adds nothing stateful." `[VERIFIED: SuggestionsController.OnResult]` |
| Serial-fetch safety vs Wappi crossing | A new lock/queue | `yield ChatManager.Instance.WaitForChatFetchesDrain()` | The drain hook (DATA-04) exists for exactly this. `[VERIFIED: ChatManager.Suggestions.cs]` |
| Tolerant JSON response parsing | Manual `JObject` walking + null checks everywhere | A `[Serializable]` DTO + static `Parse`/`MapResponse` with try/catch returning a safe default | `DashboardResponse.Parse` is the proven mirror (returns null on garbage). `[VERIFIED: DashboardModels.cs]` |
| Schema-conforming LLM JSON | Prompt-only "please return JSON" + hope | OpenAI `response_format: json_schema` (strict) + Code validation | Dashboard proves it; enum enforced at the model. `[VERIFIED: Dashboard "Classify"]` |
| Reading bot fields | Ad-hoc `PlayerPrefs.GetString("Bot0"+...)` with guessed suffixes | The exact key catalog (below) via `Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId)` | Wrong suffix reads back `""` silently. `[VERIFIED: bot-persistence/references/key-catalog.md]` |
| Vector query marshaling | Postgres node with the embedding as a `queryReplacement` param | Vector Store `load` node, or httpRequest to `/rpc/match_documents` with a JSON-array body | The Postgres node comma-splits list params → a 1536-float vector breaks. `[VERIFIED: memory project_n8n_postgres_node_gotchas]` |

**Key insight:** Nearly every "hard part" of this phase was already solved in Phase 1 or in the Dashboard feature. The phase is 80% wiring proven parts together; the only net-new logic is `BuildPayloadJson`/`MapResponse` (pure, testable) and the workflow graph.

### Exact bot-field reads for the payload (VERIFIED: key-catalog.md + Bot.cs)
Active bot = `Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId)`; `botName = bot.name` (e.g. `"Bot0"`).
| Wire field | Source | Notes |
|-----------|--------|-------|
| `profileId` | `bot.whatsappProfileId` (public field) | `"-1"`/empty = unauthed (`Bot.UnauthedProfileSentinel`). |
| `botWaId` | `bot.whatsappWorkflowId` (public field) | **= the n8n workflow id**, because RAG chunks are tagged `botWaId = {{ $workflow.id }}`. `"-1"`/`""` → workflow skips RAG. `[VERIFIED: WhatsApp_Bot metadata + schema.sql]` |
| `businessName` | `PlayerPrefs.GetString(botName+"Name","")` | |
| `businessTypeId` | `PlayerPrefs.GetString(botName+"BusinessType","")` | kebab id (e.g. `auto_parts`); may be a legacy id → workflow falls back to no vertical hint. |
| `ownerPrompt` | `PlayerPrefs.GetString(botName+"Prompt","")` | clamp ≤500. |
| `catalog` | products+services lists → `• {name} — {price}` lines | clamp ≤1500. |
| products | count `botName+"ProductsNumber"` (int, plural); items `botName+"Product"+i` / `+"Price"` / `+"Description"` (**singular** `Product`) | `[VERIFIED: Bot.cs L188-209; Manager.cs L601-603]`. CLAUDE.md's `Bot0Products0` is imprecise — the real item key is singular `Product{i}`. |
| services | count `botName+"ServicesNumber"`; items `botName+"Service"+i` / `+"Price"` / `+"Description"` | Same singular/plural asymmetry. |

## Common Pitfalls

### Pitfall 1: Provider coroutine on an inactive controller
**What goes wrong:** `StartCoroutine` throws; the request is silently lost.
**Why:** `OnChatSelected` fires ~300 ms before `SlideInToMessages` activates the chat panel, so the controller can be inactive at request time.
**How to avoid:** Run on `ChatManager.Instance` (always active). `[VERIFIED: SuggestionsController L34 + MockSuggestionsProvider L48]`
**Warning sign:** "Coroutine couldn't be started because the GameObject … is inactive" in the log on first toggle-on.

### Pitfall 2: Unity bodyless/mis-typed POST → n8n 415/parse failure
**What goes wrong:** libcurl stamps `application/x-www-form-urlencoded` on a POST unless you set the header; n8n mis-parses or 415s.
**Why:** Known Unity transport behavior (memory: project_unity_post_content_type).
**How to avoid:** Always `SetRequestHeader("Content-Type","application/json")` — every existing webhook caller does. We always send a body here, so the header is the only requirement. `[VERIFIED: DashboardPage L133, Manager.cs L2562]`

### Pitfall 3: Calling the wrong drain method / bumping the in-flight counter
**What goes wrong:** Compile error (private method), or a deadlock if the provider increments `_chatFetchesInFlight`.
**How to avoid:** Public hook `WaitForChatFetchesDrain()`; never touch the counter. It's bounded at 3 s so it can't hang. `[VERIFIED: ChatManager.cs L1315 + Suggestions.cs L18]`

### Pitfall 4: New .cs silently excluded from compilation
**What goes wrong:** Brand-new script isn't picked up on a busy asset refresh; tests/type "don't exist" with no error.
**Why:** Unity import quirk (memory: project_unity_new_file_import).
**How to avoid:** After creating each new `.cs`, run Assets → Refresh and verify the sibling `.meta` appeared before compiling/running tests. Stage `.cs` + `.meta` together. `[VERIFIED: memory + CLAUDE.md]`

### Pitfall 5: LLM count/distinct assumed enforced by the schema
**What goes wrong:** The model returns 3 or 5 items, or duplicate labels, and it sails through because strict json_schema ignores `minItems`/`maxItems`/`maxLength`.
**How to avoid:** The Code validation node MUST check count==4, `label ∈ enum`, pairwise-distinct, and clamp length. `[VERIFIED: web — OpenAI strict-mode keyword support]`

### Pitfall 6: RAG grounding untestable on local dev
**What goes wrong:** The "grounded price from RAG" e2e case returns nothing on local dev.
**Why:** The OpenAI-only dev runtime dropped Supabase; the local `documents` table is likely empty and the `Supabase` credential may be absent. `[VERIFIED: 2026-06-29-openai-only-dev-runtime-design.md; README "Known follow-ups" #1]`
**How to avoid:** Validate **catalog** grounding on dev (works with zero RAG — catalog comes from PlayerPrefs); to exercise RAG on dev, add the `Supabase` service_role credential locally and seed a few `documents` rows tagged with the test bot's `botWaId`. Otherwise validate RAG grounding during the prod bagkz replication.

### Pitfall 7: n8n Postgres/chat-memory gotchas do NOT apply here
**Note (not a trap to fix):** This workflow does not read `n8n_chat_histories`; conversation context is sent in the request. The blank-transcript / message-shape gotchas from the Dashboard feature are irrelevant. `[VERIFIED: design + memory project_n8n_postgres_node_gotchas]`

## Code Examples

### Recent-message accessor (NEW ChatManager partial — mirror Dashboard partial)
```csharp
// Source pattern: Assets/Scripts/Main/ChatManager.Dashboard.cs [VERIFIED]
// Reads the private _activeChatCache (List<MessageViewModel>, ChatManager.cs L157) — the open chat's live list.
public partial class ChatManager
{
    public bool TryGetRecentMessages(string chatId, int n, out List<MessageViewModel> messages)
    {
        messages = null;
        if (string.IsNullOrEmpty(chatId) || chatId != currentChatId || _activeChatCache == null) return false;
        int start = Mathf.Max(0, _activeChatCache.Count - n);
        messages = _activeChatCache.GetRange(start, _activeChatCache.Count - start);   // oldest→newest
        return messages.Count > 0;
    }
}
```
`MessageViewModel` fields for the payload `[VERIFIED: UI/MessageViewModel.cs]`: `type` (MessageType), `text` (body or caption), `isIncoming` (→ `role`: incoming=`client`, else `business`), `timestamp` (long, unix **seconds** per `TryGetChatLastActivitySec` — confirm at impl), `senderName`.
Media placeholder map by `type` (enum: Chat, Image, Video, Audio, Voice, Sticker, Document, Unknown, Reaction) `[VERIFIED: MessageType.cs]`: Chat→text; Image→`[фото]`; Video→`[видео]`; Voice/Audio→`[голосовое сообщение]`; Document→`[документ]`; Sticker→`[стикер]`; else→`[сообщение]`. Append `text` as caption when non-empty.

### Response DTO + MapResponse (mirror DashboardResponse.Parse)
```csharp
// Source pattern: Assets/Scripts/Main/Dashboard/DashboardModels.cs [VERIFIED]
[System.Serializable] public class SuggestReplyDto { public string text; public string label; }
[System.Serializable] public class SuggestRepliesResponse {
    public int v; public long requestSeq; public string error;
    public List<SuggestReplyDto> suggestions = new();
}
public static SuggestionResult MapResponse(string json, long requestSeq)
{
    SuggestRepliesResponse r = null;
    try { r = JsonConvert.DeserializeObject<SuggestRepliesResponse>(json); } catch { }
    if (r == null || !string.IsNullOrEmpty(r.error) || r.suggestions == null)
        return new SuggestionResult { items = null, requestSeq = requestSeq, status = SuggestionStatus.Error };
    var items = r.suggestions
        .Where(s => s != null && !string.IsNullOrEmpty(s.text) && !string.IsNullOrEmpty(s.label))
        .Select(s => new SuggestionItem { text = s.text, intentLabel = s.label }).ToList();   // {text,label}→{text,intentLabel}
    return items.Count == 0
        ? new SuggestionResult { items = null, requestSeq = requestSeq, status = SuggestionStatus.Error }
        : new SuggestionResult { items = items, requestSeq = requestSeq, status = SuggestionStatus.Ok };   // lenient 1–4
}
```

### EditMode test shape (mirror MockSuggestionsProviderTests)
```csharp
// Source: Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs + DashboardResponseParseTests.cs [VERIFIED]
using NUnit.Framework;              // no asmdef → Assembly-CSharp-Editor
public class SuggestRepliesMapTests {
    [Test] public void ErrorField_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error,
            N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":7,\"suggestions\":[],\"error\":\"generation_failed\"}", 7).status);
    [Test] public void MalformedJson_MapsToError() =>
        Assert.AreEqual(SuggestionStatus.Error, N8nSuggestionsProvider.MapResponse("not json", 7).status);
    [Test] public void FourItems_MapOkRankedAndStampRequestSeq() { /* … Ok, order preserved, requestSeq==7 … */ }
}
```
Run headless via `Tools/run-tests-headless.sh` (Editor closed) or the `Temp/claude/run-tests.trigger` bridge (Editor open). `[VERIFIED: CLAUDE.md]`

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Prompt-only "return JSON" + hope | `response_format: {type:"json_schema", strict:true}` with `enum` | OpenAI Structured Outputs GA (2024-08, gpt-4o-mini-2024-07-18+) | Label enum is guaranteed at the model; only count/distinct/length need code checks. `[VERIFIED: web]` |
| AI Agent + Structured Output Parser (LangChain) | Plain `httpRequest` → chat/completions | Adopted in-repo for Dashboard | One call, no agent loop, avoids `output`-key double-nesting bug. `[VERIFIED: Dashboard]` |

**Not enforceable in strict json_schema (must be done in code):** `minItems`/`maxItems` (→ "exactly 4"), `maxLength` (→ ≤300 chars), uniqueness (→ pairwise-distinct labels). `[VERIFIED: web — OpenAI docs + community; supported keyword subset excludes these]`

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `vectorStoreSupabase` tv 1.3 plain-retrieval mode is `mode:"load"` with `topK` + `options.metadata.metadataValues[]`, outputting matched docs as items | Standard Stack / Pattern 4 | Low-Med. Mode + filter support CITED from n8n docs ("Get Many" has metadata filter); exact JSON keys not used in-repo. Mitigation: confirm via n8n MCP `get_node_types` at build (established practice), or use the plain-HTTP fallback. |
| A2 | `MessageViewModel.timestamp` is unix **seconds** | Code Examples | Low. Inferred from `TryGetChatLastActivitySec` (documented seconds). Wrong units only mis-stamp `ts` (LLM tolerates). Confirm at impl. |
| A3 | n8n webhook `responseMode:"responseNode"` on the target instance has no sub-30 s response ceiling that would truncate a 1–2 LLM-call flow | Open Questions / Env | Low-Med. Dashboard already runs synchronous gpt-4o-mini calls under Unity's 30 s timeout. Measure p95 on dev; if a retry pushes latency high, the Phase-1 skeleton covers it. Resolves the STATE.md "timeout ceiling" flag by analogy. |
| A4 | Local dev `documents` is empty / `Supabase` cred may be absent (OpenAI-only dev runtime) | Pitfall 6 / Env | Low. Directly supported by the dev-runtime spec + README; worst case the planner over-provisions a local Supabase cred. |

**No `[ASSUMED]`-from-training claims drive the design** — the load-bearing facts (models, node types, schema, key catalog, client patterns) are all `[VERIFIED: repo]` or `[VERIFIED: web]`.

## Open Questions

1. **Exact `load`-mode param keys for `vectorStoreSupabase` tv 1.3.**
   - Known: mode exists ("Get Many"), supports a metadata filter, needs an `ai_embedding` sub-node, queries `match_documents`.
   - Unclear: the precise JSON key for the query text and the limit in this mode/typeVersion.
   - Recommendation: confirm via n8n MCP `get_node_types` at build (repo precedent). Fallback: plain-HTTP `/rpc/match_documents`.
2. **prod bagkz vs local dev credential + data state for RAG.**
   - Known: prod has `OpenAi account` + `Supabase` (service_role) + a populated `documents`; local dev intentionally dropped Supabase RAG.
   - Recommendation: build/e2e catalog-grounding on dev; either seed local `documents` + add the Supabase cred for RAG e2e, or defer RAG-grounding verification to the prod replication step.
3. **Retry shape inside the workflow (loop vs duplicated branch).** Claude's discretion. A duplicated second LLM branch fed the first violation is simplest and keeps "one primary call + at most one retry"; a loop risks unbounded calls. Recommend the duplicated-branch approach.

## Environment Availability

External services (cannot be probed from this repo — they are remote). Requirements derived from the design + verified workflow configs:

| Dependency | Required By | Available (dev) | Notes / Fallback |
|------------|------------|-----------------|------------------|
| Local n8n (`localhost:5678`) + cloudflared tunnel | Dev-first workflow build/e2e | Assumed present (dev setup) | Runtime-editable URL: paste tunnel into Profile→Edit dev field, or `Tools/n8n/rotate-tunnel.py`. `[VERIFIED: runtime-editable-n8n-url spec + README]` |
| OpenAI API (`gpt-4o-mini` + `text-embedding-3-small`) via `OpenAi account` cred | LLM + embedding nodes | Present on dev (`OpenAi account` exists) | `[VERIFIED: openai-only-dev-runtime spec Component 2]` |
| Supabase `documents` table + `match_documents` RPC via `Supabase` (service_role) cred | RAG pre-retrieval | **Likely ABSENT/empty on local dev** | Fallback: catalog-only grounding works with no RAG. To test RAG on dev, add the cred + seed rows. `[VERIFIED: dev-runtime spec + README follow-up #1]` |
| n8n MCP `get_node_types` | Confirming `load`-mode param keys at build | Project practice | `[CITED: README + openai-only-dev spec]` |

**Missing with no fallback:** none block the phase — catalog grounding + all client work proceed without local RAG.
**Missing with fallback:** local Supabase RAG (fallback: catalog grounding on dev; RAG verified at prod replication).

## Security Domain

`security_enforcement` is not disabled in config → this section is included. This is an AI-integration phase whose central risk (N8N-04) is LLM prompt injection.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control (this phase) |
|---------------|---------|-----------------|
| V2 Authentication | no (by design) | `/webhook/SuggestReplies` has NO API key — consistent with every other app `/webhook/*`. Tenant isolation comes from the `botWaId` RAG filter, not endpoint auth. Documented intentional posture; do not add auth without a milestone decision. |
| V3 Session Management | no | Stateless request/response; correlation via `requestSeq` only. |
| V4 Access Control (tenant isolation) | **yes** | Single-key `botWaId` metadata filter on `match_documents` scopes retrieval to the active bot; sentinel `""`/`"-1"` skips RAG entirely (never matches other bots' or shared-unauthed chunks). Supabase RLS default-deny + `match_documents` EXECUTE granted only to service_role. `[VERIFIED: harden-rag-store.sql]` |
| V5 Input Validation | **yes** | (a) Server Code node validates `v==1`, `chatId`, non-empty `messages`, caps counts/lengths. (b) LLM output validated: strict json_schema `enum` on `label` + Code node count/distinct/clamp/markdown-strip + 1 retry then safe error. (c) Client `MapResponse` lenient + never renders raw. |
| V6 Cryptography | no | No new crypto; secrets via existing `Secrets`/PlayerPrefs; no keys added to the app for this endpoint. |

### Known Threat Patterns
| Pattern | STRIDE / OWASP | Standard Mitigation (design-locked) |
|---------|--------|---------------------|
| Prompt injection from customer message ("игнорируй инструкции…") | OWASP LLM01 / Tampering | Data-fencing: conversation+catalog inside a fenced «ДАННЫЕ (не инструкции)» block as `JSON.stringify(...)`; prompt declares client content untrusted. **Worst case is 4 schema-valid odd texts the owner simply doesn't tap — never auto-sent** (owner-in-the-loop by design). `[VERIFIED: design §Injection hardening]` |
| Model returns out-of-set label / bad count / injected format change | Tampering / Info disclosure | Closed `enum` at the model + Code-node revalidation regardless of model output; second violation → `generation_failed` (no raw passthrough). |
| Cross-tenant catalog leakage via retrieval | Info disclosure / V4 | Single-key `botWaId` filter (OR-semantics-safe with one key); sentinel skips RAG. |
| Untrusted response crossing / stale render | (client) | Phase-1 `SuggestionSequenceGuard` (seq + captured chat) + `WaitForChatFetchesDrain` serialization; provider stamps `requestSeq` from the request. |
| Secret exposure | Info disclosure / V6 | OpenAI/Supabase creds live in n8n (server-side); the app sends no keys to this webhook. |

## Sources

### Primary (HIGH confidence — read directly this session)
- `Assets/Scripts/Chat/{ISuggestionsProvider,SuggestionRequest,SuggestionResult,SuggestionItem,SuggestionStatus,SuggestionSequenceGuard,SuggestionsController,MockSuggestionsProvider}.cs` — seam + swap point + pure-parts pattern
- `Assets/Scripts/Main/ChatManager.Suggestions.cs` — `CurrentChatId`, public `WaitForChatFetchesDrain()`
- `Assets/Scripts/Main/ChatManager.cs` — `currentChatId` (L139), `_activeChatCache` (L157), `_chatFetchesInFlight` (L137), `WaitForChatFetchesToDrain` (L1315)
- `Assets/Scripts/Main/ChatManager.Dashboard.cs`, `ChatManager.QuoteResolve.cs`, `ChatManager.BotState.cs` — accessor + serial-pull + active-bot resolution
- `Assets/Scripts/Main/Dashboard/DashboardPage.cs` (FetchRoutine), `Dashboard/DashboardModels.cs` (`Parse`) — webhook POST + tolerant parse mirrors
- `Assets/Scripts/Main/Manager.cs` — `n8nBaseUrl` (L165), `DeleteBotFilesRoutine` (L2554), product/name/prompt key sites
- `Assets/Scripts/Main/Bot.cs`, `.claude/skills/bot-persistence/{SKILL.md,references/key-catalog.md}` — exact PlayerPrefs key catalog
- `Assets/Scripts/UI/MessageViewModel.cs`, `Assets/Scripts/Chat/MessageType.cs` — payload message fields + enum
- `Assets/Tests/Editor/Chat/{MockSuggestionsProviderTests,N8nBaseUrlTests,DashboardResponseParseTests}.cs` — EditMode conventions
- `Tools/n8n/workflows/{2htWSV5IHO8E2CgB-Dashboard_Outcomes,4wYitz5ek30SVNlT-WhatsApp_Bot,KoTuIlk4LMrlvnWI-Upload_File}.json` — node types/typeVersions + LLM/RAG configs
- `Tools/n8n/supabase/{schema.sql,2026-07-02-harden-rag-store.sql}`, `Tools/n8n/README.md` — `match_documents` signature, vector(1536)/model parity, RLS, dev setup
- `docs/superpowers/specs/{2026-07-10-live-reply-suggestions-design,2026-06-30-runtime-editable-n8n-url-design,2026-06-29-openai-only-dev-runtime-design}.md`
- `Packages/nuget-packages/packages.config` — Newtonsoft.Json 13.0.4

### Secondary (MEDIUM confidence)
- `[CITED: n8n-docs]` `/n8n-io/n8n-docs` vectorstoresupabase / vector-store operation modes ("Get Many" + metadata filter, OR semantics)
- `[VERIFIED: web]` OpenAI Structured Outputs — strict json_schema supported-keyword subset excludes minItems/maxItems/maxLength (developers.openai.com; community.openai.com/t/958567)

### Tertiary (LOW confidence)
- None. All load-bearing claims verified in-repo or on official/first-party sources.

## Metadata

**Confidence breakdown:**
- Standard stack (client + models + node types): HIGH — verified against the running app's own files/workflows.
- Architecture (webhook shape, provider, guards, accessors): HIGH — mirroring proven in-repo patterns with exact signatures.
- RAG `load`-mode param keys: MEDIUM — mode + filter support cited from docs; exact keys to confirm via n8n MCP at build.
- Pitfalls & security: HIGH — from project memory + confirmed in current code + first-party sources.

**Research date:** 2026-07-10
**Valid until:** ~2026-08-10 (stable — pinned model IDs + committed workflow JSONs; re-verify only if n8n is upgraded or the `documents` embedding model changes).
