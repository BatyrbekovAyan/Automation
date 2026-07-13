---
phase: 06-channel-switcher-ui
reviewed: 2026-07-13T11:16:06Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - Assets/Scripts/UI/ChannelSwitcherModel.cs
  - Assets/Scripts/UI/ChannelSwitcherView.cs
  - Assets/Scripts/Main/BottomTabManager.cs
  - Assets/Editor/ChannelSwitcherBuilder.cs
  - Assets/Editor/NavRestructureBuilder.cs
  - Assets/Editor/DashboardPageBuilder.cs
  - Tools/run-editor-builder.sh
  - Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs
  - Assets/Tests/Editor/Chat/TabIndexShiftTests.cs
  - Assets/Scenes/Main.unity
findings:
  critical: 0
  warning: 3
  info: 5
  total: 8
status: issues_found
---

# Phase 6: Code Review Report

**Reviewed:** 2026-07-13T11:16:06Z
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Phase 6 (channel switcher UI + nav restructure) is in good shape at runtime: the pure model is correct and tested, the view binder is fully null-guarded with symmetric event lifecycle, the builder's own idempotency guard works, and the committed scene state is consistent with `BotsTabIndex = 2`. No critical issues.

The real residue lives in the **superseded Editor builders**: `NavRestructureBuilder` (edited in this phase) still hardcodes pre-restructure tab indices, so a re-run against the committed 4-tab scene silently corrupts navigation and — via its headless entry — auto-saves the corruption. `run-editor-builder.sh` compounds this: its advertised `$1` method override always reports NOT GREEN because the success sentinel is hardcoded, after the scene has already been mutated and saved.

### Verified sound (explicitly scrutinized, no finding)

- **View lifecycle** (`ChannelSwitcherView.cs:60-83`): OnEnable/OnDisable are symmetric; Unity pairs the two callbacks, so no double-subscribe is possible, and unsubscribing a never-subscribed handler is a no-op. The `ChatManager.Instance == null` early-return in OnEnable is unreachable at scene load: `ChatManager` carries `[DefaultExecutionOrder(-100)]` and assigns `Instance` in `Awake` (ChatManager.cs:198), and Script Execution Order applies to the Awake/OnEnable batch at scene load — so the -100 manager initializes before any default-order OnEnable regardless of hierarchy position. Matches the `ReplyModeToggleBinder` precedent exactly. Residual risk exists only if ChatManager's GameObject ever starts inactive, which is not the case in this scene.
- **Null-guard completeness**: `Refresh()` degrades to WhatsApp-selected/both-muted defaults with Manager, ChatManager, or the bot missing; `Manager.FindBotByName` (Manager.cs:32-38) is null/empty-safe, so the `botId = null` path is safe. Deleted-bot-mid-screen cannot NRE (T-06-02 satisfied).
- **Tap flow**: `OnChipTapped` kills tweens and resets scale before punching; `OnDisable` kills both chips' tweens; `SetActiveChannel` no-ops on same-channel taps (ChatManager.Channel.cs:52); muted chips stay tappable so the connect empty state is reachable (SWITCH-02).
- **Model matrix**: "both channels unconnected" is sane — the active (default WhatsApp) chip renders selected+muted, both chips muted, both tappable → connect CTA. Locked by test E.
- **ChannelSwitcherBuilder idempotency**: delete-and-rebuild by exact name under CenterZone with a destroyed-mid-iteration guard (`t != null`, ChannelSwitcherBuilder.cs:383-391); all stamped refs are created in the same pass (no destroyed-object binding — `RestructureNav` destroys only Screen_Telegram/TelegramTab, disjoint from the pill subtree); the Telegram-tab guard (lines 256-289) correctly skips deletion on re-run (tabs[1] is now «Сводка»/Screen_Dashboard); the managed-element delete-twice quirk is defensively handled; MenuItem path marks the scene dirty, headless path saves explicitly, and a mid-build exception aborts before `SaveScene` (safe failure).
- **Tab-index integrity**: committed scene tabs = Чаты/Сводка/Bots/Profile with `defaultTabIndex: 0` (Main.unity:135725-135761); `BotsTabIndex = 2` and `WhatsAppTabIndex = 0` match; all runtime callers use the consts (EmptyStateView.cs:171, Manager.cs:1626, BotsPage.cs:52-53, ProfilePage.cs:220, DashboardPage.cs:411); `TabIndexShiftTests` pins both consts and the TabRefreshGate rule. Zero `Screen_Telegram`/`TelegramTab` remnants in the scene.
- **Shell script exit codes**: sentinel-as-truth with exit-code corroboration is sound; empty/missing log → 2, sentinel+nonzero-exit → 1 (conservative), `grep -qF --` handles the bracketed sentinel safely.

