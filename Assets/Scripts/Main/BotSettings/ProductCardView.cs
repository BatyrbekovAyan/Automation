using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Display-only product card. Tap → OnEditRequested. No inline edit;
    /// editing happens in ItemEditSheet. Replaces Product.cs.
    ///
    /// Properties Name/Price/Description are the re-wired read contract
    /// used by Manager.SaveSettings, Manager.CloseSettings, and
    /// Manager.CheckProductsOrServicesChanged.
    /// </summary>
    public class ProductCardView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI priceLabel;
        [SerializeField] private TextMeshProUGUI descLabel;
        [SerializeField] private Image thumb;
        [SerializeField] private Button rootButton;
        [SerializeField] private ItemCardMonogram monogram;
        [SerializeField] private GameObject pricePill;

        public event Action<ProductCardView> OnEditRequested;

        public string Name
        {
            get => nameLabel != null ? nameLabel.text : string.Empty;
            set
            {
                if (nameLabel != null) nameLabel.text = value ?? string.Empty;
                // The single entry point for every write path — load, add, and
                // the edit sheet's commit all land here.
                if (monogram != null) monogram.Bind(value ?? string.Empty);
            }
        }
        public string Price
        {
            get => priceLabel != null ? priceLabel.text : string.Empty;
            set
            {
                // Hardening for a device-only symptom (2026-08-17): after
                // committing a price from the edit sheet the tag rendered at a
                // stale width until bot settings were re-opened. EditMode could
                // not reproduce it in either ordering, so the mechanism is
                // suspected, not proven — the suspect being that a freshly
                // added item has no price, so the text lands on an INACTIVE tag
                // and LayoutRebuilder.MarkLayoutForRebuild returns early for it.
                // Switching the tag on BEFORE the write costs nothing and puts
                // every write on a live object.
                bool hasPrice = !string.IsNullOrWhiteSpace(value);
                if (pricePill != null) pricePill.SetActive(hasPrice);
                if (priceLabel != null) priceLabel.text = value ?? string.Empty;
                if (hasPrice) RequestRelayout();
            }
        }
        public string Description
        {
            get => descLabel != null ? descLabel.text : string.Empty;
            set
            {
                if (descLabel == null) return;
                var text = value ?? string.Empty;
                descLabel.text = text;
                descLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(text));
            }
        }

        // The price tag sizes itself from its own text, so a new price must
        // re-run the row's layout. TMP marks it on its own when the write lands
        // on a live object; this is the belt to that braces, and it is a queued
        // mark rather than an immediate rebuild, so it costs nothing per write.
        private void RequestRelayout()
        {
            if (isActiveAndEnabled)
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

        private void Awake()
        {
            if (rootButton != null)
                rootButton.onClick.AddListener(() => OnEditRequested?.Invoke(this));
        }

        private void OnDestroy()
        {
            if (rootButton != null) rootButton.onClick.RemoveAllListeners();
            OnEditRequested = null;
        }
    }
}
