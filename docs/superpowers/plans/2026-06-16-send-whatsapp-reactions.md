# Send reactions to WhatsApp messages — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. UI-construction tasks must additionally use the `unity-ui-builder` skill; network code must follow `.claude/rules/networking.md`.

**Goal:** Let the user long-press a WhatsApp message, pick an emoji, and send a reaction to Wappi — shown instantly on the bubble.

**Architecture:** Reuse the existing *receive/display* reaction pipeline (`ReactionStore`, `MessageReaction`, `ReactionPillView`, `MessageViewModel.reactions`, `ChatManager.OnMessageReactionsChanged`). Add three new pieces — a pure outgoing-reaction resolver, a `ChatManager.SendReaction` + Wappi `message/reaction` coroutine (optimistic apply, revert on failure), and a long-press gesture that opens a single shared reaction-bar overlay. Plan A ships the quick-six bar (fully usable); Plan B adds the `+` full picker.

**Tech Stack:** Unity 6 C#, UnityWebRequest coroutines, Newtonsoft.Json, TextMeshPro, DOTween, the new Input System (`UnityEngine.InputSystem.Pointer`).

**Spec:** [docs/superpowers/specs/2026-06-16-send-whatsapp-reactions-design.md](../specs/2026-06-16-send-whatsapp-reactions-design.md)

**Confirmed Wappi contract (verified in dashboard):**
```
POST https://wappi.pro/api/sync/message/reaction?profile_id={profileId}
Authorization: {Manager.wappiAuthToken}
Content-Type: application/json
Body:    { "body": "👍", "message_id": "<target stanza id>" }   // body "" removes the reaction; no recipient field
Success: { "status": "done", "message_id": "...", "timestamp": ..., "time": "...", "uuid": "..." }   // gate on status == "done"
```

**Running EditMode tests (per project memory):**
- Editor **closed**: `Tools/run-tests-headless.sh '<testFilter regex>'` → results in `Tools/test-output/`.
- Editor **open**: drop `Temp/claude/run-tests.trigger`, read `Temp/claude/test-summary.json` (Unity must be focused).

**Conventions to honor:** `ChatManager` is a `partial` class (existing partials: `ChatManager.BotState.cs`, `ChatManager.MediaSend.cs`) — add new send code as a new partial file. Stage **both** the `.cs` and its `.meta` when committing (Unity generates the `.meta`). No git worktree (the Unity Editor lock is single-instance).

---

## Plan A — core: long-press → quick-six bar → send

### Task 1: `OutgoingReaction` pure resolver (TDD)

The only unit-testable core logic: given the long-pressed message and the tapped emoji, decide the `ReactionEvent` to apply locally and send. Tapping your current emoji toggles it off (empty emoji = removal); a different emoji replaces it.

