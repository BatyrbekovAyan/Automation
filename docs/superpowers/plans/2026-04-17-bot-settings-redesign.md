# Bot Settings Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Unity Bot Settings page to match `Design/mockup.html` pages 7 & 8, replacing the button-overlay-on-input hack and the 160-line `CloseInputBackground` with a clean component architecture (`EditableField` + `FocusScrim` + bottom-sheet product/service editor), while preserving every existing behavior (dirty-tracking save, auth flows, PlayerPrefs keys, n8n webhooks, delete-confirm popups).

**Architecture:** Ten new view components under `Assets/Scripts/Main/BotSettings/`. `BotSettings.cs` rewritten as a thin controller; auth coroutines lifted verbatim into `BotSettings.Auth.cs` partial. `Manager.cs` receives scoped re-wires only — its save/load/webhook logic stays byte-for-byte. All PlayerPrefs keys and the `PopupUI` module are preserved.

**Tech Stack:** Unity 6000.3.9f1, C#, TMPro, DOTween, URP, existing `PopupUI.cs` + `DelayedFingerUpAction`. No new packages.

**Verification:** Unity has no EditMode test asmdef for gameplay scripts, and bootstrapping one is out of scope. Each task specifies exact Editor-level checks (compile, inspector wiring, play-mode behavior) and ends with the relevant smoke-test checklist items from the spec.

---

## File Structure

**Create (all under `Assets/Scripts/Main/BotSettings/`):**
- `EditableField.cs` — single-line TMP_InputField card with label + scrim integration
- `EditableTextArea.cs` — multi-line variant for Business/Prompt; signals full-screen focus
- `ToggleRow.cs` — iOS-style DOTween toggle in a card row
- `SectionHeader.cs` — uppercase muted label
- `ProductCardView.cs` — display-only product card
- `ServiceCardView.cs` — display-only service card
- `ItemEditSheet.cs` — bottom-sheet editor for one product or service
- `AddItemButton.cs` — dashed-border add button
- `FocusScrim.cs` — full-screen dim + raised-layer reparent
- `SavedToast.cs` — fade-in/fade-out wrapper for the Saved GO
- `BotSettings.Auth.cs` — partial class containing auth coroutines (lifted verbatim)

**Rewrite:**
- `Assets/Scripts/Main/BotSettings.cs` — thin controller

**Modify (scoped re-wires only):**
- `Assets/Scripts/Main/Manager.cs` — text-access reads/writes only

**Delete:**
- `Assets/Scripts/Main/Product.cs`
- `Assets/Scripts/Main/Service.cs`

**Rebuild in Unity Editor:**
- `Assets/Prefabs/BotSettings.prefab`
- `Assets/Prefabs/Product.prefab`
- `Assets/Prefabs/Service.prefab`
- `Assets/Scenes/Main.unity` (re-wire the BotSettings subtree's serialized references)

---

## Task 1: Scaffold folder and empty component stubs

**Files:**
- Create: `Assets/Scripts/Main/BotSettings/EditableField.cs`
- Create: `Assets/Scripts/Main/BotSettings/EditableTextArea.cs`
- Create: `Assets/Scripts/Main/BotSettings/ToggleRow.cs`
- Create: `Assets/Scripts/Main/BotSettings/SectionHeader.cs`
- Create: `Assets/Scripts/Main/BotSettings/ProductCardView.cs`
- Create: `Assets/Scripts/Main/BotSettings/ServiceCardView.cs`
- Create: `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`
- Create: `Assets/Scripts/Main/BotSettings/AddItemButton.cs`
- Create: `Assets/Scripts/Main/BotSettings/FocusScrim.cs`
- Create: `Assets/Scripts/Main/BotSettings/SavedToast.cs`

- [ ] **Step 1: Create the folder**

Run: `mkdir -p /Users/ayan/Projects/Automation/Assets/Scripts/Main/BotSettings`

- [ ] **Step 2: Create each stub file with only the `using`s + empty class**

Example for `EditableField.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    public class EditableField : MonoBehaviour
    {
        // Implemented in Task 2
    }
}
```

Repeat for every file above. Use matching class names. `EditableTextArea` extends `EditableField`. `ProductCardView` / `ServiceCardView` are `MonoBehaviour`. `ItemEditSheet` is `MonoBehaviour`. `FocusScrim` is `MonoBehaviour`. `SavedToast` is `MonoBehaviour`. `AddItemButton` is `MonoBehaviour`. `SectionHeader` is `MonoBehaviour`. `ToggleRow` is `MonoBehaviour`.

- [ ] **Step 3: Verify Unity compiles**

Open Unity Editor. Check the Console — expect no errors. The folder `Assets/Scripts/Main/BotSettings/` should appear in the Project view with 10 script files.

- [ ] **Step 4: Commit**

```bash
cd /Users/ayan/Projects/Automation
git add Assets/Scripts/Main/BotSettings/
git commit -m "refactor: scaffold BotSettings component folder

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Implement FocusScrim

**Rationale:** Build the scrim first because every editable component depends on it.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/FocusScrim.cs`

- [ ] **Step 1: Replace the stub with the full implementation**

```csharp
using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Full-screen dim + raised-layer reparent. Show(field, onOutsideTap)
    /// lifts the given RectTransform onto raisedLayer so it renders above
    /// the scrim; tapping the scrim invokes onOutsideTap (via finger-up,
    /// not finger-down, matching PopupUI).
    /// </summary>
    public class FocusScrim : MonoBehaviour
    {
        [SerializeField] private GameObject scrimRoot;
        [SerializeField] private CanvasGroup scrimGroup;
        [SerializeField] private Image scrimImage;
        [SerializeField] private RectTransform raisedLayer;
        [SerializeField] private float targetAlpha = 0.5f;
        [SerializeField] private float fadeInDuration = 0.2f;
        [SerializeField] private float fadeOutDuration = 0.15f;

        private RectTransform raisedField;
        private Transform originalParent;
        private int originalSiblingIndex;
        private DelayedFingerUpAction fingerUp;
        private Action onOutsideTapCached;

        public bool IsShowing { get; private set; }

        public void Show(RectTransform field, Action onOutsideTap)
        {
            if (IsShowing) Hide();

            raisedField = field;
            originalParent = field.parent;
            originalSiblingIndex = field.GetSiblingIndex();

            field.SetParent(raisedLayer, worldPositionStays: true);
            field.SetAsLastSibling();

            scrimRoot.SetActive(true);
            scrimGroup.DOKill();
            scrimGroup.alpha = 0f;
            scrimGroup.DOFade(targetAlpha, fadeInDuration).SetEase(Ease.OutQuad);

            if (fingerUp == null)
                fingerUp = scrimImage.gameObject.GetComponent<DelayedFingerUpAction>()
                           ?? scrimImage.gameObject.AddComponent<DelayedFingerUpAction>();
            onOutsideTapCached = onOutsideTap;
            fingerUp.OnRealRelease += HandleOutsideTap;

            IsShowing = true;
        }

        public void Hide()
        {
            if (!IsShowing) return;

            if (fingerUp != null)
                fingerUp.OnRealRelease -= HandleOutsideTap;
            onOutsideTapCached = null;

            scrimGroup.DOKill();
            scrimGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.InQuad)
                .OnComplete(() => scrimRoot.SetActive(false));

            if (raisedField != null && originalParent != null)
            {
                raisedField.SetParent(originalParent, worldPositionStays: true);
                raisedField.SetSiblingIndex(originalSiblingIndex);
            }

            raisedField = null;
            originalParent = null;
            IsShowing = false;
        }

        private void HandleOutsideTap()
        {
            onOutsideTapCached?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Save. Unity Console should show no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/FocusScrim.cs
git commit -m "feat(bot-settings): add FocusScrim component

Replaces the InputBackgroundButton + hierarchy-manipulation pattern.
Show(field, onOutsideTap) reparents field onto raisedLayer, fades
scrim in over 0.2s, wires a DelayedFingerUpAction so outside-tap
commits on true finger release (matching PopupUI).

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Implement EditableField

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/EditableField.cs`

- [ ] **Step 1: Replace stub with implementation**

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Card-styled single-line input. Replaces the legacy Button-with-
    /// child-TMP + hidden TMP_InputField hack.
    ///
    /// On focus, requests FocusScrim to raise this RectTransform above a
    /// dim overlay. On blur (outside-tap, onEndEdit, keyboard-Done) fires
    /// OnCommitted only if the value changed since focus.
    /// </summary>
    public class EditableField : MonoBehaviour
    {
        [SerializeField] protected TextMeshProUGUI labelText;
        [SerializeField] protected TMP_InputField input;
        [SerializeField] protected FocusScrim scrim;

        [Serializable] public class StringEvent : UnityEvent<string> { }
        public StringEvent OnCommitted = new StringEvent();

        protected string focusValue;
        protected bool isFocused;

        public virtual string Value
        {
            get => input != null ? input.text : string.Empty;
            set { if (input != null) input.text = value ?? string.Empty; }
        }

        public string Label
        {
            get => labelText != null ? labelText.text : string.Empty;
            set { if (labelText != null) labelText.text = value ?? string.Empty; }
        }

        public bool IsFocused => isFocused;

        protected virtual void Awake()
        {
            if (input == null) return;
            input.onSelect.AddListener(HandleSelect);
            input.onEndEdit.AddListener(HandleEndEdit);
        }

        protected virtual void OnDestroy()
        {
            if (input == null) return;
            input.onSelect.RemoveListener(HandleSelect);
            input.onEndEdit.RemoveListener(HandleEndEdit);
        }

        private void HandleSelect(string _)
        {
            if (isFocused) return;
            isFocused = true;
            focusValue = input.text;
            OnFocused();
            if (scrim != null)
                scrim.Show(GetComponent<RectTransform>(), () => Blur(commit: true));
        }

        private void HandleEndEdit(string _) => Blur(commit: true);

        public void Blur(bool commit)
        {
            if (!isFocused) return;
            isFocused = false;

            var current = input.text;
            if (commit && current != focusValue)
                OnCommitted.Invoke(current);

            input.DeactivateInputField();
            if (scrim != null && scrim.IsShowing) scrim.Hide();
            OnBlurred();
        }

        /// <summary>Overridable hook for EditableTextArea to hide header etc.</summary>
        protected virtual void OnFocused() { }
        protected virtual void OnBlurred() { }
    }
}
```

- [ ] **Step 2: Verify compile**

Save. Check Unity Console — no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/EditableField.cs
git commit -m "feat(bot-settings): add EditableField component

Card-styled TMP_InputField wrapper with label + scrim integration.
Value getter/setter matches Manager.cs's re-wired read contract;
OnCommitted fires only when value changed since focus (prevents
spurious EnableSave calls). Blur(commit:true) is the single code
path for scrim-tap, onEndEdit, and keyboard-Done.

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Implement EditableTextArea

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/EditableTextArea.cs`

