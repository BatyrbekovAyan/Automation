using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;

// Allow the editor test assembly to call internal test helpers (Reset, BuildFromNames).
// Without .asmdef files Unity compiles runtime scripts into Assembly-CSharp and
// editor/test scripts into Assembly-CSharp-Editor — different assemblies, so
// internal visibility does not cross the boundary without this attribute.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

/// <summary>
/// Tracks which TMP sprite names are known (in atlas), pending (fetch in flight),
/// or failed (last fetch attempt failed). Built once at startup by EmojiPatchService.
/// </summary>
public static class EmojiSpriteRegistry
{
    private static readonly HashSet<string> _known   = new HashSet<string>();
    private static readonly HashSet<string> _pending = new HashSet<string>();
    private static readonly HashSet<string> _failed  = new HashSet<string>();

    /// <summary>Build the known set from all loaded TMP sprite assets.</summary>
    public static void Build(IEnumerable<TMP_SpriteAsset> assets)
    {
        var names = new List<string>();
        foreach (var asset in assets)
        {
            if (asset?.spriteCharacterTable == null) continue;
            foreach (var ch in asset.spriteCharacterTable)
                if (!string.IsNullOrEmpty(ch.name))
                    names.Add(ch.name);
        }
        BuildFromNames(names);
    }

    /// <summary>Build the known set directly from names. Used by unit tests.</summary>
    internal static void BuildFromNames(IEnumerable<string> names)
    {
        _known.Clear();
        _pending.Clear();
        _failed.Clear();
        foreach (var n in names)
            _known.Add(n);
    }

    public static bool IsKnown(string name)   => _known.Contains(name);
    public static bool IsPending(string name) => _pending.Contains(name);
    public static bool IsFailed(string name)  => _failed.Contains(name);

    /// <summary>Mark a name as fetch-in-flight. Clears any prior failed state.</summary>
    public static void MarkPending(string name)
    {
        _pending.Add(name);
        _failed.Remove(name);
    }

    /// <summary>Move a name from pending to known once the sprite is registered.</summary>
    public static void Register(string name)
    {
        _known.Add(name);
        _pending.Remove(name);
        _failed.Remove(name);
    }

    /// <summary>Record a fetch failure. Clears pending so a retry is possible next encounter.</summary>
    public static void MarkFailed(string name)
    {
        _pending.Remove(name);
        _failed.Add(name);
    }

    /// <summary>Clear a failed state so RequestEmoji will re-queue this name.</summary>
    public static void ClearFailed(string name) => _failed.Remove(name);

    /// <summary>Reset all state. Used by unit tests.</summary>
    internal static void Reset()
    {
        _known.Clear();
        _pending.Clear();
        _failed.Clear();
    }
}
