# Bot card activation switch — split-card footer

**Date:** 2026-07-06
**Status:** Approved (design), pending implementation plan
**Scope:** `Assets/Prefabs/Bot.prefab`, `Assets/Scripts/Main/Bot.cs`, one new editor builder, one new pure helper + EditMode tests, CLAUDE.md touch-up

## Background

The per-bot activation switch (`ActivationSwitch` in `Bot.prefab`, driven by `Bot.cs`) was displaced during the card redesign — parked at anchored position `(-9999, -9999)` with scale 0 — but never unwired. `Bot.EnableBot` still persists the on/off state (`PlayerPrefs.SetInt(transform.name, …)`), fires `Manager.GetEnableWhatsappWorkflow` / `GetEnableTelegramWorkflow` (n8n activate/deactivate), and writes the hidden `Status` TMP that `BotStatusPill` mirrors into the Активен / Подключение / Неактивен pill.

This design restores the switch as a **split card**: the existing top row keeps opening settings; a new footer row owns the switch.

Bots are runtime-instantiated from `Manager.BotPrefab` — the scene holds no baked `Bot` card instances (verified: the prefab guid appears once in `Main.unity`, as the `Manager.BotPrefab` reference). The change is therefore prefab + code only; no scene surgery.

## Layout (reference units, 1080×1920 canvas)

```
┌──────────────────────────────────────────────┐
│ [icon] [BotName        ] [pill: Активен] [›] │  ← existing Row, untouched (232)
│ [    ] [BotDesc        ]                     │
├──────────────────────────────────────────────┤  ← divider, 2 units, #E9E9EB
│ Бот работает                        [==ON==] │  ← FooterRow, 96 units
└──────────────────────────────────────────────┘
```

- **Card root:** height 232 → **≈348** (232 row + 2 divider + 96 footer + breathing room; final value lands on the 4-unit grid with footer bottom padding visually equal to the card's top padding). The builder updates whatever drives the height (root `sizeDelta` and/or `LayoutElement`) consistently.
- **Divider:** 2-unit hairline, `#E9E9EB`, inset to the card's existing horizontal content padding (match the Row's padding — do not invent new insets).
- **FooterRow:** 96 tall, horizontal: label left, switch right, vertically centered, same horizontal padding as the Row.
- **Switch (existing `ActivationSwitch` object, moved — not recreated):**
  - Track (`Background`): **150×84**, corner radius 42.
  - Handle: **74×74**, radius 37, rest inset 5 units from each track end → travel **±33** from center.
  - Keep the `ActivationSwitch → Background → Handle` child chain exactly — `SetSwitches` resolves the handle via `GetChild(0).GetChild(0)`.
  - Restore `localScale = 1`, drop the `-9999` parking, clear `LayoutElement.ignoreLayout`.
  - Rounded corners via **null sprite + RoundedCorners** (no `UISprite.psd`). RoundedCorners type must be resolved by AppDomain scan (it lives in its own UPM assembly, not `Assembly-CSharp`), radius set explicitly, `Validate()/Refresh()` after a forced layout pass.
- **Handle travel:** `SetSwitches` currently computes the rest position as `-30 * trackWidth / 160` — a magic constant tuned to the old 100×40 geometry (it yields −28.1 for a 150 track; target is −33). Replace with the geometry-derived form `-(trackWidth − handleWidth) / 2 + inset` (inset = 5) so the knob lands 5 units from the ends at any size.

## Colors

| Element | Value | Note |
|---|---|---|
| Track on (`backgroundActiveColor`, serialized on `Bot`) | `#34C759` | was `#00CC00`; now matches pill FgActive |
| Track off (Background image default color in prefab) | `#E9E9EA` | iOS-style off gray |
| Handle (both states) | `#FFFFFF` | `handleActiveColor` already white; set the prefab default white too (the on/off color tween becomes a no-op — fine) |
| Divider | `#E9E9EB` | |
| Label on | `#3A3A3C` | |
| Label off | `#8E8E93` | |

