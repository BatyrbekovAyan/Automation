# Bot Settings Redesign — Design Spec

**Date:** 2026-04-17
**Branch:** `ui-support-scripts-and-refinements`
**Owner:** Ayan
**Scope:** `Assets/Scenes/Main.unity` → `BotSettings` subtree, `Assets/Prefabs/BotSettings.prefab`, `Assets/Prefabs/Product.prefab`, `Assets/Prefabs/Service.prefab`, `Assets/Scripts/Main/BotSettings.cs`, `Assets/Scripts/Main/Product.cs`, `Assets/Scripts/Main/Service.cs`, targeted read/write re-wiring in `Assets/Scripts/Main/Manager.cs`.

---

## 1. Goal

Deliver a professional, pixel-accurate Bot Settings page that matches `Design/mockup.html` pages 7 (General tab) and 8 (Products tab), while preserving every existing behavior: dirty-tracking save button, WhatsApp/Telegram authorization flows, PlayerPrefs persistence, n8n webhook integration, product/service CRUD with finger-up delete confirmation, and Russian-language strings.

The implementation must remove two specific patterns the current code relies on:

1. The "Button-with-child-TextMeshProUGUI overlaid by a hidden TMP_InputField that swaps in on click" hack. The new fields are always real, functional `TMP_InputField`s.
2. The monolithic `CloseInputBackground()` method in `BotSettings.cs` (~160 lines, handles all tabs via `transform.GetChild(N).GetChild(M)...` indexing). Replaced by a reusable `EditableField` + `FocusScrim` pair that owns its own lifecycle.

A new focus-scrim UX is added: tapping any input raises that field above a full-screen semi-transparent dim; tapping the scrim commits the typed text and dismisses.

## 2. Non-goals

- No changes to `Manager.cs` save/load/webhook **logic**. Only field-reference reads and writes change.
- No changes to `PopupUI.cs`.
- No changes to authorization coroutines (`CheckWhatsappAuthorization`, `GetWhatsappCode`, `GetWhatsappProfileStatus`, and the Telegram equivalents). They are lifted verbatim into a `BotSettings.Auth.cs` partial for readability.
- No changes to PlayerPrefs key format. Keys like `{botName}Product{i}Price` continue to work so existing saved bots load correctly.
- No redesign of any other page (BotsPage, ChatPage, Profile, Add Bot). Scope is Bot Settings and its child prefabs only.

## 3. UX decisions (locked)

| # | Decision | Rationale |
|---|---|---|
| 1 | **Product/Service cards open a bottom-sheet detail view on tap.** The card itself displays emoji + name + price + description + chevron. The sheet contains three `EditableField`s (name, price, description) + Удалить + Готово. | Matches the mockup's chevron affordance. Cleaner than the current inline 3-button-per-card pattern. One scrim covers the page, the sheet holds its own. |
| 2 | **Scrim outside-tap commits the typed text.** Matches the existing `onEndEdit → CloseInputBackground` flow. If the user wants to cancel, they can clear the field and tap outside, or use a future explicit Cancel. | Preserves the observable contract users are used to. |
| 3 | **Business and Prompt tabs hide the header + tab bar when their textarea is focused** (same behavior as `BotSettings.cs:743-747` today). Other tabs keep the header visible and rely on scrim dim alone. | Gives more vertical room for long-form editing. Matches what users have now; not a new surprise. |
| 4 | **Thin refactor — new view components, Manager untouched logically.** Only Manager's text-access reads/writes are re-wired to go through `.Value` properties on the new components. | Minimizes risk. Matches the hard rule to preserve Manager's save/load/webhook logic exactly. |

## 4. Visual spec

All values sourced from `Design/mockup.html` `:root` CSS variables (lines 10–38) and the component sections around 715–990.

### Tokens
```
bg:          #F0F2F5    (page background)
card:        #FFFFFF    (field/card surface)
text:        #1A1A2E    (primary)
text-muted:  #8E8E93    (labels, section titles)
border:      #E4E6EB    (separators, dashed add-button border)
primary:     #1B7CEB    (save button, active tab underline, accents)
primary-lt:  #E8F2FD    (add-button hover fill)
whatsapp:    #25D366    (active toggle, green actions)
danger:      #E53935 → #C62828 (delete bot button gradient)
radius:      14px       (cards, bot-card tiles, delete bot button)
radius-sm:   10px       (settings fields, toggles, buttons, product thumb)
shadow:      0 1px 3px rgba(0,0,0,0.08), 0 1px 2px rgba(0,0,0,0.06)
font:        Inter (fallback to TMP default)
```

