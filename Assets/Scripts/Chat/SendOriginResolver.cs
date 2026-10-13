using System;
using System.Text;

/// <summary>
/// Pure classifier for learning A. similarity = 1 − Levenshtein(a, b) / max(|a|, |b|) on normalised
/// strings (trim, lower, whitespace collapsed, edge punctuation stripped). EditMode-tested; no Unity.
/// </summary>
public static class SendOriginResolver
{
    public const double Top1Threshold = 0.8;

    public static SendOrigin Resolve(string sentText, PickSnapshot snap)
    {
        var o = new SendOrigin();
        if (snap == null || !snap.HasSet) return o;                       // None: nothing was on screen this turn
        o.top1Match = Similarity(sentText, snap.top1Text) >= Top1Threshold;
        if (!snap.HasPick)
        {
            o.kind = SendOriginKind.Typed;
            o.requestKey = snap.shownKey;
            o.setSize = snap.shownSize;
            return o;
        }
        o.requestKey = string.IsNullOrEmpty(snap.pickKey) ? snap.shownKey : snap.pickKey;
        o.setSize = snap.pickSetSize > 0 ? snap.pickSetSize : snap.shownSize;
        o.index = snap.pickIndex;
        o.pickedMove = snap.pickMove;
        // spec §3.2: card = the NORMALIZED texts are EQUAL; edited = anything else — never the rounded percent, which hides
        // a one-character change in a long card (210 chars → 0.48 % → 0). An edit reports at least 1 %.
        bool sameText = Normalize(sentText) == Normalize(snap.pickText);
        o.kind = sameText ? SendOriginKind.Card : SendOriginKind.Edited;
        o.editPercent = sameText ? 0 : Math.Max(1, (int)Math.Round((1.0 - Similarity(sentText, snap.pickText)) * 100.0));
        return o;
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (char c in s.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        int start = 0, end = sb.Length;
        while (start < end && IsEdgePunct(sb[start])) start++;
        while (end > start && IsEdgePunct(sb[end - 1])) end--;
        return sb.ToString(start, end - start).Trim();
    }

    private static bool IsEdgePunct(char c) => c == '.' || c == ',' || c == '!' || c == '?' || c == '…' || c == ';' || c == ':' || c == '"' || c == '«' || c == '»' || c == '\'';

    public static double Similarity(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        int max = Math.Max(na.Length, nb.Length);
        if (max == 0) return 1.0;
        return 1.0 - (double)Levenshtein(na, nb) / max;
    }

    public static int Levenshtein(string a, string b)
    {
        a = a ?? ""; b = b ?? "";
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            var t = prev; prev = cur; cur = t;
        }
        return prev[b.Length];
    }
}
