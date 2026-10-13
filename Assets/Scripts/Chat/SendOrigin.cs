/// <summary>
/// Learning A (2026-09-25): where an outgoing text came from, relative to the «Вместе» panel.
/// Card = tapped and sent as is; Edited = tapped, then changed; Typed = a set was on screen, nothing
/// tapped; None = no set this turn. Only Card/Edited count as a preference (SuggestionPickStats).
/// </summary>
public enum SendOriginKind { None, Card, Edited, Typed }

/// <summary>
/// What the panel showed this turn (the FRESH set — its card #1 is the shadow «Авто» answer) and the
/// card the owner tapped, if any (from ANY round: fresh, drill or ‹). Plain data, filled by
/// SendOriginTracker, consumed once per send by SendOriginResolver.
/// </summary>
public sealed class PickSnapshot
{
    public string shownKey;        // request key of the fresh set (N8nSuggestionsProvider.RequestKey)
    public int shownSize;          // cards in the fresh set (1–4)
    public string top1Text;        // items[0].text of the fresh set — the shadow answer

    public string pickKey;         // request key of the set the tap came from (fresh or a drill/back round)
    public int pickSetSize;        // cards in THAT set
    public int pickIndex = -1;     // index of the tapped card in THAT set; -1 = nothing tapped
    public string pickText;        // its text (what WriteComposerRoutine put in the composer)
    public string pickMove;        // its move, already resolved by SuggestionsController.ResolvePickStatsMove (may be null)

    public bool HasSet => !string.IsNullOrEmpty(shownKey) && shownSize > 0;
    public bool HasPick => pickIndex >= 0 && !string.IsNullOrEmpty(pickText);
}

/// <summary>The classified origin of one send. Serialised by SendOriginWire into the proxy call.</summary>
public sealed class SendOrigin
{
    public SendOriginKind kind = SendOriginKind.None;
    public int index = -1;         // tapped card index (Card/Edited only)
    public string requestKey;      // the set the send is attributed to (pick's set, else the shown set)
    public int editPercent;        // round((1 − similarity(sent, tapped)) × 100); 0 for Card
    public bool top1Match;         // similarity(sent, fresh card #1) ≥ SendOriginResolver.Top1Threshold
    public int setSize;            // cards in the attributed set
    public string pickedMove;      // move of the tapped card (Card/Edited), for the preference counter
    public string botId;          // bot the send was made on — the preference counter must not credit a bot the owner switched to mid-flight

    public string KindWire => kind == SendOriginKind.Card ? "card"
                            : kind == SendOriginKind.Edited ? "edited"
                            : kind == SendOriginKind.Typed ? "typed" : "none";
}
