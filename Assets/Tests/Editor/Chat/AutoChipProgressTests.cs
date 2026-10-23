using DG.Tweening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// AutoChipProgress — the learning fill of an «Авто» capsule (spec 2026-10-22-learning-progress-percent §4.3) — and the shared
/// painter that drives it, ReplyModeToggleBinder.PaintChip. The capsule is built here the way the scene and the prefabs carry
/// it: a 190×76 Pill, its inner Fill inset 3 (184×70), a LearnClip (a RectMask2D pinned to the inner Fill's left edge) holding
/// the LearnFill, and the lamp, whose centre sits 20u in from the inner Fill's left edge. EditMode runs no layout pass, so every
/// position is set by hand.
///
/// DOTween does not tick in EditMode: a started tween leaves its target untouched until DOTween.CompleteAll. That is how
/// "animated" and "set at once" are told apart below. A growth tweens only from a percent the capsule already showed — its
/// first paint with one lands at once (spec §4.3) — so a test that wants a growth first paints a percent.
/// </summary>
public class AutoChipProgressTests
{
    private const float Inset = 3f;
    private const float InnerWidth = 184f;
    private const float InnerHeight = 70f;
    private const float LampCentreX = 20f;   // from the inner Fill's left edge
    private const float WidthTolerance = 0.01f;
    private const float ColourTolerance = 1e-4f;

    private ThemeAsset _theme;
    private GameObject _pill;
    private Image _ring;
    private Image _face;        // the capsule's inner Fill — PaintChip's `fill`, AutoChipProgress's `baseFill`
    private RectTransform _clip;
    private Image _learnFill;
    private Image _lampRing;
    private Image _lampCore;
    private AutoChipProgress _progress;

    [SetUp]
    public void SetUp()
    {
        _theme = ScriptableObject.CreateInstance<ThemeAsset>();   // code defaults: the light palette, every role distinct
        Theme.OverrideForTests(_theme, _theme, ThemeMode.Light);
        BuildCapsule();
    }

    [TearDown]
    public void TearDown()
    {
        DOTween.KillAll();
        Object.DestroyImmediate(_pill);
        Object.DestroyImmediate(_theme);
        Theme.ResetForTests();
        LogAssert.ignoreFailingMessages = false;
    }

    // ------------------------------------------------------------------------------------------------ the layer

    [Test]
    public void Apply_SizesTheClip_AndKeepsTheFillAtTheBaseSize()
    {
        var colour = new Color(0.15f, 0.21f, 0.37f, 1f);

        _progress.Apply(0.45f, colour, false, hasPercent: true);

        Assert.AreEqual(0.45f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "the clip is the fraction of the inner width");
        Assert.AreEqual(new Vector2(InnerWidth, InnerHeight), _learnFill.rectTransform.sizeDelta,
            "the fill never shrinks with the percent — its left end keeps the capsule's rounding, the clip cuts its right end");
        Assert.AreEqual(colour, _learnFill.color);
        Assert.AreEqual(0.45f, _progress.AppliedFraction);
    }

    [Test]
    public void Apply_Zero_HidesTheFill()
    {
        _progress.Apply(0.6f, Color.white, false, hasPercent: true);

        _progress.Apply(0f, Color.white, false, hasPercent: true);

        Assert.AreEqual(0f, _clip.sizeDelta.x, "a zero-width clip shows nothing");
        Assert.AreEqual(0f, _progress.AppliedFraction);
    }

    [Test]
    public void Apply_ClampsTheFraction_ToTheCapsule()
    {
        _progress.Apply(1.5f, Color.white, false, hasPercent: true);
        Assert.AreEqual(InnerWidth, _clip.sizeDelta.x, WidthTolerance, "never wider than the capsule");

        _progress.Apply(-0.2f, Color.white, false, hasPercent: true);
        Assert.AreEqual(0f, _clip.sizeDelta.x, "never a negative width");
    }