**Files:**
- Create: `Assets/Scripts/Chat/OutgoingReaction.cs`
- Test: `Assets/Tests/Editor/Chat/OutgoingReactionTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Tests/Editor/Chat/OutgoingReactionTests.cs
using System.Collections.Generic;
using NUnit.Framework;

public class OutgoingReactionTests
{
    private static MessageViewModel MsgWith(params MessageReaction[] reactions)
        => new MessageViewModel { messageId = "MSG1", chatId = "C@c.us", reactions = new List<MessageReaction>(reactions) };

    private static MessageReaction Me(string emoji)
        => new MessageReaction { emoji = emoji, reactorKey = "me", fromMe = true, time = 1 };

    private static MessageReaction Other(string emoji)
        => new MessageReaction { emoji = emoji, reactorKey = "79991234567@c.us", fromMe = false, time = 1 };

    [Test]
    public void CurrentMyEmoji_NoReactions_ReturnsNull()
        => Assert.IsNull(OutgoingReaction.CurrentMyEmoji(new MessageViewModel { messageId = "M" }));

    [Test]
    public void CurrentMyEmoji_OnlyOthers_ReturnsNull()
        => Assert.IsNull(OutgoingReaction.CurrentMyEmoji(MsgWith(Other("👍"))));

    [Test]
    public void CurrentMyEmoji_HasMine_ReturnsMyEmoji()
        => Assert.AreEqual("❤️", OutgoingReaction.CurrentMyEmoji(MsgWith(Other("👍"), Me("❤️"))));

    [Test]
    public void Resolve_NoExisting_ReturnsAddEvent()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(), "👍", 100);
        Assert.AreEqual("MSG1", ev.targetId);
        Assert.AreEqual("👍", ev.emoji);
        Assert.AreEqual("me", ev.reactorKey);
        Assert.IsTrue(ev.fromMe);
        Assert.IsFalse(ev.IsRemoval);
        Assert.AreEqual(100, ev.time);
    }

    [Test]
    public void Resolve_SameEmoji_ReturnsRemoval()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(Me("👍")), "👍", 100);
        Assert.AreEqual("", ev.emoji);
        Assert.IsTrue(ev.IsRemoval);
    }

    [Test]
    public void Resolve_DifferentEmoji_ReturnsReplace()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(Me("👍")), "❤️", 100);
        Assert.AreEqual("❤️", ev.emoji);
        Assert.IsFalse(ev.IsRemoval);
    }

    [Test]
    public void Resolve_ThenApply_AddsMyReaction()
    {
        var msg = MsgWith(Other("👍"));
        Assert.IsTrue(ReactionStore.ApplyToMessage(msg, OutgoingReaction.Resolve(msg, "❤️", 100)));
        Assert.AreEqual(2, msg.reactions.Count);
        Assert.AreEqual("❤️", OutgoingReaction.CurrentMyEmoji(msg));
    }

    [Test]
    public void Resolve_ThenApply_ToggleOff_RemovesMine()
    {
        var msg = MsgWith(Me("👍"));
        Assert.IsTrue(ReactionStore.ApplyToMessage(msg, OutgoingReaction.Resolve(msg, "👍", 100)));
        Assert.IsNull(OutgoingReaction.CurrentMyEmoji(msg));
        Assert.AreEqual(0, msg.reactions.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run (Editor closed): `Tools/run-tests-headless.sh 'OutgoingReactionTests'`
Expected: FAIL — `OutgoingReaction` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
// Assets/Scripts/Chat/OutgoingReaction.cs
/// <summary>
/// Pure decision logic for the account owner's ("me") outgoing reaction. Given the
/// long-pressed message and the tapped emoji, produces the ReactionEvent to apply
/// locally — whose .emoji is also the body to POST. Tapping the emoji you already
/// reacted with toggles it off (empty emoji == removal); a different emoji replaces it.
/// Static/pure so it is unit-testable without a MonoBehaviour.
/// </summary>
public static class OutgoingReaction
{
    /// <summary>Reactor key the owner's reactions are stored under (matches ReactionParser.ReactorKey).</summary>
    public const string MeReactorKey = "me";

    /// <summary>The emoji the owner currently has on this message, or null if none.</summary>
    public static string CurrentMyEmoji(MessageViewModel target)
    {
        var reactions = target?.reactions;
        if (reactions == null) return null;
        for (int i = 0; i < reactions.Count; i++)
            if (reactions[i] != null && reactions[i].reactorKey == MeReactorKey)
                return reactions[i].emoji;
        return null;
    }

    /// <summary>The event to apply locally and send (empty emoji when toggling the current one off).</summary>
    public static ReactionEvent Resolve(MessageViewModel target, string tappedEmoji, long timeUnix)
    {
        string current = CurrentMyEmoji(target);
        bool toggleOff = !string.IsNullOrEmpty(current) && current == tappedEmoji;
        return new ReactionEvent
        {
            targetId   = target != null ? target.messageId : null,
            emoji      = toggleOff ? "" : tappedEmoji,
            reactorKey = MeReactorKey,
            senderName = "Me",
            fromMe     = true,
            time       = timeUnix
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `Tools/run-tests-headless.sh 'OutgoingReactionTests'`
Expected: PASS — 8 tests green.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/OutgoingReaction.cs Assets/Scripts/Chat/OutgoingReaction.cs.meta \
        Assets/Tests/Editor/Chat/OutgoingReactionTests.cs Assets/Tests/Editor/Chat/OutgoingReactionTests.cs.meta
git commit -m "feat(reactions): pure outgoing-reaction resolver + tests"
```

---

### Task 2: `ChatManager.SendReaction` + Wappi `message/reaction` coroutine

