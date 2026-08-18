/// <summary>
/// The closed move taxonomy of the Suggest Replies contract — since the 2026-08-18 drill
/// redesign an INTERNAL classification (the response's <c>move</c> field), no longer the
/// display label. Shared by pickStats preference learning (PlayerPrefs key suffixes) and
/// the pick-resolution fallback; values mirror the server Validate enum verbatim — do NOT
/// localize, reorder, or add entries without changing the workflow first.
/// </summary>
public static class SuggestionMoves
{
    public static readonly string[] All =
        { "Ответ", "Уточнить", "Вариант", "К заказу", "Отложить", "Отказ" };

    /// <summary>Exact, case-sensitive membership — the server enum is exact.</summary>
    public static bool IsMove(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < All.Length; i++)
            if (All[i] == value) return true;
        return false;
    }
}
