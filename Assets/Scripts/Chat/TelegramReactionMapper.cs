using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Maps a Telegram (tapi) message's <c>reactions[]</c> array — which rides ON the target
/// message in every <c>messages/get</c> (SHAPES.md Q3, verdict GO) — into the shared
/// <see cref="MessageReaction"/> display list. Pure/static, null-tolerant, unit-testable.
///
/// Element shape (observed): <c>{reaction, count, user_id, contact_name, type:"emoji"}</c>;
/// the whole array is <c>null</c> when the message has no reactions. tapi carries NO
/// <c>type:"reaction"</c> message rows and <c>stanzaId</c> is always "" — so the WhatsApp
/// live-event / stanzaId / <see cref="ReactionStore"/> transport does not apply here and stays
/// WhatsApp-only. There is also no per-reaction <c>fromMe</c> flag, so mapped reactions carry
/// <c>fromMe=false</c>; the owner's own-reaction highlight/toggle relies on the optimistic
/// send path (<see cref="OutgoingReaction"/>) instead.
/// </summary>
public static class TelegramReactionMapper
{
    /// <summary>
    /// Returns the reactions on a tapi message, or <c>null</c> when there are none (matching
    /// "reactions is null when unreacted", so the no-reaction case is byte-identical to today).
    /// </summary>
    public static List<MessageReaction> Map(JToken reactions)
    {
        if (!(reactions is JArray array) || array.Count == 0) return null;

        var result = new List<MessageReaction>(array.Count);
        foreach (JToken element in array)
        {
            if (!(element is JObject obj)) continue;

            string emoji = obj["reaction"]?.ToString();
            if (string.IsNullOrEmpty(emoji)) continue;   // removal marker / malformed entry

            string userId = obj["user_id"]?.ToString();
            string contact = obj["contact_name"]?.ToString();

            result.Add(new MessageReaction
            {
                emoji = emoji,
                // user_id is the stable per-reactor identity; fall back to an emoji-scoped key
                // so two same-emoji reactors with no id still don't collapse into one.
                reactorKey = !string.IsNullOrEmpty(userId) ? userId : $"{emoji}@tg",
                senderName = contact ?? "",
                fromMe = false,   // tapi carries no per-reaction fromMe flag
                time = 0          // tapi carries no per-reaction timestamp on the sync API
            });
        }

        return result.Count > 0 ? result : null;
    }
}
