# Bot Card Business-Type Icons — Design

**Date:** 2026-04-15
**Status:** Approved (pending review)

## Goal

Replace the static placeholder glyph on each bot card's left-side icon tile with a real icon and tile color that reflect the bot's chosen business type. The icon updates immediately when the user changes the business type in bot settings.

## Background

Each bot has a business type, stored as an int index in `PlayerPrefs[botName + "BusinessType"]`. The index aligns with `Manager.BusinessTypesList` (Manager.cs:53). The seven business types, in order, are:

| Index | Type           |
|-------|----------------|
| 0     | Car Service    |
| 1     | Cafe           |
| 2     | Beauty Salon   |
| 3     | Dentist        |
| 4     | Real Estate    |
| 5     | Tour Agency    |
| 6     | Flowers        |

The current bot card (built by `BotsPageSetup.cs`) renders the icon tile as a uniform blue rounded square (`#2E9BE0`) with a static TMP "B" glyph. A note in `BotsPageSetup.cs:438` already calls out that SF Pro can't render emoji, and prior UI feedback established that **TMP-drawn icons are unreliable — use `Image` + sprite instead**.

## Design

### Asset additions

**Icon files** (provided by user):
```
Assets/Images/BusinessIcons/
  CarService.png
  Cafe.png
  BeautySalon.png
  Dentist.png
  RealEstate.png
  TourAgency.png
  Flowers.png
```

Specs: PNG with transparency, white foreground, 256×256 (or 512×512) source, imported as `Sprite (2D and UI)`.

**Data asset** — `Assets/Data/BusinessIcons.asset` (ScriptableObject):
```
BusinessIconsSO
  entries: [
    { sprite: CarService.png,   tileColor: #8E8E93 (gray)   }
    { sprite: Cafe.png,         tileColor: #FF9500 (orange) }
    { sprite: BeautySalon.png,  tileColor: #FF375F (pink)   }
    { sprite: Dentist.png,      tileColor: #30B0C7 (teal)   }
    { sprite: RealEstate.png,   tileColor: #5856D6 (indigo) }
    { sprite: TourAgency.png,   tileColor: #32ADE6 (sky)    }
    { sprite: Flowers.png,      tileColor: #FF2D55 (rose)   }
  ]
```

The list is index-aligned with `BusinessTypesList`. Tile colors come from the iOS system palette.

### New script: `Assets/Scripts/Main/BusinessIconsSO.cs`

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Automation/Business Icons", fileName = "BusinessIcons")]
public class BusinessIconsSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public Sprite sprite;
        public Color tileColor;
    }

    public Entry[] entries;

    public bool TryGet(int index, out Entry entry)
    {
        if (entries != null && index >= 0 && index < entries.Length)
        {
            entry = entries[index];
            return entry.sprite != null;
        }
        entry = default;
        return false;
    }
}
```

### Bot prefab change

The existing icon tile structure (in `BotsPageSetup.BuildIconTile`):
```
BotIcon (Image, rounded, color=ColIconTile)
  └── Glyph (TMP "B")
```

Becomes:
```
BotIcon (Image, rounded, color=tile color from SO at runtime)
  └── IconImage (Image, sprite from SO at runtime, ~55% of tile size, tint=white)
```

The TMP "Glyph" child is removed.

### Bot.cs changes

Add three serialized fields:
```csharp
[SerializeField] private Image BotIconTile;     // existing tile background image
[SerializeField] private Image BotIconImage;    // new sprite image on top
[SerializeField] private BusinessIconsSO businessIcons;
```

In `Awake()` (after the existing `StartCoroutine(SetSwitches())` call) add:
```csharp
ApplyBusinessIcon();
```

New methods:
```csharp
private void ApplyBusinessIcon()
{
    if (businessIcons == null) return;
    int index = PlayerPrefs.GetInt(transform.name + "BusinessType", 0);
    if (!businessIcons.TryGet(index, out var entry))
    {
        Debug.LogWarning($"[Bot] Missing business icon for index {index} on {transform.name}");
        return;
    }
    if (BotIconImage != null) BotIconImage.sprite = entry.sprite;
    if (BotIconTile  != null) BotIconTile.color   = entry.tileColor;
}

