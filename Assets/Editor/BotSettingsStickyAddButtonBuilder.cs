#if UNITY_EDITOR
using Automation.BotSettingsUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor maintenance for BotSettings.prefab.
///
/// Pins the "+ Добавить товар" / "+ Добавить услугу" button to the bottom of
/// the Product and Service tabs so it stays visible when the card list grows
/// tall enough to scroll. Previously the button was a sibling of the list
/// inside the ScrollRect content and scrolled off-screen.
///
/// Target layout per tab:
///
///   Tab (RectTransform, Image, ScrollRect)
///     Viewport                                  ← bottom inset = FOOTER_HEIGHT
///       Content                                 ← list only
///         SectionHeader
///         ProductsParent | ServicesParent
///     StickyFooter                              ← NEW. Outside the ScrollRect.
///       Divider                                 ← 1 px hairline, top edge
///       AddProductButton | AddServiceButton     ← reparented, unchanged
///
/// The step is idempotent — safe to re-run once applied.
/// </summary>
public static class BotSettingsStickyAddButtonBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string FooterName = "StickyFooter";
    private const string DividerName = "Divider";
    private const string ViewportName = "Viewport";

    // Matches BotSettingsRebuilder's design-time scale so sizes align with
    // everything else built by that tool (e.g. the 52 px Add button body).
    private const float Scale = 2.5f;
    private const float FooterHeight = 76f * Scale;      // 52 button + 12 top + 12 bottom
    private const float ButtonHeight = 52f * Scale;
    private const float HorizontalPadding = 20f * Scale;

    private static readonly Color DividerColor = Hex("#E4E6EB"); // matches Rebuilder.Border

    [MenuItem("Tools/BotSettings/Pin Add Button To Bottom")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[BotSettings] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var modified = false;
            modified |= PinTab(prefabRoot, tabName: "Product");
            modified |= PinTab(prefabRoot, tabName: "Service");

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BotSettings] Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log("[BotSettings] Nothing to do — already pinned.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool PinTab(GameObject prefabRoot, string tabName)
    {
        var tabTransform = prefabRoot.transform.Find(tabName);
        if (tabTransform == null)
        {
            Debug.LogWarning($"[BotSettings] Tab '{tabName}' not found on prefab root.");
            return false;
        }

        var viewport = (RectTransform)tabTransform.Find(ViewportName);
        if (viewport == null)
        {
            Debug.LogWarning($"[BotSettings] '{tabName}/{ViewportName}' not found — skipping.");
            return false;
        }

        var addButton = FindAddButton(tabTransform, tabName);
        if (addButton == null)
        {
            Debug.LogWarning($"[BotSettings] Add button not found under '{tabName}' — skipping.");
            return false;
        }

        var modified = false;

        var footer = EnsureFooter(tabTransform, out var footerCreated);
        modified |= footerCreated;

        modified |= EnsureDivider(footer);
        modified |= ReparentButton(addButton, footer);
        modified |= ShrinkViewport(viewport);

        return modified;
    }

    private static AddItemButton FindAddButton(Transform tab, string tabName)
    {
        // Search anywhere under the tab — works both before (inside Content)
        // and after (inside StickyFooter) pinning. BotSettings has exactly
        // one AddItemButton per tab so the first match is always correct.
        foreach (var candidate in tab.GetComponentsInChildren<AddItemButton>(includeInactive: true))
            return candidate;

        Debug.LogWarning($"[BotSettings] No AddItemButton under tab '{tabName}'.");
        return null;
    }

    private static RectTransform EnsureFooter(Transform tab, out bool created)
    {
        var existing = tab.Find(FooterName);
        if (existing != null)
        {
            created = false;
            var rt = (RectTransform)existing;
            ApplyFooterGeometry(rt);
            return rt;
        }

        var go = new GameObject(FooterName, typeof(RectTransform));
        go.transform.SetParent(tab, worldPositionStays: false);
        var footerRt = (RectTransform)go.transform;
        ApplyFooterGeometry(footerRt);
        // Render after Viewport so the divider + button are not clipped by
        // any sibling drawn on top.
        footerRt.SetAsLastSibling();
        created = true;
        return footerRt;
    }

    private static void ApplyFooterGeometry(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, FooterHeight);
        rt.anchoredPosition = Vector2.zero;
    }

    private static bool EnsureDivider(RectTransform footer)
    {
        var existing = footer.Find(DividerName);
        if (existing != null)
        {
            var img = existing.GetComponent<Image>();
            var needsColor = img != null && img.color != DividerColor;
            if (needsColor) img.color = DividerColor;

            var rt = (RectTransform)existing;
            return ApplyDividerGeometry(rt) || needsColor;
        }

        var go = new GameObject(DividerName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(footer, worldPositionStays: false);
        go.GetComponent<Image>().color = DividerColor;

        var dividerRt = (RectTransform)go.transform;
        ApplyDividerGeometry(dividerRt);
        dividerRt.SetAsFirstSibling();
        return true;
    }

    private static bool ApplyDividerGeometry(RectTransform rt)
    {
        var anchorMin = new Vector2(0f, 1f);
        var anchorMax = new Vector2(1f, 1f);
        var pivot = new Vector2(0.5f, 1f);
        var sizeDelta = new Vector2(0f, 1f);
        var anchoredPos = Vector2.zero;

        var changed =
            rt.anchorMin != anchorMin ||
            rt.anchorMax != anchorMax ||
            rt.pivot != pivot ||
            rt.sizeDelta != sizeDelta ||
            rt.anchoredPosition != anchoredPos;

        if (!changed) return false;

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;
        return true;
    }

    private static bool ReparentButton(AddItemButton addButton, RectTransform footer)
    {
        var modified = false;
        var buttonRt = (RectTransform)addButton.transform;

        if (buttonRt.parent != footer)
        {
            buttonRt.SetParent(footer, worldPositionStays: false);
            modified = true;
        }

        var anchorMin = new Vector2(0f, 0.5f);
        var anchorMax = new Vector2(1f, 0.5f);
        var pivot = new Vector2(0.5f, 0.5f);
        var halfHeight = ButtonHeight * 0.5f;
        var offsetMin = new Vector2(HorizontalPadding, -halfHeight);
        var offsetMax = new Vector2(-HorizontalPadding, halfHeight);

        if (buttonRt.anchorMin != anchorMin ||
            buttonRt.anchorMax != anchorMax ||
            buttonRt.pivot != pivot ||
            buttonRt.offsetMin != offsetMin ||
            buttonRt.offsetMax != offsetMax)
        {
            buttonRt.anchorMin = anchorMin;
            buttonRt.anchorMax = anchorMax;
            buttonRt.pivot = pivot;
            buttonRt.offsetMin = offsetMin;
            buttonRt.offsetMax = offsetMax;
            modified = true;
        }

        // Footer always draws button on top of divider.
        var lastSibling = footer.childCount - 1;
        if (buttonRt.GetSiblingIndex() != lastSibling)
        {
            buttonRt.SetAsLastSibling();
            modified = true;
        }

        return modified;
    }

    private static bool ShrinkViewport(RectTransform viewport)
    {
        var desiredMin = new Vector2(viewport.offsetMin.x, FooterHeight);
        if (viewport.offsetMin == desiredMin) return false;

        viewport.offsetMin = desiredMin;
        return true;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }
}
#endif
