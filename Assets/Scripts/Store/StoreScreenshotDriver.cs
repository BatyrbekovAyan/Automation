#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Drives Play Mode through the screens we need for App Store / Google Play listings
/// and writes a PNG per screen. Editor-only by compilation — this file cannot reach a
/// player build.
///
/// Navigation is scripted rather than clicked so a re-run produces byte-comparable
/// framing: the same tab order, the same chat, the same settle time. Seed the demo
/// data first with Tools/store/seed-demo-data.py, then run
/// <c>Tools/Store/Capture Screenshots</c>.
///
/// The GameObject is created by a RuntimeInitializeOnLoadMethod gated on a PlayerPrefs
/// flag, so Main.unity is never touched (the scene carries hand-tuning that must not be
/// disturbed — see the "Scene is source of truth" project rule).
/// </summary>
public class StoreScreenshotDriver : MonoBehaviour
{
    /// <summary>Set by the Editor menu item; cleared as soon as the driver starts.</summary>
    public const string RunFlagKey = "StoreCaptureRun";

    private const string OutputDir = "Tools/store/screenshots";
    private const string AutoChatId = "77000000011@c.us";   // Ерлан — the «Авто» payoff thread
    private const string SemiChatId = "77000000012@c.us";   // Айгерим — seeded into «Вместе»

    private const float BootSeconds = 3.0f;
    private const float SettleSeconds = 1.5f;
    // Opening a chat is ~600ms of chrome alone (300ms Prep + ~290ms slide) and PopulateBubbles
    // runs INSIDE the slide's onComplete, followed by the row-height layout chain. A 1.2s wait
    // photographed the wallpaper with no bubbles on it (2026-08-28).
    private const float ChatOpenSeconds = 4.0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (PlayerPrefs.GetInt(RunFlagKey, 0) != 1) return;
        PlayerPrefs.DeleteKey(RunFlagKey);
        PlayerPrefs.Save();

        // BEFORE scene load on purpose: SuggestionsController.Awake reads the factory, and the
        // «Вместе» panel has no offline path without this swap.
        SuggestionsController.ProviderFactory = () => new StoreDemoSuggestionsProvider();

