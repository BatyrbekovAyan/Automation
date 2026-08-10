using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// One iOS-style selection pin: invisible 132-unit hit area, visible
/// 6-unit stem + 48-unit circular head. Start pin renders the head above
/// the line (stem up), end pin below. Pure view: reports drags, owns no
/// selection logic.
public class SelectionHandleView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const float HitSize = 132f;
    public const float HeadSize = 30f;   // WhatsApp-style dot, not a lollipop head
    public const float StemWidth = 5f;

    public bool IsStart { get; private set; }
    public System.Action<SelectionHandleView, Vector2> DragMoved;
    public System.Action<SelectionHandleView> DragEnded;

    Image _stem;
    Image _head;
    float _dir;                          // +1 head above the line (start), -1 below (end)

    public static SelectionHandleView Build(Transform parent, bool isStart)
    {
        // Root carries NO Image — it only positions. (An Image here renders an
        // opaque white square: the hit area lives on the Hit child below.)
        var go = new GameObject(isStart ? "HandleStart" : "HandleEnd",
            typeof(RectTransform), typeof(SelectionHandleView));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(HitSize, HitSize);

        var view = go.GetComponent<SelectionHandleView>();
        view.IsStart = isStart;
        view._dir = isStart ? 1f : -1f;   // start: head above the line, end: below

        // Asymmetric grab zone (iOS anatomy): the start pin's touch area
        // shifts UP toward its dot, the end pin's DOWN — so two pins on a
        // short selection never cover each other's hit areas.
        var hitGo = new GameObject("Hit", typeof(RectTransform), typeof(Image));
        hitGo.transform.SetParent(go.transform, false);
        var hitRt = (RectTransform)hitGo.transform;
        hitRt.sizeDelta = new Vector2(HitSize, HitSize);
        hitRt.anchoredPosition = new Vector2(0, view._dir * 44f);
        var hitImg = hitGo.GetComponent<Image>();
        hitImg.color = Color.clear;        // raycast target, invisible
        hitImg.sprite = null;
        hitImg.raycastTarget = true;

        view._stem = NewChildImage(go.transform, "Stem", new Vector2(StemWidth, 64f));
        view._head = NewChildImage(go.transform, "Head", new Vector2(HeadSize, HeadSize));
        view._head.gameObject.AddComponent<ImageWithRoundedCorners>().radius = HeadSize / 2f;
        view.SetStemHeight(64f);
        view.SetColor(Theme.Color(ThemeRole.AccentFill));   // no white frame before the first reposition

        go.SetActive(false);
        return view;
    }

    static Image NewChildImage(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = null;                 // project convention: null sprite + RoundedCorners
        img.raycastTarget = false;
        return img;
    }

    public void SetColor(Color c) { _stem.color = c; _head.color = c; }

    public void SetStemHeight(float h)
    {
        float height = Mathf.Max(24f, h);
        ((RectTransform)_stem.transform).sizeDelta = new Vector2(StemWidth, height);
        // The dot hugs its line end (iOS/WhatsApp anatomy) whatever the
        // field's line height is.
        ((RectTransform)_head.transform).anchoredPosition =
            new Vector2(0, _dir * (height / 2f + HeadSize / 2f - 3f));
    }

    public void OnBeginDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnEndDrag(PointerEventData e) => DragEnded?.Invoke(this);
}
