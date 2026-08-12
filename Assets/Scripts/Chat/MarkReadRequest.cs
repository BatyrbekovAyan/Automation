/// <summary>
/// Pure builder for Wappi's read-receipt call: the path + query of
/// <c>message/mark/read</c> and the channel rule for its <c>mark_all</c> lever.
/// No UnityWebRequest, no I/O — ChatManager wraps this in the transport and owns
/// nothing about the wire shape, so the shape stays unit-testable.
///
/// Only the QUERY differs per channel; the body is <c>{ message_id }</c> on both.
///
/// <para><b>mark_all</b> (WhatsApp, <c>/api/sync/message/mark/read</c>): "Если true, то
/// помечает все непрочитанные сообщения в чате прочитанными." The WhatsApp docs attach NO
/// group restriction to THIS parameter. The often-misquoted note "Работает только для
/// личных чатов, для групп не работает" appears exactly once on that page and belongs to
/// <c>messages/get</c>'s own <c>mark_all</c> — a different lever on a different endpoint.
/// Do not "fix" a group read-receipt problem by reaching for that one; it is the parameter
/// documented NOT to work for groups.</para>
///
/// <para>Telegram (tapi) documents no <c>mark_all</c> on mark/read at all — its bulk lever
/// is <c>mark_all</c> on <c>messages/get</c> — so the Telegram query never carries it.</para>
///
/// <para>VERIFIED on a live group, 2026-08-12 (<c>Tools/wappi/probe-group-mark-read.py</c>):
/// <c>mark_all=true</c> DOES clear a group. One POST against a test group took
/// <c>unread_count</c> 3 → 0 and flipped all three incoming messages to <c>isRead=true</c> —
/// and it did so with an OUTGOING REACTION's id in the body. So the body id acts as a
/// watermark/trigger, not as the thing being marked: it need not be incoming, need not be a
/// real message, and one call per chat-open is enough. Do NOT add a group-aware
/// per-message loop here; it would be several redundant POSTs solving nothing.</para>
/// </summary>
public static class MarkReadRequest
{
    /// <summary>
    /// The path + query passed to <see cref="WappiEndpoints.Sync"/> for a read receipt.
    /// </summary>
    public static string Path(ChatChannel channel, string profileId) =>
        UsesMarkAll(channel)
            ? $"message/mark/read?profile_id={profileId}&mark_all=true"
            : $"message/mark/read?profile_id={profileId}";

    /// <summary>
    /// True when this channel's mark/read accepts the <c>mark_all</c> query lever.
    /// WhatsApp: yes. Telegram: no — tapi documents none on this endpoint.
    /// </summary>
    public static bool UsesMarkAll(ChatChannel channel) => channel != ChatChannel.Telegram;
}
