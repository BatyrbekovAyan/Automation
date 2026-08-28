using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the fabricated screenshot dataset from Tools/store/fixtures/demo-data.json.
///
/// Seeding MUST happen from inside the Editor process. A running Unity caches PlayerPrefs
/// in memory and flushes its own copy over the plist when Play Mode exits, so an external
/// `defaults write` silently loses: measured 2026-08-28, 99 externally seeded keys were
/// down to 20 by the time the capture ran, and the app photographed its empty state.
/// Writing through PlayerPrefs here keeps the Editor's in-memory copy and the file in
/// agreement, and Application.persistentDataPath removes the path guesswork for the caches.
///
/// Regenerate the fixture with: python3 Tools/store/seed-demo-data.py
/// </summary>
public static class StoreDemoDataSeeder
{
    private const string FixturePath = "Tools/store/fixtures/demo-data.json";

    [MenuItem("Tools/Store/Seed Demo Data")]
    public static void SeedMenu() => Seed();

    /// <returns>true when the dataset was applied.</returns>
    public static bool Seed()
    {
        if (!File.Exists(FixturePath))
        {
            Debug.LogError($"[StoreSeed] нет фикстуры {FixturePath} — " +
                           $"сгенерируй: python3 Tools/store/seed-demo-data.py");
            return false;
        }

        JObject root;
        try
        {
            root = JObject.Parse(File.ReadAllText(FixturePath));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StoreSeed] фикстура не разобралась: {e.Message}");
            return false;
        }

        int ints = 0, strings = 0;
        if (root["prefs"] is JObject prefs)
        {
            foreach (var pair in prefs)
            {
                if (pair.Value is not JValue value) continue;
                if (value.Type == JTokenType.Integer)
                {
                    PlayerPrefs.SetInt(pair.Key, value.Value<int>());
                    ints++;
                }
                else
                {
                    PlayerPrefs.SetString(pair.Key, value.Value<string>() ?? string.Empty);
                    strings++;
                }
            }
            PlayerPrefs.Save();
        }

        int written = 0;
        if (root["files"] is JObject files)
        {
            foreach (var pair in files)
            {
                string full = Path.Combine(Application.persistentDataPath, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                // Compact, no indentation — these files mimic server payloads and app caches.
                File.WriteAllText(full, pair.Value.ToString(Newtonsoft.Json.Formatting.None));
                written++;
            }
        }

        Debug.Log($"[StoreSeed] PlayerPrefs: {ints} int + {strings} string; файлов: {written} " +
                  $"→ {Application.persistentDataPath}");

        // Read the seeded history back through the app's OWN loader. A fixture that writes
        // fine but does not deserialize renders as an empty thread, which is indistinguishable
        // from a successful capture until someone opens the PNG.
        string botRoot = Path.Combine(Application.persistentDataPath, "BotCache", "Bot0");
        foreach (string chatId in new[] { "77000000011@c.us", "77000000012@c.us" })
        {
            var loaded = ChatHistoryCache.LoadHistory(botRoot, chatId);
            if (loaded == null || loaded.Count == 0)
                Debug.LogError($"[StoreSeed] история {chatId} НЕ читается загрузчиком приложения — " +
                               $"тред снимется пустым");
            else
                Debug.Log($"[StoreSeed] история {chatId}: {loaded.Count} сообщений, " +
                          $"первое «{loaded[0].text?.Substring(0, System.Math.Min(30, loaded[0].text.Length))}»");
        }
        return true;
    }
}
