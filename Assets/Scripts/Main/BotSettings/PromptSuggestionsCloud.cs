using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// The chip cloud under the «Промпт» field. Owns a pool of chips cloned
    /// from an inactive template child, decides how many fit three rows, and
    /// keeps «Ещё N ›» honest by counting what actually rendered.
    ///
    /// Holds no state of its own: a chip is "added" exactly when its line is in
    /// the prompt text, read through <see cref="ReadPrompt"/> on every refresh.
    /// </summary>
    public class PromptSuggestionsCloud : MonoBehaviour
    {
        private const int MaxRows = 3;
        // Below this the layout width has not settled yet and TMP would report
        // a ~2-unit preferred width, which packs every chip onto its own row.
        private const float SettledWidthFloor = 100f;

        [SerializeField] private RectTransform chipsParent;
        [SerializeField] private ChipFlowLayout flowLayout;
        [SerializeField] private PromptSuggestionChip chipTemplate;
        [SerializeField] private Button moreButton;
        [SerializeField] private TextMeshProUGUI moreLabel;
        [SerializeField] private float chipHorizontalPadding = 36f;
        [SerializeField] private float glyphWidth = 60f;   // glyph 42 + 18 gap
        [SerializeField] private float chipSpacing = 24f;

        private readonly List<PromptSuggestionChip> pool = new List<PromptSuggestionChip>();

        private string businessTypeId = string.Empty;
        private List<PromptSuggestion> candidates = new List<PromptSuggestion>();
        private int totalForBot;
        private Coroutine layoutRoutine;

        /// <summary>Reads the current prompt text. Set by BotSettings.</summary>
        public Func<string> ReadPrompt { get; set; }

        /// <summary>Runs a prompt transform through the focus-safe write path. Set by BotSettings.</summary>
        public Action<Func<string, string>> MutatePrompt { get; set; }

        public event Action OnMorePressed;

        private void Awake()
        {
            if (chipTemplate != null) chipTemplate.gameObject.SetActive(false);
            if (moreButton != null) moreButton.onClick.AddListener(() => OnMorePressed?.Invoke());
        }

        private void OnDisable()
        {
            // This screen's coroutines die with it; drop the handle so a later
            // open is not blocked by a latch nobody can clear.
            layoutRoutine = null;
        }

        // Bind() usually lands while this object is inactive — Bot Settings opens
        // on the «Основное» tab — so BuildChips cannot start the fit coroutine.
        // Re-run it when the Промпты tab actually appears, or the cloud renders
        // its raw candidate list with no row cap and a stale «Ещё N ›». Going
        // through BuildChips rather than StartCoroutine keeps its null-guard on
        // the only path that reaches FitAfterLayout.
        private void OnEnable()
        {
            if (candidates.Count == 0) return;
            BuildChips();
        }

        public void Bind(string verticalId)
        {
            businessTypeId = verticalId ?? string.Empty;
            candidates = PromptSuggestionCatalog.CloudCandidates(businessTypeId);
            totalForBot = PromptSuggestionCatalog.ForVertical(businessTypeId).Count;
            BuildChips();
            Refresh();
        }

        /// <summary>Re-reads the prompt and re-stamps every chip's added state.</summary>
        public void Refresh()
        {
            var prompt = ReadPrompt != null ? ReadPrompt() : string.Empty;
            for (var i = 0; i < pool.Count; i++)
            {
                if (!pool[i].gameObject.activeSelf) continue;
                pool[i].SetAdded(PromptTextComposer.Contains(prompt, pool[i].Suggestion.Text));
            }
        }

        private void BuildChips()
        {
            if (chipTemplate == null || chipsParent == null) return;

            while (pool.Count < candidates.Count)
            {
                var chip = Instantiate(chipTemplate, chipsParent);
                chip.name = $"Chip_{pool.Count}";
                pool.Add(chip);
            }

            for (var i = 0; i < pool.Count; i++)
            {
                var active = i < candidates.Count;
                pool[i].gameObject.SetActive(active);
                if (active) pool[i].Bind(candidates[i], HandleChipPressed);
            }

            if (layoutRoutine != null) StopCoroutine(layoutRoutine);
            if (isActiveAndEnabled) layoutRoutine = StartCoroutine(FitAfterLayout());
        }

        // The container's width is not final on the frame the tab activates, and
        // measuring TMP too early yields a ~2-unit width. Wait for the layout to
        // settle before trusting any preferred width.
        private IEnumerator FitAfterLayout()
        {
            yield return null;

            var guard = 0;
            while (chipsParent.rect.width < SettledWidthFloor && guard++ < 10)
                yield return null;

            var rowWidth = chipsParent.rect.width;
            var widths = new List<float>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var width = MeasureChipWidth(pool[i]);
                pool[i].SetPreferredWidth(width);
                widths.Add(width);
            }

            var visible = PromptSuggestionCloudFit.Take(widths, rowWidth, chipSpacing, MaxRows);
            for (var i = 0; i < pool.Count; i++)
                pool[i].gameObject.SetActive(i < visible);

            ApplyCloudHeight(widths, rowWidth, visible);
            if (moreLabel != null) moreLabel.text = $"Ещё {Mathf.Max(totalForBot - visible, 0)} ›";
            if (moreButton != null) moreButton.gameObject.SetActive(totalForBot > visible);

            layoutRoutine = null;
            Refresh();
        }

        // The parent VLG has childControlHeight OFF, so it positions this rect
        // by its own height — and a ContentSizeFitter can't be trusted here (the
        // VLG placed us BEFORE the fitter grew the rect around its centre pivot,
        // which is exactly how the chips ended up 132 units up over the header).
        // Set the height explicitly, then re-run the parent's layout so the VLG
        // repositions us with the final size.
        private void ApplyCloudHeight(List<float> chipWidths, float rowWidth, int visible)
        {
            if (flowLayout == null) return;

            var rows = 1;
            if (visible > 0)
            {
                var rowOf = PromptSuggestionCloudFit.RowOf(
                    chipWidths.GetRange(0, visible), rowWidth, chipSpacing);
                rows = rowOf[rowOf.Length - 1] + 1;
            }

            var height = rows * flowLayout.RowHeight + (rows - 1) * flowLayout.SpacingY;
            chipsParent.sizeDelta = new Vector2(chipsParent.sizeDelta.x, height);

            LayoutRebuilder.MarkLayoutForRebuild(chipsParent);
            if (chipsParent.parent is RectTransform parent)
                LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        private float MeasureChipWidth(PromptSuggestionChip chip) =>
            chip.PreferredLabelWidth + glyphWidth + chipHorizontalPadding * 2f;

        private void HandleChipPressed(PromptSuggestion suggestion)
        {
            if (MutatePrompt == null) return;
            MutatePrompt(prompt => PromptTextComposer.Contains(prompt, suggestion.Text)
                ? PromptTextComposer.Remove(prompt, suggestion.Text)
                : PromptTextComposer.Append(prompt, suggestion.Text));
        }
    }
}
