using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pins WHERE the first-run welcome carousel is raised from — the half of ONB-01 that
/// EditMode could not see before.
///
/// The reported bug: on a fresh install the carousel did not appear at launch, only once
/// the owner happened to open the Bots tab. <see cref="OnboardingGate.ShouldShowCarousel"/>
/// was correct and unit-tested all along; it was simply never ASKED at boot, because the
/// only caller was <c>BotsPage.RefreshEmptyState</c> ← <c>BotsPage.OnEnable</c>, and the app
/// launches on the Chats tab (<c>BottomTabManager.defaultTabIndex = 0</c>) with Screen_Bots
/// serialized inactive. «Удалить все данные» — the sanctioned first-run reset — ends with
/// <c>NavigateToWhatsAppTab()</c>, so it had the same blind spot.
///
/// So these tests cover two things: the entry point behaves like the gate on a host that is
/// INACTIVE (the boot condition), and the two out-of-screen callers still call it (a source
/// guard, because no EditMode test can boot the app — the same reason
/// <see cref="TailOutlineShaderTests"/> reads its shader as text).
/// </summary>
public class BotsPageFirstRunCarouselTests
{
    private GameObject pageGo;
    private GameObject carouselGo;
    private GameObject botsParentGo;
    private bool hadSeenKey;
    private int savedSeen;

    [SetUp]
    public void SetUp()
    {
        // The Editor's own PlayerPrefs are the owner's live first-run state — save and
        // restore it, never leave a test's value behind.
        hadSeenKey = PlayerPrefs.HasKey(OnboardingKeys.Seen);
        savedSeen = PlayerPrefs.GetInt(OnboardingKeys.Seen, 0);
        PlayerPrefs.DeleteKey(OnboardingKeys.Seen);
    }

    [TearDown]
    public void TearDown()
    {
        if (pageGo != null) Object.DestroyImmediate(pageGo);
        if (carouselGo != null) Object.DestroyImmediate(carouselGo);
        if (botsParentGo != null) Object.DestroyImmediate(botsParentGo);
        SetStaticInstance(null);

        if (hadSeenKey) PlayerPrefs.SetInt(OnboardingKeys.Seen, savedSeen);
        else PlayerPrefs.DeleteKey(OnboardingKeys.Seen);
    }

    // ── The gate entry point, on an INACTIVE host (the boot condition) ───────────────

    [Test]
    public void TryShow_FirstRun_ActivatesCarousel_EvenWhileScreenBotsIsInactive()
    {
        BotsPage page = BuildPage(bots: 0);

        Assert.IsFalse(page.gameObject.activeSelf, "test setup: the host must be inactive — that IS the boot condition");
        Assert.IsTrue(page.TryShowFirstRunCarousel(), "no bots + OnboardingSeen unset is a true first run");
        Assert.IsTrue(carouselGo.activeSelf, "the carousel must take the screen at boot, not wait for the Bots tab");
    }

    [Test]
    public void TryShow_AlreadySeen_LeavesCarouselHidden()
    {
        BotsPage page = BuildPage(bots: 0);
        PlayerPrefs.SetInt(OnboardingKeys.Seen, 1);

        Assert.IsFalse(page.TryShowFirstRunCarousel(), "ONB-01: the carousel is once per install");
        Assert.IsFalse(carouselGo.activeSelf);
    }

    [Test]
    public void TryShow_ExistingUserWithBots_LeavesCarouselHidden()
    {
        BotsPage page = BuildPage(bots: 1);

        Assert.IsFalse(page.TryShowFirstRunCarousel(), "ONB-01: any bot present ⇒ existing user ⇒ never");
        Assert.IsFalse(carouselGo.activeSelf);
    }

    [Test]
    public void TryShow_NoCarouselInScene_ReturnsFalseSoTheAutoOpenStillRuns()
    {
        BotsPage page = BuildPage(bots: 0);
        SetPrivate(page, "onboardingScreen", null);

        // A not-yet-built scene must fall through to the existing AddBotPanel auto-open
        // rather than dead-ending a brand-new user on an empty page.
        Assert.IsFalse(page.TryShowFirstRunCarousel());
    }

