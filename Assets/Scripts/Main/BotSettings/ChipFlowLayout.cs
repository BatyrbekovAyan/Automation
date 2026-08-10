using System;
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
        private int[] rowIndices = Array.Empty<int>();
        private int rowCount;

        /// <summary>Rows produced by the last layout pass.</summary>
        public int RowCount => rowCount;

        /// <summary>Exposed so the cloud can compute the height N rows need.</summary>
        public float RowHeight => rowHeight;

        /// <summary>Vertical gap between rows, exposed for the same height math.</summary>
        public float SpacingY => spacingY;

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

        /// <summary>
        /// Measures every child once, resolves row assignment, and places
        /// children along the horizontal axis. Runs before
        /// <see cref="CalculateLayoutInputVertical"/> and
        /// <see cref="SetLayoutVertical"/> in Unity's rebuild phase order, so
        /// <see cref="rowCount"/> and <see cref="rowIndices"/> are populated
        /// before either reads them.
        /// </summary>
        public override void SetLayoutHorizontal()
        {
            widths.Clear();
            for (var i = 0; i < rectChildren.Count; i++)
            {
                widths.Add(LayoutUtility.GetPreferredWidth(rectChildren[i]));
            }

            var rowWidth = rectTransform.rect.width - padding.horizontal;
            rowIndices = PromptSuggestionCloudFit.RowOf(widths, rowWidth, spacingX);
            rowCount = rowIndices.Length == 0 ? 0 : rowIndices[rowIndices.Length - 1] + 1;

            var x = (float)padding.left;
            var currentRow = 0;
            for (var i = 0; i < rectChildren.Count; i++)
            {
                if (rowIndices[i] != currentRow)
                {
                    currentRow = rowIndices[i];
                    x = padding.left;
                }
                SetChildAlongAxis(rectChildren[i], 0, x, widths[i]);
                x += widths[i] + spacingX;
            }
        }

        /// <summary>
        /// Places children along the vertical axis using the row assignment
        /// already computed by <see cref="SetLayoutHorizontal"/> — no
        /// re-measuring. Defensively no-ops for any child index beyond what
        /// <see cref="rowIndices"/> covers, in case a rebuild ever lands
        /// vertical-first.
        /// </summary>
        public override void SetLayoutVertical()
        {
            for (var i = 0; i < rectChildren.Count && i < rowIndices.Length; i++)
            {
                var y = padding.top + rowIndices[i] * (rowHeight + spacingY);
                SetChildAlongAxis(rectChildren[i], 1, y, rowHeight);
            }
        }
    }
}
