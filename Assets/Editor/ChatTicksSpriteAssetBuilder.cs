#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates three tick sprite PNGs (sent, delivered, read-blue) and packages
/// them into a TMP Sprite Asset referenced by ChatPreviewFormatter.
///
/// Outputs:
///   Assets/Images/Chat/Ticks/tick_sent.png         (gray single ✓)
///   Assets/Images/Chat/Ticks/tick_double.png       (gray double ✓✓)
///   Assets/Images/Chat/Ticks/tick_double_blue.png  (WhatsApp-blue double ✓✓)
///   Assets/Resources/Sprite Assets/ChatTicks_Atlas.png (combined 192x64 atlas)
///   Assets/Resources/Sprite Assets/ChatTicks.asset (TMP_SpriteAsset)
///
/// Idempotent — re-running overwrites existing assets in place.
/// </summary>
public static class ChatTicksSpriteAssetBuilder
{
    private const int TextureSize = 64;
    private const float StrokeRadius = 4f;
    private const string TicksFolder = "Assets/Images/Chat/Ticks";
    private const string SpriteAssetFolder = "Assets/Resources/Sprite Assets";
    private const string SpriteAssetPath = "Assets/Resources/Sprite Assets/ChatTicks.asset";
    private const string AtlasPath = "Assets/Resources/Sprite Assets/ChatTicks_Atlas.png";

    private static readonly Color32 Gray = new Color32(0x99, 0x99, 0x99, 0xFF);
    private static readonly Color32 Blue = new Color32(0x34, 0xB7, 0xF1, 0xFF);

    [MenuItem("Tools/Chat List/Generate Tick Sprites")]
    public static void Build()
    {
        EnsureFolder(TicksFolder);
        EnsureFolder(SpriteAssetFolder);

        WritePng($"{TicksFolder}/tick_sent.png", DrawSingleTick(Gray));
        WritePng($"{TicksFolder}/tick_double.png", DrawDoubleTick(Gray));
        WritePng($"{TicksFolder}/tick_double_blue.png", DrawDoubleTick(Blue));

        AssetDatabase.Refresh();

        ImportAsSprite($"{TicksFolder}/tick_sent.png");
        ImportAsSprite($"{TicksFolder}/tick_double.png");
        ImportAsSprite($"{TicksFolder}/tick_double_blue.png");

        BuildSpriteAsset();

        Debug.Log($"[ChatTicksSpriteAssetBuilder] Generated 3 ticks and {SpriteAssetPath}");
    }

    private static Texture2D DrawSingleTick(Color32 color)
    {
        var tex = NewTransparent();
        // ✓ as two line segments inside 64x64. Stroke geometry hand-tuned for balance.
        DrawLine(tex, new Vector2(16, 30), new Vector2(28, 18), color);
        DrawLine(tex, new Vector2(28, 18), new Vector2(50, 46), color);
        tex.Apply();
        return tex;
    }

    private static Texture2D DrawDoubleTick(Color32 color)
    {
        var tex = NewTransparent();
        // Front tick (right) - same shape as single, shifted right by ~4px.
        DrawLine(tex, new Vector2(20, 30), new Vector2(32, 18), color);
        DrawLine(tex, new Vector2(32, 18), new Vector2(54, 46), color);
        // Back tick (left, behind) - smaller, behind the right one.
        DrawLine(tex, new Vector2(10, 30), new Vector2(22, 18), color);
        DrawLine(tex, new Vector2(22, 18), new Vector2(38, 38), color);
        tex.Apply();
        return tex;
    }

    private static Texture2D NewTransparent()
    {
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[TextureSize * TextureSize];
        // Default Color32 is (0,0,0,0) - fully transparent. No init needed.
        tex.SetPixels32(pixels);
        return tex;
    }

