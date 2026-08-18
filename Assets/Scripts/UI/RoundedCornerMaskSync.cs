using Nobi.UiRoundedCorners;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a rounded-corner graphic's corners correct while it resizes UNDER a
/// stencil <see cref="Mask"/>.
///
/// Nobi's ImageWithRoundedCorners builds no geometry — it feeds the rect size to
/// the shader through the `_WidthHeightRadius` material vector, refreshed from
/// OnRectTransformDimensionsChange onto the graphic's BASE material. Under a
/// Mask, MaskableGraphic swaps in StencilMaterial.Add(baseMaterial, …), and that
/// call is a CACHE keyed by base-material identity: it returns the copy it made
/// once with `new Material(baseMat)` and never re-takes it. So the base keeps
/// receiving the new size while the screen keeps drawing the old one. Measured
/// on the price tag: base 263.59, rendered copy 181.65.
///
/// Marking the material dirty is not enough on its own — the rebuild goes back
/// through StencilMaterial.Add and hits the same cached copy. Only disabling the
/// graphic ever refreshed it (OnDisable → Remove → refcount 0 → entry
/// destroyed), which is the workaround seen on device: switch tabs and back.
///
/// So this component joins the modifier chain itself. materialForRendering runs
/// every IMaterialModifier on the GameObject in component order, and the
/// MaskableGraphic's own modifier — the one producing the stencil copy — sits
/// before this one, so the material arriving here IS the copy that will be
/// handed to the CanvasRenderer. Stamping the current size on it there means it
/// is refreshed on every material rebuild, and the resize itself requests one.
///
/// Each graphic owns its own base material (Nobi news one up per component), so
/// each has its own cache entry: writing here cannot leak into another card.
///
/// Symptom that led here (device 2026-08-18): editing an existing product's
/// price resized the price tag correctly but drew its corners for the previous
/// width until the tab was switched away and back.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class RoundedCornerMaskSync : MonoBehaviour, IMaterialModifier
{
    private static readonly int WidthHeightRadius = Shader.PropertyToID("_WidthHeightRadius");

    private Graphic graphic;
    private ImageWithRoundedCorners corners;

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        Resolve();
        if (corners == null || baseMaterial == null || !baseMaterial.HasProperty(WidthHeightRadius))
            return baseMaterial;

        // Same expression ImageWithRoundedCorners.Refresh uses, including the
        // doubling the shader halves again — recomputed rather than read back,
        // because the order of the two components' callbacks is undefined.
        var rect = ((RectTransform)transform).rect;
        baseMaterial.SetVector(
            WidthHeightRadius, new Vector4(rect.width, rect.height, corners.radius * 2f, 0f));
        return baseMaterial;
    }

    private void Awake() => Resolve();

    private void OnEnable()
    {
        Resolve();
        RequestMaterialRebuild();
    }

    // A resize alone never marks the material dirty, so nothing would re-enter
    // the modifier chain above.
    private void OnRectTransformDimensionsChange() => RequestMaterialRebuild();

    private void Resolve()
    {
        if (graphic == null) graphic = GetComponent<Graphic>();
        if (corners == null) corners = GetComponent<ImageWithRoundedCorners>();
    }

    private void RequestMaterialRebuild()
    {
        Resolve();
        if (graphic != null && graphic.isActiveAndEnabled) graphic.SetMaterialDirty();
    }
}
