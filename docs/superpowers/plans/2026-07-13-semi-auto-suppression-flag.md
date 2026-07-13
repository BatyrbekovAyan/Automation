# Semi-Auto Suppression Flag — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the client-side «Вместе» (semi-auto) toggle actually suppress a bot's autonomous n8n auto-reply for that chat, so the bot stands down while the owner reviews suggestions.

**Architecture:** A `reply_mode_flags` row in the existing Supabase Postgres (bot-wide `'*'` default + per-chat override) is read by a gate node at the top of each bot's reply workflow (fail-closed). The Unity app writes the flag through a new shared `/webhook/SetReplyMode` n8n workflow on two existing hooks (bot-default flip, per-chat toggle) plus a re-assert on chat open.

**Tech Stack:** n8n (Webhook / Postgres / If / Code / Respond nodes), Supabase Postgres, Unity C# (`UnityWebRequest` + coroutines, Newtonsoft), EditMode tests via the headless runner, dev n8n at `localhost:5678` + tunnel.

## Global Constraints

- Dev-first: all n8n work on local n8n (`localhost:5678`); prod bagkz stays DORMANT (folds into the one-shot bulk copy). Canonical workflow JSON committed under `Tools/n8n/workflows/`.
- n8n MCP tools are unreliable — do n8n work via the dev REST API (`X-N8N-API-KEY`) with curl/Python, mirroring `Tools/n8n/build-suggest-replies.py`.
- Never touch the «Бот работает/пауза» activation switch (real n8n activate/deactivate) and never activate a per-bot reply workflow outside a test window (real contacts).
- Networking rule: `UnityWebRequest` + coroutine, `using` block, `request.timeout = 30`, explicit `Content-Type: application/json`, check `request.result`, `JsonConvert`. No secrets in code — `Manager.n8nBaseUrl` only, no key sent to `/webhook/*`.
- **Fail-closed:** a genuine Postgres error reading the flag halts the reply (n8n default). Absence of a row → `suppressed=false` → the bot replies (never silence the never-toggled common case).
- Postgres node gotcha (project memory *n8n Postgres node gotchas*): pass scalar query parameters; the node's `queryReplacement` comma-splits list values. One input item per `profileId`.
- Stage every edited `.cs` with its `.meta`. Do not push. Do not commit `Assets/Scenes/Main.unity` (benign Editor churn).
- Identifier match (highest-risk detail): the app must send the same `chatId` string the bot workflow reads at `body.messages[0].from` (`…@c.us` for WhatsApp). Verify format equality in Task 5.

---

### Task 1: `reply_mode_flags` table

**Files:**
- Create: `Tools/n8n/supabase/reply_mode_flags.sql`

**Interfaces:**
- Produces: table `reply_mode_flags(profile_id text, chat_id text default '*', suppressed bool, updated_at timestamptz, pk(profile_id, chat_id))` in the dev Supabase Postgres (the DB behind the existing `Postgres` credential id `1H5xlpFSESU4w6JH`).

- [ ] **Step 1: Write the DDL**

`Tools/n8n/supabase/reply_mode_flags.sql`:
```sql
-- Per-chat / per-bot semi-auto suppression flag.
-- chat_id = '*' is the bot-wide default row; a specific chat_id is a per-chat override.
create table if not exists public.reply_mode_flags (
  profile_id  text        not null,
  chat_id     text        not null default '*',
  suppressed  boolean     not null,
  updated_at  timestamptz not null default now(),
  primary key (profile_id, chat_id)
);

-- match_documents / vector work uses service_role; keep this table service-role only (default-deny RLS).
alter table public.reply_mode_flags enable row level security;
```

- [ ] **Step 2: Apply to dev Postgres and verify**

