# Monetization Block 1 — Billing Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the paid core: tier entitlements (Старт/Бизнес/Сеть), 5-day no-card trial, RevenueCat IAP, server-side dialog metering + enforcement, and the Wappi profile lifecycle sweep — per the approved spec `docs/superpowers/specs/2026-08-21-monetization-design.md`.

**Architecture:** Pure C# seams (PlanCatalog / EntitlementPolicy / TrialLedger / QuotaMath / PaywallCopy) hold all decisions and are EditMode-tested; MonoBehaviours and builders stay thin. Server truth lives in Supabase (`subscribers`, `bot_profiles`, `dialog_counts`), written by n8n workflows (RevenueCat webhook, Create* registration, bot-workflow counter, daily sweep) and read by the app via a `GetUsage` webhook. RevenueCat (purchases-unity) is the only billing SDK; anonymous appUserID, no accounts backend.

**Tech Stack:** Unity 6 C# (EditMode NUnit), RevenueCat purchases-unity 9.5.x, n8n (dev localhost:5678 → canonical JSONs in `Tools/n8n/workflows/`), Supabase Postgres (Session pooler 5432), Wappi partner billing 23₽/day.

## Global Constraints

- **RU-only UI**: every user-facing string in Russian at its site; counted nouns via `RuPlural.Pick`; никаких `ToString("...")` через ambient culture — числа/даты только `CultureInfo.InvariantCulture` + ручная сборка (правило из CLAUDE.md «Conventions»).
- **Canvas units**: 1080×1920 reference; body TMP = 42, H1 = 50–55, caption = 28–32; touch target ≥ 120; icons = Image+sprite, никогда TMP-глифы; rounded corners через RoundedCorners-компонент (radius 1:1, не «компенсировать»).
- **Builders**: additive + idempotent, по образцу ближайшего существующего; НИКОГДА не запускать `Tools/Rebuild Bot Settings Prefabs`; после билдера — сохранить сцену и сразу закоммитить (parallel-clobber правило). Новые .cs — `Assets/Refresh` прежде чем Editor их увидит; коммитить .cs вместе с .meta.
- **Tests**: EditMode, каталог `Assets/Tests/Editor/Billing/` (компилируется в Assembly-CSharp-Editor без asmdef). Editor закрыт: `bash Tools/run-tests-headless.sh '<ClassNameRegex>'`; Editor открыт: bridge `Temp/claude/run-tests.trigger` → `Temp/claude/test-summary.json`. Гейтиться на `total` (фильтр с 0 совпадений = false green).
- **Networking**: UnityWebRequest + coroutines; bodyless POST к n8n — обязателен явный `Content-Type: application/json` (иначе 415); секреты только из `Assets/StreamingAssets/secrets.json` через `Secrets.Data`.
- **n8n**: канонические JSONы в `Tools/n8n/workflows/`; правки — на dev-инстансе, затем экспорт в репо; Postgres-нода: `queryReplacement` comma-splits списки (в наших параметрах запятых нет — но помнить); `n8n_chat_histories.message` — плоский `{type,content}`.
- **Fixed numbers (из спеки, не менять молча)**: тарифы 9 900/19 900/39 900 ₸/мес; год «12 за 10» = 99 000/199 000/399 000 ₸; лимиты 1/3/5 ботов, 1/3/5 каналов, 300/1000/3000 диалогов; триал 5 дней, уровень «Бизнес», кап 150 диалогов; топ-ап 500 диалогов 3 900 ₸ (не сгорает); предупреждение на 80%; таймзона учёта — `Asia/Almaty`; период — календарный месяц; «диалог» = клиент+сутки; свип-порог триала 4 дня 17 часов (запас до 6-го дня Wappi).
- **SKU ids (лочим здесь)**: `sub.start.month`, `sub.start.year`, `sub.business.month`, `sub.business.year`, `sub.network.month`, `sub.network.year`, `topup.dialogs.500`. RevenueCat entitlements: `tier_start`, `tier_business`, `tier_network`.

---

### Task 0 (OWNER, вне кода — разблокирует Task 10+): консоли сторов и RevenueCat

**Blocked by:** активация Apple Developer (оплата 2026-08-22, активация ≤48ч).

- [ ] App Store Connect: создать app record; Subscription Group «Тарифы»; 6 auto-renewable подписок с product id = SKU из Global Constraints; цены KZT: 9 990? — НЕТ: выбрать ближайшие к 9 900/19 900/39 900 и 99 000/199 000/399 000 точки сетки Apple (фактические записать в спеку §10.2); 1 consumable `topup.dialogs.500` = 3 900 ₸.
- [ ] Подать заявку в Apple Small Business Program (15%).
- [ ] Play Console: те же 6 подписок (base plans monthly/yearly) + 1 consumable, те же цены.
- [ ] RevenueCat: проект; приложения iOS+Android; привязать продукты; entitlements `tier_start`/`tier_business`/`tier_network` (по 2 продукта на каждый); default Offering с 6 packages; включить webhook → URL из Task 7 + Authorization-секрет.
- [ ] Ключи: public SDK keys (iOS, Android) и webhook-секрет передать для Task 7/10 (лягут в `secrets.json` и n8n).

### Task 1: PlanCatalog — тарифная матрица как код

**Files:**
- Create: `Assets/Scripts/Billing/PlanCatalog.cs`
- Test: `Assets/Tests/Editor/Billing/PlanCatalogTests.cs`

**Interfaces (Produces):** `enum PlanTier { None, Trial, Start, Business, Network }`; `struct PlanSpec { PlanTier Tier; int MaxBots; int MaxChannels; int DialogQuota; int PriceMonthKzt; int PriceYearKzt; string SkuMonth; string SkuYear; }`; `PlanCatalog.Get(PlanTier)`; консты `PlanCatalog.TrialDays=5`, `TrialDialogCap=150`, `TopUpDialogs=500`, `TopUpPriceKzt=3900`, `SkuTopUp`, `WarnThresholdPercent=80`, `PlanCatalog.FromEntitlementId(string)`.

