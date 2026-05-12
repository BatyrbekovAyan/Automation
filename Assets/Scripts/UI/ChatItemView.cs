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

    private ChatViewModel vm;
    private string chatId;
    private Coroutine avatarLoadCoroutine;
    private bool pendingAvatarLoad;

    // LRU cache: [ChatID_RawMessage] -> [Perfectly Sliced String]. Capped to bound memory on long sessions.
    private const int MaxTextCacheCount = 500;
    private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> textCache = new();
    private static readonly LinkedList<KeyValuePair<string, string>> textCacheOrder = new();
    
public void Bind(ChatViewModel model)
    {
        if (vm != null)
        {
            vm.OnUpdated -= OnVmUpdated;
            vm.OnLastMessageChanged -= OnLastMessageChanged;
        }

        vm = model;
        chatId = vm.ChatId;

        titleText.text = vm.Title;

        if (timeText != null)
            timeText.text = vm.LastMessageTimeString;

        ApplyUnreadBadge(vm.UnreadCount);

// --- THE ZERO-FRAME AVATAR FIX ---
        if (vm.AvatarSprite == null && !string.IsNullOrEmpty(vm.AvatarUrl) && MediaCacheManager.Instance != null)
        {
            Sprite cached = MediaCacheManager.Instance.GetSpriteFromMemory(vm.AvatarUrl);
            if (cached != null) vm.AvatarSprite = cached;
        }

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
                    MediaCacheManager.Instance.StoreSpriteInMemory(vm.AvatarUrl, sprite);
                    avatarImage.sprite = sprite;

                    avatarImage.gameObject.SetActive(true);
                    defaultAvatar.gameObject.SetActive(false); // Make sure default hides!
                    loadedFromCache = true;
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

        vm.OnUpdated += OnVmUpdated;
        vm.OnLastMessageChanged += OnLastMessageChanged;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnClick);
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
            if (MediaCacheManager.Instance != null)
                MediaCacheManager.Instance.StoreSpriteInMemory(vm.AvatarUrl, sprite);
            if (avatarImage != null) avatarImage.sprite = sprite;
            avatarImage.gameObject.SetActive(true);
            defaultAvatar.gameObject.SetActive(false);
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

        ApplyUnreadBadge(vm.UnreadCount);
    }

    private void OnLastMessageChanged(ChatViewModel vmRef)
    {
        // Move this row to the top of the list — fires only when the last message actually changed
        transform.SetAsFirstSibling();
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
            vm != null && vm.IsLastMessageMine);

        if (string.IsNullOrEmpty(formatted))
        {
            lastMessageText.text = "";
            return;
        }

        string composed = UnicodeEmojiConverter.ConvertRealEmojisToSprites(formatted);

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
        if (!pendingAvatarLoad) return;
        pendingAvatarLoad = false;
        if (vm == null || vm.AvatarSprite != null || string.IsNullOrEmpty(vm.AvatarUrl)) return;
        avatarLoadCoroutine = StartCoroutine(LoadAvatar(vm));
    }

    void OnDestroy()
    {
        if (vm != null)
        {
            vm.OnUpdated -= OnVmUpdated;
            vm.OnLastMessageChanged -= OnLastMessageChanged;
        }
    }
    
    
    void OnClick()
    {
        ChatManager.Instance.SelectChat(chatId);
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

    // WhatsApp-style time-color toggle. When the row carries unread messages
    // the timestamp on the right shifts to WhatsApp green; on read it returns
    // to the muted gray defined on the prefab.
    private static readonly Color UnreadTimeColor = new Color32(0x25, 0xD3, 0x66, 0xFF);
    private static readonly Color ReadTimeColor = new Color32(0x66, 0x66, 0x66, 0xFF);

    private void ApplyUnreadBadge(int count)
    {
        bool hasUnread = count > 0;

        if (timeText != null)
            timeText.color = hasUnread ? UnreadTimeColor : ReadTimeColor;

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