# About Business tab — structured contact fields

- **Date:** 2026-07-24
- **Status:** Approved design (ready to plan)
- **Area:** BotSettings → «Бизнес» tab; per-bot persistence; n8n bot-knowledge payload
- **Scope:** Add 5 structured contact fields to the business tab, persist them per-bot, and feed them into the bot's knowledge. No n8n workflow rework.

## Summary

The bot-settings «Бизнес» tab currently holds a single free-text field, `Описание` (business description). This spec adds a second grouped section, `КОНТАКТЫ И ИНФОРМАЦИЯ`, with five single-line fields — **Телефон, Часы работы, Адрес, Instagram, Email** — reusing the existing `EditableField` card primitive. Each value persists per-bot in PlayerPrefs and is composed into the `Business` knowledge string already sent to the n8n edit/create workflows, so the bot can answer questions like «во сколько закрываетесь?» / «где вы находитесь?» / «как позвонить?» with no changes to any n8n workflow.

## Goal

The bot owner can enter their phone, working hours, address, Instagram, and email in bot settings; those values survive save/close/reopen/restart, are wiped when the bot is deleted, and become part of what the bot knows and can tell customers.

## Background — current state (verified)

- **The field:** `[SerializeField] public EditableTextArea BusinessField;` — `Assets/Scripts/Main/BotSettings.cs:50`. Multi-line card, height 240, with `ScrollableTextArea`/`DragShield` for internal touch-scroll.
- **Persistence key:** `<botName>Business` (bot-persistence pattern keyed by the Bot GameObject's `transform.name`).
- **Existing `Business` touch-points** (all `Assets/Scripts/Main/Manager.cs`, line numbers are a current-snapshot guide — match by surrounding code, not the number alone):
  - `~383-385` read on recreate → mirrors into the bot card's `BotDesc` subtitle label.
  - `~416` read on recreate → `recreatedBotSettings.BusinessField.Value = PlayerPrefs.GetString(name+"Business","")`.
  - `~721-723` on save → copies `BusinessField.Value` into `openBotComp.BotDesc.text`.
  - `~742` **write** → `PlayerPrefs.SetString(openBot.name + "Business", openBotSettings.BusinessField.Value)`.
  - `~851` revert on `CloseSettings` → restores field from pref.
  - `~909` dirty-check → compares field value to saved pref to gate `EnableSave`.
  - `~1426, ~1464` new-bot creation → seeds field + pref from `formDescription`.
  - `Assets/Scripts/Main/Bot.cs:197` on delete → `PlayerPrefs.DeleteKey(transform.name + "Business")`.
- **n8n send sites** (`Manager.cs`): the value reaches n8n as the form field **`Business`**:
  - `~3183` CreateWhatsappWorkflow (from Edit) → `"About Business:\n" + BusinessField.Value`.
  - `~3341` CreateTelegramWorkflow (from Edit) → `"About Business:\n" + BusinessField.Value`.
  - `~3576-3585` shared `WWWForm` for `EditWhatsappWorkflow`/`EditTelegramWorkflow`; `~3582` `form.AddField("Business", openBotSettings.BusinessField.Value)` (raw, no prefix); sent at `~3625` (WA) / `~3663` (TG).
  - `~3090` / `~3245` Create-from-Start send `Business = ""` (brand-new bot, no settings yet) — **left unchanged**.
- **Wiring:** `BusinessField.OnCommitted → Manager.Instance.EnableSave()` — `BotSettings.cs:452-453` in `WireFields()`.
- **Builder:** `BuildBusinessOrPromptTab(...)` — `Assets/Editor/BotSettingsRebuilder.cs:591-602`, called at `:306`. The Business + Prompt tabs are **non-scrollable** (`nonScrollableTabs = { "Business", "Prompt" }`, `:488`) because each holds one internally-scrolling field. Scale factor `S = 2.5`; single-line card height 64, multi-line 240; content VLG padding `(20,20,24,24)`, spacing 12. `CreateEditableField(...)` at `:693`; `AddSectionHeader(...)` at `:963`. A separate menu, `BotSettingsScrollableTextAreaBuilder` («Tools/BotSettings/Build Scrollable Business+Prompt»), converts `BusinessField`/`PromptField` into the scrollable-textarea setup after a rebuild.

## Non-goals

- No changes to any n8n workflow (Create/Edit WhatsApp/Telegram) — the new data rides the existing `Business` payload.
- No structured hours picker, map/geocoding, phone/email format validation, or click-to-call — all fields are free text. (YAGNI; revisit only if UAT demands it.)
- No new field in the bot-creation wizard — the 5 fields start empty on a new bot and are filled from settings.
- No change to the `Сайт` (website) idea — deliberately dropped in favor of Instagram for the KZ market.

## Final field set

| Field | Label (RU) | PlayerPrefs suffix | Primitive | Keyboard |
|---|---|---|---|---|
| Phone | `Телефон` | `Phone` | `EditableField` (single-line) | phone pad |
| Working hours | `Часы работы` | `Hours` | `EditableField` (single-line) | default |
| Address | `Адрес` | `Address` | `EditableField` (single-line) | default |
| Instagram | `Instagram` | `Instagram` | `EditableField` (single-line) | default |
| Email | `Email` | `Email` | `EditableField` (single-line) | email |

Ordering (UI and composed knowledge): Телефон → Часы работы → Адрес → Instagram → Email — weighted by how often each comes up in a customer chat for the KZ target verticals.

## UI design

**Layout** — the «Бизнес» tab gains a second section under the existing description:

```
ОПИСАНИЕ БИЗНЕСА            (SectionHeader, existing)
  [ Описание ]             (EditableTextArea, existing, height 240)

КОНТАКТЫ И ИНФОРМАЦИЯ        (SectionHeader, new)
  [ Телефон ]              (EditableField, height 64)
  [ Часы работы ]
  [ Адрес ]
  [ Instagram ]
  [ Email ]
```

- Each new field is the standard `EditableField` card: white `#FFFFFF`, 12px-radius, hairline `#E4E6EB` border; label 12pt muted `#8E8E93` top-left; value 16pt `#1A1A2E`; empty state shows an italic muted placeholder. Cards flow under the tab's `VerticalLayoutGroup` at spacing 12. Matches the existing name/number cards exactly — no new visual component.
- **The tab becomes scrollable.** Remove `"Business"` from `nonScrollableTabs` in `BotSettingsRebuilder` so the Business tab is built with the same `ScrollRect` + viewport + `Content` (VLG + `ContentSizeFitter`) branch as the General/Product/Service tabs (`BotSettingsRebuilder.cs:508-535`). The description field stays a fixed-height `ScrollableTextArea`; its `DragShield`/`ScrollableTextArea` already forward drags to the parent `ScrollRect` (so a drag scrolls the tab, and internal text-scroll engages only while the field is focused) — the nested-scroll case is already solved in this codebase. `Prompt` stays in `nonScrollableTabs`.
- **Keyboards:** set the inner `TMP_InputField` on the phone field to a phone keyboard and the email field to `ContentType.EmailAddress` (numeric/email keypads on device). Do this in the builder via the field's `TMP_InputField` reference; default (standard) for the other three.

**Builder changes** (`Assets/Editor/BotSettingsRebuilder.cs`):
1. Split/extend `BuildBusinessOrPromptTab` into a dedicated `BuildBusinessTab` that, after the existing description block, adds `AddSectionHeader(tab, "КОНТАКТЫ И ИНФОРМАЦИЯ")` and five `CreateEditableField(tab, "<label>", scrim, multiline:false)` calls, returning references to all five. The Prompt tab keeps calling `BuildBusinessOrPromptTab`.
2. Remove `"Business"` from `nonScrollableTabs`.
3. **Stamp the new refs onto `BotSettings` via `SerializedObject`** — `so.FindProperty("PhoneField")`, `HoursField`, `AddressField`, `InstagramField`, `EmailField` — in the same serialization block that already stamps `BusinessField`. (Destroy-and-rebuild wipes serialized refs silently; this rewire is mandatory — see `[[project_builder_rewire_consumers]]`.)
4. Re-run «Tools/BotSettings/Build Scrollable Business+Prompt» after the rebuild, as today, to (re)apply the description's scrollable-textarea setup.

## Data model & lifecycle

Add five per-bot keys — `<botName>Phone`, `<botName>Hours`, `<botName>Address`, `<botName>Instagram`, `<botName>Email` — and mirror the existing `Business` handling at every touch-point:

| Site (`Manager.cs`, ~line) | Action for each of the 5 fields |
|---|---|
| Recreate `~416` | `settings.<X>Field.Value = PlayerPrefs.GetString(name + "<X>", "")` |
| Save `~742` | `PlayerPrefs.SetString(openBot.name + "<X>", openBotSettings.<X>Field.Value)` |
| Revert on close `~851` | restore `<X>Field.Value` from its pref |
| Dirty-check `~909` | include each field's `value != savedPref` in the dirty test |
| New-bot create `~1426/1464` | seed `""` (fields start empty) — optional but symmetric |
| Delete (`Bot.cs:197`) | `PlayerPrefs.DeleteKey(transform.name + "<X>")` for all 5 |

`BotSettings.cs`: add `[SerializeField] public EditableField PhoneField, HoursField, AddressField, InstagramField, EmailField;` and wire each `OnCommitted → Manager.Instance.EnableSave()` in `WireFields()` (alongside `BusinessField` at `:452-453`).

## Feeding the bot (no n8n rework)

Add one helper on `Manager`:

```csharp
// Description + labeled contact block. Empty lines/section skipped.
string ComposeBusinessKnowledge(BotSettings s)
```

Produces:
```
About Business:
<Описание>

Контакты:
Телефон: <phone>
Часы работы: <hours>
Адрес: <address>
Instagram: <instagram>
Email: <email>
```
Rules: keep the `About Business:` header + description (matches today's edit-webhook format). Emit the `Контакты:` block only if ≥1 contact field is non-empty; within it, emit only the non-empty lines.

Apply the helper at the three edit/save send sites, replacing the raw/prefixed business value:
- `~3183` CreateWhatsappWorkflow (from Edit) → `ComposeBusinessKnowledge(openBotSettings)`
- `~3341` CreateTelegramWorkflow (from Edit) → `ComposeBusinessKnowledge(openBotSettings)`
- `~3582` shared Edit form → `form.AddField("Business", ComposeBusinessKnowledge(openBotSettings))`

This standardizes all three on one labeled format. The only behavioral change at `~3582` is that the Edit-workflow `Business` payload gains the `About Business:` prefix and the contact block — deliberate and safe, since the value is free-text prompt context the model reads. Create-from-Start (`~3090/3245`) stays `""`.

**Bot-card subtitle unchanged:** the `BotDesc` label (`Manager.cs:~384, ~722`) keeps showing only `BusinessField.Value` (the description), so the Bots list stays clean — the composed contact block never appears there.

## Files to change

- `Assets/Scripts/Main/BotSettings.cs` — 5 new `EditableField` serialized refs; wire `OnCommitted` in `WireFields()`.
- `Assets/Scripts/Main/Manager.cs` — `ComposeBusinessKnowledge` helper; mirror the 5 keys at recreate/save/revert/dirty-check/create; swap the 3 send sites to the helper.
- `Assets/Scripts/Main/Bot.cs` — delete the 5 keys in `DeleteBot()`.
- `Assets/Editor/BotSettingsRebuilder.cs` — `BuildBusinessTab` (section header + 5 fields), remove `"Business"` from `nonScrollableTabs`, stamp the 5 refs via `SerializedObject`, set phone/email keyboard types.
- `Assets/Prefabs/BotSettings.prefab` + `Assets/Scenes/Main.unity` — regenerated by running the builder; commit the resulting binary/scene churn immediately after apply.
- Tests — add EditMode coverage (below).

## Testing

- **Unit (EditMode, primary):** `ComposeBusinessKnowledge` — (a) description-only → no `Контакты:` block; (b) all fields set → labeled block in the fixed order; (c) partial fields → only non-empty lines emitted; (d) all contacts empty but description present → header + description only.
- **Persistence round-trip (EditMode, PlayerPrefs works in-Editor):** set the 5 fields → save writes the 5 keys; close reverts unsaved edits; `DeleteBot` removes all 5 keys; two bots don't cross-read (namespacing by `transform.name`).
- **Manual / device:** the tab scrolls smoothly with all 6 cards; dragging over the description scrolls the tab, tapping it edits; phone shows a numeric keypad, email an email keypad; values survive app restart; a saved bot's reply references the contact info (bot workflow activated only during a supervised test — real contacts; see `[[feedback_bot_activation_policy]]`).
- Run via the project test bridge (`Temp/claude/run-tests.trigger` when the Editor is open, else `Tools/run-tests-headless.sh`). Do not trust Play-Mode green.

## Risks & gotchas

- **Serialized-ref wipe:** the rebuild destroys and recreates the tab; the 5 new refs (and `BusinessField`) MUST be re-stamped via `SerializedObject` or they silently null out — `[[project_builder_rewire_consumers]]`.
- **Scene/prefab clobber:** commit the regenerated `BotSettings.prefab` + `Main.unity` immediately after running the builder; a parallel session saving the scene will clobber uncommitted component adds — `[[project_parallel_scene_clobber]]`, `[[project_unity_builder_scene_save]]`.
- **New-file import:** no brand-new `.cs` files here (all edits to existing files), so the new-file import quirk doesn't apply; if any helper is split into a new file, run Assets/Refresh and confirm the `.meta` appears — `[[project_unity_new_file_import]]`.
- **Nested scroll:** already handled by `ScrollableTextArea`/`DragShield`; do not add a second scroll handler to the description.
- **Line numbers drift:** all `Manager.cs` line references are a current snapshot — locate by the surrounding `Business` code, since edits shift lines.

## Open questions

None blocking. Deferred/optional: upgrade `Адрес` to a short multi-line `EditableTextArea` if single-line truncation proves annoying in UAT; add lightweight format hints for phone/email later if owners enter junk.
