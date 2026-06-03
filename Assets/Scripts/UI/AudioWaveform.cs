using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Decorative voice/audio waveform. Generates bars at runtime from
/// AudioBubbleMath.BarHeights and supports tap/drag-to-seek. The prefab only
/// needs this component on a stretched RectTransform — bars are created here.
/// Lives inside the chat ScrollRect: vertical drags are forwarded to the parent
/// scroll so the message list still scrolls; only horizontal drags scrub.
/// </summary>
public class AudioWaveform : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform barsContainer;  // also the raycast/seek rect (defaults to own transform)
    [SerializeField] private Sprite barSprite;             // optional rounded sprite; null = sharp bars
    [SerializeField] private int barCount = 32;
    [SerializeField] private float barSpacing = 2f;
    [SerializeField] private Color playedColor   = new Color32(0x12, 0x8C, 0x7E, 0xFF);
    [SerializeField] private Color unplayedColor = new Color32(0xC6, 0xCD, 0xCB, 0xFF);

    /// Fired on pointer-up/tap (or scrub end) with the seek fraction (0..1). Plain
    /// delegate (not event) so a pooled list item can reassign it cleanly on rebind.
    public Action<float> OnSeek;

    private readonly List<Image> _bars = new List<Image>();
    private float[] _heights = Array.Empty<float>();
    private float _progress;
    private ScrollRect _parentScroll;
    private bool _scrubbing;     // local horizontal scrub in progress
    private bool _routeToParent; // forwarding a vertical drag to the chat ScrollRect

    public bool IsDragging => _scrubbing;

    void Awake()
    {
        if (barsContainer == null) barsContainer = (RectTransform)transform;
        _parentScroll = GetComponentInParent<ScrollRect>();

        // Pointer events require a raycast-target Graphic on this object.
        var raycaster = GetComponent<Image>();
        if (raycaster == null) raycaster = gameObject.AddComponent<Image>();
        raycaster.color = new Color(0f, 0f, 0f, 0f);
        raycaster.raycastTarget = true;
    }

    public void SetSeed(string messageId)
    {
        _heights = AudioBubbleMath.BarHeights(messageId, barCount);
        EnsureBars(_heights.Length);
        LayoutBars();
        _progress = 0f;
        _scrubbing = false;
        _routeToParent = false;
        ApplyColors();
    }

    public void SetProgress(float fraction)
    {
        _progress = Mathf.Clamp01(fraction);
        ApplyColors();
    }

    private void EnsureBars(int count)
    {
        while (_bars.Count < count)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(barsContainer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (barSprite != null) { img.sprite = barSprite; img.type = Image.Type.Sliced; }
            _bars.Add(img);
        }
        for (int i = 0; i < _bars.Count; i++)
            _bars[i].gameObject.SetActive(i < count);
    }

    private void LayoutBars()
    {
        int total = _heights.Length;
        int n = Mathf.Min(total, _bars.Count);
        for (int i = 0; i < n; i++)
        {
            float x0 = i / (float)total;
            float x1 = (i + 1) / (float)total;
            float hFrac = Mathf.Clamp01(_heights[i]);

            var rt = _bars[i].rectTransform;
            rt.anchorMin = new Vector2(x0, 0.5f - hFrac * 0.5f);
            rt.anchorMax = new Vector2(x1, 0.5f + hFrac * 0.5f);
            rt.offsetMin = new Vector2(barSpacing * 0.5f, 0f);
            rt.offsetMax = new Vector2(-barSpacing * 0.5f, 0f);
        }
    }

    private void ApplyColors()
    {
        int n = Mathf.Min(_heights.Length, _bars.Count);
        int played = AudioBubbleMath.PlayedBarCount(_progress, n);
        for (int i = 0; i < n; i++)
            _bars[i].color = i < played ? playedColor : unplayedColor;
    }

    // --- Pointer / drag -------------------------------------------------------

    public void OnPointerDown(PointerEventData e) { /* classified on pointer-up / begin-drag */ }

    public void OnPointerUp(PointerEventData e)
    {
        if (_scrubbing || _routeToParent) return; // a drag already handled it
        UpdateFromPointer(e);                      // pure tap-to-seek
        OnSeek?.Invoke(_progress);
    }

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        if (_parentScroll != null) _parentScroll.OnInitializePotentialDrag(e);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 d = e.position - e.pressPosition;
        if (Mathf.Abs(d.y) > Mathf.Abs(d.x))
        {
            _routeToParent = true;                 // vertical => let the chat list scroll
            if (_parentScroll != null) _parentScroll.OnBeginDrag(e);
        }
        else
        {
            _scrubbing = true;                     // horizontal => scrub
            UpdateFromPointer(e);
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { if (_parentScroll != null) _parentScroll.OnDrag(e); return; }
        if (_scrubbing) UpdateFromPointer(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent)
        {
            if (_parentScroll != null) _parentScroll.OnEndDrag(e);
            _routeToParent = false;
            return;
        }
        if (_scrubbing)
        {
            UpdateFromPointer(e);
            _scrubbing = false;
            OnSeek?.Invoke(_progress);
        }
    }

    private void UpdateFromPointer(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                barsContainer, e.position, e.pressEventCamera, out var local))
            return;
        float w = barsContainer.rect.width;
        if (w <= 0f) return;
        SetProgress((local.x - barsContainer.rect.xMin) / w);
    }
}
