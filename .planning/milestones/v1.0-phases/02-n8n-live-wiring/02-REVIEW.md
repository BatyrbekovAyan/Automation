---
phase: 02-n8n-live-wiring
reviewed: 2026-07-10T20:18:02Z
depth: standard
files_reviewed: 9
files_reviewed_list:
  - Assets/Scripts/Chat/N8nSuggestionsProvider.cs
  - Assets/Scripts/Chat/SuggestRepliesDtos.cs
  - Assets/Scripts/Chat/SuggestionsController.cs
  - Assets/Scripts/Main/ChatManager.RecentMessages.cs
  - Assets/Tests/Editor/Chat/SuggestRepliesMapTests.cs
  - Assets/Tests/Editor/Chat/SuggestRepliesPayloadTests.cs
  - Tools/n8n/README.md
  - Tools/n8n/build-suggest-replies.py
  - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
findings:
  critical: 0
  warning: 4
  info: 6
  total: 10
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-07-10T20:18:02Z
**Depth:** standard
**Files Reviewed:** 9
**Status:** issues_found

## Summary

Reviewed the Phase-2 live wiring of the Suggest Replies feature: the `N8nSuggestionsProvider` (live `ISuggestionsProvider` implementation), its wire DTOs, the `ChatManager.TryGetRecentMessages` partial accessor, the single-line controller swap, EditMode tests, and the n8n side (build script, committed canonical workflow JSON, README).

**Verified clean:**

