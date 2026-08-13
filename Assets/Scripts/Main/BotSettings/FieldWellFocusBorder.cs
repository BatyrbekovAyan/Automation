using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Owns the colour of an input well's 1-unit ring: Hairline at rest,
    /// InputBorder while the well's <see cref="EditableField"/> holds focus.
    ///
    /// Deliberately carries NO <see cref="ThemedColor"/> binding. ThemedColor is
    /// [DisallowMultipleComponent] and repaints its graphic on enable and on
    /// Theme.Changed, so a second owner would drop a focused ring back to the
    /// rest colour mid-edit — the same reason PromptSuggestionChip paints its
    /// own state-dependent colours. This component reads the palette directly
    /// and re-reads it on Theme.Changed.
    ///
    /// The rest state is re-asserted in OnEnable because ItemEditSheet.Hide()
    /// force-blurs its fields and <see cref="EditableField.ForceBlur"/>
    /// deliberately does NOT raise Blurred — without the reset the ring would
    /// still show the focus colour the next time the sheet slides up.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldWellFocusBorder : MonoBehaviour
    {
        [SerializeField] private EditableField field;
        [SerializeField] private Graphic ring;

        private bool focused;

        private void Awake()
        {
            if (ring == null) ring = GetComponent<Graphic>();
        }

        private void OnEnable()
        {
            if (ring == null) ring = GetComponent<Graphic>();

            focused = false;
            if (field != null)
            {
                field.Selected += HandleSelected;
                field.Blurred += HandleBlurred;
            }
            Theme.Changed += Paint;
            Paint();
        }

        private void OnDisable()
        {
            if (field != null)
            {
                field.Selected -= HandleSelected;
                field.Blurred -= HandleBlurred;
            }
            Theme.Changed -= Paint;
        }

        private void HandleSelected(EditableField _)
        {
            focused = true;
            Paint();
        }

        private void HandleBlurred(EditableField _)
        {
            focused = false;
            Paint();
        }

        private void Paint()
        {
            if (ring == null) return;

            var color = Theme.Color(focused ? ThemeRole.InputBorder : ThemeRole.Hairline);
            color.a = ring.color.a;   // authored alpha wins, as with ThemedColor
            ring.color = color;
        }
    }
}
