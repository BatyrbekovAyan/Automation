// «Личные и рабочие чаты»: the gate every open-chat messages/get page passes — SyncLatestMessages (which the live poll
// re-issues), ValidateCachePageAgainstServer and GetMessagesRoutine (whose refusal is what retries a «crossed» page). The
// live check (docs/audits/2026-10-25-personal-chats/live-checks.md, «@lid и номер — один id») found WhatsApp chats the list
// names by their number while every row of their pages carries the chat's @lid code: judged by the chat's id alone, each
// such page read as another chat's crossed answer — no history, no live message, an empty suggestions panel. The gate
// judges a page by every id the chat's rows may carry (ChatIdentity — the twin of the server's chatIdentity) and gives an
// accepted page's rows under the code the chat's id, so the bubbles, the history cache and the panel keep the chat under
// its number. The request still names the chat by its id; the code is never a key.
public partial class ChatManager
{
    /// <summary>
    /// True when a messages/get page is the chat's own — false for another chat's crossed answer, which the caller drops
    /// (or, the page fetch, retries). An answer without messages is not «another chat's», as before. An accepted page's
    /// rows under the chat's code take the chat's id before anything reads them.
    /// </summary>
    private bool AcceptChatPage(MessagesResponseRaw response, string chatId)
    {
        ChatIdentity chat = IdentityOf(chatId);
        if (CrossChatResponseGuard.IsForDifferentChat(response?.messages, chat)) return false;

        chat.AdoptRows(response?.messages);
        return true;
    }

    /// <summary>
    /// The ids a messages/get row may name the chat by: its id and, on WhatsApp, the @lid code its chat-list row names
    /// (<see cref="ChatViewModel.ChatCode"/>). Resolved from the request's own chat id, so a reply that lands after the
    /// owner opened another chat is still judged by the chat it asked about. A chat the list does not hold (a deep link
    /// before the list loads) is known by its id alone — exactly as before.
    /// </summary>
    private ChatIdentity IdentityOf(string chatId) =>
        string.IsNullOrEmpty(chatId)
            ? ChatIdentity.ByIdAlone(chatId)   // GetChat cannot look up a missing id; the guard ignores such a request
            : ChatIdentity.For(ActiveChannel, chatId, GetChat(chatId)?.ChatCode);
}
