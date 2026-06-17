using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tapping a quoted reply card jumps the list to the original message and flashes it.
/// Implements ONLY IPointerClickHandler (not a Button) so pointer-down still propagates to the
/// bubble's long-press / swipe handlers — only a clean tap triggers the jump.
/// </summary>
public class QuotedCardTap : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (ScrollClickBlocker.IsBlocking) return;

        var view = GetComponentInParent<MessageItemView>();
        if (view == null || view.BoundVm == null || string.IsNullOrEmpty(view.BoundVm.quotedMessageId)) return;

        var list = GetComponentInParent<MessageListView>();
        list?.ScrollToMessage(view.BoundVm.quotedMessageId);
    }
}
