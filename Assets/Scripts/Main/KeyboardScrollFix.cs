using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// When a TMP_InputField gains focus on mobile, adds temporary bottom padding
/// to the ScrollRect content and scrolls the input above the virtual keyboard.
/// Restores padding when focus is lost.
///
/// Does NOT rely on TouchScreenKeyboard.visible (unreliable with TMP_InputField).
/// Instead, monitors TMP_InputField.isFocused directly.
///
/// Attach to any GameObject that contains a ScrollRect (e.g., auth page root).
/// </summary>
public class KeyboardScrollFix : MonoBehaviour
{
    private ScrollRect scrollRect;
    private RectTransform contentRect;
    private VerticalLayoutGroup contentVlg;
    private int originalBottomPadding;
    private bool paddingApplied;
    private TMP_InputField lastFocusedInput;
    private Coroutine monitorCoroutine;

    private void OnEnable()
    {
        scrollRect = GetComponentInChildren<ScrollRect>();
        if (scrollRect == null) return;

        contentRect = scrollRect.content;
        contentVlg = contentRect != null ? contentRect.GetComponent<VerticalLayoutGroup>() : null;

        if (contentVlg != null)
            originalBottomPadding = contentVlg.padding.bottom;

        monitorCoroutine = StartCoroutine(MonitorFocus());
    }

    private void OnDisable()
    {
        if (monitorCoroutine != null)
        {
            StopCoroutine(monitorCoroutine);
            monitorCoroutine = null;
        }
        RestorePadding();
        lastFocusedInput = null;
    }

    private IEnumerator MonitorFocus()
    {
        while (true)
        {
            var focused = FindFocusedInput();

            if (focused != null && focused != lastFocusedInput)
            {
                // New input gained focus — wait for keyboard animation to finish
                lastFocusedInput = focused;
                yield return new WaitForSeconds(0.35f);

                float keyboardHeight = EstimateKeyboardHeight();
                if (keyboardHeight <= 0f) keyboardHeight = Screen.height * 0.4f;

                ApplyPadding(keyboardHeight);

                // Wait one frame for layout to rebuild, then scroll
                yield return null;
                ScrollToShow(focused, keyboardHeight);
            }
            else if (focused == null && lastFocusedInput != null)
            {
                // Focus lost — restore
                lastFocusedInput = null;
                RestorePadding();
            }

            yield return new WaitForSeconds(0.15f);
        }
    }

    private void ApplyPadding(float keyboardPixelHeight)
    {
        if (contentVlg == null) return;

        float scaleFactor = GetCanvasScaleFactor();
        int paddingNeeded = Mathf.CeilToInt(keyboardPixelHeight / scaleFactor);

        var pad = contentVlg.padding;
        contentVlg.padding = new RectOffset(pad.left, pad.right, pad.top, originalBottomPadding + paddingNeeded);
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        paddingApplied = true;
    }

    private void RestorePadding()
    {
        if (!paddingApplied || contentVlg == null) return;

        var pad = contentVlg.padding;
        contentVlg.padding = new RectOffset(pad.left, pad.right, pad.top, originalBottomPadding);
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        paddingApplied = false;
    }

    private void ScrollToShow(TMP_InputField input, float keyboardPixelHeight)
    {
        if (scrollRect == null || contentRect == null) return;

        Canvas.ForceUpdateCanvases();

        var inputRect = (RectTransform)input.transform;
        var viewportRect = scrollRect.viewport != null
            ? scrollRect.viewport
            : (RectTransform)scrollRect.transform;

        // Get positions in world space
        Vector3[] inputCorners = new Vector3[4];
        inputRect.GetWorldCorners(inputCorners);
        float inputBottom = inputCorners[0].y;
        float inputTop = inputCorners[1].y;

        Vector3[] viewportCorners = new Vector3[4];
        viewportRect.GetWorldCorners(viewportCorners);
        float viewportBottom = viewportCorners[0].y;
        float viewportTop = viewportCorners[1].y;
        float viewportWorldHeight = viewportTop - viewportBottom;

        // Keyboard in world units
        float keyboardWorldHeight = keyboardPixelHeight * (viewportWorldHeight / Screen.height);
        float visibleBottom = viewportBottom + keyboardWorldHeight;
        float inputHeight = inputTop - inputBottom;
        float extraMargin = inputHeight * 1f;

        // Check if input + margin is behind the keyboard
        if (inputBottom >= visibleBottom + inputHeight + extraMargin) return;

        // Calculate how much to scroll (enough to show input + button below it)
        float delta = (visibleBottom + inputHeight + extraMargin) - inputBottom;
        float contentHeight = contentRect.rect.height;
        float viewportLocalHeight = viewportRect.rect.height;
        float scrollable = contentHeight - viewportLocalHeight;

        if (scrollable <= 0f) return;

        float scale = contentRect.lossyScale.y;
        if (Mathf.Approximately(scale, 0f)) return;

        float normalizedDelta = delta / (scrollable * scale);
        float target = Mathf.Clamp01(scrollRect.verticalNormalizedPosition - normalizedDelta);

        DOTween.To(
            () => scrollRect.verticalNormalizedPosition,
            x => scrollRect.verticalNormalizedPosition = x,
            target, 0.4f
        ).SetEase(Ease.OutCubic);
    }

    private TMP_InputField FindFocusedInput()
    {
        var inputs = GetComponentsInChildren<TMP_InputField>(false);
        foreach (var input in inputs)
        {
            if (input.isFocused)
                return input;
        }
        return null;
    }

    private float GetCanvasScaleFactor()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas != null)
            return canvas.rootCanvas.scaleFactor;
        return 1f;
    }

    private static float EstimateKeyboardHeight()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        float measured = MeasureKeyboardHeightAndroid();
        return measured > 0f ? measured : Screen.height * 0.4f;
#elif UNITY_IOS && !UNITY_EDITOR
        float area = TouchScreenKeyboard.area.height;
        return area > 0f ? area : Screen.height * 0.4f;
#else
        // Editor: simulate keyboard covering 40% of screen for testing
        // Change to 0f to disable in editor
        return 0f;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static float MeasureKeyboardHeightAndroid()
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

            return height > 100 ? height : 0f;
        }
        catch
        {
            return 0f;
        }
    }
#endif
}
