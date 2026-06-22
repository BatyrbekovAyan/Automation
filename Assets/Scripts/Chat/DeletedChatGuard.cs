using System.Collections.Generic;

/// <summary>
/// Records chat ids deleted this session and suppresses their re-appearance
/// while a network sync is in flight. On each server sync, drops guarded ids
/// the server no longer lists (confirming the delete landed).
/// </summary>
public class DeletedChatGuard
{
    private readonly HashSet<string> _deleted = new HashSet<string>();

    /// <summary>Mark a chat id as deleted so it is suppressed from incoming sync data.</summary>
    public void MarkDeleted(string chatId)
    {
        if (!string.IsNullOrEmpty(chatId)) _deleted.Add(chatId);
    }

    /// <summary>Returns true if the chat id should be suppressed from the chat list.</summary>
    public bool ShouldSuppress(string chatId)
        => !string.IsNullOrEmpty(chatId) && _deleted.Contains(chatId);

    /// <summary>Remove a chat id from the guard (e.g. user explicitly re-opens it).</summary>
    public void Clear(string chatId)
    {
        if (!string.IsNullOrEmpty(chatId)) _deleted.Remove(chatId);
    }

    /// <summary>
    /// Drop any guarded id the server no longer lists — the delete is confirmed,
    /// so suppression is no longer needed.
    /// </summary>
    public void ReconcileWithServer(ICollection<string> serverChatIds)
    {
        if (serverChatIds == null) return;
        var toClear = new List<string>();
        foreach (var id in _deleted)
            if (!serverChatIds.Contains(id)) toClear.Add(id);
        foreach (var id in toClear) _deleted.Remove(id);
    }
}
