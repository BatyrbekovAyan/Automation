using System;

/// <summary>
/// «Личные и рабочие чаты»: where a chat row's swipe actions stand behind it (<see cref="SwipeToDelete"/>). Every button is
/// pinned to the row's right edge with its pivot there — «Удалить» rightmost, the move button («В личные» / «В рабочие») on its
/// left. <see cref="Solve"/> gives the reveal (how far the row slides open: the visible buttons' widths together) and each
/// button's x once the row is fully open. At rest every button stands <c>revealWidth</c> further right, just past the edge,
/// and in between it rides with the row. A hidden button's x is 0 and unused.
/// Pure (no UnityEngine), so EditMode pins it.
/// </summary>
public static class SwipeActionsLayout
{
    /// <summary>The reveal and each button's x when the row is fully open; a width below zero counts as nothing.</summary>
    public static (float revealWidth, float moveX, float deleteX) Solve(bool move, bool delete, float moveWidth, float deleteWidth)
    {
        float moveSpan = move ? Math.Max(0f, moveWidth) : 0f;
        float deleteSpan = delete ? Math.Max(0f, deleteWidth) : 0f;
        float moveX = move && delete ? -deleteSpan : 0f;   // right on «Удалить»'s left, else flush with the edge
        return (moveSpan + deleteSpan, moveX, 0f);
    }
}
