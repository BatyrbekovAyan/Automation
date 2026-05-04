using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

[DefaultExecutionOrder(-100)]
public class ChatManager : MonoBehaviour
{
    // Settings
    public static int MessagesPerPage = 50;

    [Header("UI Panels")]
    public GameObject ChatListPanel;
    public GameObject MessageListPanel;
    
    public List<ChatViewModel> Chats = new();
    private Dictionary<string, ChatViewModel> chatLookup = new();
    private HashSet<string> seenMessageIds = new HashSet<string>();

    public static ChatManager Instance;
    
    // Events
    public event Action<ChatViewModel> OnChatAdded;
    public event Action OnChatListCleared;
    public event Action<string> OnChatSelected;
    public event Action<List<MessageViewModel>, bool, bool> OnBatchMessagesLoaded;
    public event Action<List<MessageViewModel>> OnLiveMessagesReceived;
    public event Action<string> OnActiveBotChanged;
    public event Action<EmptyStateReason> OnEmptyState;
    
    // State
    public int currentPage = 1;
    private string currentChatId;

    // Active-bot state
    private const string DefaultBotId = "_default";
    public string CurrentBotId { get; private set; } = DefaultBotId;

    /// <summary>
    /// Per-bot cache root: {persistentDataPath}/BotCache/{CurrentBotId}/.
    /// Always exists after this call; safe for callers to write under.
    /// </summary>
    public string GetCacheRoot()
    {
        string botId = SanitizeBotId(CurrentBotId);
        string path = Path.Combine(Application.persistentDataPath, "BotCache", botId);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Strips path separators and invalid filename characters from a bot id.
    /// Falls back to the default sentinel if the input is empty or fully invalid.
    /// Defense-in-depth: PlayerPrefs are user-editable on disk.
    /// </summary>
    private static string SanitizeBotId(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return DefaultBotId;

        char[] invalid = Path.GetInvalidFileNameChars();
        if (botId.IndexOfAny(invalid) < 0 && botId.IndexOf("..", StringComparison.Ordinal) < 0)
            return botId;

        return DefaultBotId;
    }

    private const string LastSelectedBotPrefKey = "LastSelectedBotForChats";

    /// <summary>
    /// Switch the active bot. Persists the choice, fires OnActiveBotChanged,
    /// clears the current chat list, and triggers a fresh load.
    /// </summary>
    public void SetActiveBot(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return;
        if (botId == CurrentBotId) return;

        CurrentBotId = botId;
        PlayerPrefs.SetString(LastSelectedBotPrefKey, botId);
        PlayerPrefs.Save();

        Chats.Clear();
        chatLookup.Clear();
        OnChatListCleared?.Invoke();
        OnActiveBotChanged?.Invoke(botId);

        StopAllCoroutines();
        BeginLoadForActiveBot();
    }

    /// <summary>
    /// Returns true when profileId is a usable bot profile id — not null/empty
    /// and not the unauthed sentinel (Bot.UnauthedProfileSentinel).
    /// </summary>
    private static bool IsValidProfileId(string profileId)
        => !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Returns the active bot's WhatsApp profile ID, or null if missing/sentinel.
    /// Coroutines guard on null and abort to avoid sending malformed requests.
    /// </summary>
    private string GetActiveProfileId()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null) return null;
        return IsValidProfileId(bot.whatsappProfileId) ? bot.whatsappProfileId : null;
    }

    /// <summary>
    /// Resolve the active bot's WhatsApp profile, then load cached chats and
    /// kick off a network sync. Fires OnEmptyState if the bot has no WhatsApp.
    /// </summary>
    private void BeginLoadForActiveBot()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null || !IsValidProfileId(bot.whatsappProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            return;
        }

        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
        string cachedJson = "";
        if (File.Exists(cachePath))
        {
            cachedJson = File.ReadAllText(cachePath);
            ParseChatsJson(cachedJson, true);
        }

        StartCoroutine(SyncAllChats(cachePath, cachedJson));
    }

    public void Awake()
    {
        Instance = this;
    }

    public void Start()
    {
        ShowChatList(true);
        StartCoroutine(InitializeActiveBotNextFrame());
    }

    /// <summary>
    /// Defer active-bot resolution by one frame so Manager.Start() (which runs after
    /// ChatManager.Start() due to ChatManager's [DefaultExecutionOrder(-100)]) has a
    /// chance to instantiate the bot GameObjects under BotsParent first.
    /// </summary>
    private IEnumerator InitializeActiveBotNextFrame()
    {
        yield return null;
        if (ResolveInitialActiveBot())
        {
            BeginLoadForActiveBot();
        }
    }

    /// <summary>
    /// Pick the active bot at startup: persisted choice if it still exists,
    /// otherwise the first bot. Returns false (after firing OnEmptyState(NoBotsExist))
    /// when no bots exist — caller should skip BeginLoadForActiveBot in that case to
    /// avoid a redundant BotHasNoWhatsApp event.
    /// </summary>
    private bool ResolveInitialActiveBot()
    {
        string saved = PlayerPrefs.GetString(LastSelectedBotPrefKey, "");
        if (!string.IsNullOrEmpty(saved) && Manager.Instance != null && Manager.Instance.FindBotByName(saved) != null)
        {
            CurrentBotId = saved;
            return true;
        }

        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root != null && root.childCount > 0)
        {
            CurrentBotId = root.GetChild(0).name;
            PlayerPrefs.SetString(LastSelectedBotPrefKey, CurrentBotId);
            PlayerPrefs.Save();
            return true;
        }

        OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
        return false;
    }

    public ChatViewModel GetChat(string chatId)
    {
        if (chatLookup.TryGetValue(chatId, out var chat))
        {
            return chat;
        }
        return null;
    }
    
    // Notice the new 'isInitialLoad' boolean parameter!
    void ParseChatsJson(string json, bool isInitialLoad)
    {
        if (string.IsNullOrEmpty(json)) return;

        ChatsResponse response = JsonUtility.FromJson<ChatsResponse>(json);
        if (response?.dialogs == null) return;

        // ONLY clear the UI and memory if this is the very first instant load
        if (isInitialLoad)
        {
            Chats.Clear();
            chatLookup.Clear();
            OnChatListCleared?.Invoke(); 
        }

        foreach (var chat in response.dialogs)
        {
            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
            string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data);

            if (chatLookup.TryGetValue(chat.id, out var existingVm))
            {
                // --- THE SMART MERGE ---
                // The chat is already on the screen! Do not destroy the prefab!
                // Just quietly update the text and time. The UI will catch the event and refresh seamlessly.
                existingVm.UpdateLastMessage(lastMsg, unixTime);
            }
            else
            {
                // This is a brand new chat we haven't seen before, spawn it!
                var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime);
                Chats.Add(chatVM);
                chatLookup[chat.id] = chatVM;
                OnChatAdded?.Invoke(chatVM);
            }
        }
    }

    IEnumerator SyncAllChats(string cachePath, string cachedJson)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/chats/filter?profile_id={activeProfileId}";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success) yield break;
        
        string newJson = www.downloadHandler.text;

        if (newJson != cachedJson)
        {
            System.IO.File.WriteAllTextAsync(cachePath, newJson);
            ParseChatsJson(newJson, false); // FALSE = Background sync, DO NOT CLEAR THE UI!
        }
    }
    
