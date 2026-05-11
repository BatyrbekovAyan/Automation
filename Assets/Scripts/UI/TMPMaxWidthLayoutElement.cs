using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reports a layout-system preferredWidth equal to MIN(TMP natural width, maxWidth)
/// so a TMP text inside a HorizontalLayoutGroup grows tight to its content but
/// stops at a fixed cap. Combined with TextOverflowModes.Ellipsis on the same
/// TMP, this gives a "shrink to fit, ellipsize past max" behavior that the
/// stock LayoutElement can't express — its preferredWidth is a fixed value,
/// not a cap, so short text would still occupy the full slot.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(TMP_Text))]
[ExecuteAlways]
[DisallowMultipleComponent]
public class TMPMaxWidthLayoutElement : UIBehaviour, ILayoutElement
{
    [Tooltip("Hard cap on the reported preferred width. Past this the TMP rect stays at maxWidth and TMP's overflowMode (use Ellipsis) handles the truncation.")]
    [SerializeField] private float maxWidth = 160f;

    public float MaxWidth
    {
        get => maxWidth;
        set
        {
            if (Mathf.Approximately(maxWidth, value)) return;
            maxWidth = value;
            MarkDirty();
        }
    }

    public float minWidth => 0f;
    public float flexibleWidth => 0f;
    public float minHeight => -1f;
    public float preferredHeight => -1f;
    public float flexibleHeight => -1f;
    public int layoutPriority => 1;

    // TMP_Text.preferredWidth measures unconstrained (margins set to +Inf
    // internally) so this query is not circular when HLG sets our rect width
    // from the value we return.
    public float preferredWidth =>
        Text != null ? Mathf.Min(Text.preferredWidth, maxWidth) : 0f;

    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }

    private TMP_Text cachedText;

    private TMP_Text Text
    {
        get
        {
            if (cachedText == null) cachedText = GetComponent<TMP_Text>();
            return cachedText;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        MarkDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        MarkDirty();
    }
#endif

    private void MarkDirty()
    {
        if (transform is RectTransform rt) LayoutRebuilder.MarkLayoutForRebuild(rt);
    }
}
