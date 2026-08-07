using System.Collections.Generic;
using System.Text;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Pure line surgery on the «Промпт» field's text. The prompt itself is the
    /// only state behind the suggestion chips — a suggestion is "added" exactly
    /// when its line is present here — so every comparison is line-exact after
    /// trimming, never a substring scan: «Отвечай коротко» must not be found
    /// inside «Отвечай коротко, до 2 предложений».
    /// </summary>
    public static class PromptTextComposer
    {
        public static bool Contains(string prompt, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var needle = line.Trim();
            foreach (var existing in SplitLines(prompt))
                if (existing.Trim() == needle) return true;
            return false;
        }

        public static string Append(string prompt, string line)
        {
            var current = prompt ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line)) return current;
            if (Contains(current, line)) return current;

            var trimmed = current.Replace("\r\n", "\n").TrimEnd();
            var addition = line.Trim();
            return trimmed.Length == 0 ? addition : $"{trimmed}\n{addition}";
        }

        public static string Remove(string prompt, string line)
        {
            var current = prompt ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line)) return current;

            var needle = line.Trim();
            var kept = new List<string>();
            foreach (var existing in SplitLines(current))
            {
                if (existing.Trim() == needle) continue;
                kept.Add(existing);
            }
            return Join(kept);
        }

        public static string ApplyDiff(
            string prompt, IEnumerable<string> toAdd, IEnumerable<string> toRemove)
        {
            var result = prompt ?? string.Empty;
            if (toRemove != null)
                foreach (var line in toRemove) result = Remove(result, line);
            if (toAdd != null)
                foreach (var line in toAdd) result = Append(result, line);
            return result;
        }

        private static string[] SplitLines(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        // Re-joins kept lines, collapsing any run of blank lines the removal
        // opened down to a single one so deleting a suggestion never leaves a
        // widening hole in a hand-written prompt.
        private static string Join(List<string> lines)
        {
            var builder = new StringBuilder();
            var previousBlank = false;
            foreach (var line in lines)
            {
                var blank = string.IsNullOrWhiteSpace(line);
                if (blank && previousBlank) continue;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(line);
                previousBlank = blank;
            }
            return builder.ToString().TrimEnd();
        }
    }
}
