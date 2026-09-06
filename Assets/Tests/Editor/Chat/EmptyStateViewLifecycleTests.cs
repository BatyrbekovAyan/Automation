using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pins the empty-state card's show/hide lifecycle across a disable/enable cycle — the stale-card
/// bug (device, fresh install, 2026-09-04): create the first bot from onboarding, come back to
/// «Чаты», and the NoBotsExist card («Создайте первого бота») was still on screen over the chat
/// list, opaque and swallowing taps, with its CTA listeners already stripped by OnDisable.
///
/// Mechanism: the card's visibility is a CanvasGroup alpha that SURVIVES the GameObject being
/// deactivated, while every event that would correct it (bot created, channel authorised late in
/// settings, chats loaded) fires while the chats tab is inactive and the view is unsubscribed. The
/// OnEnable catch-up was the only place those are ever seen, and it hid the card only when the
/// chat list was already non-empty — so a resolver verdict of "no empty card" over an EMPTY list
/// (the whole 300s post-creation sync window, or forever on an account with no chats) did nothing.
///
/// The invariant, now shared with SyncingView (see SyncingViewLifecycleTests): OnEnable must act
/// on the AUTHORITATIVE resolver in both directions — reason ⇒ show that card, null ⇒ hide
/// whatever is showing — and never no-op. Reflection-driven EditMode lifecycle tests.
/// </summary>
public class EmptyStateViewLifecycleTests
{
    private const string DefaultBotId = "_default";
    // Derived from the production suffix, so a rename cannot silently turn the mid-window test
    // into the Ready case (it would then pass for the wrong reason).
    private static readonly string DefaultBotSyncKey = DefaultBotId + ChatManager.SyncUntilSuffixFor(ChatChannel.WhatsApp);

    private GameObject viewGo;
    private GameObject managerGo;
    private GameObject botsParentGo;
    private GameObject chatManagerGo;

    [TearDown]
    public void TearDown()
    {
        if (viewGo != null) UnityEngine.Object.DestroyImmediate(viewGo);
        if (botsParentGo != null) UnityEngine.Object.DestroyImmediate(botsParentGo);
        if (managerGo != null) UnityEngine.Object.DestroyImmediate(managerGo);
        if (chatManagerGo != null) UnityEngine.Object.DestroyImmediate(chatManagerGo);
        ChatManager.Instance = null;
        Manager.Instance = null;
        PlayerPrefs.DeleteKey(DefaultBotSyncKey);
    }

    // ---------------------------------------------------------------- the reported bug

    [Test]
    public void Enable_BotCreatedWhileDisabled_HidesStaleNoBotsCard()
    {
        // Fresh install: the card is raised with zero bots, the tab switches away to the add-bot
        // flow (OnDisable), a connected bot is created, and the user comes back. The list is still
        // empty — nothing has synced — so the ONLY thing that can take the card down is the
        // resolver's verdict.
        var (view, canvasGroup) = BuildShownCard();
        AttachChatManager();
        AttachManagerWithConnectedBot();

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(0f, canvasGroup.alpha, "a connected bot must take the «no bots» card down");
        Assert.IsFalse(canvasGroup.blocksRaycasts, "a stale card must not keep swallowing taps over the chat list");
    }

