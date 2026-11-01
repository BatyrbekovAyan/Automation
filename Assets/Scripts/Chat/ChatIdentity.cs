using System;
using System.Collections.Generic;

/// <summary>
/// «Личные и рабочие чаты»: which <c>messages/get</c> rows are THIS chat's — the app's twin of the server's
/// <c>chatIdentity(channel, chatId, code)</c> (Tools/n8n/audience.py CHAT_IDENTITY_JS). The live check
/// (docs/audits/2026-10-25-personal-chats/live-checks.md, «@lid и номер — один id») found WhatsApp chats the chat list names
/// by their number while every messages/get row of theirs carries the chat's @lid code in chatId, from and to; the list
/// keeps that code in <c>chat.LID</c>. So a row names the chat by the chat's id or, on WhatsApp only, by that code. A code
/// counts only when it is a string that ends in <see cref="CodeSuffix"/> with something before it and is not the chat id
/// itself — exact, case-sensitive, untrimmed, like the server's. Any other id — another number, another code — names another
/// chat (Wappi crosses concurrent answers). A row without a chatId is each caller's own rule; whose message it is, fromMe says.
///
/// Same rule, different source: the server takes the code from the webhook's whatsapp_chat_id, the app from the chat-list
/// row's chat.LID (<see cref="ChatViewModel.ChatCode"/>). Both twins run every row of
/// Tools/n8n/fixtures/chat-identity-cases.json (ChatIdentityTests here, tests/test_audience_rule.py on the server) — a new
/// row goes into that file only. The code is never a key: the chat is kept under its id everywhere (the history cache, the
/// reply mode, the personal / business decisions); <see cref="AdoptRows"/> gives an accepted page's rows that id.
/// </summary>
public readonly struct ChatIdentity
{
    /// <summary>The code's suffix — audience.py CHAT_CODE_SUFFIX (pinned against its text by ChatIdentityTests).</summary>
    public const string CodeSuffix = "@lid";

    // The one channel that has codes, named as the server names it (WappiProxyRequest.ChannelName).
    private const string WhatsappChannel = "whatsapp";

    private readonly string _chatId;
    private readonly string _code;

    private ChatIdentity(string chatId, string code)
    {
        _chatId = chatId;
        _code = code;
    }

    /// <summary>The chat's own id — the key the chat list, the history cache and the decisions keep it under.</summary>
    public string ChatId => _chatId;

    /// <summary>The code the chat's rows may carry instead of its id; "" when none counts (and for a default value).</summary>
    public string Code => _code ?? "";

    /// <summary>
    /// The server's call, the channel named as on the wire («whatsapp» / «telegram»): only «whatsapp», spelled exactly, has
    /// a code. Every row of the shared truth table runs through this overload.
    /// </summary>
    public static ChatIdentity For(string channel, string chatId, string code)
    {
        bool counts = channel == WhatsappChannel
                      && code != null
                      && code.Length > CodeSuffix.Length
                      && code.EndsWith(CodeSuffix, StringComparison.Ordinal)
                      && code != chatId;
        return new ChatIdentity(chatId, counts ? code : "");
    }

    /// <summary>The app's call: the active channel, the chat's id and the code its chat-list row names.</summary>
    public static ChatIdentity For(ChatChannel channel, string chatId, string code) =>
        For(WappiProxyRequest.ChannelName(channel), chatId, code);

    /// <summary>A chat known by its id alone — no code counts.</summary>
    public static ChatIdentity ByIdAlone(string chatId) => new ChatIdentity(chatId, "");

    /// <summary>True when a row under <paramref name="id"/> is this chat's: the chat's id, or the code that counts.</summary>
    public bool Names(string id) => id == ChatId || (Code.Length > 0 && id == Code);

    /// <summary>
    /// Rewrites every row of an accepted page that names this chat by its code to name it by the chat's own id, so
    /// everything downstream — the bubbles (MessageViewModel.chatId), the history cache, which drops a row naming another
    /// id, the media pins and reactions that load the history by the bubble's chat id — keeps the chat under its id. Rows
    /// under the id, under any other id or without one are left as they came; so is <c>from</c>. Returns how many rows
    /// were rewritten.
    /// </summary>
    public int AdoptRows(IReadOnlyList<RawMessage> rows)
    {
        if (rows == null || Code.Length == 0) return 0;

        int adopted = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            RawMessage raw = rows[i];
            if (raw == null || raw.chatId != Code) continue;
            raw.chatId = ChatId;
            adopted++;
        }
        return adopted;
    }
}
