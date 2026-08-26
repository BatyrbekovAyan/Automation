using System;

/// <summary>
/// «Слово сервера» об аккаунте: единственное место, где клиент решает, что зеркало подписок
/// ОПРЕДЁЛЕННО говорит «подписка истекла» (Task 19).
///
/// Родилось из живого инцидента 2026-08-26 15:12: анонимный id RevenueCat пережил переустановку,
/// поэтому СЕРВЕР знал про `business/expired`, а локальный <see cref="TrialLedger"/> был стёрт
/// вместе с приложением — клиент предложил триал, мастер довёл владельца до реальной авторизации
/// WhatsApp, и только вебхук Create отказал (`channel_limit`: у expired 0 слотов). Итог —
/// оплаченный Wappi-профиль, повисший на сервере, и полусозданный бот в приложении.
///
/// Правило одностороннее и намеренно узкое: сервер может ЗАБРАТЬ незапущенный триал, но не может
/// ничего выдать. Всё, что не является явным «expired» из УСПЕШНОГО снимка, читается как
/// «не знаю» и не меняет поведение (fail-open) — свежая офлайн-установка обязана открывать
/// мастер первого бота, как и раньше.
///
/// Чистый seam (<see cref="UsageSnapshot"/> — обычный data-класс, никакого Unity), как и
/// <see cref="QuotaFallbackPolicy"/> рядом: каждое плечо проверяется EditMode-тестом.
/// </summary>
public static class ServerAccountStatus
{
    /// <summary>
    /// Говорит ли снимок GetUsage, что аккаунт истёк.
    ///
    /// <c>null</c> (ни одного успешного чтения ещё не было) — это НЕИЗВЕСТНО, никогда не
    /// «истёк»: холодный старт не имеет права запирать онбординг на основании числа, которого
    /// у нас нет. <see cref="UsageSnapshot.success"/> проверяется по той же причине, что и в
    /// <see cref="QuotaFallbackPolicy.ShouldFallBackToSemi(UsageSnapshot)"/> — тело без success
    /// не описывает аккаунт (хотя <see cref="UsageClient"/> такое и не применяет).
    ///
    /// Неизвестный аккаунт сервер отдаёт как <c>trial/trialing</c> (Get Usage «Read Usage»:
    /// coalesce к 'trial'/'trialing' при отсутствии строки subscribers), поэтому «expired»
    /// приходит ТОЛЬКО из реальной строки зеркала — свежая установка сюда не попадает.
    /// </summary>
    public static bool SaysExpired(UsageSnapshot usage)
        => usage != null
        && usage.success
        && string.Equals(usage.status, QuotaFallbackPolicy.StatusExpired, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Живое чтение того же факта из <see cref="UsageStore.Current"/> — ОДИН источник и для
    /// гейта (<see cref="EntitlementGate.CurrentTier"/>), и для пейволла
    /// (<see cref="PaywallRows.IsTrialOffer"/>), чтобы они не могли разойтись во мнении.
    /// </summary>
    public static bool Expired => SaysExpired(UsageStore.Current);
}
