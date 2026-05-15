using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// <summary>
/// Reusable outline frame. Drives a thin outline ring around its content via
/// Unity's layout system instead of a script-driven mirroring component.
///
/// Structure:
///   OutlineFrame  ← HorizontalLayoutGroup + ContentSizeFitter + this component
///   ├─ Outline    ← Image (rounded), anchors stretched, offsets ±Thickness, ignoreLayout
///   └─ Content    ← whatever you want outlined; drives the frame's preferred size
///
/// The outline never shifts because both frame size and outline stretch resolve
/// in the same layout pass.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class OutlineFrame : MonoBehaviour
{
    public const int Thickness = 2;
    public const float RadiusOffset = 1f;

    public static readonly Color DefaultColor = new Color(0.851f, 0.831f, 0.792f, 1f);

    [SerializeField] private Image outlineImage;
    [SerializeField] private ImageWithRoundedCorners outlineRounded;
    [SerializeField] private RectTransform content;

    public Image OutlineImage => outlineImage;
    public RectTransform Content => content;

    public void SetVisible(bool visible)
    {
        if (outlineImage != null) outlineImage.enabled = visible;
    }

    public void SetColor(Color color)
    {
        if (outlineImage != null) outlineImage.color = color;
    }

    public void SetContentCornerRadius(float contentRadius)
    {
        if (outlineRounded == null) return;
        outlineRounded.radius = contentRadius + RadiusOffset;
        outlineRounded.Validate();
        outlineRounded.Refresh();
    }
}
