---
phase: 05-channel-aware-chatmanager-core
plan: 12
subsystem: ui
tags: [telegram, channel, theming, accent-color, empty-state, sprite-import, regression-heal]

# Dependency graph
requires:
  - phase: 05-channel-aware-chatmanager-core
    provides: "ChannelAccent.Resolve(channel, whatsappAuthored) seam + EmptyStateView accent theming (05-10)"
  - phase: 05-channel-aware-chatmanager-core
    provides: "ChatManager.ActiveChannel (per-bot WhatsApp/Telegram identity, 05-02)"
provides:
  - "Telegram empty-state hero shows the Telegram logo UNTINTED (its own colors) on a Telegram-blue parent disc; reverts 05-10's blue icon TINT"
  - "Telegram_2019_Logo imported as a Single sprite (assignable); all 3 in-scene logo refs converge on the canonical Single sprite fileID 21300000"
  - "EmptyStateTelegramIconBuilder — headless [MenuItem] SerializedObject ref-stamper for EmptyStateView.telegramIcon"
affects: [device-uat, milestone-closeout, any future channel-branded empty-state art]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Channel-branded ART swap (not just tint): cache the authored sprite + color at Awake, swap to a serialized channel sprite on TG, restore authored byte-identical otherwise"
    - "Code-resolve a sibling/parent Image for recolor (iconImage nearest-ancestor Image), stopping before the view root — avoids new scene wiring while never touching the background"

key-files:
  created:
    - Assets/Editor/EmptyStateTelegramIconBuilder.cs
  modified:
    - Assets/Scripts/UI/EmptyStateView.cs
    - Assets/Images/Icons/Telegram_2019_Logo.svg.png.meta
    - Assets/Scenes/Main.unity

key-decisions:
  - "Owner refinement to 05-10: on Telegram the hero ICON is the Telegram logo UNTINTED (Color.white → natural colors), NOT the 05-10 blue tint; the parent disc (pale-mint IconCircle) goes green→blue via ChannelAccent.Resolve. The 05-10 CTA blue recolor is kept as-is."
  - "Telegram_2019_Logo import mode Multiple→Single (correct for a single-image logo); all three in-scene users migrated to the canonical Single sprite 21300000 (visually identical — the old Multiple mode was one full-texture sub-sprite)."
  - "Blue-on-blue is owner-ACCEPTED: the full-color Telegram logo (blue disc + white plane) on a blue circle. Revisit with a white paper-plane glyph only if it reads poorly on device (Phase 8)."

patterns-established:
  - "Serialized channel sprite stamped via a headless builder (Editor closed) or its [MenuItem] (Editor open); a runtime script can't resolve an asset sprite (no Resources.Load)."

requirements-completed: []

# Metrics
duration: ~40 min active (spanned 2 owner checkpoints)
completed: 2026-07-15
---

# Phase 5 Plan 12: Telegram Empty-State Hero Refinement Summary