Add the public send entry + network coroutine as a new partial file. Optimistic apply (reuse `ReactionStore.ApplyToMessage` + fire `OnMessageReactionsChanged`), persist via load-edit-save of the disk cache (NOT `_activeChatCache`), revert on failure. Guards: a chat must be open, the message must be server-acknowledged (real stanza id, not a `sending_` temp id / Pending / Failed), and a valid profile must exist.

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.ReactionSend.cs`

Reference signatures already in the codebase (do not redefine): `GetActiveProfileId()` (`ChatManager.BotState.cs:140`), `GetCacheRoot()` (`ChatManager.BotState.cs`), `currentChatId` (`ChatManager.cs:123`), `seenMessageIds` (`ChatManager.cs:38`), `OnMessageReactionsChanged` (`ChatManager.cs:73`), `ChatHistoryCache.LoadHistory/SaveHistory`, `ReactionStore.ApplyToMessage`, `ReactionEvent`, `OutgoingReaction`, `DeliveryStatus`, `Manager.wappiAuthToken`, `Manager.Instance`.

- [ ] **Step 1: Write the implementation**

```csharp
// Assets/Scripts/Main/ChatManager.ReactionSend.cs
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>
    /// Sends (or toggles/removes) the owner's reaction to a message and reflects it
    /// instantly. Mirrors the text-send pattern: optimistic local apply first, then a
    /// background POST that reverts on failure. Runs on Manager.Instance so a mid-send
    /// bot switch (StopAllCoroutines on this object) can't strand the optimistic state.
    /// </summary>
    public void SendReaction(MessageViewModel target, string tappedEmoji)
    {
        if (target == null || string.IsNullOrEmpty(tappedEmoji)) return;
        if (string.IsNullOrEmpty(currentChatId)) return;

        // Can only react to a server-acknowledged message — a Pending/Failed optimistic
        // message still carries a temp id ("sending_…"), not a real Wappi stanza id.
        if (string.IsNullOrEmpty(target.messageId)
            || target.messageId.StartsWith("sending_")
            || target.deliveryStatus == DeliveryStatus.Pending
            || target.deliveryStatus == DeliveryStatus.Failed)
        {
            Debug.LogWarning("[ChatManager] SendReaction ignored: message not yet sent.");
            return;
        }

        string profileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(profileId))
        {
            Debug.LogWarning("[ChatManager] SendReaction aborted: no valid profile for active bot.");
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string priorEmoji = OutgoingReaction.CurrentMyEmoji(target);          // snapshot for revert
        ReactionEvent ev = OutgoingReaction.Resolve(target, tappedEmoji, now);

        // --- INSTANT UI: apply + notify + persist before any network call ---
        string sendCacheRoot = GetCacheRoot();
        if (ReactionStore.ApplyToMessage(target, ev))
        {
            OnMessageReactionsChanged?.Invoke(target);
            PersistReaction(sendCacheRoot, target);
        }

        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(PostReactionRoutine(target, ev.emoji, priorEmoji, profileId, sendCacheRoot, now));
    }

    private IEnumerator PostReactionRoutine(
        MessageViewModel target, string sentEmoji, string priorEmoji,
        string profileId, string sendCacheRoot, long appliedTime)
    {
        string url = $"https://wappi.pro/api/sync/message/reaction?profile_id={profileId}";
        var requestData = new WappiSendReactionRequest { body = sentEmoji, message_id = target.messageId };
        string jsonPayload = JsonConvert.SerializeObject(requestData);

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        bool ok = false;
        if (www.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var response = JsonConvert.DeserializeObject<WappiSendReactionResponse>(www.downloadHandler.text);
                if (response != null && response.status == "done")
                {
                    ok = true;
                    if (!string.IsNullOrEmpty(response.message_id))
                        seenMessageIds.Add(response.message_id);   // ignore our own echo if it ever syncs back
                }
                else
                {
                    Debug.LogWarning($"[Wappi] message/reaction non-done status: {www.downloadHandler.text}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wappi] message/reaction response parse failed: {ex.Message}\n{www.downloadHandler.text}");
            }
        }
        else
        {
            Debug.LogError($"[Wappi] message/reaction failed: {www.error}\n{www.downloadHandler?.text}");
        }

        if (ok) yield break;

        // --- REVERT: restore the reaction state we optimistically changed ---
        var revert = new ReactionEvent
        {
            targetId   = target.messageId,
            emoji      = priorEmoji ?? "",                 // null prior => removal
            reactorKey = OutgoingReaction.MeReactorKey,
            senderName = "Me",
            fromMe     = true,
            time       = appliedTime
        };
        if (ReactionStore.ApplyToMessage(target, revert))
        {
            OnMessageReactionsChanged?.Invoke(target);
            PersistReaction(sendCacheRoot, target);
        }
    }

    /// <summary>
    /// Persists one message's updated reactions into the on-disk history. Load-edit-save
    /// the full cached list (like the text-send path) — never save the rendered-page
    /// list (_activeChatCache), which would truncate the cached history.
    /// </summary>
    private void PersistReaction(string cacheRoot, MessageViewModel target)
    {
        if (target == null || string.IsNullOrEmpty(target.chatId)) return;
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, target.chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].messageId == target.messageId)
            {
                cached[i].reactions = target.reactions;
                ChatHistoryCache.SaveHistory(cacheRoot, target.chatId, cached);
                return;
            }
        }
        // Not in cache (older than the retained window) — skip rather than truncate.
    }
}

