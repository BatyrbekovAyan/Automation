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
    private const string DemoChatId = "77000000011@c.us";   // Ерлан — the «Авто» payoff thread

    // Settle budgets. Bot instantiation waits a frame and then paints the card one frame
    // later (Bot.InitCardState), the chat list lays out asynchronously, and a tab switch
    // animates — so every capture is preceded by a real pause rather than a yield.
    private const float BootSeconds = 3.0f;
    private const float SettleSeconds = 1.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (PlayerPrefs.GetInt(RunFlagKey, 0) != 1) return;
        PlayerPrefs.DeleteKey(RunFlagKey);
        PlayerPrefs.Save();

        var host = new GameObject(nameof(StoreScreenshotDriver));
        DontDestroyOnLoad(host);
        host.AddComponent<StoreScreenshotDriver>();
    }

    private IEnumerator Start()
    {
        Directory.CreateDirectory(OutputDir);
        Debug.Log($"[StoreCapture] старт, кадры → {OutputDir}");

        yield return new WaitForSeconds(BootSeconds);

        // 1. Chat list FIRST — opening any chat zeroes its unread badge, and offline
        //    nothing ever restores it.
        yield return Capture("01-chats");

        // 2. The auto-reply thread.
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.SelectChat(DemoChatId);
            yield return Capture("02-thread-auto");
        }
        else
        {
            Debug.LogWarning("[StoreCapture] ChatManager.Instance == null — тред пропущен");
        }

        // 3. Every tab, by index. Named by index on purpose: the tab order is scene data,
        //    so the driver does not assert which index is Боты vs Сводка — identify the
        //    PNGs by eye and rename, rather than baking a wrong constant in here.
        var bar = BottomTabManager.Instance;
        if (bar != null)
        {
            foreach (int index in new[] { 0, 1, 2, 3 })
            {
                bar.SwitchTab(index);
                yield return Capture($"03-tab{index}");
            }
        }
        else
        {
            Debug.LogWarning("[StoreCapture] BottomTabManager.Instance == null — вкладки пропущены");
        }

        // 4. Paywall.
        var paywall = PaywallController.Instance;
        if (paywall != null)
        {
            paywall.Open(PaywallTrigger.Browse);
            yield return Capture("04-paywall");
        }
        else
        {
            Debug.LogWarning("[StoreCapture] PaywallController.Instance == null — пейволл пропущен");
        }

        Debug.Log("[StoreCapture] готово — выходим из Play Mode");
        UnityEditor.EditorApplication.isPlaying = false;
    }

    private IEnumerator Capture(string name)
    {
        yield return new WaitForSeconds(SettleSeconds);

        string path = Path.Combine(OutputDir, $"{name}.png");
        ScreenCapture.CaptureScreenshot(path);

        // CaptureScreenshot lands at end of frame and the file appears a frame or two
        // later; capturing again before it flushes silently drops the previous shot.
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return null;

        Debug.Log($"[StoreCapture] {name}.png  ({Screen.width}x{Screen.height})");
    }
}
#endif
