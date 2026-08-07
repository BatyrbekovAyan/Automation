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
    public const float HeadSize = 48f;
    public const float StemWidth = 6f;

    public bool IsStart { get; private set; }
    public System.Action<SelectionHandleView, Vector2> DragMoved;
    public System.Action<SelectionHandleView> DragEnded;

    Image _stem;
    Image _head;

    public static SelectionHandleView Build(Transform parent, bool isStart)
    {
        var go = new GameObject(isStart ? "HandleStart" : "HandleEnd",
            typeof(RectTransform), typeof(Image), typeof(SelectionHandleView));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(HitSize, HitSize);

        var hit = go.GetComponent<Image>();
        hit.color = Color.clear;           // raycast target, invisible
        hit.raycastTarget = true;

        var view = go.GetComponent<SelectionHandleView>();
        view.IsStart = isStart;

        view._stem = NewChildImage(go.transform, "Stem", new Vector2(StemWidth, 64f));
        view._head = NewChildImage(go.transform, "Head", new Vector2(HeadSize, HeadSize));
        view._head.gameObject.AddComponent<ImageWithRoundedCorners>().radius = HeadSize / 2f;

        float dir = isStart ? 1f : -1f;   // start: head above the line, end: below
        ((RectTransform)view._head.transform).anchoredPosition = new Vector2(0, dir * (32f + HeadSize / 2f));

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

    public void SetStemHeight(float h) =>
        ((RectTransform)_stem.transform).sizeDelta = new Vector2(StemWidth, Mathf.Max(24f, h));

    public void OnBeginDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnDrag(PointerEventData e) => DragMoved?.Invoke(this, e.position);
    public void OnEndDrag(PointerEventData e) => DragEnded?.Invoke(this);
}
