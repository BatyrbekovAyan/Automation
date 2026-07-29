using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pins the SyncingView cover's show/hide lifecycle across a disable/enable cycle —
/// the stuck-cover bug: the cover's ONLY hide signal (OnWhatsAppSyncReady) is a
/// one-shot event the view unsubscribes from in OnDisable (without hiding), so a
/// sync window that expires while the chats screen is inactive strands the cover
/// visible with a dead spinner until app restart. OnEnable must therefore HIDE
/// whenever the active bot+channel is not inside a sync window — never no-op —
/// and must re-arm the ready producer when it re-shows mid-window (windows can be
/// stamped outside BeginLoadForActiveBot, e.g. the settings late-auth stamp).
/// Reflection-driven EditMode lifecycle tests, mirroring EditableFieldTextHealTests.
/// </summary>
public class SyncingViewLifecycleTests
{
    private const string DefaultBotSyncKey = "_defaultWhatsappSyncUntil";

    private GameObject viewGo;
    private GameObject managerGo;

    [TearDown]
    public void TearDown()
    {
        if (viewGo != null) UnityEngine.Object.DestroyImmediate(viewGo);
        if (managerGo != null) UnityEngine.Object.DestroyImmediate(managerGo);
        ChatManager.Instance = null;
        PlayerPrefs.DeleteKey(DefaultBotSyncKey);
    }

    [Test]
    public void Enable_NoChatManager_HidesStrandedCover()
    {
        // App-relaunch half of the invariant: with no sync gate reachable at all,
        // a cover left visible from a previous enable must not survive re-enable.
        var (view, canvasGroup) = BuildShownCover();

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable"); // ChatManager.Instance is null here

        Assert.AreEqual(0f, canvasGroup.alpha, "cover must hide on enable when no sync gate exists");
        Assert.IsFalse(canvasGroup.blocksRaycasts, "a stranded cover must not keep swallowing taps");
    }

    [Test]
    public void Enable_WindowExpiredWhileDisabled_HidesStrandedCover()
    {
        // The reported bug: cover shown mid-window, screen deactivated (tab switch),
        // window expires while unsubscribed (OnWhatsAppSyncReady fires into nobody),
        // screen reactivates. IsChannelSyncing is now false — the cover must hide.
        var (view, canvasGroup) = BuildShownCover();
        AttachChatManagerWithoutWindow();

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(0f, canvasGroup.alpha, "cover must hide on enable once the sync window has closed");
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    [Test]
    public void Enable_MidWindow_ShowsCoverAndArmsReadyProducer()
    {
        // Re-enable mid-window must both re-show the cover AND guarantee a running
        // wait routine — the sole producer of OnWhatsAppSyncReady. A window stamped
        // outside BeginLoadForActiveBot (settings late-auth) otherwise never ends.
        var (view, canvasGroup) = BuildShownCover();
        ChatManager manager = AttachChatManagerWithoutWindow();
        long untilMs = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeMilliseconds();
        PlayerPrefs.SetString(DefaultBotSyncKey, untilMs.ToString());

        InvokePrivate(view, "OnDisable");
        InvokePrivate(view, "OnEnable");

        Assert.AreEqual(1f, canvasGroup.alpha, "cover must re-show when the window is still open");
        Assert.IsNotNull(GetPrivateField(manager, "_syncWaitRoutine"),
            "re-showing the cover must arm the OnWhatsAppSyncReady producer");
    }

    [Test]
    public void HandleReady_HidesCover()
    {
        // Regression pin for the normal path: the ready signal hides the cover.
        var (view, canvasGroup) = BuildShownCover();

        InvokePrivate(view, "HandleReady");

        Assert.AreEqual(0f, canvasGroup.alpha);
        Assert.IsFalse(canvasGroup.blocksRaycasts);
    }

    /// <summary>
    /// SyncingView on a bare active GameObject with all [SerializeField] refs left
    /// null (every consumer is null-guarded), driven to the shown state: Awake
    /// (caches CanvasGroup, hides) then HandleSyncing with a mid-window timestamp.
    /// </summary>
    private (SyncingView view, CanvasGroup canvasGroup) BuildShownCover()
    {
        viewGo = new GameObject("SyncingState", typeof(RectTransform), typeof(CanvasGroup));
        var view = viewGo.AddComponent<SyncingView>();
        var canvasGroup = viewGo.GetComponent<CanvasGroup>();

        InvokePrivate(view, "Awake");
        Assert.AreEqual(0f, canvasGroup.alpha, "sanity: Awake hides");

        long untilMs = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeMilliseconds();
        InvokePrivate(view, "HandleSyncing", new object[] { untilMs }, new[] { typeof(long) });
        Assert.AreEqual(1f, canvasGroup.alpha, "sanity: HandleSyncing shows");

        return (view, canvasGroup);
    }

    /// <summary>
    /// A real ChatManager instance published as the singleton WITHOUT running its
    /// Awake/Start (no scene wiring needed): CurrentBotId stays the "_default"
    /// sentinel and ActiveChannel stays WhatsApp, so the sync gate reads
    /// _defaultWhatsappSyncUntil — deleted in TearDown.
    /// </summary>
    private ChatManager AttachChatManagerWithoutWindow()
    {
        PlayerPrefs.DeleteKey(DefaultBotSyncKey);
        managerGo = new GameObject("ChatManagerHost");
        var manager = managerGo.AddComponent<ChatManager>();
        ChatManager.Instance = manager;
        return manager;
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

    private static object GetPrivateField(object target, string name)
    {
        var type = target.GetType();
        FieldInfo info = null;
        while (type != null && info == null)
        {
            info = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }
        Assert.IsNotNull(info, $"field {name} not found");
        return info.GetValue(target);
    }
}
