# Chat Mark-Read Persist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user opens a chat with unread messages, persist read state to Wappi via `POST /api/sync/message/mark/read?profile_id=X&mark_all=true` so the badge does not reappear on the next sync / app restart.

**Architecture:** Three files. `ChatDialog` captures `last_message_id` from the existing `/chats/filter` response. `ChatViewModel` exposes it as `LastMessageId` (with a non-notifying mutator since nothing visual depends on it). `ChatManager` gains a fire-and-forget `MarkChatAsRead` coroutine that mirrors the existing `WappiSendText` POST pattern (line 698) and is invoked from `SelectChat` immediately after the optimistic local reset, gated on `UnreadCount > 0`.

**Tech Stack:** Unity 6000.3.9f1, C#, `UnityWebRequest` POST with `UploadHandlerRaw`, coroutines, `JsonConvert.SerializeObject` for body, `JsonUtility` for the chat-list response model.

**Testing approach:** No automated test harness. Verification is manual in Editor Play mode against a live Wappi profile with unread chats. Each task ends with a Unity compile check and an atomic commit. Final task runs the full open-chat → quit → reopen flow.

---

## File Structure

- **Modify:** `Assets/Scripts/Chat/ChatDialog.cs` — add `last_message_id` field. Single responsibility: serializable model mirroring Wappi response.
- **Modify:** `Assets/Scripts/UI/ChatViewModel.cs` — add `LastMessageId` property, `UpdateLastMessageId` (non-notifying) mutator, and a default-valued ctor parameter.
- **Modify:** `Assets/Scripts/Main/ChatManager.cs` — wire `last_message_id` through `ParseChatsJson` (both paths), add `MarkChatAsRead(string chatId)` coroutine, invoke it from `SelectChat`.

---

## Task 1: Capture `last_message_id` from Wappi in `ChatDialog`

**Why:** Smallest first step — add the field the JSON parser populates. Zero-risk, independently committable.

**Files:**
- Modify: `Assets/Scripts/Chat/ChatDialog.cs`

- [ ] **Step 1: Add the field**

Open `Assets/Scripts/Chat/ChatDialog.cs`. After the existing `public int unread_count;` line, add one line. Final file:

```csharp
using System;

[Serializable]
public class ChatDialog
{
    public string id;
    public bool isGroup;
    public string name;
    public string thumbnail;
    public string last_message_data;
    public string last_timestamp;
    public bool isArchived;
    public int unread_count;
    public string last_message_id;
}
```

Snake_case matches the JSON key character-for-character (`JsonUtility` is case-sensitive).

- [ ] **Step 2: Verify compile**

In Unity, wait for script reload. Console should be clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/ChatDialog.cs
git commit -m "feat(chat): capture last_message_id from Wappi /chats/filter response"
```

---

## Task 2: Expose `LastMessageId` on `ChatViewModel`

**Why:** Surface the field on the UI-facing ViewModel. Use a default-valued ctor parameter so the call site in `ChatManager.ParseChatsJson` keeps compiling between tasks.

**Files:**
- Modify: `Assets/Scripts/UI/ChatViewModel.cs`

- [ ] **Step 1: Add the property, mutator, and constructor parameter**

Open `Assets/Scripts/UI/ChatViewModel.cs`. Make three additions:

**1a — Add property** after the existing `UnreadCount` property:

```csharp
public int UnreadCount { get; private set; }
public string LastMessageId { get; private set; }
```

**1b — Update the constructor signature.** Locate the current constructor:

```csharp
public ChatViewModel(string chatId, string title, string avatarUrl,
                     string lastMessage, long lastTime, int unreadCount = 0)