- [ ] **Step 1: failing test**

```csharp
using NUnit.Framework;

public class PlanCatalogTests
{
    [TestCase(PlanTier.Start, 1, 1, 300, 9900, 99000)]
    [TestCase(PlanTier.Business, 3, 3, 1000, 19900, 199000)]
    [TestCase(PlanTier.Network, 5, 5, 3000, 39900, 399000)]
    public void Paid_tiers_match_spec(PlanTier t, int bots, int ch, int quota, int m, int y)
    {
        var p = PlanCatalog.Get(t);
        Assert.AreEqual(bots, p.MaxBots); Assert.AreEqual(ch, p.MaxChannels);
        Assert.AreEqual(quota, p.DialogQuota);
        Assert.AreEqual(m, p.PriceMonthKzt); Assert.AreEqual(y, p.PriceYearKzt);
    }

    [Test] public void Trial_is_business_shaped_with_150_cap()
    {
        var p = PlanCatalog.Get(PlanTier.Trial);
        Assert.AreEqual(3, p.MaxBots); Assert.AreEqual(3, p.MaxChannels);
        Assert.AreEqual(150, p.DialogQuota); Assert.AreEqual(0, p.PriceMonthKzt);
    }

    [Test] public void None_allows_nothing()
    {
        var p = PlanCatalog.Get(PlanTier.None);
        Assert.AreEqual(0, p.MaxBots); Assert.AreEqual(0, p.MaxChannels); Assert.AreEqual(0, p.DialogQuota);
    }

    [TestCase("tier_start", PlanTier.Start)]
    [TestCase("tier_business", PlanTier.Business)]
    [TestCase("tier_network", PlanTier.Network)]
    [TestCase("garbage", PlanTier.None)]
    public void Entitlement_ids_map(string id, PlanTier expected)
        => Assert.AreEqual(expected, PlanCatalog.FromEntitlementId(id));
}
```

- [ ] **Step 2: run — FAIL** (`bash Tools/run-tests-headless.sh PlanCatalogTests`, ожидаем compile error → создать файл из Step 3, снова FAIL по ассертам не будет — сразу PASS; это data-класс, допустимо)
- [ ] **Step 3: implementation**

```csharp
public enum PlanTier { None, Trial, Start, Business, Network }

public struct PlanSpec
{
    public PlanTier Tier;
    public int MaxBots, MaxChannels, DialogQuota, PriceMonthKzt, PriceYearKzt;
    public string SkuMonth, SkuYear;
}

public static class PlanCatalog
{
    public const int TrialDays = 5;
    public const int TrialDialogCap = 150;
    public const int TopUpDialogs = 500;
    public const int TopUpPriceKzt = 3900;
    public const string SkuTopUp = "topup.dialogs.500";
    public const int WarnThresholdPercent = 80;

    public static PlanSpec Get(PlanTier tier)
    {
        switch (tier)
        {
            case PlanTier.Trial:    return new PlanSpec { Tier = tier, MaxBots = 3, MaxChannels = 3, DialogQuota = TrialDialogCap };
            case PlanTier.Start:    return new PlanSpec { Tier = tier, MaxBots = 1, MaxChannels = 1, DialogQuota = 300,  PriceMonthKzt = 9900,  PriceYearKzt = 99000,  SkuMonth = "sub.start.month",    SkuYear = "sub.start.year" };
            case PlanTier.Business: return new PlanSpec { Tier = tier, MaxBots = 3, MaxChannels = 3, DialogQuota = 1000, PriceMonthKzt = 19900, PriceYearKzt = 199000, SkuMonth = "sub.business.month", SkuYear = "sub.business.year" };
            case PlanTier.Network:  return new PlanSpec { Tier = tier, MaxBots = 5, MaxChannels = 5, DialogQuota = 3000, PriceMonthKzt = 39900, PriceYearKzt = 399000, SkuMonth = "sub.network.month",  SkuYear = "sub.network.year" };
            default:                return new PlanSpec { Tier = PlanTier.None };
        }
    }

    public static PlanTier FromEntitlementId(string id)
    {
        switch (id)
        {
            case "tier_start": return PlanTier.Start;
            case "tier_business": return PlanTier.Business;
            case "tier_network": return PlanTier.Network;
            default: return PlanTier.None;
        }
    }
}
```

- [ ] **Step 4: run — PASS** (`total` в саммари ≥ 9)
- [ ] **Step 5: commit** `git add Assets/Scripts/Billing/ Assets/Tests/Editor/Billing/ && git commit -m "feat(billing): plan catalog — tier matrix as code"` (+ .meta; перед этим Assets/Refresh, чтобы .meta появились)

### Task 2: TrialLedger — 5-дневный триал, device-keyed

**Files:**
- Create: `Assets/Scripts/Billing/TrialLedger.cs`
- Test: `Assets/Tests/Editor/Billing/TrialLedgerTests.cs`

**Interfaces:**
- Consumes: `PlanCatalog.TrialDays`.
- Produces: `TrialLedger.HasStarted`, `StartIfNeeded()`, `DaysLeft()`, `IsExpired`, seams `TrialLedger.UtcNow/Load/Save` (внутр., для тестов — паттерн NotifPrefs/ThemePrefs). Хранение: PlayerPrefs key `"TrialStartedUtc"` (ISO-8601 roundtrip). Дыра reinstall принята спекой §3 — триал стоит ~0 ₸.

- [ ] **Step 1: failing test**