Apply the DDL through the existing dev `Postgres` credential (a one-off n8n Postgres `executeQuery` run, or the Supabase SQL editor — the Supabase MCP is read-only). Then verify:
```bash
# via the dev n8n Postgres node or psql against the dev Supabase connection:
#   insert a probe row and read it back
```
Expected: `insert into reply_mode_flags(profile_id, suppressed) values ('probe','t');` then `select suppressed from reply_mode_flags where profile_id='probe' and chat_id='*';` returns `t`. Delete the probe row after.

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/supabase/reply_mode_flags.sql
git commit -m "feat(suppression): reply_mode_flags table DDL"
```

---

### Task 2: `Set Reply Mode` webhook workflow + deployer

**Files:**
- Create: `Tools/n8n/build-set-reply-mode.py` (deployer, mirrors `build-suggest-replies.py`)
- Create: `Tools/n8n/workflows/<id>-Set_Reply_Mode.json` (canonical export, id assigned by n8n on create)
- Modify: `Tools/n8n/README.md` (bump workflow count, note the new webhook)

**Interfaces:**
- Produces: live dev webhook `POST /webhook/SetReplyMode`, body `{ profileIds: ["…"], chatId: "*"|"<id>", suppressed: bool }` → upserts one row per `profileId`, returns `{ success: true, written: <n> }`.

- [ ] **Step 1: Author the workflow (deployer script)**

`Tools/n8n/build-set-reply-mode.py` builds and deploys via REST (read `build-suggest-replies.py` for the auth/deploy/export pattern; resolve the `Postgres` credential by NAME). Node graph:
1. **Webhook** — `httpMethod: POST`, `path: SetReplyMode`, `responseMode: responseNode`.
2. **Validate** (Code) — require `body.v`? (no version needed here); require `Array.isArray(body.profileIds) && body.profileIds.length`, `typeof body.chatId === 'string'`, `typeof body.suppressed === 'boolean'`. Drop sentinel ids. Emit ONE item per surviving profileId: `{ profileId, chatId: body.chatId, suppressed: body.suppressed }`. If none valid → emit a single `{ __invalid: true }` item.
3. **If invalid?** (If) — `{{ $json.__invalid === true }}` → true routes straight to **Respond** with `{ success:false, error:"bad_request" }` (no DB write); false → **Upsert**.
4. **Upsert** (Postgres, cred `Postgres`) — `executeQuery`:
   ```sql
   insert into reply_mode_flags (profile_id, chat_id, suppressed, updated_at)
   values ($1, $2, $3, now())
   on conflict (profile_id, chat_id) do update
     set suppressed = excluded.suppressed, updated_at = now();
   ```
   params (scalar, per item): `$1 = {{ $json.profileId }}`, `$2 = {{ $json.chatId }}`, `$3 = {{ $json.suppressed }}`.
5. **Respond** (Respond to Webhook) — `{ success: true, written: {{ $items().length }} }`.

- [ ] **Step 2: Deploy to dev and smoke-test**

Run: `python3 Tools/n8n/build-set-reply-mode.py --deploy`
Then:
```bash
BASE=http://localhost:5678
# default row
curl -s -X POST $BASE/webhook/SetReplyMode -H 'Content-Type: application/json' \
  -d '{"profileIds":["probeP"],"chatId":"*","suppressed":true}'
# override row
curl -s -X POST $BASE/webhook/SetReplyMode -H 'Content-Type: application/json' \
  -d '{"profileIds":["probeP"],"chatId":"7701@c.us","suppressed":true}'
