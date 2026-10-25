using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pins where a chat row's swipe actions stand (<see cref="SwipeActionsLayout.Solve"/>) and how
/// <see cref="SwipeToDelete.SetActions"/> applies it: «Удалить» rightmost, the move button («В личные» / «В рабочие») on its
/// left, the reveal = the visible buttons' widths together, every visible button parked just past the row's right edge at
/// rest and flush against it once the row is fully open; no action — the swipe is off. And the rule the list's refreshes lean
/// on: the same actions leave an open row open, different ones snap it shut first.
///
/// The row is built in code. Nothing here starts a tween (SetActions snaps instantly, a drag places synchronously), so no
/// DOTween runs in EditMode.
/// </summary>
public class SwipeActionsLayoutTests
{
    private const float MoveWidth = 180f;     // not the delete's width, so a mix-up of the two shows
    private const float DeleteWidth = 200f;

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyWhatTheTestBuilt()
    {
        foreach (Object created in _created)
            if (created != null) Object.DestroyImmediate(created);
        _created.Clear();
    }

    // ---- the pure layout ----

    [Test]
    public void Solve_OneOrTwoButtons()
    {
        Assert.AreEqual((DeleteWidth, 0f, 0f), SwipeActionsLayout.Solve(false, true, MoveWidth, DeleteWidth),
            "«Удалить» alone stands flush with the edge");
        Assert.AreEqual((MoveWidth, 0f, 0f), SwipeActionsLayout.Solve(true, false, MoveWidth, DeleteWidth),
            "the move alone stands flush with the edge");
        Assert.AreEqual((MoveWidth + DeleteWidth, -DeleteWidth, 0f), SwipeActionsLayout.Solve(true, true, MoveWidth, DeleteWidth),
            "«Удалить» rightmost, the move right on its left; the row slides by both");
    }

    [Test]
    public void Solve_NoButtons_ZeroWidth()
    {
        Assert.AreEqual((0f, 0f, 0f), SwipeActionsLayout.Solve(false, false, MoveWidth, DeleteWidth), "nothing to reveal");
        Assert.AreEqual(0f, SwipeActionsLayout.Solve(true, false, -10f, DeleteWidth).revealWidth,
            "a width that is not there (a broken button) reveals nothing");
    }

    // ---- SwipeToDelete.SetActions ----

    [Test]
    public void SetActions_Both_StandRightToLeft_ParkedPastTheEdge()
    {
        SwipeToDelete swipe = Row(out RectTransform content, out RectTransform move, out RectTransform delete);

        swipe.SetActions(true, true);

        Assert.AreEqual(MoveWidth + DeleteWidth, swipe.RevealWidth, "the row slides by both buttons");
        Assert.IsTrue(move.gameObject.activeSelf, "the move button shows");
        Assert.IsTrue(delete.gameObject.activeSelf, "«Удалить» shows");
        Assert.IsTrue(swipe.enabled);
        Assert.AreEqual(0f, content.anchoredPosition.x, "shut");
        Assert.AreEqual(MoveWidth + DeleteWidth, delete.anchoredPosition.x, "«Удалить» parked outermost");
        Assert.AreEqual(MoveWidth, move.anchoredPosition.x, "the move between it and the row's right edge");
    }

    [Test]
    public void FullyOpen_DeleteMeetsTheEdge_MoveOnItsLeft()
    {
        SwipeToDelete swipe = Row(out RectTransform content, out RectTransform move, out RectTransform delete);
        swipe.SetActions(true, true);

        DragBy(swipe, -1000f);   // further than the reveal — the row stops there

        Assert.AreEqual(-(MoveWidth + DeleteWidth), content.anchoredPosition.x);
        Assert.AreEqual(0f, delete.anchoredPosition.x, "«Удалить» flush with the row's right edge");
        Assert.AreEqual(-DeleteWidth, move.anchoredPosition.x, "the move right on its left");
    }

