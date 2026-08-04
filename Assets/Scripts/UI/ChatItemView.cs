using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

public class ChatItemView : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public Image avatarImage;
    public Image defaultAvatar;
    public Button button;
    public TextMeshProUGUI lastMessageText;

    public TextMeshProUGUI timeText;
    public GameObject unreadBadge;
    public TextMeshProUGUI unreadCountText;

    [Header("Swipe-to-delete")]
    public Button deleteButton;        // the red button revealed behind the row
    public SwipeToDelete swipeToDelete; // on the SwipeContent child

    private ChatViewModel vm;
    public ChatViewModel Vm => vm;
    private string chatId;
    private ChatListView parentList;
    private Coroutine avatarLoadCoroutine;
    private bool pendingAvatarLoad;

    // LRU cache: [ChatID_RawMessage] -> [Perfectly Sliced String]. Capped to bound memory on long sessions.
    private const int MaxTextCacheCount = 500;
    private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> textCache = new();
    private static readonly LinkedList<KeyValuePair<string, string>> textCacheOrder = new();
    
public void Bind(ChatViewModel model)
    {
        // includeInactive: the initial cache load binds rows while the chat panel is
        // still hidden — the default overload would return null there and leave the
        // row detached from its list until a later rebind.
        if (parentList == null)
            parentList = GetComponentInParent<ChatListView>(true);

        if (vm != null)
        {
            vm.OnUpdated -= OnVmUpdated;
            vm.OnLastMessageChanged -= OnLastMessageChanged;
        }

        vm = model;
        chatId = vm.ChatId;

        ApplyTitle();

        if (timeText != null)
            timeText.text = vm.LastMessageTimeString;

        ApplyUnreadBadge(vm.UnreadCount);

        if (vm.AvatarSprite != null)
        {
            avatarImage.sprite = vm.AvatarSprite;
            avatarImage.gameObject.SetActive(true);
            defaultAvatar.gameObject.SetActive(false); // Make sure default hides!
        }
        else
        {
            bool loadedFromCache = false;

            // 1. Check the hard drive synchronously! No waiting for Coroutines!
            if (!string.IsNullOrEmpty(vm.AvatarUrl) && MediaCacheManager.Instance != null && MediaCacheManager.Instance.IsImageCached(vm.AvatarUrl))
            {
                string path = MediaCacheManager.Instance.GetFilePathFromUrl(vm.AvatarUrl);
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);

                if (tex.LoadImage(bytes))
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    vm.AvatarSprite = sprite;
                    avatarImage.sprite = sprite;

                    avatarImage.gameObject.SetActive(true);
                    defaultAvatar.gameObject.SetActive(false); // Make sure default hides!
                    loadedFromCache = true;
                }
                else
                {
                    Destroy(tex);
                }
            }

            // 2. ONLY fallback to the default avatar and network download if missing
            if (!loadedFromCache)
            {
                avatarImage.gameObject.SetActive(false);
                defaultAvatar.gameObject.SetActive(true);

                // --- APPLY THE RANDOM COLOR FIX HERE ---
                ApplyDefaultAvatarColor(chatId);

                if (!string.IsNullOrEmpty(vm.AvatarUrl))
                {
                    if (avatarLoadCoroutine != null) StopCoroutine(avatarLoadCoroutine);
                    // Bind can fire while the chat list panel is inactive (initial
                    // cache load before the user has navigated to it). StartCoroutine
                    // throws on inactive GameObjects, so defer to OnEnable.
                    if (isActiveAndEnabled)
                        avatarLoadCoroutine = StartCoroutine(LoadAvatar(vm));
                    else
                        pendingAvatarLoad = true;
                }
            }
        }

        UpdatePreviewText(vm.LastMessage ?? "");
        MaybeResolveRowDetails();

        vm.OnUpdated += OnVmUpdated;
        vm.OnLastMessageChanged += OnLastMessageChanged;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);

        // Swipe-to-delete is a WhatsApp-only affordance (Telegram has no chat/delete endpoint —
        // the 05-03 network guard already no-ops it; the D4 owner decision removes the VISUAL
        // slide on TG rows too). Rows are pooled and rebind across bots/channels, so set the
        // enabled state EXPLICITLY on every Bind — never inherit the prior channel's state.
        ChatChannel rowChannel = ChatManager.Instance != null
            ? ChatManager.Instance.ActiveChannel
            : ChatChannel.WhatsApp;
        bool swipeEnabled = ChatRowSwipePolicy.Enabled(rowChannel);

        // Snap shut first so a row swiped-open on WhatsApp closes before a channel-swap rebind
        // (and stays closed when the affordance is then disabled on Telegram).
        if (swipeToDelete != null)
        {
            swipeToDelete.ResetClosed();
            swipeToDelete.enabled = swipeEnabled; // disabled ⇒ no drag callbacks ⇒ no slide
        }

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            if (swipeEnabled)
            {
                deleteButton.gameObject.SetActive(true);
                deleteButton.onClick.AddListener(OnDeleteClicked);
            }
            else
            {
                // No reveal target on Telegram — hide the red button so a disabled swipe
                // can never expose it on a pooled row that had been a WhatsApp row.
                deleteButton.gameObject.SetActive(false);
            }
        }
    }

    IEnumerator LoadAvatar(ChatViewModel vm)
    {
        // Use standard Get instead of GetTexture so we have access to the raw bytes for saving!
        using UnityWebRequest req = UnityWebRequest.Get(vm.AvatarUrl);
        req.timeout = 30;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;

        byte[] bytes = req.downloadHandler.data;

        // Save it to the phone permanently!
        if (MediaCacheManager.Instance != null)
            MediaCacheManager.Instance.SaveImageToCache(vm.AvatarUrl, bytes);

        Texture2D tex = new Texture2D(2, 2);
        if (tex.LoadImage(bytes))
        {
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            vm.AvatarSprite = sprite;
            if (avatarImage != null) avatarImage.sprite = sprite;
            avatarImage.gameObject.SetActive(true);
            defaultAvatar.gameObject.SetActive(false);
        }
        else
        {
            Destroy(tex);
        }
    }

    void OnVmUpdated(ChatViewModel updated)
    {
        if (updated != vm) return;

        if (timeText != null)
            timeText.text = vm.LastMessageTimeString;

        if (vm.AvatarSprite != null)
            avatarImage.sprite = vm.AvatarSprite;

        // --- THE FIX: Format the preview text on updates too ---
        UpdatePreviewText(vm.LastMessage ?? "");
        MaybeResolveRowDetails();

        ApplyUnreadBadge(vm.UnreadCount);
    }

    // Phase 2 / group-author: when an on-screen row is missing detail — a reaction's target
    // text, or a group row's sender name (empty pushname for LID participants) — ask the
    // manager to backfill it. The manager caches/dedupes/queues, so this is a cheap ping.
    // ReactionTargetText is checked by reference-null (a resolved "" media row must not re-ping).
    private void MaybeResolveRowDetails()
    {
        if (vm == null || string.IsNullOrEmpty(vm.LastMessageId)) return;
        bool needsTarget = vm.LastMessageType == "reaction" && vm.ReactionTargetText == null;
        bool needsName = vm.IsGroup && !vm.IsLastMessageMine && string.IsNullOrEmpty(vm.LastMessageSenderName);
        if (!needsTarget && !needsName) return;
        if (ChatManager.Instance != null) ChatManager.Instance.ResolveRowDetails(vm);
    }

    private void OnLastMessageChanged(ChatViewModel vmRef)
    {
        // Ask the list for a data-driven resort (coalesced per frame) instead of a
        // positional self-move — per-row insert-at-top reversed every chat that
        // changed within one multi-chat sync pass. Re-resolve with includeInactive:
        // Bind can run while the chat panel is hidden, where the default
        // GetComponentInParent skips inactive ancestors and returns null.
        if (parentList == null)
            parentList = GetComponentInParent<ChatListView>(true);
        if (parentList != null)
            parentList.RequestResort();
    }

    // Title/preview emoji are converted in Hide mode so a sprite that has not
    // downloaded yet never shows as tofu or literal tag text. While anything is
    // missing we listen for OnEmojiReady and re-render when the sprite lands.
    private void ApplyTitle()
    {
        titleText.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            vm.Title ?? "", MissingEmojiMode.Hide, out bool titleMissing);
        if (titleMissing) SubscribeToEmojiReady();
    }

    private void SubscribeToEmojiReady()
    {
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        EmojiPatchService.OnEmojiReady += HandleEmojiReady;
    }

    private void HandleEmojiReady(string spriteName)
    {
        if (vm == null) return;
        // Unsubscribe first — ApplyTitle/UpdatePreviewText re-subscribe if anything is still missing.
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        ApplyTitle();
        UpdatePreviewText(vm.LastMessage ?? "");
    }

    private void UpdatePreviewText(string rawMessage)
    {
        // Phase 2: prepend [tick] [media-emoji] using ChatPreviewFormatter, then
        // convert any Unicode emoji (in either the rawMessage or our injected
        // media emoji prefix) to EmojiOne sprite tags. Tick sprite tags are ASCII
        // and pass through the converter unchanged.
        //
        // Run the formatter FIRST so an empty rawMessage paired with a media type
        // or delivery status still produces a visible tick/emoji prefix.
        string formatted = ChatPreviewFormatter.Format(
            rawMessage ?? "",
            vm != null ? vm.LastMessageType : null,
            vm != null ? vm.LastMessageDeliveryStatus : null,
            vm != null && vm.IsLastMessageMine,
            vm != null ? vm.ReactionTargetText : null,
            vm != null ? vm.ReactionTargetType : null,
            vm != null ? vm.LastMessageSenderName : null,
            vm != null && vm.IsGroup);

        if (string.IsNullOrEmpty(formatted))
        {
            lastMessageText.text = "";
            return;
        }

        string composed = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
            formatted, MissingEmojiMode.Hide, out bool previewMissing);
        if (previewMissing) SubscribeToEmojiReady();

        // --- THE PERFORMANCE FIX: Check the Cache! ---
        // Create a unique key for this exact composed string in this exact chat
        string cacheKey = $"{chatId}_{composed}";

        // If we already did the heavy math for this exact string, use the saved answer instantly!
        if (textCache.TryGetValue(cacheKey, out var existingNode))
        {
            textCacheOrder.Remove(existingNode);
            textCacheOrder.AddFirst(existingNode);
            lastMessageText.text = existingNode.Value.Value;
            return;
        }

        float exactWidth = lastMessageText.rectTransform.rect.width;
        float maxWidth = 0f;

        if (exactWidth > 50f)
        {
            maxWidth = exactWidth;
        }
        else
        {
            float containerWidth = 1080f;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) containerWidth = canvas.GetComponent<RectTransform>().rect.width;
            maxWidth = containerWidth - 280f;
        }

        // Do the heavy binary search math ONE TIME...
        string slicedText = SplitLongWord(composed, lastMessageText, maxWidth);

        // ...and save the answer in the LRU vault for the next time we scroll past it!
        var node = new LinkedListNode<KeyValuePair<string, string>>(
            new KeyValuePair<string, string>(cacheKey, slicedText));
        textCacheOrder.AddFirst(node);
        textCache[cacheKey] = node;
        while (textCache.Count > MaxTextCacheCount)
        {
            var tail = textCacheOrder.Last;
            textCacheOrder.RemoveLast();
            textCache.Remove(tail.Value.Key);
        }

        lastMessageText.text = slicedText;
    }
    
    // --- THE RICH-TEXT-AWARE CALCULATOR ---
    private string SplitLongWord(string text, TextMeshProUGUI textComp, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;

        float safeMaxWidth = maxWidth - 10f; // 10px safety buffer

        // 1. Tokenize into words, completely ignoring spaces inside <tags>!
        System.Collections.Generic.List<string> words = new System.Collections.Generic.List<string>();
        System.Text.StringBuilder currentWord = new System.Text.StringBuilder();
        bool insideTag = false;

        foreach (char c in text)
        {
            if (c == '<') insideTag = true;
            else if (c == '>') insideTag = false;

            if (c == ' ' && !insideTag)
            {
                words.Add(currentWord.ToString());
                currentWord.Clear();
            }
            else
            {
                currentWord.Append(c);
            }
        }
        if (currentWord.Length > 0) words.Add(currentWord.ToString());

        System.Text.StringBuilder result = new System.Text.StringBuilder();

        foreach (string word in words)
        {
            // If the word (or emoji) fits normally, just add it!
            if (textComp.GetPreferredValues(word, Mathf.Infinity, Mathf.Infinity).x <= safeMaxWidth)
            {
                result.Append(word).Append(" ");
                continue;
            }

            // 2. The word is too long (like a URL). We must split it safely.
            // First, break the word into "atomic elements" (individual chars OR full tags)
            System.Collections.Generic.List<string> elements = new System.Collections.Generic.List<string>();
            System.Text.StringBuilder currentElement = new System.Text.StringBuilder();
            bool inTag = false;

            foreach (char c in word)
            {
                currentElement.Append(c);
                if (c == '<') inTag = true;
                else if (c == '>')
                {
                    inTag = false;
                    elements.Add(currentElement.ToString());
                    currentElement.Clear();
                }
                else if (!inTag)
                {
                    elements.Add(currentElement.ToString());
                    currentElement.Clear();
                }
            }
            if (currentElement.Length > 0) elements.Add(currentElement.ToString());

            // 3. Rebuild the line element-by-element, forcing a break when it hits the edge
            string currentLine = "";
            foreach (string element in elements)
            {
                string testStr = currentLine + element;
                if (textComp.GetPreferredValues(testStr, Mathf.Infinity, Mathf.Infinity).x > safeMaxWidth)
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        result.Append(currentLine).Append("\n");
                    }
                    currentLine = element; 
                }
                else
                {
                    currentLine = testStr;
                }
            }
            if (!string.IsNullOrEmpty(currentLine))
            {
                result.Append(currentLine).Append(" ");
            }
        }

        return result.ToString().TrimEnd();
    }
    private void OnEnable()
    {
        // Repaint the runtime-tinted bits on a light/dark switch. The static
        // graphics carry their own ThemedColor bindings; these two are painted
        // here at bind time, so they need the event to stay in sync while the
        // list is on screen instead of waiting for the next rebind.
        Theme.Changed += HandleThemeChanged;

        if (!pendingAvatarLoad) return;
        pendingAvatarLoad = false;
        if (vm == null || vm.AvatarSprite != null || string.IsNullOrEmpty(vm.AvatarUrl)) return;
        avatarLoadCoroutine = StartCoroutine(LoadAvatar(vm));
    }

    private void OnDisable()
    {
        Theme.Changed -= HandleThemeChanged;
    }

    /// <summary>Re-run the runtime tint pass with the row's current unread count.</summary>
    private void HandleThemeChanged() => ApplyUnreadBadge(lastUnreadCount);

    void OnDestroy()
    {
        Theme.Changed -= HandleThemeChanged;
        EmojiPatchService.OnEmojiReady -= HandleEmojiReady;
        if (vm != null)
        {
            vm.OnUpdated -= OnVmUpdated;
            vm.OnLastMessageChanged -= OnLastMessageChanged;
        }
    }
    
    
    void OnClick()
    {
        // A scroll/swipe that ended on this row is NOT a tap — ignore it (checked first so a
        // swipe that just opened this row isn't immediately dismissed by the "any open" rule).
        if (swipeToDelete != null && swipeToDelete.ConsumeDragFlag()) return;
        // A real tap while a row's delete is open just dismisses it — it does NOT open a chat
        // (WhatsApp behavior: the first tap is "put it away").
        if (SwipeToDelete.AnyOpen)
        {
            SwipeToDelete.CloseAnyOpen();
            return;
        }
        if (ScrollClickBlocker.IsBlocking) return;

        ChatManager.Instance.SelectChat(chatId);
    }

    void OnDeleteClicked()
    {
        if (parentList != null) parentList.RequestDelete(vm);
    }
    
    // --- THE WHATSAPP DEFAULT COLORS ---
    // Pair Format: [Light Background, Dark Silhouette]
    private static readonly string[][] AvatarColors = new string[][]
    {
        new string[] { "#CFE9E4", "#00A884" }, // Teal
        new string[] { "#D6E4FB", "#1FA2FF" }, // Blue
        new string[] { "#EADCF1", "#A348D4" }, // Purple
        new string[] { "#FCE1D0", "#F8942F" }, // Orange
        new string[] { "#FCE2EC", "#E14781" }  // Pink
    };

    private void ApplyDefaultAvatarColor(string id)
    {
        if (defaultAvatar == null) return;
        
        // 1. Create a stable hash from the Chat ID
        int hash = 0;
        if (!string.IsNullOrEmpty(id))
        {
            foreach (char c in id) hash += c;
        }
        
        // 2. Pick a color pair based on the hash
        int colorIndex = Mathf.Abs(hash) % AvatarColors.Length;

        Color bgColor, fgColor;
        UnityEngine.ColorUtility.TryParseHtmlString(AvatarColors[colorIndex][0], out bgColor);
        UnityEngine.ColorUtility.TryParseHtmlString(AvatarColors[colorIndex][1], out fgColor);

        // 3. Color the Background
        defaultAvatar.color = bgColor;

        // 4. Color the Silhouette (Assuming it is the first child of the defaultAvatar!)
        if (defaultAvatar.transform.childCount > 0)
        {
            Image silhouette = defaultAvatar.transform.GetChild(0).GetComponent<Image>();
            if (silhouette != null)
            {
                silhouette.color = fgColor;
            }
        }
    }

    // WhatsApp-style time-color toggle. When the row carries unread messages the
    // timestamp on the right shifts to the channel's unread accent; on read it
    // returns to the muted secondary ink.
    //
    // Both now come from the theme layer rather than local constants, so the
    // light/dark switch reaches them. The values are unchanged: UnreadAccentWhatsApp
    // is #26B25A (fixed — it is one end of the ChannelAccent pair, see Theme.Fixed)
    // and InkSecondary resolves to #666666 under the light theme.
    private static Color UnreadTimeColor => Theme.Fixed.UnreadAccentWhatsApp;
    private static Color ReadTimeColor => Theme.Color(ThemeRole.InkSecondary);

    // The unread pill's fill. Sourced from the theme's fixed channel accent rather than
    // captured off the Image: ChannelAccent.Resolve maps from THIS value, so a pooled row
    // that went blue on a Telegram bind reverts to the exact green on the next WhatsApp
    // bind — and unlike the old lazy capture it cannot latch an already-recolored value.
    private Image unreadBadgeImage;

    // Last count this row was bound with, so a light/dark switch can repaint the
    // runtime-tinted bits without waiting for the next rebind.
    private int lastUnreadCount;

    private void CacheUnreadBadgeColor()
    {
        if (unreadBadgeImage == null && unreadBadge != null)
            unreadBadgeImage = unreadBadge.GetComponent<Image>();
    }

    private void ApplyUnreadBadge(int count)
    {
        lastUnreadCount = count;

        // Profile → Уведомления → «Счётчик непрочитанных»: treating the count
        // as zero also reverts the green time tint, covering both call sites.
        if (!NotifPrefs.UnreadBadgeEnabled) count = 0;

        bool hasUnread = count > 0;

        // Telegram recolors the green unread accents to brand blue; WhatsApp keeps the
        // authored green (ChannelAccent passthrough). Null-guard ChatManager (Editor/tests)
        // ⇒ default WhatsApp ⇒ accents stay exactly as authored.
        ChatChannel channel = ChatManager.Instance != null
            ? ChatManager.Instance.ActiveChannel
            : ChatChannel.WhatsApp;

        if (timeText != null)
            timeText.color = hasUnread ? ChannelAccent.Resolve(channel, UnreadTimeColor) : ReadTimeColor;

        CacheUnreadBadgeColor();
        if (unreadBadgeImage != null)
            unreadBadgeImage.color = ChannelAccent.Resolve(channel, Theme.Fixed.UnreadAccentWhatsApp);

        if (unreadBadge == null) return;
        if (!hasUnread)
        {
            unreadBadge.SetActive(false);
            return;
        }
        unreadBadge.SetActive(true);
        if (unreadCountText != null)
        {
            unreadCountText.text = count > 99 ? "99+" : count.ToString();
        }
    }
}