using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class GreenApiSecrets
{
    public string apiUrl;
    public string idInstance;
    public string apiTokenInstance;
}

[System.Serializable]
public class SecretsData
{
    public string wappiAuthToken;
    public string n8nAPIKey;
    public string n8nBaseUrl;
    public string telegramBotToken;
    public GreenApiSecrets greenApi;
    public GreenApiSecrets greenApiAvatar;
}

public static class Secrets
{
    // On Android this resolves to a jar URL (jar:file://…/base.apk!/assets/secrets.json):
    // the file is packed inside the APK and System.IO cannot read it — only
    // UnityWebRequest can. iOS/Editor/desktop get a real filesystem path that File reads.
    private static string SecretsPath => Path.Combine(Application.streamingAssetsPath, "secrets.json");

    private static SecretsData _data;

    public static SecretsData Data
    {
        get
        {
            if (_data == null)
                LoadBlocking();
            return _data;
        }
    }

    // Preferred entry point: start this once at app launch (see Manager.Awake) and let it
    // populate the cache asynchronously. On Android it reads the APK via UnityWebRequest
    // without blocking the main thread; everywhere else it reads the file directly in the
    // same frame. Every user-driven API call runs after launch, so it hits a warm cache.
    public static IEnumerator Preload()
    {
        if (_data != null)
            yield break;

#if UNITY_ANDROID && !UNITY_EDITOR
        yield return LoadFromApk();
#else
        LoadFromFile();
#endif
    }

    // Synchronous fallback for the rare case where something reads Data before Preload has
    // finished. iOS/Editor/desktop read the file directly. Android does a bounded blocking
    // read of the APK so secrets are still correct (never silently empty) — Preload() exists
    // precisely so this path is not normally taken.
    private static void LoadBlocking()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        LoadFromApkBlocking();
#else
        LoadFromFile();
#endif
    }

    private static void LoadFromFile()
    {
        if (!File.Exists(SecretsPath))
        {
            Debug.LogError("secrets.json not found in StreamingAssets. Copy secrets.json.example and fill in your keys.");
            _data = EmptyData();
            return;
        }
        Parse(File.ReadAllText(SecretsPath));
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static IEnumerator LoadFromApk()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(SecretsPath))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Secrets] Could not read secrets.json from the APK [{request.responseCode}] {SecretsPath}: {request.error}");
                _data = EmptyData();
                yield break;
            }
            Parse(request.downloadHandler.text);
        }
    }

    private static void LoadFromApkBlocking()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(SecretsPath))
        {
            request.timeout = 30;
            var operation = request.SendWebRequest();

            // StreamingAssets reads complete on a worker thread, so isDone flips without the
            // main loop pumping. Bound the spin on the wall clock (which advances even while
            // the main thread is blocked) so a stall can never hard-hang the app.
            float start = Time.realtimeSinceStartup;
            while (!operation.isDone && Time.realtimeSinceStartup - start < 5f) { }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Secrets] Blocking read of secrets.json failed [{request.responseCode}] {SecretsPath}: {request.error}. Call Secrets.Preload() at startup to avoid this path.");
                _data = EmptyData();
                return;
            }
            Parse(request.downloadHandler.text);
        }
    }
#endif

    private static void Parse(string json)
    {
        _data = JsonUtility.FromJson<SecretsData>(json) ?? EmptyData();

        // Guard nested objects so the Green API getters never NPE on a partial file.
        _data.greenApi ??= new GreenApiSecrets();
        _data.greenApiAvatar ??= new GreenApiSecrets();
    }

    private static SecretsData EmptyData() => new SecretsData
    {
        greenApi = new GreenApiSecrets(),
        greenApiAvatar = new GreenApiSecrets()
    };
}