[Serializable]
public class WappiSendReactionRequest
{
    public string body;        // the emoji; "" removes the reaction
    public string message_id;  // target message's Wappi stanza id
}

[Serializable]
public class WappiSendReactionResponse
{
    public string status;      // "done" on success
    public string message_id;  // the reaction stanza's own id
    public long timestamp;
    public string time;
    public string uuid;
}
```

- [ ] **Step 2: Verify it compiles**

Recompile in Unity (open Editor) or run the headless test launch (it compiles first):
Run: `Tools/run-tests-headless.sh 'OutgoingReactionTests'`
Expected: PASS (no new tests, but a green run proves `ChatManager.ReactionSend.cs` compiles against the real signatures).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.ReactionSend.cs Assets/Scripts/Main/ChatManager.ReactionSend.cs.meta
git commit -m "feat(reactions): ChatManager.SendReaction + Wappi message/reaction coroutine"
```

---

### Task 3: Long-press gesture + bubble-rect accessor

Add a public bubble-rect accessor to `MessageItemView`, then a long-press component that opens the reaction bar. The component implements **only** `IPointerDownHandler`/`IPointerUpHandler` (never drag interfaces) so scroll still bubbles to the ScrollRect, and gates on `ScrollClickBlocker.IsBlocking` + `SwipeToBack.IsSliding` + a movement slop — exactly the codebase's established pattern.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (add the `BubbleRect` accessor near `BoundVm`, line 183)
- Create: `Assets/Scripts/Chat/MessageBubbleLongPress.cs`

- [ ] **Step 1: Add the bubble-rect accessor to `MessageItemView`**

Insert immediately after the `BoundVm` property (after line 183):

```csharp
    /// <summary>The visible bubble surface's RectTransform — anchor for the long-press reaction bar.</summary>
    public RectTransform BubbleRect => bubbleBackground != null ? (RectTransform)bubbleBackground.transform : null;
```

- [ ] **Step 2: Write the long-press component**

```csharp
// Assets/Scripts/Chat/MessageBubbleLongPress.cs
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Opens the reaction bar when a message bubble is held. Implements ONLY the pointer
/// (down/up) interfaces — never drag — so vertical scroll bubbles to the parent
/// ScrollRect untouched (same coexistence trick as ClickPassthrough /
/// DelayedFingerUpAction). Cancels if the finger drifts past the drag slop, if a
/// fling/slide is in progress, or on release before the hold elapses.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MessageBubbleLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float holdSeconds = 0.45f;
    [SerializeField] private float moveCancelPixels = 16f;

    private MessageItemView _view;
    private Coroutine _holdRoutine;
    private Vector2 _downPos;

    private void Awake() => _view = GetComponentInParent<MessageItemView>();

    public void OnPointerDown(PointerEventData eventData)
    {
        _downPos = eventData.position;
        if (_holdRoutine != null) StopCoroutine(_holdRoutine);
        _holdRoutine = StartCoroutine(HoldRoutine());
    }

    public void OnPointerUp(PointerEventData eventData) => CancelHold();
    private void OnDisable() => CancelHold();

    private void CancelHold()
    {
        if (_holdRoutine != null) { StopCoroutine(_holdRoutine); _holdRoutine = null; }
    }

    private IEnumerator HoldRoutine()
    {
        float elapsed = 0f;
        while (elapsed < holdSeconds)
        {
            if (SwipeToBack.IsSliding) yield break;                  // a swipe-back is animating
            if (Pointer.current != null)
            {
                Vector2 pos = Pointer.current.position.ReadValue();
                if (Vector2.Distance(pos, _downPos) > moveCancelPixels) yield break;  // turned into a scroll
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _holdRoutine = null;
        if (ScrollClickBlocker.IsBlocking) yield break;             // landed on a flinging list
        if (_view == null || _view.BoundVm == null) yield break;
        ReactionBarController.Instance?.Show(_view);
    }
}
```

