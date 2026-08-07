using System;
using System.Collections;
using Automation.BotSettingsUI;
using UnityEngine;

/// <summary>
/// Промпты tab: the suggestion cloud, the catalog sheet, and the ONE write
/// path both of them use.
///
/// Every prompt mutation blurs the field and waits a frame before writing.
/// On iOS a write into a still-focused TMP field round-trips through the
/// shared native keyboard buffer and lands in the wrong place — this ordering
/// is the invariant, not a precaution.
/// </summary>
public partial class BotSettings
{
    [SerializeField] private PromptSuggestionsCloud promptSuggestionsCloud;
    [SerializeField] private PromptSuggestionsSheet promptSuggestionsSheet;

    private Coroutine promptMutation;

    private void WirePromptSuggestions()
    {
        if (promptSuggestionsCloud != null)
        {
            promptSuggestionsCloud.ReadPrompt = ReadPromptValue;
            promptSuggestionsCloud.MutatePrompt = MutatePrompt;
            promptSuggestionsCloud.OnMorePressed += OpenPromptSuggestionsSheet;
        }

        if (promptSuggestionsSheet != null)
        {
            promptSuggestionsSheet.ReadPrompt = ReadPromptValue;
            promptSuggestionsSheet.MutatePrompt = MutatePrompt;
            promptSuggestionsSheet.OnClosed += HandlePromptSheetClosed;
        }
    }

    private string ReadPromptValue() => PromptField != null ? PromptField.Value : string.Empty;

    /// <summary>The open bot's vertical, or "" when it has none or a pre-vertical legacy id.</summary>
    private static string OpenBotVerticalId()
    {
        var bot = Manager.openBot;
        return bot == null
            ? string.Empty
            : PlayerPrefs.GetString($"{bot.name}BusinessType", string.Empty);
    }

    /// <summary>Rebinds the cloud to the open bot's vertical. Called after the prompt value loads.</summary>
    public void RefreshPromptSuggestions()
    {
        if (promptSuggestionsCloud == null) return;
        promptSuggestionsCloud.Bind(OpenBotVerticalId());
    }

    /// <summary>
    /// Re-reads the prompt and re-stamps the chips. Cheap; called when the tab
    /// opens so a line typed by hand on a previous visit shows as added.
    /// </summary>
    public void RefreshPromptSuggestionStates()
    {
        if (promptSuggestionsCloud != null) promptSuggestionsCloud.Refresh();
    }

    /// <summary>Called from OnDisable — the coroutine that would clear this latch is already dead.</summary>
    public void ResetPromptMutationState() => promptMutation = null;

    private void OpenPromptSuggestionsSheet()
    {
        if (promptSuggestionsSheet != null) promptSuggestionsSheet.Show(OpenBotVerticalId());
    }

    private void HandlePromptSheetClosed() => RefreshPromptSuggestionStates();

    private void MutatePrompt(Func<string, string> transform)
    {
        if (transform == null || promptMutation != null) return;
        promptMutation = StartCoroutine(MutatePromptRoutine(transform));
    }

    private IEnumerator MutatePromptRoutine(Func<string, string> transform)
    {
        if (PromptField != null && PromptField.IsFocused)
        {
            PromptField.ForceBlur();
            yield return null;   // let the release land before touching .text
        }

        if (PromptField != null) PromptField.Value = transform(PromptField.Value);

        promptMutation = null;
        RefreshPromptSuggestionStates();
    }
}
