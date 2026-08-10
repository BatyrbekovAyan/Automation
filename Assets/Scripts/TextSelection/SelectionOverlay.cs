using UnityEngine;
using UnityEngine.UI;

/// Runtime-created ScreenSpaceOverlay canvas hosting the two selection pins
/// and the edit menu. Sorting order 4: above the scene's main canvas
/// (order 0) like the reaction-bar overlay precedent (order 5), and below
/// that bar so bubble long-press UI wins if ever concurrent. Selection UI
/// only exists while a field is focused, and focus loss dismisses it, so it
/// never sits over LoadingPanel moments. Created lazily by
/// TextSelectionRouter; survives for the app's lifetime.
public class SelectionOverlay : MonoBehaviour
{
    public const int SortingOrder = 4;

    public SelectionHandleView StartHandle { get; private set; }
    public SelectionHandleView EndHandle { get; private set; }
    public RectTransform HandleClip { get; private set; }
    public RectTransform MenuRoot { get; private set; }
    public Canvas Canvas { get; private set; }

    public bool HandlesVisible => StartHandle != null && StartHandle.gameObject.activeSelf;

    public static SelectionOverlay Create()
    {
        var go = new GameObject("TextSelectionOverlay",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(SelectionOverlay));
        DontDestroyOnLoad(go);

        var overlay = go.GetComponent<SelectionOverlay>();
        overlay.Canvas = go.GetComponent<Canvas>();
        overlay.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.Canvas.sortingOrder = SortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // The pins live under a RectMask2D driven to the field's visible area
        // (SetHandleClip). Without it nothing clips them to the field — they
        // render on this canvas, not inside the card — so a pin whose line
        // scrolls to the card's edge would hang its dot over the neighbouring
        // UI. Masking (rather than hiding) gives the iOS behavior: the dot is
        // cut at the card edge and culled once fully outside, and a pin being
        // dragged keeps working the whole time.
        var clipGo = new GameObject("HandleClip", typeof(RectTransform), typeof(RectMask2D));
        clipGo.transform.SetParent(go.transform, false);
        overlay.HandleClip = (RectTransform)clipGo.transform;
        overlay.HandleClip.anchorMin = new Vector2(0.5f, 0.5f);
        overlay.HandleClip.anchorMax = new Vector2(0.5f, 0.5f);
        overlay.HandleClip.pivot = new Vector2(0.5f, 0.5f);

        overlay.StartHandle = SelectionHandleView.Build(clipGo.transform, isStart: true);
        overlay.EndHandle = SelectionHandleView.Build(clipGo.transform, isStart: false);

        var menuRoot = new GameObject("MenuRoot", typeof(RectTransform));
        menuRoot.transform.SetParent(go.transform, false);
        overlay.MenuRoot = (RectTransform)menuRoot.transform;
        overlay.MenuRoot.anchorMin = Vector2.zero;
        overlay.MenuRoot.anchorMax = Vector2.one;
        overlay.MenuRoot.offsetMin = Vector2.zero;
        overlay.MenuRoot.offsetMax = Vector2.zero;

        return overlay;
    }

    public void ShowHandles()
    {
        StartHandle.gameObject.SetActive(true);
        EndHandle.gameObject.SetActive(true);
    }

    /// <summary>
    /// Confines the pins to the area the field's text is actually visible in,
    /// in screen space. Called every reposition — the card scrolls, so the
    /// window moves.
    /// </summary>
    public void SetHandleClip(Rect screenRect)
    {
        if (HandleClip == null) return;

        var root = (RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            root, new Vector2(screenRect.xMin, screenRect.yMin), null, out var min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            root, new Vector2(screenRect.xMax, screenRect.yMax), null, out var max);

        // A field scrolled entirely out of its page yields disjoint rects;
        // collapse to zero so the mask culls the pins instead of flipping
        // inside out.
        var size = new Vector2(Mathf.Max(0f, max.x - min.x), Mathf.Max(0f, max.y - min.y));
        var center = (min + max) * 0.5f;

        // Runs every frame a selection is up — only touch the RectTransform on
        // a real change, or each frame marks the canvas for rebuild.
        if ((HandleClip.anchoredPosition - center).sqrMagnitude > 0.01f)
            HandleClip.anchoredPosition = center;
        if ((HandleClip.sizeDelta - size).sqrMagnitude > 0.01f)
            HandleClip.sizeDelta = size;
    }

    public void HideHandles()
    {
        StartHandle.gameObject.SetActive(false);
        EndHandle.gameObject.SetActive(false);
    }

    public void HideAll()
    {
        HideHandles();
        for (int i = 0; i < MenuRoot.childCount; i++)
            MenuRoot.GetChild(i).gameObject.SetActive(false);
    }

    /// worldTop/worldBottom: the caret line's top/bottom in world space at
    /// the selection edge. The pin parks its stem over that line segment.
    public void PositionHandle(SelectionHandleView handle, Vector3 worldTop, Vector3 worldBottom)
    {
        var rt = (RectTransform)handle.transform;
        Vector2 screenTop = RectTransformUtility.WorldToScreenPoint(null, worldTop);
        Vector2 screenBottom = RectTransformUtility.WorldToScreenPoint(null, worldBottom);
        // Relative to the pin's own parent (HandleClip), which moves with the
        // field — not to the overlay root.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)rt.parent, (screenTop + screenBottom) * 0.5f, null, out var local);
        rt.anchoredPosition = local;
        handle.SetStemHeight(Mathf.Abs(screenTop.y - screenBottom.y) / CanvasScale());
        handle.SetColor(Theme.Color(ThemeRole.AccentFill));
    }

    float CanvasScale() => Canvas.scaleFactor <= 0f ? 1f : Canvas.scaleFactor;
}
