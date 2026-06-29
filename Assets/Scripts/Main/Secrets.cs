using UnityEngine;
using System.IO;

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
    private static SecretsData _data;

    public static SecretsData Data
    {
        get
        {
            if (_data == null)
                Load();
            return _data;
        }
    }

    private static void Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "secrets.json");
        if (!File.Exists(path))
        {
            Debug.LogError("secrets.json not found in StreamingAssets. Copy secrets.json.example and fill in your keys.");
            _data = new SecretsData();
            return;
        }
        string json = File.ReadAllText(path);
        _data = JsonUtility.FromJson<SecretsData>(json);
    }
}
