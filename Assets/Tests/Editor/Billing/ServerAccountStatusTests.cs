using NUnit.Framework;

public class ServerAccountStatusTests
{
    [SetUp]
    public void Seams() => UsageStore.ResetSeamsForTests();

    [TearDown]
    public void Reset() => UsageStore.ResetSeamsForTests();

    private static UsageSnapshot Snapshot(string status, bool success = true) => new UsageSnapshot
    {
        success = success,
        plan = "business",
        status = status,
        quota = 1000,
        used = 12,
    };

    [Test] public void Expired_status_is_the_one_true_case()
        => Assert.IsTrue(ServerAccountStatus.SaysExpired(Snapshot("expired")));

    // Статус приходит из колонки subscribers.status; сравнение регистронезависимое по той же
    // причине, что и в QuotaFallbackPolicy — форма значения не наша, а зеркала.
    [TestCase("EXPIRED")]
    [TestCase("Expired")]
    public void Case_is_ignored(string status)
        => Assert.IsTrue(ServerAccountStatus.SaysExpired(Snapshot(status)));

    // Всё остальное — НЕ «истёк». grace намеренно НЕ считается: там доступ ещё есть
    // (спека §5.4, 3 дня), и решение об онбординге принимает обычный путь.
    [TestCase("trialing")]
    [TestCase("active")]
    [TestCase("grace")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("expired_soon")]
    [TestCase("unknown")]
    public void Everything_else_is_not_expired(string status)
        => Assert.IsFalse(ServerAccountStatus.SaysExpired(Snapshot(status)));

    /// <summary>
    /// Главный fail-open: ни одного успешного чтения ещё не было. Свежая офлайн-установка
    /// обязана вести себя ровно как раньше, а не запираться на числе, которого у нас нет.
    /// </summary>
    [Test] public void No_snapshot_is_unknown_not_expired()
        => Assert.IsFalse(ServerAccountStatus.SaysExpired(null));

    /// <summary>Тело без success не описывает аккаунт — даже если в нём написано «expired».</summary>
    [Test] public void An_unsuccessful_body_is_never_believed()
        => Assert.IsFalse(ServerAccountStatus.SaysExpired(Snapshot("expired", success: false)));

    [Test] public void Live_read_starts_unknown()
        => Assert.IsFalse(ServerAccountStatus.Expired, "холодный старт: UsageStore.Current == null");

    [Test] public void Live_read_follows_the_store()
    {
        UsageStore.Apply(Snapshot("expired"));
        Assert.IsTrue(ServerAccountStatus.Expired);

        UsageStore.Apply(Snapshot("active"));
        Assert.IsFalse(ServerAccountStatus.Expired, "покупка/восстановление обязаны снимать запрет");
    }

    /// <summary>
    /// Пин на РЕАЛЬНОЕ тело GetUsage (Shape Response), а не на руками собранный объект:
    /// имя поля status или значение 'expired' изменится — тест упадёт здесь, а не на устройстве.
    /// </summary>
    [Test] public void Parses_the_real_wire_body()
    {
        var parsed = UsageStore.Parse(
            "{\"success\":true,\"plan\":\"business\",\"status\":\"expired\",\"quota\":1000,\"used\":3," +
            "\"topupBalance\":0,\"botsRegistered\":1,\"channelsConnected\":1,\"periodEnd\":null," +
            "\"productId\":\"sub.business.month\",\"interval\":\"month\"}");

        Assert.IsTrue(ServerAccountStatus.SaysExpired(parsed));
    }

    /// <summary>
    /// Неизвестный сервером аккаунт (свежая установка) приходит как trial/trialing — это тот
    /// самый ответ, который НЕ имеет права запереть онбординг. Форма — из «Read Usage»
    /// (coalesce(sub.plan,'trial') / coalesce(sub.status,'trialing')).
    /// </summary>
    [Test] public void An_account_the_server_never_saw_is_not_expired()
    {
        var parsed = UsageStore.Parse(
            "{\"success\":true,\"plan\":\"trial\",\"status\":\"trialing\",\"quota\":150,\"used\":0," +
            "\"topupBalance\":0,\"botsRegistered\":0,\"channelsConnected\":0,\"periodEnd\":null}");

        Assert.IsFalse(ServerAccountStatus.SaysExpired(parsed));
    }

    /// <summary>
    /// Одна и та же строка-значение для двух seam'ов: если кто-то заведёт свой литерал,
    /// они разойдутся молча.
    /// </summary>
    [Test] public void Shares_the_wire_constant_with_the_quota_fallback_seam()
        => Assert.AreEqual("expired", QuotaFallbackPolicy.StatusExpired);
}
