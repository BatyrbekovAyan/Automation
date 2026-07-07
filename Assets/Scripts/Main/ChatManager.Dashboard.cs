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
}
