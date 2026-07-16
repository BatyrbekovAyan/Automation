---
gsd_state_version: 1.0
milestone: v1.1
milestone_name: Telegram Parity
status: executing
stopped_at: Completed 08-05-PLAN.md (D7 TG service-dialog dedup + bleed; 1091/1091 green FRESH)
last_updated: "2026-07-16T12:37:07.859Z"
last_activity: 2026-07-16
progress:
  total_phases: 8
  completed_phases: 5
  total_plans: 29
  completed_plans: 27
  percent: 93
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-07-12)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 08 — device-uat-milestone-closeout

## Current Position

Phase: 08 (device-uat-milestone-closeout) — EXECUTING
Plan: 08-05 done (8/10 phase-08 plans complete; 08-09/10 remain)
Status: Executing (gap-closure) — milestone NOT complete
Last activity: 2026-07-16

Progress: [█████████░] 93%

## Performance Metrics

**Velocity:**

- Total plans completed (v1.0): 8
- Average duration: ~11min/plan (v1.0 phase 2 sample)

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work (v1.1 design, spec §2):

- [D1]: In-screen channel switcher (TopBar CenterZone segmented pill); Telegram bottom tab + Screen_Telegram placeholder removed; per-bot channel persistence `{botId}ActiveChatChannel` → Phase 6.
- [D2]: Dashboard Telegram inclusion sequenced last (server zero changes; chips + deep-link need the channel concept) → Phase 7; explicit cut line if scope shrinks.
- [D3]: Suggestions = additive v1.1 contract (`channel` + `botTgId`, `botWaId` kept) + channel-branched RAG (single-key invariant) → Phase 7 (client) / Phase 4 (workflow branch).
- [D4]: `ChatChannel` enum + `ActiveChannel`; `WappiEndpoints.Sync(channel, path)` replaces 11 URL literals; Telegram cache under `BotCache/{botId}/telegram/` (no WA migration); `OutboxEntry` gains channel → Phase 5.
- [D5]: Confirmed tapi divergences (type:"text", numeric ids, last_time/last_timestamp swap, no isGroup, reply endpoint, reaction recipient, no chat/delete, native avatars, 2FA branch) → Phases 3 (verify) + 5 (implement).
- [D6]: Live shape capture is a USER-ASSISTED gate (`secrets.json` deny-ruled) — `Tools/tapi/capture-shapes.sh`; 13 open items in tapi-shapes.md §11 → Phase 3.
- [D7]: Telegram_Bot template fixes + RAG re-stamp in BOTH Create orchestrators; WhatsApp template untouched → Phase 4.
- Phase 3 shape-capture tooling shipped: read-only Tools/tapi/capture-shapes.sh + pre-filled 13-question SHAPES.md; Q9-Q13 verdicts DEFERRED (not observable read-only). Owner-run capture is the phase-closing human gate (03-HUMAN-UAT.md), blocking Phase 5 CHAT-03/CHAT-07 media/Normalize.
- [Phase 4]: Telegram_Bot template moved onto tapi (send/mark-read/typing + text routing + length_seconds voice fallback + chatId sessionKey); node order preserved
- [Phase 4]: RAG re-stamp added to both Create orchestrators (parameterized UPDATE, cred vvRrFiEXzLVqKjOx) preserving the { id } response; Suggest_Replies given additive channel-branched RAG (botWaId | botTgId), verifier verify-telegram-parity.py green
- [Phase 4]: Unity create-workflow forms now send the opposite channel's workflow id (sentinel-guarded '-1') — enables the 04-01 RAG re-stamp on late channel auth (TPL-05 client half)
- [Phase 4]: 04-HUMAN-UAT.md is the TPL-06 owner gate (dev n8n + tunnel + import-by-literal-id + Postgres cred pre-flight + text/voice/memory/pre-auth re-stamp e2e); closes the phase
- [Phase 5]: Telegram 2FA fix (TGAUTH-01) — pure TelegramAuthResponseParser classifier + detail:2fa cloud-password branch in both code and QR flows posting tapi/sync/auth/2fa {pwd_code}; password never logged/persisted, cleared after submit; no new scene objects; 839/839 EditMode green
- [Phase 5]: 05-02 ChatManager identity seam — ActiveChannel persisted per bot ({botId}ActiveChatChannel), SetActiveChannel reuses SetActiveBot reset choreography + OnActiveChannelChanged; channel-aware GetActiveProfileId/GetCacheRoot (BotCache/{botId}/telegram/ isolation, CHAT-11)/empty-state/sync-gate; ResolveChannelForBot auto-selects connected channel on switch/startup; BotHasNoTelegram empty state; WhatsApp byte-identical, 854/854 EditMode green
- [Phase 5]: 05-03 read pipeline + tapi parser divergences — 8 non-send chat URLs via WappiEndpoints.Sync(ActiveChannel); ActiveChannelSupportsChatDelete no-ops DeleteChat on Telegram; ParseMessageType 'text'=>Chat + last_timestamp->last_time fallback + DisplayFallback retires chat.id[..^5] + ChatIdFormat.IsGroup groupness; ChatViewModel.IsGroup at construction; pending/undelivered/error ticks; pure MessageTypeParser/ChatDialogTime seams; WhatsApp byte-identical, 878/878 EditMode green
- [Phase 5]: 05-04 send-path channel branches — Telegram quoted reply => tapi message/reply {body, message_id} (no recipient); reaction body gains required recipient (NullValueHandling.Ignore, WA byte-identical); mark-read drops mark_all on Telegram; media EndpointFor 3-arg via (ChatChannel)entry.channel; text+media outbox snapshot channel, text retry rebuilds URL from entry.channel; last api/sync literals in ChatManager.cs retired; WhatsApp byte-identical, 888/888 EditMode green
- [Phase 6]: 06-01 channel-switcher runtime — pure ChannelSwitcherModel (selected=equality, muted=own-channel connectivity, both can hold) + event-driven ChannelSwitcherView binder (reads ChatManager.ActiveChannel read-only so SWITCH-03 persistence flows through; muted chips stay tappable for SWITCH-02's connect empty state; every ref null-guarded; field names are the 06-02 builder's SerializedObject contract); BottomTabManager.BotsTabIndex 3→2 locked by TabIndexShiftTests (all SwitchTab consumers already constant-based, no literals); no scene mutation; SWITCH-01/04 land in 06-02; 900/900 EditMode green
- [Phase 6]: 06-02 channel switcher scene half — headless ChannelSwitcherBuilder builds the WhatsApp|Telegram segmented pill into TopBar CenterZone (two independent brand fills per 06-01 binder contract, text-only chips mirroring ModeToggle) + stamps all 6 ChannelSwitcherView refs via SerializedObject; guarded nav restructure removes Telegram tab (tabs 5→4) + Screen_Telegram + TelegramTab, relabels tab 0 «Чаты»; run-editor-builder.sh (Editor-closed, sentinel verdict); scene committed immediately 8f1d25f; 900/900 EditMode green; SWITCH-01/04 marked; owner UAT gate open
- [Phase 7]: 07-01 channel-aware «Вместе» payload — additive v1.1 wire (botTgId + channel appended after messages, v1 keys byte-identical; server Prep defaults absent channel=>whatsapp, Phase 4). BuildPayloadJson pure + channel-RESOLVED (profileId TG=>telegramProfileId, botWaId=whatsappWorkflowId ALWAYS for the default WA RAG branch, channel lowercase enum-derived ONLY per T-07-01-01); Run() reads ChatManager.ActiveChannel; 7 channel-matrix + additive-identity tests, 908/908 EditMode green; SUGG-01/02 client half, live TG grounding rides TPL-06 (07-HUMAN-UAT.md)
- [Phase 7]: 07-02 dashboard «Сводка» on Telegram — pure DashboardProfileMap seam collects BOTH channels' authed ids + maps id→(botName,channel) (channel from the matched LOCAL entry, never the server payload; T-07-02-01); DashboardMetrics.FilterByProfiles(ISet) set filter, FilterByProfile delegates; DashboardPage POSTs TG ids (DASH-01), one chip per bot with a HashSet filter so a dual-channel bot is a single chip (DASH-02), and channel-aware OpenChat SetActiveBot→SetActiveChannel→SwitchTab(«Чаты»)→deferred SelectChat (DASH-03); WhatsApp byte-identical, server contract + Main.unity untouched; 916/916 EditMode green
- [Phase 5]: 05-06 capture-gated media/reactions/reply — tapi media is download-only (body:null+s3Info:{} → existing serial media/download-by-id; metadata from media_info + flat name/mime; video-as-document→Video via mimetype); receive-side reactions BUILT (Q3 GO, v2 TG-REACT-RECV superseded) as a Normalize-time reactions[] map + reconcile merge preserving optimistic 'me'; ChatIdFormat classifies 'channel' dialogs group-ish (Q4); reply Q8 no-echo verified; name/isDeleted verdict-resolved (no change); WhatsApp byte-identical, 957/957 EditMode green
- [Phase 5]: 05-07 Telegram media presentation gap-closure — .tgs (application/x-tgsticker) refines to Sticker and renders a deliberate borderless placeholder + «Стикер» with NO download (gzipped Lottie undecodable; native animation = v2); video note detected by pure heuristic (square + video.mp4 + ≤60s — is_round deliberately ignored, unreliable per SHAPES.md Q2) and rendered as a circle (half-side RoundedCorners radius) + duration badge; GIF (isGif through all 4 layers) keeps the video pipeline + 'GIF' corner badge; flags minted ONLY inside ApplyTelegramMediaShape so WhatsApp is byte-identical; 988/988 EditMode green (966+22)
- [Phase 5]: 05-09 tapi status parse hardening (off-plan, from owner Editor screenshots after 05-08) — new pure WappiStatusParser (Newtonsoft JObject, throw-safe): TryGetAuthorized / TryGetPhone (top-level `phone` wins over `account.phone`; the pretty tapi body carries TWO phone keys, which the old substring scan + no-whitespace `","platform":` guard mis-read into a stored JSON blob) / IsPlausiblePhone. Wired into the two named Telegram status sites (Manager.GetTelegramProfileStatus + BotSettings.CheckTelegramAuthorization); the stale `{bot}TelegramNumber` blob self-heals to "" via IsPlausiblePhone at the two field-load sites + the EnableSave dirty-check (no re-auth). Rule 1 deviations: a THIRD Telegram status site (CheckTelegramUnauthorizationOutsideApp) THREW on the pretty body (negative Substring) → same throw-safe parser; and the dirty-check was sanitized too so the load-sanitize doesn't spuriously light Save. WhatsApp api/sync parses byte-identical (safe future adopter). Fix B: channel-switcher chip labels padded (ChannelSwitcherBuilder LabelSize 28→22 + 12px label inset), Main.unity rebuilt headlessly + grep-verified (2× fontSize-22 labels, 2× -24 insets; nav a guarded no-op). 21 new parser tests, suite 1028/1028 EditMode green. commits 584be1d + d68534f + e4f6451. Pixel-perfect look + correct number = Phase 8 device re-verify (05-HUMAN-UAT gaps 4+5)
- [Phase 5]: 05-10 channel-aware accent theming (off-plan, owner request — Phase-8-era polish) — new pure `ChannelAccent.Resolve(channel, whatsappAuthored)` seam: Telegram ⇒ brand blue #2AABEE (authored alpha preserved, matches ChannelSwitcherView.TgSelectedFill), any other channel ⇒ authored color BYTE-IDENTICAL. Wired at two accent surfaces, each caching its authored green at runtime (never hardcodes a scene green; pooled rows revert exactly on WA rebind): ChatItemView.ApplyUnreadBadge (unread pill fill + unread time tint) and EmptyStateView.ConfigureForReason (connect/create CTA fill + placeholder icon, all three reasons). Scope = accents only (owner-confirmed): bubbles, Авто/Вместе toggle, and channel/bot switcher chips deliberately untouched. Null-safe throughout (missing Image / null ChatManager ⇒ no-op). 7 new ChannelAccentTests; WhatsApp byte-identical, suite 1036/1036 EditMode green via in-Editor bridge (real Editor was open — lockfile untouched). commit 861ddfe. Pixel-perfect blue = Phase 8 device re-verify (05-HUMAN-UAT §7)
- [Phase 5]: 05-11 scroll-to-unread FAB badge theming (off-plan, follow-on to 05-10) — the open-chat FAB's unread-count badge pill (same #26B25A UnreadGreen as the chat-list unread pill) was the last green accent 05-10 left on the messages view; now recolored via the SAME `ChannelAccent.Resolve` seam. New `ScrollToBottomFab.ApplyChannelAccent(ChatChannel)` caches the authored green once at Awake and re-applies EVERY call (the FAB is a persistent widget — never re-instantiated — so "leave it" would stick blue after a switch); `MessageListView.ApplyFabChannelAccent()` reads ChatManager.ActiveChannel (null-guarded) and repaints at OnChatSelected (a channel switch closes+reopens the chat → reopen repaints). SCENE AUDIT (UnreadMarkersBuilder + Main.unity) resolved the fix brief's incorrect premise ("green is on button.image"): the FAB body is a WHITE circle, the button image is a TRANSPARENT hit area — the badge pill is the ONLY green, so badge-only recolor per the "genuinely-green accents only / WA byte-identical" constraint; badge text stays white. No new test (MonoBehaviour glue through the already-tested seam); WhatsApp byte-identical, suite 1036/1036 EditMode green via in-Editor bridge (real Editor PID 22038 was open — headless refused correctly, lockfile untouched; editorAssemblyWrittenUtc 14:22:48Z postdates the edit). commit 7affb7b. Pixel-perfect blue = Phase 8 device re-verify (05-HUMAN-UAT §8)
- [Phase 5]: 05-12 Telegram empty-state hero refinement (off-plan, owner refinement to 05-10) — on the Telegram channel the hero ICON now shows the `Telegram_2019_Logo` UNTINTED (`iconImage.sprite`=telegramIcon, `color`=Color.white → natural logo colors), REVERTING 05-10's blue icon TINT; the pale-mint parent disc (IconCircle) recolors green→blue via the same `ChannelAccent.Resolve`. 05-10 CTA blue kept as-is. `EmptyStateView.ApplyChannelAccent` branches on ActiveChannel: TG swaps sprite+white+blue disc; WA/default restores authored sprite+color+disc BYTE-IDENTICAL (persistent widget; authored state cached at Awake; disc code-resolved via icon's nearest-ancestor Image, bounded by the view root so the white bg is never recolored). New `telegramIcon` serialized field stamped into Main.unity by new headless `EmptyStateTelegramIconBuilder` ([MenuItem] + StampHeadless, ChannelSwitcherBuilder idiom). GOTCHA/Rule-1 heal: the required `spriteMode 2→1` (Single) removed the old Multiple sub-sprite fileID that TWO pre-existing Images used — TelegramAuth `Logo` + Add-Bot form Telegram `Icon`; both migrated to the canonical Single sprite 21300000 (visually identical). All 3 in-scene logo refs converge on 21300000; scene object count preserved (4918). Blue-on-blue owner-ACCEPTED (revisit w/ white paper-plane if it reads poorly on device). Env: Editor PID 22038 open the whole time (coordinator's "closed" report was stale) — code+builder committed autonomously, scene stamp via owner-run menu checkpoint, then owner quit (Don't Save, heal preserved) → suite 1036/1036 EditMode green HEADLESS (no new tests; MonoBehaviour glue). commits 3206498/979f478/1e20dbb/c61a04b. Pixel-perfect look = Phase 8 device re-verify (05-HUMAN-UAT §9)
- [Phase 5]: 05-08 media device-UAT round-2 polish (off-plan, from an owner device screenshot after 05-07) — (1) video note now floats BUBBLE-FREE: the transparency decision extracted to a pure seam BubbleTransparencyPolicy.IsTransparent(isSticker,isVideoNote,isPlaceholderActive,hideBubble) + isVideoNote, so the circle floats chrome-free like native TG (time stays readable via the existing Video+no-caption white-text/timeBackground media overlay; !isPlaceholderActive keeps an UNAVAILABLE note on a visible retry card); (2) .tgs sticker now renders a deliberate sticker-slot-sized (396²) neutral rounded CARD with its OWN fill + centered «Стикер» + mid-gray glyph (the 05-07 white-silhouette placeholder was invisible on the transparent bubble → collapsed to a tiny pill on device). Telegram-only (isVideoNote default-false; card gated on TgsStickerMime), WhatsApp byte-identical; verified via the sanctioned in-Editor bridge (owner's Editor was open, headless refuses on a held lock; gated on editorAssemblyWrittenUtc 17:06:24Z), 1007/1007 EditMode green (997+10). commits 72a5909 + a27cf16. Pixel-perfect look = Phase 8 device re-verify
- [Phase 8]: 08-04 D5 gap-closure — open-chat live poll. Root cause: SyncLatestMessages (the only OnLiveMessagesReceived/brandNew site, ChatManager.cs:789) is started once per open (OpenChatRoutine:943); no timer re-fetched the open chat, so incoming never rendered until re-entry and the «Вместе» payload (TryGetRecentMessages over _activeChatCache) stayed stale (H2). Fix: pure OpenChatLivePollGate (3s IntervalSeconds) + a single always-running self-gating OpenChatLivePollRoutine (ChatManager.LivePoll.cs) that REUSES SyncLatestMessages (no new messages/get caller — inherits currentChatId re-check, CrossChatResponseGuard, _chatFetchesInFlight serial gate). Foreground-gated (OnApplicationFocus/Pause); chatIsOpen also gated on MessageListPanel.activeSelf (currentChatId sticky after ShowChatList); re-kicked after StopAllCoroutines in SetActiveBot/SetActiveChannel (never stranded). Cross-channel (no ChatChannel branch). Cascades to bubbles + card refresh + fresh payload with zero wiring change. 1043/1043 EditMode green FRESH. Device re-verify (I.1 #3, I.2 #6, H2) rides 08-10. Push-based delivery stays v2.
- [Phase 8]: 08-06 D1+D2 gap-closure — Telegram reaction set + removal tombstone. D1: pure TelegramReactionCatalog (standard free set + VS16-normalizing IsAllowed + TG-safe quick 6 [😂→😁, 😮→🔥] + FilterCategories); quick-bar reads ActiveQuickEmojis at click/render time, picker rebuilds per-channel (_builtForChannel) from FilterCategories on Telegram; PostReactionRoutine reverts BOTH pill and chat-list preview on a 400 (no stuck 'You reacted…'). D2: confirmed resurrection path (b) — a bare removal RemoveAt-deletes the 'me' entry so Merge sees no mine and returns the still-echoing server list verbatim; fix = empty-emoji 'me' tombstone (StampRemovalTombstone on Telegram toggle-off, persisted) + a removal branch in TelegramReactionMerge.Merge that suppresses the stale 'me' echo within the 90s grace. Rule-2: ReactionSummary counts only visible emoji + MessageItemView clearance follows visible emoji (empty-emoji tombstone never counts/reserves). WhatsApp byte-identical; 1063/1063 EditMode green FRESH (1043+20). Allowed set is a starting point — re-confirm at 08-10. Device re-verify B9/B13 rides 08-10.
- [Phase 8]: 08-07 D3 gap-closure — Telegram video-note (кружок) presentation. Diagnosis-first, root cause = (ii): the incoming note IS flagged isVideoNote (the round crop the owner sees proves it), but unlike the outgoing note 05-08 verified (inline s3Info.url → media present at bind → floats), an INCOMING note is placeholder-first (tapi body:null + s3Info:{} → download-by-id), so isPlaceholderActive stays true UNDER the visible circle and the square bubble stays opaque = the 'white background bubble'. Fix D3b at the transparency CALL SITE (not the heuristic, not ChatManager): UpdateBubbleVisuals computes note-scoped effectivePlaceholderActive = isPlaceholderActive && !(isVideoNote && messageImage.activeInHierarchy), so once the round media surface is the visible content the circle floats — card-only states (messageImage hidden) keep the opaque retry card. D3a: ToggleDurationBadgeOverlay + new RefreshBadgeCornersDeferred refresh the badge pill's ImageWithRoundedCorners after layout (radius assigned post-AddComponent never re-bakes on a fixed-size point anchor → sharp ends); immediate RefreshCorners + one-frame-deferred pooled-bubble-guarded pass, radius/layout unchanged. Pure BubbleTransparencyPolicy/TelegramVideoNoteHeuristic seams untouched (is_round still ignored); WhatsApp byte-identical; 1063/1063 EditMode green FRESH. commits 161b540 (D3b) + 89479db (D3a). Device re-verify E1 float + B5 badge corners rides 08-10.
- [Phase 8]: 08-08 D4+D6 gap-closure — chat-row swipe stack. D6: SwipeToDelete lifecycle null-guard — lazy Rt accessor (_rt != null ? _rt : (_rt=(RectTransform)transform)) backs every _rt deref + _scroll ??= lazy at drag entry points, so ChatItemView.Bind→ResetClosed→SetContentX no longer NREs when a fresh-sync row is Instantiated+Bound before Awake (inactive list panel); Awake still assigns on the normal path, channel-agnostic (fired on WhatsApp too). D4: new pure ChatRowSwipePolicy.Enabled(channel) (WhatsApp-only; mirrors ActiveChannelSupportsChatDelete) — ChatItemView.Bind gates the affordance on ActiveChannel per (pooled) bind: Telegram snaps shut + disables SwipeToDelete (no drag callbacks) + hides the red button; WhatsApp keeps ResetClosed()+wired button byte-identical (added enabled=true/SetActive(true) are idempotent no-ops). 2 ChatRowSwipePolicy tests; 1065/1065 EditMode green FRESH (1063+2); commits a12f467/fea922d/991bd2f. Device re-verify D4/D6 rides 08-10.
- [Phase 8]: 08-05 D7 gap-closure — TG service-dialog dedup + cross-channel bleed. Diagnosis: (a) two id-forms of the 777000 service dialog (read-only capture shows only bare 777000; the twin is server/device-side — no in-app @c.us appender — leading hypothesis 777000@c.us; EXACT form to confirm at 08-10); (b) bleed = a pre-CHAT-11 legacy WhatsApp cache file still holding the TG dialog (today's sync path is channel-correct: GetCacheRoot/cachePath follow ActiveChannel, StopAllCoroutines guards a switch). Fix: pure ChatIdFormat.CanonicalKey(id, channel) (WhatsApp VERBATIM/byte-identical; Telegram strips a spurious @c.us/@g.us twin) + IsForeignToChannel(id, channel) (WhatsApp drops a bare no-'@' Telegram-form id — '@'-test keeps every genuine WA jid incl broadcast/newsletter/lid; Telegram NEVER rejects so the service dialog stays). ParseChatsJson keys chatLookup/serverIds/isDeleted/merge by the canonical id AND constructs the surviving VM with it (vm.ChatId==key ⇒ bare tapi id on TG, byte-identical on WA, every downstream chatLookup consumer resolves unchanged); GetChat + both ShouldNotify + DisplayFallback + IsGroup route through the canonical key. Holds under 08-04's repeating open-chat poll. 1091/1091 EditMode green FRESH (1065+26). commits f379a5f/cc04503/1c9d8fe. Device re-verify D7 + EXACT twin-form capture ride 08-10.

### Pending Todos

None yet.

### Blockers/Concerns

- [RESOLVED-tentative 2026-07-16 — 05-06-REVIEW WR-02]: vthumb id-ambiguity probed in Gate A — owner: "seems ok, not really sure"; no crossing observed (low-confidence pass, no defect filed). Watch during normal use; if a crossing ever appears, check whether tapi accepts a `chat_id` param on `message/media/download`.
- [CODE-CLOSED 2026-07-16 — 08-06, device confirm rides 08-10]: **D1** (REACTION_INVALID) and **D2** (removal never clears) are fixed. D1: TG reaction bar+picker constrained to `TelegramReactionCatalog.AllowedSet` (standard free set; quick 6 swaps 😂→😁/😮→🔥) + a clean 400 revert of both the pill and the chat-list preview. D2: confirmed resurrection path (b) — a bare removal deletes the "me" entry so `Merge` returns the still-echoing server list; fixed with a fresh empty-emoji "me" tombstone (`StampRemovalTombstone`) + a removal branch in `TelegramReactionMerge.Merge`. WhatsApp byte-identical; 1063/1063 EditMode green FRESH. Remaining: IN-04 still accepted v1 (no "X reacted…" chat-list preview on TG receive-side); the allowed set is a starting point to re-confirm at 08-10 (B9 add / B13 remove).
- [Gate A result 2026-07-16]: device UAT RUN — Overall ISSUES; defects D1–D9 in 08-DEVICE-UAT.md §Defects (high: D5 incoming never renders in the open chat until re-enter — owner-confirmed BOTH channels, «Вместе»/H2 suggestions stale as downstream; D7 TG service-dialog duplicated — logo-avatar + silhouette rows — and visible in the WA list). All owner clarifications received (D5 both-channels, D6 SwipeToDelete.SetContentX stack via ChatItemView.Bind←ParseChatsJson, D7 identity, O1→D9 TG sync indicator). G6 reminder: deactivate the dev test clone. Gap planning started.
- [Gate/Phase 3]: tapi media message shapes (messages/get) undocumented — Normalize/media work (Phase 5 CHAT-03) blocked until the owner runs the capture script against an authorized dev Telegram profile.
- [Gate/Phase 4]: TPL-06 e2e needs dev n8n (localhost:5678) + tunnel + a real authorized Telegram profile (user-assisted).
- [Constraint]: Assume Wappi response-crossing bugs apply to tapi — keep serial media queue + `_chatFetchesInFlight` gate; reset on channel switch like bot switch.
- [Constraint]: Bot workflow clones stay INACTIVE except during active testing (real contacts!); prod bagkz stays dormant.
- [Risk]: Any existing dev Telegram workflow clones carry wrong api/sync URLs — recreate after template fix.

## Deferred Items

Items acknowledged and carried forward:

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| Feedback | FB-01 thumbs-up/down to improve ranking | Deferred to v2 | v1.0 Init |
| Insight | FB-02 per-chat/per-bot suggestion analytics | Deferred to v2 | v1.0 Init |
| uat_gap | Phase 01: 01-HUMAN-UAT.md — 4 pending device scenarios | partial → Phase 8 | v1.0 close 2026-07-11 |
| uat_gap | Phase 02: 02-HUMAN-UAT.md — 4 pending device scenarios | partial → Phase 8 | v1.0 close 2026-07-11 |
| verification_gap | Phase 01: 01-VERIFICATION.md awaits device confirmation | human_needed → Phase 8 | v1.0 close 2026-07-11 |
| Polish | POL-01 streaming/animated suggestion reveal | Deferred to v2 | v1.0 Init |
| Milestone | Prod bagkz replication (Suggest Replies + all Telegram fixes) | pending → Phase 8 checklist | v1.1 start 2026-07-12 |
| Milestone | Server-side «Вместе» suppression | pending (v2 SUPPRESS-01) | v1.0 close |
| Design | Push-based incoming delivery (n8n → device push with the incoming text) to replace client polling — owner preference | Deferred to v2 (needs FCM/APNs + device-token registry + n8n hook; D5 gap fixes the existing refresh path first) | Gate A 2026-07-16 |

Note: POL-02 "Telegram chat support for the panel" graduated to v1.1 scope (SUGG-01/02, Phase 7).
| Phase 03 P01 | 10 min | 2 tasks | 5 files |
| Phase 04 P01 | 15min | 3 tasks | 5 files |
| Phase 04 P02 | ~10min | 2 tasks | 2 files |
| Phase 05 P01 | 35min | 3 tasks | 12 files |
| Phase 05 P05 | 26min | 3 tasks | 4 files |
| Phase 05 P02 | 19min | 3 tasks | 6 files |
| Phase 05 P03 | 27min | 3 tasks | 13 files |
| Phase 05 P04 | 9min | 3 tasks | 7 files |
| Phase 06 P01 | 21min | 3 tasks | 5 files |
| Phase 06 P02 | 10min | 3 tasks | 5 files |
| Phase 07 P01 | 7min | 3 tasks | 4 files |
| Phase 07 P02 | 7min | 2 tasks | 5 files |
| Phase 05 P06 | 42min | 3 tasks | 13 files |
| Phase 05 P07 | 25min | 3 tasks | 12 files |
| Phase 05 P08 | ~24min | 2 fixes | 3 files |
| Phase 05 P09 | ~22min | 2 fixes | 6 files |
| Phase 08 P08-04 | ~22min | 2 tasks | 6 files |
| Phase 08 P08-06 | 21min | 2 tasks | 10 files |
| Phase 08 P08-07 | 20min | 2 tasks | 1 files |
| Phase 08 P08-08 | ~11min | 2 tasks | 4 files |
| Phase 08 P05 | 18min | 2 tasks | 4 files |

## Session Continuity

Last session: 2026-07-16T12:37:07.832Z
Stopped at: Completed 08-05-PLAN.md (D7 TG service-dialog dedup + bleed; 1091/1091 green FRESH)
Resume file: None

**Planned Phase:** 08 () — 0 plans — 2026-07-16T10:01:11.226Z