public void RefreshBusinessIcon() => ApplyBusinessIcon();
```

### Reactive update — `Manager.cs`

After the user changes the business type in bot settings and saves, refresh the card. In `Manager.cs:330` (the line `PlayerPrefs.SetInt(openBot.name + "BusinessType", openBotSettings.BusinessTypeDropdown.value);`), append:
```csharp
openBot.GetComponent<Bot>()?.RefreshBusinessIcon();
```

No other call sites need updating — bots already in the list re-read on Awake when the scene loads, and newly created bots run through `Awake` on instantiate.

### BotsPageSetup.cs changes

In `BuildIconTile`:
- Remove the TMP "Glyph" child.
- Add an `IconImage` child: `Image` component, white tint, no sprite (assigned at runtime), centered, sized at 55% of `IconSize` (~76 units).
- Return the tile `Image` and the icon `Image` references so `BuildBotCard` can wire them on the Bot component.

In `BuildBotCard`:
- After adding the `Bot` component, set serialized fields:
  - `BotIconTile` → tile `Image`
  - `BotIconImage` → icon `Image`
  - `businessIcons` → loaded SO (see below)

In `Build` (the `[MenuItem]` entry point):
- Before building the prefab, call `EnsureBusinessIconsAsset()` which:
  1. Loads `Assets/Data/BusinessIcons.asset` if it exists, otherwise creates it.
  2. If new, populates the 7 entries with tile colors from the table above.
  3. For each entry, if `sprite` is null, attempts to load `Assets/Images/BusinessIcons/{Name}.png` by convention and assigns it. Logs a warning for any sprite still missing after the lookup.
  4. Saves the asset.

This means the user's workflow is: drop the 7 PNGs into `Assets/Images/BusinessIcons/`, run `Tools > Setup My Bots Page`, and everything wires up automatically. If they add or replace a PNG later, re-running the menu item picks it up.

## Data flow

```
PlayerPrefs[botName + "BusinessType"] (int)
        │
        ▼
Bot.Awake() → ApplyBusinessIcon()
        │
        ▼
BusinessIconsSO.TryGet(index) → { sprite, tileColor }
        │
        ▼
BotIconImage.sprite = sprite
BotIconTile.color   = tileColor

(on settings save)
Manager saves PlayerPrefs → openBot.RefreshBusinessIcon() → same pipeline
```

## Edge cases

- **Missing SO reference on Bot prefab**: silently no-op (icon stays whatever the prefab had). Logged once per Awake only if explicit lookup is attempted.
- **Missing sprite for a valid index**: warning logged, icon image left blank, tile keeps its default color.
- **Index out of range** (e.g., business type list shrinks): `TryGet` returns false → same as missing sprite path.
- **Editor builder run before user adds PNGs**: `BusinessIcons.asset` is created with tile colors but null sprites; warning lists which files were not found.

## Files touched

| File | Change |
|------|--------|
| `Assets/Scripts/Main/BusinessIconsSO.cs` | New ScriptableObject definition |
| `Assets/Data/BusinessIcons.asset` | New, auto-created by the editor builder |
| `Assets/Images/BusinessIcons/*.png` | New, user-provided (7 files) |
| `Assets/Scripts/Main/Bot.cs` | Add fields + `ApplyBusinessIcon`/`RefreshBusinessIcon` |
| `Assets/Scripts/Main/Manager.cs` | One line to refresh icon after settings save |
| `Assets/Scripts/Editor/BotsPageSetup.cs` | Replace TMP glyph with Image, wire fields, ensure SO exists |
| `Assets/Prefabs/Bot.prefab` | Regenerated by editor builder |

## Out of scope

- Changing how business type is stored (still int index)
- Migrating existing bots' data
- Adding new business types
- Animated icon transitions on type change
- Per-platform icon variants