- [ ] **Step 1: Replace stub**

```csharp
using UnityEngine;
using UnityEngine.Events;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Multi-line variant of EditableField used for Business description
    /// and Prompt fields. Raises OnFullScreenFocusRequested so BotSettings
    /// can hide the header + tab bar (matching BotSettings.cs:743-747).
    /// </summary>
    public class EditableTextArea : EditableField
    {
        public UnityEvent OnFullScreenFocusRequested = new UnityEvent();
        public UnityEvent OnFullScreenFocusReleased = new UnityEvent();

        protected override void OnFocused() => OnFullScreenFocusRequested.Invoke();
        protected override void OnBlurred() => OnFullScreenFocusReleased.Invoke();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/EditableTextArea.cs
git commit -m "feat(bot-settings): add EditableTextArea with full-screen focus events

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Implement ToggleRow, SectionHeader, AddItemButton, SavedToast

**Rationale:** Grouping simple MonoBehaviour wrappers. Each is short enough to share one commit.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ToggleRow.cs`
- Modify: `Assets/Scripts/Main/BotSettings/SectionHeader.cs`
- Modify: `Assets/Scripts/Main/BotSettings/AddItemButton.cs`
- Modify: `Assets/Scripts/Main/BotSettings/SavedToast.cs`

- [ ] **Step 1: ToggleRow**

```csharp
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// iOS-style card toggle. Animates thumb position + track color via
    /// DOTween. Exposes the underlying Toggle so BotSettings keeps typed
    /// references named WhatsappToggle / TelegramToggle for the auth
    /// coroutines that read Toggle.isOn directly.
    /// </summary>
    public class ToggleRow : MonoBehaviour
    {
        [SerializeField] private Toggle toggle;
        [SerializeField] private Image trackImage;
        [SerializeField] private RectTransform thumb;
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private Color onColor = new Color(0.145f, 0.827f, 0.4f);   // #25D366
        [SerializeField] private Color offColor = new Color(0.878f, 0.878f, 0.878f); // #E0E0E0
        [SerializeField] private float thumbOffsetX = 20f;
        [SerializeField] private float animDuration = 0.2f;

        public Toggle Toggle => toggle;
        public string Label { get => labelText.text; set => labelText.text = value; }

        private Vector2 thumbOffAnchored;
        private Vector2 thumbOnAnchored;

        private void Awake()
        {
            thumbOffAnchored = thumb.anchoredPosition;
            thumbOnAnchored = thumbOffAnchored + new Vector2(thumbOffsetX, 0f);
            toggle.onValueChanged.AddListener(AnimateTo);
            ApplyImmediate(toggle.isOn);
        }

        private void OnDestroy() => toggle.onValueChanged.RemoveListener(AnimateTo);

        private void AnimateTo(bool on)
        {
            thumb.DOAnchorPos(on ? thumbOnAnchored : thumbOffAnchored, animDuration)
                 .SetEase(Ease.OutCubic);
            trackImage.DOColor(on ? onColor : offColor, animDuration);
        }

        private void ApplyImmediate(bool on)
        {
            thumb.anchoredPosition = on ? thumbOnAnchored : thumbOffAnchored;
            trackImage.color = on ? onColor : offColor;
        }
    }
}
```

