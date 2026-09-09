#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Regenerates the four static SF Pro SDF atlases IN PLACE (2026-09-09, Android device pass).
///
/// Why: the atlases were created with AutoSize sampling (224 pt to fill 4096²) and padding 5 —
/// an SDF spread of 2.2 % of the em. TextMeshPro's Distance Field shader can only anti-alias a
/// glyph while that spread covers ≥ 1 screen pixel; below it the face alpha at the quad's edge
/// stays above 0 and every glyph draws a translucent BOX behind it. Threshold = pointSize /
/// (√2 · (padding + 1)) px of em ≈ 45 px, i.e. every label under ~45u on a 1080-wide phone and
/// nearly all body text on a 720-wide one (canvas scale 0.667). The Editor and iOS never showed
/// it because their canvas scale keeps the em above the threshold. 112 pt / padding 12 (10.7 %)
/// moves the threshold to ~6 px. Pinned by FontAtlasPaddingPremiseTests; the character set is
/// unchanged, so FontGlyphCoverageTests stays the coverage gate.
///
/// How: TMP's own creator window has no batch API, so the atlas is rendered through the public
/// runtime path (a throw-away Dynamic font asset + TryAddCharacters) and then written INTO the
/// existing asset the way the creator's "update existing" branch does — same asset GUID, same
/// atlas-texture and material sub-assets, so every scene/prefab reference survives. Idempotent.
///
///   Unity -batchmode -nographics -projectPath . -executeMethod SfProAtlasRegenerator.RegenerateHeadless -quit
/// </summary>
public static class SfProAtlasRegenerator
{
    public const int PointSize = 112;
    public const int Padding = 12;
    public const int AtlasSize = 4096;
    public const string CharacterSequence = "32-591,1024-1327,8192-8303,8352-8399";

    private static readonly (string asset, string ttf)[] Fonts =
    {
        ("Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset",  "Assets/TextMesh Pro/Fonts/SFProText-Regular.ttf"),
        ("Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset",   "Assets/TextMesh Pro/Fonts/SFProText-Medium.ttf"),
        ("Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset", "Assets/TextMesh Pro/Fonts/SFProText-Semibold.ttf"),
        ("Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset",     "Assets/TextMesh Pro/Fonts/SFProText-Bold.ttf"),
    };