## Warnings

### WR-01: NavRestructureBuilder re-run silently corrupts the committed 4-tab scene

**File:** `Assets/Editor/NavRestructureBuilder.cs:139-140`
**Issue:** `BuildInternal` still assumes the pre-06-02 5-tab layout: `newTab = tabsProp.GetArrayElementAtIndex(2)` and `botsTab = tabsProp.GetArrayElementAtIndex(3)`. Against the committed 4-tab scene, tabs[2] is **Bots** and tabs[3] is **Profile**. Every guard passes anyway (`arraySize < 4` → 4 tabs; `tabs[3].screenPanel` → Screen_Profile, non-null), so a re-run proceeds and: (1) `BuildDashboard` (line ~176 via `DestroyAllByName(container, "Screen_Dashboard")`) destroys the fully-built dashboard, leaving tabs[1].screenPanel dangling — the real «Сводка» tab goes dead; (2) `RewriteNewTabToDashboard` (line 384) renames the **Bots** tab to «Сводка» and points it at the bare placeholder — Bots becomes unreachable and the bar shows two «Сводка» tabs. `BuildHeadless` (line 79-85) then auto-saves the corrupted scene with a success log. This phase edited the file (ReorderScreens prune, line ~422) making re-run look supported while leaving the destructive path in place — the exact hazard flagged by 06-01.
**Fix:** Add an identity guard before any mutation, mirroring ChannelSwitcherBuilder's guarded tab delete — fail loudly when the scene is already restructured:
```csharp
// BuildInternal, replacing lines 139-140's blind indexing:
var newTab = tabsProp.GetArrayElementAtIndex(2);
string tab2Name = newTab.FindPropertyRelative("tabName").stringValue;
var tab2Screen = newTab.FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
bool isPreRestructure = tab2Name == "New" || (tab2Screen != null && tab2Screen.name == "Screen_New");
if (!isPreRestructure)
    throw new System.InvalidOperationException(
        "[NavRestructureBuilder] tabs[2] is not the 'New' tab — the nav was already restructured (06-02). " +
        "Re-running would clobber the Bots tab. Nothing to do.");
```
(Alternatively resolve the target tab by name instead of index.)

### WR-02: AssignDashboardIcons re-run stamps dashboard icons onto the Bots tab

**File:** `Assets/Editor/NavRestructureBuilder.cs:111`
**Issue:** `var dashboardTab = tabsProp.GetArrayElementAtIndex(2);` — in the committed 4-tab scene, «Сводка» moved to index **1** and index 2 is **Bots**. Re-running `Tools/Nav Restructure/Assign Dashboard Icons` (e.g., after regenerating the glyphs, a legitimate use) overwrites the Bots tab's icons with the line-chart sprites and marks the scene dirty. The `arraySize < 3` guard (line 108) passes. Unlike WR-01 this entry point has a real future use, so a hard "already restructured" bail is wrong — it must find the right slot.
**Fix:** Resolve the tab by identity, not index:
```csharp
SerializedProperty dashboardTab = null;
for (int i = 0; i < tabsProp.arraySize; i++)
{
    var t = tabsProp.GetArrayElementAtIndex(i);
    var panel = t.FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
    if (t.FindPropertyRelative("tabName").stringValue == "Сводка"
        || (panel != null && panel.name == "Screen_Dashboard"))
    { dashboardTab = t; break; }
}
if (dashboardTab == null)
    throw new System.InvalidOperationException("[NavRestructureBuilder] «Сводка» tab not found in BottomTabManager.tabs.");
```

### WR-03: run-editor-builder.sh — method override always reports NOT GREEN after the scene was already mutated and saved

**File:** `Tools/run-editor-builder.sh:41,48`
**Issue:** The usage header (line 20) advertises `Tools/run-editor-builder.sh SomeOther.EntryMethod` to override the `-executeMethod` target, but `SENTINEL` (line 41) is hardcoded to ChannelSwitcherBuilder's log line. Running any other builder (e.g., `NavRestructureBuilder.BuildHeadless`) mutates **and saves** Main.unity, then the verdict grep never matches → exit 1 "NOT GREEN". An operator or agent trusting the exit code concludes the run failed and the scene is untouched — the opposite of reality. Combined with WR-01, `Tools/run-editor-builder.sh NavRestructureBuilder.BuildHeadless` corrupts the scene, saves it, and reports failure.
**Fix:** Derive the sentinel from the method's class segment (both existing headless builders log the same shape, `[<Class>] Headless build + save complete`), and keep an explicit override for irregular builders:
```bash
METHOD="${1:-ChannelSwitcherBuilder.BuildHeadless}"
# $2 (optional) = explicit sentinel override for builders with a different log shape
SENTINEL="${2:-[${METHOD%%.*}] Headless build + save complete}"
```

