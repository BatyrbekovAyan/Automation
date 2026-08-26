using NUnit.Framework;

public class QuotaMathTests
{
    [Test] public void Under_80_is_ok() => Assert.AreEqual(QuotaState.Ok, QuotaMath.State(239, 300, 0));
    [Test] public void At_80_is_warn() => Assert.AreEqual(QuotaState.Warn, QuotaMath.State(240, 300, 0));
    [Test] public void Over_quota_without_topup_is_over() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(300, 300, 0));

    // Резерв (решение владельца 2026-08-26): топ-ап НЕ расширяет квоту — он тратится по одному
    // диалогу СВЕРХ исчерпанной квоты. Поэтому «квота кончилась, но резерв ещё платит» — это
    // собственное состояние, а не Over: стены нет, бот всё ещё отвечает сам.
    [Test] public void Quota_spent_with_reserve_left_is_the_reserve_state()
    {
        Assert.AreEqual(QuotaState.Reserve, QuotaMath.State(300, 300, 500));
        Assert.AreEqual(QuotaState.Reserve, QuotaMath.State(560, 300, 240), "260 диалогов ушло из резерва");
        Assert.AreEqual(QuotaState.Reserve, QuotaMath.State(799, 300, 1), "последний диалог резерва — ещё не стена");
    }

    [Test] public void The_wall_is_only_reached_when_the_reserve_is_empty()
    {
        // Старая формула (used >= quota + reserve) объявляла Over на ПОЛОВИНЕ резерва: used растёт,
        // а reserve падает, так что разрыв закрывался вдвое быстрее.
        Assert.AreEqual(QuotaState.Reserve, QuotaMath.State(550, 300, 250));
        Assert.AreEqual(QuotaState.Over, QuotaMath.State(800, 300, 0));
    }

    [Test] public void Remaining_is_quota_left_plus_the_whole_reserve()
    {
        Assert.AreEqual(500, QuotaMath.Remaining(300, 300, 500));
        Assert.AreEqual(561, QuotaMath.Remaining(239, 300, 500));
        Assert.AreEqual(250, QuotaMath.Remaining(550, 300, 250), "квота уже перерасходована — остался только резерв");
        Assert.AreEqual(0, QuotaMath.Remaining(800, 300, 0));
        Assert.AreEqual(61, QuotaMath.Remaining(239, 300, 0));
    }

    [Test] public void Zero_quota_is_over_at_zero() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(0, 0, 0));
    [Test] public void Percent_clamps_100() => Assert.AreEqual(100, QuotaMath.Percent(999, 300));
}
