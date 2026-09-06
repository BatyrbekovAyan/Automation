using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Makes the Android system Back (button and edge gesture) navigate the app (2026-09-06, Google
/// Play track). Unity delivers KEYCODE_BACK to the Input System as <see cref="Key.Escape"/>, and
/// until now nothing read it: every screen could only be left through its own chevron or the
/// left-edge swipe — which Android 10+ gesture navigation reserves for the SYSTEM back gesture,
/// so the natural swipe produced exactly the key the app ignored.
///
/// ZERO scene wiring: bootstrapped from <c>RuntimeInitializeOnLoadMethod</c> like
/// <see cref="TextSelectionRouter"/>. The ordered list of surfaces lives in
/// <see cref="Targets"/> — top-most first, each pair being a surface's own «is it open» and the
/// SAME action its chevron/scrim performs (never a bare SetActive(false): the auth back deletes
/// pending profiles, Bot Settings reverts unsaved edits, item sheets discard an uncommitted
/// card). The walk itself is the pure <see cref="BackNavigation.Dispatch"/>.
///
/// Compiled into every target (the Editor's Escape key drives the same path for play-mode
/// checks); the root action — backgrounding the app — is Android-only. Predictive back stays
/// OFF in Player Settings so Unity keeps receiving the key.
/// </summary>
[DefaultExecutionOrder(-40)]
public class AndroidBackRouter : MonoBehaviour
{
    private static AndroidBackRouter _instance;
    public static AndroidBackRouter Existing => _instance;

    public static AndroidBackRouter Instance
    {
        get
        {
            if (_instance == null && Application.isPlaying)
            {
                var go = new GameObject("AndroidBackRouter", typeof(AndroidBackRouter));
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_ANDROID || UNITY_EDITOR
        var _ = Instance;
#endif
    }

    /// <summary>The surface that took the last press, for logs and tests.</summary>
    public string LastHandled { get; private set; }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        HandleBack();
    }

    /// <summary>One Back press. Public so a device pass can drive it from a debug hook.</summary>
    public void HandleBack()
    {
        LastHandled = BackNavigation.Dispatch(Targets());
        if (LastHandled != null)
        {
            Debug.Log($"[AndroidBackRouter] Back → {LastHandled}");
            return;
        }

        // Nothing open on a tab root: background the app (Android convention), never quit —
        // OnApplicationQuit is the PendingProfileLedger's best-effort settle point and must keep
        // its meaning.
        Debug.Log("[AndroidBackRouter] Back → background");
        MoveTaskToBack();
    }

    /// <summary>
    /// The surfaces Back can act on, top-most first. Resolved on every press (a press is rare),
    /// so instances that come and go — sheets built at runtime, screens toggled by tabs — are
    /// found as they are now.
    /// </summary>
    private static List<BackTarget> Targets()
    {
        var chat = ChatManager.Instance;
        var manager = Manager.Instance;
        var settings = BotSettings.Instance;

        return new List<BackTarget>
        {
            // ---- covers nothing may tunnel through ----
            new BackTarget("loading",
                () => manager != null && manager.LoadingPanel != null && manager.LoadingPanel.activeInHierarchy,
                null, swallow: true),
            new BackTarget("onboarding",
                () => FindFirstObjectByType<OnboardingScreen>() != null,   // active instances only: no bypass by design
                null, swallow: true),

            // ---- full-screen media ----
            new BackTarget("photo-viewer",
                () => PhotoViewer.Instance != null && PhotoViewer.Instance.panel != null && PhotoViewer.Instance.panel.activeSelf,
                () => PhotoViewer.Instance.Close()),
            new BackTarget("video",
                () => VideoController.Instance != null && VideoController.Instance.gameObject.activeSelf,
                () => VideoController.Instance.CloseVideo()),

            // ---- overlays above the screens ----
            new BackTarget("emoji-picker",
                () => EmojiPickerController.Instance != null && EmojiPickerController.Instance.IsShowing,
                () => EmojiPickerController.Instance.Hide()),
            new BackTarget("reaction-bar",
                () => ReactionBarController.Instance != null && ReactionBarController.Instance.IsShowing,
                () => ReactionBarController.Instance.Hide()),
            new BackTarget("bot-activation-confirm", () => BotActivationConfirm.IsShowing, BotActivationConfirm.Cancel),
            new BackTarget("billing-gate-sheet", () => BillingGateSheet.IsOpen, BillingGateSheet.Dismiss),
            new BackTarget("popup", () => PopupUI.HasOpenPopup, () => PopupUI.TryCloseTop()),
            new BackTarget("paywall",
                () => PaywallController.Instance != null && PaywallController.Instance.IsOpen,
                () => PaywallController.Instance.Close()),

            // ---- chat-screen sheets ----
            new BackTarget("attachment-preview",
                () => Find<AttachmentPreviewScreen>() is { IsOpen: true },
                () => Find<AttachmentPreviewScreen>()?.RequestBack()),
            new BackTarget("attach-sheet",
                () => Find<MessagesBottomPanel>()?.AttachSheet is { IsOpen: true },
                () => Find<MessagesBottomPanel>()?.AttachSheet?.Close()),

            // ---- bot-settings sheets, bot switcher ----
            new BackTarget("bot-settings-sheet",
                () => settings != null && settings.HasOpenSheet,
                () => settings.TryCloseTopSheet()),
            new BackTarget("bot-switcher",
                () => Find<BotSwitcherSheet>() is { IsOpen: true },
                () => Find<BotSwitcherSheet>()?.Close()),

            // ---- the open chat thread ----
            new BackTarget("chat-opening",
                () => chat != null && chat.MessageListPanel != null && chat.MessageListPanel.activeSelf
                      && (chat.Phase != ChatManager.ChatOpenPhase.Idle || SwipeToBack.IsSliding),
                null, swallow: true),   // mid-slide: the same refusal SwipeToBack gives a drag
            new BackTarget("chat-thread",
                () => chat != null && chat.MessageListPanel != null && chat.MessageListPanel.activeSelf,
                () => chat.ShowChatList()),

            // ---- full-screen flows ----
            new BackTarget("auth-page",
                () => manager != null && manager.IsAuthPageOpen,
                () => manager.TryBackFromAuth()),
            new BackTarget("bot-settings",
                () => settings != null && settings.gameObject.activeInHierarchy,
                () => settings.RequestBack()),
            new BackTarget("add-bot",
                () => AddBotPanel.Instance != null && AddBotPanel.Instance.IsOpen,
                () => { if (manager != null) manager.CloseAddBotForm(); else AddBotPanel.Instance.Close(); }),

            // ---- slide-in sub-pages ----
            new BackTarget("profile-subpage",
                () => Find<ProfileSubPages>() is { HasOpenPage: true },
                () => Find<ProfileSubPages>()?.TryCloseOpenPage()),
            new BackTarget("dashboard-list",
                () => Find<DashboardPage>() is { IsStatusListOpen: true },
                () => Find<DashboardPage>()?.RequestCloseStatusList()),
        };
    }

    private static T Find<T>() where T : Object => FindFirstObjectByType<T>(FindObjectsInactive.Include);

    private static void MoveTaskToBack()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call<bool>("moveTaskToBack", true);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AndroidBackRouter] moveTaskToBack failed: {e.Message}");
        }
#endif
    }
}