    [Test]
    public void SetActions_OneButton_TakesTheEdge_TheOtherHides()
    {
        SwipeToDelete swipe = Row(out _, out RectTransform move, out RectTransform delete);

        swipe.SetActions(true, false);   // a Telegram working chat, or a personal chat
        Assert.AreEqual(MoveWidth, swipe.RevealWidth);
        Assert.IsTrue(move.gameObject.activeSelf);
        Assert.IsFalse(delete.gameObject.activeSelf, "no «Удалить» to reveal");
        Assert.AreEqual(MoveWidth, move.anchoredPosition.x, "parked just past the edge");

        swipe.SetActions(false, true);   // a non-person among the working list's WhatsApp rows
        Assert.AreEqual(DeleteWidth, swipe.RevealWidth);
        Assert.IsFalse(move.gameObject.activeSelf);
        Assert.IsTrue(delete.gameObject.activeSelf);
        Assert.AreEqual(DeleteWidth, delete.anchoredPosition.x, "parked just past the edge");
    }

    [Test]
    public void SetActions_NoAction_TurnsTheSwipeOff()
    {
        SwipeToDelete swipe = Row(out _, out RectTransform move, out RectTransform delete);

        swipe.SetActions(false, false);
        Assert.IsFalse(swipe.enabled, "no drag reaches a row with nothing to reveal — it goes to the list's scroll");
        Assert.IsFalse(move.gameObject.activeSelf);
        Assert.IsFalse(delete.gameObject.activeSelf);
        Assert.AreEqual(0f, swipe.RevealWidth);

        swipe.SetActions(true, false);
        Assert.IsTrue(swipe.enabled, "an action brings the swipe back");
    }

    [Test]
    public void SetActions_TheSameActions_LeaveAnOpenRowOpen_NewOnesSnapItShut()
    {
        SwipeToDelete swipe = Row(out RectTransform content, out RectTransform move, out RectTransform delete);
        swipe.SetActions(true, true);
        DragBy(swipe, -1000f);

        swipe.SetActions(true, true);    // a refresh that changed nothing for this row
        Assert.AreEqual(-(MoveWidth + DeleteWidth), content.anchoredPosition.x, "still open");
        Assert.AreEqual(0f, delete.anchoredPosition.x);

        swipe.SetActions(true, false);   // the chat turned personal while it is shown
        Assert.AreEqual(0f, content.anchoredPosition.x, "snapped shut before the new actions");
        Assert.AreEqual(MoveWidth, move.anchoredPosition.x, "the move alone, parked past the edge");
        Assert.IsFalse(delete.gameObject.activeSelf);
    }

    // ---- helpers ----

    // ChatItem's shape: the two buttons behind (pinned to the right edge, pivot there), the sliding content in front.
    private SwipeToDelete Row(out RectTransform content, out RectTransform move, out RectTransform delete)
    {
        var root = new GameObject("ChatItem", typeof(RectTransform));
        _created.Add(root);
        delete = ActionButton(root.transform, "DeleteButton", DeleteWidth);
        move = ActionButton(root.transform, "MoveButton", MoveWidth);
        var contentGo = new GameObject("SwipeContent", typeof(RectTransform));
        contentGo.transform.SetParent(root.transform, false);
        content = (RectTransform)contentGo.transform;

        var swipe = contentGo.AddComponent<SwipeToDelete>();
        var refs = new SerializedObject(swipe);
        refs.FindProperty("deleteButton").objectReferenceValue = delete;
        refs.FindProperty("moveButton").objectReferenceValue = move;
        refs.ApplyModifiedPropertiesWithoutUndo();
        return swipe;
    }

    private static RectTransform ActionButton(Transform parent, string name, float width)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(width, 0f);
        return rect;
    }

    // A horizontal drag, begun and moved (never ended — the end would start the snap tween).
    private static void DragBy(SwipeToDelete swipe, float dx)
    {
        var press = new Vector2(600f, 400f);
        var drag = new PointerEventData(null) { pressPosition = press, position = press + new Vector2(dx, 0f) };
        swipe.OnBeginDrag(drag);
        swipe.OnDrag(drag);
    }
}
