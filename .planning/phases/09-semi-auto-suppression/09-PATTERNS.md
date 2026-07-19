# Phase 9: Semi-Auto Suppression Flag - Pattern Map

**Mapped:** 2026-07-19
**Files analyzed:** 10 (5 new, 5 modified)
**Analogs found:** 10 / 10 (every file has a live in-repo analog — this phase composes existing patterns, adds no new stack)

Every seam below was cross-checked against RESEARCH.md's Staleness Re-Validation table (C1–C7). Where an analog must be copied *with a correction*, the correction is called out inline so the planner lifts the fixed form, not the stale plan's form.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Assets/Scripts/Main/Manager.ReplyModeSync.cs` (NEW) | service (networking) | request-response (fire-and-forget POST) | `Manager.DeleteBotFilesOnServer` + `DeleteBotFilesRoutine` (`Manager.cs:2834-2859`) | exact |
| `Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs` (NEW) | test | pure transform (JObject) | `SuggestRepliesPayloadTests.cs` | exact |
| `Assets/Scripts/Main/Manager.cs` (MOD) | service (god-object) | n/a — add `partial` keyword | self (`Manager.cs:14`) | in-file (C2) |
| `Assets/Scripts/Main/ChatManager.Channel.cs` (MOD) | model / accessor | request-response (helper) | `GetActiveProfileId` (`ChatManager.BotState.cs:197`) + `ProfileIdForChannel` (`Channel.cs:41`) | exact (C3) |
| `Assets/Scripts/Chat/SuggestionsController.cs` (MOD) | controller (UI mediator) | event-driven | self — `HandleToggle:105` / `RestoreForActiveChat:90` | in-file |
| `Tools/n8n/build-set-reply-mode.py` (NEW) | config / deployer | batch | `Tools/n8n/build-suggest-replies.py` | exact (C5) |
| `Tools/n8n/workflows/<id>-Set_Reply_Mode.json` (NEW) | service (workflow) | request-response | `Suggest_Replies.json` (webhook→Code→If→Respond) + `Delete_File.json` (Postgres upsert + Respond) | exact / role-match |
| `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` (MOD) | config (workflow) | event-driven gate | `Delete_File` Postgres node + `Suggest_Replies` "If invalid?" boolean If | role-match (compose) |
| `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` (MOD) | config (workflow) | event-driven gate | its own WhatsApp twin (byte-identical `If`) | exact |
| `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` (NEW) | migration | DDL | `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql` | exact (C7) |

---

## Pattern Assignments

### `Assets/Scripts/Main/Manager.ReplyModeSync.cs` (service, request-response)

**Analog:** `Manager.DeleteBotFilesOnServer` + `DeleteBotFilesRoutine` (`Manager.cs:2834-2859`) — the canonical fire-and-forget webhook coroutine on the always-alive `Manager` singleton.

**Prerequisite (C2):** `Manager.cs:14` is `public class Manager : MonoBehaviour` — **NOT partial**. This new file is a second partial; add the `partial` keyword to `Manager.cs:14` first or it won't compile. This is the ONLY edit to `Manager.cs`.

**Imports pattern** (from `Manager.cs:1-12` — the file this partial extends already has these; a partial needs only what it references):
```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
```

**Fire-and-forget POST coroutine to copy verbatim** (`Manager.cs:2834-2859`):
```csharp
public void DeleteBotFilesOnServer(string whatsappWorkflowId, string telegramWorkflowId)
{
    bool noWhatsapp = string.IsNullOrEmpty(whatsappWorkflowId) || whatsappWorkflowId == Bot.UnauthedProfileSentinel;
    bool noTelegram = string.IsNullOrEmpty(telegramWorkflowId) || telegramWorkflowId == Bot.UnauthedProfileSentinel;
    if (noWhatsapp && noTelegram) return; // never-authed bot — nothing is tagged server-side

    StartCoroutine(DeleteBotFilesRoutine(...));
}

