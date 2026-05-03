namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Multi-line variant of EditableField used for Business description
    /// and Prompt fields. Kept as a distinct type so prefab references
    /// (BusinessField, PromptField) and ScrollableTextArea's
    /// [RequireComponent] can target the multi-line case specifically.
    /// </summary>
    public class EditableTextArea : EditableField
    {
    }
}