- [ ] **Step 2: SectionHeader**

```csharp
using TMPro;
using UnityEngine;

namespace Automation.BotSettingsUI
{
    public class SectionHeader : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        public string Text { get => labelText.text; set => labelText.text = value; }
    }
}
```

- [ ] **Step 3: AddItemButton**

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>Dashed-border "+ Добавить товар" button. Styling in prefab.</summary>
    public class AddItemButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        public UnityEvent OnTap = new UnityEvent();

        private void Awake() => button.onClick.AddListener(() => OnTap.Invoke());
        private void OnDestroy() => button.onClick.RemoveListener(() => OnTap.Invoke());
    }
}
```

- [ ] **Step 4: SavedToast**

```csharp
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>Wraps the existing Saved GameObject with fade in / auto-hide.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SavedToast : MonoBehaviour
    {
        [SerializeField] private float visibleDuration = 1.5f;
        [SerializeField] private float fadeDuration = 0.2f;
        private CanvasGroup group;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            gameObject.SetActive(false);
        }

        public void Show()
        {
            StopAllCoroutines();
            StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            gameObject.SetActive(true);
            group.DOKill();
            group.DOFade(1f, fadeDuration);
            yield return new WaitForSeconds(visibleDuration);
            group.DOFade(0f, fadeDuration).OnComplete(() => gameObject.SetActive(false));
        }
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ToggleRow.cs \
        Assets/Scripts/Main/BotSettings/SectionHeader.cs \
        Assets/Scripts/Main/BotSettings/AddItemButton.cs \
        Assets/Scripts/Main/BotSettings/SavedToast.cs
git commit -m "feat(bot-settings): add ToggleRow, SectionHeader, AddItemButton, SavedToast

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Implement ProductCardView and ServiceCardView

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ProductCardView.cs`
- Modify: `Assets/Scripts/Main/BotSettings/ServiceCardView.cs`

- [ ] **Step 1: ProductCardView**

```csharp
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

        public event Action<ProductCardView> OnEditRequested;

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
```

- [ ] **Step 2: ServiceCardView**

Same content as ProductCardView but rename the class to `ServiceCardView` and the event parameter type to `ServiceCardView`. Copy-paste and rename — the two types are structurally identical but kept separate to match Manager.cs's separate Product/Service persistence paths and to avoid accidental cross-wiring.

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
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
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ProductCardView.cs \
        Assets/Scripts/Main/BotSettings/ServiceCardView.cs
git commit -m "feat(bot-settings): add ProductCardView and ServiceCardView

Display-only replacements for Product.cs / Service.cs. Name/Price/
Description properties are the re-wired read contract for Manager.cs.
Tap → OnEditRequested; no inline editing (that lives in ItemEditSheet).

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Implement ItemEditSheet

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings/ItemEditSheet.cs`

- [ ] **Step 1: Implementation**

```csharp
using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Automation.BotSettingsUI
{
    /// <summary>
    /// Bottom sheet for editing one ProductCardView or ServiceCardView.
    /// Holds its own FocusScrim scoped to the sheet — nested field focus
    /// raises above the sheet, not the main page.
    ///
    /// Delete routes through PopupUI so the existing finger-up confirm
    /// popup continues to work unchanged.
    /// </summary>
    public class ItemEditSheet : MonoBehaviour
    {
        [SerializeField] private RectTransform sheetRoot;
        [SerializeField] private EditableField nameField;
        [SerializeField] private EditableField priceField;
        [SerializeField] private EditableField descField;
        [SerializeField] private Button doneButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private GameObject deleteConfirmPopup;
        [SerializeField] private Button deleteConfirmYes;
        [SerializeField] private Button deleteConfirmNo;
        [SerializeField] private GameObject scrimBehind;  // dim behind sheet (separate from FocusScrim)
        [SerializeField] private CanvasGroup scrimBehindGroup;
        [SerializeField] private float slideDuration = 0.25f;
        [SerializeField] private float scrimAlpha = 0.5f;

        private ProductCardView boundProduct;
        private ServiceCardView boundService;
        private Vector2 hiddenAnchored;
        private Vector2 shownAnchored;

        public event Action<ProductCardView> OnProductDeleted;
        public event Action<ServiceCardView> OnServiceDeleted;
        public event Action OnAnyCommitted;

        private void Awake()
        {
            shownAnchored = sheetRoot.anchoredPosition;
            hiddenAnchored = new Vector2(shownAnchored.x, -sheetRoot.rect.height);
            sheetRoot.anchoredPosition = hiddenAnchored;
            gameObject.SetActive(false);

            doneButton.onClick.AddListener(Commit);
            deleteButton.onClick.AddListener(() => PopupUI.Show(deleteConfirmPopup));
            PopupUI.WireFingerUp(deleteConfirmYes, ConfirmDelete);
            PopupUI.WireFingerUp(deleteConfirmNo, () => PopupUI.Hide(deleteConfirmPopup));
        }

        public void Show(ProductCardView card)
        {
            boundProduct = card;
            boundService = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }

        public void Show(ServiceCardView card)
        {
            boundService = card;
            boundProduct = null;
            BindFields(card.Name, card.Price, card.Description);
            SlideIn();
        }

        private void BindFields(string n, string p, string d)
        {
            nameField.Value = n;
            priceField.Value = p;
            descField.Value = d;
        }

        private void SlideIn()
        {
            gameObject.SetActive(true);
            if (scrimBehind != null)
            {
                scrimBehind.SetActive(true);
                scrimBehindGroup.alpha = 0f;
                scrimBehindGroup.DOFade(scrimAlpha, slideDuration).SetEase(Ease.OutQuad);
            }
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(shownAnchored, slideDuration).SetEase(Ease.OutCubic);
        }

        public void Hide()
        {
            sheetRoot.DOKill();
            sheetRoot.DOAnchorPos(hiddenAnchored, slideDuration).SetEase(Ease.InCubic);
            if (scrimBehind != null)
            {
                scrimBehindGroup.DOFade(0f, slideDuration).SetEase(Ease.InQuad)
                    .OnComplete(() =>
                    {
                        scrimBehind.SetActive(false);
                        gameObject.SetActive(false);
                    });
            }
            else
            {
                Invoke(nameof(Deactivate), slideDuration);
            }
            boundProduct = null;
            boundService = null;
        }

        private void Deactivate() => gameObject.SetActive(false);

        private void Commit()
        {
            if (boundProduct != null)
            {
                boundProduct.Name = nameField.Value;
                boundProduct.Price = priceField.Value;
                boundProduct.Description = descField.Value;
            }
            else if (boundService != null)
            {
                boundService.Name = nameField.Value;
                boundService.Price = priceField.Value;
                boundService.Description = descField.Value;
            }
            OnAnyCommitted?.Invoke();
            Hide();
        }

        private void ConfirmDelete()
        {
            PopupUI.Hide(deleteConfirmPopup);
            if (boundProduct != null)
            {
                var card = boundProduct;
                Hide();
                OnProductDeleted?.Invoke(card);
            }
            else if (boundService != null)
            {
                var card = boundService;
                Hide();
                OnServiceDeleted?.Invoke(card);
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Main/BotSettings/ItemEditSheet.cs
git commit -m "feat(bot-settings): add ItemEditSheet bottom-sheet editor

One sheet instance per tab (Products / Services), reused across
cards. Delete routes via PopupUI finger-up for confirm popup. On
Commit writes to bound card + fires OnAnyCommitted so BotSettings
can trigger Manager.Instance.EnableSave().

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Rewrite BotSettings.cs + extract auth to partial

**Rationale:** All component scripts are in place. The main controller now becomes a thin wire-up. Auth coroutines lift verbatim into a partial class — same methods, only the two or three number-field text-access lines re-wire.

**Files:**
- Modify: `Assets/Scripts/Main/BotSettings.cs` (full rewrite)
- Create: `Assets/Scripts/Main/BotSettings.Auth.cs` (partial class)

- [ ] **Step 1: Create BotSettings.Auth.cs**

Move these methods out of `BotSettings.cs` verbatim, wrapped in `public partial class BotSettings`:

- `WhatsappChannelToggleChanged(bool)` (line ~409)
- `TelegramChannelToggleChanged(bool)` (line ~421)
- `OpenConfirmChangeWhatsappNumberPopup`, `ConfirmChangeWhatsappNumber`, `CancelChangeWhatsappNumber` (~433-442)
- `OpenConfirmChangeTelegramNumberPopup`, `ConfirmChangeTelegramNumber`, `CancelChangeTelegramNumber` (~444-453)
- `OpenAuthorization(bool)` (~796)
- `SetButtonText(Button, string)` (~809)
- `OpenWhatsappAuthorization`, `WhatsappAuthorizationBack`, `WhatsappAuthorizationDone` (~816-853)
- `OpenWhatsappQRPanel` (coroutine, ~855)
- `CloseWhatsappQRPanel`, `OpenWhatsappCodePanel`, `WhatsappNumberInputChanged`, `GetWhatsappCode`, `CloseWhatsappCodePanel`, `GetWhatsappProfileStatus`, `CheckWhatsappAuthorization`, `CheckWhatsappUnauthorizationOutsideApp`, `UnauthorizeWhatsapp` (~931-1200)
- All Telegram equivalents (lines ~1200–1839)

Change only these three field-access patterns inside these methods:
```
WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text → WhatsappNumberField.Value
TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text → TelegramNumberField.Value
BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text → BotNameField.Value
```
And the two lines that `SetActive(!text.Equals(""))` the number button's parent → `WhatsappNumberField.gameObject.SetActive(...)`.

Header (file skeleton):
```csharp
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using Automation.BotSettingsUI;

