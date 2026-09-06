using System;
using System.Collections.Generic;

/// <summary>
/// One surface the system Back key can act on: how to tell it is open, and what Back does to it.
/// <see cref="Swallow"/> surfaces (a loading cover, the onboarding carousel) consume the press
/// without closing anything — Back must not tunnel through them to whatever lies beneath.
/// </summary>
public sealed class BackTarget
{
    public readonly string Name;
    public readonly Func<bool> IsOpen;
    public readonly Action Close;
    public readonly bool Swallow;

    public BackTarget(string name, Func<bool> isOpen, Action close, bool swallow = false)
    {
        Name = name;
        IsOpen = isOpen;
        Close = close;
        Swallow = swallow;
    }
}

/// <summary>
/// The Android Back rule, pure (UnityEngine-free; pinned by BackNavigationTests): walk the
/// surfaces TOP-MOST FIRST and act on the first one that is open. Exactly one surface reacts to
/// a press — the same contract as a tap on that surface's own chevron or scrim — and nothing
/// below it is asked. <see cref="AndroidBackRouter"/> supplies the ordered list from the live
/// scene and reads the key.
/// </summary>
public static class BackNavigation
{
    /// <summary>
    /// Returns the name of the surface that took the press (closed or swallowed), or null when
    /// none was open — the caller then treats the press as «leave the app».
    /// </summary>
    public static string Dispatch(IReadOnlyList<BackTarget> targets)
    {
        if (targets == null) return null;

        for (int i = 0; i < targets.Count; i++)
        {
            BackTarget t = targets[i];
            if (t == null || t.IsOpen == null) continue;

            bool open;
            try { open = t.IsOpen(); }
            catch (Exception) { open = false; }   // a torn-down surface never blocks the walk
            if (!open) continue;

            if (!t.Swallow) t.Close?.Invoke();
            return t.Name;
        }

        return null;
    }
}
