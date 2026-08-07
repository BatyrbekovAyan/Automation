using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// The floating iOS-style edit menu («Вырезать · Копировать · Вставить ·
/// Выделить всё»). Pure view: renders whichever items the policy allows and
/// reports taps; owns no clipboard/selection logic. Labels use the focused
/// field's own font so Cyrillic always renders.
public class SelectionMenuView : MonoBehaviour
{
    const float Height = 120f;
    const float Radius = 60f;
    const float ItemPad = 40f;
    const float LabelSize = 44f;
    const float Gap = 24f;
    const float EdgeMargin = 24f;

    public System.Action<SelectionMenuItems> ItemTapped;
    public bool IsVisible => gameObject.activeSelf;

    RectTransform _rt;
    Image _bg;
    readonly List<Entry> _entries = new List<Entry>();

    struct Entry
    {
        public SelectionMenuItems Item;   // None = hairline separator
        public GameObject Root;
        public TMP_Text Label;
        public Image Hairline;
    }

    static readonly (SelectionMenuItems item, string label)[] Order =
    {
        (SelectionMenuItems.Cut, "Вырезать"),
        (SelectionMenuItems.Copy, "Копировать"),
        (SelectionMenuItems.Paste, "Вставить"),
        (SelectionMenuItems.SelectAll, "Выделить всё"),
    };

    public static SelectionMenuView Build(RectTransform parent)
    {
        var go = new GameObject("SelectionMenu",
            typeof(RectTransform), typeof(Image), typeof(SelectionMenuView),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        var view = go.GetComponent<SelectionMenuView>();
        view._rt = (RectTransform)go.transform;
        view._bg = go.GetComponent<Image>();
        view._bg.sprite = null;
        go.AddComponent<ImageWithRoundedCorners>().radius = Radius;

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        view._rt.sizeDelta = new Vector2(0, Height);

        foreach (var (item, label) in Order)
        {
            if (view._entries.Count > 0) view.BuildHairline(go.transform);
            view.BuildItem(go.transform, item, label);
        }

        go.SetActive(false);
        return view;
    }

    void BuildHairline(Transform parent)
    {
        var sep = new GameObject("Hairline", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        sep.transform.SetParent(parent, false);
        var le = sep.GetComponent<LayoutElement>();
        le.preferredWidth = 2f;
        le.preferredHeight = Height * 0.55f;
        var img = sep.GetComponent<Image>();
        img.sprite = null;
        img.raycastTarget = false;
        _entries.Add(new Entry { Item = SelectionMenuItems.None, Root = sep, Hairline = img });
    }

    void BuildItem(Transform parent, SelectionMenuItems item, string label)
    {
        var itemGo = new GameObject(item.ToString(),
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        itemGo.transform.SetParent(parent, false);
        itemGo.GetComponent<Image>().color = Color.clear;
        itemGo.GetComponent<LayoutElement>().preferredHeight = Height;
        var itemLayout = itemGo.GetComponent<HorizontalLayoutGroup>();
        itemLayout.childControlWidth = true;
        itemLayout.childControlHeight = true;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        labelGo.transform.SetParent(itemGo.transform, false);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = LabelSize;
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.margin = new Vector4(ItemPad, 0, ItemPad, 0);
        labelGo.GetComponent<LayoutElement>().preferredHeight = Height;

        var captured = item;
        itemGo.GetComponent<Button>().onClick.AddListener(() => ItemTapped?.Invoke(captured));
        _entries.Add(new Entry { Item = item, Root = itemGo, Label = tmp });
    }

    public void Show(SelectionMenuItems items, Vector2 screenAnchorTop, Vector2 screenAnchorBottom, TMP_FontAsset font)
    {
        if (items == SelectionMenuItems.None) { Hide(); return; }
        gameObject.SetActive(true);
        ApplyTheme();

        bool previousWasVisibleItem = false;
        foreach (var entry in _entries)
        {
            if (entry.Item == SelectionMenuItems.None)
            {
                entry.Root.SetActive(previousWasVisibleItem);   // hairline only after a visible item
                previousWasVisibleItem = false;
                continue;
            }
            bool visible = (items & entry.Item) != 0;
            entry.Root.SetActive(visible);
            if (visible && font != null) entry.Label.font = font;
            if (visible) previousWasVisibleItem = true;
        }
        TrimLeadingAndTrailingHairlines();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);

        var parent = (RectTransform)_rt.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorTop, null, out var top);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorBottom, null, out var bottom);

        float halfWidth = _rt.rect.width / 2f;
        float x = Mathf.Clamp(top.x,
            -parent.rect.width / 2f + halfWidth + EdgeMargin,
            parent.rect.width / 2f - halfWidth - EdgeMargin);
        float yAbove = top.y + Gap + Height / 2f;
        float y = (yAbove + Height / 2f + EdgeMargin > parent.rect.height / 2f)
            ? bottom.y - Gap - Height / 2f
            : yAbove;
        _rt.anchoredPosition = new Vector2(x, y);
    }

    void TrimLeadingAndTrailingHairlines()
    {
        // A hairline whose FOLLOWING item is hidden must hide too (covers the
        // trailing edge and any run of hidden items).
        bool nextItemSeen = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Item == SelectionMenuItems.None)
            {
                if (!nextItemSeen) entry.Root.SetActive(false);
                nextItemSeen = false;
            }
            else if (entry.Root.activeSelf)
            {
                nextItemSeen = true;
            }
        }
    }

    public void Hide() => gameObject.SetActive(false);

    public void ApplyTheme()
    {
        _bg.color = Theme.Color(ThemeRole.Surface);
        foreach (var entry in _entries)
        {
            if (entry.Label != null) entry.Label.color = Theme.Color(ThemeRole.InkPrimary);
            if (entry.Hairline != null) entry.Hairline.color = Theme.Color(ThemeRole.Hairline);
        }
    }
}