**On the Telegram channel the empty-state hero now shows the Telegram logo UNTINTED on a Telegram-blue disc (reverting 05-10's blue icon tint); the sprite was moved to Single-mode import and all three in-scene logo references converge on the canonical Single sprite — WhatsApp byte-identical.**

## Performance

- **Duration:** ~40 min active (execution spanned two owner checkpoints — an in-Editor menu stamp, then a quit for headless tests)
- **Completed:** 2026-07-15
- **Tasks:** 4 atomic commits + 1 regression heal
- **Files:** 1 created (+.meta), 3 modified

## Accomplishments

- **EmptyStateView TG-channel icon swap.** `ApplyChannelAccent` now branches on `ChatManager.Instance.ActiveChannel`: on **Telegram** the placeholder icon becomes the `Telegram_2019_Logo` sprite at `Color.white` (untinted → the logo's own colors), and the pale-mint parent disc (`IconCircle`) recolors to Telegram blue via `ChannelAccent.Resolve`. On **WhatsApp/default** the authored sprite (null placeholder) + green tint + pale-mint disc are restored byte-identically (the empty state is a persistent widget reused across channel switches). The 05-10 connect/create **CTA** blue recolor is kept exactly as-is — the owner changed only the icon + its disc.
- **Authored state cached once at Awake** — icon color + sprite, and the disc's Image + color. The disc is code-resolved via the icon's nearest-ancestor Image, walking up but STOPPING before the view root, so the opaque white background can never be recolored. Every ref null-guarded (Editor/tests have no `ChatManager`).
- **`telegramIcon` serialized ref stamped into `Main.unity`** — via the new `EmptyStateTelegramIconBuilder` (headless `[MenuItem]` + `StampHeadless`, mirroring `ChannelSwitcherBuilder`'s SerializedObject idiom). Grep-verified on the `EmptyStateView` block (script guid `90d6c66e…`).
- **Sprite import corrected + regression healed** — `Telegram_2019_Logo.svg.png.meta` `spriteMode 2→1` (Single is correct for a single-image logo). This orphaned two pre-existing users of the old Multiple-mode sub-sprite fileID; both were migrated to the canonical Single sprite (see Deviations).
- **Suite green:** 1036/1036 EditMode (headless), exit 0, 0 failed/skipped/inconclusive — unchanged baseline (no new tests; MonoBehaviour glue through the already-tested `ChannelAccent` seam, per 05-08/11 precedent).

## Task Commits

1. **Sprite mode → Single (assignable)** — `3206498` (fix) — `Telegram_2019_Logo.svg.png.meta` `spriteMode 2→1`.
2. **Empty-state hero: untinted logo on a blue disc** — `979f478` (fix) — `EmptyStateView.cs` runtime theming.
3. **EmptyStateTelegramIconBuilder ref-stamper** — `1e20dbb` (fix) — new headless `[MenuItem]` builder (+.meta).
4. **Stamp `telegramIcon` + heal 2 logo refs to Single sprite** — `c61a04b` (fix) — `Main.unity` (owner-run menu stamp + the Logo/Icon fileID migration + benign Editor re-serialization churn).

**Plan metadata:** (this SUMMARY + `05-HUMAN-UAT.md` + `STATE.md`) — see final docs commit.

## Files Created/Modified

- `Assets/Scripts/UI/EmptyStateView.cs` — `telegramIcon` serialized field; cache authored icon sprite/color + resolve/cache the parent disc; `ApplyChannelAccent` branch (TG = untinted logo + blue disc, else authored restore); `ResolveIconCircle` ancestor walk bounded by the view root.
- `Assets/Editor/EmptyStateTelegramIconBuilder.cs` — headless ref-stamper (`Tools ▸ Empty State ▸ Stamp Telegram Icon` + `StampHeadless`).
- `Assets/Images/Icons/Telegram_2019_Logo.svg.png.meta` — `spriteMode 2 → 1` (Single; `textureType` stays 8/Sprite).
- `Assets/Scenes/Main.unity` — `EmptyStateView.telegramIcon` = sprite `21300000`; TelegramAuth `Logo` + Add-Bot form `Icon` sprite refs migrated `-3702629612883576132 → 21300000`.

## Decisions Made

- **Icon = untinted logo, disc = blue** (owner-refined from 05-10). 05-10 tinted the placeholder icon blue; the owner wanted the Telegram logo shown in its own colors, with the (green) parent circle turned blue instead. Implemented exactly: `iconImage.sprite = telegramIcon; iconImage.color = Color.white` + `iconCircle.color = ChannelAccent.Resolve(Telegram, authored)`.
- **Single-mode import + canonical sprite for all three users.** The logo is a single 1024² image; Single mode is the correct import. Rather than preserve the odd Multiple mode, all three references (the two pre-existing + the new `telegramIcon`) converge on the canonical Single sprite `21300000` — visually identical, and no weird negative sub-sprite fileIDs.
- **Blue-on-blue accepted.** The full-color tracked logo (blue disc + white plane) on a blue circle merges the discs, leaving a white plane on blue. Owner accepted this over substituting a different asset; documented as a device-reverify with a white-paper-plane fallback.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Healed two pre-existing Telegram-logo Images orphaned by the sprite-mode change**
- **Found during:** Scene ref-stamp verification (post-checkpoint).
- **Issue:** The plan's `spriteMode 2→1` change replaced the old Multiple-mode sub-sprite (fileID `-3702629612883576132`) with the canonical Single sprite (`21300000`). Two pre-existing Images still referenced the removed sub-sprite fileID and would render broken: **`Logo`** on the TelegramAuth screen (`TelegramAuth/…/QRPanel/…/Logo`) and **`Icon`** in the Add-Bot form's platform row (`Screen_New/…/PlatformRow/TelegramGroup/Icon`).
- **Fix:** Migrated both `m_Sprite` refs `-3702629612883576132 → 21300000` (the same canonical Single sprite `telegramIcon` uses). Visually identical — the old Multiple mode was a single sub-sprite covering the full 1024² texture, so the Single sprite renders the same. A builder can't heal a dangling ref (it reads as null at runtime), and the Editor was closed for the heal, so a precise text-level fileID migration was the correct tool.
- **Files modified:** `Assets/Scenes/Main.unity`.
- **Verification:** Grep — 3 guid refs, all `fileID: 21300000`, zero `-3702629612883576132` remaining; scene object count preserved (4918=4918, net +1 line = the `telegramIcon` field); suite 1036/1036 green.
- **Committed in:** `c61a04b`.

---

**Total deviations:** 1 auto-fixed (1 bug — a regression the plan's own sprite-mode change introduced, in-scope per the deviation scope boundary).
**Impact on plan:** Necessary for correctness (two existing screens would otherwise show a broken logo). No scope creep — the fix is a visually-identical fileID migration of the same asset the plan already touched.

## Issues Encountered

- **Environment differed from the plan brief AND the coordinator's mid-task report.** The brief said "NO real Unity process now (verified)"; in fact the Editor (PID 22038 — the same idle session from 05-11) was open the entire time. Per the plan's own contingency and 05-08/09/10/11 precedent, I did all safe file work autonomously (code, `.meta`, builder) and committed, confirmed the headless builder correctly refuses (lock held), and reached a **checkpoint** for the Editor-dependent scene stamp. The owner ran `Tools ▸ Empty State ▸ Stamp Telegram Icon` + saved.
- **Coordinator's "Editor is now CLOSED" report was stale.** On resuming, `ps` showed PID 22038 still live (`STAT Ss`, 2h19m, 3 AssetImportWorkers). Running headless would have refused; I did **not** delete the live lock or kill the process. Because I had hand-edited `Main.unity` (the logo-ref heal) believing the Editor was closed, the Editor's in-memory scene was stale versus my committed version, so I returned a second checkpoint asking the owner to **quit (Don't Save)** — which both eliminated the clobber risk and unblocked headless. The coordinator then verified `Main.unity` clean == `c61a04b` (heal preserved) and no Unity process. Headless suite ran green.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Code-correct + suite-green + WhatsApp byte-identical. **Pixel-perfect look is an owner device re-verify (Phase 8)** — a device-reverify line was appended to `05-HUMAN-UAT.md` (§9: Telegram empty-state hero → untinted logo on a blue circle; blue-on-blue caveat + white-paper-plane fallback).
- Scene edit committed immediately (`c61a04b`); the `.meta` retains vestigial Multiple-sprite tables that Unity self-heals on a future full reimport (harmless — the canonical `21300000` resolves regardless).

## Self-Check: PASSED

- Created file exists on disk: `Assets/Editor/EmptyStateTelegramIconBuilder.cs` (+.meta) — FOUND.
- Modified files present: `EmptyStateView.cs`, `Telegram_2019_Logo.svg.png.meta`, `Main.unity` — all FOUND.
- Commits `3206498`, `979f478`, `1e20dbb`, `c61a04b` — all FOUND in git log.
- Scene grep: `telegramIcon: {fileID: 21300000, …}` on the `EmptyStateView` block; all 3 logo refs = `21300000`; 0 dead refs.
- Suite `1036/1036` green via `run-tests-headless.sh` (Editor closed), exit 0.

---
*Phase: 05-channel-aware-chatmanager-core*
*Completed: 2026-07-15*