```

Change it to add the new parameter (with a default) and a body assignment:

```csharp
public ChatViewModel(string chatId, string title, string avatarUrl,
                     string lastMessage, long lastTime, int unreadCount = 0,
                     string lastMessageId = null)
{
    ChatId = chatId;
    Title = title;
    AvatarUrl = avatarUrl;
    LastMessage = lastMessage;
    LastMessageTime = lastTime;
    UnreadCount = unreadCount;
    LastMessageId = lastMessageId;
    LastMessageTimeString = FormatTimestamp(lastTime);
    
    // OnlineStatus = "tap here for contact info";
}
```

**1c — Add the non-notifying mutator** near `UpdateUnreadCount`:

```csharp
public void UpdateLastMessageId(string id)
{
    if (LastMessageId == id) return;
    LastMessageId = id;
    // No NotifyUpdated — metadata for API calls, not visual state.
}
```

Do NOT call `NotifyUpdated()` from `UpdateLastMessageId`. Nothing visual depends on `LastMessageId`, and firing the event would needlessly re-run row repaints.

- [ ] **Step 2: Verify compile**

In Unity, wait for script reload. Console should be clean. The existing call site in `ChatManager.cs:94` (with `unreadCount:` named arg only) still compiles because `lastMessageId` defaults to `null`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/ChatViewModel.cs
git commit -m "feat(chat): expose LastMessageId on ChatViewModel (non-notifying mutator)"
```

---

## Task 3: Add `MarkChatAsRead` coroutine to `ChatManager`

**Why:** The core API integration. Coroutine is a stand-alone unit; testing it requires call-from-`SelectChat` which happens in Task 4. Keeping the coroutine in its own commit lets us code-review the API contract independently.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Wire `last_message_id` through `ParseChatsJson`**

Locate the inner loop of `ParseChatsJson` (around lines 75-100). Currently the create path looks like:

```csharp
var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime, unreadCount: chat.unread_count);
```

Add the new named argument:

```csharp
var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime,
                               unreadCount: chat.unread_count,
                               lastMessageId: chat.last_message_id);
```

And in the merge path, after the existing `existingVm.UpdateUnreadCount(chat.unread_count);` line, add:

```csharp
existingVm.UpdateLastMessageId(chat.last_message_id);
```

- [ ] **Step 2: Add the `MarkChatAsRead` coroutine**

Add this method to `ChatManager`. Place it near the existing message-send POST coroutine (around line 685). Mirror the canonical POST pattern at `ChatManager.cs:698`.

```csharp
/// <summary>
/// Tells Wappi the user has read the given chat. Fire-and-forget — on failure,
/// the next /chats/filter sync corrects any drift.
/// </summary>
private IEnumerator MarkChatAsRead(string chatId)
{
    if (string.IsNullOrEmpty(chatId)) yield break;

    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] No active profile_id; skipping for chat {chatId}.");
        yield break;
    }

    if (!chatLookup.TryGetValue(chatId, out var vm))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] Chat {chatId} not in lookup; skipping.");
        yield break;
    }

    if (string.IsNullOrEmpty(vm.LastMessageId))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] Chat {chatId} has no LastMessageId; skipping.");
        yield break;
    }

    string url = $"https://wappi.pro/api/sync/message/mark/read?profile_id={activeProfileId}&mark_all=true";
    string jsonPayload = JsonConvert.SerializeObject(new { message_id = vm.LastMessageId });

    using UnityWebRequest www = new UnityWebRequest(url, "POST");
    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
    www.downloadHandler = new DownloadHandlerBuffer();
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
    www.timeout = 30;

    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"[ChatManager.MarkChatAsRead] {www.responseCode} {url}: {www.error}\n{www.downloadHandler.text}");
        yield break;
    }

    // Success — server will return unread_count=0 on next sync.
}
```

Notes for the implementer:
- The anonymous-object body `new { message_id = vm.LastMessageId }` works because `JsonConvert` (Newtonsoft) serializes it cleanly. No need for a dedicated request DTO for a single field.
- `Manager.wappiAuthToken` is already used by every other Wappi call in this file. No new auth wiring.
- `GetActiveProfileId()` is a private helper that already exists in `ChatManager`; mirror how `SyncAllChats` uses it.
- `chatLookup` and `vm.LastMessageId` are both safe to access on the main thread (the coroutine continuation is main-thread).

- [ ] **Step 3: Verify compile**

