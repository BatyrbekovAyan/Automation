using System.Collections.Generic;

/// <summary>
/// F2 (plan 2026-10-11 §1.2 #15, task T8): what a bot's channel IS once the server retired its profile — pure, no Unity
/// types, EditMode-tested. The store half (PlayerPrefs + the Bot's fields) is <see cref="ProfileRetirement"/>.
///
/// M4 only remembered the retired profile next to the channel's ids. F2 MOVES them aside: the channel's workflow id goes to
/// <c>{bot}{Ch}RetiredWorkflowId</c>, the current profile / workflow ids become the sentinel and the channel is switched
/// off. Every one of the ~110 sites that read a channel's ids then sees an ordinary «not connected» channel by itself —
/// chats show their empty state, the reply mode is not sent, the client gate no longer counts the channel, the claim loop
/// no longer asks it, and switching it on takes the fresh path (profile.add → pairing → Create naming the retired profile).
/// The deletion (V12) reads the retired ids too — shipping the move without that would let a bot with only retired ids
/// leave the phone with its server data in place.
///
/// An unfinished app delete (<c>app_delete</c>) is NOT moved: its screens only offer «Удалить ещё раз», and the server would
/// never take such a row as a predecessor. Its ids stay and the M4 marker keeps applying to them.
/// </summary>
public static class RetiredChannelPolicy
{
    /// <summary>One channel of a bot as the device stores it: the current ids, its switch, and the retired set.</summary>
    public struct Channel
    {
        public string ProfileId;
        public string WorkflowId;
        public bool IsOn;
        /// <summary>The profile the server retired on this channel ("" = none). Also M4's marker.</summary>
        public string RetiredProfileId;
        /// <summary>The clone the channel had when its profile was retired ("" / sentinel = none).</summary>
        public string RetiredWorkflowId;
        /// <summary>The server's deleted_reason ("" = not told — the row is not this install's).</summary>
        public string RetiredReason;
    }

    private static bool IsId(string id) => ProfileClaimPolicy.IsAuthedId(id);

    /// <summary>Every reason moves the channel's ids aside except an unfinished app delete.</summary>
    public static bool MovesIdsAside(string reason) => reason != ProfileRetirement.ReasonAppDelete;

    /// <summary>
    /// The channel after the server answered profile_retired for <paramref name="profileId"/>. Only the channel's OWN current
    /// profile is retired: an answer about any other id (the channel moved on meanwhile — a new pairing, a repeated answer
    /// after the move) changes nothing, so the move is idempotent and a live channel is never touched. The retired set
    /// always describes the latest retirement (an older set of another profile is replaced — documented residual).
    /// </summary>
    public static Channel Retire(Channel channel, string profileId, string reason)
    {
        if (!IsId(profileId) || channel.ProfileId != profileId) return channel;
        channel.RetiredProfileId = profileId;
        channel.RetiredReason = reason ?? "";
        if (!MovesIdsAside(reason)) return channel;                 // app_delete: the M4 marker only, the ids stay
        channel.RetiredWorkflowId = IsId(channel.WorkflowId) ? channel.WorkflowId : Bot.UnauthedProfileSentinel;
        channel.ProfileId = Bot.UnauthedProfileSentinel;
        channel.WorkflowId = Bot.UnauthedProfileSentinel;
        channel.IsOn = false;
        return channel;
    }

    /// <summary>
    /// The reason to show for this channel ("" = retired, reason unknown), or null when the channel is not retired:
    /// either its ids were moved aside (no current profile, a retired one), or an M4 marker names the current profile
    /// itself and the token does not list it (an app delete that did not finish, or a marker this build has not moved yet).
    /// </summary>
    public static string ActiveReason(Channel channel, InstallSession.TokenClaims claims)
    {
        if (!IsId(channel.RetiredProfileId)) return null;
        if (!IsId(channel.ProfileId)) return channel.RetiredReason ?? "";
        return ProfileClaimPolicy.RetiredMarkerApplies(channel.RetiredProfileId, channel.ProfileId, claims) ? channel.RetiredReason ?? "" : null;
    }

    public static bool IsRetired(Channel channel, InstallSession.TokenClaims claims) => ActiveReason(channel, claims) != null;

    /// <summary>
    /// A marker the M4 build wrote for the channel's own current id (it kept the ids): this build moves it aside once —
    /// the marker is the server's 410 for that very profile. Never an app delete, never while the token lists the profile.
    /// </summary>
    public static bool NeedsMoveAside(Channel channel, InstallSession.TokenClaims claims) =>
        claims != null && IsId(channel.ProfileId) && MovesIdsAside(channel.RetiredReason)
        && ProfileClaimPolicy.RetiredMarkerApplies(channel.RetiredProfileId, channel.ProfileId, claims);

    /// <summary>Create's <c>PreviousProfileId</c> for <paramref name="newProfileId"/>: the channel's retired profile, never the new one itself.</summary>
    public static string PreviousProfileId(Channel channel, string newProfileId) =>
        IsId(channel.RetiredProfileId) && channel.RetiredProfileId != newProfileId ? channel.RetiredProfileId : "";

    /// <summary>
    /// After a successful Create: the retired set goes ONLY when the server named its profile among the adopted ones (its
    /// memory, history and price lists now live under the new connection). Otherwise it stays — the bot's deletion then
    /// covers both the current and the retired ids.
    /// </summary>
    public static Channel AfterCreate(Channel channel, IEnumerable<string> adoptedProfiles)
    {
        if (!IsId(channel.RetiredProfileId) || adoptedProfiles == null) return channel;
        foreach (string adopted in adoptedProfiles)
        {
            if (adopted != channel.RetiredProfileId) continue;
            channel.RetiredProfileId = "";
            channel.RetiredReason = "";
            channel.RetiredWorkflowId = "";
            break;
        }
        return channel;
    }
}
