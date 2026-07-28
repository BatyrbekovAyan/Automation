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
/// NOTE: KeyboardAwarePanel (chat), FocusedFieldKeyboardLift (sheets) and
/// ItemEditSheet each carry their own equivalent reader, tuned and verified on
/// device. They are deliberately left alone — consolidating them would change
/// behaviour on screens that cannot be re-verified right now.
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

            int visibleBottom = visibleRect.Call<int>("bottom");
            int rootHeight = rootView.Call<int>("getHeight");
            int height = rootHeight - visibleBottom;

            // Noise floor: small deltas are status-bar/gesture-inset jitter.
            return height > 100 ? height : 0f;
        }
        catch
        {
            return 0f;
        }
    }
#endif
}