> Note: `ReactionBarController` is created in Task 4. This file will not compile until then — implement Task 4 before recompiling. (Subagent-driven execution: do Tasks 3 and 4 as one unit, commit after both compile.)

- [ ] **Step 3: Commit (after Task 4 compiles)** — see Task 4, Step 5.

---

### Task 4: `ReactionBarController` + reaction-bar overlay (UI)

A single shared overlay owned by the WhatsApp messages screen panel. Its root stays active (singleton always reachable); the scrim+bar `content` toggles. Shows the quick-six bar above the long-pressed bubble, highlights the user's current reaction, sends on tap via `ChatManager.SendReaction`, and dismisses on scrim tap / chat switch / slide-out.

**Use the `unity-ui-builder` skill** for the prefab/scene construction (Step 2). Sizes below are canvas reference units (1080×1920).

**Files:**
- Create: `Assets/Scripts/Chat/ReactionBarController.cs`
- Modify scene `Assets/Scenes/Main.unity` — add the overlay under the WhatsApp messages panel (Step 2)

- [ ] **Step 1: Write the controller**

```csharp
// Assets/Scripts/Chat/ReactionBarController.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Single shared overlay showing the quick-reaction bar above a long-pressed bubble.
/// Lives on the WhatsApp messages screen panel; its root stays active so the singleton
/// is always reachable while the scrim+bar 'content' is toggled. Tapping an emoji sends
/// via ChatManager; the bubble updates itself through OnMessageReactionsChanged.
/// </summary>
public class ReactionBarController : MonoBehaviour
{
    public static ReactionBarController Instance { get; private set; }

    // WhatsApp quick-reaction set (raw unicode; converted to TMP sprites at render).
    private static readonly string[] QuickEmojis = { "👍", "❤️", "😂", "😮", "😢", "🙏" };

    [Header("Overlay")]
    [SerializeField] private GameObject content;     // scrim + bar; toggled on show/hide
    [SerializeField] private Button scrimButton;     // full-panel dismiss
    [SerializeField] private RectTransform bar;      // the pill that floats over the bubble

    [Header("Buttons (6 quick + plus)")]
    [SerializeField] private Button[] emojiButtons;  // length 6, each with a TMP label child
    [SerializeField] private Button plusButton;

    [Header("Selected highlight")]
    [SerializeField] private Color selectedTint = new Color(0.85f, 0.92f, 1f, 1f);
    [SerializeField] private Color normalTint = Color.white;

    private MessageViewModel _target;
    private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[6];

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            int idx = i;
            _labels[i] = emojiButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            emojiButtons[i].onClick.AddListener(() => OnEmojiTapped(QuickEmojis[idx]));
        }
        if (plusButton != null) plusButton.onClick.AddListener(OnPlusTapped);
        if (scrimButton != null) scrimButton.onClick.AddListener(Hide);
        if (content != null) content.SetActive(false);
    }

    private void OnEnable()
    {
        EmojiPatchService.OnEmojiReady += HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected += HandleChatSelected;
        SwipeToBack.OnSlideOutComplete += Hide;
        RenderEmojiLabels();
    }

    private void OnDisable()
    {
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (ChatManager.Instance != null) ChatManager.Instance.OnChatSelected -= HandleChatSelected;
        SwipeToBack.OnSlideOutComplete -= Hide;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show(MessageItemView source)
    {
        if (source == null || source.BoundVm == null || source.BubbleRect == null) return;
        _target = source.BoundVm;

        if (content != null) content.SetActive(true);
        RenderEmojiLabels();
        RefreshHighlight();

        LayoutRebuilder.ForceRebuildLayoutImmediate(bar);   // bar size valid before positioning
        PositionBarOver(source.BubbleRect);

        bar.localScale = Vector3.one * 0.85f;
        bar.DOScale(1f, 0.18f).SetEase(Ease.OutBack);
    }

    public void Hide()
    {
        _target = null;
        if (content != null) content.SetActive(false);
    }

    private void OnEmojiTapped(string emoji)
    {
        if (_target != null) ChatManager.Instance?.SendReaction(_target, emoji);
        Hide();
    }

    private void OnPlusTapped()
    {
        // Plan B: EmojiPickerController.Instance?.Show(_target);
        Hide();
    }

    private void RenderEmojiLabels()
    {
        for (int i = 0; i < _labels.Length; i++)
            if (_labels[i] != null)
                _labels[i].text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(QuickEmojis[i], MissingEmojiMode.Hide);
    }

    private void RefreshHighlight()
    {
        string mine = OutgoingReaction.CurrentMyEmoji(_target);
        for (int i = 0; i < emojiButtons.Length && i < 6; i++)
        {
            var img = emojiButtons[i].targetGraphic as Image;
            if (img != null) img.color = (mine != null && QuickEmojis[i] == mine) ? selectedTint : normalTint;
        }
    }

    private void PositionBarOver(RectTransform bubble)
    {
        RectTransform parentRt = (RectTransform)bar.parent;
        Vector3[] corners = new Vector3[4];
        bubble.GetWorldCorners(corners);                       // 0=BL 1=TL 2=TR 3=BR

        Vector2 topLocal = (Vector2)parentRt.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
        float gap = 16f;
        float y = topLocal.y + gap + bar.rect.height * 0.5f;

        float halfBarW = bar.rect.width * 0.5f;
        float maxX = parentRt.rect.width * 0.5f - halfBarW - 12f;
        float x = Mathf.Clamp(topLocal.x, -maxX, maxX);

        float topLimit = parentRt.rect.height * 0.5f - bar.rect.height * 0.5f - 12f;
        if (y > topLimit)   // not enough room above — drop below the bubble
        {
            Vector2 botLocal = (Vector2)parentRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            y = botLocal.y - gap - bar.rect.height * 0.5f;
        }

        bar.anchoredPosition = new Vector2(x, y);
    }

    private void HandleChatSelected(string chatId) => Hide();
    private void HandleEmojiReady(string spriteName) => RenderEmojiLabels();
}
```

