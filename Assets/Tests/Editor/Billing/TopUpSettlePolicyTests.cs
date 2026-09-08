using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

/// <summary>
/// Pins the post-top-up settle (2026-09-08). Live evidence on prod n8n, 2026-09-06 15:20:19Z:
/// execution 1546 credited the reserve 1 500 → 2 000 and the app's GetUsage read 1547, fired
/// from the purchase callback in the SAME second, still returned 1 500 — the store's callback
/// lands before RevenueCat's webhook has written the balance. The page then said «Диалоги
/// начислены» over the old number until it was reopened 22 s later. One immediate read is a
/// coin flip; the fix re-reads on a short back-off schedule until the reserve has grown.
/// </summary>
public class TopUpSettlePolicyTests
{
    [Test]
    public void The_first_read_is_immediate_and_the_rest_back_off()
    {
        Assert.AreEqual(0f, TopUpSettlePolicy.DelayBeforeRead(0), "первый запрос — сразу, как и раньше");
        Assert.AreEqual(2f, TopUpSettlePolicy.DelayBeforeRead(1));
        Assert.AreEqual(5f, TopUpSettlePolicy.DelayBeforeRead(2));
        Assert.AreEqual(10f, TopUpSettlePolicy.DelayBeforeRead(3));
        Assert.AreEqual(20f, TopUpSettlePolicy.DelayBeforeRead(4));
    }

    [Test]
    public void The_schedule_runs_out_instead_of_polling_forever()
    {
        Assert.IsNull(TopUpSettlePolicy.DelayBeforeRead(5));
        Assert.IsNull(TopUpSettlePolicy.DelayBeforeRead(-1));

        float total = 0f;
        for (int i = 0; ; i++)
        {
            float? d = TopUpSettlePolicy.DelayBeforeRead(i);
            if (!d.HasValue) break;
            total += d.Value;
        }
        Assert.LessOrEqual(total, 40f, "весь цикл дочитываний должен укладываться в ~40 с");
    }

    [Test]
    public void Settles_once_the_reserve_grew_by_the_packs_bought()
    {
        // The live race: baseline 1 500, one pack bought, the stale read still says 1 500.
        Assert.IsFalse(TopUpSettlePolicy.Settled(1500, 1, 1500), "прочитали старый баланс — ждать дальше");
        Assert.IsTrue(TopUpSettlePolicy.Settled(1500, 1, 2000), "вебхук долетел");

        // A second pack bought while the first is still landing raises the bar.
        Assert.IsFalse(TopUpSettlePolicy.Settled(2000, 2, 2500), "долетел только один из двух");
        Assert.IsTrue(TopUpSettlePolicy.Settled(2000, 2, 3000));

        // More than expected (a consolidation landed too) is still settled.
        Assert.IsTrue(TopUpSettlePolicy.Settled(2000, 1, 2600));
    }

    [Test]
    public void An_unknown_baseline_never_settles_so_the_whole_schedule_runs()
    {
        // No snapshot before the tap: nothing to compare against, so every read is taken and
        // the page simply converges on whatever the server says.
        Assert.IsFalse(TopUpSettlePolicy.Settled(null, 1, 2500));
        Assert.IsFalse(TopUpSettlePolicy.Settled(null, 1, 0));
    }

    // ── Wiring guard: the purchase callback must go through the settle, not a single fetch ──

    private const string PagePath = "Assets/Scripts/Main/ProfileSubPages.Subscription.cs";

    [Test]
    public void The_top_up_callback_starts_the_settle_instead_of_one_bare_fetch()
    {
        string body = MethodBody(PagePath, "OnTopUpClicked");
        StringAssert.Contains("BeginTopUpSettle(", body,
            "после успешной докупки баланс дочитывается по расписанию TopUpSettlePolicy");
        StringAssert.DoesNotContain("FetchUsage(", body,
            "одиночный FetchUsage() после покупки — это и есть гонка с вебхуком (exec 1546/1547)");

        string settle = MethodBody(PagePath, "TopUpSettleRoutine");
        StringAssert.Contains("TopUpSettlePolicy.DelayBeforeRead(", settle);
        StringAssert.Contains("TopUpSettlePolicy.Settled(", settle);
        StringAssert.Contains("UsageClient.FetchRoutine()", settle, "читает через тот же клиент, что и все остальные");
    }

    /// <summary>Brace-matched body of <paramref name="method"/> (SendPathWiringTests idiom).</summary>
    private static string MethodBody(string relativePath, string method)
    {
        string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
        Match m = Regex.Match(source, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{");
        Assert.IsTrue(m.Success, $"{method} not found in {relativePath} — update TopUpSettlePolicyTests");
        int depth = 0;
        for (int i = m.Index + m.Length - 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(m.Index, i - m.Index + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}
