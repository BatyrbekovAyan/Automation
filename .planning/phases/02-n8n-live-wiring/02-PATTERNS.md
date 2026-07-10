# Phase 2: n8n Live Wiring - Pattern Map

**Mapped:** 2026-07-10
**Files analyzed:** 7 (5 new, 1 modified, 1 new n8n workflow)
**Analogs found:** 7 / 7 (6 exact/strong; 1 partial — n8n RAG `load` mode has no in-repo analog)

This is a **brownfield wiring phase**: nearly every artifact copies a Phase-1 or Dashboard pattern that already exists in the repo. Excerpts below are the exact code an executor should imitate, with file paths + line numbers.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` (NEW) | service (provider) | request-response | `Chat/MockSuggestionsProvider.cs` + `Dashboard/DashboardPage.cs` FetchRoutine | exact (seam) + role-match (network) |
| `Assets/Scripts/Chat/SuggestRepliesDtos.cs` (NEW, DTO names at discretion) | model (DTO) | transform (serialize) | `Dashboard/DashboardModels.cs` | exact |
| `Assets/Scripts/Main/ChatManager.RecentMessages.cs` (NEW partial) | service (accessor) | transform (in-memory read) | `Main/ChatManager.Dashboard.cs` | exact |
| `Assets/Scripts/Chat/SuggestionsController.cs` (MODIFIED — 1 line) | controller | event-driven | itself, L31 (Awake) | N/A (surgical edit) |
| `Tools/n8n/workflows/<newId>-Suggest_Replies.json` (NEW) | config (workflow) | request-response (webhook) | `Dashboard_Outcomes.json` + `WhatsApp_Bot.json` (RAG) | role-match (skeleton) + partial (RAG load mode) |
| `Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs` (NEW) | test | — | `MockSuggestionsProviderTests.cs` | exact |
| `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs` (NEW) | test | — | `DashboardResponseParseTests.cs` | exact |

**Zero-edit constraint (N8N-02):** ONLY the 1 Awake line in `SuggestionsController.cs` may change among Phase-1 files. Editing `SuggestionsPanel`, `SuggestionCard`, `SemiAutoToggle`, the 5 seam DTOs (`ISuggestionsProvider`, `SuggestionRequest`, `SuggestionResult`, `SuggestionItem`, `SuggestionStatus`), or `SuggestionSequenceGuard` is a seam-breach defect.

---

## Pattern Assignments

### `Assets/Scripts/Chat/N8nSuggestionsProvider.cs` (service, request-response)

Plain C# class (NOT a MonoBehaviour) implementing `ISuggestionsProvider`. Three analogs compose it: the mock for the seam + inactive-runner dodge, DashboardPage for the POST coroutine, QuoteResolve for the drain.

**Analog A — seam shape + inactive-runner dodge + pure-parts:** `Assets/Scripts/Chat/MockSuggestionsProvider.cs`

The `Request` entry (L43-54) — copy the `isActiveAndEnabled` guard idea, but note the provider must run on `ChatManager.Instance` (always active), NOT an injected `_runner`:
```csharp
public void Request(SuggestionRequest request, Action<SuggestionResult> callback)
{
    // OnChatSelected fires ~300ms before SlideInToMessages activates the chat panel,
    // so StartCoroutine on the controller would THROW ("game object is inactive").
    if (_runner == null || !_runner.isActiveAndEnabled) { callback?.Invoke(BuildResult(request)); return; }
    _runner.StartCoroutine(RespondAfterLatency(request, callback));
}
```
Pure-parts testability convention (L67 `BuildResult` is pure, called directly by tests; L56-60 the coroutine is the only Unity dep). Mirror this with pure static `BuildPayloadJson(...)` and `MapResponse(json, seq)`.

**Analog B — the webhook-POST-returning-JSON coroutine to copy verbatim:** `Assets/Scripts/Main/Dashboard/DashboardPage.cs` `FetchRoutine` (L127-160):
```csharp
string url  = $"{Manager.n8nBaseUrl}/webhook/DashboardOutcomes";       // → /webhook/SuggestReplies
string body = JsonConvert.SerializeObject(new { profileIds = profiles });
using var req = new UnityWebRequest(url, "POST");
req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
req.downloadHandler = new DownloadHandlerBuffer();
req.SetRequestHeader("Content-Type", "application/json");   // REQUIRED (Pitfall 2)
req.timeout = 30;
yield return req.SendWebRequest();
if (req.result != UnityWebRequest.Result.Success)
{
    Debug.LogWarning($"[Dashboard] fetch failed [{req.responseCode}] {req.error}");  // → map to Error(req)
    yield break;
}
var parsed = DashboardResponse.Parse(req.downloadHandler.text);   // → MapResponse(text, req.requestSeq)
```
Required `using` directives (DashboardPage.cs L1-11): `System`, `System.Collections`, `System.Collections.Generic`, `System.Linq`, `System.Text`, `Newtonsoft.Json`, `UnityEngine`, `UnityEngine.Networking`.

**Analog C — serial-guarded drain before assembling the payload:** the provider must `yield return ChatManager.Instance.WaitForChatFetchesDrain();` (PUBLIC hook — note: no "To") BEFORE reading messages, then assemble. See `ChatManager.QuoteResolve.cs` L104 for how the internal caller uses the private twin:
```csharp
// Defer to in-flight chat-open/sync/pagination fetches so this backfill never races them.
yield return WaitForChatFetchesToDrain();   // provider calls the PUBLIC WaitForChatFetchesDrain() instead
```
The public hook is `ChatManager.Suggestions.cs` L18: `public IEnumerator WaitForChatFetchesDrain() => WaitForChatFetchesToDrain();`. **Never** increment `_chatFetchesInFlight` — the provider only *waits*, it is not a `messages/get` caller (deadlock risk).

**Short-circuit policy (from CONTEXT):** no `ChatManager.Instance` / no active bot / no open chat / 0 messages → `callback?.Invoke(Empty(req))` with NO network call.

**Mapping policy (from CONTEXT):** HTTP failure / malformed JSON / `error` field set / 0 valid items → `Error`; 1–4 valid items → `Ok`. `result.requestSeq` is stamped from the REQUEST, not the server echo.

**`.meta` note:** brand-new `.cs` is silently excluded from compile on a busy refresh (Pitfall 4) — after creating, run Assets → Refresh and verify the sibling `.meta` appeared; stage `.cs` + `.meta` together.

---

### `Assets/Scripts/Chat/SuggestRepliesDtos.cs` (model, transform)

**Analog:** `Assets/Scripts/Main/Dashboard/DashboardModels.cs`

`[Serializable]` public-field DTOs + a static tolerant parse that returns a safe default on garbage. DashboardModels.cs L44-63:
```csharp
[Serializable]
public class DashboardResponse
{
    public bool success;
    public int classified;
    public bool truncated;
    public List<DashboardOutcome> outcomes = new();

