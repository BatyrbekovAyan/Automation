using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Partial class extension housing ChatManager's active-bot identity, per-bot
// cache root, legacy cache migration, profile resolution, and the load/empty
// state helpers driven by the active bot. Lifecycle entry points (Awake/Start)
// remain in ChatManager.cs and call into these helpers.
public partial class ChatManager
{
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
    /// Deletes the cache subtree for a bot. If that bot was active, falls back
    /// to the first remaining bot or fires NoBotsExist. Called by Bot.DeleteBot.
    /// </summary>
    public void PurgeCacheForBot(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return;

        try
        {
            string botCacheDir = Path.Combine(Application.persistentDataPath, "BotCache", botId);
            if (Directory.Exists(botCacheDir))
            {
                Directory.Delete(botCacheDir, recursive: true);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatManager] PurgeCacheForBot({botId}) failed: {e.Message}");
        }

        if (botId != CurrentBotId) return;

        // The active bot was deleted. Pick the next bot or empty out.
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        Transform next = null;
        if (root != null)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name != botId) { next = child; break; }
            }
        }

        if (next != null)
        {
            // SetActiveBot's early-return guard (`botId == CurrentBotId`) does not
            // fire here because next.name differs from the just-deleted bot we are
            // still nominally "on". SetActiveBot persists, clears list, and refreshes.
            SetActiveBot(next.name);
        }
        else
        {
            CurrentBotId = DefaultBotId;
            PlayerPrefs.DeleteKey(LastSelectedBotPrefKey);
            PlayerPrefs.Save();
            Chats.Clear();
            chatLookup.Clear();
            OnChatListCleared?.Invoke();
            OnEmptyState?.Invoke(EmptyStateReason.NoBotsExist);
        }
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
        // Drop the per-bot outbox cache so the next access against the new bot
        // re-loads from disk instead of returning stale entries from the previous bot.
        _outbox = null;
        PlayerPrefs.SetString(LastSelectedBotPrefKey, botId);
        PlayerPrefs.Save();

        Chats.Clear();
        chatLookup.Clear();
        OnChatListCleared?.Invoke();
        OnActiveBotChanged?.Invoke(botId);

        StopAllCoroutines();        // also cancels in-flight thumbnail extraction coroutines
        ClearVideoThumbQueue();     // reset queue bookkeeping the cancelled coroutines never freed
        ClearMediaDownloadQueue();  // same for the serial media-download worker
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
    /// Returns the current empty-state reason without firing an event. Used by
    /// late-attaching subscribers (e.g., a UI surface that activates after the
    /// initial OnEmptyState fired) to catch up to current state.
    /// Returns null when there is no empty state — i.e., a valid bot is active
    /// with a real profile.
    /// </summary>
    public EmptyStateReason? ComputeCurrentEmptyState()
    {
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root == null || root.childCount == 0)
        {
            return EmptyStateReason.NoBotsExist;
        }

        Bot bot = Manager.Instance.FindBotByName(CurrentBotId);
        if (bot == null || !IsValidProfileId(bot.whatsappProfileId))
        {
            return EmptyStateReason.BotHasNoWhatsApp;
        }

        return null;
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

    /// <summary>
    /// Defer active-bot resolution by one frame so Manager.Start() (which runs after
    /// ChatManager.Start() due to ChatManager's [DefaultExecutionOrder(-100)]) has a
    /// chance to instantiate the bot GameObjects under BotsParent first. Fires
    /// OnActiveBotChanged after resolving so UI subscribers whose OnEnable ran in
    /// frame 0 — while CurrentBotId was still the "_default" sentinel — refresh to
    /// the real bot. Matches SetActiveBot's announce-then-load ordering.
    /// </summary>
    private IEnumerator InitializeActiveBotNextFrame()
    {
        yield return null;
        if (ResolveInitialActiveBot())
        {
            OnActiveBotChanged?.Invoke(CurrentBotId);
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
}
