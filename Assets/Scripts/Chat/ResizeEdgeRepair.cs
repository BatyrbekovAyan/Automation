using UnityEngine;

/// <summary>
/// Repairs the 1px dark line that NativeGallery's native maxSize downscaler bakes onto the
/// trailing edge of an image's shorter dimension.
///
/// The native scaler renders the resized image into a fractional-pixel rect (e.g. a 1170x2532
/// screenshot fit to maxSize 1024 becomes 473.175 x 1024). The long edge lands on an exact
/// integer (clean); the short edge is fractional, so its trailing column/row is only partially
/// covered and darkens against the opaque (black) backing — a visible line on the SENT image:
/// portrait -> right edge, landscape -> top/bottom edge. WhatsApp's own send never runs this
/// scaler, which is why those images are clean.
///
/// iOS is fixed at the source (NativeGallery.mm rounds the rect to whole pixels), but the
/// Android scaler ships as a prebuilt .aar we cannot patch, so this runs after decode as a
/// cross-platform safety net. It preserves dimensions and aspect (it copies the neighbor line,
/// it does not crop) and is a no-op on already-clean textures.
/// </summary>
public static class ResizeEdgeRepair
{
    // The suspect edge must differ from its inner neighbor by at least this many times more than
    // the OPPOSITE (clean) edge does. Self-calibrating against the image's own clean edge, so no
    // absolute brightness threshold is needed and it works on bright or dark images alike.
    private const float DominanceRatio = 3f;

    // Absolute floor on the mean per-channel delta (0..1) so a uniform image's sensor noise — or
    // a clean texture where both edges match their neighbors — never trips a repair.
    private const float MinEdgeDelta = 0.03f;

    /// <summary>
    /// Detects and repairs the artifact in place. Returns true if a line was repaired.
    /// <paramref name="maxSize"/> is the downscale cap that was requested (e.g. 1024); the repair
    /// only runs when the texture's long edge equals it, i.e. a fit-to-maxSize downscale actually
    /// happened. The texture must be readable and uncompressed (the case for NativeGallery decodes
    /// with markTextureNonReadable:false).
    /// </summary>
    public static bool Repair(Texture2D tex, int maxSize)
    {
        if (tex == null || maxSize <= 0) return false;

        int w = tex.width, h = tex.height;
        if (w < 3 || h < 3) return false;            // no inner neighbor to sample
        if (w == h) return false;                    // square -> both dims integer-exact, no fractional edge
        if (Mathf.Max(w, h) != maxSize) return false; // not freshly downscaled to maxSize -> no artifact

        // Only the SHORTER dimension is fractional after the downscale; examine its two ends and
        // repair the one that stands out from its neighbor. Checking both ends (rather than
        // assuming which is "trailing") absorbs the Texture2D y-flip and any platform difference
        // in which edge the scaler darkens.
        bool vertical = w < h;   // shorter dim is width -> the candidate lines are columns
        int last = (vertical ? w : h) - 1;

        Color[] leading      = Line(tex, vertical, 0);
        Color[] leadingInner = Line(tex, vertical, 1);
        Color[] trailing     = Line(tex, vertical, last);
        Color[] trailingInner = Line(tex, vertical, last - 1);

        float dLeading  = MeanAbsDiff(leading, leadingInner);
        float dTrailing = MeanAbsDiff(trailing, trailingInner);

        if (dTrailing >= MinEdgeDelta && dTrailing >= dLeading * DominanceRatio)
            return WriteLine(tex, vertical, last, trailingInner);
        if (dLeading >= MinEdgeDelta && dLeading >= dTrailing * DominanceRatio)
            return WriteLine(tex, vertical, 0, leadingInner);

        return false;
    }

    // A column (vertical) at x=index, or a row (horizontal) at y=index.
    private static Color[] Line(Texture2D tex, bool vertical, int index) =>
        vertical ? tex.GetPixels(index, 0, 1, tex.height)
                 : tex.GetPixels(0, index, tex.width, 1);

    private static bool WriteLine(Texture2D tex, bool vertical, int index, Color[] src)
    {
        if (vertical) tex.SetPixels(index, 0, 1, tex.height, src);
        else          tex.SetPixels(0, index, tex.width, 1, src);
        tex.Apply(false, false);   // keep the texture readable for the downstream EncodeToJPG
        return true;
    }

    private static float MeanAbsDiff(Color[] a, Color[] b)
    {
        int n = Mathf.Min(a.Length, b.Length);
        if (n == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < n; i++)
            sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
        return sum / (n * 3f);
    }
}