## Info

### IN-01: DashboardPageBuilder resolves the container via a stale tab index that only works by coincidence

**File:** `Assets/Editor/DashboardPageBuilder.cs:136-140`
**Issue:** `tabsProp.GetArrayElementAtIndex(3).FindPropertyRelative("screenPanel")` is named `screenBots` and the error message says "tabs[3].screenPanel (Screen_Bots) is unassigned" — but post-restructure tabs[3] is **Profile**. The build still works because the value is only used to reach the shared parent container (`screenBots.transform.parent`), which is identical for every screen. A future reader (or a failure) gets a misleading picture. 06-01 flagged this line; unlike NavRestructureBuilder, re-running DashboardPageBuilder remains safe.
**Fix:** Resolve the container index-agnostically and rename the variable, e.g. take `tabsProp.GetArrayElementAtIndex(0)`'s screenPanel as `anyTabScreen`, or find `Screen_Dashboard` by name directly and use its parent.

### IN-02: BottomTabManager field-initializer default selects Profile on a fresh component; stale comments

**File:** `Assets/Scripts/Main/BottomTabManager.cs:100` (also line 3)
**Issue:** `[SerializeField] private int defaultTabIndex = 3; // 'Chats' matches WhatsApp default` — the comment is wrong and the value is stale: in the current 4-tab order index 3 is **Profile**. The committed scene serializes `defaultTabIndex: 0` (verified), so this is inert today, but any fresh add of the component (e.g., a future nav-bar rebuild) would boot on Profile with a comment claiming it's Chats. The class header (line 3) also still says "5 tabs". Pre-existing lines, but directly adjacent to this phase's `BotsTabIndex` edit and the same hazard class.
**Fix:**
```csharp
[SerializeField] private int defaultTabIndex = WhatsAppTabIndex; // Chats (index 0) is the launch tab
```

### IN-03: Model tests miss the Telegram mirror of the selected+muted edge

**File:** `Assets/Tests/Editor/Chat/ChannelSwitcherModelTests.cs:58-84`
**Issue:** The matrix covers 5 of 8 (active × connectivity) rows. The untested mirror — `active=Telegram, tg=false` (TG chip selected+muted) — is reachable in production: `ChannelResolver.Resolve` keeps a persisted Telegram choice when **neither** channel is connected (ChatManager.Channel.cs:135), e.g. a bot whose Telegram was connected, chosen, then logged out. Tests D/E only exercise the WA-chip side of the `chip == ChatChannel.Telegram ? tgConnected : waConnected` ternary under selection.
**Fix:** Add one mirror test: `active=Telegram, wa=false, tg=false` → TG chip `{selected=true, muted=true}`, WA chip `{selected=false, muted=true}`.

### IN-04: Selected+muted chip keeps its brand fill at full alpha — "muted" reads only in the label

**File:** `Assets/Scripts/UI/ChannelSwitcherView.cs:147-158` (doc at 10-11)
**Issue:** `ApplyChip` sets the fill alpha purely from `Selected` (1 or 0); `Muted` fades only label (×0.40) and icon. In the reachable "active channel disconnected" state the chip shows a fully saturated brand fill with dim text — the class doc's claim that the segment "renders MUTED (~40% alpha)" only half-holds, and a saturated fill may not read as disconnected at a glance. The ApplyChip comment shows this is deliberate, so this is a design-judgment note, not a bug.
**Fix:** Either fade the selected fill when muted (`f.a = state.Selected ? (state.Muted ? MutedAlpha : 1f) : 0f;`) or tighten the class doc to say only the label/icon fade.

### IN-05: Lock guard can false-positively match a sibling project path

**File:** `Tools/run-editor-builder.sh:66-68`
**Issue:** `grep -iF -- "-projectpath ${PROJECT}"` is an unanchored substring match — an Editor open on e.g. `/Users/ayan/Projects/Automation2` also matches and blocks a headless run for this project. Fails safe (refuses instead of colliding), so low priority.
**Fix:** Anchor the path end, e.g. `grep -iE -- "-projectpath ${PROJECT_RE}( |$)"` with a regex-escaped `PROJECT_RE`, or post-filter matches by exact argument comparison.

---

_Reviewed: 2026-07-13T11:16:06Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