Pill palette (`BotStatusPill`) is untouched.

## Behavior

- **Footer label:** new TMP, font asset matching `BotDesc`, size 38 (Body2), left-aligned (alignment set explicitly). Text/color:
  - switch on → «Бот работает» / `#3A3A3C`
  - switch off → «Бот на паузе» / `#8E8E93`
  - The label reflects the switch's *desired* state; the pill owns *live* status. During «Подключение» the label stays «Бот работает».
- **Pure helper:** `Assets/Scripts/Main/BotSwitchFooter.cs` — static mapping `(bool isOn) → (string text, Color color)`, following the project's testable-pure-helper pattern (`ScrollFabMath`, `ReactionBarLayout`). EditMode tests in `Assets/Tests/Editor/Chat/BotSwitchFooterTests.cs` (predefined `Assembly-CSharp-Editor`, no asmdef).
- **`Bot.cs` changes (minimal):**
  - New `[SerializeField] private TextMeshProUGUI SwitchFooterLabel;` (PascalCase private, matching `BotIconTile` style).
  - Apply the helper's text+color in `EnableBot` and in both branches of `SetSwitches`; null-guard the ref.
  - Nothing else changes: same `PlayerPrefs` key (bare bot name), same n8n enable/disable calls, same `Status` data channel, same DOTween handle/track animation.
- **No confirmation** on toggle in either direction (decided): instant flip, reversible; the red pill + «Бот на паузе» label are the feedback. No snackbar/undo.
- **Unauthed/missing-workflow bots:** unchanged contract — `EnableBot` already forwards to Manager as before displacement; no new card-level guard.

## Tap zones

- Top Row → opens settings (existing `EditButton` behavior, untouched).
- FooterRow gets a transparent `Image` (`color (1,1,1,0)`, `raycastTarget = true`) so footer taps never fall through to the settings button. The `Toggle` renders above it and keeps receiving its own taps. The switch is the only interactive element in the footer.

## Build approach

One idempotent editor builder, `Assets/Editor/BotCardFooterBuilder.cs` (`[MenuItem]`, path following the existing builder menu convention):

1. `PrefabUtility.LoadPrefabContents("Assets/Prefabs/Bot.prefab")`.
2. Delete any existing `Divider` / `FooterRow` children (delete-and-rebuild idempotency; no Undo grouping).
3. Build divider + footer (label TMP + raycast blocker), reparent `ActivationSwitch` into the footer, apply sizes/colors/RoundedCorners per this spec.
4. Update serialized data via `SerializedObject`: `Bot.SwitchFooterLabel` ref, `backgroundActiveColor`, prefab default track/handle colors, root height.
5. `SaveAsPrefabAsset`, unload contents. Edit Mode only.

No scene save required (prefab-only edit). Known project quirk: a brand-new `.cs` written during a busy refresh can be silently excluded from compilation — if the builder type isn't found, delete the `.cs` + `.meta` and recreate.

## Out of scope

- Undo snackbar (no primitive exists; not building one).
- «Только активные» filter interactions — the filter no longer exists in the codebase.
- Reply-mode toggles (Авто/Вместе) — different axis, untouched.
- Pill palette or copy changes.

## Verification

1. EditMode tests green (`BotSwitchFooterTests`) via the test bridge / headless script.
2. Run the builder; open Game view at 1080×2400; visually check the card in all states: on+Активен, on+Подключение, off+Неактивен — switch position/colors, label text/color, divider insets, no layout overlap with long bot names.
3. Tap checks in Play Mode: top row opens settings; footer tap (not on switch) does nothing; switch toggles with the existing DOTween animation and flips the pill.
4. Device pass per the usual GREEN loop (optional for this change; Editor rendering is representative for this card).

## Documentation touch-up

CLAUDE.md's `BotsPage.cs` line still claims an "all vs active filter" (`BotsPage.onlyActiveBotsVisible`) — stale; remove alongside the implementation commit.
