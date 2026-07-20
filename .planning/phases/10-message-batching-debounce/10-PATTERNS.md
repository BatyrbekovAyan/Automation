# Phase 10: Message Batching / Debounce - Pattern Map

**Mapped:** 2026-07-20
**Files analyzed:** 6 (3 new, 3 modified)
**Analogs found:** 6 / 6

Every file in this phase has a strong in-repo analog. The two halves each have a canonical precedent: the **n8n side** copies `Tools/n8n/apply-rag-fixes.py` (the idempotent by-node-name splice over these SAME two bot templates), and the **client side** copies `Assets/Scripts/Chat/OpenChatLivePollGate.cs` (pure injectable-clock gate) + its EditMode test + the `ChatManager.LivePoll.cs` coroutine-poll driver.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Tools/n8n/apply-message-batching.py` (new) | migration/tooling | transform (in-place JSON edit) | `Tools/n8n/apply-rag-fixes.py` | exact |
| `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` (modified) | config (n8n workflow) | event-driven (webhook orchestration) | its own `Suppressed?`→`Input type` splice + Phase-9 gate splice | exact (self) |
| `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` (modified) | config (n8n workflow) | event-driven | WhatsApp template (channel-agnostic Code; tapi base swap) | exact (self) |
| `Assets/Scripts/Chat/IncomingDebounceGate.cs` (new) | utility (pure gate) | event-driven decision (stateful debounce) | `Assets/Scripts/Chat/OpenChatLivePollGate.cs` | role-match (purity/clock exact; stateful shape is new) |
| `Assets/Scripts/Chat/SuggestionsController.cs` (modified) | controller (MonoBehaviour mediator) | event-driven | its own Phase-9 wiring + `ChatManager.LivePoll.cs` coroutine driver | exact (self) |
| `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs` (new) | test | — | `Assets/Tests/Editor/Chat/OpenChatLivePollGateTests.cs` | exact |

---

## Pattern Assignments

### `Tools/n8n/apply-message-batching.py` (migration/tooling, transform)

**Analog:** `Tools/n8n/apply-rag-fixes.py` — the SAME idempotent, by-node-name, in-place edit of BOTH bot templates. Copy its skeleton wholesale; swap the `fix_bot` body for the debounce splice.

**Module header + path resolution + BOT_IDS tuple** (`apply-rag-fixes.py:14-20`):
```python
import json, os, uuid

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WF = os.path.join(REPO, "Tools/n8n/workflows")
BOT_IDS = ("4wYitz5ek30SVNlT-WhatsApp_Bot.json", "4VN3gsFaC2HUYmcc-Telegram_Bot.json")
```

**load / save (preserve `indent=2, ensure_ascii=False`, NO trailing newline) + find helper** (`apply-rag-fixes.py:23-39`):
```python
def load(fname):
    with open(os.path.join(WF, fname), encoding="utf-8") as f:
        return json.load(f)

def save(fname, wf):
    with open(os.path.join(WF, fname), "w", encoding="utf-8") as f:
        json.dump(wf, f, indent=2, ensure_ascii=False)  # match source: no trailing newline

def find(nodes, name=None, type_suffix=None):
    for n in nodes:
        if name is not None and n["name"] == name:
            return n
        if type_suffix is not None and n["type"].endswith(type_suffix):
            return n
    return None
```

**Idempotent node-add idiom — stable uuid5 id, position offset, `is None` guard** (`apply-rag-fixes.py:113-122`). Reuse for the 4 new nodes (`Debounce Wait`, `Fetch Recent`, `Latest+Combine`, `Is Latest?`); key each uuid5 off `wf["id"] + "-<nodename>"` so re-runs are stable and both templates get distinct ids:
```python
if find(nodes, type_suffix="textSplitterRecursiveCharacterTextSplitter") is None:
    x, y = find(nodes, name="Data Loader")["position"]
    nodes.append({
        "parameters": {"chunkSize": 1000, "chunkOverlap": 150, "options": {}},
        "type": "@n8n/n8n-nodes-langchain.textSplitterRecursiveCharacterTextSplitter",
        "typeVersion": 1,
        "position": [x, y + 220],
        "id": str(uuid.uuid5(uuid.NAMESPACE_DNS, wf.get("id", "") + "-splitter")),
        "name": "Recursive Character Text Splitter",
    })
