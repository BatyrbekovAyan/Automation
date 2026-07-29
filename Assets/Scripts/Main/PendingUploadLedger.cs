using System.Collections.Generic;
using UnityEngine;

/// <summary>One upload that was sent but not yet confirmed recorded on-device.</summary>
public struct PendingUploadEntry
{
    public string FileId;       // matches metadata.fileId on every RAG chunk the upload created
    public string BotName;
    public string ContentType;  // "product" | "service"
}

/// <summary>
/// Persistent ledger of price-list uploads that are in flight.
///
/// UploadCenter keeps an upload alive across leaving the settings screen, but
/// not across losing the process. A swipe-kill — or the OS reclaiming a
/// backgrounded app while the upload coroutine sits frozen mid-request — takes
/// the in-memory job with it, while n8n finishes ingesting regardless. The
/// chunks then sit in the RAG store under a fileId nothing on-device remembers:
/// the bot answers from a price list the app cannot list, cannot delete, and
/// will happily index a second time on the next upload.
///
/// So the fileId is written here before the request goes out and cleared once
/// the file is recorded, and Manager sweeps whatever is left at launch — the
/// same discipline PendingProfileLedger applies to orphaned Wappi profiles.
/// Unlike that one this is a LIST: a multi-file pick starts one upload per file.
/// </summary>
public static class PendingUploadLedger
{
    private const string CountKey = "PendingUploadsNumber";
    private const string ItemPrefix = "PendingUpload";

    /// <summary>
    /// Records an upload as in flight. Call immediately before sending — every
    /// failure that happens earlier (unreadable file, bad format) creates no
    /// chunks and so must create no entry.
    /// </summary>
    public static void Add(string fileId, string botName, string contentType)
    {
        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType))
            return;

        var entries = LoadAll();
        if (entries.FindIndex(entry => entry.FileId == fileId) >= 0) return; // retry of the same attempt

        entries.Add(new PendingUploadEntry { FileId = fileId, BotName = botName, ContentType = contentType });
        Persist(entries);
    }

    /// <summary>Settles an entry once its file is recorded (or swept). True if one was removed.</summary>
    public static bool Remove(string fileId)
    {
        if (string.IsNullOrEmpty(fileId)) return false;

        var entries = LoadAll();
        int index = entries.FindIndex(entry => entry.FileId == fileId);
        if (index < 0) return false;

        entries.RemoveAt(index);
        Persist(entries);
        return true;
    }

    public static List<PendingUploadEntry> LoadAll()
    {
        var entries = new List<PendingUploadEntry>();
        int count = PlayerPrefs.GetInt(CountKey, 0);

        for (int i = 0; i < count; i++)
        {
            string fileId = PlayerPrefs.GetString($"{ItemPrefix}{i}", "");
            if (string.IsNullOrEmpty(fileId)) continue;

            entries.Add(new PendingUploadEntry
            {
                FileId = fileId,
                BotName = PlayerPrefs.GetString($"{ItemPrefix}{i}Bot", ""),
                ContentType = PlayerPrefs.GetString($"{ItemPrefix}{i}Type", "")
            });
        }
        return entries;
    }

    /// <summary>
    /// Whether this entry's chunks are genuinely unreachable and safe to delete.
    ///
    /// The process can also die AFTER the local record is written but before the
    /// entry is cleared. Sweeping on the ledger alone would then delete the
    /// chunks of a file the user still sees listed — the bot would silently
    /// forget a price list. A recorded fileId is therefore never an orphan,
    /// whatever the ledger still says.
    /// </summary>
    public static bool IsOrphan(PendingUploadEntry entry)
    {
        if (string.IsNullOrEmpty(entry.BotName) || string.IsNullOrEmpty(entry.ContentType))
            return false; // malformed entry — never delete on a guess

        return UploadedFilesStore.Load(entry.BotName, entry.ContentType)
                                 .FindIndex(stored => stored.Id == entry.FileId) < 0;
    }

    public static void Clear()
    {
        int count = PlayerPrefs.GetInt(CountKey, 0);
        for (int i = 0; i < count; i++) DeleteItemKeys(i);
        PlayerPrefs.DeleteKey(CountKey);
        PlayerPrefs.Save();
    }

    // Writes contiguously from index 0 and deletes any orphan tail keys left by
    // a shrink, mirroring UploadedFilesStore.Persist.
    private static void Persist(List<PendingUploadEntry> entries)
    {
        int oldCount = PlayerPrefs.GetInt(CountKey, 0);

        for (int i = 0; i < entries.Count; i++)
        {
            PlayerPrefs.SetString($"{ItemPrefix}{i}", entries[i].FileId);
            PlayerPrefs.SetString($"{ItemPrefix}{i}Bot", entries[i].BotName ?? "");
            PlayerPrefs.SetString($"{ItemPrefix}{i}Type", entries[i].ContentType ?? "");
        }
        for (int i = entries.Count; i < oldCount; i++) DeleteItemKeys(i);

        PlayerPrefs.SetInt(CountKey, entries.Count);
        // Flush immediately — a mobile process can be killed without any
        // callback, and an unflushed pending entry is an orphan no sweep can
        // ever find.
        PlayerPrefs.Save();
    }

    private static void DeleteItemKeys(int index)
    {
        PlayerPrefs.DeleteKey($"{ItemPrefix}{index}");
        PlayerPrefs.DeleteKey($"{ItemPrefix}{index}Bot");
        PlayerPrefs.DeleteKey($"{ItemPrefix}{index}Type");
    }
}
