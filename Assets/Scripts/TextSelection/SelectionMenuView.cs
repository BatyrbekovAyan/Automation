using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// The floating iOS-style edit menu («Вырезать · Копировать · Вставить ·
/// Выделить всё»). Pure view: renders whichever items the policy allows and
/// reports taps; owns no clipboard/selection logic. Labels use the focused
/// field's own font so Cyrillic always renders.
///
/// Structure: root (positioning + drag) → Pill (flat Surface fill + items).
/// When the pill is wider than the screen it starts left-aligned (first
/// items visible) and can be dragged horizontally, clamped so the first and
/// last items are always reachable — iOS overflow behavior. A drag past the
/// threshold swallows the release-click so paging never triggers an action.
public class SelectionMenuView : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    const float Height = 104f;
    const float Radius = 52f;
    const float ItemPad = 24f;
    const float LabelSize = 36f;
    const float Gap = 44f;               // clears the start pin's dot above the line
    const float EdgeMargin = 24f;
    const float ClickSuppressPixels = 20f;

    public System.Action<SelectionMenuItems> ItemTapped;
    public bool IsVisible => gameObject.activeSelf;

    RectTransform _rt;
    RectTransform _pillRt;
    Image _bg;
    float _baseX;
    float _dragMinX;
    float _dragMaxX;
    float _dragOffset;
    bool _suppressNextClick;
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
        var go = new GameObject("SelectionMenu", typeof(RectTransform), typeof(SelectionMenuView));
        go.transform.SetParent(parent, false);
        var view = go.GetComponent<SelectionMenuView>();
        view._rt = (RectTransform)go.transform;

        var pillGo = new GameObject("Pill",
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        pillGo.transform.SetParent(go.transform, false);
        view._pillRt = (RectTransform)pillGo.transform;
        view._bg = pillGo.GetComponent<Image>();
        view._bg.sprite = null;
        pillGo.AddComponent<ImageWithRoundedCorners>().radius = Radius;

        var layout = pillGo.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = pillGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        view._pillRt.sizeDelta = new Vector2(0, Height);

        foreach (var (item, label) in Order)
        {
            if (view._entries.Count > 0) view.BuildHairline(pillGo.transform);
            view.BuildItem(pillGo.transform, item, label);
        }

        go.SetActive(false);
        return view;
    }

    void BuildHairline(Transform parent)
    {
        var sep = new GameObject("Hairline", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        sep.transform.SetParent(parent, false);
        var layoutElement = sep.GetComponent<LayoutElement>();
        layoutElement.preferredWidth = 2f;
        layoutElement.preferredHeight = Height * 0.5f;
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
        itemGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_suppressNextClick) { _suppressNextClick = false; return; }
            ItemTapped?.Invoke(captured);
        });
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
        TrimTrailingHairlines();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_pillRt);

        var parent = (RectTransform)_rt.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorTop, null, out var top);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenAnchorBottom, null, out var bottom);

        float pillHalf = _pillRt.rect.width / 2f;
        float usableHalf = parent.rect.width / 2f - EdgeMargin;
        if (pillHalf <= usableHalf)
        {
            _baseX = Mathf.Clamp(top.x, -usableHalf + pillHalf, usableHalf - pillHalf);
            _dragMinX = _dragMaxX = _baseX;   // fits — no paging
        }
        else
        {
            _baseX = -usableHalf + pillHalf;  // left-aligned: first item visible
            _dragMaxX = _baseX;
            _dragMinX = usableHalf - pillHalf; // dragged fully left: last item visible
        }
        _dragOffset = 0f;
        _suppressNextClick = false;

        float yAbove = top.y + Gap + Height / 2f;
        float y = (yAbove + Height / 2f + EdgeMargin > parent.rect.height / 2f)
            ? bottom.y - Gap - Height / 2f
            : yAbove;
        _rt.anchoredPosition = new Vector2(_baseX, y);
    }

    void TrimTrailingHairlines()
    {
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

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragMinX >= _dragMaxX) return;   // pill fits — nothing to reveal
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        _dragOffset += eventData.delta.x / scale;
        if (Mathf.Abs(_dragOffset) > ClickSuppressPixels) _suppressNextClick = true;
        float x = Mathf.Clamp(_baseX + _dragOffset, _dragMinX, _dragMaxX);
        _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);
    }

    public void Hide() => gameObject.SetActive(false);

    public void ApplyTheme()
    {
        var ink = Theme.Color(ThemeRole.InkPrimary);
        _bg.color = Theme.Color(ThemeRole.Surface);
        foreach (var entry in _entries)
        {
            if (entry.Label != null) entry.Label.color = ink;
            if (entry.Hairline != null) entry.Hairline.color = Theme.Color(ThemeRole.Hairline);
        }
    }
}
