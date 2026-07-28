# Phase 6: Channel Switcher UI - Context

**Gathered:** 2026-07-13
**Status:** Ready for planning
**Source:** Design-spec express path (docs/superpowers/specs/2026-07-12-telegram-parity-design.md §D1; .planning/research/telegram-parity/ui-scaffolding.md; autonomous session). UI design contract embedded below (deliberate substitute for a separate UI-SPEC — the design decisions are already locked and the visual precedent is an existing in-scene control).

<domain>
## Phase Boundary

The owner flips between the active bot's WhatsApp and Telegram chats inside the existing chats screen via a TopBar segmented control; the Telegram bottom tab and `Screen_Telegram` placeholder are retired; tab 0 reads «Чаты». All ChatManager machinery exists (Phase 5): `ActiveChannel`, `SetActiveChannel`, `OnActiveChannelChanged`, per-channel empty states, `ActiveChannelSupportsChatDelete`.

**Environment reality:** Unity Editor is CLOSED. Builders run HEADLESSLY via `-batchmode -executeMethod` (a new `Tools/run-editor-builder.sh` modeled on `run-tests-headless.sh`). The scene mutation is committed IMMEDIATELY after the builder run (project memory: parallel-scene-clobber; benign churn was pre-committed separately). Visual/device polish verification is impossible headless → recorded in `06-HUMAN-UAT.md` (Editor screenshot pass at 1080×2400 + device feel), same open-gate pattern as Phases 3/4.

NOT in this phase: suggestions/dashboard (Phase 7), swipe-to-delete visual affordance removal beyond the existing no-op guard IF it requires per-row prefab surgery (record as UAT follow-up if so; the network no-op from 05-03 already protects correctness).

</domain>

<decisions>
## Implementation Decisions

### UI contract (locked)

**Placement:** `Screen_Whatsapp/ChatsPanel/TopBar/CenterZone` — currently INACTIVE, 360×140, anchored center (0.5/0.5), pos y=-60, holding an unused `Title` TMP. The builder activates CenterZone, removes/deactivates the unused Title, and builds the pill there. LeftZone (BotSwitcherTitle) and RightZone (ModeToggle + NewChatButton) are untouched.

**Visual language:** mirror the existing `ModeToggle` segmented pill (RightZone) exactly — same corner rounding approach (null sprite + RoundedCorners, NEVER UISprite.psd), same track/knob-or-fill pattern, same font asset + type scale, same touch-target height. The executor MUST read the ModeToggle construction (grep `Assets/Editor/` for the builder that created it — `ReplyModeToggle`/`Screen_WhatsappHeaderRebuilder` family) and copy its measured values (track color, chip padding, label size) rather than inventing new ones. 1080×1920 reference units (dp×3), spacing in multiples of 4 canvas units at minimum.

**Chips:** two segments — «WhatsApp» | «Telegram» (labels; if the BotSwitcherRowView channel-chip sprites/colors are reusable as 40-48u leading icons, reuse them; otherwise text-only is acceptable v1). Brand accents only on the SELECTED chip: WhatsApp `#25D366`-family green (match whatever BotSwitcherRowView uses), Telegram `#2AABEE` (`Manager.TelegramBrandColor` already defines it). Unselected chip: neutral text on transparent.

