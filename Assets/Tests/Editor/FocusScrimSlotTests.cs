using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Automation.BotSettingsUI;

/// <summary>
/// Regression guard for FocusScrim's slot capture. The placeholder inserted
/// by Show() is created with default (centered) anchors; if its position is
/// read before a layout pass, the captured slot start is the middle of the
/// form and TrackSlot teleports the raised card on the next frame — which
/// yanks the card out from under the finger mid-tap, cancels the click, and
/// the keyboard never opens (real device bug, 2026-07-28: only Описание and
/// Часы работы could summon the keyboard).
/// </summary>
public class FocusScrimSlotTests
{
    private GameObject canvasRoot;

    [TearDown]
    public void TearDown()
    {
        if (canvasRoot != null) Object.DestroyImmediate(canvasRoot);
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void Show_CapturesRealSlot_NoFirstFrameJump()
    {
        // DOTween tween creation in edit mode can emit warnings; they are not
        // the behaviour under test.
        LogAssert.ignoreFailingMessages = true;

        var setup = BuildScrimWithScrollingForm();
        var restBottom = WorldBottom(setup.field);

        setup.scrim.Show(setup.field, null);

        // The placeholder must occupy the field's real slot at capture time…
        var placeholder = (RectTransform)GetPrivate(setup.scrim, "placeholder");
        Assert.IsNotNull(placeholder, "Show() did not create a placeholder");
        Assert.AreEqual(restBottom, WorldBottom(placeholder), 0.5f,
            "placeholder was not laid out into the field's slot before capture");

        // …so the first Update (keyboard down, no scroll) must not move the
        // raised layer. A jump here is the click-cancelling teleport.
        InvokePrivate(setup.scrim, "Update");
        Assert.AreEqual(0f, setup.raisedLayer.anchoredPosition.y, 0.5f,
            "raised layer jumped on the first frame after Show");
    }

    [Test]
    public void Show_OnNonScrollingForm_StillCapturesWithoutJump()
    {
        LogAssert.ignoreFailingMessages = true;

        var setup = BuildScrimWithScrollingForm(includeScrollRect: false);
        setup.scrim.Show(setup.field, null);

        InvokePrivate(setup.scrim, "Update");
        Assert.AreEqual(0f, setup.raisedLayer.anchoredPosition.y, 0.5f,
            "non-scrolling fallback lifted with the keyboard down");
    }

    // ---- fixture ---------------------------------------------------------

    private struct Setup
    {
        public FocusScrim scrim;
        public RectTransform field;
        public RectTransform raisedLayer;
    }

    private Setup BuildScrimWithScrollingForm(bool includeScrollRect = true)
    {
        canvasRoot = new GameObject("Canvas", typeof(Canvas));
        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var canvasRt = (RectTransform)canvasRoot.transform;
        canvasRt.sizeDelta = new Vector2(1080f, 1920f);

        // Tab (viewport) -> Content (VerticalLayoutGroup) -> 3 cards.
        var tab = NewRect("Tab", canvasRt);
        tab.sizeDelta = new Vector2(1080f, 1500f);
        var content = NewRect("Content", tab);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 30f;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        if (includeScrollRect)
        {
            var scroll = tab.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = tab;
            scroll.horizontal = false;
        }

        RectTransform field = null;
        for (var i = 0; i < 3; i++)
        {
            var card = NewRect($"Card{i}", content);
            card.sizeDelta = new Vector2(0f, 192f);
            if (i == 2) field = card; // bottom card — largest bogus jump pre-fix
        }

        var scrimVisual = new GameObject(
            "ScrimRoot", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        scrimVisual.transform.SetParent(canvasRt, false);

        var raisedLayer = NewRect("RaisedLayer", canvasRt);
        raisedLayer.anchorMin = Vector2.zero;
        raisedLayer.anchorMax = Vector2.one;
        raisedLayer.sizeDelta = Vector2.zero;

        var scrimHost = new GameObject("FocusScrim");
        scrimHost.transform.SetParent(canvasRt, false);
        var scrim = scrimHost.AddComponent<FocusScrim>();
        SetPrivate(scrim, "scrimRoot", scrimVisual);
        SetPrivate(scrim, "scrimGroup", scrimVisual.GetComponent<CanvasGroup>());
        SetPrivate(scrim, "scrimImage", scrimVisual.GetComponent<Image>());
        SetPrivate(scrim, "raisedLayer", raisedLayer);
        InvokePrivate(scrim, "Awake"); // resolves rootCanvas (no play mode here)

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        return new Setup { scrim = scrim, field = field, raisedLayer = raisedLayer };
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static float WorldBottom(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return corners[0].y;
    }

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(target, value);

    private static object GetPrivate(object target, string name) =>
        target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(target);

    private static void InvokePrivate(object target, string name) =>
        target.GetType()
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(target, null);
}
