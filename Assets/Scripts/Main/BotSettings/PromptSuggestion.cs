namespace Automation.BotSettingsUI
{
    public enum PromptSuggestionCategory
    {
        Tone,
        Format,
        Sales,
        Limits,
        Order,
    }

    /// <summary>
    /// One tappable mini-prompt. <see cref="Text"/> is what lands in the prompt
    /// field and what the sheet shows; <see cref="ShortLabel"/> is the pill
    /// caption — long instructions would otherwise wreck the chip rhythm.
    /// <see cref="VerticalId"/> is empty for core entries, otherwise a
    /// BusinessTypes.asset id. <see cref="Featured"/> marks core entries the
    /// cloud may show; it is never set on a vertical entry.
    /// </summary>
    public readonly struct PromptSuggestion
    {
        public readonly string Id;
        public readonly string Text;
        public readonly string ShortLabel;
        public readonly PromptSuggestionCategory Category;
        public readonly string VerticalId;
        public readonly bool Featured;

        public PromptSuggestion(string id, string text, string shortLabel,
            PromptSuggestionCategory category, string verticalId, bool featured)
        {
            Id = id;
            Text = text;
            ShortLabel = shortLabel;
            Category = category;
            VerticalId = verticalId ?? string.Empty;
            Featured = featured;
        }
    }

    public static class PromptSuggestionCategoryLabels
    {
        public static string Ru(PromptSuggestionCategory category)
        {
            switch (category)
            {
                case PromptSuggestionCategory.Tone:   return "Тон общения";
                case PromptSuggestionCategory.Format: return "Формат ответа";
                case PromptSuggestionCategory.Sales:  return "Продажи";
                case PromptSuggestionCategory.Limits: return "Ограничения";
                case PromptSuggestionCategory.Order:  return "Заказ и оплата";
                default: return string.Empty;
            }
        }
    }
}