**States (locked behavior):**
- Selected chip = filled; unselected = transparent (ModeToggle pattern).
- A channel whose profile is NOT connected (`"-1"`/empty) renders MUTED (≈40% alpha on its label/icon) but stays TAPPABLE — tapping selects it and the 05-02 empty state (BotHasNoWhatsApp/BotHasNoTelegram) shows the connect CTA. No dead chips, no hidden switcher: both chips always visible for every bot (discoverability decision D1).
- Switch tap: `ChatManager.Instance.SetActiveChannel(channel)`; no-op when already active.
- Reacts to: `OnActiveBotChanged` (recompute both chips' connectivity + selection for the new bot), `OnActiveChannelChanged` (move selection), and BotSettings-driven connectivity changes are covered on next bot/channel event (no polling).

### Runtime code (locked)

- Pure logic seam: `ChannelSwitcherModel` (new, `Assets/Scripts/UI/`) — static/pure: given `(waConnected, tgConnected, active)` returns per-chip `{selected, muted}`; EditMode-tested (connected/unconnected × active matrix, both channels).
- View binder: `ChannelSwitcherView` MonoBehaviour (new, `Assets/Scripts/UI/`) — `[SerializeField]` refs stamped by the builder (chip Buttons, labels, fills); subscribes in OnEnable/unsubscribes in OnDisable (event-driven, no Update polling); reads connectivity via `Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId)` + the `Bot.UnauthedProfileSentinel` convention (same predicate semantics as `BotSwitcherRowView`'s chips).
- Late-activation catch-up: on OnEnable, pull current state directly (bot may have been switched while the screen was inactive) — mirror how other TopBar binders do it.

### Editor builders + headless run (locked)

- New `ChannelSwitcherBuilder` in `Assets/Editor/` — `[MenuItem("Tools/Channel Switcher/Build")]` + a public static parameterless method suitable for `-executeMethod`. Idempotent delete-and-rebuild (project memory: builder scene-save discipline — open Main.unity, build, mark dirty, SAVE scene, no Undo grouping). Stamps all `ChannelSwitcherView` serialized refs via SerializedObject.
- Nav restructure (same headless run, second static entry or same method): remove `tabs[1]` (Telegram) from `BottomTabManager` via SerializedObject array edit; relabel tab 0 to «Чаты» (find the tab's label TMP under its tabRoot); deactivate+DELETE the `Screen_Telegram` GameObject AND its `TelegramTab` bottom-bar root; update `NavRestructureBuilder.ReorderScreens`' expected-order list (remove Screen_Telegram) so future builder runs don't warn.
- **Tab-index shift audit (critical):** removing tabs[1] shifts Сводка/Bots/Profile from indices 2/3/4 to 1/2/3. The plan MUST grep-audit every tab-index reference — `BottomTabManager` (WhatsAppTabIndex, defaultTabIndex, any hardcoded index), `TabRefreshGate`, `DashboardPage` (deep-link SwitchTab), `EmptyStateView.OpenCurrentBotAuth` (switches to Bots tab), `ProfileSubPages*`, `BotsPage` — and update constants/scene values so every SwitchTab target still lands on the right screen. Add/extend an EditMode test where a pure seam exists (e.g., TabRefreshGate).
- New `Tools/run-editor-builder.sh` — mirrors `run-tests-headless.sh` conventions (refuses if Editor lock present, logs to Tools/test-output/, passes `-batchmode -nographics -projectPath . -executeMethod <entry> -quit`, checks exit code and greps the builder's completion log line).
- Scene commit IMMEDIATELY after a successful headless run, scene payload verified by grepping Main.unity for the new objects' names/GUIDs (memory: verify payload by GUID grep). Then run the headless TEST suite (builder must not break compilation or tests).

### Verification (locked)

- EditMode: ChannelSwitcherModel matrix, tab-index seam tests, full suite green (baseline 891).
- Structural scene asserts post-build (grep Main.unity): `ChannelSwitcher` objects exist under CenterZone; `Screen_Telegram` absent; BottomTabManager tabs array has 4 entries; tab-0 label «Чаты».
- `06-HUMAN-UAT.md`: owner visual pass (Editor Game view 1080×2400: pill matches ModeToggle styling, muted chip reads clearly, switch swaps lists without flicker, Telegram-only bot auto-selects Telegram with WhatsApp chip muted, tab bar shows 4 tabs with «Чаты») — open gate, phase closes on it.

### Claude's Discretion
- Exact object naming, whether nav restructure lives inside ChannelSwitcherBuilder or a NavRestructureBuilder update, icon usage vs text-only chips, DOTween micro-transition on selection (match ModeToggle; omit if ModeToggle has none).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

- `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` — §D1 (the locked UX decision + rationale)
- `.planning/research/telegram-parity/ui-scaffolding.md` — scene fileIDs/line refs: CenterZone slot, tabs array (tabs[1].screenPanel line 135599), Screen_Telegram (fileID 163358610), ScreenContainer order, BotSwitcher wiring, SetActiveBot reset choreography §4
- `.claude/skills/unity-ui-builder/SKILL.md` — MANDATORY: calibrated type/spacing scale, rendering gotchas (null sprite + RoundedCorners; TMP icons don't render; RectMask2D Maskable audit for new TopBar children)
- `.claude/skills/mobile-app-ui-design/SKILL.md` — design judgment (thumb zones, 60/30/10) — dp values need ×3
- `Assets/Editor/Screen_WhatsappHeaderRebuilder.cs` + the ModeToggle builder (grep Assets/Editor for ReplyModeToggle construction) — the construction pattern + measured values to copy
- `Assets/Scripts/UI/ReplyModeToggleBinder.cs`, `BotSwitcherRowView.cs`, `BotSwitcherTitleBinder.cs` — binder + chip-connectivity precedents
- `Assets/Editor/NavRestructureBuilder.cs` — SerializedObject rewire idiom (L384-405) + ReorderScreens list (L422)
- `Assets/Scripts/Main/BottomTabManager.cs`, `TabRefreshGate.cs` — tab wiring + index seam
- `Tools/run-tests-headless.sh` — the headless-launch conventions to mirror
- Project memories honored: builders-must-rewire-consumers (grep consumers, SerializedObject), bubble-graphics-must-be-Maskable, builder scene-save, parallel-scene-clobber (immediate commit + GUID grep), unity-new-file-import quirk

</canonical_refs>

<specifics>
## Specific Ideas

- `BottomTabManager.WhatsAppTabIndex = 0` keeps its value; consider renaming to `ChatsTabIndex` ONLY if the rename is mechanical (IDE-safe greps) — otherwise leave and comment.
- `ChatListPreWarmer` pre-warms Screen_Whatsapp — unaffected, verify no Screen_Telegram reference.
- The deleted `Screen_Telegram` had a live tab pointing at it — after removal, `defaultTabIndex: 0` still lands on Screen_Whatsapp; verify BottomTabManager tolerates a 4-entry array on first SwitchTab.
- EmptyStateView Telegram copy shipped in 05-02; this phase only ensures the CTA still targets the right (shifted) Bots tab index.
</specifics>

<deferred>
## Deferred Ideas

- Per-row swipe-delete affordance hiding on Telegram (needs ChatItemView/prefab surgery; the 05-03 guard already makes it a safe no-op) → record in 06-HUMAN-UAT as a polish follow-up decision.
- RU localization sweep of English empty-state copy (IN-09 from 05-REVIEW) → pre-store polish.
- Screen_Whatsapp → Screen_Chats rename → cosmetic, many refs, defer.
</deferred>

---

*Phase: 06-channel-switcher-ui*
*Context gathered: 2026-07-13 via design-spec express path*