### Components

| Element | Style |
|---|---|
| Header | White bg, 4px 16px 14px padding. Back arrow (primary color, 24×24) + title (20px 700, centered-ish) + Save button (right-aligned, padding 8×20, radius 10, primary fill; disabled = gray). |
| Tab bar | White bg with 2px bottom border. Each tab: padding 12×14, 13px 600. Active tab: primary text + 2px primary bottom border. Horizontal scroll on overflow. |
| Section title | 13px 600, uppercase, text-muted, 0.5px letter-spacing, 8px bottom margin, 4px left padding. |
| Settings field (idle) | White card, radius-sm, padding 14×16, 12px bottom margin. Shadow. Label on top (12px 500 text-muted). Value below (16px 500 text). |
| Settings field (textarea) | Same card, textarea content (14px, line-height 1.5). |
| Toggle row | Same card + padding. Label left (16px 500), toggle right. Toggle track 52×32, radius 16. Thumb 28×28 with 0 1px 3px shadow. Off = #E0E0E0; on = whatsapp. 0.2s DOTween slide + color crossfade. |
| Product/Service card | White card, radius-sm, padding 14×16, 10px bottom margin. 50×50 thumb (radius 10, bg-colored, 24px emoji). Name 15px 600. Price 13px 600 primary. Desc 12px text-muted. Chevron right (16×16, #C7C7CC). |
| Add item button | Dashed 2px border, border-color border token, transparent bg, primary text 15px 600, radius-sm, padding 14px. Plus icon + label. Hover/press → fill primary-light. |
| Delete bot button | Full-width, radius 14, 16px padding, 17px 700 white. Gradient danger → #C62828. Shadow 0 4px 12px rgba(229,57,53,0.3). |
| Scrim | Full-screen Image, alpha 0 → 0.5 (0.2s OutQuad on show; 0.15s InQuad on hide). |
| Bottom sheet | Card rising from bottom. Radius 20 top corners, white bg, shadow. DOAnchorPosY 0.25s OutCubic on show. |

## 5. Component architecture

All new scripts live under `Assets/Scripts/Main/BotSettings/`.

### 5.1 `EditableField` (MonoBehaviour)

Single-line input wrapped as a "settings-field" card.

**Serialized fields:**
- `[SerializeField] TextMeshProUGUI label`
- `[SerializeField] TMP_InputField input`
- `[SerializeField] FocusScrim scrim` (may be null if host handles focus manually)

**Public API:**
```csharp
public string Value { get; set; }              // passes through to input.text
public string Label { get; set; }               // passes through to label.text
public UnityEvent<string> OnCommitted;          // fires on focus-loss if value changed
public bool IsFocused { get; }
public void Focus();                            // programmatic focus
public void Blur(bool commit);
```

**Behavior:**
- `Awake`: subscribes `input.onEndEdit` → `Blur(commit: true)`.
- On `input.onSelect`: stores `Value` as `focusValue`; if `scrim != null`, calls `scrim.Show(GetComponent<RectTransform>(), () => Blur(commit: true))`.
- `Blur(commit)`: if `commit` and `Value != focusValue`, raises `OnCommitted` with the new value. Calls `input.DeactivateInputField()` and `scrim?.Hide()`.
- `OnDestroy`: unsubscribes.

### 5.2 `EditableTextArea` (extends or composes `EditableField`)

Same API as `EditableField` but `input.lineType = MultiLineNewline`, taller card, uses the textarea style. On focus, additionally raises a public `OnFocusedForFullScreen` event. `BotSettings` subscribes it to hide the header + tab bar (animates `General.transform.parent` anchoredPosY up to clear the keyboard area, same calc as `BotSettings.cs:752`).

### 5.3 `ToggleRow` (MonoBehaviour)

Serialized: `[SerializeField] Toggle toggle`, `[SerializeField] Image trackImage`, `[SerializeField] RectTransform thumb`, `[SerializeField] TextMeshProUGUI label`, `[SerializeField] Color onColor`, `[SerializeField] Color offColor`.

Owns the iOS-style DOTween animation (thumb slide + track color crossfade) so `Bot.cs`'s `SetSwitches` pattern isn't duplicated. Exposes `toggle` directly so `BotSettings.WhatsappToggle` and `TelegramToggle` remain `Toggle` refs for auth-coroutine compatibility.

### 5.4 `SectionHeader` (MonoBehaviour)

Serialized: `[SerializeField] TextMeshProUGUI label`. Trivial; used for layout consistency.

### 5.5 `ProductCardView` / `ServiceCardView` (MonoBehaviour, two near-identical files)

Replaces `Product.cs` / `Service.cs`. Display-only.

**Serialized:**
- `[SerializeField] TextMeshProUGUI nameLabel`
- `[SerializeField] TextMeshProUGUI priceLabel`
- `[SerializeField] TextMeshProUGUI descLabel`
- `[SerializeField] Image thumb`
- `[SerializeField] Button rootButton` (covers the whole card for tap)
- No inline delete on the card. Delete lives inside `ItemEditSheet` only (matches the chevron-to-detail mental model).

**Public API:**
```csharp
public string Name { get; set; }        // wraps nameLabel.text
public string Price { get; set; }       // wraps priceLabel.text
public string Description { get; set; } // wraps descLabel.text
public event Action<ProductCardView> OnEditRequested;
public event Action<ProductCardView> OnDeleteRequested;
```

**Behavior:** `rootButton.onClick` → `OnEditRequested?.Invoke(this)`. `deleteButton` (in the sheet) → `PopupUI.Show(deletePopup)`, confirm → `PopupUI.WireFingerUp` → `OnDeleteRequested?.Invoke(this)` → `Destroy(gameObject)` + `Manager.Instance.EnableSave()`.

Parity note: `Product.cs` and `Service.cs` are replaced by `ProductCardView.cs` and `ServiceCardView.cs` as part of this refactor (deleted, not aliased). Manager.cs's `GetComponent<Product>()` calls re-wire to `GetComponent<ProductCardView>()`. The prefab file names stay `Product.prefab` / `Service.prefab` for git history continuity.

### 5.6 `ItemEditSheet` (MonoBehaviour)

One instance for Products tab, one for Services tab. Reused across all cards on that tab.

**Serialized:**
- `[SerializeField] RectTransform sheetRoot`
- `[SerializeField] EditableField nameField`
- `[SerializeField] EditableField priceField`
- `[SerializeField] EditableField descField`
- `[SerializeField] Button doneButton`
- `[SerializeField] Button deleteButton`
- `[SerializeField] GameObject deleteConfirmPopup`
- `[SerializeField] FocusScrim scrim`

**API:**
```csharp
public void Show(ProductCardView card);  // (overload for ServiceCardView)
public void Hide();
```

**Behavior:**
- `Show(card)`: binds `nameField.Value = card.Name`, etc. DOAnchorPosY slides sheet up from off-screen bottom. Scrim fades in behind the sheet.
- Internal `EditableField`s have their own focus-scrim behavior scoped to this sheet (raises above sheet overlay).
- `doneButton` commits all three fields back to card, calls `Manager.Instance.EnableSave()`, calls `Hide()`.
- `deleteButton` → `PopupUI.Show(deleteConfirmPopup)` → confirm (via `PopupUI.WireFingerUp`) → destroys card → `Manager.Instance.EnableSave()` → `Hide()`.
- `Hide()`: slides sheet down, fades scrim out.

### 5.7 `AddItemButton` (MonoBehaviour)

Dashed-border styled button. Serialized: `[SerializeField] Button button`, `[SerializeField] UnityEvent onTap`. Thin wrapper so the prefab can own the visual style uniformly.

### 5.8 `FocusScrim` (MonoBehaviour, one per BotSettings canvas)

**Serialized:**
- `[SerializeField] CanvasGroup scrimGroup` (alpha tween target)
- `[SerializeField] Image scrimImage` (raycast target for dismissal)
- `[SerializeField] RectTransform raisedLayer` (sibling-last in canvas, renders above scrim)

**API:**
```csharp
public void Show(RectTransform field, Action onOutsideTap);
public void Hide();
public bool IsShowing { get; }
```

**Behavior:**
- `Show`: remembers `field`'s original parent + sibling index. Reparents onto `raisedLayer`. Activates scrim GO; DOFade `scrimGroup.alpha` 0 → 0.5 over 0.2s OutQuad. Wires `scrimImage` with `PopupUI.WireFingerUp(scrimImage.gameObject, onOutsideTap)` — one-shot, unsubscribed in `Hide`.
- `Hide`: DOFade `scrimGroup.alpha` → 0 over 0.15s InQuad. On tween complete: reparent field back to original parent at original sibling index, deactivate scrim GO.

### 5.9 `SavedToast`

Thin wrapper around the existing `Saved` GameObject so it has a `.Show()` method that fades in and auto-hides after 1.5s.

### 5.10 `BotSettings` (rewritten) + `BotSettings.Auth.cs` (partial)

`BotSettings.cs` becomes a thin controller:

**Serialized — main tab refs:**
- `[SerializeField] EditableField BotNameField`
- `[SerializeField] TMP_Dropdown BusinessTypeDropdown` (kept as-is)
- `[SerializeField] ToggleRow whatsappRow` (exposes `.Toggle` getter as `public Toggle WhatsappToggle`)
- `[SerializeField] ToggleRow telegramRow`
- `[SerializeField] EditableField WhatsappNumberField`
- `[SerializeField] EditableField TelegramNumberField`
- `[SerializeField] EditableTextArea BusinessField`
- `[SerializeField] EditableTextArea PromptField`
- `[SerializeField] RectTransform ProductsParent`
- `[SerializeField] RectTransform ServicesParent`
- `[SerializeField] AddItemButton AddProductButton`
- `[SerializeField] AddItemButton AddServiceButton`
- `[SerializeField] ItemEditSheet ProductEditSheet`
- `[SerializeField] ItemEditSheet ServiceEditSheet`
- `[SerializeField] FocusScrim mainScrim`
- `[SerializeField] RectTransform headerGroup`, `tabBarGroup` (for hide-on-focus)

**Serialized — auth refs (unchanged names):** `WhatsappAuthorization`, `WhatsappQRPanel`, `WhatsappCodePanel`, `WhatsappQRCodeImage`, `TelegramAuthorization`, `TelegramQRPanel`, `TelegramCodePanel`, `TelegramQRCodeImage`, `Saved`, `ConfirmChangeWhatsappNumberPopup`, `ConfirmChangeTelegramNumberPopup`, `WhatsappCodeTimer`, `TelegramCodeTimer`, plus every auth-related button. Identical to today.

**Start():**
- Wire tab buttons → `OpenGeneralTab` / `OpenBusinessTab` / ... (methods unchanged).
- Subscribe every `EditableField.OnCommitted` → `() => Manager.Instance.EnableSave()`.
- Subscribe `BusinessTypeDropdown.onValueChanged` → `Manager.Instance.EnableSave()`.
- Subscribe `WhatsappToggle.onValueChanged` → `WhatsappChannelToggleChanged` (unchanged signature).
- Wire `AddProductButton.onTap` → `AddProduct`, same for service.
- Subscribe `ProductCardView.OnEditRequested` → `ProductEditSheet.Show(card)` (for all current + newly-added cards).
- Subscribe `EditableTextArea.OnFocusedForFullScreen` → `HideHeaderAndTabs()`; `OnCommitted` → `RestoreHeaderAndTabs()`.
- Wire all auth buttons exactly as today.

**No more:** `InputBackgroundButton`, `CloseInputBackground`, `HandleKeyboardAppearing`, `HandleKeyboardDisappearing`, `SetProductsParentPosition`, `SetServicesParentPosition`, `OpenBotNameInput`, `OpenBusinessInput`, `FinishEditingBusiness`, `OpenPromptInput`, `FinishEditingPrompt`. All of these are absorbed by `EditableField` + `FocusScrim`.

**`BotSettings.Auth.cs`** (partial): contains `CheckWhatsappAuthorization`, `OpenWhatsappAuthorization`, `WhatsappAuthorizationBack`, `WhatsappAuthorizationDone`, `OpenWhatsappQRPanel`, `CloseWhatsappQRPanel`, `OpenWhatsappCodePanel`, `CloseWhatsappCodePanel`, `WhatsappNumberInputChanged`, `GetWhatsappCode`, `GetWhatsappProfileStatus`, `CheckWhatsappUnauthorizationOutsideApp`, `UnauthorizeWhatsapp`, and the mirrored Telegram handlers, plus the `ConfirmChange*` + `CancelChange*` methods. Moved verbatim; only the two or three lines that write to `WhatsappNumberButton.transform.GetChild(0)…` become `WhatsappNumberField.Value = …`.

## 6. Manager.cs re-wiring (scoped)

**Methods touched:** `SaveSettings` (line 330), `CloseSettings` (line 442), `EnableSave` (line 497), `CheckProductsOrServicesChanged` (line 528). **Logic unchanged** — only field accessors.

Mapping:

| Old | New |
|---|---|
| `openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text` | `openBotSettings.BotNameField.Value` |
| `openBotSettings.WhatsappNumberButton.transform.GetChild(0)…text` | `openBotSettings.WhatsappNumberField.Value` |
| `openBotSettings.TelegramNumberButton.transform.GetChild(0)…text` | `openBotSettings.TelegramNumberField.Value` |
| `openBotSettings.BusinessInputButton.transform.GetChild(0)…text` | `openBotSettings.BusinessField.Value` |
| `openBotSettings.PromptInputButton.transform.GetChild(0)…text` | `openBotSettings.PromptField.Value` |
| `product.GetComponent<Product>().ProductButton.transform.GetChild(0)…text` | `product.GetComponent<ProductCardView>().Name` |
| `product.GetComponent<Product>().PriceButton.transform.GetChild(0)…text` | `product.GetComponent<ProductCardView>().Price` |
| `product.GetComponent<Product>().DescriptionButton.transform.GetChild(0)…text` | `product.GetComponent<ProductCardView>().Description` |
| (same pattern for `Service`) | (same pattern for `ServiceCardView`) |
| `ProductsParent.transform.GetChild(p).GetChild(0).GetChild(0)…text` (in CheckProductsOrServicesChanged) | `ProductsParent.GetChild(p).GetComponent<ProductCardView>().Name` |

`ProductPrefab` and `ServicePrefab` references in Manager remain — they just now instantiate the new card prefabs whose root has `ProductCardView` / `ServiceCardView`. The `AddProductButton.transform.parent` sibling-ordering trick is preserved.

**Side-effect visible to user:** the `WhatsappNumberButton.transform.parent.gameObject.SetActive(...)` guard that hides the field when number is empty becomes `WhatsappNumberField.gameObject.SetActive(...)`. Semantic-identical.

## 7. Data flow

```
App launch / open bot
 └─► Manager.CloseSettings()
      └─► reads PlayerPrefs → writes to EditableField.Value, ToggleRow.isOn, etc.
      └─► destroys old product/service children → instantiates from PlayerPrefs

User edits any field / toggles / dropdown / adds/deletes card
 └─► component raises OnCommitted / onValueChanged
      └─► BotSettings forwards to Manager.Instance.EnableSave()
           └─► Manager.EnableSave reads current .Value from views,
               compares to PlayerPrefs, sets SaveButton.interactable

User taps Save
 └─► Manager.SaveSettings()
      └─► writes .Value from views to PlayerPrefs (keys unchanged)
      └─► fires n8n webhook (unchanged)
      └─► Manager.EnableSave() re-runs → SaveButton disabled
      └─► SavedToast.Show()

User taps back without saving
 └─► Manager.CloseSettings() → reloads from PlayerPrefs → dirty cleared
```

## 8. Focus + scrim sequence diagram

```
Tap EditableField
  ├─► input.onSelect fires
  ├─► field caches focusValue
  ├─► FocusScrim.Show(fieldRect, onOutsideTap)
  │    ├─► remember parent + siblingIndex
  │    ├─► SetParent(raisedLayer)
  │    ├─► scrim GO active, DOFade alpha 0 → 0.5
  │    └─► WireFingerUp(scrim, onOutsideTap)
  └─► (Business/Prompt only) EditableTextArea.OnFocusedForFullScreen
       └─► BotSettings hides header + tab bar (DOAnchorPosY up)

Tap scrim OR onEndEdit OR keyboard-Done
  ├─► field.Blur(commit: true)
  │    ├─► if value ≠ focusValue → OnCommitted(value)
  │    │    └─► Manager.Instance.EnableSave()
  │    ├─► input.DeactivateInputField()
  │    └─► FocusScrim.Hide()
  │         ├─► DOFade alpha → 0
  │         └─► on complete: reparent field, deactivate scrim GO
  └─► (Business/Prompt only) BotSettings restores header + tab bar
```

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Auth coroutines write to `WhatsappNumberButton.transform.GetChild(0)…` in many places; missing one breaks auth. | Full grep for `NumberButton.transform.GetChild` and `NumberButton.transform.parent` across `BotSettings.cs`, migrate every site. Keep a comment in `WhatsappNumberField` noting auth-flow coupling. |
| PlayerPrefs key drift — e.g., typo in mapping breaks existing saved bots. | Migration is read-only at the UI level (keys are unchanged). Verify by loading an existing bot post-refactor before committing. |
| `FocusScrim.Show` reparent breaks layout (e.g. parent had `VerticalLayoutGroup`). | Before reparent, capture the RectTransform's world rect; set it on the raisedLayer clone so visual position doesn't jump. Test in Editor during step 3. |
| `ItemEditSheet` reused across cards can leak field subscriptions. | `Show` clears previous `OnCommitted` listeners before wiring new ones. `Hide` unsubscribes explicitly. |
| Bottom sheet + field focus + nested scrim → z-order confusion. | `ItemEditSheet` holds its own `FocusScrim` instance scoped to the sheet; the sheet's scrim renders above the sheet but below the raised field. |
| Product/Service cards currently have internal `Canvas overrideSorting = true` toggling; removing this could regress scroll clipping. | The new cards don't need overrideSorting — scrim handles the raise. Remove the Canvas component entirely from the card prefabs. |
| Manager.cs reads `GetChild(p).GetChild(0).GetChild(0)…` in `CheckProductsOrServicesChanged` — indexing not property access. | Change to `GetChild(p).GetComponent<ProductCardView>().Name`. Verified in Section 6 mapping table. |

## 10. Smoke-test checklist (must all pass before claiming done)

Copied from user brief, to be executed manually in Unity Editor at 1080×2400:

1. Open a bot → General tab shows with correct visuals, save button disabled.
2. Change bot name → save button enables. Revert name → disables again.
3. Tap each toggle → visual state animates, save-dirty triggers.
4. Change business type dropdown → dirty triggers.
5. Edit business description (multi-line textarea) → scrim appears, header+tabs hide, keyboard works, commit updates field, save-dirty works, header+tabs restore.
6. Edit prompt text → same as above.
7. Switch between all 5 tabs → no layout jank, correct active-tab underline.
8. Products tab: tap Add → new card appears, save-dirty enables. Tap card → sheet slides up with scrim. Edit each field → values persist in card on Готово. Delete from sheet → finger-up confirm popup, finger release confirms, card destroys, save-dirty stays set.
9. Services tab: same as products.
10. WhatsApp toggle on → authorization screen opens, QR + phone-code flows work, success returns to settings with number populated.
11. Telegram toggle on → same.
12. Number change confirm popup fires on finger-up (existing `PopupUI.WireFingerUp` path).
13. Save button → writes to PlayerPrefs, calls n8n, shows Saved toast, button disables.
14. Close settings without save → state reverts to last-saved PlayerPrefs values.
15. Delete bot (from General tab) → confirmation popup → bot removed from BotsPage.
16. Close and reopen the app → bot settings load correctly from PlayerPrefs (confirms no key format change).

## 11. Build sequence (commit per step)

1. Create `Assets/Scripts/Main/BotSettings/` folder. Add component stubs (empty classes with serialized field declarations). Commit: `refactor: scaffold BotSettings component folder`.
2. Implement `EditableField` + `EditableTextArea`. Commit.
3. Implement `FocusScrim`. Hook an EditableField to it in an isolated editor scene for manual verification. Commit.
4. Implement `ToggleRow`, `SectionHeader`, `AddItemButton`, `PrimaryButton`, `DangerButton` (visual only). Commit.
5. Implement `ProductCardView` + `ServiceCardView` + `ItemEditSheet`. Commit.
6. Rewrite `BotSettings.cs` as a thin controller; move auth code verbatim into `BotSettings.Auth.cs` partial. Commit.
7. Rebuild `BotSettings.prefab`, `Product.prefab`, `Service.prefab` using new components. Re-parent auth panels unchanged. Commit.
8. Re-wire `Manager.cs` reads/writes (Section 6 table). No logic changes. Commit.
9. Update `Main.unity` scene references. Commit.
10. Walk the Section 10 smoke-test checklist. If all pass, final commit. Otherwise, fix and re-run.

Rollback plan: each step is a commit; any regression is reverted per-step without losing other progress.

## 12. Out-of-scope follow-ups (not in this spec)

- Typed `BotConfig` POCO for dirty-tracking (rejected as duplicate source of truth).
- Unity TextMeshPro font swap to Inter (requires importing font asset; can ship with TMP default).
- Redesign of the auth panels' visual style (lifted unchanged; re-styling is a separate task if desired).
