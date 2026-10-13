/// <summary>
/// Learning A: the ONE handoff between the panel and the send path. The panel calls SetShown when a
/// set renders (fresh = round 1 of a turn: its card #1 is the shadow «Авто» answer; a drill/‹ round
/// keeps the fresh snapshot) and SetPick when a card is tapped; a fresh round Clears; hiding the panel
/// (toggle OFF, server flip to «Авто») drops a shown set but keeps a tapped card; the send calls
/// ResolveAndClear ONCE, before it announces the optimistic bubble through OnLiveMessagesReceived —
/// SuggestionsController.HandleLive folds that echo as an answered run and clears the snapshot
/// SYNCHRONOUSLY. Static on purpose: SuggestionsController and ChatManager are separate MonoBehaviours
/// with no reference to each other, and there is exactly one open chat.
/// </summary>
public static class SendOriginTracker
{
    private static PickSnapshot _snapshot;

    public static PickSnapshot Current => _snapshot;

    public static void SetShown(string requestKey, int setSize, string top1Text, bool fresh)
    {
        if (string.IsNullOrEmpty(requestKey) || setSize <= 0) return;
        if (!fresh)
        {
            if (_snapshot != null) return;   // drill / back: the turn's fresh #1 stays the shadow answer, a pending tap stays
            _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = null };   // a non-fresh set never becomes the shadow #1
            return;
        }
        _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = top1Text };
    }

    public static void SetPick(string requestKey, int setSize, int index, string text, string move)
    {
        if (string.IsNullOrEmpty(text) || index < 0) return;
        if (_snapshot == null)   // ‹ after a send restored a round: attribute to that set, no shadow #1 known
            _snapshot = new PickSnapshot { shownKey = requestKey, shownSize = setSize, top1Text = null };
        _snapshot.pickKey = requestKey;
        _snapshot.pickSetSize = setSize;
        _snapshot.pickIndex = index;
        _snapshot.pickText = text;
        _snapshot.pickMove = move;
    }

    public static void Clear() => _snapshot = null;

    /// <summary>Classifies the send and consumes the snapshot — a second message in the same turn is «none».</summary>
    public static SendOrigin ResolveAndClear(string sentText)
    {
        SendOrigin o = SendOriginResolver.Resolve(sentText, _snapshot);
        _snapshot = null;
        return o;
    }
}