public partial class BotSettings
{
    // All auth methods moved here. Keep identical logic.
}
```

- [ ] **Step 2: Rewrite BotSettings.cs as the thin controller**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Automation.BotSettingsUI;

public partial class BotSettings : MonoBehaviour
{
    #region Serialized — Tabs
    [SerializeField] private GameObject General;
    [SerializeField] private GameObject Business;
    [SerializeField] private GameObject Product;
    [SerializeField] private GameObject Service;
    [SerializeField] private GameObject Prompt;
    [SerializeField] private Button GeneralTabButton;
    [SerializeField] private Button BusinessTabButton;
    [SerializeField] private Button ProductTabButton;
    [SerializeField] private Button ServiceTabButton;
    [SerializeField] private Button PromptTabButton;
    [SerializeField] private RectTransform headerGroup;
    [SerializeField] private RectTransform tabBarGroup;
    [SerializeField] private FocusScrim mainScrim;
    #endregion

    #region Serialized — General tab
    [SerializeField] public EditableField BotNameField;
    public TMP_Dropdown BusinessTypeDropdown;
    [SerializeField] public ToggleRow whatsappRow;
    [SerializeField] public ToggleRow telegramRow;
    public Toggle WhatsappToggle => whatsappRow != null ? whatsappRow.Toggle : null;
    public Toggle TelegramToggle => telegramRow != null ? telegramRow.Toggle : null;
    [SerializeField] public EditableField WhatsappNumberField;
    [SerializeField] public EditableField TelegramNumberField;
    #endregion

    #region Serialized — Business / Prompt
    [SerializeField] public EditableTextArea BusinessField;
    [SerializeField] public EditableTextArea PromptField;
    #endregion

    #region Serialized — Products / Services
    [SerializeField] public GameObject ProductPrefab;
    [SerializeField] public GameObject ServicePrefab;
    [SerializeField] public RectTransform ProductsParent;
    [SerializeField] public RectTransform ServicesParent;
    [SerializeField] private AddItemButton addProductButton;
    [SerializeField] private AddItemButton addServiceButton;
    [SerializeField] private ItemEditSheet productEditSheet;
    [SerializeField] private ItemEditSheet serviceEditSheet;
    public AddItemButton AddProductButton => addProductButton;
    public AddItemButton AddServiceButton => addServiceButton;
    #endregion

    #region Serialized — Auth (names preserved for Manager + auth partial)
    [SerializeField] public GameObject WhatsappAuthorization;
    [SerializeField] public GameObject WhatsappQRPanel;
    [SerializeField] public GameObject WhatsappCodePanel;
    [SerializeField] public GameObject TelegramAuthorization;
    [SerializeField] public GameObject TelegramQRPanel;
    [SerializeField] public GameObject TelegramCodePanel;
    [SerializeField] private TextMeshProUGUI TelegramPhoneTitle;
    [SerializeField] private TextMeshProUGUI TelegramPhoneBody;
    [SerializeField] public SavedToast Saved;
    [SerializeField] private GameObject ConfirmChangeWhatsappNumberPopup;
    [SerializeField] private GameObject ConfirmChangeTelegramNumberPopup;
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    [SerializeField] public Button WhatsappAuthorizationBackButton;
    [SerializeField] private Button WhatsappAuthorizationDoneBotton;
    [SerializeField] private Button OpenWhatsappQRPanelButton;
    [SerializeField] private Button OpenWhatsappCodePanelButton;
    [SerializeField] private Button CloseWhatsappQRPanelButton;
    [SerializeField] private Button CloseWhatsappCodePanelButton;
    [SerializeField] private Button GetWhatsappCodeButton;
    [SerializeField] public Button TelegramAuthorizationBackButton;
    [SerializeField] private Button TelegramAuthorizationDoneBotton;
    [SerializeField] private Button OpenTelegramQRPanelButton;
    [SerializeField] private Button OpenTelegramCodePanelButton;
    [SerializeField] private Button CloseTelegramQRPanelButton;
    [SerializeField] private Button CloseTelegramCodePanelButton;
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private Button SendTelegramCodeButton;
    [SerializeField] private Button ConfirmChangeWhatsappNumberButton;
    [SerializeField] private Button CancelChangeWhatsappNumberButton;
    [SerializeField] private Button ConfirmChangeTelegramNumberButton;
    [SerializeField] private Button CancelChangeTelegramNumberButton;
    [SerializeField] private Button UploadPriceListButton;
    public TMP_InputField WhatsappNumberInput;  // used inside auth code flow
    public TMP_InputField TelegramNumberInput;
    public TMP_InputField TelegramCodeInput;
    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;
    private string telegramPhoneTitleInitial;
    private string telegramPhoneBodyInitial;
    #endregion

    private float headerOriginalY;
    private float tabBarOriginalY;

    void Start()
    {
        if (TelegramPhoneTitle != null) telegramPhoneTitleInitial = TelegramPhoneTitle.text;
        if (TelegramPhoneBody != null) telegramPhoneBodyInitial = TelegramPhoneBody.text;

        headerOriginalY = headerGroup != null ? headerGroup.anchoredPosition.y : 0f;
        tabBarOriginalY = tabBarGroup != null ? tabBarGroup.anchoredPosition.y : 0f;

        WireTabs();
        WireFields();
        WireProductsAndServices();
        WireAuthButtons();
    }

    public void OnEnable()
    {
        StartCoroutine(CheckWhatsappUnauthorizationOutsideApp());
        StartCoroutine(CheckTelegramUnauthorizationOutsideApp());
    }

    public void OnDisable() => OpenGeneralTab();

    //////////////////////////////////////// TABS ////////////////////////////////////////

    public void OpenGeneralTab()  => SetActiveTab(general: true);
    public void OpenBusinessTab() => SetActiveTab(business: true);
    public void OpenProductTab()  => SetActiveTab(product: true);
    public void OpenServiceTab()  => SetActiveTab(service: true);
    public void OpenPromptTab()   => SetActiveTab(prompt: true);

    private void SetActiveTab(bool general = false, bool business = false,
                              bool product = false, bool service = false,
                              bool prompt = false)
    {
        General.SetActive(general);
        Business.SetActive(business);
        Product.SetActive(product);
        Service.SetActive(service);
        Prompt.SetActive(prompt);
    }

    private void WireTabs()
    {
        if (GeneralTabButton != null)  GeneralTabButton.onClick.AddListener(OpenGeneralTab);
        if (BusinessTabButton != null) BusinessTabButton.onClick.AddListener(OpenBusinessTab);
        if (ProductTabButton != null)  ProductTabButton.onClick.AddListener(OpenProductTab);
        if (ServiceTabButton != null)  ServiceTabButton.onClick.AddListener(OpenServiceTab);
        if (PromptTabButton != null)   PromptTabButton.onClick.AddListener(OpenPromptTab);
    }

    //////////////////////////////////////// FIELDS ////////////////////////////////////////

    private void WireFields()
    {
        // Simple fields → EnableSave
        BotNameField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        WhatsappNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        TelegramNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());

        // Textareas: fullscreen focus hides header/tabs
        BusinessField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        BusinessField.OnFullScreenFocusRequested.AddListener(HideHeaderAndTabs);
        BusinessField.OnFullScreenFocusReleased.AddListener(RestoreHeaderAndTabs);

        PromptField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        PromptField.OnFullScreenFocusRequested.AddListener(HideHeaderAndTabs);
        PromptField.OnFullScreenFocusReleased.AddListener(RestoreHeaderAndTabs);

        if (BusinessTypeDropdown != null)
            BusinessTypeDropdown.onValueChanged.AddListener(_ => Manager.Instance.EnableSave());

        if (WhatsappToggle != null)
            WhatsappToggle.onValueChanged.AddListener(WhatsappChannelToggleChanged);
        if (TelegramToggle != null)
            TelegramToggle.onValueChanged.AddListener(TelegramChannelToggleChanged);
    }

    private void HideHeaderAndTabs()
    {
        if (headerGroup != null) headerGroup.gameObject.SetActive(false);
        if (tabBarGroup != null) tabBarGroup.gameObject.SetActive(false);
    }

    private void RestoreHeaderAndTabs()
    {
        if (headerGroup != null) headerGroup.gameObject.SetActive(true);
        if (tabBarGroup != null) tabBarGroup.gameObject.SetActive(true);
    }

    //////////////////////////////////////// PRODUCTS / SERVICES ////////////////////////////////////////

    private void WireProductsAndServices()
    {
        if (addProductButton != null) addProductButton.OnTap.AddListener(AddProduct);
        if (addServiceButton != null) addServiceButton.OnTap.AddListener(AddService);

        if (productEditSheet != null)
        {
            productEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            productEditSheet.OnProductDeleted += DeleteProductCard;
        }
        if (serviceEditSheet != null)
        {
            serviceEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            serviceEditSheet.OnServiceDeleted += DeleteServiceCard;
        }

        WireExistingProductCards();
        WireExistingServiceCards();
    }

    private void WireExistingProductCards()
    {
        for (int i = 0; i < ProductsParent.childCount; i++)
        {
            var card = ProductsParent.GetChild(i).GetComponent<ProductCardView>();
            if (card != null) BindProductCard(card);
        }
    }

    private void WireExistingServiceCards()
    {
        for (int i = 0; i < ServicesParent.childCount; i++)
        {
            var card = ServicesParent.GetChild(i).GetComponent<ServiceCardView>();
            if (card != null) BindServiceCard(card);
        }
    }

    private void BindProductCard(ProductCardView card)
    {
        card.OnEditRequested += c => productEditSheet.Show(c);
    }

    private void BindServiceCard(ServiceCardView card)
    {
        card.OnEditRequested += c => serviceEditSheet.Show(c);
    }

    public void AddProduct()
    {
        var go = Instantiate(ProductPrefab,
                             ProductPrefab.transform.position,
                             ProductPrefab.transform.rotation,
                             ProductsParent);
        // Preserve "AddProductButton is always last sibling" invariant
        // that Manager.CloseSettings + SaveSettings rely on.
        if (addProductButton != null)
            addProductButton.transform.parent.SetAsLastSibling();

        var card = go.GetComponent<ProductCardView>();
        if (card != null) BindProductCard(card);

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        Manager.Instance.EnableSave();
    }

    public void AddService()
    {
        var go = Instantiate(ServicePrefab,
                             ServicePrefab.transform.position,
                             ServicePrefab.transform.rotation,
                             ServicesParent);
        if (addServiceButton != null)
            addServiceButton.transform.parent.SetAsLastSibling();

        var card = go.GetComponent<ServiceCardView>();
        if (card != null) BindServiceCard(card);

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        Manager.Instance.EnableSave();
    }

    private void DeleteProductCard(ProductCardView card)
    {
        Destroy(card.gameObject);
        Manager.Instance.EnableSave();
    }

    private void DeleteServiceCard(ServiceCardView card)
    {
        Destroy(card.gameObject);
        Manager.Instance.EnableSave();
    }

    //////////////////////////////////////// AUTH WIRING ////////////////////////////////////////

    private void WireAuthButtons()
    {
        if (WhatsappAuthorizationBackButton != null)
            WhatsappAuthorizationBackButton.onClick.AddListener(WhatsappAuthorizationBack);
        if (WhatsappAuthorizationDoneBotton != null)
            WhatsappAuthorizationDoneBotton.onClick.AddListener(WhatsappAuthorizationDone);
        if (OpenWhatsappQRPanelButton != null)
            OpenWhatsappQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenWhatsappQRPanel()));
        if (OpenWhatsappCodePanelButton != null)
            OpenWhatsappCodePanelButton.onClick.AddListener(OpenWhatsappCodePanel);
        if (CloseWhatsappQRPanelButton != null)
            CloseWhatsappQRPanelButton.onClick.AddListener(CloseWhatsappQRPanel);
        if (CloseWhatsappCodePanelButton != null)
            CloseWhatsappCodePanelButton.onClick.AddListener(CloseWhatsappCodePanel);
        if (GetWhatsappCodeButton != null)
            GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));

        if (TelegramAuthorizationBackButton != null)
            TelegramAuthorizationBackButton.onClick.AddListener(TelegramAuthorizationBack);
        if (TelegramAuthorizationDoneBotton != null)
            TelegramAuthorizationDoneBotton.onClick.AddListener(TelegramAuthorizationDone);
        if (OpenTelegramQRPanelButton != null)
            OpenTelegramQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenTelegramQRPanel()));
        if (OpenTelegramCodePanelButton != null)
            OpenTelegramCodePanelButton.onClick.AddListener(OpenTelegramCodePanel);
        if (CloseTelegramQRPanelButton != null)
            CloseTelegramQRPanelButton.onClick.AddListener(CloseTelegramQRPanel);
        if (CloseTelegramCodePanelButton != null)
            CloseTelegramCodePanelButton.onClick.AddListener(CloseTelegramCodePanel);
        if (GetTelegramCodeButton != null)
            GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        if (SendTelegramCodeButton != null)
            SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));

        if (WhatsappNumberInput != null)
            WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        if (TelegramNumberInput != null)
            TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        if (TelegramCodeInput != null)
            TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

        if (ConfirmChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeWhatsappNumberButton, ConfirmChangeWhatsappNumber);
        if (CancelChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeWhatsappNumberButton, CancelChangeWhatsappNumber);
        if (ConfirmChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeTelegramNumberButton, ConfirmChangeTelegramNumber);
        if (CancelChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeTelegramNumberButton, CancelChangeTelegramNumber);

        if (UploadPriceListButton != null)
            UploadPriceListButton.onClick.AddListener(UploadPriceList);
    }
}
```

