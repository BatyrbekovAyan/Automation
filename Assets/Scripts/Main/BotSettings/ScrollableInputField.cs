using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// TMP_InputField variant that forwards drag gestures to the nearest
    /// parent ScrollRect instead of interpreting them as text selection.
    /// Lets a fixed-height card scroll its contents via swipe without the
    /// caret jumping and highlighting text along the way.
    /// Base TMP_InputField behaviour (tap-to-place-caret, keyboard input,
    /// focus, onValueChanged, etc.) is unchanged — only OnBeginDrag /
    /// OnDrag / OnEndDrag are rerouted.
    /// </summary>
    public class ScrollableInputField : TMP_InputField
    {
        private ScrollRect parentScroll;
        private bool parentScrollCached;

        private ScrollRect ParentScroll
        {
            get
            {
                if (!parentScrollCached)
                {
                    parentScroll = GetComponentInParent<ScrollRect>();
                    parentScrollCached = true;
                }
                return parentScroll;
            }
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            var scroll = ParentScroll;
            if (scroll != null) scroll.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            var scroll = ParentScroll;
            if (scroll != null) scroll.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            var scroll = ParentScroll;
            if (scroll != null) scroll.OnEndDrag(eventData);
        }
    }
}
