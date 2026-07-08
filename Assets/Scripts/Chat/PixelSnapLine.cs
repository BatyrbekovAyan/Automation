using UnityEngine;

/// <summary>
/// Sizes one axis of this RectTransform to the nearest whole physical pixel so a
/// thin line/divider renders crisp and identically across devices (no sub-pixel
/// shimmer). Generalizes the retired NativeHairline (which only forced 1px height).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class PixelSnapLine : MonoBehaviour
{
    public enum SnapAxis { Height, Width }

    [SerializeField] private SnapAxis axis = SnapAxis.Height;
    [Tooltip("Authored thickness in canvas units (1, 2, 3…). Snapped to whole px at runtime.")]
    [SerializeField] private float designThicknessUnits = 1f;

    private RectTransform rect;
    private Canvas canvas;

    public static RectTransform.Axis ToUnityAxis(SnapAxis a)
        => a == SnapAxis.Height ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal;

    private void OnEnable() { Cache(); Apply(); }

    private void OnRectTransformDimensionsChange() { Apply(); }

    private void Cache()
    {
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
    }

    private void Apply()
    {
        if (rect == null || canvas == null) Cache();
        if (rect == null || canvas == null) return;

        float snapped = PixelSnap.SnapUnits(designThicknessUnits, canvas);
        var uAxis = ToUnityAxis(axis);

        // Epsilon guard: skip the resize when nothing changes, so we don't re-dirty
        // layout on unrelated rebuild passes. SnapUnits is idempotent, so this converges.
        float current = uAxis == RectTransform.Axis.Vertical ? rect.rect.height : rect.rect.width;
        if (Mathf.Abs(current - snapped) < 0.01f) return;

        rect.SetSizeWithCurrentAnchors(uAxis, snapped);
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: set serialized fields from the wiring tool.</summary>
    public void EditorConfigure(SnapAxis snapAxis, float thicknessUnits)
    {
        axis = snapAxis;
        designThicknessUnits = thicknessUnits;
    }
#endif
}
