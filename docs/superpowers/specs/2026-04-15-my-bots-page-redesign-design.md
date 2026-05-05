# My Bots Page Redesign — Design Spec

**Date:** 2026-04-15
**Scope:** "My Bots" list page only (status bar + nav header + scrollable bots list). Bot Settings page is out of scope — separate follow-up sessions.
**Pattern reference:** `Assets/Scripts/Editor/AuthPageSetup.cs`
**Mockup:** `Design/mockup.html` — `.bots-list` CSS (lines ~388–450), PAGE 4 markup (lines ~1321–1410).

---

## Goal

Rebuild the My Bots page UI to match the mockup, following the editor-script pattern established for the Auth pages. Keep `Bot.cs` and `BotsPage.cs` edits to the absolute minimum (no edits required this session).

## Decisions (resolved during brainstorming)

| # | Topic | Choice |
|---|-------|--------|
| 1 | Activation toggle | **Park off-screen.** Toggle GameObject lives as a hidden child under the bot-card root, positioned off-canvas and scaled to zero. `Bot.cs` untouched. |
| 2 | Pill text localization | **Add `BotStatusPill` observer component.** Watches the legacy `Status` TMP's color and drives a separate pill-label TMP with Russian strings + background color. `Bot.cs` untouched. |
| 3 | Delete affordance | **Leave all delete refs unassigned.** `DeleteButton`, `DeletePopup`, `DeleteConfirmButton`, `DeleteCancelButton` are already null-guarded in `Bot.cs`. No hidden scaffolding. |

## Deliverables

1. `Assets/Scripts/Editor/BotsPageSetup.cs` — new editor script (`#if UNITY_EDITOR`, `[MenuItem("Tools/Setup My Bots Page")]`) that rebuilds the My Bots page and bot-card template in the open scene.
2. `Assets/Scripts/Main/BotStatusPill.cs` — new runtime `MonoBehaviour` (~30 lines) that renders the Russian status pill by observing the legacy `Status` TMP's color.
3. Scene marked dirty via `EditorSceneManager.MarkSceneDirty`. User saves with Cmd+S.

## Architecture

### Page structure (top → bottom)

```
Canvas/
└── Bots                       (root, Image = #F2F2F7, SetActive false by default)
    ├── StatusBar              (reused pattern: 9:41 + signal/wifi/battery, white bg)
    ├── NavHeader              (white, 139px, border-bottom)
    │   ├── Title              ("Мои Боты", SFProText-Semibold 50pt, #1C1C1E)
    │   └── HeaderIcons        (HorizontalLayoutGroup, right-anchored)
    │       ├── SearchButton   (🔍 icon, no-op this session)
    │       └── NewBotButton   (+ icon → BotsPage.NewBotButton)
    ├── ScrollContent          (ScrollRect, vertical only)
    │   └── Viewport           (RectMask2D)
    │       └── BotsParent     (VerticalLayoutGroup, 10px spacing, 16/20 padding, ContentSizeFitter)
    │           └── [BotCardTemplate]   (first child — Instantiate source; see below)
    └── BottomNav              (existing — reused as-is)
```

### Bot-card template

```
BotCard                        (white, rounded-14, ~194px tall, Button → EditButton)
├── HorizontalLayoutGroup      (14px spacing, 16px padding)
│   ├── BotIcon                (50×50, rounded-14, gradient bg, emoji/char TMP centered)
│   ├── BotDetails             (flex column, min-width 0)
│   │   ├── BotName            (16pt semibold #1C1C1E)
│   │   └── BotDesc            (13pt regular #8E8E93, single-line truncate)
│   ├── StatusPill             (rounded-20, has BotStatusPill component)
│   │   ├── PillBg             (Image — BotStatusPill.background ref)
│   │   ├── PillLabel          (12pt semibold — BotStatusPill.pillLabel ref)
│   │   └── Status             (legacy TMP, enabled=false, BotStatusPill.statusSource ref, Bot.cs Status ref)
│   └── BotArrow               ("›" 18pt #C7C7CC)
└── ActivationSwitch           (PARKED: anchoredPos=(-9999,0), scale=0)
    └── Background             (child 0 — Image, RectTransform present)
        └── Handle             (child 0.0 — RectTransform + Image)
```

### `BotStatusPill.cs` behavior

- Serialized refs: `Image background`, `TextMeshProUGUI pillLabel`, `TextMeshProUGUI statusSource`.
- Each `LateUpdate`: compare `statusSource.color` to last-observed color; if changed, update pill.
- Color → state map:
  - Green `(0,1,0)` → label "Активен", bg `#E8F8EE`, text color `#34C759`.
  - Red `(1,0,0)` → label "Неактивен", bg `#FFECEC`, text color `#FF3B30`.
  - Blue `(0,0.698,1)` → label "Подключение", bg `#E3F2FF`, text color `#007AFF`.
- Legacy `Status` TMP has `.enabled = false` so it writes silently without rendering.

