using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Display-only service card. Tap → OnEditRequested. No inline edit;
    /// editing happens in ItemEditSheet. Replaces Service.cs.
    ///
    /// Properties Name/Price/Description are the re-wired read contract
    /// used by Manager.SaveSettings, Manager.CloseSettings, and
    /// Manager.CheckProductsOrServicesChanged.
    /// </summary>
    public class ServiceCardView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI priceLabel;
        [SerializeField] private TextMeshProUGUI descLabel;
        [SerializeField] private Image thumb;
        [SerializeField] private Button rootButton;

        public event Action<ServiceCardView> OnEditRequested;

        public string Name
        {
            get => nameLabel != null ? nameLabel.text : string.Empty;
            set { if (nameLabel != null) nameLabel.text = value ?? string.Empty; }
        }
        public string Price
        {
            get => priceLabel != null ? priceLabel.text : string.Empty;
            set { if (priceLabel != null) priceLabel.text = value ?? string.Empty; }
        }
        public string Description
        {
            get => descLabel != null ? descLabel.text : string.Empty;
            set { if (descLabel != null) descLabel.text = value ?? string.Empty; }
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
