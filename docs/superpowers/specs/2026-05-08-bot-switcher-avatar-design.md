# Bot Switcher Avatar — Design

## Goal

Fill the empty `Avatar` slot in the WhatsApp header's `BotSwitcherTitle` (and the matching slot in each `BotSwitcherRowView`) with the active bot's business identity badge — a tinted circle plus the business icon sprite. Visual continuity with the BotsPage card (`Bot.BotIconTile` + `Bot.BotIconImage`).

Source of truth: `BusinessTypesSO`, keyed by the per-bot PlayerPrefs string `{botId}+"BusinessType"`. No network. No new assets. No new dependencies.

## Non-goals

- Real WhatsApp profile pictures (GreenAPI / Wappi avatar fetch).
- Per-bot custom avatar uploads.
- Updating Profile page or any other surface that displays bot identity.
- Refactoring the existing `Bot.BotIconTile` / `Bot.BotIconImage` setup on the BotsPage card.

## Why business icon (not real avatar)

Considered four options up-front: real WhatsApp avatar, business icon, initials-on-tint, and a real-with-fallback hybrid. Picked business icon because:

- Zero network and zero asset work — works for every bot the moment it has a `BusinessType` set.
- Reuses data the user already saw and chose during the bot creation wizard.
- Visual continuity: the same identity badge the user picks the bot by on BotsPage now follows them into the Whatsapp header and the switcher sheet.
- Tradeoff accepted: two bots with the same business type have identical avatars. The bot name, shown immediately to the right, disambiguates.

## Architecture

### 1. Bot accessors (data source)

Add two getters on `Assets/Scripts/Main/Bot.cs`:

```csharp
public Sprite GetBusinessIconSprite()
{
    if (businessTypes == null) return null;
    var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
    if (string.IsNullOrEmpty(id)) return null;
    return businessTypes.TryGetById(id, out var entry) ? entry.sprite : null;
}

public Color GetBusinessIconTint()
{
    if (businessTypes == null) return NeutralTile;
    var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
    if (string.IsNullOrEmpty(id)) return NeutralTile;
    return businessTypes.TryGetById(id, out var entry) ? entry.tileColor : NeutralTile;
}

private static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);
```

Why these live on `Bot`:

- The `BusinessTypesSO` reference is already a serialized field on `Bot`.
- The PlayerPrefs key convention is already a `Bot` concern.
- Both `BotSwitcherTitleBinder` and `BotSwitcherRowView` need the same data — putting it on `Bot` removes the need for either caller to hold its own `BusinessTypesSO` reference or duplicate the lookup logic.
- Mid-wizard or otherwise unset state (`BusinessType == ""`) returns a neutral fallback rather than null/Color.clear, so the avatar always renders something rather than showing a hole.

### 2. Avatar GameObject hierarchy

Both the title Avatar and each row's avatar restructure to:

```
Avatar          (RectTransform, Image=white sprite tinted with tileColor,
                 ImageWithRoundedCorners radius = sizeDelta.x / 2 → circle)
└── IconSprite  (RectTransform centered at ~64% of parent size,
                 Image=BusinessTypesSO sprite, raycastTarget=false)
```

Same nesting pattern already used by `Bot.BotIconTile` → `Bot.BotIconImage` on the BotsPage card, and matches the rounded-corners + child-image pattern in `MessageItemView.cs:1013` for chat thumbnails. `Nobi.UiRoundedCorners.ImageWithRoundedCorners` is the package already in use; `radius` is in pixels.

### 3. Runtime binding

**`Assets/Scripts/UI/BotSwitcherTitleBinder.cs`** — add two serialized refs and one apply call:

