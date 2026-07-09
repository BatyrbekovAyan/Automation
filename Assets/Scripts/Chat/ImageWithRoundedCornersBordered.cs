using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rounded-corners UI image that also draws a uniform, self-anti-aliased SDF border via
/// UI/RoundedCorners/RoundedCornersBordered. Border width is snapped to a whole physical pixel
/// (shared PixelSnap helper). Mirrors the public surface of Nobi's ImageWithRoundedCorners
/// (radius / Validate / Refresh) so MessageItemView.RefreshCorners can drive it identically.
/// </summary>
[ExecuteInEditMode]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ImageWithRoundedCornersBordered : MonoBehaviour
{
    private static readonly int Props = Shader.PropertyToID("_WidthHeightRadius");
    private static readonly int OuterUvProp = Shader.PropertyToID("_OuterUV");
    private static readonly int BorderColorProp = Shader.PropertyToID("_BorderColor");
    private static readonly int BorderWidthProp = Shader.PropertyToID("_BorderWidth");

    public float radius = 28f;
    public Color borderColor = new Color(0.851f, 0.831f, 0.792f, 1f);
    [Tooltip("Authored per-side border width in canvas units. Snapped to whole px at runtime.")]
    public float designBorderUnits = 1f;

    private Material material;
    private Vector4 outerUV = new Vector4(0, 0, 1, 1);
    private Canvas canvas;
    [HideInInspector, SerializeField] private MaskableGraphic image;

    private void OnEnable() { Validate(); Refresh(); }

    private void OnValidate() { Validate(); Refresh(); }

    private void OnDestroy()
    {
        if (image != null) image.material = null;
        if (material != null)
        {
            // Destroy() at runtime, DestroyImmediate() in the editor — the per-instance material
            // is a runtime object, not a disk asset.
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        image = null;
        material = null;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (enabled && material != null) Refresh();
    }

    public void Validate()
    {
        if (material == null)
            material = new Material(Shader.Find("UI/RoundedCorners/RoundedCornersBordered"));
        if (image == null) TryGetComponent(out image);
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (image != null) image.material = material;
        if (image is Image uiImage && uiImage.sprite != null)
            outerUV = UnityEngine.Sprites.DataUtility.GetOuterUV(uiImage.sprite);
    }

    public void Refresh()
    {
        if (material == null) return;
        var rect = ((RectTransform)transform).rect;
        material.SetVector(Props, new Vector4(rect.width, rect.height, radius * 2f, 0));
        material.SetVector(OuterUvProp, outerUV);
        material.SetColor(BorderColorProp, borderColor);
        float snapped = canvas != null ? PixelSnap.SnapUnits(designBorderUnits, canvas) : designBorderUnits;
        material.SetFloat(BorderWidthProp, snapped);
    }

    /// <summary>Hide the border (e.g. transparent sticker bubble) by zeroing its width.</summary>
    public void SetBorderVisible(bool visible)
    {
        if (material == null) Validate();
        if (material == null) return;
        float snapped = canvas != null ? PixelSnap.SnapUnits(designBorderUnits, canvas) : designBorderUnits;
        material.SetFloat(BorderWidthProp, visible ? snapped : 0f);
    }
}
