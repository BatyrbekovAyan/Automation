using System;

public enum QuotaState { Ok, Warn, Over }

public static class QuotaMath
{
    public static int Percent(int used, int quota)
        => quota <= 0 ? 100 : Math.Min(100, (int)Math.Floor(used * 100.0 / quota));

    public static int Remaining(int used, int quota, int topupBalance)
        => Math.Max(0, quota + topupBalance - used);

    public static QuotaState State(int used, int quota, int topupBalance)
    {
        if (used >= quota + topupBalance) return QuotaState.Over;
        if (used >= quota || Percent(used, quota) >= PlanCatalog.WarnThresholdPercent) return QuotaState.Warn;
        return QuotaState.Ok;
    }
}
