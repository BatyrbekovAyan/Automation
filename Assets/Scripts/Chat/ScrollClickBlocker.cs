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

    // The press frame the shared verdict was last started on. Two lists can be on screen at once (the «Личные» folder over
    // the chat list), and their blockers judge the same press in an order Unity does not fix.
    private static int judgedFrame = -1;

    /// <summary>
    /// One blocker's say on a press: the first to judge a press frame starts from «not blocking», and any list that was flying
    /// blocks — so a still list can never clear the catch of a flying one, whichever judges first.
    /// </summary>
    internal static bool Verdict(bool soFar, bool firstToJudge, bool flying) => (!firstToJudge && soFar) || flying;

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
            // 2. Was it flying now, or flying one frame ago? They caught a spinning list — or it is a peaceful, normal tap.
            bool flying = scrollRect != null && (scrollRect.velocity.sqrMagnitude > 900f || lastFrameVelocity > 900f);
            int frame = Time.frameCount;
            IsBlocking = Verdict(IsBlocking, judgedFrame != frame, flying);
            judgedFrame = frame;
        }

        // 3. Always record the speed for the next frame
        if (scrollRect != null)
        {
            lastFrameVelocity = scrollRect.velocity.sqrMagnitude;
        }
    }
}