    [Test]
    public void Apply_AGrowthOnScreen_Tweens_ALowerFillLandsAtOnce()
    {
        LogAssert.ignoreFailingMessages = true;   // DOTween's first edit-mode use can log; not the behaviour under test
        _progress.Apply(0.2f, Color.white, false, hasPercent: true);   // a percent already shown: only that one grows smoothly

        _progress.Apply(0.6f, Color.white, true, hasPercent: true);
        Assert.AreEqual(0.2f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "a growth on screen is a tween, not a jump");
        Assert.AreEqual(0.6f, _progress.AppliedFraction, "the target is known at once");
        DOTween.CompleteAll();
        Assert.AreEqual(0.6f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "…and the tween lands on it");

        _progress.Apply(0.3f, Color.white, true, hasPercent: true);
        Assert.AreEqual(0.3f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "a lower fill is set at once, even when asked to animate");
    }

    [Test]
    public void Apply_OnAHiddenCapsule_LandsAtOnce()
    {
        _progress.Apply(0.1f, Color.white, false, hasPercent: true);   // shown on screen first: on a visible capsule 0.5 would tween
        _pill.SetActive(false);

        _progress.Apply(0.5f, Color.white, true, hasPercent: true);

        Assert.AreEqual(0.5f * InnerWidth, _clip.sizeDelta.x, WidthTolerance,
            "nobody sees a hidden capsule grow — it shows the right fill the moment it appears");
    }

    [Test]
    public void Covers_TheLampsCentre_OnlyOnceTheFillReachesIt()
    {
        Assert.IsTrue(_progress.Covers(_lampCore.rectTransform, 0.45f), "45 % of 184 runs past a lamp 20u in");
        Assert.IsFalse(_progress.Covers(_lampCore.rectTransform, 0.05f), "5 % stops short of it");
        Assert.IsFalse(_progress.Covers(_lampCore.rectTransform, 0f), "no fill covers nothing");
        Assert.IsFalse(_progress.Covers(null, 1f));
    }

    // ---------------------------------------------------------------------------------------------- the painter

    [Test]
    public void PaintChip_DrivesTheLayer_WithTheLooksFractionAndColour()
    {
        ReplyModeToggleBinder.PaintChip(true, 30, _ring, _face, _progress, null, _lampRing, _lampCore, animate: false);

        Assert.AreEqual(0.30f * InnerWidth, _clip.sizeDelta.x, WidthTolerance);
        AssertColour(ThemeRole.LearnFillOn, _learnFill.color, "«Авто» on, learning: the dark-green fill…");
        AssertColour(ThemeRole.LearnTrackOn, _face.color, "…over the lighter green");
        AssertColour(ThemeRole.LearnTrackOn, _ring.color);
        AssertColour(ThemeRole.PositiveInk, _lampCore.color, "a solid lamp keeps its own colour over the fill");
    }

    [Test]
    public void PaintChip_AHollowLampsCore_TakesTheColourUnderIt()
    {
        ReplyModeToggleBinder.PaintChip(false, 45, _ring, _face, _progress, null, _lampRing, _lampCore, animate: false);
        AssertColour(ThemeRole.LearnFillOff, _lampCore.color, "the blue fill runs under the lamp — its hole shows the blue");
        AssertColour(ThemeRole.InkTertiary, _lampRing.color, "the ring of a hollow lamp is the lamp's own colour");

        ReplyModeToggleBinder.PaintChip(false, 5, _ring, _face, _progress, null, _lampRing, _lampCore, animate: false);
        AssertColour(ThemeRole.Surface, _lampCore.color, "the fill stops short of the lamp — its hole shows the face");
    }

    [Test]
    public void PaintChip_ALowerFill_SetsEveryColourAtOnce()
    {
        LogAssert.ignoreFailingMessages = true;
        ReplyModeToggleBinder.PaintChip(true, 99, _ring, _face, _progress, null, _lampRing, _lampCore, animate: false);

        ReplyModeToggleBinder.PaintChip(true, 100, _ring, _face, _progress, null, _lampRing, _lampCore, animate: true);

        // Trained: the fill layer goes and the face itself takes the fill's colour. A cross-fade would start the face from the
        // lighter track under a fill that is already gone — a flash. So nothing tweens: the final colours are there at once.
        Assert.AreEqual(0f, _clip.sizeDelta.x, "the layer is gone");
        AssertColour(ThemeRole.LearnFillOn, _face.color, "the face is its final colour at once");
        AssertColour(ThemeRole.LearnFillOn, _ring.color);
    }