// --- ADDED the bool parameter here! ---
    public void ShowChatList(bool instant = false)
    {
        bool isAlreadySlidingOut = false;
        if (SwipeToBack.Instance != null && SwipeToBack.Instance.chatPanelToSlide != null)
        {
            isAlreadySlidingOut = SwipeToBack.Instance.chatPanelToSlide.anchoredPosition.x > 50f;
        }

        if (SwipeToBack.Instance != null && MessageListPanel.activeSelf && !isAlreadySlidingOut)
        {
            // Pass the parameter to the swipe script!
            SwipeToBack.Instance.SlideOutToChatList(instant);
        }
        else
        {
            MessageListPanel.SetActive(false);
        }

        if (AudioController.Instance != null)
        {
            AudioController.Instance.Stop();
        }
    }

    public void SelectChat(string chatId)
    {
        if (ScrollClickBlocker.IsBlocking) return;
        
        currentChatId = chatId;
        currentPage = 1;
        seenMessageIds.Clear();

        // --- 1. WAKE UP THE PANEL FIRST ---
        // We must force the panel to wake up so its scripts run OnEnable() and listen for events!
        if (SwipeToBack.Instance != null && SwipeToBack.Instance.chatPanelToSlide != null)
        {
            SwipeToBack.Instance.chatPanelToSlide.gameObject.SetActive(true);
        }
        else
        {
            MessageListPanel.SetActive(true);
        }

        // --- 2. CLEAR THE OLD CHAT ---
        // Now that the UI is awake, it will hear this command and instantly delete the old prefabs!
        OnChatSelected?.Invoke(chatId);

        // --- 3. ANIMATE AND LOAD ---
        if (SwipeToBack.Instance != null)
        {
            // Trigger the smooth slide, and spawn the messages when it finishes
            SwipeToBack.Instance.SlideInToMessages(() => 
            {
                LoadMessagesForChat(chatId);
            });
        }
        else
        {
            LoadMessagesForChat(chatId);
        }
    }

    // --- NEW: The heavy lifting is now safely isolated here! ---
    private void LoadMessagesForChat(string chatId)
    {
        // Safety check: Make sure the user didn't swipe back out before the animation finished!
        if (currentChatId != chatId) return;

        List<MessageViewModel> cachedMessages = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);

        if (cachedMessages != null && cachedMessages.Count > 0)
        {
            // 1. INSTANT LOAD: Register the cached IDs and draw the UI immediately!
            foreach (var msg in cachedMessages) seenMessageIds.Add(msg.messageId);
            OnBatchMessagesLoaded?.Invoke(cachedMessages, false, true);

            // 2. BACKGROUND SYNC: Quietly check for missed messages
            StartCoroutine(SyncLatestMessages(chatId, cachedMessages));
        }
        else
        {
            // 3. NO CACHE: This is a brand new chat, do a normal fetch
            StartCoroutine(GetMessagesRoutine(chatId, 1, (newMessages, hasMore) => 
            {
                if (newMessages.Count > 0) ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);
                OnBatchMessagesLoaded?.Invoke(newMessages, false, hasMore);
            }));
        }
    }
    
    IEnumerator SyncLatestMessages(string chatId, List<MessageViewModel> cachedList)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            Debug.LogWarning("[ChatManager] SyncLatestMessages aborted: no valid profile for active bot.");
            yield break;
        }
        string escapedId = UnityWebRequest.EscapeURL(chatId);
        // We strictly only check offset 0 (the absolute newest messages)
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success) yield break;

        List<MessageViewModel> newMessages = new List<MessageViewModel>();
        bool hasChanges = false;

        try 
        {
            MessagesResponseRaw response = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);

            if (response?.messages != null)
            {
                foreach (var raw in response.messages)
                {
                    // If the ID isn't in our seenMessageIds, it's a BRAND NEW message we missed!
                    if (seenMessageIds.Add(raw.id))
                    {
                        NormalizedMessage norm = Normalize(raw);
                        if (norm.messageType != MessageType.Unknown)
                        {
                            newMessages.Add(CreateViewModel(norm));
                            hasChanges = true;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Sync JSON Parse Error: {e.Message}");
        }

        // If we found new messages while the app was closed, merge them!
        if (hasChanges && newMessages.Count > 0)
        {
            // Add the old cached messages to the bottom of the brand new ones
            newMessages.AddRange(cachedList);

            // Keep the cache file size healthy (max 100 messages)
            if (newMessages.Count > 100) newMessages = newMessages.GetRange(0, 100);

            // Re-register everything so LoadNextPage() works perfectly later
            seenMessageIds.Clear();
            foreach (var m in newMessages) seenMessageIds.Add(m.messageId);

            // Save the newly merged list permanently
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);

            // Refresh the UI with the fully up-to-date list!
            OnBatchMessagesLoaded?.Invoke(newMessages, false, true);
        }
    }

    public void LoadNextPage()
    {
        if (string.IsNullOrEmpty(currentChatId)) return;

        currentPage++;

        StartCoroutine(GetMessagesRoutine(currentChatId, currentPage, (messages, hasMore) => 
        {
            OnBatchMessagesLoaded?.Invoke(messages, true, hasMore);
        }));
    }

// Notice the Action signature now includes a bool!
    IEnumerator GetMessagesRoutine(string chatId, int page, Action<List<MessageViewModel>, bool> onComplete)
    {
        int offset = (page - 1) * MessagesPerPage;

        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onComplete?.Invoke(new List<MessageViewModel>(), false);
            yield break;
        }
        string escapedId = UnityWebRequest.EscapeURL(chatId);
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset={offset}";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

#if UNITY_EDITOR
        var text = www.downloadHandler.text;
        System.IO.File.WriteAllText(
            Application.persistentDataPath + "/response.txt",
            text
        );
        Debug.Log("Saved to: " + Application.persistentDataPath);
#endif

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[ChatManager] Error loading messages: {www.error}");
            onComplete?.Invoke(new List<MessageViewModel>(), false); 
            yield break;
        }
        
        List<MessageViewModel> loadedMessages = new List<MessageViewModel>();
        int rawServerCount = 0; // --- THE FIX: Track the raw count before filtering! ---
        
        try 
        {
            MessagesResponseRaw response = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);

            if (response?.messages != null)
            {
                foreach (var raw in response.messages)
                {
                    rawServerCount++; // Count every single message the server gave us

                    if (string.IsNullOrEmpty(raw.id) || !seenMessageIds.Add(raw.id)) continue;

                    NormalizedMessage norm = Normalize(raw);
                    if (norm.messageType == MessageType.Unknown) continue;
                    
                    MessageViewModel vm = CreateViewModel(norm);
                    loadedMessages.Add(vm);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON Parse Error: {e.Message}");
        }

        bool hasMore = rawServerCount >= MessagesPerPage;

        // --- THE GHOST PAGE FIX ---
        // If the server gave us 50 messages, but we filtered ALL of them out,
        // we must silently fetch the next page so the UI doesn't freeze!
        if (loadedMessages.Count == 0 && hasMore)
        {
            currentPage++;
            StartCoroutine(GetMessagesRoutine(chatId, currentPage, onComplete));
            yield break;
        }

        onComplete?.Invoke(loadedMessages, hasMore);
    }

    MessageViewModel CreateViewModel(NormalizedMessage msg)
    {
        return new MessageViewModel
        {
            messageId = msg.id,
            chatId = msg.chatId,
            senderName = msg.senderName,
            type = msg.messageType,
            text = msg.text,
            fileName = msg.fileName, // <--- Add this line!
            mediaUrl = msg.mediaUrl,
            thumbnailUrl = msg.thumbnailUrl,
            aspectRatio = msg.aspectRatio,
            expireTime = msg.expireTime,
            mimeType = msg.mimeType,
            videoUrl = msg.videoUrl,
            duration = msg.duration,
            isSticker = msg.isSticker,
            isIncoming = !msg.fromMe,
            timestamp = msg.time,
            fileSize = msg.fileSize,
            pageCount = msg.pageCount
        };
    }

    NormalizedMessage Normalize(RawMessage raw)
    {
        NormalizedMessage msg = new NormalizedMessage
        {
            id = raw.id,
            chatId = raw.chatId,
            senderName = raw.senderName,
            messageType = ParseMessageType(raw.type),
            fromMe = raw.fromMe,
            time = raw.time
        };

        // --- 1. SEPARATE TEXT FROM CAPTIONS ---
        if (msg.messageType == MessageType.Chat)
        {
            msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw.body?.ToString());
        }
        else 
        {
            // Extract hidden captions for media objects!
            string captionStr = raw.caption?.ToString();
            if (string.IsNullOrEmpty(captionStr) && raw.body is JObject bodyObj && bodyObj["caption"] != null)
            {
                captionStr = bodyObj["caption"].ToString();
            }

            if (!string.IsNullOrEmpty(captionStr))
            {
                msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(captionStr);
            }
        }

        JToken dimSource = raw.mediaInfo ?? raw.body;
        if (dimSource is JObject dObj)
        {
            float width = 0, height = 0;
            if (dObj["width"] != null) float.TryParse(dObj["width"].ToString(), out width);
            if (dObj["height"] != null) float.TryParse(dObj["height"].ToString(), out height);
        
            if (width > 0 && height > 0) 
                msg.aspectRatio = width / height;
            else
                msg.aspectRatio = 1.0f; 
        }
        else 
        {
            msg.aspectRatio = 1.0f; 
        }
        
