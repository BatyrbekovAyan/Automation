using System;
using System.Collections.Generic;

/// <summary>
/// The mutations an outgoing send makes to the in-memory message cache, as pure,
/// UnityEngine-free rules (same convention as MessageOrder / MediaGhostMatch / ScrollTopInsetMath).
///
/// <para><b>Why this exists.</b> <c>ChatManager._activeChatCache</c> is the list the open-chat
/// live poll hands to <c>SyncLatestMessages</c> as <c>cachedList</c>, and therefore the list
/// <c>ReconcileGhostSend</c> searches when the server echoes a message back to us. The optimistic
/// send used to mutate a DETACHED <c>ChatHistoryCache.LoadHistory</c> list instead, so when the
/// 3-second poll's echo beat the send's own ack (a coin flip — <c>message/send</c> routinely takes
/// 1–3 s and does not hold the <c>_chatFetchesInFlight</c> gate) the reconcile searched a list the
/// optimistic bubble was never in, reported "not recovered", and the merge appended the echo as a
/// SECOND bubble. Device symptom, 2026-09-04: a sent message rendered twice, ✓ beside ✓✓, and
/// deduplicated only on reopen (the disk cache only ever held one row).</para>
///
/// <para><b>Newest-first.</b> Every operation preserves the descending
/// <see cref="MessageOrder"/> order every assignment site of <c>_activeChatCache</c> establishes
/// and <c>RecentMessageWindow.TakeNewest</c> depends on — which is why <see cref="Insert"/> exists
/// at all instead of a bare <c>List.Add</c>: a just-sent message is the NEWEST, so appending it
/// puts it at the wrong end.</para>
///
/// <para><b>Which list.</b> <see cref="UsesLiveList"/> is the guard <c>ChatManager.LiveCacheFor</c>
/// delegates to; it is here, pure, so the four branches are pinned by tests rather than by a
/// comment. The five send-path sites are held to the seam by SendPathWiringTests.</para>
/// </summary>
public static class OutgoingSendCache
{
    /// <summary>
    /// The first local sequence an optimistic send carries (the ChatManager counter starts
    /// here). Server messages carry <see cref="MessageOrder.WithinSecondSequence"/>, which can
    /// never exceed the page size, so a floor above it orders a just-sent message newest within
    /// its second — the premise <see cref="Insert"/>'s tie-break rests on.
    /// </summary>
    public const int LocalSendSequenceFloor = 1000;

    /// <summary>Temp-id prefixes of the two optimistic send kinds (text / staged media).</summary>
    public const string TextTempIdPrefix = "sending_";
    public const string MediaTempIdPrefix = "staging_";

    /// <summary>The ± window <c>BestGhostMatch</c> accepts between an outbox entry and its echo.</summary>
    public const long EchoWindowSeconds = 120;

