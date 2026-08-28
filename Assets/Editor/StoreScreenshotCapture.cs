using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu entry that arms <see cref="StoreScreenshotDriver"/> and enters Play Mode.
///
/// Why the Editor and not the iOS Simulator: the Editor renders the SAME UI with the
/// same canvas, fonts and shaders, so a Game-view capture is a genuine screenshot of
/// the app — no Xcode, no iOS runtime, no IL2CPP build in the loop. Set the Game view
/// to the store's required pixel size before running (see the dialog text).
///
/// Seed the demo data first: python3 Tools/store/seed-demo-data.py
/// </summary>
public static class StoreScreenshotCapture
{
    [MenuItem("Tools/Store/Capture Screenshots")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Скриншоты",
                "Play Mode уже запущен. Останови его и запусти пункт меню заново.", "Ок");
            return;
        }

        bool go = EditorUtility.DisplayDialog(
            "Скриншоты для сторов",
            "Перед съёмкой:\n\n" +
            "1. Демо-данные засеяны (python3 Tools/store/seed-demo-data.py).\n" +
            "2. В Game view выбран нужный размер в ПИКСЕЛЯХ, например 1290×2796 " +
            "(iPhone 6.7\") или 1242×2688 (6.5\").\n" +
            "3. Game view виден на экране — свёрнутая вкладка не отрисовывается.\n\n" +
            "Play Mode запустится, кадры лягут в Tools/store/screenshots/ " +
            "и Play Mode остановится сам.",
            "Снимать", "Отмена");
        if (!go) return;

        PlayerPrefs.SetInt(StoreScreenshotDriver.RunFlagKey, 1);
        PlayerPrefs.Save();
        EditorApplication.isPlaying = true;
    }
}
