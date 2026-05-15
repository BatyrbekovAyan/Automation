using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Sizes a sibling "outline" RectTransform to match this RectTransform's rendered
/// rect plus a small padding, so the outline visually hugs the bubble's edge.
///
/// Hooks into Unity's layout pass via ILayoutSelfController so the outline is
/// synced *within* the same rebuild as ContentSizeFitter / VerticalLayoutGroup
/// rather than one frame later. DrivenRectTransformTracker marks the outline
/// as driven, so its size/position can't be hand-edited in the inspector and
/// the layout system knows we own those properties.
///
/// Public field names are preserved (`outlineRect`, `extraSize`, `outgoing`) so
/// existing prefab references stay valid. The `UpdateSize()` entry point is
/// kept for callers that nudge a sync manually (see MessageItemView).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class MirrorSize : UIBehaviour, ILayoutSelfController
{
    [Tooltip("Drag your Outline GameObject here")]
    public RectTransform outlineRect;

    [Tooltip("How much bigger should the outline be? (e.g., 2 = 1px border on all sides)")]
    public float extraSize = 2f;

    public bool outgoing;

    private RectTransform myRect;
    private DrivenRectTransformTracker tracker;

    protected override void OnEnable()
    {
        base.OnEnable();
        CacheRect();
        RegisterDriven();
        Sync();
    }

    protected override void OnDisable()
    {
        tracker.Clear();
        base.OnDisable();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Sync();
    }

    public void SetLayoutHorizontal() => Sync();

    public void SetLayoutVertical() => Sync();

    public void UpdateSize() => Sync();

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        RegisterDriven();
        Sync();
    }
#endif

    private void CacheRect()
    {
        if (myRect == null) myRect = (RectTransform)transform;
    }

    private void RegisterDriven()
    {
        tracker.Clear();
        if (outlineRect == null) return;
        tracker.Add(
            this,
            outlineRect,
            DrivenTransformProperties.SizeDelta | DrivenTransformProperties.AnchoredPosition);
    }

    private void Sync()
    {
        if (outlineRect == null) return;
        CacheRect();
        if (myRect == null) return;

        outlineRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, myRect.rect.width + extraSize);
        outlineRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, myRect.rect.height + extraSize);
        outlineRect.localPosition = myRect.localPosition;
    }
}
