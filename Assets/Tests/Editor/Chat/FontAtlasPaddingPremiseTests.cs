using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;

/// <summary>
/// Premise guard for the SDF atlases (2026-09-09, Android device pass on a 720p phone).
///
/// TextMeshPro's Distance Field shader can only anti-alias a glyph while its SDF spread
/// (the atlas padding) covers at least ONE screen pixel: below that, `scale` in the shader
/// drops under 1 and the face alpha at the far edge of the glyph quad no longer reaches 0,
/// which draws a translucent BOX behind every glyph. The four SF Pro atlases were generated
/// with AutoSize sampling (224 pt to fill 4096²) and padding 5 — a spread of 2.2 % of the em —
/// so every label under ~45 px em got a box: on a 1080-wide phone that is the smallest UI
/// text, on a 720-wide phone (canvas scale 0.667) it is nearly all body text. The Editor and
/// iOS never showed it because their canvas scale keeps the em above the threshold.
///
/// The cure is the atlas, not the shader: padding ≥ 10 % of the sampling point size (TMP's own
/// default ratio) keeps the spread above a pixel down to ~5 px em. This test fails the moment a
/// regenerated atlas comes back with the old ratio.
/// </summary>
public class FontAtlasPaddingPremiseTests
{
    private static readonly string[] FontAssetPaths =
    {
        "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset",
        "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset",
    };

    /// <summary>Padding as a fraction of the sampling point size. TMP's creator defaults to 1/10.</summary>
    public const float MinPaddingRatio = 0.09f;

    [Test]
    public void EverySfProAtlas_HasSdfSpreadWideEnoughForSmallText()
    {
        foreach (var path in FontAssetPaths)
        {
            Assert.IsTrue(File.Exists(path), $"font asset missing: {path}");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            Assert.IsNotNull(font, path);

            int padding = font.atlasPadding;
            float pointSize = font.faceInfo.pointSize;
            Assert.Greater(pointSize, 0f, path);
            float ratio = padding / pointSize;

            Assert.GreaterOrEqual(ratio, MinPaddingRatio,
                $"{Path.GetFileName(path)}: padding {padding} at {pointSize} pt is a {ratio:P1} SDF spread — " +
                $"small text draws a box behind every glyph on low-DPI phones. Regenerate with padding ≥ {MinPaddingRatio:P0} of the point size.");

            // The material must agree with the atlas it samples, or the shader's bias is off.
            var material = font.material;
            Assert.IsNotNull(material, path);
            Assert.AreEqual(padding + 1, (int)material.GetFloat(ShaderUtilities.ID_GradientScale),
                $"{Path.GetFileName(path)}: _GradientScale must equal padding + 1");
            Assert.AreEqual(font.atlasWidth, (int)material.GetFloat(ShaderUtilities.ID_TextureWidth), path);
            Assert.AreEqual(font.atlasHeight, (int)material.GetFloat(ShaderUtilities.ID_TextureHeight), path);
        }
    }
}