```csharp
using System;
using NUnit.Framework;

public class TrialLedgerTests
{
    string _stored; DateTime _now;

    [SetUp] public void Seams()
    {
        _stored = ""; _now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        TrialLedger.Load = _ => _stored;
        TrialLedger.Save = (_, v) => _stored = v;
        TrialLedger.UtcNow = () => _now;
    }
    [TearDown] public void Reset() => TrialLedger.ResetSeamsForTests();

    [Test] public void Fresh_install_has_full_trial_not_started()
    {
        Assert.IsFalse(TrialLedger.HasStarted);
        Assert.AreEqual(5, TrialLedger.DaysLeft());
        Assert.IsFalse(TrialLedger.IsExpired);
    }

    [Test] public void Start_stamps_once_and_counts_down()
    {
        TrialLedger.StartIfNeeded();
        var first = _stored;
        _now = _now.AddDays(2.5); TrialLedger.StartIfNeeded();
        Assert.AreEqual(first, _stored, "второй Start не перезаписывает");
        Assert.AreEqual(3, TrialLedger.DaysLeft());   // floor(2.5)=2 прошло
    }

    [Test] public void Expires_after_day_5()
    {
        TrialLedger.StartIfNeeded();
        _now = _now.AddDays(5.01);
        Assert.AreEqual(0, TrialLedger.DaysLeft());
        Assert.IsTrue(TrialLedger.IsExpired);
    }

    [Test] public void Clock_rollback_does_not_extend_past_five_days()
    {
        TrialLedger.StartIfNeeded();
        _now = _now.AddDays(-100);
        Assert.AreEqual(5, TrialLedger.DaysLeft());
        Assert.IsFalse(TrialLedger.IsExpired);
    }
}
```

- [ ] **Step 2: run — FAIL** (нет класса)
- [ ] **Step 3: implementation**

```csharp
using System;
using System.Globalization;
using UnityEngine;

public static class TrialLedger
{
    const string Key = "TrialStartedUtc";

    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static Func<string, string> Load = k => PlayerPrefs.GetString(k, "");
    internal static Action<string, string> Save = (k, v) => { PlayerPrefs.SetString(k, v); PlayerPrefs.Save(); };

    internal static void ResetSeamsForTests()
    {
        UtcNow = () => DateTime.UtcNow;
        Load = k => PlayerPrefs.GetString(k, "");
        Save = (k, v) => { PlayerPrefs.SetString(k, v); PlayerPrefs.Save(); };
    }

    public static bool HasStarted => !string.IsNullOrEmpty(Load(Key));

    public static void StartIfNeeded()
    {
        if (!HasStarted)
            Save(Key, UtcNow().ToString("o", CultureInfo.InvariantCulture));
    }

    public static int DaysLeft()
    {
        if (!HasStarted) return PlanCatalog.TrialDays;
        var start = DateTime.Parse(Load(Key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var elapsedDays = (int)Math.Floor((UtcNow() - start).TotalDays);
        if (elapsedDays < 0) elapsedDays = 0;   // откат часов не удлиняет триал; настоящий enforcement — серверный свип (Task 12)
        return Math.Max(0, PlanCatalog.TrialDays - elapsedDays);
    }

    public static bool IsExpired => HasStarted && DaysLeft() <= 0;
}
```

- [ ] **Step 4: run — PASS**
- [ ] **Step 5: commit** `git commit -m "feat(billing): trial ledger — 5-day device-keyed trial clock"`

### Task 3: EntitlementPolicy + QuotaMath — что можно при каком тарифе

**Files:**
- Create: `Assets/Scripts/Billing/EntitlementPolicy.cs`, `Assets/Scripts/Billing/QuotaMath.cs`
- Test: `Assets/Tests/Editor/Billing/EntitlementPolicyTests.cs`, `Assets/Tests/Editor/Billing/QuotaMathTests.cs`

**Interfaces:**
- Consumes: `PlanCatalog`, `TrialLedger`.
- Produces: `EntitlementPolicy.EffectiveTier(PlanTier purchased, bool trialStarted, bool trialExpired)` → purchased≠None ? purchased : (`None` ТОЛЬКО когда триал стартовал И истёк; НЕ стартовавший триал = `Trial` — pre-auth grace: часы триала запускает первая успешная авторизация (Task 15), а мастер первого бота должен открываться на свежей установке); `CanCreateBot(PlanTier, int existingBots)`, `CanConnectChannel(PlanTier, int connectedChannels)`; `enum QuotaState { Ok, Warn, Over }`; `QuotaMath.State(used, quota, topupBalance)`, `QuotaMath.Remaining(used, quota, topupBalance)`, `QuotaMath.Percent(used, quota)`.

- [ ] **Step 1: failing tests**

