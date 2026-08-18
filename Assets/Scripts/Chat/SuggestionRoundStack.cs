using System.Collections.Generic;

/// <summary>
/// History of suggestion rounds behind the panel's back button (flow decision 2026-08-11).
/// Each card pick moves a round FORWARD (the next set steers toward the pick); Push records
/// the round being left so back can restore its cards INSTANTLY — no LLM call, no skeleton.
/// Pure C#, controller-owned lifecycle: push on pick, pop on back, Clear on any fresh round
/// (new incoming, chat/bot switch, toggle, answered-run reset). A refresh re-rolls the
/// CURRENT round in place and is deliberately never pushed.
/// </summary>
public sealed class SuggestionRoundStack
{
    /// <summary>Retained rounds. Real sessions go 2–4 deep; the cap only bounds memory —
    /// overflow drops the OLDEST round, so back still walks the recent path.</summary>
    public const int MaxDepth = 8;

    private readonly List<(SuggestionResult result, string steer, string header)> _rounds = new();

    public int Count => _rounds.Count;
    public bool CanGoBack => _rounds.Count > 0;

    /// <summary>Record the round being left: its cards, the steer that PRODUCED it, and the
    /// display header it was shown under (null = the default «ПРЕДЛОЖЕНИЯ» overline). A null
    /// <paramref name="result"/> is a no-op — a pick that lands while nothing is rendered
    /// has no round to return to.</summary>
    public void Push(SuggestionResult result, string steer, string header)
    {
        if (result == null) return;
        if (_rounds.Count == MaxDepth) _rounds.RemoveAt(0);
        _rounds.Add((result, steer, header));
    }

    /// <summary>LIFO restore of the most recent round, the steer that produced it (null =
    /// fresh set — a refresh after back re-rolls the right direction) and its header.</summary>
    public bool TryPop(out SuggestionResult result, out string steer, out string header)
    {
        result = null;
        steer = null;
        header = null;
        if (_rounds.Count == 0) return false;
        (result, steer, header) = _rounds[_rounds.Count - 1];
        _rounds.RemoveAt(_rounds.Count - 1);
        return true;
    }

    public void Clear() => _rounds.Clear();
}