    private static void DrawLine(Texture2D tex, Vector2 a, Vector2 b, Color32 color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - StrokeRadius - 1));
        int maxX = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + StrokeRadius + 1));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - StrokeRadius - 1));
        int maxY = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + StrokeRadius + 1));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float distance = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, b);
                if (distance >= StrokeRadius + 1f) continue;
                float coverage = Mathf.Clamp01(StrokeRadius + 0.5f - distance);
                if (coverage <= 0f) continue;

                var existing = tex.GetPixel(x, y);
                float existingA = existing.a;
                float newA = (color.a / 255f) * coverage;
                float outA = newA + existingA * (1f - newA);
                if (outA < 0.001f) continue;

                var outColor = new Color(
                    (color.r / 255f * newA + existing.r * existingA * (1f - newA)) / outA,
                    (color.g / 255f * newA + existing.g * existingA * (1f - newA)) / outA,
                    (color.b / 255f * newA + existing.b * existingA * (1f - newA)) / outA,
                    outA);
                tex.SetPixel(x, y, outColor);
            }
        }
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector2 projection = a + t * ab;
        return Vector2.Distance(p, projection);
    }

    private static void EnsureFolder(string path)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
    }

    private static void WritePng(string path, Texture2D tex)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static void ImportAsSprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = TextureSize;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    private static void BuildSpriteAsset()
    {
        var sentSprite = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TicksFolder}/tick_sent.png");
        var doubleSprite = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TicksFolder}/tick_double.png");
        var blueSprite = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TicksFolder}/tick_double_blue.png");
        if (sentSprite == null || doubleSprite == null || blueSprite == null)
        {
            Debug.LogError("[ChatTicksSpriteAssetBuilder] Could not load generated source textures.");
            return;
        }

        // Pack into a 192x64 atlas (1 row, 3 columns).
        var atlas = new Texture2D(TextureSize * 3, TextureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        atlas.SetPixels32(new Color32[TextureSize * 3 * TextureSize]);
        atlas.SetPixels(0,             0, TextureSize, TextureSize, sentSprite.GetPixels());
        atlas.SetPixels(TextureSize,   0, TextureSize, TextureSize, doubleSprite.GetPixels());
        atlas.SetPixels(TextureSize*2, 0, TextureSize, TextureSize, blueSprite.GetPixels());
        atlas.Apply();

        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(AtlasPath);

        var atlasImporter = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        atlasImporter.textureType = TextureImporterType.Sprite;
        atlasImporter.spriteImportMode = SpriteImportMode.Single;
        atlasImporter.mipmapEnabled = false;
        atlasImporter.alphaIsTransparency = true;
        atlasImporter.isReadable = true;
        atlasImporter.filterMode = FilterMode.Bilinear;
        atlasImporter.SaveAndReimport();
        var atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        TMP_SpriteAsset spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
        if (spriteAsset == null)
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, SpriteAssetPath);
        }

        spriteAsset.spriteSheet = atlasTexture;

        // Inherit material from the EmojiOne sprite asset (always present in this project).
        // The material MUST be added as a sub-asset of the TMP_SpriteAsset so it survives
        // domain reloads — matching TMP's own creation pattern at
        // TMP_SpriteAssetMenu.cs:374-383 (AddDefaultMaterial). Without AddObjectToAsset
        // the reference is orphaned on disk and resolves to "Missing (Material)" on
        // the next Unity reload.
        var emojiOne = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
            "Assets/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset");
        if (emojiOne != null && emojiOne.material != null)
        {
            // Clean up any previous material we authored on a prior builder run so we don't
            // accumulate orphan sub-assets across re-runs (the builder is idempotent).
            var previousMaterial = spriteAsset.material;
            var newMaterial = new Material(emojiOne.material)
            {
                mainTexture = atlasTexture,
                name = "ChatTicks Material",
            };
            spriteAsset.material = newMaterial;
            AssetDatabase.AddObjectToAsset(newMaterial, spriteAsset);
            if (previousMaterial != null
                && previousMaterial != newMaterial
                && AssetDatabase.IsSubAsset(previousMaterial))
            {
                Object.DestroyImmediate(previousMaterial, true);
            }
        }
        else
        {
            Debug.LogWarning("[ChatTicksSpriteAssetBuilder] Could not load EmojiOne material — sprite asset will use TMP fallback at render time.");
        }

        var glyphTable = new List<TMP_SpriteGlyph>(3);
        var charTable = new List<TMP_SpriteCharacter>(3);
        AddGlyph(glyphTable, charTable, 0u, "tick_sent", 0);
        AddGlyph(glyphTable, charTable, 1u, "tick_double", TextureSize);
        AddGlyph(glyphTable, charTable, 2u, "tick_double_blue", TextureSize * 2);

        spriteAsset.spriteGlyphTable = glyphTable;
        spriteAsset.spriteCharacterTable = charTable;
        spriteAsset.UpdateLookupTables();

        EditorUtility.SetDirty(spriteAsset);
        if (spriteAsset.material != null) EditorUtility.SetDirty(spriteAsset.material);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void AddGlyph(List<TMP_SpriteGlyph> glyphs, List<TMP_SpriteCharacter> chars,
                                  uint index, string name, int atlasX)
    {
        var glyph = new TMP_SpriteGlyph
        {
            index = index,
            sprite = null,
            glyphRect = new UnityEngine.TextCore.GlyphRect(atlasX, 0, TextureSize, TextureSize),
            metrics = new UnityEngine.TextCore.GlyphMetrics(TextureSize, TextureSize, 0, TextureSize, TextureSize),
            scale = 1f,
            atlasIndex = 0,
        };
        glyphs.Add(glyph);

        var character = new TMP_SpriteCharacter(0u, glyph)
        {
            name = name,
        };
        chars.Add(character);
    }
}
#endif
