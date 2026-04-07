using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// This tells Unity: "You can only attach this script to an object that has a ScrollRect!"
[RequireComponent(typeof(ScrollRect))]
public class ScrollClickBlocker : MonoBehaviour
{
    private ScrollRect scrollRect;
    
    // A global flag that any button in your app can ask!
    public static bool IsBlocking { get; private set; } 
    
    private float lastFrameVelocity = 0f;

    void Awake()
    {
        // Automatically grab the ScrollRect on this object
        scrollRect = GetComponent<ScrollRect>();
    }

    void Update()
    {
        // 1. On the exact frame the finger hits the glass...
        if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
        {
            // 2. Was it flying now, or flying one frame ago?
            if (scrollRect != null && (scrollRect.velocity.sqrMagnitude > 900f || lastFrameVelocity > 900f))
            {
                IsBlocking = true; // They caught a spinning list!
            }
            else
            {
                IsBlocking = false; // Peaceful, normal tap.
            }
        }

        // 3. Always record the speed for the next frame
        if (scrollRect != null)
        {
            lastFrameVelocity = scrollRect.velocity.sqrMagnitude;
        }
    }
}