    [Test]
    public void PaintChip_AnOrdinaryModeChange_StillCrossFades()
    {
        LogAssert.ignoreFailingMessages = true;
        ReplyModeToggleBinder.PaintChip(false, 45, _ring, _face, _progress, null, _lampRing, _lampCore, animate: false);

        ReplyModeToggleBinder.PaintChip(true, 45, _ring, _face, _progress, null, _lampRing, _lampCore, animate: true);

        AssertColour(ThemeRole.Surface, _face.color, "switching «Авто» on cross-fades, as it did before the percent");
        AssertColour(ThemeRole.LearnFillOff, _learnFill.color, "the fill changes colour with the rest of the capsule");
        DOTween.CompleteAll();
        AssertColour(ThemeRole.LearnTrackOn, _face.color);
        AssertColour(ThemeRole.LearnFillOn, _learnFill.color);
        Assert.AreEqual(0.45f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "the same percent, the same width");
    }

    [Test]
    public void PaintChip_WithoutTheLayer_PaintsAsBefore()
    {
        ReplyModeToggleBinder.PaintChip(false, 45, _ring, _face, null, null, _lampRing, _lampCore, animate: false);

        AssertColour(ThemeRole.Border, _ring.color);
        AssertColour(ThemeRole.Surface, _face.color);
        AssertColour(ThemeRole.Surface, _lampCore.color, "no layer: nothing but the face under a hollow lamp");
    }

    [Test]
    public void PaintChip_WithADestroyedLayer_PaintsAsBefore()
    {
        // A destroyed (or, in the Editor, an unassigned) layer is "null" only to Unity's ==, never to C#'s ?. — the painter
        // must ask Unity, or it would call into a dead component (an animated paint touches its gameObject and throws).
        LogAssert.ignoreFailingMessages = true;
        Object.DestroyImmediate(_progress);

        Assert.DoesNotThrow(() => ReplyModeToggleBinder.PaintChip(false, 45, _ring, _face, _progress, null, _lampRing, _lampCore,
            animate: true), "a dead layer is skipped, never called into");
        DOTween.CompleteAll();

        AssertColour(ThemeRole.Surface, _face.color);
        AssertColour(ThemeRole.Surface, _lampCore.color, "no live layer: nothing but the face under a hollow lamp");
    }

    // -------------------------------------------------- the first paint with a percent (spec §4.3 «при первой отрисовке — сразу»)

    [Test]
    public void PaintChip_TheFirstPercent_LandsAtOnce_EvenWhenAskedToAnimate()
    {
        LogAssert.ignoreFailingMessages = true;
        PaintOff(null, animate: false);   // «данных нет»: the capsule has never shown a percent

        PaintOff(45, animate: true);      // the first snapshot lands on a capsule that is on screen

        Assert.AreEqual(0.45f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "a first paint: the fill stands at 45 % at once, no sweep 0 → 45");
        AssertColour(ThemeRole.LearnFillOff, _learnFill.color, "…in its colour at once");
        AssertNothingTweens("the first paint with a percent");
        Assert.IsTrue(_progress.ShowsPercent, "from now on the capsule shows a percent");
    }

    [Test]
    public void PaintChip_APercentTheCapsuleAlreadyShowed_StillGrowsSmoothly()
    {
        LogAssert.ignoreFailingMessages = true;
        PaintOff(null, animate: false);
        PaintOff(45, animate: true);

        PaintOff(56, animate: true);

        Assert.AreEqual(0.45f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "45 → 56 % on screen is a tween, not a jump");
        Assert.IsTrue(DOTween.IsTweening(_clip), "the growth runs");
        DOTween.CompleteAll();
        Assert.AreEqual(0.56f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "…and lands on 56 %");
    }

    [Test]
    public void PaintChip_AfterTheConnectClear_TheAutoFaceIsAFirstPaintAgain()
    {
        LogAssert.ignoreFailingMessages = true;
        PaintOff(null, animate: false);
        PaintOff(45, animate: true);
        PaintOff(56, animate: true);
        DOTween.CompleteAll();

        _progress.Clear();   // Bot.PaintConnectPill: «Подключить» outranks the fill
        Assert.AreEqual(0f, _clip.sizeDelta.x, "«Подключить» shows no fill");
        Assert.IsFalse(_progress.ShowsPercent, "…and the capsule forgets its percent");

        PaintOff(56, animate: true);   // the «Авто» face is back

        Assert.AreEqual(0.56f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "set at once — no sweep 0 → 56 % on the capsule's return");
        AssertNothingTweens("the «Авто» face back from «Подключить»");
    }

