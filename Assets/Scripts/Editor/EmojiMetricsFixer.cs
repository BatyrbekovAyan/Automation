using UnityEngine;
using UnityEditor;
using TMPro;

public class EmojiMetricsFixer : EditorWindow
{
    private TMP_SpriteAsset spriteAsset;
    
    [Header("Glyph Metrics (Alignment)")]
    private float bearingY = 148f; 
    private float bearingX = 0f;
    private float extraSpacing = 0f;

    [Header("Glyph Rect (Texture Coordinates)")]
    private int rectWidth = 180;
    private int rectHeight = 180;
    private float targetWidth = 160f;
    private float targetHeight = 160f;
    
    [MenuItem("Tools/Emoji Metrics Fixer")]
    public static void ShowWindow() => GetWindow<EmojiMetricsFixer>("Emoji Fixer");

    private void OnGUI()
    {
        GUILayout.Label("Emoji Asset Global Fixer", EditorStyles.boldLabel);
        spriteAsset = (TMP_SpriteAsset)EditorGUILayout.ObjectField("Sprite Asset", spriteAsset, typeof(TMP_SpriteAsset), false);
        
        EditorGUILayout.Space();
        GUILayout.Label("Glyph Rect (Atlas Size)", EditorStyles.miniBoldLabel);
        rectWidth = EditorGUILayout.IntField("Rect W", rectWidth);
        rectHeight = EditorGUILayout.IntField("Rect H", rectHeight);

        EditorGUILayout.Space();
        GUILayout.Label("Glyph Metrics (Alignment & Space)", EditorStyles.miniBoldLabel);
        bearingX = EditorGUILayout.FloatField("BX (Horizontal)", bearingX);
        bearingY = EditorGUILayout.FloatField("BY (Vertical)", bearingY);
        targetWidth = EditorGUILayout.FloatField("Width (W)", targetWidth);
        targetHeight = EditorGUILayout.FloatField("Height (H)", targetHeight);
        extraSpacing = EditorGUILayout.FloatField("Extra Spacing", extraSpacing);

        if (GUILayout.Button("Apply to All Emojis") && spriteAsset != null)
        {
            FixMetrics();
        }
    }

    private void FixMetrics()
    {
        Undo.RecordObject(spriteAsset, "Fix Emoji Metrics and Rect");

        foreach (var glyph in spriteAsset.spriteGlyphTable)
        {
            // 1. Update Glyph Rect (The area on the texture)
            UnityEngine.TextCore.GlyphRect rect = glyph.glyphRect;
            rect.width = rectWidth;
            rect.height = rectHeight;
            glyph.glyphRect = rect;

            // 2. Update Glyph Metrics (The positioning in text)
            UnityEngine.TextCore.GlyphMetrics metrics = glyph.metrics;
            metrics.width = targetWidth;
            metrics.height = targetHeight;
            metrics.horizontalBearingX = bearingX; 
            metrics.horizontalBearingY = bearingY;
            
            // Advance = Width + Spacing
            metrics.horizontalAdvance = targetWidth + extraSpacing;

            glyph.metrics = metrics;
        }

        EditorUtility.SetDirty(spriteAsset);
        AssetDatabase.SaveAssets();
        Debug.Log($"Applied Rect ({rectWidth}x{rectHeight}) and Metrics to {spriteAsset.name}");
    }
}