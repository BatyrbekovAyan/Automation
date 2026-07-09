using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Snaps a <see cref="LayoutElement"/>'s preferredHeight to a whole physical pixel, for thin
/// dividers whose thickness is driven by a parent LayoutGroup (childControlHeight) rather than
/// the RectTransform size. Those cannot be snapped by <see cref="PixelSnapLine"/> — the layout
/// group overrides its SetSize each pass — so we snap the source value the layout reads instead.
/// Companion to PixelSnapLine; shares the <see cref="PixelSnap"/> helper.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LayoutElement))]
public class PixelSnapLayoutThickness : MonoBehaviour
{
    [Tooltip("Authored preferredHeight in canvas units (1, 2, 3…). Snapped to whole px at runtime.")]
    [SerializeField] private float designThicknessUnits = 1f;

    private LayoutElement layoutElement;
    private Canvas canvas;

    private void OnEnable() { Cache(); Apply(); }

    private void OnRectTransformDimensionsChange() { Apply(); }

    private void Cache()
    {
        layoutElement = GetComponent<LayoutElement>();
        canvas = GetComponentInParent<Canvas>();
    }

    private void Apply()
    {
        if (layoutElement == null || canvas == null) Cache();
        if (layoutElement == null || canvas == null) return;

        float snapped = PixelSnap.SnapUnits(designThicknessUnits, canvas);
        // Setting preferredHeight marks the layout dirty, so the parent group rebuilds.
        // Epsilon guard avoids a redundant dirty (and re-entrant layout) when nothing changes.
        if (Mathf.Abs(layoutElement.preferredHeight - snapped) < 0.01f) return;
        layoutElement.preferredHeight = snapped;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: set the serialized field from the wiring tool.</summary>
    public void EditorConfigure(float thicknessUnits) => designThicknessUnits = thicknessUnits;
#endif
}