if (msg.messageType == MessageType.Video)
        {
            // 1. ALWAYS grab the thumbnail first!
            if (raw.body is JObject bodyObj && bodyObj["JPEGThumbnail"] != null)
            {
                // --- THE CACHE FIX: Remove Base64 from the JSON! ---
                try 
                {
                    string b64 = bodyObj["JPEGThumbnail"].ToString();
                    byte[] bytes = Convert.FromBase64String(b64);
                    string thumbUrl = "thumb://" + msg.id; // Tiny fake URL
                    
                    if (MediaCacheManager.Instance != null) 
                        MediaCacheManager.Instance.SaveImageToCache(thumbUrl, bytes);
                        
                    msg.thumbnailUrl = thumbUrl; 
                }
                catch { msg.thumbnailUrl = ""; }
            }

            // 2. THEN grab the S3 URL
            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.videoUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }
        else if (msg.messageType == MessageType.Image || msg.messageType == MessageType.Sticker)
        {
            if (msg.messageType == MessageType.Sticker) 
            {
                msg.isSticker = true;
                if (msg.aspectRatio <= 0) msg.aspectRatio = 1.0f;
            }

            // 1. ALWAYS grab the thumbnail first!
            if (raw.body is JObject bodyObj)
            {
                if (bodyObj["JPEGThumbnail"] != null) 
                {
                    // --- THE CACHE FIX: Remove Base64 from the JSON! ---
                    try 
                    {
                        string b64 = bodyObj["JPEGThumbnail"].ToString();
                        
                        // 1. Strip off HTML data prefixes if they exist
                        if (b64.Contains(",")) b64 = b64.Substring(b64.IndexOf(",") + 1);
                        
                        // 2. Clean out hidden spaces or line breaks that APIs sometimes leave behind
                        b64 = b64.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                        
                        byte[] bytes = Convert.FromBase64String(b64);
                        string thumbUrl = "thumb://" + msg.id; 
                        
                        if (MediaCacheManager.Instance != null) 
                            MediaCacheManager.Instance.SaveImageToCache(thumbUrl, bytes);
                            
                        msg.thumbnailUrl = thumbUrl; 
                    }
                    catch (Exception e) 
                    { 
                        Debug.LogWarning("Failed to decode thumbnail: " + e.Message);
                        msg.thumbnailUrl = ""; 
                    }
                }
                
                if (bodyObj["url"] != null) msg.mediaUrl = bodyObj["url"].ToString();
            }

            // 2. THEN grab the best HD S3 URL to overwrite the fallback
            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.mediaUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }
        else if (msg.messageType == MessageType.Audio || msg.messageType == MessageType.Voice)
        {
             if (raw.s3Info is JObject s3 && s3["url"] != null)
             {
                 msg.mediaUrl = s3["url"].ToString(); 
                 if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
             }
             
             if (raw.mediaInfo is JObject info && info["duration"] != null)
                 int.TryParse(info["duration"].ToString(), out msg.duration);
        }
        else if (msg.messageType == MessageType.Document)
        {
            if (raw.body is JObject bodyObj)
            {
                // THE FIX: Save to fileName! Leave msg.text alone so the caption survives!
                string fName = bodyObj["fileName"]?.ToString() ?? bodyObj["title"]?.ToString();
                msg.fileName = fName; 
                
                msg.mimeType = bodyObj["mimetype"]?.ToString();

                // --- ADDED: Extract the file length and page count right here! ---
                if (bodyObj["fileLength"] != null)
                {
                    long.TryParse(bodyObj["fileLength"].ToString(), out msg.fileSize);
                }
                 
                if (bodyObj["pageCount"] != null)
                {
                    int.TryParse(bodyObj["pageCount"].ToString(), out msg.pageCount);
                }
            }

            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.mediaUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }

        return msg;
    }
    
    MessageType ParseMessageType(string type)
    {
        return type switch
        {
            "chat" => MessageType.Chat,
            "image" => MessageType.Image,
            "video" => MessageType.Video,
            "audio" => MessageType.Audio,
            "ptt" => MessageType.Voice,
            "sticker" => MessageType.Sticker,
            "document" => MessageType.Document,
            _ => MessageType.Unknown
        };
    }

    public void DownloadMediaForMessage(string messageId, Action<string> onSuccess, Action onFailure)
    {
        StartCoroutine(DownloadMediaRoutine(messageId, onSuccess, onFailure));
    }

    IEnumerator DownloadMediaRoutine(string messageId, Action<string> onSuccess, Action onFailure)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onFailure?.Invoke();
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/message/media/download?profile_id={activeProfileId}&message_id={messageId}";
        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // if (www.responseCode == 400) Debug.LogWarning($"[ChatManager] Media expired: {messageId}");
            onFailure?.Invoke();
            yield break;
        }
        
        try
        {
            JObject json = JObject.Parse(www.downloadHandler.text);
            string fileLink = json["file_link"]?.ToString();
            string fileB64 = json["file_b64"]?.ToString();

            if (!string.IsNullOrEmpty(fileLink)) onSuccess?.Invoke(fileLink);
            else if (!string.IsNullOrEmpty(fileB64)) onSuccess?.Invoke("base64://" + fileB64);
            else onFailure?.Invoke();
        }
        catch { onFailure?.Invoke(); }
    }
    
    public void SendTextMessage(string text)
    {
        // Don't send empty blanks or if no chat is selected
        if (string.IsNullOrEmpty(currentChatId) || string.IsNullOrWhiteSpace(text)) return;
        
        StartCoroutine(SendTextMessageRoutine(currentChatId, text));
    }

