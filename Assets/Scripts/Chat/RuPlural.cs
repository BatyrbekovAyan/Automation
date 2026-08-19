/// <summary>
/// Russian three-form plural agreement, in one place so the 11..14 exception is
/// never re-derived at a call site. Pure + static, pinned by RuPluralTests.
/// </summary>
public static class RuPlural
{
    /// <summary>
    /// Picks the form matching <paramref name="count"/>:
    /// <c>one</c> for 1, 21, 101…; <c>few</c> for 2..4, 22..24…;
    /// <c>many</c> for 0, 5..20, 25… — note 11..14 take <c>many</c> despite
    /// ending in 1..4, which is the rule everyone gets wrong.
    /// </summary>
    public static string Pick(int count, string one, string few, string many)
    {
        int n = count < 0 ? -count : count;
        int mod100 = n % 100;
        int mod10 = n % 10;
        bool teens = mod100 >= 11 && mod100 <= 14;

        if (!teens && mod10 == 1) return one;
        if (!teens && mod10 >= 2 && mod10 <= 4) return few;
        return many;
    }
}
