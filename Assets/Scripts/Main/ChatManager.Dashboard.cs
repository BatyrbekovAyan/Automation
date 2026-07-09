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
}
