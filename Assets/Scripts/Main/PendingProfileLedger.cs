using UnityEngine;

/// <summary>
/// Persistent ledger of the last Wappi profile created but not yet claimed by a
/// bot (wizard mid-flight, or a settings re-auth in progress). While an entry is
/// pending, the profile is not referenced by any completed bot state and is safe
/// to delete at any time — Manager settles the ledger at quit time
/// (OnApplicationQuit) and sweeps it again on the next launch (Start).
/// The PlayerPrefs keys predate this class and are kept verbatim so pending
/// state written by older builds still settles after an update.
/// </summary>
public static class PendingProfileLedger
{
    public const string NoProfile = "-1";

    private const string WhatsappIdKey = "lastCreatedWhatsappProfileId";
    private const string WhatsappSavedKey = "lastCreatedWhatsappProfileIdSaved";
    private const string TelegramIdKey = "lastCreatedTelegramProfileId";
    private const string TelegramSavedKey = "lastCreatedTelegramProfileIdSaved";

    // ── WhatsApp ──

    public static void MarkWhatsappPending(string profileId) => MarkPending(WhatsappIdKey, WhatsappSavedKey, profileId);

    public static void MarkWhatsappClaimed() => MarkClaimed(WhatsappSavedKey);

    public static void ClearWhatsappIfMatches(string profileId) => ClearIfMatches(WhatsappIdKey, WhatsappSavedKey, profileId);

    public static bool TryGetPendingWhatsapp(out string profileId) => TryGetPending(WhatsappIdKey, WhatsappSavedKey, out profileId);

    // ── Telegram ──

    public static void MarkTelegramPending(string profileId) => MarkPending(TelegramIdKey, TelegramSavedKey, profileId);

    public static void MarkTelegramClaimed() => MarkClaimed(TelegramSavedKey);

    public static void ClearTelegramIfMatches(string profileId) => ClearIfMatches(TelegramIdKey, TelegramSavedKey, profileId);

    public static bool TryGetPendingTelegram(out string profileId) => TryGetPending(TelegramIdKey, TelegramSavedKey, out profileId);

    // ── Shared implementation ──

    private static void MarkPending(string idKey, string savedKey, string profileId)
    {
        if (string.IsNullOrEmpty(profileId) || profileId.Equals(NoProfile)) return;

        PlayerPrefs.SetString(idKey, profileId);
        PlayerPrefs.SetInt(savedKey, 0);
        // Flush immediately — a mobile process can be killed without any
        // callback, and an unflushed pending entry is an orphan no sweep can
        // ever find.
        PlayerPrefs.Save();
    }

    private static void MarkClaimed(string savedKey)
    {
        PlayerPrefs.SetInt(savedKey, 1);
        PlayerPrefs.Save();
    }

    private static void ClearIfMatches(string idKey, string savedKey, string profileId)
    {
        // Only the recorded profile may settle its own entry — an unrelated
        // delete (e.g. Bot.DeleteBot on an old profile) must not wipe a
        // pending entry that still needs sweeping.
        if (!PlayerPrefs.GetString(idKey, NoProfile).Equals(profileId)) return;

        PlayerPrefs.SetString(idKey, NoProfile);
        PlayerPrefs.SetInt(savedKey, 1);
        PlayerPrefs.Save();
    }

    private static bool TryGetPending(string idKey, string savedKey, out string profileId)
    {
        profileId = PlayerPrefs.GetString(idKey, NoProfile);
        // Saved defaults to 1 ("claimed") so fresh installs never sweep.
        bool pending = PlayerPrefs.GetInt(savedKey, 1) == 0 && !profileId.Equals(NoProfile);
        if (!pending) profileId = NoProfile;
        return pending;
    }
}