```

**Connection rewire idiom — overwrite a source's `main` branch to re-point an edge** (`apply-rag-fixes.py:109-110`). This is exactly how to delete `Suppressed?` `main[1] → Input type` and re-point it through the new chain:
```python
conns["Extract from PDF"] = {"main": [[{"node": "Merge", "type": "main", "index": 0}]]}
conns["Source Text"] = {"main": [[{"node": "Supabase Vector Store", "type": "main", "index": 0}]]}
```

**main() loop over both templates** (`apply-rag-fixes.py:168-172`):
```python
def main():
    for fname in BOT_IDS:
        wf = load(fname); fix_bot(wf); save(fname, wf); print(f"  fixed {fname}")
    print("done")
```

**Splice-specific note (from RESEARCH, verified against the templates):** in `fix_bot`, after adding the 4 nodes, rewire:
- Remove `Suppressed?` `main[1][0] → Input type` and set `Suppressed?` `main[1] → Debounce Wait`.
- Chain `Debounce Wait → Fetch Recent → Latest+Combine → Is Latest?`.
- `Is Latest?` `main[1]` (FALSE / `abort==false`) → `Input type`; `main[0]` (TRUE / abort) → `[]` (dead-end).
- Edit the `Text` set node value to `={{ $json.combinedText ?? $json.body.messages[0].body }}`.
- The Code node re-emits body: `return [{ json: { ...$('Webhook').first().json, abort, combinedText } }]` (Pitfall 1 — non-negotiable).

---

### `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` + `4VN3gsFaC2HUYmcc-Telegram_Bot.json` (config, event-driven)

**Analog:** the templates' own existing nodes. All 4 node types already exist in both files — clone their exact `typeVersion` and shape for byte-consistency. **Verified current splice coordinates (identical in both templates):**

```
'If'             main[0] -> ['Read Reply Mode']
'Read Reply Mode' main[0] -> ['Suppressed?']          # Phase-9 Postgres gate
'Suppressed?'    main[0] -> []                          # TRUE (semi-auto) = dead-end, no wait
'Suppressed?'    main[1] -> ['Input type']             # FALSE = the edge to REPLACE
'Input type'     main[0] -> ['Text']     main[1] -> ['Download Audio']  main[2..4] -> ['Ask to Send Text']
'Text'           main[0] -> ['AI Agent']
```

**`Fetch Recent` HTTP node — copy the `Mark Read` node verbatim, drop `mark_all`, swap endpoint** (`Mark Read` node, WhatsApp template — `typeVersion 4.2`, cred `EuhhqAaV56DpoqAN`):
```json
{
  "method": "POST",  // Fetch Recent uses "GET" on messages/get
  "url": "https://wappi.pro/api/sync/message/mark/read",
  "authentication": "genericCredentialType",
  "genericAuthType": "httpHeaderAuth",
  "sendQuery": true,
  "queryParameters": { "parameters": [
    { "name": "profile_id", "value": "={{ $('Webhook').item.json.body.messages[0].profile_id }}" },
    { "name": "mark_all", "value": "true" }
  ] },
  "credentials": { "httpHeaderAuth": { "id": "EuhhqAaV56DpoqAN", "name": "WappiAuthToken" } }
}
```
Fetch Recent = `GET https://wappi.pro/api/sync/messages/get` (Telegram: `tapi/sync/messages/get`), query `profile_id` + `chat_id` + `limit=15`, NO `mark_all` (Pitfall 5). Same `genericCredentialType`/`httpHeaderAuth`/cred `EuhhqAaV56DpoqAN`.

**`Debounce Wait` — copy the existing `Reading Pause` Wait node** (`typeVersion 1.1`, plain seconds `amount`):
```json
{ "amount": "={{ $json.wordCount / 2 + 2 }}" }   // Debounce Wait uses a plain: { "amount": 8 }
```