```csharp
[SerializeField] private Image avatarTile;
[SerializeField] private Image avatarIcon;

private void UpdateTitle(string botId)
{
    Bot bot = !string.IsNullOrEmpty(botId) && Manager.Instance != null
        ? Manager.Instance.FindBotByName(botId) : null;

    if (nameLabel != null)
        nameLabel.text = bot != null ? PlayerPrefs.GetString(botId + "Name", botId) : "Bot";

    ApplyAvatar(bot);
}

private void ApplyAvatar(Bot bot)
{
    if (avatarTile != null)
        avatarTile.color = bot != null ? bot.GetBusinessIconTint() : new Color(0.85f, 0.85f, 0.85f);
    if (avatarIcon != null)
    {
        Sprite sprite = bot != null ? bot.GetBusinessIconSprite() : null;
        avatarIcon.sprite = sprite;
        avatarIcon.enabled = sprite != null;
    }
}
```

`UpdateTitle` already runs on `OnEnable` and on every `OnActiveBotChanged` event, so the avatar refreshes automatically whenever the Whatsapp tab activates or the active bot changes.

**`Assets/Scripts/UI/BotSwitcherRowView.cs`** — replace the current `avatarImage` + `avatarFallback` fields with `avatarTile` + `avatarIcon`. In `Bind`, call the same two getters from `Bot`. The "future avatar fetcher will overwrite this" comment is removed — the icon/tint pair is now the resolved avatar; no fetcher is involved.

`Bind` is invoked fresh on every `BotSwitcherSheet.PopulateRows` call (which runs on every `Open()`), so business-type changes show up the next time the sheet opens.

### 4. Editor builders — surgical, scoped to the Avatar only

The existing `Screen_WhatsappHeaderRebuilder` and `BotSwitcherSheetBuilder` are **not** modified. The user has post-build customizations on the title (BotName, Chevron) and on the row (nameLabel, subLineLabel, statusDot, selectedBackground, selectedAccentBar) that a full rebuild would wipe.

Two new menu items are added, each touching only its Avatar:

**`Tools/Bot Switcher/Rebuild Title Avatar`** — operates on `Screen_Whatsapp/ChatsPanel/TopBar/BotSwitcherTitle/Avatar`. If that path doesn't exist, errors and exits.

If it does exist, it:

- Preserves the existing `RectTransform` (size, anchors, position) and `LayoutElement` (preferredWidth/Height). Whatever the user set to 44×44 stays at 44×44.
- Ensures the GameObject has exactly one `Image` configured for the tile (white sprite, color overwritten at runtime by the binder).
- Adds or refreshes `ImageWithRoundedCorners` with `radius = RectTransform.sizeDelta.x / 2`, so it stays circular at the user's chosen size.
- Removes any existing children of `Avatar`, then creates a single `IconSprite` child:
  - Anchored center, `sizeDelta = sizeDelta * 0.64f`, `anchoredPosition = Vector2.zero`.
  - `Image` with no sprite (set at runtime), `raycastTarget = false`.
- Wires the new fields on the parent's `BotSwitcherTitleBinder` via `SerializedObject`: `avatarTile` → the Avatar's `Image`, `avatarIcon` → the `IconSprite`'s `Image`. Does not touch `nameLabel`.
- `EditorUtility.SetDirty` + `EditorSceneManager.MarkSceneDirty` and selects the rebuilt Avatar.

**`Tools/Bot Switcher/Rebuild Row Avatar`** — same surgical pattern, but operates on the row template's avatar slot. Resolves the slot by path: `Canvas/BotSwitcherRowPrefabHolder/BotSwitcherRow/Avatar`. Path-based rather than SerializedProperty-based because the `BotSwitcherRowView.avatarImage` field is being renamed in this same change — reading the old field would break the order of operations (pull C# → run menu item).

Restructures only that GameObject's internals (Image config, ImageWithRoundedCorners, IconSprite child) and rewires `avatarTile` / `avatarIcon` on `BotSwitcherRowView`. Leaves `nameLabel`, `subLineLabel`, `statusDot`, `selectedBackground`, `selectedAccentBar`, `rowButton` untouched.