    [Test]
    public void Enable_MidPostCreationSyncWindow_HidesStaleCard()
    {
        // The first 300 seconds after creation: the resolver answers Syncing (the syncing cover
        // owns the screen) and the chat list is provably empty — BeginLoadForActiveBot returns
        // before loading. The card must still come down; it used to survive underneath the cover
        // and surface the moment the window closed.
        var (view, canvasGroup) = BuildShownCard();
        AttachChatManager();
        AttachManagerWithConnectedBot();
        PlayerPrefs.SetString(DefaultBotSyncKey,
            DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeMilliseconds().ToString());
        Assert.IsTrue(ChatManager.Instance.IsChannelSyncing(DefaultBotId, ChatChannel.WhatsApp, out _),
            "sanity: the harness must actually be inside the sync window, or this is the Ready case in disguise");

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(0f, canvasGroup.alpha, "the card must hide inside the post-creation sync window too");
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    [Test]
    public void Enable_NoChatManager_HidesStrandedCard()
    {
        // Mirrors SyncingViewLifecycleTests.Enable_NoChatManager_HidesStrandedCover: with no
        // resolver reachable at all there is nothing to authorise keeping a card up, and the
        // hide must therefore sit OUTSIDE the manager guard.
        var (view, canvasGroup) = BuildShownCard();

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable"); // ChatManager.Instance is null here

        Assert.AreEqual(0f, canvasGroup.alpha, "a stranded card must not survive re-enable");
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    // ---------------------------------------------------------------- the card must still appear

    [Test]
    public void Enable_StillNoBots_ReshowsCard()
    {
        // The other direction: nothing changed while the tab was away, so the card comes back.
        // OnDisable strips the CTA listeners, so this re-show MUST go through ConfigureForReason
        // (it is what re-wires the button) — hence the reason is asserted, not just the alpha.
        var (view, canvasGroup) = BuildShownCard();
        AttachChatManager(); // no Manager ⇒ zero bots ⇒ NoBotsExist

        InvokePrivate(view, "OnDisable");
        Assert.AreEqual(0f, canvasGroup.alpha, "sanity: leaving the tab takes the card off screen");

        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(1f, canvasGroup.alpha, "with still no bots the card must come back");
        Assert.IsTrue(canvasGroup.blocksRaycasts);
        Assert.AreEqual(EmptyStateReason.NoBotsExist, GetPrivateField(view, "_lastReason"),
            "the re-show must re-run ConfigureForReason — that is what re-wires the CTA");
    }

    [Test]
    public void Disable_TakesCardOffScreen()
    {
        // The stale state is killed at the source: OnDisable strips the button listeners, so
        // anything left visible past it is a dead card.
        var (view, canvasGroup) = BuildShownCard();

        InvokePrivate(view, "OnDisable");

        Assert.AreEqual(0f, canvasGroup.alpha);
        Assert.IsFalse(canvasGroup.blocksRaycasts);
        Assert.IsNull(GetPrivateField(view, "_lastReason"), "OnDisable must still forget the reason");
    }

    // ---------------------------------------------------------------- channel switch

    [Test]
    public void ChannelChanged_ResolverSaysNoCard_HidesCard()
    {
        // WR-02's re-derive, now reachable even when the card is on screen WITHOUT a remembered
        // reason — the residue the pre-fix OnDisable left behind (listeners stripped, _lastReason
        // nulled, alpha still 1). Post-fix OnDisable no longer produces that state, so it is
        // reproduced by reflection: the pre-fix handler early-returned on the null reason and
        // left the card up, which is exactly why only a WhatsApp→Telegram→WhatsApp round trip
        // repaired the bug. (Against the pre-fix view this test fails with alpha 1.)
        var (view, canvasGroup) = BuildShownCard();
        AttachChatManager();
        AttachManagerWithConnectedBot();
        SetPrivateField(view, "_lastReason", null);
        Assert.AreEqual(1f, canvasGroup.alpha, "sanity: the card is up with no remembered reason");

        InvokePrivate(view, "HandleActiveChannelChanged",
            new object[] { ChatChannel.WhatsApp }, new[] { typeof(ChatChannel) });

        Assert.AreEqual(0f, canvasGroup.alpha);
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    // ---------------------------------------------------------------- the two halves of the fix, isolated

    [Test]
    public void Enable_AlphaThatSurvivedTheDisable_IsHiddenByTheResolverAlone()
    {
        // OnDisable's Hide() is the primary repair; this pins the OTHER half — the OnEnable
        // re-derive — by putting the card back up after the disable, the way a CanvasGroup alpha
        // that outlived its GameObject did on device. A build with OnDisable.Hide() but the
        // pre-fix OnEnable body (hide only when Chats.Count > 0) leaves alpha at 1 here.
        var (view, canvasGroup) = BuildShownCard();
        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "Show");
        Assert.AreEqual(1f, canvasGroup.alpha, "sanity: the surviving alpha is reproduced");
        Assert.IsNull(GetPrivateField(view, "_lastReason"), "sanity: and no reason is remembered");

        AttachChatManager();
        AttachManagerWithConnectedBot();

        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(0f, canvasGroup.alpha, "the resolver said «no empty card» over an empty list — the card must come down");
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    [Test]
    public void Reassert_WithNoRememberedReason_StillRaisesTheCardTheResolverWants()
    {
        // The frame-1 reassert used to bail on a null _lastReason — the cold-start case where
        // OnEnable found no reason (card hidden) and the settled bot/channel then resolves to a
        // card: it stayed blank with no CTA. The re-derive is unconditional now; against the
        // pre-fix coroutine the guard returns before the resolver is asked and alpha stays 0.
        var (view, canvasGroup) = BuildShownCard();
        InvokePrivate(view, "Hide");
        SetPrivateField(view, "_lastReason", null);
        Assert.AreEqual(0f, canvasGroup.alpha, "sanity: hidden with nothing remembered");
        AttachChatManager(); // no Manager ⇒ zero bots ⇒ NoBotsExist

        var routine = (System.Collections.IEnumerator)view.GetType()
            .GetMethod("ReassertAfterChannelRestore", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(view, null);
        routine.MoveNext();   // yield return null — the frame the channel/bot settles on
        routine.MoveNext();   // the re-derive

        Assert.AreEqual(1f, canvasGroup.alpha, "the reassert must raise the card the resolver now wants");
        Assert.AreEqual(EmptyStateReason.NoBotsExist, GetPrivateField(view, "_lastReason"));
    }

    // ---------------------------------------------------------------- harness

    /// <summary>
    /// EmptyStateView on a bare ACTIVE GameObject with every [SerializeField] left null (all of
    /// them are null-guarded), driven to the shown state: Awake (caches the CanvasGroup, hides)
    /// then HandleEmptyState(NoBotsExist). Active because OnEnable starts a coroutine.
    /// </summary>
    private (EmptyStateView view, CanvasGroup canvasGroup) BuildShownCard()
    {
        viewGo = new GameObject("EmptyState", typeof(RectTransform), typeof(CanvasGroup));
        var view = viewGo.AddComponent<EmptyStateView>();
        var canvasGroup = viewGo.GetComponent<CanvasGroup>();

        InvokePrivate(view, "Awake");
        Assert.AreEqual(0f, canvasGroup.alpha, "sanity: Awake hides");

        InvokePrivate(view, "HandleEmptyState",
            new object[] { EmptyStateReason.NoBotsExist }, new[] { typeof(EmptyStateReason) });
        Assert.AreEqual(1f, canvasGroup.alpha, "sanity: the card is up before the lifecycle under test");

        return (view, canvasGroup);
    }

    /// <summary>
    /// A real ChatManager published as the singleton WITHOUT running its Awake/Start: CurrentBotId
    /// stays the "_default" sentinel and ActiveChannel stays WhatsApp, so the resolver reads the
    /// bot named "_default" and the _defaultWhatsappSyncUntil key (deleted in TearDown).
    /// </summary>
    private ChatManager AttachChatManager()
    {
        PlayerPrefs.DeleteKey(DefaultBotSyncKey);
        chatManagerGo = new GameObject("ChatManagerHost");
        var manager = chatManagerGo.AddComponent<ChatManager>();
        ChatManager.Instance = manager;
        return manager;
    }

    /// <summary>
    /// A Manager singleton whose BotsParent holds one bot named "_default" with a valid WhatsApp
    /// profile — so ComputeCurrentEmptyState sees botCount 1 + a connected active channel and
    /// answers null ("not an empty-card state").
    /// </summary>
    private void AttachManagerWithConnectedBot()
    {
        managerGo = new GameObject("ManagerHost");
        var manager = managerGo.AddComponent<Manager>();

        botsParentGo = new GameObject("BotsParent");
        var botGo = new GameObject(DefaultBotId);
        botGo.transform.SetParent(botsParentGo.transform);
        botGo.AddComponent<Bot>().whatsappProfileId = "wa-test-profile";

        SetPrivateField(manager, "BotsParent", botsParentGo);
        Manager.Instance = manager;
    }

    private static void InvokePrivate(object target, string name, object[] args = null, Type[] argTypes = null)
    {
        var type = target.GetType();
        MethodInfo info = null;
        while (type != null && info == null)
        {
            info = argTypes == null
                ? type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                : type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance, null, argTypes, null);
            type = type.BaseType;
        }
        Assert.IsNotNull(info, $"method {name} not found");
        info.Invoke(target, args);
    }

    private static FieldInfo FindField(object target, string name)
    {
        var type = target.GetType();
        FieldInfo info = null;
        while (type != null && info == null)
        {
            info = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }
        Assert.IsNotNull(info, $"field {name} not found");
        return info;
    }

    private static object GetPrivateField(object target, string name) => FindField(target, name).GetValue(target);

    private static void SetPrivateField(object target, string name, object value)
        => FindField(target, name).SetValue(target, value);
}
