using System.Collections.Generic;
using UnityEngine;

// One uploaded price-list file, as remembered on-device. Plain data — the
// backend (n8n/Supabase RAG) is the source of truth for the actual content;
// this record only drives the in-app "uploaded files" list + per-file delete.
public struct UploadedFileEntry
{
    public string Id;         // GUID minted by the app at upload; matches metadata.fileId in the RAG store
    public string Name;       // original file name incl. extension
    public long Size;         // bytes
    public long DateUnixMs;   // upload time (Unix ms)
}

// Per-bot, per-type (product/service) list of uploaded price lists, persisted in
// PlayerPrefs. Keys follow the Product/Service list convention (see bot-persistence):
// count = "<bot><Type>FilesNumber" (plural + Number), items = "<bot><Type>File<i>"
// (singular prefix + 0-based index) plus "…Name", "…Size", "…Date".
// contentType is the upload tag: "product" or "service".
public static class UploadedFilesStore
{
    public static List<UploadedFileEntry> Load(string botName, string contentType)
    {
        var list = new List<UploadedFileEntry>();
        if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType))
            return list;

        string prefix = ItemPrefix(botName, contentType);
        int count = PlayerPrefs.GetInt(CountKey(botName, contentType), 0);
        for (int i = 0; i < count; i++)
        {
            list.Add(new UploadedFileEntry
            {
                Id = PlayerPrefs.GetString($"{prefix}{i}", ""),
                Name = PlayerPrefs.GetString($"{prefix}{i}Name", ""),
                Size = ParseLong(PlayerPrefs.GetString($"{prefix}{i}Size", "0")),
                DateUnixMs = ParseLong(PlayerPrefs.GetString($"{prefix}{i}Date", "0"))
            });
        }
        return list;
    }

    // Entries of this type whose Name matches exactly. Used by replace-on-reupload:
    // uploading a file with a name that is already in the list supersedes the old
    // upload(s), whose RAG chunks are then deleted by their fileId.
    public static List<UploadedFileEntry> FindByName(string botName, string contentType, string name)
    {
        var matches = new List<UploadedFileEntry>();
        if (string.IsNullOrEmpty(name))
            return matches;

        foreach (var entry in Load(botName, contentType))
            if (entry.Name == name)
                matches.Add(entry);
        return matches;
    }

    public static void Add(string botName, string contentType, UploadedFileEntry entry)
    {
        if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType) || string.IsNullOrEmpty(entry.Id))
            return;

        var list = Load(botName, contentType);
        list.Add(entry);
        Persist(botName, contentType, list);
    }

    // Removes the entry with the given fileId. Returns true if one was removed.
    public static bool Remove(string botName, string contentType, string fileId)
    {
        if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType) || string.IsNullOrEmpty(fileId))
            return false;

        var list = Load(botName, contentType);
        int index = list.FindIndex(e => e.Id == fileId);
        if (index < 0)
            return false;

        list.RemoveAt(index);
        Persist(botName, contentType, list);
        return true;
    }

    // Removes every entry of this type for the bot (used by Bot.DeleteBot teardown).
    public static void Clear(string botName, string contentType)
    {
        if (string.IsNullOrEmpty(botName) || string.IsNullOrEmpty(contentType))
            return;

        string prefix = ItemPrefix(botName, contentType);
        int count = PlayerPrefs.GetInt(CountKey(botName, contentType), 0);
        for (int i = 0; i < count; i++)
            DeleteItemKeys(prefix, i);
        PlayerPrefs.DeleteKey(CountKey(botName, contentType));
        PlayerPrefs.Save();
    }

    // Writes the list contiguously from index 0 and deletes any orphan tail keys
    // left by a shrink, mirroring how Bot.DeleteBot clears list items before the count.
    private static void Persist(string botName, string contentType, List<UploadedFileEntry> list)
    {
        string prefix = ItemPrefix(botName, contentType);
        int oldCount = PlayerPrefs.GetInt(CountKey(botName, contentType), 0);

        for (int i = 0; i < list.Count; i++)
        {
            PlayerPrefs.SetString($"{prefix}{i}", list[i].Id);
            PlayerPrefs.SetString($"{prefix}{i}Name", list[i].Name ?? "");
            PlayerPrefs.SetString($"{prefix}{i}Size", list[i].Size.ToString());
            PlayerPrefs.SetString($"{prefix}{i}Date", list[i].DateUnixMs.ToString());
        }
        for (int i = list.Count; i < oldCount; i++)
            DeleteItemKeys(prefix, i);

        PlayerPrefs.SetInt(CountKey(botName, contentType), list.Count);
        PlayerPrefs.Save();
    }

    private static void DeleteItemKeys(string prefix, int i)
    {
        PlayerPrefs.DeleteKey($"{prefix}{i}");
        PlayerPrefs.DeleteKey($"{prefix}{i}Name");
        PlayerPrefs.DeleteKey($"{prefix}{i}Size");
        PlayerPrefs.DeleteKey($"{prefix}{i}Date");
    }

    private static string CountKey(string botName, string contentType) => $"{botName}{TypePrefix(contentType)}FilesNumber";

    private static string ItemPrefix(string botName, string contentType) => $"{botName}{TypePrefix(contentType)}File";

    private static string TypePrefix(string contentType) => char.ToUpperInvariant(contentType[0]) + contentType.Substring(1);

    private static long ParseLong(string value) => long.TryParse(value, out long parsed) ? parsed : 0;
}