## Data flow

1. `Bot.Awake` → `SetSwitches()` coroutine reads `PlayerPrefs.GetInt(name, 1)` → writes `Status.text` + `Status.color` (English, unrendered).
2. User activation via the parked toggle (or future settings page) → `ActivationSwitch.onValueChanged` → `EnableBot(bool)` → tweens hidden switch visuals + writes `Status`.
3. `BotStatusPill.LateUpdate` observes `statusSource.color` → renders pill.

`Bot.cs`'s DOTween calls on `switchBackgroundImage` / `switchHandleImage` / `switchHandle` run silently off-canvas — no visible effect, no exceptions.

## `Bot.cs` contract preservation

| Field | Requirement | This session |
|-------|-------------|--------------|
| `Status` (TMP) | Not null-guarded; written in `EnableBot`/`SetSwitches`. | Wired. Lives inside `StatusPill`. `.enabled = false` to hide. |
| `EditButton` | Null-guarded. | Wired to `BotCard` root's `Button` — entire card is the edit tap target. |
| `DeleteButton` | Null-guarded. | Unassigned. |
| `ActivationSwitch` (Toggle) | Not null-guarded. `SetSwitches` dereferences `.transform.GetChild(0).GetChild(0)` → requires full hierarchy. | Parked off-canvas with full hierarchy. |
| `DeletePopup`, `DeleteConfirmButton`, `DeleteCancelButton` | All null-guarded transitively. | Unassigned. |
| `backgroundActiveColor`, `handleActiveColor` | Used by tween. | Keep current values (preserved by editor script). |

## `BotsPage.cs` contract preservation

| Field | This session |
|-------|--------------|
| `MainPage` | Wired (unchanged). |
| `BotsParent` | Wired to new scroll content's parent. |
| `Chanel` | Wired (unchanged). |
| `MainPageButton` | Wired to back affordance if present; else unassigned. |
| `AllBotsButton`, `ActiveBotsButton` | Unassigned. Filter UI removed this session. |
| `NewBotButton` | Wired to the `+` icon in header. |

Brittle code path `BotsPage.OpenActiveBots` — `bot.GetChild(1).GetComponent<Toggle>()` — will silently break because child 1 is now `BotDetails`, not the toggle. Acceptable: the only caller (`ActiveBotsButton`) is not in the UI. Removal of the filter code itself is a separate cleanup session.

## Palette / typography / scale constants

Copied verbatim from `AuthPageSetup.cs`:
- Canvas reference: 1080 × 1920, design-pt × 2.77 ≈ canvas units (pre-multiplied).
- Fonts: `SFProText-Regular`, `SFProText-Medium`, `SFProText-Semibold`, `SFProText-Bold` from `Assets/TextMesh Pro/Fonts/`.
- Colors: `#F2F2F7` bg, `#FFFFFF` card, `#1C1C1E` text-primary, `#8E8E93` text-secondary, `#C7C7CC` text-tertiary, `#E5E5EA` border, `#007AFF` iOS blue. Status-specific: `#34C759`/`#E8F8EE` (success), `#FF3B30`/`#FFECEC` (danger).

## Testing

Manual only — no automated tests (editor scripts + thin runtime observer).

1. Open Unity Hub → open project (version 6000.3.9f1).
2. Menu: `Tools > Setup My Bots Page`.
3. Save scene (Cmd+S).
4. Enter Play mode in Game view at 1080×2400.
5. Verify:
   - Status bar + nav header render correctly.
   - Header title reads "Мои Боты", `+` icon opens `Chanel` page.
   - Existing bots (from `PlayerPrefs`) render as cards with correct name/desc/icon/pill.
   - Pill shows "Активен" (green) / "Неактивен" (red) / "Подключение" (blue) as `Status.color` changes.
   - Tapping a card opens that bot's settings page.
   - Toggle state persists across play-mode restart.
   - Bottom nav still works — "Мои Боты" tab still highlights correctly.

## Risks / follow-ups

- **Icon gradients** from the mockup are a 2-stop CSS `linear-gradient`. Unity UI doesn't support gradients without additional setup. This session will approximate with a solid color (midpoint of the gradient stops) — gradient fidelity can be a future polish task.
- **Search icon** in the header is visual-only this session; no search feature.
- **Filter cleanup** (removing `AllBotsButton` / `ActiveBotsButton` fields and `OpenActiveBots` from `BotsPage.cs`) deferred to a separate session.
- **Delete UX** (swipe-to-delete / long-press) entirely deferred — `Bot.cs`'s delete machinery remains intact but unreachable from this page.
- **Bot-card height** is hard-coded; long bot names may wrap. Acceptable for this session given description is single-line truncated.

## Out of scope (explicit)

- Bot Settings page (General / Business / Products / Services / Prompts tabs).
- Any runtime API changes, new endpoints, or secrets handling.
- `Product.cs`, `Service.cs`, chat code, or anything under `Assets/Scripts/Chat/`.
