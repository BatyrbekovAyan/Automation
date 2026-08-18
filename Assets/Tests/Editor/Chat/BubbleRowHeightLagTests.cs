using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the message-row height chain behind the reply-bubble artifacts reported 2026-08-18
/// ("some bubbles stick to the next message", "the tail slides off the corner").
///
/// The thread nests three size-driven layers:
///     Content (VerticalLayoutGroup, childControlHeight = false, spacing 10)
///       -> Row    (HorizontalLayoutGroup + ContentSizeFitter)   = MessageText{Incoming,Outgoing} root
///         -> Bubble (VerticalLayoutGroup + ContentSizeFitter)
///
/// uGUI's <c>HorizontalOrVerticalLayoutGroup.GetChildSizes</c> reads <c>child.sizeDelta[axis]</c>
/// when childControlHeight is FALSE — the child's size from the PREVIOUS pass — and
/// <c>LayoutRebuilder.PerformLayoutControl</c> runs the Row's ContentSizeFitter (an
/// ILayoutSelfController) BEFORE descending into the Bubble's own fitter. So with the row not
/// controlling the bubble's height, one layout pass sizes the row from the bubble's STALE height,
/// and the correction never arrives on its own: a dirty raised during a layout pass is swallowed by
/// CanvasUpdateRegistry's dedup (the queue already contains that layout root and is cleared at the
/// end of the update).
///
/// Row too short  => the bubble overflows past the row into the list's 10px inter-row spacing, so
///                   two bubbles read as glued together.
/// Row too tall   => Tail/TailOutline — ignoreLayout, anchored to the ROW's bottom edge — hang below
///                   the bubble's corner, so the tail looks detached.
///
/// The fix is to let the row control the bubble's height, which makes the row read the bubble's
/// freshly computed preferred height in the same pass. <see cref="MessagePrefabRootControlsBubbleHeight"/>
/// pins that on the real prefabs; the cases below prove it is what removes the lag.
/// </summary>
public class BubbleRowHeightLagTests
{
    private GameObject _canvasGo;
    private RectTransform _row, _bubble;
    private LayoutElement _payload;

    [TearDown]
    public void TearDown()
    {
        if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
    }

    /// <summary>Builds the real Content -> Row -> Bubble chain, mirroring Main.unity + the prefabs.</summary>
    private void BuildChain(bool rowControlsBubbleHeight)
    {
        _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
        var canvasRt = (RectTransform)_canvasGo.transform;
        canvasRt.sizeDelta = new Vector2(1080f, 1920f);

        // Main.unity -> MessagesPanel/.../Content
        var content = NewRect("Content", canvasRt);
        var contentVlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentVlg.spacing = 10f;
        contentVlg.childControlWidth = true;
        contentVlg.childControlHeight = false;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // MessageTextIncoming / MessageTextOutgoing root
        _row = NewRect("Row", content);
        var rowHlg = _row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHlg.childControlWidth = false;
        rowHlg.childControlHeight = rowControlsBubbleHeight;
        rowHlg.childForceExpandWidth = false;
        rowHlg.childForceExpandHeight = false;
        var rowFitter = _row.gameObject.AddComponent<ContentSizeFitter>();
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Bubble
        _bubble = NewRect("Bubble", _row);
        var bubbleVlg = _bubble.gameObject.AddComponent<VerticalLayoutGroup>();
        bubbleVlg.padding = new RectOffset(8, 8, 8, 12);
        bubbleVlg.spacing = 5f;
        bubbleVlg.childControlWidth = true;
        bubbleVlg.childControlHeight = true;
        bubbleVlg.childForceExpandWidth = false;
        bubbleVlg.childForceExpandHeight = false;
        var bubbleFitter = _bubble.gameObject.AddComponent<ContentSizeFitter>();
        bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Stands in for QuotedCard + body text — the content whose height changes when an async
        // quote resolves from the "Message" placeholder to a real sender row + snippet.
        _payload = NewRect("Payload", _bubble).gameObject.AddComponent<LayoutElement>();
        _payload.preferredWidth = 400f;
        _payload.preferredHeight = 60f;

        _content = content;
    }

    private RectTransform _content;

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private void RebuildOnce() => LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

    // --- The lag, and its absence once the row controls the bubble's height ---