    [Test]
    public void PaintChip_AfterANoDataPaint_ThePercentIsAFirstPaintAgain()
    {
        LogAssert.ignoreFailingMessages = true;
        PaintOff(45, animate: false);
        PaintOff(null, animate: false);   // «данных нет» (the chats header switched to a bot without learning data)
        Assert.IsFalse(_progress.ShowsPercent, "a «данных нет» paint forgets the percent");

        PaintOff(56, animate: true);

        Assert.AreEqual(0.56f * InnerWidth, _clip.sizeDelta.x, WidthTolerance, "set at once");
        AssertNothingTweens("the first percent after «данных нет»");
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private void PaintOff(int? percent, bool animate) =>
        ReplyModeToggleBinder.PaintChip(false, percent, _ring, _face, _progress, null, _lampRing, _lampCore, animate);

    /// <summary>No part of the capsule — the layer or the face, ring and lamp the painter colours — has a tween running.</summary>
    private void AssertNothingTweens(string what)
    {
        foreach (Object part in new Object[] { _clip, _learnFill, _face, _ring, _lampRing, _lampCore })
            Assert.IsFalse(DOTween.IsTweening(part), $"{what} is set at once, yet {part.name} tweens");
    }

    private static void AssertColour(ThemeRole role, Color actual, string message = null)
    {
        Color expected = Theme.Color(role);
        string what = $"{message ?? role.ToString()}: expected {role} {expected}, got {actual}";
        Assert.AreEqual(expected.r, actual.r, ColourTolerance, what);
        Assert.AreEqual(expected.g, actual.g, ColourTolerance, what);
        Assert.AreEqual(expected.b, actual.b, ColourTolerance, what);
        Assert.AreEqual(expected.a, actual.a, ColourTolerance, what);
    }

    private void BuildCapsule()
    {
        _pill = new GameObject("Pill", typeof(RectTransform));
        ((RectTransform)_pill.transform).sizeDelta = new Vector2(InnerWidth + 2f * Inset, InnerHeight + 2f * Inset);

        _ring = NewImage("Ring", _pill.transform);
        Stretch(_ring.rectTransform, 0f);
        _face = NewImage("Fill", _pill.transform);
        Stretch(_face.rectTransform, Inset);

        _clip = (RectTransform)new GameObject("LearnClip", typeof(RectTransform), typeof(RectMask2D)).transform;
        _clip.SetParent(_pill.transform, false);
        _clip.anchorMin = Vector2.zero;
        _clip.anchorMax = new Vector2(0f, 1f);
        _clip.pivot = new Vector2(0f, 0.5f);
        _clip.anchoredPosition = new Vector2(Inset, 0f);
        _clip.sizeDelta = new Vector2(0f, -2f * Inset);

        _learnFill = NewImage("LearnFill", _clip);
        RectTransform fill = _learnFill.rectTransform;
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 0.5f);
        fill.sizeDelta = new Vector2(10f, 10f);   // deliberately off: Apply keeps it at the inner Fill's size

        // Content/Lamp/Core in the real capsule, where a layout group seats the lamp; here it is seated by hand.
        _lampRing = NewImage("Lamp", _pill.transform);
        RectTransform lamp = _lampRing.rectTransform;
        lamp.anchorMin = lamp.anchorMax = new Vector2(0f, 0.5f);
        lamp.sizeDelta = new Vector2(18f, 18f);
        lamp.anchoredPosition = new Vector2(Inset + LampCentreX, 0f);
        _lampCore = NewImage("Core", lamp);
        Stretch(_lampCore.rectTransform, 4f);

        _progress = _pill.AddComponent<AutoChipProgress>();
        var so = new SerializedObject(_progress);
        Wire(so, "clip", _clip);
        Wire(so, "fill", _learnFill);
        Wire(so, "baseFill", _face.rectTransform);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);
        Assert.IsNotNull(property, $"AutoChipProgress.{field} — the layer builder wires it by this name");
        property.objectReferenceValue = value;
    }

    private static Image NewImage(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