- [ ] **Step 3: Delete Product.cs and Service.cs**

```bash
cd /Users/ayan/Projects/Automation
git rm Assets/Scripts/Main/Product.cs Assets/Scripts/Main/Service.cs
```

- [ ] **Step 4: Verify compile**

Open Unity. Expect errors only in `Manager.cs` (it still references `GetComponent<Product>()` etc.); those will be fixed in Task 9. No other compile errors should appear.

If any other errors appear in BotSettings.cs itself, fix by returning to Step 2 of this task (missing method, typo). Do not proceed to Task 9 until BotSettings.cs / BotSettings.Auth.cs compile and only Manager.cs errors remain.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Main/BotSettings.cs \
        Assets/Scripts/Main/BotSettings.Auth.cs
git commit -m "refactor: rewrite BotSettings.cs as thin controller; extract auth to partial

BotSettings.cs shrinks from 1839 lines to a focused controller that
wires components. All auth coroutines lift verbatim into
BotSettings.Auth.cs with only number-field text-access lines
re-wired to EditableField.Value. Deletes Product.cs / Service.cs
(replaced by ProductCardView / ServiceCardView).

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Re-wire Manager.cs reads/writes

**Rationale:** Spec Section 6 mapping table. Logic is byte-identical; only the text-access fragments change. After this task, compile is clean.

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — methods `SaveSettings` (line ~330), `CloseSettings` (~442), `EnableSave` (~497), `CheckProductsOrServicesChanged` (~528)