    [MenuItem("Tools/Fonts/Regenerate SF Pro Atlases (112pt, pad 12)")]
    public static void Regenerate()
    {
        foreach (var (asset, ttf) in Fonts)
            RegenerateOne(asset, ttf);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void RegenerateHeadless()
    {
        Regenerate();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void RegenerateOne(string assetPath, string ttfPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (existing == null || font == null)
            throw new InvalidOperationException($"[SfProAtlasRegenerator] missing {assetPath} or {ttfPath}");

        int charactersBefore = existing.characterTable.Count;
        FaceInfo metricsBefore = existing.faceInfo;   // the line metrics every layout and test was tuned to

        // 1. Render the new atlas through the public runtime path.
        var temp = TMP_FontAsset.CreateFontAsset(font, PointSize, Padding, GlyphRenderMode.SDFAA,
            AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
        uint[] unicodes = ParseSequence(CharacterSequence);
        temp.TryAddCharacters(unicodes, out uint[] missing, includeFontFeatures: true);
        temp.glyphTable.Sort((a, b) => a.index.CompareTo(b.index));
        temp.characterTable.Sort((a, b) => a.unicode.CompareTo(b.unicode));

        if (temp.atlasTextures == null || temp.atlasTextures.Length == 0 || temp.atlasTextures[0] == null)
            throw new InvalidOperationException($"[SfProAtlasRegenerator] {assetPath}: no atlas texture rendered");
        if (temp.characterTable.Count < charactersBefore)
            throw new InvalidOperationException(
                $"[SfProAtlasRegenerator] {assetPath}: {temp.characterTable.Count} characters rendered, the atlas had {charactersBefore} — refusing to shrink the set");

        // 2. Copy the definition into the EXISTING asset (creator's "update existing" branch).
        var src = new SerializedObject(temp);
        var dst = new SerializedObject(existing);
        foreach (var field in new[]
                 {
                     "m_FaceInfo", "m_GlyphTable", "m_CharacterTable", "m_FontFeatureTable",
                     "m_AtlasWidth", "m_AtlasHeight", "m_AtlasPadding", "m_AtlasRenderMode",
                     "m_UsedGlyphRects", "m_FreeGlyphRects",
                 })
        {
            var prop = src.FindProperty(field);
            if (prop == null) throw new InvalidOperationException($"[SfProAtlasRegenerator] TMP_FontAsset has no '{field}' — TMP changed shape");
            dst.CopyFromSerializedProperty(prop);
        }
        dst.FindProperty("m_AtlasTextureIndex").intValue = 0;
        dst.ApplyModifiedPropertiesWithoutUndo();

        // Keep the ORIGINAL line metrics, scaled to the new sampling size. The freshly loaded face
        // reports hhea ascender/descender (0.952/0.241 em) where the 2024 atlas carried 0.970/0.260 em
        // — a 3 % tighter line height that every multi-line layout and the font-measuring tests were
        // tuned against. This regeneration is about the SDF spread only; glyph shapes and advances
        // come from the same font, so pinning the face metrics keeps text layout byte-identical.
        existing.faceInfo = ScaleMetrics(metricsBefore, existing.faceInfo);

        // 3. Pixel data into the existing atlas sub-asset (keeps its fileID for the material).
        Texture2D srcTex = temp.atlasTextures[0];
        Texture2D dstTex = existing.atlasTextures[0];
        SetReadable(dstTex, true);
        if (dstTex.width != srcTex.width || dstTex.height != srcTex.height || dstTex.format != srcTex.format)
            dstTex.Reinitialize(srcTex.width, srcTex.height, srcTex.format, false);
        dstTex.SetPixelData(srcTex.GetPixelData<byte>(0), 0);
        dstTex.Apply(false, false);
        SetReadable(dstTex, false);

        // 4. Every material sampling this atlas learns the new spread.
        foreach (var material in MaterialsSampling(existing, dstTex))
        {
            material.SetTexture(ShaderUtilities.ID_MainTex, dstTex);
            material.SetFloat(ShaderUtilities.ID_TextureWidth, dstTex.width);
            material.SetFloat(ShaderUtilities.ID_TextureHeight, dstTex.height);
            material.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
            EditorUtility.SetDirty(material);
        }

        // 5. Creation settings, so the creator window shows how this atlas was made.
        var settings = existing.creationSettings;
        settings.pointSizeSamplingMode = 1;      // Custom Size
        settings.pointSize = PointSize;
        settings.padding = Padding;
        settings.atlasWidth = AtlasSize;
        settings.atlasHeight = AtlasSize;
        settings.characterSequence = CharacterSequence;
        settings.renderMode = (int)GlyphRenderMode.SDFAA;
        settings.includeFontFeatures = true;
        existing.creationSettings = settings;

        existing.ReadFontAssetDefinition();
        EditorUtility.SetDirty(existing);
        EditorUtility.SetDirty(dstTex);

        UnityEngine.Object.DestroyImmediate(srcTex);
        UnityEngine.Object.DestroyImmediate(temp);

        Debug.Log($"[SfProAtlasRegenerator] {System.IO.Path.GetFileName(assetPath)}: {existing.characterTable.Count} characters " +
                  $"(was {charactersBefore}, missing in font: {missing?.Length ?? 0}) at {PointSize} pt, padding {Padding}, " +
                  $"{existing.atlasWidth}×{existing.atlasHeight}, _GradientScale {Padding + 1}");
    }

    /// <summary>Original metrics re-expressed at the new sampling point size (unitsPerEM/family/style from the new face).</summary>
    private static FaceInfo ScaleMetrics(FaceInfo before, FaceInfo fresh)
    {
        float ratio = fresh.pointSize / before.pointSize;
        var f = fresh;
        f.scale = before.scale;
        f.lineHeight = before.lineHeight * ratio;
        f.ascentLine = before.ascentLine * ratio;
        f.capLine = before.capLine * ratio;
        f.meanLine = before.meanLine * ratio;
        f.baseline = before.baseline * ratio;
        f.descentLine = before.descentLine * ratio;
        f.superscriptOffset = before.superscriptOffset * ratio;
        f.superscriptSize = before.superscriptSize;
        f.subscriptOffset = before.subscriptOffset * ratio;
        f.subscriptSize = before.subscriptSize;
        f.underlineOffset = before.underlineOffset * ratio;
        f.underlineThickness = before.underlineThickness * ratio;
        f.strikethroughOffset = before.strikethroughOffset * ratio;
        f.strikethroughThickness = before.strikethroughThickness * ratio;
        f.tabWidth = before.tabWidth * ratio;
        return f;
    }

    /// <summary>
    /// The font's own material plus every material asset in the project that samples this atlas
    /// (TMP material presets) — TMP_EditorUtility.FindMaterialReferences is internal, so this is
    /// the same search done by hand.
    /// </summary>
    private static IEnumerable<Material> MaterialsSampling(TMP_FontAsset font, Texture2D atlas)
    {
        var found = new HashSet<Material>();
        if (font.material != null) found.Add(font.material);
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Material m && m.HasProperty(ShaderUtilities.ID_MainTex) &&
                    m.GetTexture(ShaderUtilities.ID_MainTex) == atlas)
                    found.Add(m);
            }
        }
        return found;
    }

    private static void SetReadable(Texture2D texture, bool readable)
    {
        var so = new SerializedObject(texture);
        var prop = so.FindProperty("m_IsReadable");
        if (prop == null) throw new InvalidOperationException("[SfProAtlasRegenerator] Texture2D has no m_IsReadable");
        prop.boolValue = readable;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>"32-591,1024-1327" → every code point in the inclusive ranges, ascending, unique.</summary>
    public static uint[] ParseSequence(string sequence)
    {
        var set = new SortedSet<uint>();
        foreach (var part in sequence.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = part.Trim().Split('-');
            uint lo = uint.Parse(bounds[0]);
            uint hi = bounds.Length > 1 ? uint.Parse(bounds[1]) : lo;
            for (uint u = lo; u <= hi; u++) set.Add(u);
        }
        return set.ToArray();
    }
}
#endif
