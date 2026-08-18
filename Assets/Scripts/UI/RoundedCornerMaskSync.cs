using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Re-pushes a rounded-corner graphic's material after its rect changes, for
/// graphics that live under a stencil <see cref="Mask"/>.
///
/// Nobi's ImageWithRoundedCorners feeds the rect size to the shader through a
/// material vector and refreshes it from OnRectTransformDimensionsChange — but
/// it writes to the graphic's BASE material. A stencil Mask is an
/// IMaterialModifier, so what actually reaches the screen is StencilMaterial's
/// COPY of that material, taken when the graphic last rebuilt its material. A
/// plain resize never marks the material dirty, so the copy goes on rendering
/// the previous size.
///
/// Symptom that led here (device, 2026-08-18): editing an existing product's
/// price resized the price tag correctly but left its corners drawn for the old
/// width, and only re-opening bot settings — which re-enables the graphic and
/// rebuilds the copy — put it right. The Product tab's Viewport carries a Mask,
/// and the tag is the one element in the card whose width changes at runtime.
///
/// NOT reproduced in EditMode: materialForRendering recomputes the modifier
/// chain on every access, so a test cannot observe the stale copy that the
/// CanvasRenderer is holding. The mechanism is read off uGUI's own behaviour
/// rather than off a red test — the guard below is structural.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class RoundedCornerMaskSync : MonoBehaviour
{
    private Graphic graphic;

    private void Awake() => graphic = GetComponent<Graphic>();

    private void OnRectTransformDimensionsChange()
    {
        if (graphic == null) graphic = GetComponent<Graphic>();

        // Queued, not immediate: the rebuild lands in the same canvas update
        // that produced the resize, so the corners never render a stale frame.
        if (graphic != null && graphic.isActiveAndEnabled) graphic.SetMaterialDirty();
    }
}
