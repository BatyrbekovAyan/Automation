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
/// WhatsApp-only. There is no per-reaction <c>fromMe</c> flag either — instead the OWNER's
/// element is identified by <c>user_id == ownUserId</c> (the owner's profile-user id, learned
/// from <c>fromMe</c> rows' <c>from</c> field, SHAPES.md Q4) and mapped to
/// <see cref="OutgoingReaction.MeReactorKey"/> so the server echo of a just-sent reaction
/// lands AS "me" — keeping toggle-off and the quick-bar highlight working after the echo
/// (05-06-REVIEW WR-01). Unidentified reactors stay keyed by their <c>user_id</c>.
/// </summary>
public static class TelegramReactionMapper
{
    /// <summary>
    /// Returns the reactions on a tapi message, or <c>null</c> when there are none (matching
    /// "reactions is null when unreacted", so the no-reaction case is byte-identical to today).
    /// <paramref name="ownUserId"/> is the owner's Telegram profile-user id when learned
    /// (null/empty ⇒ no element can be identified as the owner's).
    /// </summary>
    public static List<MessageReaction> Map(JToken reactions, string ownUserId = null)
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
            bool isOwn = !string.IsNullOrEmpty(ownUserId) && userId == ownUserId;

            result.Add(new MessageReaction
            {
                emoji = emoji,
                // The owner's element adopts the "me" identity (mirrors the optimistic send
                // entry, so OutgoingReaction.CurrentMyEmoji keeps finding it). Otherwise
                // user_id is the stable per-reactor identity; fall back to an emoji-scoped key
                // so two same-emoji reactors with no id still don't collapse into one.
                reactorKey = isOwn ? OutgoingReaction.MeReactorKey
                           : !string.IsNullOrEmpty(userId) ? userId : $"{emoji}@tg",
                senderName = isOwn ? "Me" : contact ?? "",
                fromMe = isOwn,
                time = 0          // tapi carries no per-reaction timestamp on the sync API
            });
        }

        return result.Count > 0 ? result : null;
    }
}