        var host = new GameObject(nameof(StoreScreenshotDriver));
        DontDestroyOnLoad(host);
        host.AddComponent<StoreScreenshotDriver>();
    }

    private IEnumerator Start()
    {
        Directory.CreateDirectory(OutputDir);

        // Keep the player loop (and therefore rendering) running while Unity sits in the
        // background. Without this the run stalls the moment the Editor loses focus: the
        // capture waits on a frame that is never drawn, and the whole thing silently hangs
        // until someone clicks the window — which is exactly what made earlier runs look
        // like they had frozen (2026-08-28).
        Application.runInBackground = true;

        Debug.Log($"[StoreCapture] старт, кадры → {OutputDir}  ({Screen.width}x{Screen.height}), " +
                  $"timeScale={Time.timeScale}");

        // Realtime, never scaled: a scaled wait never returns while the app sits at
        // timeScale 0, and this codebase already carries that lesson in two other places
        // (ChatManager.LivePoll, SuggestionsController). A stalled capture looks identical
        // to a hung Editor.
        yield return new WaitForSecondsRealtime(BootSeconds);
        Debug.Log("[StoreCapture] загрузка дождалась, проверяю менеджеров");

        // Fail loudly rather than writing a blank PNG that looks like a successful run:
        // in an empty scene every manager is null and the capture is just the camera's
        // background colour.
        if (Manager.Instance == null || BottomTabManager.Instance == null)
        {
            Debug.LogError("[StoreCapture] сцена без менеджеров — открыта не Main.unity. " +
                           "Кадры не сняты, выхожу.");
            UnityEditor.EditorApplication.isPlaying = false;
            yield break;
        }

        // 1. Chat list FIRST — opening any chat zeroes its unread badge, and offline
        //    nothing ever restores it.
        yield return Capture("01-chats");

        // 2. The auto-reply thread — the «Авто» payoff.
        ChatManager.Instance.SelectChat(AutoChatId);
        yield return new WaitForSecondsRealtime(ChatOpenSeconds);
        yield return Capture("02-thread-auto");

        // 3. The «Вместе» panel over its own chat. This chat is seeded per-chat semi-auto, so
        //    the panel is the slot's tenant on open and the cards come from the demo provider.
        ChatManager.Instance.SelectChat(SemiChatId);
        yield return new WaitForSecondsRealtime(ChatOpenSeconds);
        yield return Capture("03-suggestions");

        // 4. Every tab, by index. Named by index on purpose: the tab order is scene data, so the
        //    driver does not assert which index is Боты vs Сводка — identify the PNGs by eye.
        //    Each switch gets its own settle: a tab captured mid-transition photographs a
        //    half-drawn nav bar (measured 2026-08-28).
        // Leave the open chat before touching the tabs. The messages panel stays active over
        // the chat list otherwise, and its black composer strip covers part of the nav bar —
        // which is what put a black block in the corner of every tab shot on 2026-08-28.
        if (SwipeToBack.Instance != null)
        {
            SwipeToBack.Instance.SlideOutToChatList(instant: true);
            yield return new WaitForSecondsRealtime(1.5f);
        }

        foreach (int index in new[] { 1, 2, 3, 0 })
        {
            BottomTabManager.Instance.SwitchTab(index);
            yield return new WaitForSecondsRealtime(2.0f);

            // Сводка on «7 дней»: the seeded outcomes span three days, so the default
            // «Сегодня» window shows a nearly empty board (1/0/1/1/0) and undersells the
            // screen. No singleton here — an Editor-only find is fine.
            if (index == 1)
            {
                var dash = FindFirstObjectByType<DashboardPage>();
                if (dash != null)
                {
                    dash.SetPeriod(DashboardPeriod.Week);
                    yield return new WaitForSecondsRealtime(1.0f);
                }
                else Debug.LogWarning("[StoreCapture] DashboardPage не найден — период не сменён");
            }

            yield return Capture($"04-tab{index}");
        }

        // 5. Paywall BEFORE the settings screen: bot settings is a full-screen panel and the
        //    paywall never appeared once it was open (2026-08-28 run ended one shot short).
        var paywall = PaywallController.Instance;
        if (paywall != null)
        {
            paywall.Open(PaywallTrigger.Browse);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("05-paywall");
            paywall.Close();
            yield return new WaitForSecondsRealtime(1.5f);
        }
        else
        {
            Debug.LogWarning("[StoreCapture] PaywallController.Instance == null — пейволл пропущен");
        }

        // 6. Bot settings: the price-list screen and the prompt screen. Opened through the
        //    card's own entry point so the paired settings clone is the one the app wired.
        var bot = BotsPage.Instance != null
            ? System.Array.Find(BotsPage.Instance.GetComponentsInChildren<Bot>(true),
                                b => b.name == "Bot0")
            : null;
        if (bot != null)
        {
            BottomTabManager.Instance.SwitchTab(BottomTabManager.BotsTabIndex);
            yield return new WaitForSecondsRealtime(1.0f);

            bot.OpenSettingsAtProductTab();
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Capture("06-settings-products");

            if (Manager.openBotSettings != null)
            {
                Manager.openBotSettings.OpenPromptTab();
                yield return new WaitForSecondsRealtime(2.0f);
                yield return Capture("07-settings-prompt");
            }
            else Debug.LogWarning("[StoreCapture] Manager.openBotSettings == null — вкладка промпта пропущена");
        }
        else
        {
            Debug.LogWarning("[StoreCapture] карточка Bot0 не найдена — настройки пропущены");
        }


        Debug.Log("[StoreCapture] готово — выходим из Play Mode");
        UnityEditor.EditorApplication.isPlaying = false;
    }

    private IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(SettleSeconds);

        string path = Path.Combine(OutputDir, $"{name}.png");
        ScreenCapture.CaptureScreenshot(path);

        // CaptureScreenshot lands at end of frame and the file appears a frame or two later;
        // capturing again before it flushes silently drops the previous shot. Plain frame
        // yields rather than WaitForEndOfFrame — the latter never resumes in an unfocused
        // Editor, which hung the run for 15 minutes with no error (2026-08-28).
        for (int frame = 0; frame < 4; frame++) yield return null;

        Debug.Log($"[StoreCapture] {name}.png  ({Screen.width}x{Screen.height})");
    }
}
#endif
