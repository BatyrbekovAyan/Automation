using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Full-catalog bottom sheet for the Промпты tab. Structurally mirrors
    /// <see cref="UploadSourceSheet"/> — slide-up, scrim behind, tap-outside to
    /// close — and adds a category filter, a checkbox list and a diff apply.
    ///
    /// Checkboxes are initialised from the prompt text, never from stored
    /// state, so the sheet and the chips can never disagree. «Применить»
    /// removes the newly-unchecked lines and appends the newly-checked ones.
    /// </summary>
    public class PromptSuggestionsSheet : MonoBehaviour
    {
        [SerializeField] private RectTransform sheetRoot;
        [SerializeField] private GameObject scrimBehind;
        [SerializeField] private CanvasGroup scrimBehindGroup;
        [SerializeField] private DelayedFingerUpAction scrimBehindFinger;
        [SerializeField] private Button closeButton;
        [SerializeField] private float slideDuration = 0.28f;
        [SerializeField] private float scrimAlpha = 0.5f;

        // Mirrors the builder's category-label side inset (Stretch left/right 30).
        private const float CategoryLabelPadding = 30f;

        [SerializeField] private RectTransform rowsParent;
        [SerializeField] private PromptSuggestionRowView rowTemplate;
        [SerializeField] private RectTransform categoriesParent;
        [SerializeField] private Button categoryTemplate;
        [SerializeField] private TextMeshProUGUI selectedCountLabel;
        [SerializeField] private Button applyButton;
        [SerializeField] private TextMeshProUGUI applyLabel;

        private readonly List<PromptSuggestionRowView> rowPool = new List<PromptSuggestionRowView>();
        private readonly List<Button> categoryPool = new List<Button>();
        private readonly HashSet<string> pendingChecked = new HashSet<string>();

        private List<PromptSuggestion> entries = new List<PromptSuggestion>();
        private PromptSuggestionCategory? categoryFilter;
        private Vector2 hiddenAnchored;
        private Vector2 shownAnchored;
        private Tween positionTween;
        private bool visible;

        public Func<string> ReadPrompt { get; set; }
        public Action<Func<string, string>> MutatePrompt { get; set; }
        public event Action OnClosed;

        private void Awake()
        {
            shownAnchored = sheetRoot.anchoredPosition;
            hiddenAnchored = new Vector2(shownAnchored.x, -sheetRoot.rect.height);
            sheetRoot.anchoredPosition = hiddenAnchored;
            // The prefab ships this container inactive, so Awake runs on the
            // first Show(); deactivating here would cancel that first slide-in.

            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (categoryTemplate != null) categoryTemplate.gameObject.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (applyButton != null) applyButton.onClick.AddListener(Apply);
            if (scrimBehindFinger != null) scrimBehindFinger.OnRealRelease += Hide;
        }

        private void OnDestroy()
        {
            if (scrimBehindFinger != null) scrimBehindFinger.OnRealRelease -= Hide;
        }

        public void Show(string verticalId)
        {
            entries = PromptSuggestionCatalog.ForVertical(verticalId ?? string.Empty);
            categoryFilter = null;
            pendingChecked.Clear();

            var prompt = ReadPrompt != null ? ReadPrompt() : string.Empty;
            foreach (var entry in entries)
                if (PromptTextComposer.Contains(prompt, entry.Text)) pendingChecked.Add(entry.Id);

            gameObject.SetActive(true);
            if (scrimBehind != null) scrimBehind.SetActive(true);
            if (scrimBehindGroup != null)
            {
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }

            positionTween?.Kill();
            positionTween = sheetRoot.DOAnchorPos(shownAnchored, slideDuration).SetEase(Ease.OutCubic);
            visible = true;

            BuildCategories();
            BuildRows();
            RefreshApplyButton();
        }

        /// <summary>True while the sheet is up (Back router).</summary>
        public bool IsVisible => visible;

        public void Hide()
        {
            if (!visible) return;
            visible = false;

            positionTween?.Kill();
            positionTween = sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    if (scrimBehind != null) scrimBehind.SetActive(false);
                    gameObject.SetActive(false);
                    OnClosed?.Invoke();
                });

            if (scrimBehindGroup != null)
            {
                scrimBehindGroup.DOKill();
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad);
            }
        }

        private void BuildCategories()
        {
            if (categoryTemplate == null || categoriesParent == null) return;

            var categories = new List<PromptSuggestionCategory?> { null };
            foreach (PromptSuggestionCategory value in Enum.GetValues(typeof(PromptSuggestionCategory)))
                categories.Add(value);

            while (categoryPool.Count < categories.Count)
            {
                var clone = Instantiate(categoryTemplate, categoriesParent);
                categoryPool.Add(clone);
            }

            for (var i = 0; i < categoryPool.Count; i++)
            {
                var active = i < categories.Count;
                categoryPool[i].gameObject.SetActive(active);
                if (!active) continue;

                var category = categories[i];
                var text = categoryPool[i].GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (text != null)
                {
                    text.text = category.HasValue
                        ? PromptSuggestionCategoryLabels.Ru(category.Value)
                        : "Все";

                    // The pill's own Image is sprite-less, so its ILayoutElement
                    // preferred width is 0 and the rail's HLG would collapse every
                    // pill to nothing — publish the measured label width instead,
                    // the same contract the chips use.
                    var element = categoryPool[i].GetComponent<LayoutElement>();
                    if (element != null)
                        element.preferredWidth =
                            text.GetPreferredValues(text.text).x + CategoryLabelPadding * 2f;
                }

                categoryPool[i].onClick.RemoveAllListeners();
                categoryPool[i].onClick.AddListener(() =>
                {
                    categoryFilter = category;
                    BuildRows();
                });
            }
        }

        private void BuildRows()
        {
            if (rowTemplate == null || rowsParent == null) return;

            var shown = new List<PromptSuggestion>(entries.Count);
            foreach (var entry in entries)
                if (!categoryFilter.HasValue || entry.Category == categoryFilter.Value) shown.Add(entry);

            while (rowPool.Count < shown.Count)
            {
                var clone = Instantiate(rowTemplate, rowsParent);
                rowPool.Add(clone);
            }

            for (var i = 0; i < rowPool.Count; i++)
            {
                var active = i < shown.Count;
                rowPool[i].gameObject.SetActive(active);
                if (active) rowPool[i].Bind(shown[i], pendingChecked.Contains(shown[i].Id), ToggleRow);
            }
        }

        private void ToggleRow(PromptSuggestion suggestion)
        {
            if (!pendingChecked.Remove(suggestion.Id)) pendingChecked.Add(suggestion.Id);

            foreach (var row in rowPool)
                if (row.gameObject.activeSelf && row.Suggestion.Id == suggestion.Id)
                    row.SetChecked(pendingChecked.Contains(suggestion.Id));

            RefreshApplyButton();
        }

        private void CollectDiff(out List<string> toAdd, out List<string> toRemove)
        {
            var prompt = ReadPrompt != null ? ReadPrompt() : string.Empty;
            toAdd = new List<string>();
            toRemove = new List<string>();

            foreach (var entry in entries)
            {
                var present = PromptTextComposer.Contains(prompt, entry.Text);
                var wanted = pendingChecked.Contains(entry.Id);
                if (wanted && !present) toAdd.Add(entry.Text);
                else if (!wanted && present) toRemove.Add(entry.Text);
            }
        }

        private void RefreshApplyButton()
        {
            CollectDiff(out var toAdd, out var toRemove);

            if (selectedCountLabel != null)
                selectedCountLabel.text = $"выбрано {pendingChecked.Count}";

            var empty = toAdd.Count == 0 && toRemove.Count == 0;
            if (applyButton != null) applyButton.interactable = !empty;
            if (applyLabel != null)
                applyLabel.text = toRemove.Count == 0 ? $"Добавить {toAdd.Count}" : "Применить";
        }

        private void Apply()
        {
            CollectDiff(out var toAdd, out var toRemove);
            if (toAdd.Count == 0 && toRemove.Count == 0) return;

            MutatePrompt?.Invoke(prompt => PromptTextComposer.ApplyDiff(prompt, toAdd, toRemove));
            Hide();
        }
    }
}