    public static bool IsTempId(string id) =>
        !string.IsNullOrEmpty(id)
        && (id.StartsWith(TextTempIdPrefix, StringComparison.Ordinal)
            || id.StartsWith(MediaTempIdPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Whether a send-path mutation must land in the live in-memory list rather than a fresh
    /// disk load. Both halves are load-bearing: the chat must be the OPEN one, and the send's
    /// snapshotted <paramref name="cacheRoot"/> must still be the ACTIVE bot's — a send that
    /// completes after a bot switch carries the originating bot's root while the live list has
    /// moved on to another bot's chat. No live list at all (first open of an uncached chat, or
    /// a channel switch / privacy clear that nulled it) falls back to disk, which is exactly the
    /// pre-2026-09-04 behaviour for everything off the open chat.
    /// </summary>
    public static bool UsesLiveList(bool hasLiveList, string chatId, string currentChatId,
                                    string cacheRoot, string activeRoot)
        => hasLiveList
           && !string.IsNullOrEmpty(chatId) && chatId == currentChatId
           && !string.IsNullOrEmpty(cacheRoot) && cacheRoot == activeRoot;

    /// <summary>
    /// Inserts an outgoing message into a NEWEST-FIRST list at its canonical
    /// <see cref="MessageOrder"/> position. Null list or message is a no-op.
    /// </summary>
    public static void Insert(List<MessageViewModel> newestFirst, MessageViewModel sent)
    {
        if (newestFirst == null || sent == null) return;

        // First entry that is strictly OLDER than the new one — the slot it belongs in.
        // Normally index 0 (the send is the newest thing in the chat), but a device clock
        // running behind the server's places it further down rather than out of order.
        for (int i = 0; i < newestFirst.Count; i++)
        {
            if (newestFirst[i] == null) continue;
            if (MessageOrder.Compare(sent, newestFirst[i]) > 0)
            {
                newestFirst.Insert(i, sent);
                return;
            }
        }

        newestFirst.Add(sent);
    }

    /// <summary>
    /// Swaps an optimistic message's temp id for the server's real id in place and adopts the
    /// given delivery status, leaving order untouched (the id is the last key in
    /// <see cref="MessageOrder"/>, so a swap can only re-order same-timestamp same-sequence ties,
    /// which the next sync's canonical-key adoption settles anyway).
    /// <para>Returns true iff a cached entry was found and mutated. A false is meaningful: it is
    /// the signal <c>ReconcileGhostSend</c> turns into "append this echo as a new bubble", so it
    /// must only ever mean the bubble genuinely is not cached (evicted past the 100-message cap),
    /// never that the send wrote to a different list than the one being searched. The ACK path
    /// reads the same false as "the poll's echo already reconciled this one" — see
    /// <see cref="StatusToAnnounce"/>.</para>
    /// </summary>
    public static bool AdoptServerId(List<MessageViewModel> newestFirst, string tempId,
                                     string serverId, DeliveryStatus status)
        => AdoptServerId(newestFirst, tempId, serverId, status, null, null);

    /// <summary>
    /// The echo-reconcile form: besides the id and status, adopts the server's canonical order
    /// keys (its timestamp and within-second sequence) so a reopen sorts the message exactly
    /// where WhatsApp does even when the device clock was skewed at send time.
    /// </summary>
    public static bool AdoptServerId(List<MessageViewModel> newestFirst, string tempId,
                                     string serverId, DeliveryStatus status,
                                     long? timestamp, int? sequence)
    {
        if (newestFirst == null || string.IsNullOrEmpty(tempId) || string.IsNullOrEmpty(serverId))
            return false;

        for (int i = 0; i < newestFirst.Count; i++)
        {
            if (newestFirst[i] == null || newestFirst[i].messageId != tempId) continue;
            newestFirst[i].messageId      = serverId;
            newestFirst[i].deliveryStatus = status;
            if (timestamp.HasValue) newestFirst[i].timestamp = timestamp.Value;
            if (sequence.HasValue)  newestFirst[i].sequence  = sequence.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The status an ack may announce for a bubble. When the ack's own
    /// <see cref="AdoptServerId"/> found the temp id, that is <paramref name="ackStatus"/>. When
    /// it did not, the poll's echo has already reconciled the bubble under
    /// <paramref name="serverId"/> and may have carried a FRESHER tick (Delivered / Read) — a
    /// hard-coded Sent would then step the bubble back to ✓ for a whole poll interval (the
    /// ✓✓→✓→✓✓ flicker found in review), so the cached status wins whenever it outranks the ack.
    /// An entry that is not cached at all (evicted) still gets the ack's status: the rendered
    /// bubble must leave Pending regardless.
    /// </summary>
    public static DeliveryStatus StatusToAnnounce(List<MessageViewModel> newestFirst, bool adopted,
                                                  string serverId, DeliveryStatus ackStatus)
    {
        if (adopted || newestFirst == null || string.IsNullOrEmpty(serverId)) return ackStatus;

        for (int i = 0; i < newestFirst.Count; i++)
        {
            var m = newestFirst[i];
            if (m == null || m.messageId != serverId) continue;
            return Rank(m.deliveryStatus) > Rank(ackStatus) ? m.deliveryStatus : ackStatus;
        }

        return ackStatus;
    }

    // The delivery ladder an ack must never descend: Sent < Delivered < Read. None / Pending /
    // Failed are not on it — an ack always outranks those (they are what it is there to replace).
    private static int Rank(DeliveryStatus s) =>
        s == DeliveryStatus.Sent ? 1 : s == DeliveryStatus.Delivered ? 2 : s == DeliveryStatus.Read ? 3 : 0;

    /// <summary>
    /// Carries the outgoing rows a send wrote to DISK while a chat's first page was still in
    /// flight into the server list that is about to become the live list. On the first open of
    /// a never-cached chat there is no live list yet, so <c>LiveCacheFor</c> writes the optimistic
    /// row to disk — and the page callback then overwrote that file and installed a list without
    /// the row, re-opening the exact echo-appends-a-duplicate window this seam exists to close.
    /// <para>Rules: incoming rows are never carried (the server owns those); a row already in the
    /// server list by id is skipped; a TEXT row still under its temp id whose echo is already in
    /// the page (same text, same direction, within <see cref="EchoWindowSeconds"/>) is dropped —
    /// keeping it would render the bubble twice. Media rows are kept as they are: a caption is
    /// too weak a key to risk dropping a genuine send. Returns the number of rows carried.</para>
    /// </summary>
    public static int MergeOptimisticRows(List<MessageViewModel> serverNewestFirst,
                                          IReadOnlyList<MessageViewModel> diskSnapshot)
    {
        if (serverNewestFirst == null || diskSnapshot == null) return 0;

        int carried = 0;
        for (int i = 0; i < diskSnapshot.Count; i++)
        {
            var row = diskSnapshot[i];
            if (row == null || row.isIncoming || string.IsNullOrEmpty(row.messageId)) continue;
            if (ContainsId(serverNewestFirst, row.messageId)) continue;
            if (IsTempId(row.messageId) && row.type == MessageType.Chat && HasTextEcho(serverNewestFirst, row)) continue;

            Insert(serverNewestFirst, row);
            carried++;
        }
        return carried;
    }

    private static bool ContainsId(List<MessageViewModel> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].messageId == id) return true;
        return false;
    }

    private static bool HasTextEcho(List<MessageViewModel> list, MessageViewModel row)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null || m.isIncoming || m.type != MessageType.Chat) continue;
            if (m.text != row.text) continue;
            if (Math.Abs(m.timestamp - row.timestamp) <= EchoWindowSeconds) return true;
        }
        return false;
    }
}