```csharp
using NUnit.Framework;

public class EntitlementPolicyTests
{
    [Test] public void Purchase_beats_trial()
        => Assert.AreEqual(PlanTier.Start, EntitlementPolicy.EffectiveTier(PlanTier.Start, true, true));

    [Test] public void Active_trial_when_nothing_purchased()
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, true, false));

    [Test] public void Expired_trial_without_purchase_is_none()
        => Assert.AreEqual(PlanTier.None, EntitlementPolicy.EffectiveTier(PlanTier.None, true, true));

    [Test] public void Not_started_trial_is_trial_grace()   // мастер первого бота должен открываться до первой авторизации
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, false, false));

    [TestCase(PlanTier.Start, 0, true)]
    [TestCase(PlanTier.Start, 1, false)]
    [TestCase(PlanTier.Business, 2, true)]
    [TestCase(PlanTier.Business, 3, false)]
    [TestCase(PlanTier.None, 0, false)]
    public void Bot_gate(PlanTier t, int existing, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanCreateBot(t, existing));

    [TestCase(PlanTier.Network, 4, true)]
    [TestCase(PlanTier.Network, 5, false)]
    [TestCase(PlanTier.Trial, 2, true)]
    [TestCase(PlanTier.Trial, 3, false)]
    public void Channel_gate(PlanTier t, int connected, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanConnectChannel(t, connected));
}

public class QuotaMathTests
{
    [Test] public void Under_80_is_ok() => Assert.AreEqual(QuotaState.Ok, QuotaMath.State(239, 300, 0));
    [Test] public void At_80_is_warn() => Assert.AreEqual(QuotaState.Warn, QuotaMath.State(240, 300, 0));
    [Test] public void Over_quota_without_topup_is_over() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(300, 300, 0));
    [Test] public void Topup_extends_quota() {
        Assert.AreEqual(QuotaState.Warn, QuotaMath.State(300, 300, 500));   // 300/800 = 37% но базовая квота выбрана → Warn, не Over
        Assert.AreEqual(QuotaState.Over, QuotaMath.State(800, 300, 500));
        Assert.AreEqual(500, QuotaMath.Remaining(300, 300, 500));
    }
    [Test] public void Zero_quota_is_over_at_zero() => Assert.AreEqual(QuotaState.Over, QuotaMath.State(0, 0, 0));
    [Test] public void Percent_clamps_100() => Assert.AreEqual(100, QuotaMath.Percent(999, 300));
}
```

- [ ] **Step 2: run — FAIL**
- [ ] **Step 3: implementation**

```csharp
public static class EntitlementPolicy
{
    public static PlanTier EffectiveTier(PlanTier purchased, bool trialStarted, bool trialExpired)
    {
        if (purchased != PlanTier.None) return purchased;
        // Не стартовавший триал = Trial (pre-auth grace): часы запускает первая авторизация,
        // а мастер первого бота обязан открываться на свежей установке.
        return trialStarted && trialExpired ? PlanTier.None : PlanTier.Trial;
    }

    public static bool CanCreateBot(PlanTier tier, int existingBots)
        => existingBots < PlanCatalog.Get(tier).MaxBots;

    public static bool CanConnectChannel(PlanTier tier, int connectedChannels)
        => connectedChannels < PlanCatalog.Get(tier).MaxChannels;
}
```

```csharp
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
```

- [ ] **Step 4: run — PASS** · **Step 5: commit** `feat(billing): entitlement policy + quota math`

### Task 4: PaywallCopy — цены и склонения по-русски

**Files:**
- Create: `Assets/Scripts/Billing/PaywallCopy.cs`
- Test: `Assets/Tests/Editor/Billing/PaywallCopyTests.cs`

**Interfaces:**
- Consumes: `RuPlural.Pick` (существующий seam, `Assets/Scripts/Chat/`), `PlanSpec`.
- Produces: `PaywallCopy.Kzt(int)` → `"9 900 ₸"` (узкий пробел NBSP U+00A0 между тысячами, руками — не culture); `PerMonth(int)` → `"9 900 ₸/мес"`; `YearLine(PlanSpec)` → `"99 000 ₸/год — 12 месяцев по цене 10"`; `Dialogs(int)` → `"300 диалогов"` / `"1 диалог"` / `"22 диалога"`; `TrialCta()` → `"Попробовать 5 дней бесплатно"`; `TrialPill(int daysLeft)` → `"Пробный · 5 дн."`.

- [ ] **Step 1: failing test**

```csharp
using NUnit.Framework;

public class PaywallCopyTests
{
    [TestCase(9900, "9 900 ₸")]
    [TestCase(199000, "199 000 ₸")]
    [TestCase(500, "500 ₸")]
    [TestCase(-199000, "-199\u00A0000\u00A0₸")]
    public void Kzt_groups_thousands_with_nbsp(int v, string s) => Assert.AreEqual(s, PaywallCopy.Kzt(v));

    [TestCase(1, "1 диалог")]
    [TestCase(22, "22 диалога")]
    [TestCase(300, "300 диалогов")]
    [TestCase(11, "11 диалогов")]
    public void Dialog_plural(int n, string s) => Assert.AreEqual(s, PaywallCopy.Dialogs(n));

    [Test] public void Trial_cta_is_five_days() => StringAssert.Contains("5 дней", PaywallCopy.TrialCta());
    [Test] public void Year_line_carries_12_for_10() => StringAssert.Contains("12 месяцев по цене 10", PaywallCopy.YearLine(PlanCatalog.Get(PlanTier.Start)));
    [Test] public void PerMonth_appends_suffix() => Assert.AreEqual("9\u00A0900\u00A0₸/мес", PaywallCopy.PerMonth(9900));
    [Test] public void TrialPill_formats_days() => Assert.AreEqual("Пробный · 3 дн.", PaywallCopy.TrialPill(3));
}
```

- [ ] **Step 2: run — FAIL** · **Step 3: implementation**

```csharp
using System.Text;

public static class PaywallCopy
{
    const char Nbsp = ' ';

    public static string Kzt(int amount)
    {
        var negative = amount < 0;
        var digits = System.Math.Abs((long)amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (negative) sb.Append('-');
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

    public static string TrialCta()
        => "Попробовать " + PlanCatalog.TrialDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
         + " " + RuPlural.Pick(PlanCatalog.TrialDays, "день", "дня", "дней") + " бесплатно";

    public static string TrialPill(int daysLeft)
        => "Пробный · " + daysLeft.ToString(System.Globalization.CultureInfo.InvariantCulture) + " дн.";
}
```

- [ ] **Step 4: run — PASS** · **Step 5: commit** `feat(billing): RU paywall copy — prices, plurals, trial strings`

### Task 5: EntitlementGate + клиентские гейты в Manager/BotsPage

