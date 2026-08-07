using System.Collections.Generic;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Greedy left-to-right row packing for the chip cloud. Pure so the cloud's
    /// «Ещё N ›» count is provable in a unit test instead of eyeballed on a
    /// device — the count is only honest if it reflects the chips that actually
    /// rendered.
    /// </summary>
    public static class PromptSuggestionCloudFit
    {
        /// <summary>Row index for each chip, laying them out left to right.</summary>
        public static int[] RowOf(IReadOnlyList<float> widths, float rowWidth, float spacing)
        {
            if (widths == null || widths.Count == 0) return new int[0];

            var rows = new int[widths.Count];
            var row = 0;
            var used = 0f;

            for (var i = 0; i < widths.Count; i++)
            {
                var width = widths[i];
                var needed = used <= 0f ? width : used + spacing + width;

                // A chip wider than the whole row still occupies one — the view
                // clamps its label, it is never silently dropped.
                if (used > 0f && needed > rowWidth)
                {
                    row++;
                    used = width;
                }
                else
                {
                    used = needed;
                }
                rows[i] = row;
            }
            return rows;
        }

        /// <summary>How many leading chips fit within <paramref name="maxRows"/> rows.</summary>
        public static int Take(IReadOnlyList<float> widths, float rowWidth, float spacing, int maxRows)
        {
            if (widths == null || widths.Count == 0 || maxRows <= 0) return 0;

            var rows = RowOf(widths, rowWidth, spacing);
            var count = 0;
            foreach (var row in rows)
            {
                if (row >= maxRows) break;
                count++;
            }
            return count;
        }
    }
}