- **No hardcoded secrets anywhere.** The committed workflow JSON carries n8n credential *ids* only (`WNHwKWlO2E9OClkA`, `vrywn6AxQMlvbbzC`) — references into the instance's credential store, not secret material, per the documented Dashboard Outcomes precedent. The build script reads `n8nAPIKey` from `secrets.json`/env; the client resolves `Manager.n8nBaseUrl` from Secrets/dev-override (Manager.cs:165-175, trailing slash trimmed).
- **Networking rules honored** (`.claude/rules/networking.md`): coroutine + `UnityWebRequest`, hosted on the always-active `ChatManager.Instance` (provider is a plain C# class, not a MonoBehaviour), `timeout = 30`, explicit `Content-Type: application/json` (libcurl gotcha), `result` checked before parse, `JsonConvert` parsing, `using`-disposed request (Unity runs iterator `finally` blocks on coroutine stop).
- **Single-line swap contract honored:** `git diff 9ef5cbf..HEAD` shows `SuggestionsController.cs` changed on exactly one line (the `Awake` provider construction).
- **PlayerPrefs keys verified against ground truth** (Bot.cs, Manager.cs, `bot-persistence` skill catalog): `Name`, `Prompt`, `BusinessType`, `ProductsNumber`/`ServicesNumber`, and the singular `Product{i}`/`Product{i}Price` item keys are all correct.
- **Drain discipline correct:** the provider only waits on `WaitForChatFetchesDrain()` (bounded 3 s, ChatManager.cs:1316-1320) and never touches `_chatFetchesInFlight` — no deadlock path.
- The empty `catch { }` in `MapResponse` mirrors the established `DashboardModels` tolerant-parse convention (DashboardModels.cs:61) — accepted, not flagged.
- **Injection hardening of the workflow is solid overall** (see IN-06 for the residual surface): untrusted conversation/catalog/RAG content rides only in the JSON-stringified «ДАННЫЕ (не инструкции)» user-role block, a БЕЗОПАСНОСТЬ rule is present, output is pinned by strict `json_schema` with the closed 6-label enum, and the Validate node enforces exactly-4/enum/pairwise-distinct/non-empty/markdown-strip/≤300 with one retry then `generation_failed`. Adversarial e2e (6-case matrix incl. steer injection) passed on dev 2026-07-10 with zero fixes.
- Tests are deterministic pure-static coverage with no reliability issues; `MessageType.Reaction` used by the tests exists in the enum.

**Key concerns:** a chat-switch race in the provider's payload assembly that self-defeats the accessor's designed guard (WR-01), a missing client-side upper bound on suggestion count at the trust boundary (WR-02), invalid webhook requests still paying for 1–2 LLM calls (WR-03), and the deploy tool resolving credentials by type instead of by name as documented (WR-04).

## Warnings

### WR-01: Chat-switch race — provider defeats `TryGetRecentMessages`' designed chat-mismatch guard

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:63`
**Issue:** After the drain, `Run` calls `cm.TryGetRecentMessages(cm.CurrentChatId, ...)`. The accessor's `chatId != currentChatId → return false` check (ChatManager.RecentMessages.cs:20) exists precisely to detect "the chat changed since the request was issued", but passing `cm.CurrentChatId` makes it compare the open chat against itself — it can never fire, despite the comment on line 60 claiming "bail if the chat/bot changed underneath us."

The race window is real and the drain widens it: if the user backs out of chat A (request in flight from a card tap / live message) and opens chat B, chat B's open fetch bumps `_chatFetchesInFlight`, so the drain *waits for chat B's messages to finish loading*, then assembles a payload mixing **chat B's messages** with **chat A's `chatId`, `lastIncomingText`, and `steerTowardText`**. The result is never rendered (the controller's seq bump + `SuggestionSequenceGuard` discard it), but the mixed-context request is still sent — one wasted paid LLM call and a server-side execution log keyed to the wrong chat. (The cross-*bot* variant is already dead: `SetActiveBot` calls `StopAllCoroutines()`, killing the coroutine at the drain yield.)
**Fix:** Scope the fetch to the chat the request was issued for — the accessor then short-circuits to `Empty` exactly as designed:

```csharp
if (cm == null || bot == null || !cm.TryGetRecentMessages(req.chatId, MaxMessages, out var msgs))
{
    cb?.Invoke(Empty(req));
    yield break;
}
```

### WR-02: `MapResponse` has no upper cap — >4 server items render >4 cards

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:224-231`
**Issue:** The contract is "exactly 4 from the server; client maps 1–4 valid items → Ok". `MapResponse` enforces the lower bound (0 → Error) but not the upper one: 5+ valid `{text,label}` items return `Ok` with all of them, and `SuggestionsPanel.RenderCards` (SuggestionsPanel.cs:92-106) instantiates one card per item with no cap — overflowing the fixed-footprint panel. Unreachable with the current workflow (its Validate node guarantees exactly 4), but this mapper is the trust boundary for *any* server response (misbehaving deploy, future server change), and its whole design is tolerance — tolerance should bound both ends.
**Fix:** Cap at 4 after the validity filter, and add a `FiveItems_CappedAtFour_Ok` case to `SuggestRepliesMapTests`:

```csharp
var items = r.suggestions
    .Where(s => s != null && !string.IsNullOrEmpty(s.text) && !string.IsNullOrEmpty(s.label))
    .Take(4)   // enforce the wire contract's upper bound client-side
    .Select(s => new SuggestionItem { text = s.text, intentLabel = s.label })
    .ToList();
```

### WR-03: Workflow pays 1–2 LLM calls for known-invalid requests — no short-circuit on `invalid`

**File:** `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` (Prep node + connections; source: `Tools/n8n/build-suggest-replies.py:399-476`)
**Issue:** `Prep` computes `invalid` (v mismatch / missing `chatId` / empty `messages`), but nothing branches on it. A garbage `POST {}` flows Prep → If skipRag? → Assemble → **LLM** → Validate → (if the output fails validation, **LLM Retry** too) → Build Response, which only *then* returns `generation_failed` because `j.invalid` is true. Every invalid request costs one to two gpt-4o-mini calls (`max_tokens: 700` each) plus seconds of latency — on an unauthenticated webhook (unauthenticated is the established project-wide webhook pattern, which is exactly why known-garbage should be cheap to reject). The adversarial e2e verified the *response* is correct for malformed input, but not that the pipeline avoids paying for it.
**Fix:** In `build_full()`, add an `If invalid?` node right after `Prep`: TRUE → `Build Response` (its existing `j.invalid || !j.ok` check already emits the `generation_failed` payload with the echoed `requestSeq` — no code change needed), FALSE → `If skipRag?`. Rebuild with `--stage full --update 9PTyYcelRQI7bGDb`, re-export the canonical JSON, and carry the fix into the prod bagkz replication.

### WR-04: `resolve_cred` resolves by credential *type*, not *name* as documented — wrong-credential risk on prod

**File:** `Tools/n8n/build-suggest-replies.py:57-72`
**Issue:** The docstring ("preferring the live DB by name"), the module header, and `Tools/n8n/README.md:23` ("Credential ids resolve by name") all promise name resolution, but the query is:

```python
"SELECT id, name FROM credentials_entity WHERE type=? ORDER BY name LIMIT 1"
```

— resolve-by-type, alphabetically-first. On the dev instance with one credential per type this works by luck; on the prod instance (where this script is the designated replication path) a second credential of the same type — e.g. `OpenAi account` plus an older `OpenAi (old)`, or two Supabase projects — silently binds whichever sorts first, pointing the workflow at the wrong account/project with no error.
**Fix:** Match the documented contract — exact-name first, type-only as fallback:

```python
row = con.execute(
    "SELECT id, name FROM credentials_entity WHERE type=? AND name=? LIMIT 1",
    (cred_type, want_name),
).fetchone()
if not row:
    row = con.execute(
        "SELECT id, name FROM credentials_entity WHERE type=? ORDER BY name LIMIT 1",
        (cred_type,),
    ).fetchone()
```

## Info

### IN-01: Fetch-failure log omits the URL

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:89`
**Issue:** `networking.md` requires errors logged with status code *and* URL. The log has the status code and error text only. (`LogWarning` instead of the rule's `LogError` is a reasonable call for a non-fatal suggestions feature — the URL is the actionable gap, especially with the dev tunnel/prod base-URL split.)
**Fix:** `Debug.LogWarning($"[Suggest] fetch failed [{www.responseCode}] {www.url}: {www.error}");`

### IN-02: `Clamp` can split a UTF-16 surrogate pair at the boundary

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:202-206`
**Issue:** `Substring(0, max)` on a string whose char at `max-1` is a high surrogate (emoji — common in WhatsApp text) leaves an unpaired surrogate; `Encoding.UTF8.GetBytes` then emits U+FFFD, so the clamped field ends in `�` on the wire. Cosmetic degradation of the LLM input, no crash.
**Fix:**

```csharp
private static string Clamp(string s, int max)
{
    if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
    int cut = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
    return s.Substring(0, cut);
}
```

### IN-03: `lastIncomingText` is the only unclamped text field in the payload

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:153`
**Issue:** `messages[].text`, `ownerPrompt`, and `catalog` are clamped; `lastIncomingText` passes through verbatim (and the workflow's Prep node also leaves it unclamped — only the derived `queryText` is sliced to 500, and `lastIncomingText` never reaches the prompt). A very long incoming message (e.g. a pasted document) bloats the request body for no benefit.
**Fix:** `lastIncomingText = Clamp(req?.lastIncomingText, MaxTextChars),`

### IN-04: `_mockLatencySeconds` is now dead — expect a CS0414 compiler warning

**File:** `Assets/Scripts/Chat/SuggestionsController.cs:22`
**Issue:** The single-line swap removed the field's only reader (`MockSuggestionsProvider(this, _mockLatencySeconds)`), leaving an assigned-but-never-read serialized field. This is the correct trade-off *now* (the phase constraint forbids other Phase-1 edits, and removing a `[SerializeField]` touches scene serialization) — flagged so it gets cleaned up deliberately, not forgotten.
**Fix:** No action this phase. When the single-line constraint lifts, remove the field together with the `MockSuggestionsProvider` retention decision, and re-save the scene.

### IN-05: Provider coroutine dies silently under `ChatManager.StopAllCoroutines()` — no controller-side timeout

**File:** `Assets/Scripts/Chat/N8nSuggestionsProvider.cs:47` (hosting choice), `Assets/Scripts/Main/ChatManager.BotState.cs:123`, `Assets/Scripts/Main/ChatManager.PrivacyClear.cs:81`
**Issue:** Hosting `Run` on `ChatManager.Instance` means `SetActiveBot` and `ClearAllLocalHistory` (both call `StopAllCoroutines()`) kill an in-flight request; the callback never fires. Both current paths are benign — bot switch fires `OnActiveBotChanged` (panel hidden, seq superseded) *before* the stop, and privacy clear runs from the Profile screen — but the controller has no request timeout, so any future `StopAllCoroutines` while the skeleton is visible leaves it stuck until the user taps manual refresh. The codebase already treats this hazard class explicitly for reactions (ChatManager.ReactionSend.cs:14).
**Fix:** No code change required now. Either document the invariant next to the `StartCoroutine` call ("every StopAllCoroutines caller must hide/supersede the suggestions panel first"), or add a controller-side watchdog (e.g. ~35 s after `ShowSkeleton`, render Error if the seq is still current).

### IN-06: Residual prompt-injection surface — `steerTowardText`/`ownerPrompt` ride in the SYSTEM role

**File:** `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` (Assemble node; source: `Tools/n8n/build-suggest-replies.py:180-184`)
**Issue:** The hardening architecture is otherwise clean (fenced user-role data block, closed-enum strict schema, Validate + retry — see Summary). The one place attacker-*influenceable* text crosses into the system role: `steerTowardText` is a prior model-generated suggestion (second-order — a customer message would have to steer the model into echoing a payload, which the owner then taps), concatenated into the НАПРАВЛЕНИЕ line with «» quoting and a 500-char clamp, but newlines are not stripped, so a multi-line steer visually breaks out of the quoted line. `ownerPrompt` in the system role is owner-authored self-scoped content, consistent with the main bot workflows' Additional Instructions — accepted. Blast radius is bounded by the schema/validation to the 4 card texts the owner reviews before sending; the adversarial e2e steer case passed with zero fixes. Defense-in-depth note, not a vulnerability report.
**Fix:** In `ASSEMBLE_JS`, flatten steer whitespace before interpolation: `String(p.steerTowardText).replace(/\s+/g, ' ').slice(0, 300)`.

---

_Reviewed: 2026-07-10T20:18:02Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