**`Latest+Combine` Code node — copy the `Count Input Words` Code node shape** (`typeVersion 2`; note the `.first()` — NOT `.item` — reach-back to Webhook, which survives the Wait+HTTP paired-item break, Pitfall/Anti-pattern):
```json
{ "jsCode": "...countWords($('Webhook').first().json.body.messages[0].body)..." }
```
The full Code body (channel-agnostic, re-emits `body`) is in RESEARCH.md `Code Examples`. Key anti-patterns baked in: `.first()` not `.item`; sort by `time` desc; `type === 'chat' || type === 'text'` for both channels; `return [{ json: { ...wh, abort, combinedText } }]`.

**`Is Latest?` If node — copy the `Suppressed?` If node** (`typeVersion 2.2`; boolean single-value condition on `={{ $json.abort }}`):
```json
{ "conditions": { "conditions": [ {
  "leftValue": "={{ $json.suppressed }}",   // Is Latest? uses ={{ $json.abort }}
  "operator": { "type": "boolean", "operation": "true", "singleValue": true }
} ], "combinator": "and" } }
```
n8n If output indices: `main[0]` = TRUE, `main[1]` = FALSE (already how `Suppressed?` dead-ends TRUE and proceeds FALSE). So `Is Latest?` `main[0]` (abort true) → dead-end; `main[1]` → `Input type`.

**Channel divergence (both verified):** the Telegram `Input type` text rule already matches `chat` OR `text` (combinator `or`, 2 conditions) — the Code node's `type === 'chat' || type === 'text'` mirrors this, so ONE Code body works in both templates. Telegram `Mark Read`/fetch base is `tapi/sync/...`; `Download Audio`/`Text`/`settings {"executionOrder":"v1"}` are identical.

---

### `Assets/Scripts/Chat/IncomingDebounceGate.cs` (utility, pure gate)

**Analog:** `Assets/Scripts/Chat/OpenChatLivePollGate.cs` for the discipline (pure, UnityEngine-free, injectable clock, named `const` window, rich XML doc). **Caveat:** all four existing gates (`OpenChatLivePollGate`, `DashboardRefreshGate`, `TabRefreshGate`, `WhatsAppSyncGate`) are STATELESS pure functions that take `now`/elapsed as a parameter; `IncomingDebounceGate` is STATEFUL (holds `_deadline` + `_armed` across `Poke`/`ShouldFire`). No existing gate has that stateful holder shape — use the exact class body from RESEARCH.md `Code Examples` (`IncomingDebounceGate` §), applying the analog's conventions below.

**Conventions to copy** (`OpenChatLivePollGate.cs:11-19, 37-48`):
- No namespace (compiles into `Assembly-CSharp`), no `using UnityEngine` (keeps it EditMode-testable with synthetic time).
- A single named tunable `public const float` window with a doc explaining the tradeoff.
```csharp
public static class OpenChatLivePollGate
{
    public const float IntervalSeconds = 3f;   // the tunable "one refresh cycle"
    public static bool ShouldIssue(bool chatIsOpen, bool appFocused, bool fetchInFlight,
        bool chatOpenSettled, float secondsSinceLastPoll)
        => chatIsOpen && appFocused && !fetchInFlight && chatOpenSettled
           && secondsSinceLastPoll >= IntervalSeconds;
}
```
`IncomingDebounceGate` = `sealed class` with `public const float WindowSeconds = 2.5f;`, `Poke(float now)`, `Cancel()`, `bool ShouldFire(float now)` (fires once then disarms). Full body in RESEARCH.md.

---

### `Assets/Scripts/Chat/SuggestionsController.cs` (controller, event-driven — MODIFIED)

**Analog:** the file itself + its Phase-9 edit history (the `PushReplyModeForActiveChat` calls added into `HandleToggle`/`RestoreForActiveChat`). Two coroutine-driver analogs feed the new debounce loop.

**Site 1 — `HandleLive` is the ONLY place that debounces** (`SuggestionsController.cs:183-195`). Replace the direct `IssueRequest` with a `_debounce.Poke(...)` + capture of the pending incoming text; the guards stay:
```csharp
private void HandleLive(List<MessageViewModel> msgs)
{
    if (!_semiAutoOn) return;                              // SEMI-03
    if (msgs == null || !msgs.Exists(m => m != null && m.isIncoming)) return;   // ignore outgoing echoes (Pitfall 7)
    IssueRequest(steerTowardText: null, lastIncomingText: LastIncomingText(msgs));   // <- becomes: _debounce.Poke(Time.time); capture LastIncomingText(msgs)
}

private static string LastIncomingText(List<MessageViewModel> msgs)
{
    for (int i = msgs.Count - 1; i >= 0; i--)
        if (msgs[i] != null && msgs[i].isIncoming) return msgs[i].text;
    return null;
}
```