- [ ] **Step 2: Build the overlay in the scene (use `unity-ui-builder`)**

Add under the WhatsApp messages screen panel (`Screen_Whatsapp/ChatsPanel` area — the messages screen, NOT the canvas root; per project memory floating UI is scoped to its screen panel), as the **last sibling** so it renders above the message list:

```
ReactionBarController            RectTransform: anchor stretch full-panel, offsets 0; ReactionBarController component (root stays ACTIVE)
└── content                      RectTransform: anchor stretch full-panel, offsets 0; pivot (0.5,0.5)   ← assign to 'content'
    ├── Scrim                    Image (color #000000, alpha 0.28), RaycastTarget on; Button (transition None) ← assign to 'scrimButton'
    └── Bar                      RectTransform anchor+pivot CENTER (0.5,0.5); HorizontalLayoutGroup (spacing 8, padding 12, childAlignment Middle, control/expand size off);
                                 ContentSizeFitter (Horizontal+Vertical = PreferredSize); background Image (white) + ImageWithRoundedCorners (radius ≈ 28) ← assign to 'bar'
        ├── Emoji0..Emoji5       Button 84×84 (LayoutElement preferred 84×84); white circular bg Image (targetGraphic);
        │                        child TMP label (stretch, alignment Center, fontSize ≈ 48)             ← assign all six to 'emojiButtons' in order 👍❤️😂😮😢🙏
        └── Plus                 Button 72×72; circular bg Image; child TMP "+" (fontSize ≈ 40) or a ti-style icon ← assign to 'plusButton'
```

Wire all `[SerializeField]` refs (`content`, `scrimButton`, `bar`, `emojiButtons[6]` in emoji order, `plusButton`). Leave the emoji TMP labels empty in-editor — `RenderEmojiLabels()` fills them at runtime so they always reflect the sprite atlas.

