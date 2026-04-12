using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Forces an immediate layout rebuild on this RectTransform (and all nested
/// ContentSizeFitter / LayoutGroup chains) whenever the GameObject becomes
/// active. Unity's layout system otherwise resolves nested CSF+LG chains across
/// multiple frames, which causes a visible "pop" on the first activation where
/// children settle into their final positions one frame after they appear.
///
/// Also runs once in Start via a delayed coroutine to handle the case where
/// this GameObject is active at scene load — in that case OnEnable fires before
/// the Canvas has finished initializing, so the rebuild computes against a
/// parent that hasn't been sized yet.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class LayoutRebuildOnEnable : MonoBehaviour
{
    private void OnEnable()
    {
        Rebuild();
        // Also schedule a deferred rebuild — Canvas/parent rects may not be
        // finalized on the same frame the object becomes active.
        if (isActiveAndEnabled)
        {
            StartCoroutine(RebuildNextFrame());
        }
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;          // wait one frame
        Rebuild();
        yield return new WaitForEndOfFrame();
        Rebuild();
    }

    private void Rebuild()
    {
        RectTransform rt = (RectTransform)transform;
        // Rebuild innermost CSFs first, then the root — matches Unity's layout order.
        ContentSizeFitter[] fitters = GetComponentsInChildren<ContentSizeFitter>(true);
        for (int i = fitters.Length - 1; i >= 0; i--)
        {
            if (fitters[i].transform is RectTransform childRt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(childRt);
            }
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }
}