    public static DashboardResponse Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var r = JsonConvert.DeserializeObject<DashboardResponse>(json);
            if (r != null) r.outcomes ??= new List<DashboardOutcome>();
            return r;
        }
        catch (JsonException) { return null; }
    }
}
```
Item DTO with public fields (L31-42): `public string profileId; public string chatId; ... public long outcomeAt; // unix ms`. Note the `long` for ms timestamps and `[JsonIgnore]` on computed props.

**Wire contract v1 shape to model (CONTEXT/RESEARCH):**
```csharp
[System.Serializable] public class SuggestReplyDto { public string text; public string label; }
[System.Serializable] public class SuggestRepliesResponse {
    public int v; public long requestSeq; public string error;
    public List<SuggestReplyDto> suggestions = new();
}
```
Request DTO mirrors the seam `SuggestionRequest.cs` (public-field `[System.Serializable]` — "JsonConvert-friendly for the Phase-2 swap") but carries the full v1 payload: `v, requestSeq, profileId, chatId, botWaId, businessTypeId, businessName, ownerPrompt, catalog, steerTowardText, lastIncomingText, messages[≤12 × {role, text, ts}]`. `MapResponse` remaps `{text,label}` → seam `SuggestionItem{text,intentLabel}` (SuggestionItem.cs L6-10). DTOs may be folded into the provider file (Claude's discretion).

---

### `Assets/Scripts/Main/ChatManager.RecentMessages.cs` (service, in-memory read)

**Analog:** `Assets/Scripts/Main/ChatManager.Dashboard.cs`

Additive partial-class accessor over ChatManager's private state — the underlying members stay private. Copy the `partial class ChatManager` + read-private-field shape (Dashboard.cs L1-14):
```csharp
using UnityEngine;
public partial class ChatManager
{
    // Lives in a ChatManager partial so it can read the private chatLookup that callers can't reach.
    public bool TryGetChatTitle(string chatId, out string title)
    {
        title = null;
        if (chatLookup != null && chatLookup.TryGetValue(chatId, out var vm) && vm != null)
        { title = vm.Title; return true; }
        return false;
    }
}
```
The new accessor reads the private `_activeChatCache` (`List<MessageViewModel>`, ChatManager.cs L157) and scopes to the open chat via private `currentChatId` (L139):
```csharp
public bool TryGetRecentMessages(string chatId, int n, out List<MessageViewModel> messages)
{
    messages = null;
    if (string.IsNullOrEmpty(chatId) || chatId != currentChatId || _activeChatCache == null) return false;
    int start = Mathf.Max(0, _activeChatCache.Count - n);
    messages = _activeChatCache.GetRange(start, _activeChatCache.Count - start);   // oldest→newest
    return messages.Count > 0;
}
```
There is already a public `CurrentChatId => currentChatId` accessor in `ChatManager.Suggestions.cs` L11 — reuse it, do not re-expose.

**`MessageViewModel` fields for the payload** (`Assets/Scripts/UI/MessageViewModel.cs`): `type` (MessageType, L9), `text` (body/caption, L10), `isIncoming` (L17 → `role`: incoming=`"client"`, else `"business"`), `timestamp` (L18, `long`, unix **seconds** per `TryGetChatLastActivitySec`), `senderName` (L28).

**Media placeholder map by `type`** (`Assets/Scripts/Chat/MessageType.cs` enum: `Chat, Image, Video, Audio, Voice, Sticker, Document, Unknown, Reaction`): Chat→text; Image→`[фото]`; Video→`[видео]`; Voice/Audio→`[голосовое сообщение]`; Document→`[документ]`; Sticker→`[стикер]`; else→`[сообщение]`. Append `text` as caption when non-empty.

---

### `Assets/Scripts/Chat/SuggestionsController.cs` (controller — MODIFIED, exactly 1 line)

**The single swap — L31 (Awake):**
```csharp
// BEFORE:
_provider = new MockSuggestionsProvider(this, _mockLatencySeconds);   // 'this' = runner; ONLY mock reference
// AFTER:
_provider = new N8nSuggestionsProvider();
```
`_mockLatencySeconds` (L22) becomes an unused serialized field — leave it, no compile error. `MockSuggestionsProvider.cs` stays in the repo (its EditMode tests still reference it). No other line in this file, and no other Phase-1 file, may change (N8N-02). The whole downstream loop (seq guard L142-149, skeleton L131, composer hand-off L153-162) is untouched and keeps working through the seam.

---

### `Tools/n8n/workflows/<newId>-Suggest_Replies.json` (config, webhook request-response)

**Primary analog:** `Tools/n8n/workflows/2htWSV5IHO8E2CgB-Dashboard_Outcomes.json` — the shared always-active webhook skeleton (Webhook → Code → httpRequest json_schema → Code parse → Respond). Committed as a canonical export (12th workflow), `"active": true`, `"settings": { "executionOrder": "v1" }` (Dashboard L406-414).

**Webhook entry (Dashboard L4-20)** — change `path` only:
```jsonc
{ "httpMethod": "POST", "path": "SuggestReplies", "responseMode": "responseNode", "options": {} }
// type "n8n-nodes-base.webhook", typeVersion 2.1
```
Request body arrives as `$json.body.*` in the first Code node.

**Input-validate/normalize Code node (mirror Dashboard "Prep" L22-33)** — `type "n8n-nodes-base.code", typeVersion 2`. Reads `$json.body`, validates `v==1` + `chatId` + non-empty `messages`, derives `queryText = lastIncomingText ?? last client message`, flags `skipRag = botWaId∈{"","-1"} || queryText empty`.

**Branch on skipRag (mirror Dashboard "Has Sessions?" `If` node L60-91)** — `type "n8n-nodes-base.if", typeVersion 2.2`, `combinator "and"`, a single condition. Two `main` outputs (true/false) wired like Dashboard L299-316.

**LLM call — structured JSON via plain httpRequest (mirror Dashboard "Classify" L107-132)** — `type "n8n-nodes-base.httpRequest", typeVersion 4.2`. This is THE pattern for reliable schema-conforming JSON (avoids the AI-Agent `output`-key double-nesting bug):
```jsonc
{
  "method": "POST", "url": "https://api.openai.com/v1/chat/completions",
  "authentication": "predefinedCredentialType", "nodeCredentialType": "openAiApi",
  "sendBody": true, "specifyBody": "json",
  "jsonBody": "={ \"model\":\"gpt-4o-mini\", \"temperature\":0.4, \"max_tokens\":700,
    \"messages\":[ {\"role\":\"system\",\"content\": <RU system prompt + vertical hint> },
                   {\"role\":\"user\",\"content\": {{ JSON.stringify($json.fencedData) }} } ],   // data-fencing (N8N-04)
    \"response_format\":{ \"type\":\"json_schema\", \"json_schema\":{ \"name\":\"reply_suggestions\", \"strict\":true,
      \"schema\":{ \"type\":\"object\", \"additionalProperties\":false, \"required\":[\"suggestions\"],
        \"properties\":{ \"suggestions\":{ \"type\":\"array\", \"items\":{ \"type\":\"object\",
          \"additionalProperties\":false, \"required\":[\"text\",\"label\"], \"properties\":{
            \"text\":{ \"type\":\"string\" },
            \"label\":{ \"type\":\"string\", \"enum\":[\"Ответ\",\"Уточнить\",\"Вариант\",\"К заказу\",\"Отложить\",\"Отказ\"] }
      }}}}}}}}"
}
// credentials: { "openAiApi": { "name": "OpenAi account" } };  Dashboard also sets  "onError": "continueRegularOutput"
```
Dashboard's live `jsonBody` (L114) is the exact escaping/interpolation template to copy — RU system prompt as an escaped string, `{{ JSON.stringify($json.transcript) }}` for the untrusted user content, `response_format.json_schema.strict:true` with an `enum`-constrained field.

**Output-validate Code node (mirror Dashboard "Parse" L134-146)** — `mode "runOnceForEachItem"`, reads `$json.choices?.[0]?.message?.content`, `JSON.parse`, enforces what strict schema CANNOT (count==4, pairwise-distinct labels, ≤300 clamp, markdown strip). Dashboard "Parse" idiom:
```javascript
let outcome = null, summary = '';
try {
  const content = $json.choices?.[0]?.message?.content;
  if (content) {
    const parsed = JSON.parse(content);
    const allowed = ['order_collected','owner_needed','in_dialog','question_closed'];   // → the 6 suggestion labels
    if (allowed.includes(parsed.outcome)) outcome = parsed.outcome;
    if (typeof parsed.summary === 'string') summary = parsed.summary.slice(0, 120);     // → clamp text ≤300
  }
} catch (e) { /* outcome stays null → route to retry/error */ }
```
First violation → ONE retry (Claude's discretion: duplicated second LLM branch fed the violation is simplest); second → `{ suggestions:[], error:"generation_failed" }`.

**Respond (mirror Dashboard "Respond" L249-263)** — `type "n8n-nodes-base.respondToWebhook", typeVersion 1.5`:
```jsonc
{ "respondWith": "json", "responseBody": "={{ $json }}", "options": {} }
```
A "Build Response" Code node (Dashboard L237-248 idiom) assembles `{ v:1, requestSeq, suggestions:[...] }`, always echoing `requestSeq` verbatim (N8N-01).

**Secondary analog — conditional RAG pre-retrieval:** `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` "Supabase Vector Store" (L673-711) + "OpenAI Embedding" (L735-748). typeVersion + credentials + `botWaId` filter to copy; **change `mode` from `retrieve-as-tool` → `load`** (Get Many, plain pre-retrieval — see No Analog Found):
```jsonc
// @n8n/n8n-nodes-langchain.vectorStoreSupabase, typeVersion 1.3
{
  "mode": "retrieve-as-tool",                        // → CHANGE TO "load"  (confirm param keys via n8n MCP)
  "tableName": { "__rl": true, "value": "documents", "mode": "list", "cachedResultName": "documents" },
  "topK": 10,                                         // → design locks topK 5
  "options": { "queryName": "match_documents",
    "metadata": { "metadataValues": [ { "name": "botWaId", "value": "={{ $workflow.id }}" } ] } }
  // → source botWaId from the REQUEST ($json.botWaId), not $workflow.id (this is a shared workflow, not the per-bot template)
}
// credentials: { "supabaseApi": { "name": "Supabase" } }
```
Embedding sub-node (`ai_embedding`) — WhatsApp_Bot L735-748, IDENTICAL in Upload_File.json L49-61 (parity is the point):
```jsonc
// @n8n/n8n-nodes-langchain.embeddingsOpenAi, typeVersion 1.2
{ "options": {}, "model": "text-embedding-3-small" }   // MUST match the index model (documents.embedding is vector(1536))
```
`match_documents` signature (`Tools/n8n/supabase/schema.sql` L31): `match_documents(query_embedding vector, filter jsonb DEFAULT '{}', match_count int DEFAULT 5)`. **Multi-key filter = OR** (schema.sql L26-27) — keep the SINGLE `botWaId` key or you silently widen the tenant scope.

---

### `Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs` (test)

**Analog:** `Assets/Tests/Editor/Chat/MockSuggestionsProviderTests.cs`

No asmdef (compiles into `Assembly-CSharp-Editor`); `using NUnit.Framework;`; a `[SetUp]` + small `Req(...)` factory + focused `[Test]` methods calling the PURE part directly. MockSuggestionsProviderTests.cs L4-28:
```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class MockSuggestionsProviderTests
{
    private MockSuggestionsProvider _provider;
    [SetUp] public void SetUp() => _provider = new MockSuggestionsProvider(null);   // runner=null → no coroutine
    private static SuggestionRequest Req(long seq = 1, string steer = null)
        => new SuggestionRequest { chatId = "c1@c.us", requestSeq = seq, steerTowardText = steer };

    [Test] public void FreshRequest_ReturnsFourRankedOkItems()
    {
        var result = _provider.BuildResult(Req());
        Assert.AreEqual(SuggestionStatus.Ok, result.status);
        Assert.AreEqual(4, result.items.Count);
        Assert.AreEqual("Приветствие", result.items[0].intentLabel);   // best-first lead
    }
}
```
Cover (from CONTEXT): roles mapping, oldest→newest ordering, ≤12 cap, media placeholders, ownerPrompt/catalog truncations, sentinel `botWaId`, steer passthrough, seq. `BuildPayloadJson` returns a JSON string — assert on parsed `JObject`/substring, keeping the test Unity-free.

---

### `Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs` (test)

**Analog:** `Assets/Tests/Editor/Chat/DashboardResponseParseTests.cs`

Tolerant-parse test triplet (success + garbage + null) — DashboardResponseParseTests.cs L1-27:
```csharp
using NUnit.Framework;

public class DashboardResponseParseTests
{
    [Test] public void ParsesOutcomesAndFlags()
    {
        string json = "{\"success\":true,\"classified\":2,\"truncated\":true,\"outcomes\":[" +
            "{\"profileId\":\"p1\",\"chatId\":\"7701@c.us\",\"outcome\":\"order_collected\"," +
            "\"summary\":\"101 роза\",\"outcomeAt\":1700000000000,\"lastMessageAt\":1700000005000}]}";
        var r = DashboardResponse.Parse(json);
        Assert.IsTrue(r.success);
        Assert.AreEqual(OutcomeStatus.OrderCollected, r.outcomes[0].Status);
    }

    [Test] public void NullOrGarbageJsonIsSafe()
    {
        Assert.IsNull(DashboardResponse.Parse(null));
        Assert.IsNull(DashboardResponse.Parse("not json"));
    }
}
```
For `MapResponse` cover (CONTEXT): success mapping + ranking order preserved + `requestSeq` stamped from request; lenient 1–3 items → `Ok`; `error` field set → `Error`; malformed JSON → `Error`; empty `suggestions` → `Error`. RESEARCH gives ready one-liners, e.g.:
```csharp
[Test] public void ErrorField_MapsToError() =>
    Assert.AreEqual(SuggestionStatus.Error,
        N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":7,\"suggestions\":[],\"error\":\"generation_failed\"}", 7).status);
[Test] public void MalformedJson_MapsToError() =>
    Assert.AreEqual(SuggestionStatus.Error, N8nSuggestionsProvider.MapResponse("not json", 7).status);
```

Run headless via `Tools/run-tests-headless.sh` (Editor closed) or the `Temp/claude/run-tests.trigger` bridge (Editor open).

---

## Shared Patterns

### n8n base URL resolution
**Source:** `Assets/Scripts/Main/Manager.cs` L165-175 (`Manager.n8nBaseUrl` static; `ResolveN8nBaseUrl`)
**Apply to:** N8nSuggestionsProvider (build the endpoint)
```csharp
public static string n8nBaseUrl =>
    ResolveN8nBaseUrl(PlayerPrefs.GetString(DevN8nBaseUrlKey, ""), Secrets.Data.n8nBaseUrl);
// resolves runtime override (PlayerPrefs "DevN8nBaseUrl") → secrets.json → Cloud default; trims trailing '/'
```
Build the URL exactly as `$"{Manager.n8nBaseUrl}/webhook/SuggestReplies"`. Do NOT re-read secrets or concat by hand. `ResolveN8nBaseUrl` is already unit-tested (`N8nBaseUrlTests.cs`) — mirror that style if the provider adds URL logic (it should not).

### Networking POST (mandatory shape)
**Source:** `.claude/rules/networking.md` (POST block) + `DashboardPage.cs` L130-135 (live instance)
**Apply to:** N8nSuggestionsProvider
- `using` block/var on the `UnityWebRequest`; `new UnityWebRequest(url, "POST")` with `UploadHandlerRaw(Encoding.UTF8.GetBytes(json))` + `DownloadHandlerBuffer()`
- `SetRequestHeader("Content-Type", "application/json")` — REQUIRED; libcurl otherwise stamps `x-www-form-urlencoded` → n8n mis-parse (Pitfall 2 / memory: project_unity_post_content_type)
- `request.timeout = 30`; check `request.result != UnityWebRequest.Result.Success` before parsing; log `[{responseCode}] {url}: {error}`
- NO `X-N8N-API-KEY` here — `/webhook/*` endpoints are public (consistent with every other app webhook)
- coroutine + `IEnumerator` + `yield return SendWebRequest()` — NEVER async/await in a MonoBehaviour path

### Serial-guarded drain (never bump the counter)
**Source:** `ChatManager.QuoteResolve.cs` L104 (usage) + `ChatManager.cs` L1315-1320 (`WaitForChatFetchesToDrain`, bounded 3s) + `ChatManager.Suggestions.cs` L18 (public hook)
**Apply to:** N8nSuggestionsProvider (before assembling the payload)
```csharp
yield return ChatManager.Instance.WaitForChatFetchesDrain();   // public; provider is a separate class
// NEVER _chatFetchesInFlight++  — provider only waits; it is not a messages/get caller (deadlock risk)
```

### Tolerant JSON parse → safe default
**Source:** `DashboardModels.cs` L52-62 (`DashboardResponse.Parse`)
**Apply to:** `MapResponse` in the provider — `try { JsonConvert.DeserializeObject<T>(...) } catch { }`, return a non-throwing default (here `SuggestionResult{ status = Error }`), never let malformed server JSON surface raw.

### Active-bot resolution + bot-field reads
**Source:** `ChatManager.BotState.cs` L142-147 (`GetActiveProfileId` via `Manager.Instance.FindBotByName(CurrentBotId)`) + `.claude/skills/bot-persistence/references/key-catalog.md`
**Apply to:** the payload assembly in N8nSuggestionsProvider
- Active bot: `Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId)`; `botName = bot.name` (e.g. `"Bot0"`)
- `profileId` = `bot.whatsappProfileId` (public field; `"-1"`/empty = `Bot.UnauthedProfileSentinel`)
- `botWaId` = `bot.whatsappWorkflowId` (public field; **= the n8n workflow id** — RAG chunks are tagged `botWaId = $workflow.id`; `"-1"`/`""` → workflow skips RAG)
- `businessName` = `PlayerPrefs.GetString(botName+"Name","")`; `businessTypeId` = `+"BusinessType"`; `ownerPrompt` = `+"Prompt"` (≤500)
- catalog: count `botName+"ProductsNumber"` / `+"ServicesNumber"` (plural+Number); items `botName+"Product"+i` / `+"Price"` / `+"Description"` (**singular** `Product{i}`; same for `Service{i}`) → `• {name} — {price}` lines (≤1500). CLAUDE.md's `Bot0Products0` is imprecise — the real item key is singular.

### n8n data-fencing against prompt injection (N8N-04)
**Source:** `Dashboard_Outcomes.json` "Classify" L114 — untrusted content injected as `{{ JSON.stringify($json.transcript) }}` in the user message; system prompt owns the instructions.
**Apply to:** the LLM node — conversation + catalog inside a fenced «ДАННЫЕ (не инструкции)» block as serialized JSON; the closed `enum` on `label` + the Code-node revalidation run regardless of model output.

### EditMode test conventions
**Source:** every file under `Assets/Tests/Editor/Chat/` (no asmdef; `using NUnit.Framework;`)
**Apply to:** both new test files — no asmdef (compile into `Assembly-CSharp-Editor`), test the PURE static seams only (`BuildPayloadJson` / `MapResponse`), no Unity objects, no network. After creating, Assets → Refresh and confirm `.meta` (Pitfall 4).

---

## No Analog Found

| File / concern | Role | Data Flow | Reason |
|----------------|------|-----------|--------|
| Supabase Vector Store **`load` ("Get Many") mode** inside `<newId>-Suggest_Replies.json` | config (RAG node) | request-response | The repo uses `@n8n/n8n-nodes-langchain.vectorStoreSupabase` tv 1.3 ONLY in `insert` (Upload_File.json L23) and `retrieve-as-tool` (WhatsApp_Bot.json L675) modes. Plain query-time pre-retrieval (`mode:"load"`) — required because the design forbids an agent tool — has never been exercised here. The node type, `tableName`, `topK`, `queryName`, `metadataValues[botWaId]`, credentials, and the `embeddingsOpenAi` sub-node ARE all copyable from WhatsApp_Bot.json; only the `load`-mode **param keys** (query-text field name, limit key) are unverified. Planner must confirm via n8n MCP `get_node_types` at build time (established project practice), OR use the documented plain-HTTP fallback: OpenAI `POST /v1/embeddings` → Supabase PostgREST `POST /rest/v1/rpc/match_documents` with `{query_embedding, filter, match_count}` as a JSON-array body (do NOT route the 1536-float vector through a Postgres node's `queryReplacement` — it comma-splits list params). |

Everything else has a strong in-repo analog. Note there is no local-dev RAG data (OpenAI-only dev runtime dropped Supabase) — validate **catalog** grounding on dev; defer RAG grounding to prod bagkz replication or seed local `documents` + add the `Supabase` cred.

## Metadata

**Analog search scope:** `Assets/Scripts/Chat/` (seam + provider + DTOs), `Assets/Scripts/Main/` (ChatManager partials + Manager + Bot), `Assets/Scripts/Main/Dashboard/` (fetch + models), `Assets/Scripts/UI/` (MessageViewModel), `Assets/Tests/Editor/Chat/` (EditMode conventions), `Tools/n8n/workflows/` (Dashboard_Outcomes, WhatsApp_Bot, Upload_File), `Tools/n8n/supabase/` (schema), `.claude/skills/bot-persistence/` (key catalog), `.claude/rules/networking.md`.
**Files read (analogs):** 20
**Pattern extraction date:** 2026-07-10