- [ ] **Step 3: Verify the six quick-reaction sprites exist in the static atlas**

In the open Editor, confirm the static emoji sprite atlas contains: `1f44d` (👍), `2764-fe0f` (❤️), `1f602` (😂), `1f62e` (😮), `1f622` (😢), `1f64f` (🙏). If any are missing, the controller already re-renders on `EmojiPatchService.OnEmojiReady`, so they will fill in once downloaded — but the bar should feel instant, so prefer they be static. (No code change if present.)

- [ ] **Step 4: Recompile & run the test suite**

Run: `Tools/run-tests-headless.sh 'OutgoingReactionTests'` (or drop `Temp/claude/run-tests.trigger` if the Editor is open)
Expected: PASS — proves `MessageBubbleLongPress.cs` (Task 3) and `ReactionBarController.cs` compile together.

- [ ] **Step 5: Commit Tasks 3 + 4**

```bash
git add Assets/Scripts/Chat/MessageBubbleLongPress.cs Assets/Scripts/Chat/MessageBubbleLongPress.cs.meta \
        Assets/Scripts/Chat/ReactionBarController.cs Assets/Scripts/Chat/ReactionBarController.cs.meta \
        Assets/Scripts/UI/MessageItemView.cs \
        Assets/Scenes/Main.unity \
        "Assets/Prefabs"
git commit -m "feat(reactions): long-press gesture + shared reaction-bar overlay"
```

---

### Task 5: Attach the gesture to both message prefabs + end-to-end verify

**Files:**
- Modify prefabs: `Assets/Prefabs/MessageTextIncoming.prefab`, `Assets/Prefabs/MessageTextOutgoing.prefab`

- [ ] **Step 1: Add the gesture component to both bubble prefabs**

On each prefab, add `MessageBubbleLongPress` to the **`bubbleBackground`** GameObject (the visible bubble Image — already a raycast target). `Awake` resolves its `MessageItemView` via `GetComponentInParent`. No per-Bind wiring needed (Bind never touches pointer handlers, so it survives pooling/rebind).

- [ ] **Step 2: Device / Editor Play-mode verification**

Open a WhatsApp chat with real messages and verify:
1. Long-press an **incoming** bubble → bar appears above it; tap 👍 → pill shows 👍 instantly; confirm the reaction lands in WhatsApp on the phone.
2. Long-press the same bubble → 👍 is highlighted; tap 👍 again → pill disappears (toggle off) and the reaction is removed in WhatsApp.
3. Long-press → tap ❤️, then long-press → tap 😂 → pill replaces (one reaction at a time).
4. Long-press your **own** outgoing (Sent) bubble → can react.
5. **Scroll** the list with a flick — long-press must NOT fire (gated by movement slop + `ScrollClickBlocker`).
6. Tap the scrim → bar dismisses with no reaction. Swipe back to the chat list → bar is gone.
7. Long-press a still-`Pending` outgoing message → nothing happens (guarded; check console for the "not yet sent" warning).
8. Airplane mode → react → pill appears then reverts when the POST fails (check console for the `message/reaction failed` log).

- [ ] **Step 3: Final test run + commit**

Run: `Tools/run-tests-headless.sh` (full suite)
Expected: PASS — existing tests + the 8 `OutgoingReactionTests`.

```bash
git add "Assets/Prefabs/MessageTextIncoming.prefab" "Assets/Prefabs/MessageTextOutgoing.prefab"
git commit -m "feat(reactions): enable long-press-to-react on message bubbles"
```

**Plan A complete — sending quick-six reactions works end-to-end.**

---

## Plan B — follow-on: the `+` full emoji picker

Plan B reuses everything from Plan A: `ChatManager.SendReaction(target, emoji)` already accepts any emoji string, and `ReactionBarController.OnPlusTapped()` is the hook. Only a grid UI is new. Build after Plan A merges.

**Files:**
- Create: `Assets/Scripts/Chat/EmojiPickerController.cs` + an `EmojiPicker` overlay prefab (a scrollable grid)
- Create: `Assets/Scripts/Chat/ReactionEmojiCatalog.cs` (curated emoji list)
- Modify: `Assets/Scripts/Chat/ReactionBarController.cs` — `OnPlusTapped` opens the picker for `_target`

### Task B1: Curated emoji catalog (TDD)

