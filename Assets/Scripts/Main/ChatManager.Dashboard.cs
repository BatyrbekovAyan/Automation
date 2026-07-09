using UnityEngine;

public partial class ChatManager
{
    // Read helper for DashboardPage: resolve a chat's live display title by id.
    // Lives in a ChatManager partial so it can read the private `chatLookup`
    // (Dictionary<string, ChatViewModel>) that DashboardPage can't reach directly.
    public bool TryGetChatTitle(string chatId, out string title)
    {
        title = null;
        if (chatLookup != null && chatLookup.TryGetValue(chatId, out var vm) && vm != null)
        { title = vm.Title; return true; }
        return false;
    }

    // Local last-activity time (unix SECONDS) for the dashboard's "local time wins":
    // reflects the newest message in the chat — including owner-typed manual replies
    // the bot transcript (server outcome) never sees.
    public bool TryGetChatLastActivitySec(string chatId, out long lastActivitySec)
    {
        lastActivitySec = 0;
        if (chatLookup != null && chatLookup.TryGetValue(chatId, out var vm) && vm != null)
        { lastActivitySec = vm.LastMessageTime; return true; }
        return false;
    }

    // Real chat avatar for the dashboard row, reusing what the chat list already loaded:
    // an in-memory sprite if present, else the on-disk avatar cache (same synchronous
    // path as ChatItemView). No network fetch here — the dashboard falls back to the
    // colored-initial default when nothing is cached.
    public bool TryGetChatAvatar(string chatId, out Sprite sprite)
    {
        sprite = null;
        if (chatLookup == null || !chatLookup.TryGetValue(chatId, out var vm) || vm == null) return false;
        if (vm.AvatarSprite != null) { sprite = vm.AvatarSprite; return true; }
        if (string.IsNullOrEmpty(vm.AvatarUrl) || MediaCacheManager.Instance == null
            || !MediaCacheManager.Instance.IsImageCached(vm.AvatarUrl)) return false;
        try
        {
            string path = MediaCacheManager.Instance.GetFilePathFromUrl(vm.AvatarUrl);
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
            {
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                vm.AvatarSprite = sprite;   // cache back so this and the chat list reuse it
                return true;
            }
            Object.Destroy(tex);
        }
        catch (System.Exception e) { Debug.LogWarning($"[Dashboard] avatar cache load failed: {e.Message}"); }
        return false;
    }
}