**Site 2 — `HandleManualRefresh` + `HandleCardTapped` stay IMMEDIATE (do NOT touch)** (`SuggestionsController.cs:170-179, 233-236`):
```csharp
private void HandleCardTapped(string replyText)
{
    // ...composer overwrite...
    IssueRequest(steerTowardText: replyText, lastIncomingText: null);   // INT-04 — immediate, unchanged
}
private void HandleManualRefresh()
{
    if (_semiAutoOn) IssueRequest(steerTowardText: null, lastIncomingText: null);   // INT-03 — immediate, unchanged
}
```

**Site 3 — cancel the pending window in `OnDisable` + `ResetForNoOpenChat`; KEEP the existing `_requestSeq++`** (`SuggestionsController.cs:66-72, 82-88`):
```csharp
void OnDisable()
{
    _requestSeq++;                                         // supersede in-flight (keep) — add: _debounce.Cancel();
    _offsetTween?.Kill();
    if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived -= HandleLive;
    // ...
}
private void ResetForNoOpenChat()
{
    _semiAutoOn = false;
    _requestSeq++;                                         // keep — add: _debounce.Cancel();
    if (_toggle != null) _toggle.SetLit(false);
    HidePanel();
}
```

**Coroutine driver — copy the `while(true)` poll loop from `ChatManager.LivePoll.cs:63-106`.** `SuggestionsController` is a MonoBehaviour, so host the coroutine on itself: `StartCoroutine` in `OnEnable`, `StopCoroutine` in `OnDisable` (existing subscribe/unsubscribe lifecycle sites, lines 60-72). One always-running instance self-gates on `_debounce.ShouldFire`:
```csharp
private IEnumerator OpenChatLivePollRoutine()
{
    while (true)
    {
        yield return new WaitForSecondsRealtime(1f);   // fresh instance each loop (codebase idiom)
        // ...cheap bool checks...
        if (OpenChatLivePollGate.ShouldIssue( ... ))   // <- becomes: if (_debounce.ShouldFire(Time.time))
        {
            // fire the one-shot                        // <- IssueRequest(null, _pendingIncomingText)
        }
    }
}
```
Note: the debounce loop should poll faster than 1s (or use `Time.time` deltas) so the ~2.5s window resolves promptly — Claude's discretion; the structural idiom (self-gating single instance, realtime wait, fire-inside-if) is the copy target.

---

### `Assets/Tests/Editor/Chat/IncomingDebounceGateTests.cs` (test)

**Analog:** `Assets/Tests/Editor/Chat/OpenChatLivePollGateTests.cs` (exact) — same folder, no asmdef (compiles into `Assembly-CSharp-Editor`), no namespace, `using NUnit.Framework;`, expression-bodied `[Test]` methods, references the gate's `const` for boundary tests.

**Structure to copy** (`OpenChatLivePollGateTests.cs:1-16, 44-47`):
```csharp
using NUnit.Framework;

public class OpenChatLivePollGateTests
{
    private const float Interval = OpenChatLivePollGate.IntervalSeconds;

    [Test] public void Fires_WhenAllConditionsHold()
        => Assert.IsTrue(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval + 1f));

    // boundary is inclusive: >= IntervalSeconds fires
    [Test] public void Fires_ExactlyAtIntervalBoundary()
        => Assert.IsTrue(OpenChatLivePollGate.ShouldIssue( ... secondsSinceLastPoll: Interval));
}
```
Because the debounce gate is STATEFUL, tests instantiate `new IncomingDebounceGate()` and drive it with synthetic time (like `DashboardRefreshGateTests.cs:5-12` passes explicit `now` values). Required assertions (from CONTEXT §Specific Ideas): three rapid `Poke(t0), Poke(t0+0.1), Poke(t0+0.2)` → `ShouldFire` false until `t0+0.2+Window`, then true exactly once (`ShouldFire` again → false); `Cancel()` mid-window → never fires; a fresh `Poke` after fire re-arms.

