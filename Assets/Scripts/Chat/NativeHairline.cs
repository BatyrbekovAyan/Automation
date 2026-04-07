using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(RectTransform))]
public class NativeHairline : MonoBehaviour
{
    private RectTransform rect;
    private Canvas canvas;

    void OnEnable()
    {
        rect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        ApplyHairline();
    }

    // Also update if the screen rotates or changes size
    void OnRectTransformDimensionsChange()
    {
        ApplyHairline();
    }

    void ApplyHairline()
    {
        if (canvas == null || rect == null) return;
        
        // WhatsApp Math: Force the line to be exactly 1 hardware pixel thick, 
        // completely ignoring the Canvas Scaler's stretching!
        float hardwarePixelSize = 1f / canvas.scaleFactor;
        
        // Apply the mathematically perfect height
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, hardwarePixelSize);
    }
}