IEnumerator SendTextMessageRoutine(string chatId, string text)
{
    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId))
    {
        Debug.LogWarning("[ChatManager] SendTextMessageRoutine aborted: no valid profile for active bot.");
        yield break;
    }

    string recipient = chatId;
    if (recipient.EndsWith("@c.us")) recipient = recipient.Replace("@c.us", "");

    // --- INSTANT UI: Fire before ANY network call ---
    string tempId = "sending_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    seenMessageIds.Add(tempId);

    var instantMessage = new MessageViewModel
    {
        messageId = tempId,
        chatId = chatId,
        senderName = "Me",
        type = MessageType.Chat,
        text = text,
        isIncoming = false,
        timestamp = now
    };

    OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { instantMessage });

    var chatVm = GetChat(chatId);
    if (chatVm != null) chatVm.UpdateLastMessage(text, now);

    List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);
    cachedList.Add(instantMessage);
    ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cachedList);

    // --- BACKGROUND: Send to server silently ---
    string url = $"https://wappi.pro/api/sync/message/send?profile_id={activeProfileId}";
    var requestData = new WappiSendTextRequest { body = text, recipient = recipient };
    string jsonPayload = JsonConvert.SerializeObject(requestData);

    using UnityWebRequest www = new UnityWebRequest(url, "POST");
    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
    www.downloadHandler = new DownloadHandlerBuffer();
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"[Wappi] Failed to send: {www.error}\n{www.downloadHandler.text}");
        yield break;
    }

    try
    {
        var response = JsonConvert.DeserializeObject<WappiSendTextResponse>(www.downloadHandler.text);
        if (response != null && response.status == "done" && !string.IsNullOrEmpty(response.message_id))
        {
            // Silently swap temp ID for the real server ID
            seenMessageIds.Remove(tempId);
            seenMessageIds.Add(response.message_id);

            var cache = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);
            var msg = cache.Find(m => m.messageId == tempId);
            if (msg != null)
            {
                msg.messageId = response.message_id;
                if (response.timestamp > 0) msg.timestamp = response.timestamp;
                ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cache);
            }
        }
    }
    catch (Exception e)
    {
        Debug.LogError($"[Wappi] Response parse error: {e.Message}");
    }
}
}

[Serializable]
public class WappiSendTextRequest
{
    public string body;
    public string recipient;
}

[Serializable]
public class WappiSendTextResponse
{
    public string status;
    public string message_id;
    public long timestamp;
}

public enum EmptyStateReason
{
    NoBotsExist,
    BotHasNoWhatsApp,
}