In Unity, wait for script reload. Console should be clean. (The coroutine is defined but not yet called from anywhere — that's Task 4.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): MarkChatAsRead coroutine — POST /message/mark/read?mark_all=true"
```

Verify only `ChatManager.cs` is in the commit: `git show --stat HEAD`. The intentionally-unstaged debug block in the working tree should still appear in `git status`.

---

## Task 4: Invoke `MarkChatAsRead` from `SelectChat`

**Why:** The trigger. After the optimistic local reset, kick off the API call (fire-and-forget) so the server persists the read state. Gated on `UnreadCount > 0` to avoid pointless API traffic.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs`

- [ ] **Step 1: Add the invocation in `SelectChat`**

Locate the existing optimistic-reset block at the top of `SelectChat`:

```csharp
        // Optimistic local reset — match WhatsApp's instant feel.
        // If the next sync returns a non-zero count, the badge re-appears.
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            selectedVm.UpdateUnreadCount(0);
        }
```

Replace it with:

```csharp
        // Optimistic local reset — match WhatsApp's instant feel.
        // If the next sync returns a non-zero count, the badge re-appears.
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            // Persist read state to Wappi so the badge does not re-appear on next sync.
            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }
```

`hadUnread` is captured **before** `UpdateUnreadCount(0)` because the reset clobbers the count locally. We gate the API call on the pre-reset value to avoid hitting `mark/read` for chats that were already at 0 unread (no-op on the server, but wasteful).

- [ ] **Step 2: Verify compile**

In Unity, wait for script reload. Console should be clean.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.cs
git commit -m "feat(chat): persist read state on chat open via MarkChatAsRead"
```

---

## Task 5: Manual verification

**Why:** Confirm the end-to-end behavior matches the spec.

**Files:** None (test only).

- [ ] **Step 1: Pre-conditions**

Connect a Wappi WhatsApp profile that has at least one chat with `unread_count > 0`. Verify the badge is visible on that chat row.

- [ ] **Step 2: Open the chat**

Tap the chat. Badge hides immediately (optimistic reset). Console should show no errors.

- [ ] **Step 3: Verify the API call fired**

In the Unity Console with `Development Build` / `Stack Trace` toggled on, you should see no `[ChatManager.MarkChatAsRead]` error log. If you want to confirm the call, temporarily add a `Debug.Log` line after `if (www.result != UnityWebRequest.Result.Success)` succeeds — remove it before the next commit.

- [ ] **Step 4: Force-quit and reopen**

Force-quit the app (close from app switcher, not just background). Reopen. Navigate to the WhatsApp page. Wait for the chat list to sync.

- [ ] **Step 5: Verify the badge stays hidden**

The previously-opened chat's badge should remain hidden. The server now says `unread_count = 0`.

- [ ] **Step 6: Failure-mode spot check**

Turn airplane mode on. Open another chat with unread. Verify:
- Badge hides locally (optimistic reset works regardless of network).
- Console logs an error from `MarkChatAsRead` (network failure).

Turn airplane mode off. Wait for next sync.
- Expected: the chat's badge remains hidden if the server still has it as unread (which it will, because the airplane-mode call failed) — **wait, this is wrong.** Re-read the spec: on API failure, the next sync re-establishes the real `unread_count`, so the badge SHOULD re-appear if the server still has unread messages. This is correct behavior — the user sees reality.

Verify: badge re-appears on the failed-network chat after next sync. (This is correct — failed write means server still has unread.)

- [ ] **Step 7: No regression on the bump-to-top fix**

Open a chat that is NOT at the top of the list (must have unread). Verify the row does not jump to the top while the chat opens. (This was fixed in commit `709774b`; ensure this task didn't reintroduce the bug.)

- [ ] **Step 8: Done**

No commit needed for Task 5 — it's verification only.

---

## Self-Review Notes

Spec coverage check:

| Spec section | Implemented in |
|---|---|
| Add `last_message_id` field to `ChatDialog` | Task 1 |
| Expose `LastMessageId` on `ChatViewModel` with non-notifying mutator | Task 2 |
| Wire `last_message_id` through `ParseChatsJson` (create + merge) | Task 3 Step 1 |
| `MarkChatAsRead` coroutine (POST with `mark_all=true`) | Task 3 Step 2 |
| Invoke from `SelectChat` gated on `UnreadCount > 0` | Task 4 |
| Fire-and-forget — no UI feedback on success/failure | Task 3 Step 2 (return paths log only) |
| Manual verification checklist | Task 5 |

No spec section unmapped. No placeholders. Type and field names consistent across tasks (`last_message_id`, `LastMessageId`, `UpdateLastMessageId`, `MarkChatAsRead`).
