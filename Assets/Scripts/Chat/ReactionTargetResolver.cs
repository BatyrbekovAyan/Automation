using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Pure resolver for a reaction's target text. Given a fetched message window, finds the
/// reaction (id == reactionId) and its target (id == reaction.stanzaId) and returns the
/// target's display text (caption preferred, else body if it's a string) plus the Wappi
/// type keyword. Empty text+type means "reaction or target not in this window" — the
/// caller treats that as a definitive "show who + emoji only" outcome.
/// </summary>
public static class ReactionTargetResolver
{
    public struct Result { public string text; public string type; }

    public static Result Resolve(IReadOnlyList<RawMessage> messages, string reactionId)
    {
        var empty = new Result { text = "", type = "" };
        if (messages == null || string.IsNullOrEmpty(reactionId)) return empty;

        RawMessage reaction = FindById(messages, reactionId);
        if (reaction == null || string.IsNullOrEmpty(reaction.stanzaId)) return empty;

        RawMessage target = FindById(messages, reaction.stanzaId);
        if (target == null) return empty;

        string text = !string.IsNullOrEmpty(target.caption)
            ? target.caption
            : (target.body != null && target.body.Type == JTokenType.String ? target.body.ToString() : "");
        string type = string.IsNullOrEmpty(target.type) ? "chat" : target.type;
        return new Result { text = text, type = type };
    }

    private static RawMessage FindById(IReadOnlyList<RawMessage> messages, string id)
    {
        for (int i = 0; i < messages.Count; i++)
            if (messages[i] != null && messages[i].id == id) return messages[i];
        return null;
    }
}