**`DashboardRefreshGateTests.cs` (synthetic-time, 3-assert minimalism):**
```csharp
[Test] public void SkipsWithinInterval()
    => Assert.IsFalse(DashboardRefreshGate.ShouldFetch(1_000_000, 1_030_000)); // 30s < 60s
[Test] public void FetchesAfterInterval()
    => Assert.IsTrue(DashboardRefreshGate.ShouldFetch(1_000_000, 1_061_000));  // 61s > 60s
```

---

## Shared Patterns

### Idempotent by-node-name n8n migration
**Source:** `Tools/n8n/apply-rag-fixes.py` (whole file)
**Apply to:** `apply-message-batching.py` + both `*-Bot.json` edits
`load`/`save` with `indent=2, ensure_ascii=False` (no trailing newline), `find(nodes, name=/type_suffix=)`, `delete_nodes` (prunes dangling connections — `apply-rag-fixes.py:42-56`), stable `uuid5` node ids, `find(...) is None` guards for re-runnability, and the `for fname in BOT_IDS` both-template loop. Editing by NODE NAME (never index) is the invariant — the templates' node order is a guarded contract.

### Pure injectable-clock gate (EditMode-testable seam)
**Source:** `Assets/Scripts/Chat/OpenChatLivePollGate.cs` + `Assets/Scripts/Main/Dashboard/DashboardRefreshGate.cs`
**Apply to:** `IncomingDebounceGate.cs` + `IncomingDebounceGateTests.cs`
No namespace, no `using UnityEngine`, time passed IN as a parameter, single named `const` tunable with a tradeoff doc. The "3 rapid pokes → 1 fire" assertion is ONLY unit-testable because time is injected — do not read `Time.time` inside the gate (the MonoBehaviour driver does that and passes it in).

### Coroutine poll-driver loop
**Source:** `Assets/Scripts/Main/ChatManager.LivePoll.cs:63-106`
**Apply to:** the new debounce driver coroutine in `SuggestionsController.cs`
`while(true) { yield return new WaitForSecondsRealtime(...); <cheap guards>; if (gate.<decision>) <fire>; }` — one self-gating instance for the whole session, fresh `WaitForSecondsRealtime` each loop (avoids the cached-wait reuse gotcha), `StartCoroutine` in `OnEnable` / `StopCoroutine` in `OnDisable`.

### n8n HTTP fetch node + WappiAuthToken credential
**Source:** `Mark Read` node in both `*-Bot.json` (`httpRequest` tv 4.2, cred `EuhhqAaV56DpoqAN` "WappiAuthToken")
**Apply to:** the new `Fetch Recent` node in both templates
`authentication: genericCredentialType` + `genericAuthType: httpHeaderAuth` + `sendQuery` params + the same bound credential id. No new secret, no new endpoint. Fetch Recent drops `mark_all` and uses GET on `messages/get`.

### Owner-run deploy / export / credential-rebind (live-gate plans only)
**Source:** `Tools/n8n/build-set-reply-mode.py` (`api_key()` from `secrets.json`, `req()` with `X-N8N-API-KEY`, `--dry-run`/`--update <id>`/`--export`, activate-on-deploy)
**Apply to:** the `autonomous: false` deploy/runData plan (re-import both templates by literal id, recreate frozen dev clones — Pitfall 6). `secrets.json` is deny-ruled for Claude, so this is owner-run. `apply-message-batching.py` itself only edits the committed JSON; a separate deploy step (owner) imports it.

---

## No Analog Found

None. All 6 files have a strong in-repo analog.

## Metadata

**Analog search scope:** `Tools/n8n/` (10 python scripts, 13 workflow JSONs), `Assets/Scripts/Chat/` (gates, SuggestionsController), `Assets/Scripts/Main/Dashboard/` + `Assets/Scripts/Main/ChatManager.LivePoll.cs` (gate driver), `Assets/Tests/Editor/Chat/` (4 gate tests), `.claude/rules/` (csharp-quality, unity-general, networking).
**Files scanned:** ~14 source files read in full or targeted; both bot templates introspected programmatically for node list + connections + key node parameters.
**Pattern extraction date:** 2026-07-20
</content>
</invoke>
