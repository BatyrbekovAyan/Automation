using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// iOS SIMULATOR only: keeps a demo/screenshot build presentable. Two things the simulator does
/// that no shipping device does (seen 2026-09-03, Intel Mac, iOS 26.5 simulator):
///   1. Its Metal layer refuses the project's 8× MSAA — URP logs «Attachment 0 was created with
///      4 samples but 8 samples were requested» every frame. Clamped to 4× here, at runtime, so
///      the project asset stays untouched for real devices.
///   2. Unity ships the simulator player ONLY as the development variant (there is no
///      Release_sim* variation under PlaybackEngines/iOSSupport), and a development player pops
///      the on-screen Development Console on any Debug.LogError — straight into a recording.
/// Compiled ONLY into the demo simulator build (DEMO_SIMULATOR scripting define, stamped by
/// DemoSimulatorBuild) — device/store builds do not contain this code at all.
/// </summary>
public static class SimulatorRenderingGuard
{
#if UNITY_IOS && !UNITY_EDITOR && DEMO_SIMULATOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        // DEMO_SIMULATOR is set only by DemoSimulatorBuild (extraScriptingDefines), so this code does not
        // even exist in device/store builds. Unity reports the SIMULATED model in deviceModel (e.g.
        // «iPhone18,2»), so a runtime model check is not a usable simulator test — the define is.
        string model = SystemInfo.deviceModel;

        Debug.developerConsoleEnabled = false;
        Debug.developerConsoleVisible = false;

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.msaaSampleCount > 4)
            urp.msaaSampleCount = 4;
        if (QualitySettings.antiAliasing > 4)
            QualitySettings.antiAliasing = 4;

        Debug.Log($"[SimulatorRenderingGuard] {model} / {SystemInfo.graphicsDeviceName}: MSAA clamped to 4, developer console off");
    }
#endif
}