    [Test]
    public void FirstLayout_RowMatchesBubble_WhenItControlsHeight()
    {
        BuildChain(rowControlsBubbleHeight: true);
        RebuildOnce();
        Assert.AreEqual(_bubble.rect.height, _row.rect.height, 0.01f,
            "A freshly laid-out row must be exactly as tall as the bubble it wraps.");
    }

    [Test]
    public void FirstLayout_RowMisreadsBubble_WhenItDoesNotControlHeight()
    {
        BuildChain(rowControlsBubbleHeight: false);
        RebuildOnce();
        Assert.AreNotEqual(_bubble.rect.height, _row.rect.height,
            "Documents the defect: on the very first pass the row is sized from the bubble's " +
            "untouched RectTransform (Unity's default 100u), not from its content.");
    }

    [Test]
    public void BubbleGrows_RowLagsOnlyWhenItDoesNotControlHeight()
    {
        BuildChain(rowControlsBubbleHeight: false);
        RebuildOnce();
        float bubbleBeforeGrowth = _bubble.rect.height;

        _payload.preferredHeight = 260f;   // quote resolves: placeholder -> sender row + snippet
        RebuildOnce();

        Assert.AreEqual(bubbleBeforeGrowth, _row.rect.height, 0.01f,
            "Documents the defect: with childControlHeight = false the row reads the bubble's " +
            "sizeDelta from the PREVIOUS pass, so it lands exactly one pass behind — here on the " +
            "height the bubble had before the quote resolved.");
        Assert.Greater(_bubble.rect.height - _row.rect.height, 1f,
            "…and the bubble now overflows the row, eating the 10px inter-row spacing.");
    }

    [Test]
    public void BubbleGrows_RowKeepsUpWhenItControlsHeight()
    {
        BuildChain(rowControlsBubbleHeight: true);
        RebuildOnce();

        _payload.preferredHeight = 260f;
        RebuildOnce();

        Assert.AreEqual(_bubble.rect.height, _row.rect.height, 0.01f,
            "Bubble grew: a row left at the old height lets the bubble overflow into the inter-row " +
            "spacing, which is what makes two bubbles look glued together.");
    }

    [Test]
    public void BubbleShrinks_RowKeepsUpWhenItControlsHeight()
    {
        BuildChain(rowControlsBubbleHeight: true);
        _payload.preferredHeight = 260f;
        RebuildOnce();

        _payload.preferredHeight = 60f;
        RebuildOnce();

        Assert.AreEqual(_bubble.rect.height, _row.rect.height, 0.01f,
            "Bubble shrank: a row left at the old height drops the tail — ignoreLayout, anchored to " +
            "the ROW's bottom edge — below the bubble's corner.");
    }

    [Test]
    public void BubbleNeverOverflowsRow_SoTheInterRowGapSurvives()
    {
        BuildChain(rowControlsBubbleHeight: true);
        RebuildOnce();
        _payload.preferredHeight = 260f;
        RebuildOnce();

        float overflow = _bubble.rect.height - _row.rect.height;
        Assert.LessOrEqual(overflow, 0.01f,
            $"Bubble overflows the row by {overflow}px, eating the spacing between messages.");
    }

    // --- The fix itself lives in the prefabs; pin it so a re-save cannot silently drop it ---

    [Test]
    public void MessagePrefabRootControlsBubbleHeight(
        [Values("Assets/Prefabs/MessageTextIncoming.prefab", "Assets/Prefabs/MessageTextOutgoing.prefab")]
        string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.IsNotNull(prefab, $"{prefabPath} not found.");

        var hlg = prefab.GetComponent<HorizontalLayoutGroup>();
        Assert.IsNotNull(hlg, "The message row root must keep its HorizontalLayoutGroup.");
        Assert.IsTrue(hlg.childControlHeight,
            "childControlHeight must stay ON: with it off the row reads the bubble's stale " +
            "sizeDelta and lags a full layout pass behind it (glued bubbles / detached tail).");
        Assert.IsFalse(hlg.childForceExpandHeight,
            "childForceExpandHeight must stay OFF, or the bubble stretches to the row instead of " +
            "hugging its content.");
        Assert.IsFalse(hlg.childControlWidth,
            "Width stays self-fit — the bubble's own ContentSizeFitter owns it (max-width clamps " +
            "in MessageItemView measure against that).");
    }
}