- [ ] **Step 1: Write the test**

```csharp
// Assets/Tests/Editor/Chat/ReactionEmojiCatalogTests.cs
using NUnit.Framework;

public class ReactionEmojiCatalogTests
{
    [Test]
    public void All_HasReasonableCount()
        => Assert.GreaterOrEqual(ReactionEmojiCatalog.All.Length, 48);

    [Test]
    public void All_ContainsQuickSix()
    {
        foreach (var e in new[] { "👍", "❤️", "😂", "😮", "😢", "🙏" })
            CollectionAssert.Contains(ReactionEmojiCatalog.All, e);
    }

    [Test]
    public void All_NoNullOrEmpty()
        => CollectionAssert.DoesNotContain(ReactionEmojiCatalog.All, "");
}
```

- [ ] **Step 2: Run → fail.** `Tools/run-tests-headless.sh 'ReactionEmojiCatalogTests'` → FAIL (type missing).

- [ ] **Step 3: Implement** a `public static class ReactionEmojiCatalog { public static readonly string[] All = { /* ~64 common raw emoji incl. the quick six */ }; }` in `Assets/Scripts/Chat/ReactionEmojiCatalog.cs`. Use a curated set of common reaction emoji (faces, hearts, hands, celebration) as raw unicode strings.

- [ ] **Step 4: Run → pass.** Commit (`.cs` + `.meta` for both files).

### Task B2: `EmojiPickerController` + grid overlay

- [ ] **Step 1:** Write `EmojiPickerController` (singleton, same overlay/scrim/dismiss pattern as `ReactionBarController`): `Show(MessageViewModel target)` populates a `GridLayoutGroup` ScrollRect by instantiating an emoji-cell button prefab per `ReactionEmojiCatalog.All`, each rendering `UnicodeEmojiConverter.ConvertRealEmojisToSprites(emoji, MissingEmojiMode.KeepRaw)` and calling `ChatManager.Instance.SendReaction(target, emoji)` + `Hide()` on tap. Subscribe to `EmojiPatchService.OnEmojiReady` to re-render cells whose sprite arrives late (lazy CDN load). Dismiss on scrim tap / `OnChatSelected` / `OnSlideOutComplete`.
- [ ] **Step 2:** Build the `EmojiPicker` overlay prefab in the messages panel (use `unity-ui-builder`): a bottom sheet with a `ScrollRect` + `GridLayoutGroup` (cell ≈ 96×96, ~6 columns) + a header. Pool/cap cells to the catalog length (log if capped — no silent truncation).
- [ ] **Step 3:** In `ReactionBarController.OnPlusTapped`, replace the comment with `EmojiPickerController.Instance?.Show(_target);` (capture `_target` into a local before `Hide()` nulls it).
- [ ] **Step 4:** Recompile, run full test suite (PASS), device-verify: `+` opens the grid, tapping any emoji reacts and the picker closes; a not-yet-downloaded emoji renders once its sprite loads.
- [ ] **Step 5:** Commit (scripts + `.meta` + prefab + scene + `ReactionBarController.cs`).

**Plan B complete — arbitrary-emoji reactions via the full picker.**

---

## Self-review notes

- **Spec coverage:** trigger (Task 3) · quick-six bar + `+` (Task 4 / Plan B) · WhatsApp-only direct Wappi call (Task 2, confirmed endpoint) · optimistic reuse of receive pipeline (Task 2 via `ReactionStore.ApplyToMessage` + `OnMessageReactionsChanged`) · toggle/replace/remove (Task 1) · react to own messages (Task 5 verify #4) · removal = `body ""` (Task 1/2) · no new data fields (uses existing `MessageReaction`/`reactions`) · quick-six sprite-atlas risk (Task 4 Step 3) · gesture-vs-scroll (Task 3 gates) · positioning clamp (Task 4 `PositionBarOver`) · profile-id resolution (Task 2 `GetActiveProfileId`). All covered.
- **Type consistency:** `OutgoingReaction.MeReactorKey`/`CurrentMyEmoji`/`Resolve`, `ChatManager.SendReaction(MessageViewModel,string)`, `WappiSendReactionRequest{body,message_id}`, `ReactionBarController.Instance.Show(MessageItemView)`, `MessageItemView.BubbleRect`/`BoundVm` are used identically everywhere they appear.
