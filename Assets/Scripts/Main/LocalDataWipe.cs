using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Disk half of «Удалить все данные» (Profile → Аккаунт). PlayerPrefs are wiped
/// separately via PlayerPrefs.DeleteAll (per-chat SemiAuto keys are not
/// enumerable), and server/scene teardown goes through Bot.DeleteBot — this
/// class only knows which local files hold user data. Target list is pure so
/// tests can pin the wipe surface.
/// </summary>
public static class LocalDataWipe
{
    /// <summary>
    /// Everything under persistentDataPath that holds user data: the per-bot
    /// cache tree, raw API response dumps, scraped link previews, and the
    /// legacy pre-BotCache chat-list cache (may exist on old installs).
    /// Root-level sticker files are matched separately via IsStickerFile.
    /// </summary>
    public static List<string> DiskWipeTargets(string persistentDataPath) => new List<string>
    {
        Path.Combine(persistentDataPath, "BotCache"),
        Path.Combine(persistentDataPath, "response.txt"),
        Path.Combine(persistentDataPath, "link_metadata.json"),
        Path.Combine(persistentDataPath, "all_chats_cache.json"),
    };

    /// <summary>Matches WappiUnitySync's root-level sticker downloads (sticker_{id}.webp).</summary>
    public static bool IsStickerFile(string fileName) =>
        !string.IsNullOrEmpty(fileName)
        && fileName.StartsWith("sticker_", StringComparison.Ordinal)
        && fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    public static void DeleteDiskData(string persistentDataPath)
    {
        if (string.IsNullOrEmpty(persistentDataPath)) return;

        foreach (string target in DiskWipeTargets(persistentDataPath))
        {
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                else if (File.Exists(target)) File.Delete(target);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalDataWipe] Could not delete {target}: {e.Message}");
            }
        }

        try
        {
            foreach (string file in Directory.GetFiles(persistentDataPath))
            {
                if (!IsStickerFile(Path.GetFileName(file))) continue;
                try { File.Delete(file); }
                catch (Exception e) { Debug.LogWarning($"[LocalDataWipe] Could not delete {file}: {e.Message}"); }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LocalDataWipe] Sticker sweep failed: {e.Message}");
        }
    }
}
