using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.8): «Сводка» shows nothing from a personal chat — at once, not two days later when the
/// server wipes its data. <see cref="DashboardAudienceFilter"/> drops a row whose chat is personal for the assistant
/// (<see cref="ChatAudienceStore.KindOf"/> under the row's profile: the owner's move, else the server's decision) and keeps a
/// chat nobody has decided. <see cref="DashboardPage"/> filters before its metrics — the numbers and the rows agree — and
/// filters again on every decision or move while it is shown.
/// </summary>
public class DashboardAudienceFilterTests
{
    private const string PagePath = "Scripts/Main/Dashboard/DashboardPage.cs";
    private const string Wa = "dash-wa-profile";
    private const string Tg = "dash-tg-profile";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;

    [SetUp]
    public void UseATestStore()
    {
        dir = Path.Combine(Path.GetTempPath(), "DashboardAudienceFilterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
    }

    [TearDown]
    public void RestoreTheStore()
    {
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void Dashboard_HidesPersonalRows_KeepsUnknown()
    {
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Wa, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    Decision("77000000001@c.us", AudienceKind.Personal, AudienceReason.Contact),
                    Decision("77000000002@c.us", AudienceKind.Business, AudienceReason.New),
                    Decision("77000000004@c.us", AudienceKind.Business, AudienceReason.New),
                    Decision("77000000005@c.us", AudienceKind.Personal, AudienceReason.History),
                },
            },
        });
        ChatAudienceStore.ApplyDesire(Wa, "77000000004@c.us", AudienceKind.Personal);   // the owner just moved it away
        ChatAudienceStore.ApplyDesire(Wa, "77000000005@c.us", AudienceKind.Business);   // …and this one to work

        var rows = new List<DashboardOutcome>
        {
            Row(Wa, "77000000001@c.us"),   // personal by the server's decision
            Row(Wa, "77000000002@c.us"),   // working
            Row(Wa, "77000000003@c.us"),   // nobody has decided it — kept
            Row(Wa, "77000000004@c.us"),   // made personal by the owner, not confirmed yet
            Row(Wa, "77000000005@c.us"),   // made working by the owner
            Row(Tg, "77000000001@c.us"),   // the same id under another profile — decisions are per profile
            null,
        };

        List<DashboardOutcome> shown = DashboardAudienceFilter.WithoutPersonal(rows);
        CollectionAssert.AreEqual(
            new[] { $"{Wa}:77000000002@c.us", $"{Wa}:77000000003@c.us", $"{Wa}:77000000005@c.us", $"{Tg}:77000000001@c.us" },
            shown.Select(r => $"{r.profileId}:{r.chatId}").ToArray(),
            "personal rows go, in place; working and undecided ones stay in their order");
    }

    [Test]
    public void WithoutPersonal_NothingGiven_IsEmpty()
    {
        Assert.IsEmpty(DashboardAudienceFilter.WithoutPersonal(null));
        Assert.IsEmpty(DashboardAudienceFilter.WithoutPersonal(new List<DashboardOutcome>()));
    }

    [Test]
    public void TheNumbers_FollowTheRows_AndAMoveReachesThemAtOnce()
    {
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Wa, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    Decision("77000000001@c.us", AudienceKind.Personal, AudienceReason.Contact),
                    Decision("77000000002@c.us", AudienceKind.Business, AudienceReason.New),
                },
            },
        });
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        (DashboardPage page, TextMeshProUGUI hero) = PageWith(Order(Wa, "77000000001@c.us", now), Order(Wa, "77000000002@c.us", now));

        Invoke(page, "Render");
        Assert.AreEqual("1", hero.text, "an order collected in a personal chat is no order in «Сводка»");

        ChatAudienceStore.ApplyDesire(Wa, "77000000002@c.us", AudienceKind.Personal);   // the owner makes the other one personal
        Invoke(page, "HandleAudienceChanged");
        Assert.AreEqual("0", hero.text, "a chat made personal leaves the numbers at once");
    }

    [Test]
    public void ThePage_FiltersBeforeItsMetrics_AndAgainOnEveryChange()
    {
        string page = Read(PagePath);
        Assert.IsTrue(MethodBody(page, "private void OnEnable()").Contains("ChatAudienceStore.OnChanged += HandleAudienceChanged"),
            "while the page is shown it hears every decision and move");
        Assert.IsTrue(MethodBody(page, "private void OnDisable()").Contains("ChatAudienceStore.OnChanged -= HandleAudienceChanged"));
        Assert.IsTrue(MethodBody(page, "private void HandleAudienceChanged()").Contains("Render()"));

        Assert.IsTrue(Squash(MethodBody(page, "private List<DashboardOutcome> VisibleRows()")).Contains("DashboardAudienceFilter.WithoutPersonal(_all)"),
            "one place reads the rows «Сводка» may show");
        string render = MethodBody(page, "private void Render()");
        int visible = render.IndexOf("VisibleRows()", StringComparison.Ordinal);
        int metrics = render.IndexOf("DashboardMetrics.", StringComparison.Ordinal);
        Assert.IsTrue(visible >= 0 && metrics > visible, "the counts, the delta and the recent orders are computed from the visible rows");
        Assert.IsFalse(render.Contains("_all"), "nothing in Render reads the unfiltered rows — the empty state neither");
        Assert.IsTrue(MethodBody(page, "private List<DashboardOutcome> StatusRows(").Contains("VisibleRows()"), "the drill-down list too");
        Assert.AreEqual(0, Regex.Matches(page, @"FilterByProfiles\(\s*_all").Count, "no metric or list over the unfiltered rows");
    }

    // ---- helpers ----

    private (DashboardPage page, TextMeshProUGUI hero) PageWith(params DashboardOutcome[] rows)
    {
        var host = new GameObject("DashboardHost");   // AddComponent in EditMode runs neither Awake nor OnEnable
        hosts.Add(host);
        var page = host.AddComponent<DashboardPage>();
        var heroGo = new GameObject("HeroCount", typeof(RectTransform));
        hosts.Add(heroGo);
        var hero = heroGo.AddComponent<TextMeshProUGUI>();
        var refs = new SerializedObject(page);
        SerializedProperty heroProp = refs.FindProperty("heroCount");
        Assert.IsNotNull(heroProp, "DashboardPage.heroCount moved — update this test");
        heroProp.objectReferenceValue = hero;
        refs.ApplyModifiedPropertiesWithoutUndo();

        FieldInfo all = typeof(DashboardPage).GetField("_all", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(all, "DashboardPage._all moved — update this test");
        ((List<DashboardOutcome>)all.GetValue(page)).AddRange(rows);
        return (page, hero);
    }

    private static void Invoke(DashboardPage page, string method)
    {
        MethodInfo info = typeof(DashboardPage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, $"DashboardPage.{method} is gone");
        OpenChatFixture.Call(() => info.Invoke(page, null));
    }

    private static AudienceDecision Decision(string chatId, AudienceKind kind, AudienceReason reason) =>
        new AudienceDecision { ChatId = chatId, Kind = kind, Reason = reason, ByOwner = reason == AudienceReason.Owner, Version = 1 };

    private static DashboardOutcome Row(string profileId, string chatId) =>
        new DashboardOutcome { profileId = profileId, chatId = chatId, outcome = "in_dialog" };

    private static DashboardOutcome Order(string profileId, string chatId, long atMs) =>
        new DashboardOutcome { profileId = profileId, chatId = chatId, outcome = "order_collected", outcomeAt = atMs, lastMessageAt = atMs };

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ");
}
