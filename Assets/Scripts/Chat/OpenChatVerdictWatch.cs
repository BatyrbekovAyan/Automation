using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// «Личные и рабочие чаты»: when the OPEN chat asks the server for its verdict. An incoming message in an open person chat
/// the app holds no verdict for (no server decision mirrored, no owner's move on its way) asks for a list read at once
/// (ChatManager.AskVerdictForOpenChat). The server writes its decision some seconds after the message — the bot's mode read,
/// two Wappi reads and the write, each Postgres step ≈ 1.9 s on prod — so that first read may come before it. The watch asks
/// again at <see cref="AskAtSeconds"/> after the message: at most <see cref="MaxAsks"/> asks per incoming message, each read
/// coming ChatAudienceSyncService.ListSoonSeconds after its ask (≈ 3, 10 and 18 s after the message); after a pause (the app
/// was in the background) the asks keep the same gaps from the one before, never a burst. It stops at once — no
/// further ask — when the chat is no longer an undecided person (a decision or the owner's move is mirrored), the chat is
/// closed or another chat is open, or another profile is active (a bot or channel switch); every ask made, it stops too. A
/// new incoming message starts the schedule over. Pure and UnityEngine-free: the caller hands in its clock and what the open
/// chat is; ChatManager looks once a second from the open chat's live poll.
/// </summary>
public sealed class OpenChatVerdictWatch
{
    /// <summary>Seconds after the incoming message at which the open chat asks for a read (the first one is the message's own).</summary>
    public static readonly IReadOnlyList<float> AskAtSeconds = new ReadOnlyCollection<float>(new[] { 0f, 7f, 15f });

    /// <summary>The most asks one incoming message makes.</summary>
    public static int MaxAsks => AskAtSeconds.Count;

    private string profileId;
    private string chatId;
    private float armedAt;
    private float lastAskAt;
    private int asked;

    /// <summary>An incoming message is waiting for the server's verdict and an ask may still be due.</summary>
    public bool IsArmed => chatId != null;

    /// <summary>An incoming message in the open undecided person chat: the schedule starts over, its first ask due now.</summary>
    public void Arm(string forProfileId, string forChatId, float now)
    {
        if (string.IsNullOrEmpty(forProfileId) || string.IsNullOrEmpty(forChatId))
        {
            Stop();
            return;
        }
        profileId = forProfileId;
        chatId = forChatId;
        armedAt = now;
        asked = 0;
    }

    /// <summary>Nothing more is asked until the next incoming message.</summary>
    public void Stop()
    {
        profileId = null;
        chatId = null;
        asked = 0;
    }

    /// <summary>
    /// One look, with the open chat as it is now: true when a read should be asked for now (the ask is then counted). The
    /// watch stops — false now and on every later look — once the chat it waits for is closed, another chat or another
    /// profile is open, the chat no longer awaits a verdict, or every ask was made.
    /// </summary>
    public bool ShouldAsk(string openProfileId, string openChatId, bool chatIsOpen, bool awaitsVerdict, float now)
    {
        if (chatId == null) return false;
        bool sameChat = chatIsOpen && openChatId == chatId && openProfileId == profileId;
        if (!sameChat || !awaitsVerdict || asked >= MaxAsks)
        {
            Stop();
            return false;
        }
        float due = asked == 0 ? armedAt : lastAskAt + (AskAtSeconds[asked] - AskAtSeconds[asked - 1]);
        if (now < due) return false;
        asked++;
        lastAskAt = now;
        if (asked >= MaxAsks)
        {
            Stop();
        }
        return true;
    }
}
