using System;

/// <summary>
/// One person's reaction to a message. Stored inline on the target
/// MessageViewModel.reactions so it persists in ChatHistoryCache. [Serializable]
/// with public primitive fields so Unity's JsonUtility round-trips it.
/// </summary>
[Serializable]
public class MessageReaction
{
    public string emoji;       // Raw unicode emoji, e.g. "😘". Converted to a TMP sprite at render time.
    public string reactorKey;  // Stable per-reactor identity: "me" or the reactor jid.
    public string senderName;  // Display name of the reactor.
    public bool fromMe;        // Reaction came from the account owner.
    public long time;          // Reaction event time (unix seconds).
}