**Files:**
- Create: `Assets/Scripts/Billing/EntitlementGate.cs`
- Modify: `Assets/Scripts/Main/Manager.cs` (вход в мастера создания бота; вход в auth-канала в `BotSettings.Auth.cs`), `Assets/Scripts/Main/BotsPage.cs` (кнопка «+»)
- Test: `Assets/Tests/Editor/Billing/EntitlementGateTests.cs`

**Interfaces:**
- Consumes: `EntitlementPolicy`, `TrialLedger`, позже `BillingService` (Task 10) — до неё purchased = None.
- Produces: `EntitlementGate.CurrentTier` (авто из purchased+trial, purchased сеттится Task 10), `EntitlementGate.CanCreateBot(int)`, `CanConnectChannel(int)`, `RequestPaywall(PaywallTrigger)` событие `OnPaywallRequested` (enum `PaywallTrigger { BotLimit, ChannelLimit, TrialExpired, Browse }`) — UI-задачи Task 12/14 подписываются.
- Счёт текущих ботов/каналов: боты = число живых Bot-объектов (`BotsParent`), каналы = число профилей с непустым `whatsappProfileId`/`telegramProfileId` и авторизованным статусом — собрать в `EntitlementGate.CountConnectedChannels(IEnumerable<Bot>)` как чистую функцию (тестируемо на фейках с парой строковых полей — вынести подсчёт в `static int CountChannels(IEnumerable<(bool wa, bool tg)>)`).

- [ ] Step 1: тест на `CountChannels` + `CanConnectChannels(connectedNow, demand)` (чистая арифметика «последний запрошенный слот влезает»: `demand <= 0 || CanConnectChannel(tier, connectedNow + demand - 1)`) + «gate возвращает false и зовёт `RequestPaywall`». Step 2: FAIL. Step 3: реализация + врезки: `BotsPage.StartNewBot` — `CanCreateBot(botsParent.childCount)` → `BotLimit`; **пре-флайт в начале `Manager.CreateBotFromForm`** — `CanCreateBot` + `CanConnectChannels(ConnectedChannelCount(), demand)` где demand = useWhatsapp+useTelegram, отказ ДО какой-либо авторизации (НЕ пер-лег гейты внутри мастера: Step-2-отказ после успешного WhatsApp-пейринга уничтожал бы завершённую авторизацию через CancelBotCreation); в BotSettings.Auth — гейт ТОЛЬКО fresh-ветки (`profileId == "-1"`), ре-авторизация существующего канала не гейтится. `ResetSeamsForTests` обнуляет и `OnPaywallRequested`. Step 4: PASS + весь Billing-фильтр зелёный. Step 5: commit `feat(billing): entitlement gate wired into bot/channel creation`.

### Task 6: Supabase-схема биллинга

**Files:**
- Create: `Tools/n8n/sql/2026-08-21-billing-schema.sql`

- [ ] **Step 1: SQL файл**

```sql
create table if not exists subscribers (
  app_user_id text primary key,
  plan text not null default 'trial' check (plan in ('trial','start','business','network','none')),
  status text not null default 'trialing' check (status in ('trialing','active','grace','expired')),
  trial_started_at timestamptz,
  current_period_end timestamptz,
  topup_balance int not null default 0,
  updated_at timestamptz not null default now()
);

create table if not exists bot_profiles (
  profile_id text primary key,
  app_user_id text not null,
  channel text not null check (channel in ('whatsapp','telegram')),
  created_at timestamptz not null default now(),
  deleted_at timestamptz
);
create index if not exists bot_profiles_owner_alive on bot_profiles (app_user_id) where deleted_at is null;

create table if not exists dialog_counts (
  app_user_id text not null,
  chat_id text not null,
  d date not null,
  primary key (app_user_id, chat_id, d)
);
create index if not exists dialog_counts_month on dialog_counts (app_user_id, d);
```

- [ ] **Step 2: применить** через Supabase SQL Editor (dashboard) — Supabase MCP тут read-only; альтернатива: одноразовый n8n workflow c Postgres-нодой (Session pooler 5432, НЕ 6543). Проверка: `select count(*) from subscribers;` → 0.
- [ ] **Step 3: commit** `feat(n8n): billing schema — subscribers, bot_profiles, dialog_counts`

### Task 7: n8n RevenueCat_Events — зеркало подписок

**Files:**
- Create (dev n8n → экспорт): `Tools/n8n/workflows/<id>-RevenueCat_Events.json`
- Create: `Tools/n8n/probe-billing.py` (расширяется в Task 8–9)

**Workflow (nodes):** `Webhook` (POST `/webhook/RevenueCatEvent`) → `If Auth` (`headers.authorization == секрет`; иначе respond 401) → `Code: Map Event` → `Postgres: Upsert Subscriber` → `Respond 200`.

- [ ] **Step 1: Map Event (Code node, полный текст)**

```javascript
const e = $json.body.event ?? $json.body;
const type = e.type;                       // INITIAL_PURCHASE | RENEWAL | ...
const appUserId = e.app_user_id;
const ent = (e.entitlement_ids && e.entitlement_ids[0]) || '';
const planByEnt = { tier_start: 'start', tier_business: 'business', tier_network: 'network' };
const out = { app_user_id: appUserId, topup_delta: 0 };

if (type === 'NON_RENEWING_PURCHASE' && e.product_id === 'topup.dialogs.500') {
  out.topup_delta = 500;                   // план/статус не трогаем
} else if (['INITIAL_PURCHASE','RENEWAL','UNCANCELLATION','PRODUCT_CHANGE'].includes(type)) {
  out.plan = planByEnt[ent] ?? 'none';
  out.status = 'active';
  out.period_end = e.expiration_at_ms ? new Date(e.expiration_at_ms).toISOString() : null;
} else if (type === 'EXPIRATION') {
  out.status = 'expired';
} else if (type === 'CANCELLATION') {
  return [];                               // автопродление выключили — доступ до period_end, ничего не меняем
}
return [{ json: out }];
```