- [ ] **Step 1: In `SaveSettings`, replace every occurrence**

Mapping (each applies everywhere in this method):

| Old | New |
|---|---|
| `openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.BotNameField.Value` |
| `openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.BusinessField.Value` |
| `openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.PromptField.Value` |
| `openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.WhatsappNumberField.Value` |
| `openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.TelegramNumberField.Value` |
| `openBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(...)` | `openBotSettings.WhatsappNumberField.gameObject.SetActive(...)` |
| `openBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(...)` | `openBotSettings.TelegramNumberField.gameObject.SetActive(...)` |
| `product.GetComponent<Product>().ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `product.GetComponent<ProductCardView>().Name` |
| `product.GetComponent<Product>().PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `product.GetComponent<ProductCardView>().Price` |
| `product.GetComponent<Product>().DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `product.GetComponent<ProductCardView>().Description` |
| `service.GetComponent<Service>().ServiceButton...` | `service.GetComponent<ServiceCardView>().Name` |
| `service.GetComponent<Service>().PriceButton...` | `service.GetComponent<ServiceCardView>().Price` |
| `service.GetComponent<Service>().DescriptionButton...` | `service.GetComponent<ServiceCardView>().Description` |

Keep the loops, PlayerPrefs calls, key strings, and the `Trim()` lines exactly as-is. The only change is how the text is read.

Note: the `Trim()` in the original method mutates nothing (it returns a new string that's immediately discarded). Preserve the call literally so behavior remains byte-identical. Example:
```csharp
// Before
product.GetComponent<Product>().ProductButton.transform.GetChild(0)
    .GetComponent<TextMeshProUGUI>().text.Trim();

// After
product.GetComponent<ProductCardView>().Name.Trim();
```

- [ ] **Step 2: In `CloseSettings`, apply the same mapping**

`CloseSettings` is the reverse direction — it writes into views. Replace every site that assigns to `.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = …` with the matching `.Value =` setter. Same for `ProductCardView.Name/Price/Description`.

Also fix the instantiate blocks:
```csharp
// Before
Product recreatedProduct = Instantiate(ProductPrefab, ..., openBotSettings.AddProductButton.transform.parent.parent).GetComponent<Product>();
recreatedProduct.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(...);

