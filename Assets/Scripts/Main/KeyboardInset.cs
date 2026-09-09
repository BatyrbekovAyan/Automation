using UnityEngine;

/// <summary>
/// Reads how much of the screen the on-screen keyboard currently occludes,
/// in DEVICE PIXELS (0 when it is down).
///
/// Android deliberately uses the JNI visible-frame measurement rather than
/// TouchScreenKeyboard.area: the bot-settings screen already rejected the
/// latter (see ItemEditSheet.EstimateKeyboardHeightPixels), and with no
/// windowSoftInputMode set in the manifest the Unity surface does not resize,
/// so the decor view's visible-frame delta is the only reliable signal.
/// Android is the primary build target, so it gets the proven path.
///
/// The Editor returns 0 on purpose — a simulated keyboard would silently
/// change what every Editor play-through and screenshot of the settings
/// screen looks like. Editor coverage lives in KeyboardLiftMath's unit tests;
/// the real gate is a device pass.
///
/// NOTE: ItemEditSheet and KeyboardScrollFix carry their own copies of the same
/// JNI reader. None of the three had ever worked on a device until 2026-09-09:
/// Rect.top/bottom were read as methods and the bare catch returned 0 (see
/// MeasureAndroid). Keep the three in step; AndroidRectJniGuardTests pins them.
/// Verified on a Galaxy J5 Prime (Android 8): occluded=582 of 1280 px with the
/// Samsung keyboard up, and TouchScreenKeyboard.area was a zero rect the whole time.
/// </summary>
public static class KeyboardInset
{
    /// <summary>Occluded height in device pixels; 0 when the keyboard is down.</summary>
    public static float OccludedScreenPixels()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Returns 0 whenever the keyboard is down, so no fallback is needed —
        // a 0.4 * Screen.height fallback would lift the field on dismiss.
        return MeasureAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
        if (!TouchScreenKeyboard.visible) return 0f;
        var area = TouchScreenKeyboard.area.height;
        return area > 0f ? area : Screen.height * 0.4f;
#else
        return 0f;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static float MeasureAndroid()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var window = activity.Call<AndroidJavaObject>("getWindow");
            using var decorView = window.Call<AndroidJavaObject>("getDecorView");
            using var rootView = decorView.Call<AndroidJavaObject>("getRootView");

            using var visibleRect = new AndroidJavaObject("android.graphics.Rect");
            decorView.Call("getWindowVisibleDisplayFrame", visibleRect);

            // Rect.top/bottom are FIELDS, so this is Get<>, never Call<> — Call<int>("bottom")
            // throws NoSuchMethodError, the catch below swallowed it, and every Android keyboard
            // reader in the project returned 0 on every device until the 2026-09-09 pass
            // (composer under the IME, sheets and forms not lifting at all). AndroidRectJniGuardTests
            // pins the accessor.
            int visibleBottom = visibleRect.Get<int>("bottom");
            int rootHeight = rootView.Call<int>("getHeight");
            int height = rootHeight - visibleBottom;

            // Noise floor: small deltas are status-bar/gesture-inset jitter.
            float result = height > 100 ? height : 0f;
#if CR_DIAGNOSTICS
            LogIfChanged(result, rootHeight, visibleRect, decorView);
#endif
            return result;
        }
        catch
#if CR_DIAGNOSTICS
        (System.Exception e)
        {
            if (!_exceptionLogged)
            {
                _exceptionLogged = true;
                Debug.Log("[KeyboardInset] EXCEPTION in MeasureAndroid: " + e);
            }
            return 0f;
        }
#else
        {
            return 0f;
        }
#endif
    }

#if CR_DIAGNOSTICS
    // ── Diagnostics build only (2026-09-09 Android keyboard pass; DevAndroidBuild sets CR_DIAGNOSTICS) ──
    // Logs once per CHANGE of the measured value, beside the numbers the fix
    // decision needs: the visible-frame delta this reader trusts, the IME and
    // navigation-bar insets the platform reports through WindowInsets (API 30+),
    // and Unity's own TouchScreenKeyboard view. Compiled out of every store build.
    private static float _lastLogged = -1f;
    private static bool _exceptionLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AnnounceDiagnostics()
    {
        string activity = "?";
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var current = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            activity = current == null ? "NULL" : current.Call<string>("toString");
        }
        catch (System.Exception e) { activity = "err:" + e.GetType().Name + " " + e.Message; }
        Debug.Log($"[KeyboardInset] diagnostics build alive; UnityPlayer.currentActivity={activity} " +
                  $"screen={Screen.width}x{Screen.height} dpi={Screen.dpi} safeArea={Screen.safeArea}");
    }

    private static void LogIfChanged(float result, int rootHeight, AndroidJavaObject visibleRect,
                                     AndroidJavaObject decorView)
    {
        if (Mathf.Approximately(result, _lastLogged)) return;
        _lastLogged = result;

        int visibleTop = visibleRect.Get<int>("top");
        int visibleBottom = visibleRect.Get<int>("bottom");
        string ime = "n/a", nav = "n/a";
        try
        {
            using var insets = decorView.Call<AndroidJavaObject>("getRootWindowInsets");   // API 23+
            if (insets != null)
            {
                using var typeClass = new AndroidJavaClass("android.view.WindowInsets$Type"); // API 30+
                int imeType = typeClass.CallStatic<int>("ime");
                int navType = typeClass.CallStatic<int>("navigationBars");
                using var imeInsets = insets.Call<AndroidJavaObject>("getInsets", imeType);
                using var navInsets = insets.Call<AndroidJavaObject>("getInsets", navType);
                bool imeVisible = insets.Call<bool>("isVisible", imeType);
                ime = imeInsets.Get<int>("bottom") + (imeVisible ? " visible" : " hidden");
                nav = navInsets.Get<int>("bottom").ToString();
            }
        }
        catch (System.Exception e)
        {
            ime = "err:" + e.GetType().Name;
        }

        Debug.Log($"[KeyboardInset] occluded={result} root={rootHeight} visible=[{visibleTop}..{visibleBottom}] " +
                  $"imeInsetBottom={ime} navInsetBottom={nav} screen={Screen.width}x{Screen.height} " +
                  $"safeArea={Screen.safeArea} tskVisible={TouchScreenKeyboard.visible} tskArea={TouchScreenKeyboard.area}");
    }
#endif // CR_DIAGNOSTICS
#endif
}
