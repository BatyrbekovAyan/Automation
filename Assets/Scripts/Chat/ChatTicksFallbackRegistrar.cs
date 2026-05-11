using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Registers the ChatTicks TMP_SpriteAsset as a fallback on the project's
/// default sprite asset (EmojiOne) at runtime startup. Without this, bare
/// `<sprite name="tick_sent">` / `tick_double` / `tick_double_blue` tags
/// emitted by ChatPreviewFormatter cannot be resolved — TMP's name-lookup
/// searches the current sprite asset → its fallbacks → the default sprite
/// asset → its fallbacks, and ChatTicks is not otherwise reachable from
/// EmojiOne (the project default).
///
/// Why runtime registration vs. modifying EmojiOne.asset on disk: keeping
/// EmojiOne.asset unmodified preserves the TextMesh Pro vendor folder as
/// reset-safe (TMP package updates won't conflict) and avoids tracking a
/// modified vendor asset in git for a single fallback entry.
///
/// Runs once per domain reload (Unity's RuntimeInitializeOnLoadMethod fires
/// at app start and after every script recompile in the editor).
/// </summary>
public static class ChatTicksFallbackRegistrar
{
    private const string ChatTicksResourcePath = "Sprite Assets/ChatTicks";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        var ticks = Resources.Load<TMP_SpriteAsset>(ChatTicksResourcePath);
        if (ticks == null)
        {
            Debug.LogWarning(
                $"[ChatTicksFallbackRegistrar] ChatTicks sprite asset not found at " +
                $"Resources/{ChatTicksResourcePath}. Run Tools → Chat List → " +
                $"Generate Tick Sprites to create it. Read-receipt ticks will not render.");
            return;
        }

        var defaultAsset = TMP_Settings.defaultSpriteAsset;
        if (defaultAsset == null)
        {
            Debug.LogWarning(
                "[ChatTicksFallbackRegistrar] TMP_Settings.defaultSpriteAsset is null; " +
                "cannot register ChatTicks as a fallback. Configure a default sprite " +
                "asset in TMP Settings or assign ChatTicks per-component.");
            return;
        }

        if (defaultAsset.fallbackSpriteAssets == null)
            defaultAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();

        if (!defaultAsset.fallbackSpriteAssets.Contains(ticks))
            defaultAsset.fallbackSpriteAssets.Add(ticks);
    }
}
