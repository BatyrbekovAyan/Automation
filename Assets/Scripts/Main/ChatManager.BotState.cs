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
    /// Per-bot, per-channel cache root. WhatsApp keeps the legacy
    /// {persistentDataPath}/BotCache/{CurrentBotId}/ (byte-identical, no migration);
    /// Telegram nests one hardcoded sub-dir deeper so a dual-channel bot never collides
    /// chats.json. Always exists after this call; safe for callers to write under.
    /// </summary>
    public string GetCacheRoot()
    {
        string botId = SanitizeBotId(CurrentBotId);
        string subDir = ChannelCachePath.SubDir(ActiveChannel);
        string path = string.IsNullOrEmpty(subDir)
            ? Path.Combine(Application.persistentDataPath, "BotCache", botId)
            : Path.Combine(Application.persistentDataPath, "BotCache", botId, subDir);
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

        if (_syncWaitRoutine != null) { StopCoroutine(_syncWaitRoutine); _syncWaitRoutine = null; }

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
        _chatFetchesInFlight = 0;   // counter never decremented for the killed-mid-flight fetches
        _chatListSyncing = false;   // a SyncAllChats killed mid-flight never runs its finally
        ClearVideoThumbQueue();     // reset queue bookkeeping the cancelled coroutines never freed
        ClearMediaDownloadQueue();  // same for the serial media-download worker
        ClearResolveQueues();       // quote/reaction drain workers were just killed; reset their bookkeeping
        _tgOwnUserId = null;        // owner identity is per-profile — never carry it across bots

        // D5: StopAllCoroutines() above killed the open-chat live poll — re-kick it so a bot
        // switch never strands it (guarded against duplicates).
        if (_livePollRoutine != null) StopCoroutine(_livePollRoutine);
        _livePollRoutine = StartCoroutine(OpenChatLivePollRoutine());

        // Restore the bot's persisted channel (auto-selecting the connected one if the
        // persisted channel is unconnected) BEFORE loading so the loaded channel matches.
        ActiveChannel = ResolveChannelForBot(botId);
        BeginLoadForActiveBot();
    }

    /// <summary>
    /// Returns true when profileId is a usable bot profile id — not null/empty
    /// and not the unauthed sentinel (Bot.UnauthedProfileSentinel).
    /// </summary>
    private static bool IsValidProfileId(string profileId)
        => !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Returns the active bot's profile ID for the ACTIVE channel (WhatsApp or Telegram),
    /// or null if missing/sentinel. Coroutines guard on null and abort to avoid sending
    /// malformed requests. Channel-aware but zero call-site churn — every consumer that
    /// used the WhatsApp id now follows ActiveChannel automatically.
    /// </summary>
    private string GetActiveProfileId()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null) return null;
        string profileId = ProfileIdForChannel(bot, ActiveChannel);
        return IsValidProfileId(profileId) ? profileId : null;
    }

    /// <summary>
    /// The empty-state reason for "active channel has no connected profile" — WhatsApp or
    /// Telegram copy depending on ActiveChannel so a Telegram-only bot no longer dead-ends
    /// on the WhatsApp empty state.
    /// </summary>
    private EmptyStateReason NoConnectionEmptyState() =>
        ActiveChannel == ChatChannel.Telegram
            ? EmptyStateReason.BotHasNoTelegram
            : EmptyStateReason.BotHasNoWhatsApp;

    /// <summary>PlayerPrefs key suffix holding a bot's sync-window end (Unix ms).</summary>
    private const string SyncUntilKeySuffix = "WhatsappSyncUntil";

    private static long NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// True when the given bot is still inside its fixed post-creation sync window.
    /// Missing/unparseable key (e.g. bots created before this feature) ⇒ not syncing.
    /// </summary>
    public bool IsWhatsAppSyncing(string botId, out long syncUntilUnixMs)
    {
        syncUntilUnixMs = 0L;
        if (string.IsNullOrEmpty(botId)) return false;
        string raw = PlayerPrefs.GetString(botId + SyncUntilKeySuffix, "0");
        if (!long.TryParse(raw, out syncUntilUnixMs)) { syncUntilUnixMs = 0L; return false; }
        return WhatsAppSyncGate.IsSyncing(syncUntilUnixMs, NowUnixMs());
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
        int botCount = root != null ? root.childCount : 0;

        // Channel-aware: hasChannel reflects the ACTIVE channel's profile; the sync
        // window applies only to WhatsApp (no Telegram window is written at auth).
        bool hasChannel = IsValidProfileId(GetActiveProfileId());
        bool syncing = ActiveChannel == ChatChannel.WhatsApp && hasChannel && IsWhatsAppSyncing(CurrentBotId, out _);

        return ChannelTabStateResolver.Resolve(botCount, hasChannel, syncing) switch
        {
            ChannelTabState.NoBots => EmptyStateReason.NoBotsExist,
            ChannelTabState.NoConnection => NoConnectionEmptyState(),
            _ => (EmptyStateReason?)null, // Syncing / Ready are not empty-card states
        };
    }

    /// <summary>
    /// Resolve the active bot's WhatsApp profile, then load cached chats and
    /// kick off a network sync. Fires OnEmptyState if the bot has no WhatsApp.
    /// </summary>
    private Coroutine _syncWaitRoutine;

    private void BeginLoadForActiveBot()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null || !IsValidProfileId(ProfileIdForChannel(bot, ActiveChannel)))
        {
            OnEmptyState?.Invoke(NoConnectionEmptyState());
            return;
        }

        // Post-creation sync window is a WhatsApp-only concept — no Telegram window is
        // written at auth, so Telegram skips the sync-gate and loads immediately.
        if (ActiveChannel == ChatChannel.WhatsApp && IsWhatsAppSyncing(CurrentBotId, out long syncUntilUnixMs))
        {
            OnWhatsAppSyncing?.Invoke(syncUntilUnixMs);
            if (_syncWaitRoutine != null) StopCoroutine(_syncWaitRoutine);
            _syncWaitRoutine = StartCoroutine(WaitForWhatsAppSyncRoutine(syncUntilUnixMs));
            return;
        }

        LoadChatsForActiveBot();
    }

    private void LoadChatsForActiveBot()
    {
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
    /// Quietly re-sync the active bot's chat list against the server without
    /// clearing the visible list. Called when the user navigates to the WhatsApp
    /// tab so the list stays fresh between bot switches. No-ops when there is no
    /// WhatsApp profile, the post-creation sync window is still open, or a
    /// chat-list sync is already in flight.
    /// </summary>
    public void RefreshActiveBotChats()
    {
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(CurrentBotId) : null;
        if (bot == null || !IsValidProfileId(ProfileIdForChannel(bot, ActiveChannel))) return; // empty card already shown
        if (ActiveChannel == ChatChannel.WhatsApp && IsWhatsAppSyncing(CurrentBotId, out _)) return; // syncing UI owns this (WhatsApp-only window)
        if (_chatListSyncing) return;                                        // collapse duplicate syncs

        string cachePath = Path.Combine(GetCacheRoot(), "chats.json");
        string cachedJson = File.Exists(cachePath) ? File.ReadAllText(cachePath) : "";
        StartCoroutine(SyncAllChats(cachePath, cachedJson));
    }

    private IEnumerator WaitForWhatsAppSyncRoutine(long syncUntilUnixMs)
    {
        while (WhatsAppSyncGate.IsSyncing(syncUntilUnixMs, NowUnixMs()))
            yield return new WaitForSecondsRealtime(1f);

        OnWhatsAppSyncReady?.Invoke();
        _syncWaitRoutine = null;
        LoadChatsForActiveBot();
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
            // Restore the persisted channel (auto-correcting to the connected one) BEFORE
            // announcing/loading, so subscribers reading ActiveChannel in OnActiveBotChanged
            // and the initial load both see the resolved channel.
            ActiveChannel = ResolveChannelForBot(CurrentBotId);
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