- [ ] **Step 2: Upsert (Postgres node, query)**

```sql
insert into subscribers (app_user_id, plan, status, current_period_end, topup_balance, updated_at)
values ($1, coalesce($2,'trial'), coalesce($3,'trialing'), $4, greatest($5,0), now())
on conflict (app_user_id) do update set
  plan = coalesce($2, subscribers.plan),
  status = coalesce($3, subscribers.status),
  current_period_end = coalesce($4, subscribers.current_period_end),
  topup_balance = subscribers.topup_balance + $5,
  updated_at = now();
```
queryReplacement: `{{$json.app_user_id}},{{$json.plan ?? null}},{{$json.status ?? null}},{{$json.period_end ?? null}},{{$json.topup_delta}}`

- [ ] **Step 3: probe** — `Tools/n8n/probe-billing.py`: POST фейковых событий (INITIAL_PURCHASE business, NON_RENEWING topup, EXPIRATION) с секретом и без; ассерты: 401 без секрета; после серии — `plan=business,status=expired,topup_balance=500`. Run: `python3 Tools/n8n/probe-billing.py`. Expected: `ALL OK`.
- [ ] **Step 4:** экспорт JSON в репо + commit `feat(n8n): RevenueCat webhook mirror into subscribers`.

### Task 8: регистрация профилей + триала + слот-бэкстоп в Create*Workflow

**Files:**
- Modify (dev → экспорт): `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json`, `Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json`
- Modify: `Assets/Scripts/Main/Manager.cs` — обе Create*/Edit* формы получают поле `AppUserID` (значение из Task 10 `BillingService.AppUserId`; до неё — `SystemInfo.deviceUniqueIdentifier` за seam `BillingIdentity.AppUserId`)
- Create: `Assets/Scripts/Billing/BillingIdentity.cs` (+ тест на стабильность/непустоту через seam)

**Вставка в оба Create*-workflow (после парсинга формы, до создания клона):**
1. `Postgres: Ensure Subscriber` — `insert into subscribers (app_user_id, plan, status, trial_started_at) values ($1,'trial','trialing', now()) on conflict (app_user_id) do nothing;`
2. `Postgres: Count Channels` — `select count(*) c from bot_profiles where app_user_id=$1 and deleted_at is null;` + `select plan,status from subscribers where app_user_id=$1`.
3. `If Slot Limit` — лимит по plan (`Code`-мапа `{trial:3,start:1,business:3,network:5,none:0}`; status `expired`→0): `c >= limit` → `Respond {success:false, error:"channel_limit"}` (клиент показывает гейт-шит Task 14).
4. `Postgres: Register Profile` — `insert into bot_profiles (profile_id, app_user_id, channel) values ($1,$2,$3) on conflict (profile_id) do update set deleted_at = null;`

- [ ] Step 1: клиент — `BillingIdentity` + поле формы (`form.AddField("AppUserID", BillingIdentity.AppUserId)`) в 4 местах (`CreateWhatsappWorkflowFromStart/FromEdit`, Telegram-пара). Step 2: n8n-вставки на dev. Step 3: probe (расширить `probe-billing.py`: дважды создать «профиль» одному user при plan=start → второй ответ `channel_limit`). Step 4: экспорт + commit `feat(n8n): profile registry, trial upsert, channel-slot backstop`.

### Task 9: счётчик диалогов + enforcement в бот-workflow

**Files:**
- Modify (dev → экспорт): `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `4VN3gsFaC2HUYmcc-Telegram_Bot.json`

**Правило (из спеки):** диалог = (app_user_id, chat_id, дата Asia/Almaty). НОВЫЙ диалог сверх `quota+topup` → бот НЕ шлёт автоответ (ведёт себя как «Вместе»); диалог, начатый в пределах квоты, договаривает сутки. Порядок узлов: после `Suppressed?` (false-ветка = разрешено отвечать) вставить `Postgres: Count Dialog` → `If Over Quota` → true → END (без отправки); false → дальше по существующей цепочке.

- [ ] **Step 1: Count Dialog (Postgres node, один запрос)**

```sql
with me as (
  select bp.app_user_id, s.plan, s.status, s.topup_balance
  from bot_profiles bp join subscribers s using (app_user_id)
  where bp.profile_id = $1
), ins as (
  insert into dialog_counts (app_user_id, chat_id, d)
  select app_user_id, $2, (now() at time zone 'Asia/Almaty')::date from me
  on conflict do nothing
  returning 1
)
select
  me.plan, me.status, me.topup_balance,
  exists(select 1 from ins) as is_new,
  (select count(*) from dialog_counts dc, me
     where dc.app_user_id = me.app_user_id
       and date_trunc('month', dc.d) = date_trunc('month', (now() at time zone 'Asia/Almaty')::date)) as used
