using UnityEngine;

public static class ChatListCacheEditor
{
    /// <summary>
    /// Returns <paramref name="json"/> with the dialog whose id == <paramref name="chatId"/>
    /// removed. Returns the input unchanged when it's null/empty/unparseable, has no dialogs,
    /// or the chat isn't present. Re-serialized via JsonUtility (only modeled fields preserved —
    /// sufficient: the cache is non-authoritative and the next sync overwrites it).
    /// </summary>
    public static string RemoveChat(string json, string chatId)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(chatId)) return json;

        ChatsResponse response;
        try { response = JsonUtility.FromJson<ChatsResponse>(json); }
        catch { return json; }

        if (response?.dialogs == null) return json;

        int removed = response.dialogs.RemoveAll(d => d != null && d.id == chatId);
        if (removed == 0) return json;

        return JsonUtility.ToJson(response);
    }
}
