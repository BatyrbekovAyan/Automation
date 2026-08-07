using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Lays active children out left to right, wrapping to a new row when the
    /// next child would overflow. Unity ships no wrapping layout —
    /// GridLayoutGroup is fixed-cell and would clip variable-width pills — so
    /// the chip cloud needs this. Row assignment is delegated to
    /// <see cref="PromptSuggestionCloudFit"/> so it stays unit-tested.
    /// </summary>
    [AddComponentMenu("Layout/Chip Flow Layout")]
    public class ChipFlowLayout : LayoutGroup
    {
        [SerializeField] private float spacingX = 24f;
        [SerializeField] private float spacingY = 24f;
        [SerializeField] private float rowHeight = 108f;

        private readonly List<float> widths = new List<float>();
        private int rowCount;

        /// <summary>Rows produced by the last layout pass.</summary>
        public int RowCount => rowCount;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(padding.horizontal, padding.horizontal, -1, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            var rows = Mathf.Max(rowCount, 1);
            var height = padding.vertical + rows * rowHeight + (rows - 1) * spacingY;
            SetLayoutInputForAxis(height, height, -1, 1);
        }

        public override void SetLayoutHorizontal() => Arrange();

        public override void SetLayoutVertical() => Arrange();

        private void Arrange()
        {
            widths.Clear();
            var children = new List<RectTransform>();
            for (var i = 0; i < rectChildren.Count; i++)
            {
                var child = rectChildren[i];
                children.Add(child);
                widths.Add(LayoutUtility.GetPreferredWidth(child));
            }

            var rowWidth = rectTransform.rect.width - padding.horizontal;
            var rows = PromptSuggestionCloudFit.RowOf(widths, rowWidth, spacingX);
            rowCount = rows.Length == 0 ? 0 : rows[rows.Length - 1] + 1;

            var x = (float)padding.left;
            var currentRow = 0;
            for (var i = 0; i < children.Count; i++)
            {
                if (rows[i] != currentRow)
                {
                    currentRow = rows[i];
                    x = padding.left;
                }
                var y = padding.top + currentRow * (rowHeight + spacingY);
                SetChildAlongAxis(children[i], 0, x, widths[i]);
                SetChildAlongAxis(children[i], 1, y, rowHeight);
                x += widths[i] + spacingX;
            }
        }
    }
}