from me;
```
queryReplacement: `{{profileId из контекста workflow}},{{chatId сообщения}}` (profileId в клонах уже зашит — взять из того же выражения, которым его читает существующий `HTTP Request`-отправитель).

- [ ] **Step 2: If Over Quota** — `Code`-мапа квот `{trial:150,start:300,business:1000,network:3000,none:0}` (status `expired|grace` → 0); условие: `is_new && used > quota + topup_balance` → true-ветка в END.
- [ ] **Step 3: probe** — сценарий: подписчик start (300), нагенерить 300 строк dialog_counts SQL-ом, послать сообщение НОВОГО chat_id → бот молчит; послать в СТАРЫЙ chat_id этого дня → бот отвечает. Прогнать для обоих каналов.
- [ ] **Step 4:** ВАЖНО — клоны существующих ботов не обновятся сами (правило «old clones keep old behavior»): для тестовых ботов владельца пересоздать/прогнать Edit. Экспорт + commit `feat(n8n): per-day dialog metering with quota enforcement`.

### Task 10: RevenueCat SDK + BillingService

**Files:**
- Add package: purchases-unity 9.5.x (UPM tarball по официальной инструкции RevenueCat; папка `Assets/Plugins/RevenueCat/` — как NativeFilePicker и пр.)
- Create: `Assets/Scripts/Billing/BillingService.cs`, `Assets/Scripts/Billing/IBillingBackend.cs`, `Assets/Scripts/Billing/RevenueCatBackend.cs`, `Assets/Scripts/Billing/FakeBillingBackend.cs` (Editor)
- Modify: `Assets/StreamingAssets/secrets.json.example` (+ `revenueCat: { iosKey, androidKey }`), `Secrets.cs` (поля)
- Test: `Assets/Tests/Editor/Billing/BillingServiceTests.cs`

**Interfaces:**
- Produces: `BillingService.Initialize()` (ключ по платформе из Secrets; Editor → FakeBackend), `BillingService.AppUserId` (RevenueCat anonymous id; Editor — `SystemInfo.deviceUniqueIdentifier`; `BillingIdentity.AppUserId` из Task 8 переключить сюда), `PurchasedTier` (максимальный активный entitlement через `PlanCatalog.FromEntitlementId`), события `OnEntitlementChanged`; `Purchase(string sku, Action<bool,string>)`, `RestorePurchases(Action<bool>)`. `EntitlementGate` начинает читать `purchased = BillingService.PurchasedTier`.
- Тестируется маппинг entitlement→tier и выбор максимума на `FakeBillingBackend` (например `["tier_start","tier_network"]` → Network). Реальный SDK — device-only, в Editor не инициализируется.

- [ ] Steps: тест маппинга (FAIL→PASS), реализация, wiring в `Manager.Start()` (`BillingService.Initialize()`), commit `feat(billing): RevenueCat service behind backend seam`.

### Task 11: Get_Usage + клиентский UsageStore

**Files:**
- Create (dev → экспорт): `Tools/n8n/workflows/<id>-Get_Usage.json` — POST `/webhook/GetUsage` `{appUserId}` → один Postgres-запрос → `{plan,status,quota,used,topupBalance,botsRegistered,channelsConnected,periodEnd}` (quota из той же Code-мапы, used — как в Task 9, channels — count bot_profiles alive).
- Create: `Assets/Scripts/Billing/UsageStore.cs` (модель `UsageSnapshot` + `Parse(string json)` через JsonConvert + событие `OnUsageChanged`; координатор fetch — coroutine в `Manager` по паттерну существующих вызовов, с явным Content-Type), Test: `UsageStoreTests.cs` (парс валидного/битого JSON).
- [ ] Steps: тест парса → реализация → probe вебхука → wiring (fetch при открытии «Боты» и после каждого ответа бота не нужен — при `OnChatSelected` и на открытии приложения достаточно) → commit `feat(billing): usage endpoint + client usage store`.

### Task 12: Profile_Lifecycle_Sweep — защита от ретро-списания 6-го дня

**Files:**
- Create (dev → экспорт): `Tools/n8n/workflows/<id>-Profile_Lifecycle_Sweep.json`

**Workflow:** Schedule (каждые 6 часов) → `Postgres: Candidates`:

```sql
select bp.profile_id, bp.channel, bp.app_user_id
from bot_profiles bp
join subscribers s using (app_user_id)
where bp.deleted_at is null
  and (
    (s.status = 'trialing' and bp.created_at < now() - interval '4 days 17 hours')
    or (s.status in ('expired','grace') and coalesce(s.current_period_end, now() - interval '99 days') < now() - interval '3 days')
  );