private IEnumerator DeleteBotFilesRoutine(string botWaId, string botTgId)
{
    string url = $"{n8nBaseUrl}/webhook/DeleteBotFiles";
    string body = JsonConvert.SerializeObject(new { botWaId, botTgId });

    using var request = new UnityWebRequest(url, "POST");
    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
    request.downloadHandler = new DownloadHandlerBuffer();
    request.SetRequestHeader("Content-Type", "application/json");
    request.timeout = 30;
    yield return request.SendWebRequest();

    if (request.result != UnityWebRequest.Result.Success)
        Debug.LogError($"[DeleteBotFiles] [{request.responseCode}] {url}: {request.error}\n{request.downloadHandler?.text}");
}
```
Copy this structure exactly for `SyncReplyMode` + `SyncReplyModeRoutine`. Key invariants the analog encodes: (1) POST to `$"{n8nBaseUrl}/webhook/SetReplyMode"`; (2) `Content-Type: application/json`, **no auth header** (every `/webhook/*` is unauthenticated — R-02-01); (3) `request.timeout = 30`; (4) `using` block; (5) log-only on `!Success` (fire-and-forget — no callback, no retry).

**`n8nBaseUrl` — already `public static`** (`Manager.cs:188-189`), use as-is:
```csharp
public static string n8nBaseUrl =>
    ResolveN8nBaseUrl(PlayerPrefs.GetString(DevN8nBaseUrlKey, ""), Secrets.Data.n8nBaseUrl);
```

**`FindBotByName` — already `public`** (`Manager.cs:32-37`), the bot-default hook uses it:
```csharp
public Bot FindBotByName(string botName)
{
    if (BotsParent == null || string.IsNullOrEmpty(botName)) return null;
    Transform t = BotsParent.transform.Find(botName);
    return t != null ? t.GetComponent<Bot>() : null;
}
```

**Pure static builders (for EditMode testability, mirrors `N8nSuggestionsProvider.BuildPayloadJson`):**
- `BuildReplyModePayload(profileIds, chatId, suppressed)` → `JsonConvert.SerializeObject` of an anonymous/`[Serializable]` DTO. Return the JSON string so tests can `JObject.Parse` it (see test analog below).
- `AuthedProfileIds(Bot bot)` → filter sentinels. **C1 correction:** the constant is `Bot.UnauthedProfileSentinel` (= `"-1"`, `Bot.cs:67`) — the stale plan's `Bot.ProfileSentinel` does not exist. The existing `DeleteBotFilesOnServer` already gates on `Bot.UnauthedProfileSentinel` — mirror it. Bot fields are public: `bot.whatsappProfileId` / `bot.telegramProfileId` (`Bot.cs:69-70`).

**Bot-default hook (from RESEARCH Code Examples, wired to the built-but-unconsumed event):**
```csharp
private void OnBotReplyModeChanged(string botId, ReplyModeToggleBinder.ReplyMode mode)
{
    Bot bot = FindBotByName(botId);
    if (bot == null) return;
    SyncReplyMode(AuthedProfileIds(bot), "*", mode == ReplyModeToggleBinder.ReplyMode.Semi);
}
```
Lifecycle is discretion (C4): `Manager` has no `OnEnable`/`OnDisable`/`OnDestroy` — only `Awake`/`Start` (public) + `OnApplicationQuit` (private). Once partial, this file may declare its OWN `OnEnable`/`OnDisable` to `+=`/`-=` `ReplyModeToggleBinder.OnReplyModeChanged` (no conflict with the primary file), OR subscribe in `Start` and add an `OnDestroy`. Either is safe (static event + singleton).

---

### `Assets/Scripts/Main/ChatManager.Channel.cs` (accessor add — C3)

**Analog:** `GetActiveProfileId` (`ChatManager.BotState.cs:197-203`) is the exact private core; `ProfileIdForChannel` (`Channel.cs:41-42`) is `private static`.

**Why the edit:** `SuggestionsController.PushReplyModeForActiveChat` needs the active channel's profile id, but `ProfileIdForChannel` is `private static` (`Channel.cs:41`) → not callable from `SuggestionsController` (compile error if lifted from the stale plan). `ActiveChannel` IS `public` (`Channel.cs:18`) — that part is fine.

**Existing private resolver to wrap** (`ChatManager.BotState.cs:197-203`):
```csharp
private string GetActiveProfileId()
{
    Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
    if (bot == null) return null;
    string profileId = ProfileIdForChannel(bot, ActiveChannel);
    return IsValidProfileId(profileId) ? profileId : null;   // null on sentinel/empty
}
```

**Add a thin public accessor** (smallest surface — preferred over promoting `ProfileIdForChannel`):
```csharp
/// <summary>The active channel's profile id for the current bot, or null if missing/sentinel.</summary>
public string ActiveChannelProfileId() => GetActiveProfileId();
```
Note: `GetActiveProfileId` already returns null for sentinel/empty via `IsValidProfileId` (`BotState.cs:158-159`, which checks `Bot.UnauthedProfileSentinel`) — so the caller's own sentinel re-check is belt-and-suspenders, not required.

---

### `Assets/Scripts/Chat/SuggestionsController.cs` (controller, event-driven — in-file edits)

**Analog:** the file itself. Two existing call sites get a mirror-to-server line; add one private helper.

**Per-chat write site** — immediately after `SemiAutoStore.Set` (`SuggestionsController.cs:109`):
```csharp
private void HandleToggle(bool desiredOn)
{
    if (ChatManager.Instance == null) return;
    _semiAutoOn = desiredOn;
    SemiAutoStore.Set(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, desiredOn);   // :109
    // NEW: PushReplyModeForActiveChat(desiredOn);
    ...
}
```

**Re-assert (heal) site** — inside the existing `if (_semiAutoOn)` block in `RestoreForActiveChat` (`:95-99`):
```csharp
private void RestoreForActiveChat()
{
    if (ChatManager.Instance == null) return;
    _semiAutoOn = SemiAutoStore.IsOn(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId);
    if (_toggle != null) _toggle.SetLit(_semiAutoOn);
    if (_semiAutoOn)
    {
        ShowPanel();
        IssueRequest(null, null);
        // NEW: PushReplyModeForActiveChat(true);   // heal a lost "back to Авто"; ON-only
    }
    else HidePanel();
}
```

**New private helper (from RESEARCH Code Examples, with the C3 accessor):**
```csharp
private void PushReplyModeForActiveChat(bool suppressed)
{
    var cm = ChatManager.Instance;
    if (cm == null || Manager.Instance == null) return;
    Bot bot = Manager.Instance.FindBotByName(cm.CurrentBotId);
    if (bot == null) return;
    string profileId = cm.ActiveChannelProfileId();   // C3 accessor, wraps GetActiveProfileId()
    if (string.IsNullOrEmpty(profileId) || profileId == Bot.UnauthedProfileSentinel) return;
    Manager.Instance.SyncReplyMode(new[] { profileId }, cm.CurrentChatId, suppressed);
}
```

**Anti-pattern guard (RESEARCH Pitfall 3):** do NOT add any write to `HandleLive` (`:166`) — the 3s open-chat LivePoll fires `OnLiveMessagesReceived` → `HandleLive` (suggestions only). The re-assert belongs ONLY in `RestoreForActiveChat` (fires once per `OnChatSelected`). `CurrentChatId` (`ChatManager.Suggestions.cs:11`, `public string CurrentChatId => currentChatId`) is the canonical chat-id read; it is the SAME string the gate reads at `messages[0].from` for 1:1 chats (RESEARCH Pitfall 1 — verified-equivalent).

---

### `Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs` (test, pure transform)

**Analog:** `SuggestRepliesPayloadTests.cs` — pure JObject assertions on a static builder, no PlayerPrefs, no Unity objects. Lives in `Assets/Tests/Editor/Chat/` with **no asmdef** (compiles into `Assembly-CSharp-Editor`).

**Imports + class shape to copy** (`SuggestRepliesPayloadTests.cs:1-9`):
```csharp
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class ReplyModeSyncPayloadTests   // no [TestFixture] needed; no namespace
{
```

**Payload test idiom** (`SuggestRepliesPayloadTests.cs:34-56`):
```csharp
var o = JObject.Parse(Manager.BuildReplyModePayload(new[] {"pWA","pTG"}, "*", true));
Assert.AreEqual("*", (string)o["chatId"]);
Assert.IsTrue((bool)o["suppressed"]);
// profileIds as JArray:
var ids = (JArray)o["profileIds"];
Assert.AreEqual(2, ids.Count);
```

**Sentinel-filter test (needs a real `Bot` MonoBehaviour — instantiate + `DestroyImmediate`):**
```csharp
var go = new GameObject("Bot9");
var bot = go.AddComponent<Bot>();
bot.whatsappProfileId = "pWA";
bot.telegramProfileId = "-1";                     // Bot.UnauthedProfileSentinel
CollectionAssert.AreEqual(new[] {"pWA"}, Manager.AuthedProfileIds(bot));
Object.DestroyImmediate(go);
```
(`SemiAutoStoreTests.cs` shows the injectable-seam/in-memory idiom if a non-static seam is needed; here the builders are pure static so `JObject.Parse` alone suffices. `Bot.UnauthedProfileSentinel` is `public const` so tests reference it by name.)

**Suite baseline:** 1165/1165 (STATE.md). New tests add to it. Run via `Tools/run-tests-headless.sh -testFilter 'ReplyModeSync'` (Editor closed) or the `Temp/claude/run-tests.trigger` bridge (Editor open).

---

### `Tools/n8n/workflows/<id>-Set_Reply_Mode.json` (service workflow, request-response)

**Analog (graph shape):** `Suggest_Replies.json` — `Webhook → Prep(Code, validate+clamp) → If invalid? → …`. **Analog (Postgres upsert + Respond):** `Delete_File.json`.

**Webhook node** (`Suggest_Replies.json:4-19` / `Delete_File.json:4-19`):
```json
{
  "parameters": { "httpMethod": "POST", "path": "SetReplyMode", "responseMode": "responseNode", "options": {} },
  "type": "n8n-nodes-base.webhook", "typeVersion": 2.1, "name": "Webhook"
}
```

**Validate Code node** — mirror `Suggest_Replies` "Prep" (`Suggest_Replies.json:22-32`, `type: n8n-nodes-base.code`, `typeVersion: 2`). Its jsCode is the V5 input-validation template to copy (require types, drop sentinels, emit ONE item per surviving profileId, set an `invalid` flag). Existing validation shape:
```js
const b = $json.body || {};
let invalid = false;
if (b.v !== 1) invalid = true;
if (typeof b.chatId !== 'string' || !b.chatId) invalid = true;
if (!Array.isArray(b.messages) || b.messages.length === 0) invalid = true;
// ...clamps + returns [{ json: {...} }]
```
For SetReplyMode: require `Array.isArray(profileIds) && length`, `typeof chatId === 'string'`, `typeof suppressed === 'boolean'`; drop sentinel/empty ids; **`return` one `{json:{profileId, chatId, suppressed}}` item per surviving profileId** (Postgres upserts once per item — the list-param comma-split trap is why we fan out to items, RESEARCH Don't-Hand-Roll).

**If invalid? node** — copy `Suggest_Replies` "If invalid?" (`Suggest_Replies.json:34-66`) verbatim; this is ALSO the exact shape for the gate's `Suppressed?` If (boolean condition, `operation: "true"`, `singleValue: true`, `typeValidation: loose`):
```json
{
  "parameters": { "conditions": { "options": { "typeValidation": "loose", "version": 2 },
    "conditions": [ { "leftValue": "={{ $json.invalid }}", "rightValue": "",
      "operator": { "type": "boolean", "operation": "true", "singleValue": true } } ],
    "combinator": "and" } },
  "type": "n8n-nodes-base.if", "typeVersion": 2.2, "name": "If invalid?"
}
```
Invalid → Respond `{success:false, error:"bad_request"}` **before** the upsert (no partial write). Valid → Upsert (Postgres) → Respond `{success:true, ...}`.

**Upsert Postgres node** — mirror `Delete_File` "Delete File Chunks" (`Delete_File.json:21-43`): `resource: "database"`, `operation: "executeQuery"`, `typeVersion: 2.6`, positional `options.queryReplacement`. **C6 correction:** cast the boolean param (`queryReplacement` passes params as TEXT):
```sql
insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at)
values ($1, $2, $3::boolean, now())
on conflict (profile_id, chat_id) do update
  set suppressed = excluded.suppressed, updated_at = now();
-- options.queryReplacement: ={{ $json.profileId }},{{ $json.chatId }},{{ $json.suppressed }}
```
**C5 correction:** bind the Postgres cred by id, not name (two creds are both named "Postgres"). Use `1H5xlpFSESU4w6JH` (the bot-template Chat Memory DB — the same DB the gate reads).

**Respond node** — mirror `Delete_File` "Respond" (`Delete_File.json:45-59`, `type: n8n-nodes-base.respondToWebhook`, `typeVersion: 1.5`):
```json
{ "parameters": { "respondWith": "json",
  "responseBody": "={{ { \"success\": true, \"written\": $json... } }}", "options": {} },
  "type": "n8n-nodes-base.respondToWebhook", "typeVersion": 1.5, "name": "Respond" }
```

---

### `Tools/n8n/build-set-reply-mode.py` (config / deployer, batch)

**Analog:** `Tools/n8n/build-suggest-replies.py` — REST deploy/export against `{BASE}/api/v1`, `X-N8N-API-KEY` from `secrets.json` (`n8nAPIKey`) or env, `--dry-run` / `--update <id>` / `--export` flags, `canonical_payload()` loads the committed JSON verbatim and rebinds ONLY credential ids.

**Structure to mirror** (`build-suggest-replies.py`):
- `req(method, path, body)` (`:132-143`) — `urllib` + `X-N8N-API-KEY` header, `Content-Type: application/json`.
- `canonical_payload()` (`:146-178`) — read committed JSON, rebind creds, return `{name, nodes, connections, settings}`.
- `deploy()` (`:181-211`) — POST-create then `POST /workflows/{id}/activate` (SetReplyMode is a SHARED always-active workflow, like Suggest_Replies — activating on deploy is by design; per-bot activation policy does NOT apply).
- `export_canonical()` (`:214-234`) — GET + write the canonical shape back.

**C5 correction (the load-bearing deviation from the analog):** `resolve_cred()` does `... WHERE type=? AND name=? LIMIT 1` (`:101-104`) — with TWO "Postgres"-named creds this is ambiguous. For this deployer, pass the Postgres cred **id** explicitly (add a `--postgres-cred` flag / `N8N_POSTGRES_CRED_ID` env, defaulting to `1H5xlpFSESU4w6JH`) rather than by-name. The override mechanism already exists in the analog: `CRED_OVERRIDES` short-circuits `resolve_cred` (`:93-97`) — extend it for `postgres`.

---

### `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` + `4VN3gsFaC2HUYmcc-Telegram_Bot.json` (config workflow, event-driven gate)

**The two templates are byte-identical at the insertion point** — author the gate once, paste into both. VERIFIED: same `If` node params, same Chat Memory cred, same single-output wiring.

**The group-chat `If` node** (`WhatsApp_Bot.json:610-641`, identical at `Telegram_Bot.json:624-655`) — this is the node whose output you rewire:
```json
{
  "parameters": { "conditions": { "options": { "caseSensitive": true, "typeValidation": "strict", "version": 2 },
    "conditions": [ { "leftValue": "={{ $json.body.messages[0].from }}",
      "rightValue": "={{ $json.body.messages[0].chatId }}",
      "operator": { "type": "string", "operation": "equals" } } ],
    "combinator": "and" } },
  "type": "n8n-nodes-base.if", "typeVersion": 2.2, "name": "If"
}
```

**Current output wiring — the exact insertion point** (`WhatsApp_Bot.json:990-1000`; `Telegram_Bot.json:1004-1014` is identical):
```json
"If": {
  "main": [
    [ { "node": "Input type", "type": "main", "index": 0 } ]
  ]
}
```
Note there is only ONE output array (`main[0]` = TRUE / 1:1). `main[1]` (FALSE / group) is absent → groups dead-end at the `If` BEFORE the gate. So **the gate only ever runs on 1:1 chats**, where `from == chatId == CurrentChatId` (RESEARCH Pitfall 1). `Webhook.main[0] → If` stays unchanged (`:757-766`).

**Rewire to:** `If.main[0] → Read Reply Mode → Suppressed?`; `Suppressed?(false) → Input type` (existing path, unchanged); `Suppressed?(true) → <unconnected>` (dead-end — no `Mark Read`, stays unread).

**Read Reply Mode node** — a Postgres `executeQuery` node (copy `Delete_File`'s node shape at `Delete_File.json:21-43`: `resource:"database"`, `operation:"executeQuery"`, `typeVersion:2.6`), with the LOCKED resolve query and **NO `continueOnFail`/`onError`** (fail-closed — SUP-04):
```sql
select coalesce(
  (select suppressed from reply_mode_flags
   where profile_id = $1 and chat_id in ($2, '*')
   order by (chat_id = '*')   -- specific chat_id sorts before the '*' default
   limit 1),
  false) as suppressed;
```
```
options.queryReplacement (positional; mirrors how Chat Memory references the webhook):
={{ $('Webhook').item.json.body.messages[0].profile_id }},{{ $('Webhook').item.json.body.messages[0].from }}
```
**Cred:** hardcode Postgres id `1H5xlpFSESU4w6JH` — the SAME cred the existing Chat Memory node already uses (`WhatsApp_Bot.json:603-607`, `Telegram_Bot.json:617-621`), so the gate reads the DB the table lives on (C5 / RESEARCH Pitfall 2).

**Suppressed? node** — the boolean-`true` `If` shape from `Suggest_Replies` "If invalid?" (`Suggest_Replies.json:34-66`, quoted above), reading `{{ $json.suppressed }}`. RESEARCH A1/Pitfall 4: confirm the branch via runData in the structural verify (cast `suppressed::boolean` in the read if runData shows a string).

**The webhook already exposes both params the query needs** (`WhatsApp_Bot.json:31,44`): `$('Webhook').item.json.body.messages[0].profile_id` and `.messages[0].from`. `Mark Read` is downstream of `Input type` (`Input type.main` → … → `Mark Read`), so a `Suppressed?` dead-end correctly never marks the message read.

---

### `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` (migration, DDL)

**Analog:** `Tools/n8n/supabase/2026-07-07-conversation-outcomes.sql` — the default-deny RLS + `revoke` precedent, date-prefixed filename (C7).

**Full DDL/RLS shape to copy** (`2026-07-07-conversation-outcomes.sql:18-38`):
```sql
create table if not exists public.conversation_outcomes ( ... );
create index if not exists conversation_outcomes_profile_idx on public.conversation_outcomes (profile_id);

-- Default-deny RLS: NO policies, strip client-key roles (anon key ships in the mobile app).
-- service_role (n8n Supabase cred) has bypassrls; the Postgres cred is the table owner —
-- both unaffected (non-FORCE RLS exempts the owner).
alter table public.conversation_outcomes enable row level security;
revoke all on table public.conversation_outcomes from anon, authenticated;
```

**For `reply_mode_flags`** (SUP-01 shape, idempotent, `pk(profile_id, chat_id)`, `chat_id default '*'`):
```sql
create table if not exists public.reply_mode_flags (
  profile_id  text not null,
  chat_id     text not null default '*',
  suppressed  boolean not null default false,
  updated_at  timestamptz not null default now(),
  primary key (profile_id, chat_id)
);
alter table public.reply_mode_flags enable row level security;
revoke all on table public.reply_mode_flags from anon, authenticated;
```
File header should mirror the analog's (`:1-6`): "run once via a service-role/postgres connection, idempotent, no policies on purpose." **C5/A3 + RESEARCH Q2 (BLOCKING):** apply the DDL through cred `1H5xlpFSESU4w6JH` (the gate's cred) so the table is visible to the gate — not the other "Postgres" cred `vvRrFiEXzLVqKjOx`. `secrets.json` is deny-ruled for Claude → DDL application is a human/dev task.

---

## Shared Patterns

### Fire-and-forget webhook coroutine (client → server)
**Source:** `Manager.DeleteBotFilesRoutine` (`Manager.cs:2845-2859`) + `.claude/rules/networking.md` (POST block)
**Apply to:** `Manager.ReplyModeSync.cs` (`SyncReplyModeRoutine`)
```csharp
using var request = new UnityWebRequest(url, "POST");
request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
request.downloadHandler = new DownloadHandlerBuffer();
request.SetRequestHeader("Content-Type", "application/json");
request.timeout = 30;
yield return request.SendWebRequest();
if (request.result != UnityWebRequest.Result.Success)
    Debug.LogError($"[SetReplyMode] [{request.responseCode}] {url}: {request.error}");
```
Mandatory (networking.md): coroutine not async/await; `request.timeout = 30`; check `request.result`; `using` block; log status+url. **No auth header** for `/webhook/*`.

### Profile-id sentinel filtering
**Source:** `Bot.UnauthedProfileSentinel` (`Bot.cs:67`), used by `DeleteBotFilesOnServer` (`Manager.cs:2836-2837`) and `ChatManager.IsValidProfileId` (`BotState.cs:158-159`)
**Apply to:** `Manager.AuthedProfileIds` + `SuggestionsController.PushReplyModeForActiveChat`
```csharp
!string.IsNullOrEmpty(id) && id != Bot.UnauthedProfileSentinel   // "-1"
```
C1: it is `Bot.UnauthedProfileSentinel`, NEVER `Bot.ProfileSentinel`.

### n8n Postgres executeQuery + positional params
**Source:** `Delete_File.json:21-43` (`Delete File Chunks` node)
**Apply to:** both bot gates' `Read Reply Mode` + the new workflow's `Upsert`
```json
"resource": "database", "operation": "executeQuery",
"query": "... $1 ... $2 ...",
"options": { "queryReplacement": "={{ ...$1 }},{{ ...$2 }}" }
"type": "n8n-nodes-base.postgres", "typeVersion": 2.6
```
Scalars only, comma-separated (lists comma-split — fan out to items). Cast booleans `$n::boolean` on binds (C6).

### Default-deny RLS on a server-only table
**Source:** `2026-07-07-conversation-outcomes.sql:34-38`
**Apply to:** `reply_mode_flags.sql`
```sql
alter table public.<t> enable row level security;
revoke all on table public.<t> from anon, authenticated;
```
No permissive policies — the n8n Postgres cred is table owner (exempt from non-FORCE RLS). Reinforced by the `supabase-postgres-best-practices` skill (Security & RLS = CRITICAL category).

### Webhook → validate(Code) → If-invalid → Respond (server ingress)
**Source:** `Suggest_Replies.json:4-66` (Webhook + Prep + If invalid?)
**Apply to:** `Set_Reply_Mode.json`
Webhook (`typeVersion 2.1`, `responseMode: responseNode`) → Code (`typeVersion 2`) validates + clamps + sets `invalid` → If (`typeVersion 2.2`, boolean `operation:"true"`) routes invalid → Respond-error BEFORE any DB write.

### REST deployer from committed canonical JSON
**Source:** `build-suggest-replies.py` (`req`/`canonical_payload`/`deploy`/`export_canonical`)
**Apply to:** `build-set-reply-mode.py`
Import the committed JSON verbatim; rebind only cred ids; activate shared workflows on deploy. C5: bind Postgres by explicit id, not by-name.

---

## No Analog Found

None. Every file composes an existing in-repo pattern. Two items are *net-new artifacts* but each still has a direct structural analog:

| File | Role | Data Flow | Note |
|------|------|-----------|------|
| `<id>-Set_Reply_Mode.json` | service (workflow) | request-response | No single workflow does "validate → Postgres upsert → respond", but `Suggest_Replies` (webhook/validate/respond) + `Delete_File` (Postgres + respond) compose it exactly. This is the 13th canonical workflow (README count 12 → 13). |
| gate node pair in bot templates | config gate | event-driven | No existing suppression gate, but the `Read Reply Mode` node = `Delete_File`'s Postgres node shape, and `Suppressed?` = `Suggest_Replies`'s boolean-`true` If. |

---

## Metadata

**Analog search scope:** `Assets/Scripts/Main/` (Manager, ChatManager partials, Bot), `Assets/Scripts/Chat/` (SuggestionsController, SemiAutoStore), `Assets/Scripts/UI/` (ReplyModeToggleBinder), `Assets/Tests/Editor/Chat/`, `Tools/n8n/workflows/`, `Tools/n8n/supabase/`, `Tools/n8n/*.py`
**Files scanned:** 16 (6 Unity source, 2 Unity test, 5 n8n workflow JSON, 1 deployer py, 2 SQL) + `.claude/skills/` + `.claude/rules/`
**Corrections carried from RESEARCH:** C1 (`Bot.UnauthedProfileSentinel`), C2 (`Manager` → `partial`), C3 (public `ActiveChannelProfileId()` accessor), C4 (lifecycle discretion), C5 (Postgres cred by id `1H5xlpFSESU4w6JH`, not by-name), C6 (`$3::boolean` cast), C7 (date-prefixed SQL filename)
**Pattern extraction date:** 2026-07-19