    [Test]
    public void TryShow_IsIdempotent()
    {
        BotsPage page = BuildPage(bots: 0);

        Assert.IsTrue(page.TryShowFirstRunCarousel());
        Assert.IsTrue(page.TryShowFirstRunCarousel(), "boot and the Bots-tab chokepoint both fire — the second must be a no-op, not a fault");
        Assert.IsTrue(carouselGo.activeSelf);
    }

    // ── The handle the boot caller reaches it through ────────────────────────────────

    [Test]
    public void Instance_ResolvesWhileEveryBotsPageIsInactive()
    {
        // Before the fix Instance was a field assigned in Start(), i.e. null until the Bots
        // tab had been opened once — so `BotsPage.Instance?.TryShowFirstRunCarousel()` at
        // boot would have been a silent no-op, and so was the carousel's own «Создать бота»
        // (BotsPage.Instance?.StartNewBot()). The Chats empty-state CTA already had to
        // hand-roll an include-inactive lookup for exactly this reason (D12, device).
        BuildPage(bots: 0);
        SetStaticInstance(null);

        foreach (var live in Object.FindObjectsByType<BotsPage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Assume.That(live.gameObject.activeInHierarchy, Is.False,
                "precondition: no ACTIVE BotsPage may exist, or resolving one proves nothing");

        Assert.IsNotNull(BotsPage.Instance, "Instance must resolve include-inactive — it is the boot path's only handle");
    }

    // ── Source guards: the callers EditMode cannot boot ──────────────────────────────

    [Test]
    public void ManagerRaisesTheCarouselAtBoot()
    {
        // Manager.LoadBots' tail is the first moment the LIVE bot count is truthful, and it
        // already asks the other half of the same question there (ShouldAutoFlagSeen).
        string src = ReadScript("Scripts/Main/Manager.cs");
        StringAssert.Contains("TryShowFirstRunCarousel", src,
            "Manager must raise the first-run carousel at boot — without it the app launches " +
            "on the Chats tab and onboarding only appears if the owner opens the Bots tab.");
    }

    [Test]
    public void FullWipeRaisesTheCarouselAgain()
    {
        // «Удалить все данные» restores a true first-run state and then navigates to the
        // Chats tab, so it must re-raise the carousel itself.
        string src = ReadScript("Scripts/Main/ProfileSubPages.Account.cs");
        StringAssert.Contains("TryShowFirstRunCarousel", src,
            "the full local wipe is a first run again — it must re-raise the carousel, not " +
            "wait for the next launch.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>A BotsPage on an INACTIVE host, with a hidden carousel and `bots` cards.</summary>
    private BotsPage BuildPage(int bots)
    {
        pageGo = new GameObject("Screen_Bots_Test");
        pageGo.SetActive(false);                 // never runs Start/OnEnable — the boot condition
        BotsPage page = pageGo.AddComponent<BotsPage>();

        carouselGo = new GameObject("Screen_Onboarding_Test");
        carouselGo.SetActive(false);

        botsParentGo = new GameObject("BotsParent_Test");
        for (int i = 0; i < bots; i++)
            new GameObject("Bot" + i).transform.SetParent(botsParentGo.transform);

        SetPrivate(page, "onboardingScreen", carouselGo);
        SetPrivate(page, "botsParent", botsParentGo.transform);
        return page;
    }

    private static void SetPrivate(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
              .SetValue(target, value);

    private static void SetStaticInstance(BotsPage value) =>
        typeof(BotsPage).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                        .SetValue(null, value);

    private static string ReadScript(string relativeToAssets)
    {
        string path = Path.Combine(Application.dataPath, relativeToAssets);
        Assert.IsTrue(File.Exists(path), $"script missing at {path}");
        return File.ReadAllText(path);
    }
}