```
→ Loop: `HTTP: profile/delete` (base `api|tapi` по channel, header Authorization; те же вызовы, что в Delete Orphan Profiles) → `Postgres: mark` `update bot_profiles set deleted_at = now() where profile_id = $1;` → `Postgres: demote` `update subscribers set status='expired' where app_user_id=$1 and status='trialing';`

**Инварианты (как у orphan-sweep):** никогда не трогает профили владельцев со `status='active'`; идемпотентен; первый запуск — с выключенной веткой delete (dry-run, только лог кандидатов) и ручной проверкой списка.

- [ ] Steps: build → dry-run на dev с фейковыми строками (created_at бэкдейтнуть SQL-ом) → включить delete → probe: триал-профиль старше 4д17ч исчез из Wappi и помечен deleted_at; активный подписчик не тронут → экспорт + commit `feat(n8n): profile lifecycle sweep — day-5 trial deletion + churn grace`.

### Task 13: модель + prompt caching в бот-шаблонах

**Files:**
- Modify (dev → экспорт): оба бот-шаблона — `OpenAI [lmChatOpenAi]` node.

- [ ] Явно зафиксировать model id = актуальный mini-класс (`gpt-5.4-mini`); проверить, что systemMessage (статичная часть: vertical prompt + business knowledge) стоит ПЕРВОЙ и ≥1024 токенов — тогда OpenAI кэширует префикс автоматически (cached input ×0.1).
- [ ] Probe: два последовательных запроса одному боту → в OpenAI usage появляются `cached_tokens > 0` на втором. Зафиксировать скрин/числа в PR-описании.
- [ ] Экспорт + commit `feat(n8n): pin mini model + verify prompt-prefix caching`.

### Task 14: UI — пейволл, Подписка, пилюля+счётчик, гейт-шиты, чек ценности

Разбито на 4 подзадачи-билдера; каждый — additive, по образцу ближайшего билдера; после каждого: сохранить сцену, Game view 1080×2400 сверка с утверждёнными мокапами (тёмная тема), commit сцены сразу.

**14a. `Assets/Editor/PaywallBuilder.cs`** (`Tools/Billing/Build Paywall`) — новый `Screen_Paywall` в `ScreenContainer` ПОСЛЕ `Screen_New`, ДО auth-экранов; обновить список в `NavRestructureBuilder.ReorderScreens` (правило «builders must rewire consumers»). Контент по мокапу «paywall_v2_dark_unified_features»: заголовок H1 50 «Все возможности — в каждом тарифе», тумблер Месяц/Год (сегмент-контрол 96 выс.), 3 карточки тарифа (радиус 42, внутр. отступ 48, выбранная — рамка 6 `accentFill` + бейдж «Популярный»), блок «ВО ВСЕХ ТАРИФАХ» (overline 26 + карточка с 8 строками, чекмарк = Image спрайт galочки из `Assets/Images/Icons/`, тинт `positiveInk`), CTA 132 выс. `accentFill` в thumb-zone, подпись caption 28 `inkTertiary`. Все цвета — `ThemedColor` бинды на Theme-роли. Контроллер `Assets/Scripts/Billing/PaywallController.cs`: строит строки из `PlanCatalog`+`PaywallCopy` (single source), période toggle, `BillingService.Purchase`, `RestorePurchases`, вариант «чек ценности» (заголовок-блок со статами из `DashboardMetrics` при `PaywallTrigger.TrialExpired`), подписка на `EntitlementGate.OnPaywallRequested`. DOTween slide-in 0.3 OutCubic.
**14b. `Assets/Editor/SubscriptionPageBuilder.cs`** — шестая под-страница «Подписка» в ProfileSubPages-паттерне (расширить массив страниц, builder по образцу `ProfileSubPagesBuilder`): карточка тарифа+статус+`periodEnd`, прогресс диалогов из `UsageStore`, строки «Изменить тариф» (→ Screen_Paywall), «Купить 500 диалогов — 3 900 ₸» (`Purchase(SkuTopUp)`), «Восстановить покупки», «Отменить подписку» (deep-link в управление подписками стора).
**14c. `Assets/Editor/BotsPageBillingWirer.cs`** — в шапку «Боты»: пилюля `TrialPill` (текст `PaywallCopy.TrialPill`, tap → paywall Browse; видна только при `CurrentTier==Trial`); под шапкой строка-счётчик «Диалоги ИИ · <месяц RuDateFormat>» + прогресс (`accentFill`→`#F8942F` при Warn→`negative` при Over) из `UsageStore`; dashed-карточка «+ бот» получает сабтекст «Ещё N ботов в тарифе» из `EntitlementGate`.
**14d. Гейт-шиты** — bottom-sheet по паттерну ItemEditSheet (свой лёгкий билдер `BillingGateSheetBuilder`): два варианта копий («Лимит ботов вашего тарифа», «Лимит каналов») + CTA «Посмотреть тарифы»; показывается из `OnPaywallRequested(BotLimit|ChannelLimit)` ПЕРЕД полным пейволлом.

- [ ] Каждая подзадача: builder → scene save → визуальная сверка (если Game view недоступен мне — явно передать владельцу чеклист что смотреть) → commit (`feat(billing-ui): ...`).

### Task 15: связка триала и сквозной прогон

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` (auth-success точка), `PaywallController.cs`

- [ ] `TrialLedger.StartIfNeeded()` в момент первой успешной авторизации канала (существующий success-обработчик в Manager/BotSettings.Auth — та же точка, где сегодня включается success-оверлей).
- [ ] На старте приложения: `if (EntitlementGate.CurrentTier == PlanTier.None && TrialLedger.IsExpired)` → paywall (TrialExpired, вариант «чек ценности»).
- [ ] **E2E чеклист (device, сторы в sandbox):** свежая установка → мастер → авторизация WhatsApp → триал стартовал (пилюля «5 дн.») → бот отвечает → счётчик растёт → sandbox-покупка Старт (месяц) → entitlement активен, пилюля исчезла, гейты = 1/1/300 → попытка второго канала → гейт-шит → sandbox-апгрейд Бизнес → канал подключается → restore на переустановке → топ-ап +500 отражается в Get_Usage. Серверно: строки в subscribers/bot_profiles/dialog_counts корректны; sweep в dry-run не видит активного подписчика.
- [ ] Commit `feat(billing): trial start wiring + e2e pass` + отметить в спеке §10.2 фактические стор-цены.

---

## Self-Review (выполнено при написании)

1. **Spec coverage:** §2 матрица → Task 1; §3 триал → Task 2, 8 (upsert), 12 (день-5 удаление), 15 (старт+receipt); §4 кэширование → Task 13; §5.1 → Task 7; §5.2–3 → Task 9, 11; §5.4 → Task 12; §5.5 → Task 5 (клиент) + 8 (бэкстоп); §6 → Task 10, 14a–d; SKU/консоли → Task 0. Промо (founding/рефералка) — офферы конфигурируются в RevenueCat/сторах без кода: добавлено в Task 0 чеклистом? — НЕТ: сознательно отложено до конца Блока 1 (нужен работающий биллинг раньше промо; вернуть при запуске маркетинга). Блок 2 — отдельный план.
2. **Placeholder scan:** каждая кодовая задача несёт полный код/SQL/JS; билдерные задачи задают иерархию+метрики+паттерн-источник — это документированный процесс проекта (unity-ui-builder), не заглушка.
3. **Type consistency:** `PlanTier/PlanSpec/QuotaState`, `EntitlementGate.RequestPaywall(PaywallTrigger)`, `BillingIdentity.AppUserId`→`BillingService.AppUserId` (Task 8→10 переключение указано), SKU-строки и entitlement-ids едины по всем задачам.
