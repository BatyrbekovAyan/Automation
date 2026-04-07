using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

[ExecuteAlways] 
public class MirrorSize : UIBehaviour
{
    [Tooltip("Drag your Outline GameObject here")]
    public RectTransform outlineRect;
    
    [Tooltip("How much bigger should the outline be? (e.g., 2 = 1px border on all sides)")]
    public float extraSize = 2f;

    public bool outgoing;

    private RectTransform myRect;

    protected override void OnEnable()
    {
        base.OnEnable();
        UpdateSize();
        
        // Fix for the First-Frame Layout Bug: Wait for the Layout Group to finish!
        if (Application.isPlaying)
        {
            StartCoroutine(UpdateAfterLayoutRoutine());
        }
    }

    // This native event keeps the Rounded Corners shader rendering correctly!
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        UpdateSize();
    }

    private IEnumerator UpdateAfterLayoutRoutine()
    {
        // Wait until the very end of the frame when all UI positions are final
        yield return new WaitForEndOfFrame();
        UpdateSize();
    }

    public void UpdateSize()
    {
        if (myRect == null) myRect = GetComponent<RectTransform>();
        
        if (outlineRect != null && myRect != null)
        {
            // 1. Match the exact physical width and height, plus your outline padding
            outlineRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, myRect.rect.width + extraSize);
            outlineRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, myRect.rect.height + extraSize);
            
            // 2. Your original working math for positioning
            outlineRect.localPosition = myRect.localPosition;
        }
    }
}