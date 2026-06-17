using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure resolver for chat-list row details from a fetched message window. Given the row's
/// last-message id, returns that message's sender display name (the message sender, or for a
/// reaction the reactor) and — when the message is a reaction — the reacted-to target's text
/// and Wappi type. Empty fields mean "not in this window"; the caller treats that as a
/// definitive outcome (no prefix / who+emoji only).
/// </summary>
public static class ReactionTargetResolver
{
    public struct Result { public string text; public string type; public string senderName; }

    public static Result Resolve(IReadOnlyList<RawMessage> messages, string messageId)
    {
        var empty = new Result { text = "", type = "", senderName = "" };
        if (messages == null || string.IsNullOrEmpty(messageId)) return empty;

        RawMessage msg = FindById(messages, messageId);
        if (msg == null) return empty;

        string senderName = msg.senderName ?? "";

        // Reaction rows carry a stanzaId pointing at the reacted-to message; resolve its
        // display text/type. Normal messages have no stanzaId → no target clause.
        string text = "", type = "";
        if (!string.IsNullOrEmpty(msg.stanzaId))
        {
            RawMessage target = FindById(messages, msg.stanzaId);
            if (target != null)
            {
                text = !string.IsNullOrEmpty(target.caption)
                    ? target.caption
                    : (target.body != null && target.body.Type == JTokenType.String ? target.body.ToString() : "");
                type = string.IsNullOrEmpty(target.type) ? "chat" : target.type;
            }
        }

        return new Result { text = text, type = type, senderName = senderName };
    }

    private static RawMessage FindById(IReadOnlyList<RawMessage> messages, string id)
    {
        for (int i = 0; i < messages.Count; i++)
            if (messages[i] != null && messages[i].id == id) return messages[i];
        return null;
    }
}
