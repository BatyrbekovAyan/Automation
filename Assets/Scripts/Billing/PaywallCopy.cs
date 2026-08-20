using System.Text;

public static class PaywallCopy
{
    const char Nbsp = ' ';

    public static string Kzt(int amount)
    {
        var digits = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(Nbsp);
            sb.Append(digits[i]);
        }
        sb.Append(Nbsp).Append('₸');
        return sb.ToString();
    }

    public static string PerMonth(int amount) => Kzt(amount) + "/мес";

    public static string YearLine(PlanSpec p) => Kzt(p.PriceYearKzt) + "/год — 12 месяцев по цене 10";

    public static string Dialogs(int n)
        => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + RuPlural.Pick(n, "диалог", "диалога", "диалогов");

    public static string TrialCta() => "Попробовать 5 дней бесплатно";

    public static string TrialPill(int daysLeft)
        => "Пробный · " + daysLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) + " дн.";
}