Net effect for both menu items: the only things modified are the Avatar's internal hierarchy and the two new serialized fields on the binder/row. Every other tweak survives.

## Fallback behavior

| Condition | Tile color | Icon sprite |
| --- | --- | --- |
| No active bot (e.g., `CurrentBotId == _default`, or active bot deleted) | `(0.85, 0.85, 0.85)` | none, `Image.enabled = false` |
| Bot exists, `BusinessType == ""` (mid-wizard) | `(0.85, 0.85, 0.85)` | none, `Image.enabled = false` |
| Bot exists, `BusinessType` not in SO | `(0.85, 0.85, 0.85)` | none, `Image.enabled = false` |
| Bot exists, business type found | `entry.tileColor` | `entry.sprite` |

A neutral light gray circle is preferable to a hole or a magenta missing-sprite — it reads as "no identity yet" rather than broken.

## Live-update guarantees

| Trigger | Refresh path |
| --- | --- |
| Switch active bot (`ChatManager.SetActiveBot`) | `OnActiveBotChanged` → `BotSwitcherTitleBinder.UpdateTitle` → re-applies tile + icon. |
| Open Whatsapp tab after a BotSettings change | `BotSwitcherTitleBinder.OnEnable` → `UpdateTitle(CurrentBotId)`. |
| Open the bot switcher sheet | `BotSwitcherSheet.Open` → `PopulateRows` → fresh `BotSwitcherRowView.Bind` for every row. |

No new event subscriptions are required.

## Files touched

| File | Change |
| --- | --- |
| `Assets/Scripts/Main/Bot.cs` | Add `GetBusinessIconSprite()`, `GetBusinessIconTint()`, `NeutralTile` constant. |
| `Assets/Scripts/UI/BotSwitcherTitleBinder.cs` | Add `avatarTile`/`avatarIcon` serialized fields and `ApplyAvatar`; call from `UpdateTitle`. |
| `Assets/Scripts/UI/BotSwitcherRowView.cs` | Replace `avatarImage` + `avatarFallback` with `avatarTile` + `avatarIcon`; call `Bot.GetBusinessIconSprite/Tint` from `Bind`; remove the "future avatar fetcher" comment. |
| `Assets/Editor/BotSwitcherTitleAvatarRebuilder.cs` (new) | `Tools/Bot Switcher/Rebuild Title Avatar` menu item. Surgical — restructures only the Avatar. |
| `Assets/Editor/BotSwitcherRowAvatarRebuilder.cs` (new) | `Tools/Bot Switcher/Rebuild Row Avatar` menu item. Surgical — restructures only the row's avatar slot. |

The existing `Screen_WhatsappHeaderRebuilder.cs` and `BotSwitcherSheetBuilder.cs` are not modified.

## Risks / things to watch

- **Avatar GameObject must already exist when the new menu items run.** Both new builders error out if the parent (`BotSwitcherTitle` or the row prefab template) lacks an `Avatar` slot. The existing builders create that slot, so the order is: run existing builder once → run new Avatar rebuilder. Documented in the menu item's `Debug.LogError` message.
- **`Bot.GetBusinessIconSprite/Tint` are called during `OnEnable` and on every `OnActiveBotChanged`** — both cheap PlayerPrefs reads + dictionary lookup on `BusinessTypesSO`. Not an Update-loop hot path; no caching needed.
- **`ImageWithRoundedCorners.radius` is set to `sizeDelta.x / 2` at build time.** If the user later resizes the Avatar through the Inspector after running the menu item, the radius doesn't auto-update. Documented in the menu item's success log so the user knows to re-run after resizing. Acceptable because resize-then-not-re-run produces a rounded square, not a broken state.
- **`avatarIcon.enabled = false` (rather than null sprite) when there's no business type.** A null sprite on a `UI.Image` would render the default white square. Disabling the component is the clean way to make it visually absent without removing the GameObject.
