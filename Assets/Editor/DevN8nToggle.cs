#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor toggle for Manager.DevN8nBaseUrlKey — points the app (Editor Play
/// Mode) at the local dev n8n instead of prod Cloud. PlayerPrefs in the
/// Editor is the same store Play Mode reads, so this takes effect on the
/// next Play. Device builds are unaffected (their PlayerPrefs is separate).
/// </summary>
public static class DevN8nToggle
{
    private const string LocalUrl = "http://localhost:5678";

    [MenuItem("Tools/n8n/Use LOCAL Dev n8n (localhost:5678)")]
    public static void UseLocal()
    {
        PlayerPrefs.SetString(Manager.DevN8nBaseUrlKey, LocalUrl);
        PlayerPrefs.Save();
        Debug.Log($"[n8n] Editor now points at {LocalUrl} (dev). Prod is untouched.");
    }

    [MenuItem("Tools/n8n/Use PROD n8n (clear override)")]
    public static void UseProd()
    {
        PlayerPrefs.DeleteKey(Manager.DevN8nBaseUrlKey);
        PlayerPrefs.Save();
        Debug.Log("[n8n] Dev override cleared — Editor uses the configured prod n8n.");
    }

    [MenuItem("Tools/n8n/Show Current n8n Target")]
    public static void ShowCurrent()
    {
        string overrideUrl = PlayerPrefs.GetString(Manager.DevN8nBaseUrlKey, "");
        Debug.Log(string.IsNullOrEmpty(overrideUrl)
            ? "[n8n] No dev override — Editor uses the configured prod n8n."
            : $"[n8n] Dev override active: {overrideUrl}");
    }
}
#endif