# malformed
curl -s -X POST $BASE/webhook/SetReplyMode -H 'Content-Type: application/json' -d '{"chatId":"*"}'
```
Expected: first two → `{"success":true,"written":1}`; malformed → `{"success":false,"error":"bad_request"}`. Confirm two rows exist for `probeP` in `reply_mode_flags`, then delete them.

- [ ] **Step 3: Export canonical JSON + commit**

Run: `python3 Tools/n8n/build-set-reply-mode.py --export` (writes `Tools/n8n/workflows/<id>-Set_Reply_Mode.json`).
```bash
git add Tools/n8n/build-set-reply-mode.py Tools/n8n/workflows/*-Set_Reply_Mode.json Tools/n8n/README.md
git commit -m "feat(suppression): Set Reply Mode webhook workflow + deployer (dev)"
```

---

### Task 3: gate node in the WhatsApp bot template

**Files:**
- Modify: `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` (add gate; rewire the group-chat `If` true branch)

**Interfaces:**
- Consumes: `reply_mode_flags` (Task 1). Reads `profile_id = $('Webhook').item.json.body.messages[0].profile_id`, `from = $('Webhook').item.json.body.messages[0].from`.
- Produces: an auto-reply that runs only when the resolved flag is not suppressed; suppressed → dead-end (no reply, no mark-read).

- [ ] **Step 1: Add the two gate nodes**

Insert between the existing group-chat `If` (true output) and `Input type`:
- **Read Reply Mode** (Postgres, cred `Postgres`, `executeQuery`) — always returns exactly one row:
  ```sql
  select coalesce(
    (select suppressed from reply_mode_flags
     where profile_id = $1 and chat_id in ($2, '*')
     order by (chat_id = '*')   -- specific chat_id sorts before the '*' default
     limit 1),
    false
  ) as suppressed;
  ```
  params: `$1 = {{ $('Webhook').item.json.body.messages[0].profile_id }}`, `$2 = {{ $('Webhook').item.json.body.messages[0].from }}`.
- **Suppressed?** (If) — condition `{{ $json.suppressed }}` is `true`.

Rewire: group-chat `If` (true) → **Read Reply Mode** → **Suppressed?**; **Suppressed?** false → **Input type** (the existing reply path); **Suppressed?** true → leave unconnected (dead-end: no mark-read, message stays unread). Do NOT set `continueOnFail`/`onError` on **Read Reply Mode** — a genuine error must halt (fail-closed).

- [ ] **Step 2: Redeploy the dev template and structurally verify**

Redeploy the edited template to dev (REST update or `build`-style script). Structural check via execution introspection (pattern from `02-03-SUMMARY`): send a test message for a NON-suppressed chat and confirm `runData` includes `Read Reply Mode`, `Suppressed?`, then the reply path; for a suppressed chat confirm the reply path nodes are ABSENT after `Suppressed?`. (The clone must be active only during the test window, then deactivated — real contacts.)

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
git commit -m "feat(suppression): WhatsApp bot template reply-mode gate (fail-closed)"
```

---

### Task 4: gate node in the Telegram bot template

**Files:**
- Modify: `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` (same gate)

**Interfaces:**
- Consumes: `reply_mode_flags` (Task 1). Same query, same params — the table is channel-agnostic (keyed by `profile_id`, which is per-channel).

- [ ] **Step 1: Add the identical gate**

Insert **Read Reply Mode** (Postgres, same query/params as Task 3 Step 1) + **Suppressed?** (If) between the Telegram template's group-chat `If` true output and its reply entry node (the Telegram equivalent of `Input type` — confirm its name in the tapi'd template from Phase 4). Suppressed true → dead-end; false → the reply path. No error-tolerance on the Postgres node (fail-closed).

- [ ] **Step 2: Redeploy + structural verify (dev, real TG profile)**

Same introspection check as Task 3 Step 2 against a dev Telegram profile via tunnel; clone active only during the window, then deactivated.

- [ ] **Step 3: Commit**

```bash
git add Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
git commit -m "feat(suppression): Telegram bot template reply-mode gate (fail-closed)"
```

---

### Task 5: Unity payload builder + Manager sync coroutine

**Files:**
- Create: `Assets/Scripts/Main/Manager.ReplyModeSync.cs` (partial class)
- Test: `Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs`

**Interfaces:**
- Produces:
  - `public static string Manager.BuildReplyModePayload(IReadOnlyList<string> profileIds, string chatId, bool suppressed)` — pure, returns the JSON body.
  - `public static string[] Manager.AuthedProfileIds(Bot bot)` — `{ whatsappProfileId, telegramProfileId }` minus sentinels (`""`, `"-1"`, `Bot.ProfileSentinel`).
  - `public void Manager.SyncReplyMode(string[] profileIds, string chatId, bool suppressed)` — fire-and-forget coroutine POST to `{n8nBaseUrl}/webhook/SetReplyMode`.

- [ ] **Step 1: Write the failing EditMode test**

`Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs`:
```csharp
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReplyModeSyncPayloadTests
{
    [Test]
    public void BuildPayload_DefaultRow_UsesStarChatIdAndSuppressedTrue()
    {
        string json = Manager.BuildReplyModePayload(new[] { "pWA", "pTG" }, "*", true);
        var o = JObject.Parse(json);
        Assert.AreEqual("*", (string)o["chatId"]);
        Assert.IsTrue((bool)o["suppressed"]);
        CollectionAssert.AreEqual(new[] { "pWA", "pTG" }, o["profileIds"].ToObject<string[]>());
    }

    [Test]
    public void BuildPayload_PerChat_CarriesRealChatIdAndFalse()
    {
        string json = Manager.BuildReplyModePayload(new[] { "pWA" }, "7701@c.us", false);
        var o = JObject.Parse(json);
        Assert.AreEqual("7701@c.us", (string)o["chatId"]);
        Assert.IsFalse((bool)o["suppressed"]);
    }

    [Test]
    public void AuthedProfileIds_SkipsSentinelsAndBlanks()
    {
        var bot = new UnityEngine.GameObject("Bot9").AddComponent<Bot>();
        bot.whatsappProfileId = "pWA";
        bot.telegramProfileId = "-1";              // unauthed sentinel
        CollectionAssert.AreEqual(new[] { "pWA" }, Manager.AuthedProfileIds(bot));
        UnityEngine.Object.DestroyImmediate(bot.gameObject);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Tools/run-tests-headless.sh 'ReplyModeSync'` (Editor closed) — or the in-Editor bridge if the Editor is open.
Expected: FAIL — `Manager.BuildReplyModePayload` / `AuthedProfileIds` do not exist.

- [ ] **Step 3: Implement the partial**

`Assets/Scripts/Main/Manager.ReplyModeSync.cs`:
```csharp
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class Manager
{
    [System.Serializable]
    private class ReplyModePayload
    {
        public string[] profileIds;
        public string chatId;
        public bool suppressed;
    }

    /// <summary>Pure JSON builder for POST /webhook/SetReplyMode (EditMode-testable).</summary>
    public static string BuildReplyModePayload(IReadOnlyList<string> profileIds, string chatId, bool suppressed)
    {
        var payload = new ReplyModePayload
        {
            profileIds = profileIds is string[] a ? a : new List<string>(profileIds).ToArray(),
            chatId = chatId,
            suppressed = suppressed
        };
        return JsonConvert.SerializeObject(payload);
    }

    /// <summary>A bot's authed profile ids across channels, minus unauthed sentinels.</summary>
    public static string[] AuthedProfileIds(Bot bot)
    {
        var ids = new List<string>(2);
        if (IsRealProfileId(bot.whatsappProfileId)) ids.Add(bot.whatsappProfileId);
        if (IsRealProfileId(bot.telegramProfileId)) ids.Add(bot.telegramProfileId);
        return ids.ToArray();
    }

    private static bool IsRealProfileId(string id) =>
        !string.IsNullOrEmpty(id) && id != "-1" && id != Bot.ProfileSentinel;

    /// <summary>Fire-and-forget mode-flag write. Lives on Manager (always-alive) like DeleteBotFilesOnServer.</summary>
    public void SyncReplyMode(string[] profileIds, string chatId, bool suppressed)
    {
        if (profileIds == null || profileIds.Length == 0) return;
        StartCoroutine(SyncReplyModeRoutine(BuildReplyModePayload(profileIds, chatId, suppressed)));
    }

    private IEnumerator SyncReplyModeRoutine(string jsonBody)
    {
        string url = $"{n8nBaseUrl}/webhook/SetReplyMode";
        using (var request = new UnityWebRequest(url, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[SetReplyMode {request.responseCode}] {url}: {request.error}");
        }
    }
}
```
> Confirm `Bot.ProfileSentinel` is the actual sentinel constant name (Task 5 read_first: `Bot.cs:64`). If it is `private`, use the literal `"-1"` guard only and drop the constant reference. Confirm `n8nBaseUrl` is the resolved-base accessor used elsewhere in `Manager` (same one `SuggestRepliesUrl`/DashboardOutcomes use).

- [ ] **Step 4: Run tests to verify pass**

Run: `Tools/run-tests-headless.sh 'ReplyModeSync'`
Expected: PASS (3/3). Confirm `Manager.ReplyModeSync.cs.meta` was generated (new-file import) before committing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Manager.ReplyModeSync.cs Assets/Scripts/Main/Manager.ReplyModeSync.cs.meta \
        Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs.meta
git commit -m "feat(suppression): Manager reply-mode sync payload builder + coroutine (+ EditMode tests)"
```

---

### Task 6: wire the two hooks + re-assert on chat open

**Files:**
- Modify: `Assets/Scripts/Main/Manager.ReplyModeSync.cs` (subscribe to `ReplyModeToggleBinder.OnReplyModeChanged`)
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs` (per-chat toggle write + re-assert)

**Interfaces:**
- Consumes: `Manager.SyncReplyMode`, `Manager.AuthedProfileIds` (Task 5); `ChatManager.Instance.ActiveChannel`, `ProfileIdForChannel(bot, channel)`, `CurrentBotId`, `CurrentChatId`; `ReplyModeToggleBinder.OnReplyModeChanged` (static event); `SemiAutoStore.Set`.

- [ ] **Step 1: Bot-default hook — subscribe in Manager**

In `Manager.ReplyModeSync.cs`, add lifecycle subscription (call `HookReplyMode()` from `Manager.OnEnable`, `UnhookReplyMode()` from `OnDisable` — or add these lines to the existing lifecycle methods if the partial can't redeclare them):
```csharp
private void HookReplyMode()   => ReplyModeToggleBinder.OnReplyModeChanged += OnBotReplyModeChanged;
private void UnhookReplyMode()  => ReplyModeToggleBinder.OnReplyModeChanged -= OnBotReplyModeChanged;

private void OnBotReplyModeChanged(string botId, ReplyModeToggleBinder.ReplyMode mode)
{
    Bot bot = FindBotByName(botId);            // same resolver SuggestionsController/N8nSuggestionsProvider use
    if (bot == null) return;
    string[] profiles = AuthedProfileIds(bot);
    SyncReplyMode(profiles, "*", mode == ReplyModeToggleBinder.ReplyMode.Semi);
}
```
> read_first: `Manager.cs` (find the existing `OnEnable`/`OnDisable`; if the partial cannot add its own Unity lifecycle methods without duplicating, add the `Hook/Unhook` calls inside the primary file's existing methods and keep the bodies here). Confirm `FindBotByName` exists on `Manager`/`ChatManager` (it is used by `N8nSuggestionsProvider`).

- [ ] **Step 2: Per-chat hook — in SuggestionsController.HandleToggle**

In `SuggestionsController.HandleToggle`, immediately after the existing `SemiAutoStore.Set(...)` line (currently ~L109), add the server write scoped to the open chat's channel profile:
```csharp
SemiAutoStore.Set(ChatManager.Instance.CurrentBotId, ChatManager.Instance.CurrentChatId, desiredOn);   // (existing)
PushReplyModeForActiveChat(desiredOn);   // (new) mirror to server
```
Add the helper:
```csharp
private void PushReplyModeForActiveChat(bool suppressed)
{
    var cm = ChatManager.Instance;
    if (cm == null || Manager.Instance == null) return;
    Bot bot = Manager.Instance.FindBotByName(cm.CurrentBotId);
    if (bot == null) return;
    string profileId = cm.ProfileIdForChannel(bot, cm.ActiveChannel);   // open chat's channel profile
    if (string.IsNullOrEmpty(profileId) || profileId == "-1") return;
    Manager.Instance.SyncReplyMode(new[] { profileId }, cm.CurrentChatId, suppressed);
}
```
> read_first: `SuggestionsController.cs` (HandleToggle at ~L105; RestoreForActiveChat at ~L90), `ChatManager.BotState.cs` (confirm `ProfileIdForChannel` signature + that `ProfileIdForChannel`/`ActiveChannel` are public or expose them).

- [ ] **Step 3: Re-assert heal — in RestoreForActiveChat**

In `SuggestionsController.RestoreForActiveChat`, inside the `if (_semiAutoOn)` branch (where it already `ShowPanel()` + `IssueRequest(null, null)`), add `PushReplyModeForActiveChat(true)` so reopening a suppressed chat self-corrects a lost write. (Only re-assert the ON state — a chat restored to semi-auto must be suppressed server-side; OFF chats are healed by their own toggle write.)

- [ ] **Step 4: Verify compile + full suite green**

Run: `Tools/run-tests-headless.sh` (full EditMode suite).
Expected: PASS, 0 failures, fresh `editorAssemblyWrittenUtc` (real recompile). Zero-edit invariant does NOT apply here (this is a deliberate wiring change to Phase-1 files); confirm the only Phase-1 file touched is `SuggestionsController.cs` plus the new Manager partial.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/Manager.ReplyModeSync.cs Assets/Scripts/Chat/SuggestionsController.cs \
        Assets/Scripts/Chat/SuggestionsController.cs.meta
git commit -m "feat(suppression): wire bot-default + per-chat mode writes + re-assert on chat open"
```

---

### Task 7: propagation verify + human dev e2e gate

**Files:**
- Create: `docs/superpowers/plans/2026-07-13-suppression-UAT.md` (owner e2e checklist) — or a phase HUMAN-UAT file if run under GSD.

**Interfaces:**
- Consumes: everything above (live dev workflows + app build).

- [ ] **Step 1: Verify a freshly-created bot inherits the gate**

Create a throwaway bot via the app (or drive the Create orchestrator on dev) and confirm the resulting cloned workflow contains **Read Reply Mode** + **Suppressed?** wired after the group-chat `If` (curl the created workflow JSON via the n8n REST API and grep the node names). This confirms the "clone inherits the template gate" assumption — no orchestrator surgery needed. If ABSENT, the orchestrator's "Get Sample Workflow" is fetching a stale/partial template — fix the template id/fetch, don't hand-edit clones.

- [ ] **Step 2: Author the owner e2e checklist**

Scenarios (owner drives; dev n8n + tunnel up, clone active only during the window):
1. WhatsApp: flip a chat to «Вместе» → customer sends a message → **bot does NOT reply, chat stays unread**; suggestions panel still populates.
2. WhatsApp: flip the same chat back to «Авто» → customer sends → **bot replies**.
3. Bot-wide default: set the bot default to «Вместе» → a **never-opened** chat's incoming message is **not** auto-replied (the `'*'` row).
4. Telegram: repeat 1–2 on a Telegram-authed bot.
5. Fail-closed sanity: with the flag DB reachable, a never-toggled chat replies normally (absence → reply).

- [ ] **Step 3: Commit the checklist**

```bash
git add docs/superpowers/plans/2026-07-13-suppression-UAT.md
git commit -m "docs(suppression): owner e2e UAT checklist"
```

---

## Notes for the executor / GSD mapping

- This plan is self-contained and dev-only; it is **independent of the remaining v1.1 Telegram phases** (the n8n gate is channel-agnostic; the app side uses the already-landed `ChatChannel`/`ProfileIdForChannel` seam). It can run now.
- If tracked under GSD, Tasks 1–2 and 3–4 and 5–6 map naturally to three waves (n8n-data / n8n-templates / unity), with Task 7 a human-verify closeout — importable as a phase whose PLAN files mirror these tasks.
- Deferred (own next design): **message batching/debounce** (combine a multi-fragment customer message into one reply, both auto and suggestions). Pipeline order when it lands: group-chat `If` → suppression gate (this plan) → debounce+combine → agent.