// After
var recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, openBotSettings.ProductsParent).GetComponent<ProductCardView>();
recreatedProduct.Name = PlayerPrefs.GetString(openBot.name + "Product" + p, "");
recreatedProduct.Price = PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "");
recreatedProduct.Description = PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "");
```

Then the `openBotSettings.AddProductButton.transform.parent.SetAsLastSibling()` line is unchanged (the AddItemButton.transform.parent still holds the "button row" and still needs to be the last child so PlayerPrefs indexing works).

Apply the same to the Service loop.

- [ ] **Step 3: In `EnableSave`, apply the same mapping**

The giant boolean expression at lines ~501–510 reads from the same fields. Replace each text-access fragment per the table in Step 1. Keep the OR-chain structure. `WhatsappToggle.isOn` and `TelegramToggle.isOn` are unchanged (still read directly from the Toggle via the `WhatsappToggle` / `TelegramToggle` getter on `BotSettings`).

- [ ] **Step 4: In `CheckProductsOrServicesChanged`, apply the same mapping**

Change the child-indexed reads to property reads:
```csharp
// Before (inside the loop)
!openBotSettings.ProductsParent.transform.GetChild(p).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p, ""))

// After
!openBotSettings.ProductsParent.GetChild(p).GetComponent<ProductCardView>().Name.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p, ""))
```
(Price uses `.Price`, Description uses `.Description`. Same for Service.)

Also note: `openBotSettings.ProductsParent` was a `GameObject` before — in the new `BotSettings` it's a `RectTransform`, so `.transform.GetChild(...)` becomes `.GetChild(...)`.

- [ ] **Step 5: Verify compile**

Open Unity. Console should be clean — zero errors.

If errors remain, grep for the old patterns in Manager.cs:
```bash
grep -n "GetComponent<Product>\|GetComponent<Service>\|BotNameButton\|WhatsappNumberButton\|TelegramNumberButton\|BusinessInputButton\|PromptInputButton" /Users/ayan/Projects/Automation/Assets/Scripts/Main/Manager.cs
```
Each hit is a missed re-wire. Fix all remaining hits, then re-verify.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/Manager.cs
git commit -m "refactor: re-wire Manager.cs reads to new BotSettings components

SaveSettings / CloseSettings / EnableSave / CheckProductsOrServicesChanged
now read through EditableField.Value and ProductCardView / ServiceCardView
property accessors. PlayerPrefs keys, n8n webhooks, and the overall
save/load flow are unchanged byte-for-byte.

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Rebuild prefabs + scene + manual smoke test

**Rationale:** Code is complete. This task is Unity-Editor-only: rebuild prefabs to match the mockup visuals, then walk the smoke-test checklist.

**Files (Unity Editor):**
- Rebuild: `Assets/Prefabs/Product.prefab`
- Rebuild: `Assets/Prefabs/Service.prefab`
- Rebuild: `Assets/Prefabs/BotSettings.prefab`
- Rebuild: `Assets/Scenes/Main.unity` (re-wire the BotSettings subtree references)

### Visual tokens reference

Copy these from `Design/mockup.html` `:root` (lines 10–38):
```
bg:          #F0F2F5
card:        #FFFFFF
text:        #1A1A2E
text-muted:  #8E8E93
border:      #E4E6EB
primary:     #1B7CEB
primary-lt:  #E8F2FD
whatsapp:    #25D366
danger grad: #E53935 → #C62828
radius:      14 (bot cards, delete button)
radius-sm:   10 (fields, toggles, product thumb)
font:        Inter (fallback: TMP default)
shadow:      0 1px 3px rgba(0,0,0,0.08)
```

- [ ] **Step 1: Rebuild Product.prefab**

Hierarchy:
```
Product (RectTransform, ProductCardView, Button "rootButton")
├─ Thumb (Image, radius 10, bg=#F0F2F5, 50×50)
│   └─ Emoji (TMP, 24px, centered)
├─ Info (VerticalLayoutGroup)
│   ├─ Name (TMP, 15px 600, text=#1A1A2E)
│   ├─ Price (TMP, 13px 600, text=#1B7CEB)
│   └─ Desc (TMP, 12px, text=#8E8E93)
├─ Chevron (Image, 16×16, color=#C7C7CC)
```
Card root: white bg, RoundedCorners (radius 10), padding 14×16, shadow.

Inspector wiring on `ProductCardView`:
- `nameLabel` → Info/Name
- `priceLabel` → Info/Price
- `descLabel` → Info/Desc
- `thumb` → Thumb Image
- `rootButton` → root Button

Save prefab. **Delete the legacy Product.cs-era buttons / TMP_InputFields inside the prefab.**

- [ ] **Step 2: Rebuild Service.prefab**

Identical structure to Product.prefab but with `ServiceCardView` on the root and a service-style emoji.

- [ ] **Step 3: Rebuild BotSettings.prefab — Header + Tabs**

Top of prefab:
- `Header` (RectTransform, `headerGroup` serialized on BotSettings)
  - Back button (left, 24×24, color=#1B7CEB)
  - Title TMP (20px 700, bot name, bound by Manager)
  - Save button (right; `SaveButton` is a Manager-level ref — do NOT move it into BotSettings)
- `TabBar` (`tabBarGroup` serialized on BotSettings)
  - 5 tab buttons: Основное / Бизнес / Продукты / Услуги / Промпты
  - Active tab underline: 2px, color=#1B7CEB

- [ ] **Step 4: Rebuild BotSettings.prefab — General tab**

Under `General`:
- SectionHeader "ИНФОРМАЦИЯ"
- `BotNameField` (EditableField prefab instance; label="Имя бота")
- Dropdown `BusinessTypeDropdown` styled as a settings field
- SectionHeader "ПОДКЛЮЧЕНИЯ"
- `whatsappRow` (ToggleRow, label="WhatsApp")
- `telegramRow` (ToggleRow, label="Telegram")
- Number-display fields:
  - `WhatsappNumberField` (EditableField, shown only when value non-empty)
  - `TelegramNumberField` (EditableField, shown only when value non-empty)
- Delete-bot button (DangerButton, gradient, text "Удалить бота")

- [ ] **Step 5: Rebuild BotSettings.prefab — Business, Prompt tabs**

Under `Business`:
- SectionHeader "ОПИСАНИЕ БИЗНЕСА"
- `BusinessField` (EditableTextArea, tall card, multi-line TMP_InputField)

Under `Prompt`:
- SectionHeader "ПРОМПТ"
- `PromptField` (EditableTextArea, tall card)

- [ ] **Step 6: Rebuild BotSettings.prefab — Products, Services tabs**

Under `Product`:
- SectionHeader "КАТАЛОГ ТОВАРОВ"
- `ProductsParent` (RectTransform, VerticalLayoutGroup)
  - (empty — populated at runtime by Manager.CloseSettings / AddProduct)
- `addProductButton` row (AddItemButton component, text "Добавить товар")

Under `Service`: same structure, text "Добавить услугу".

- [ ] **Step 7: Add FocusScrim to BotSettings canvas**

At the top-level of the BotSettings canvas:
- `mainScrim` GameObject
  - `ScrimRoot` (Image, black, alpha 0, raycast target true, CanvasGroup)
  - `RaisedLayer` (empty RectTransform, last sibling)
- Attach `FocusScrim` component to the parent.
- Wire its serialized fields to scrimRoot / scrimGroup / scrimImage / raisedLayer.
- Set `mainScrim` on each `EditableField` / `EditableTextArea` used on the main page (NOT on sheet-internal fields — those get the sheet's own scrim).

- [ ] **Step 8: Wire ItemEditSheet instances**

Two instances, one per tab:
- `productEditSheet` under `Product`:
  - sheetRoot (RectTransform anchored to bottom edge of phone)
  - 3× EditableField (nameField, priceField, descField; labels: "Название", "Цена", "Описание")
  - Кнопка "Готово" (PrimaryButton) → wired as `doneButton`
  - Кнопка "Удалить" (DangerButton) → wired as `deleteButton`
  - deleteConfirmPopup GameObject (reuse existing delete popup prefab from legacy Product.prefab)
  - deleteConfirmYes / deleteConfirmNo buttons inside the popup
  - scrimBehind + scrimBehindGroup (the sheet's own dim layer, distinct from `mainScrim`)
- Same for `serviceEditSheet` under `Service`.

- [ ] **Step 9: Re-parent existing Auth panels**

The `WhatsappAuthorization`, `WhatsappQRPanel`, `WhatsappCodePanel`, `TelegramAuthorization`, `TelegramQRPanel`, `TelegramCodePanel`, their QR `RawImage`s, their buttons, the `WhatsappCodeTimer`, `TelegramCodeTimer`, and the `ConfirmChangeWhatsappNumberPopup` / `ConfirmChangeTelegramNumberPopup` GameObjects move from the old prefab into the new BotSettings prefab **unchanged** — don't re-style, don't re-child internally. Just re-parent and re-wire the serialized fields on the new `BotSettings` component.

Also re-parent the `Saved` GO and add a `CanvasGroup` if missing; attach `SavedToast` component.

- [ ] **Step 10: Re-wire Main.unity**

Open `Assets/Scenes/Main.unity`. Find the BotSettings subtree under its parent. Replace the old prefab instance with the new `BotSettings.prefab`. Re-assign any external references (e.g. `Manager.BotSettingsParentStatic`) that may have pointed into the old subtree by name.

On `Manager` in the scene: `ProductPrefab` and `ServicePrefab` still point to the legacy paths — update them to the rebuilt `Product.prefab` / `Service.prefab`.

- [ ] **Step 11: Smoke-test checklist**

Enter Play mode. Walk each item from the spec's Section 10 list, in order. Tick each only after observing the correct behavior:

1. Open a bot → General tab shows with correct visuals, save button disabled.
2. Change bot name → save button enables. Revert name → disables again.
3. Tap each toggle → visual state animates, save-dirty triggers.
4. Change business type dropdown → dirty triggers.
5. Edit business description → scrim appears, header+tabs hide, keyboard works, commit updates field, save-dirty works, header+tabs restore.
6. Edit prompt text → same.
7. Switch between all 5 tabs → no layout jank, correct active-tab underline.
8. Products: Add → save-dirty enables. Tap card → sheet + scrim. Edit fields → Готово commits. Delete → finger-up confirm popup, card destroys, save-dirty stays set.
9. Services: same.
10. WhatsApp toggle on → auth opens, QR + phone-code flows work.
11. Telegram toggle on → same.
12. Number change confirm fires on finger-up.
13. Save → PlayerPrefs written, n8n called, toast shows, button disables.
14. Close without save → reverts to last saved.
15. Delete bot (General tab) → confirm popup → bot removed from BotsPage.
16. Close + reopen app → bot loads correctly (PlayerPrefs keys unchanged).

If any item fails, stop and fix before committing. The commit message lists what was verified.

- [ ] **Step 12: Final commit**

```bash
git add Assets/Prefabs/BotSettings.prefab \
        Assets/Prefabs/Product.prefab \
        Assets/Prefabs/Service.prefab \
        Assets/Scenes/Main.unity
git commit -m "feat(bot-settings): rebuild prefabs + scene to match mockup pages 7-8

Prefabs re-authored against Design/mockup.html tokens. All 16
smoke-test items from the design spec verified in Play mode at
1080x2400: dirty-tracking save button, scrim+field lifecycle,
product/service bottom-sheet edit, WhatsApp/Telegram auth,
number-change confirm, save/n8n, delete bot, PlayerPrefs persist.

Co-Authored-By: Claude Opus 4 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage:**
- Visual tokens (spec §4): Task 10 step 1 + visual reference block. ✔
- EditableField, EditableTextArea (spec §5.1, §5.2): Tasks 3, 4. ✔
- ToggleRow (§5.3): Task 5. ✔
- SectionHeader (§5.4): Task 5. ✔
- ProductCardView / ServiceCardView (§5.5): Task 6. ✔
- ItemEditSheet (§5.6): Task 7. ✔
- AddItemButton (§5.7): Task 5. ✔
- FocusScrim (§5.8): Task 2. ✔
- SavedToast (§5.9): Task 5. ✔
- BotSettings rewrite + Auth partial (§5.10): Task 8. ✔
- Manager.cs re-wires (§6): Task 9. ✔
- Focus/scrim sequence (§8): implemented by FocusScrim + EditableField (Tasks 2–4). ✔
- Smoke-test checklist (§10): Task 10 step 11. ✔
- Build sequence (§11): Tasks 1–10 aligned 1-to-1. ✔

**Placeholder scan:**
- No "TBD" / "TODO" / "implement later" in the plan body (spec's "out of scope" section is distinct and intentional).
- Every code block is complete and compilable.
- Re-wire mappings in Task 9 are exhaustive per the spec §6 table.

**Type consistency:**
- `EditableField.Value` (string) → read by `Manager.SaveSettings`, `Manager.EnableSave`, `Manager.CloseSettings`, `BotSettings.Auth.cs`. Consistent.
- `ProductCardView.Name/Price/Description` (string properties) → read by `Manager.SaveSettings`, `Manager.CheckProductsOrServicesChanged`; written by `Manager.CloseSettings` + `ItemEditSheet.Commit`. Consistent.
- `FocusScrim.Show(RectTransform, Action)` → called by `EditableField` and `EditableTextArea` (via inherited code). Consistent.
- `BotSettings.WhatsappToggle` / `TelegramToggle` remain `Toggle` getters → unchanged surface for auth partial. Consistent.
- `ItemEditSheet.Show(ProductCardView)` + overload `Show(ServiceCardView)` → bound by `BotSettings.BindProductCard` / `BindServiceCard`. Consistent.

**Notes for the implementer:**
- Namespace: all new components live in `Automation.BotSettingsUI`. The partial `BotSettings` stays in the default (global) namespace to match the legacy reference `Manager.openBotSettings`.
- `DelayedFingerUpAction` is an existing type; don't create a new one.
- `RoundedCorners` package is already in the project — use the component on cards.
- Do NOT introduce TDD tests as part of this plan. Spec §11 scopes verification to manual smoke testing; bootstrapping an EditMode asmdef is out of scope.

