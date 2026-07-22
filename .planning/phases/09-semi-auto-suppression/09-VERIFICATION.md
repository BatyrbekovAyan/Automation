---
phase: 09-semi-auto-suppression
verified: 2026-07-22T16:18:18Z
status: passed
score: 5/5 must-have truth groups verified (15/15 individual plan must_haves truths across 5 plans)
overrides_applied: 0
---

# Phase 9: Semi-Auto Suppression Flag Verification Report

**Phase Goal:** When a chat is in «Вместе» (semi-auto), the bot's autonomous n8n reply workflow stands down for that chat — no auto-reply, message stays unread, suggestions panel still works — identically on WhatsApp and Telegram. The «Бот работает/пауза» activation switch is untouched.

**Verified:** 2026-07-22T16:18:18Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth (ROADMAP SC) | Status | Evidence |
|---|---|---|---|
| 1 | A chat flipped to «Вместе» gets NO auto-reply and stays unread while suggestions still populate; flipping back to «Авто» restores auto-replies — both channels | ✓ VERIFIED | 09-HUMAN-UAT.md scenarios 1/2 (WhatsApp) + 4 (Telegram) all recorded PASS on-device 2026-07-22; runData (09-04) shows suppressed chat dead-ends before `Input type`/`Mark Read`, non-suppressed runs full path |
| 2 | Bot-wide `'*'` default suppresses never-opened chats; per-chat override beats default; absence → bot replies | ✓ VERIFIED | Precedence SQL live-run in 09-04: override→`false`, default-fallback→`true`, absence→`false` (all match expected); UAT scenario 3 (never-opened chat suppressed by `'*'` default) PASS; UAT scenario 5 (never-toggled chat replies) PASS |
| 3 | Gate is fail-closed with zero extra error wiring; app re-asserts flag on chat open so a lost write self-heals | ✓ VERIFIED (with a documented edge-case caveat — see Anti-Patterns) | `grep -c continueOnFail` = 0 in both templates; no `onError`/`retryOnFail`/`alwaysOutputData` on `Read Reply Mode`; UAT scenario 1 sub-check (re-open a suppressed chat → still suppressed) PASS. Caveat: code review WR-01 identifies that the same heal mechanism, when the bot default is «Вместе» and a chat is merely *opened* (not explicitly toggled), writes a sticky per-chat override that survives a later default flip back to «Авто» — a real but narrow interaction not exercised by any must_have truth, ROADMAP SC wording, or UAT scenario (see below) |
| 4 | A freshly created bot inherits the gate via template cloning; existing dev clones recreated | ✓ VERIFIED | 09-04 Task 3: REST grep on the 10-03 fresh clones (`fKCMIGXJSbLRimdR`/`pOMkkP8MYS8WhiNY`) shows `Read Reply Mode` + `Suppressed?` present, fed by the group-chat `If` |
| 5 | EditMode payload/hook tests green; n8n curl matrix (upsert, precedence, absence→reply, malformed→clean error) green | ✓ VERIFIED | Suite 1197/1197 green as of Phase 10 close (`ad212db`, 2026-07-22) — includes the 5 `ReplyModeSyncPayloadTests`; `git diff --stat ad212db..HEAD -- '*.cs'` = 0 lines (zero `.cs` changes since, git-verified this session — the green result still describes the current code); curl matrix (a)/(b)/(c) + precedence SQL all matched expected in 09-04 |

**Score:** 5/5 ROADMAP success criteria verified.

### Per-Plan must_haves (all 5 plans)

| Plan | Truth | Status | Evidence |
|---|---|---|---|
| 09-01 | DDL creates pk(profile_id, chat_id) table, chat_id default '*', default-deny RLS | ✓ VERIFIED | `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` matches exactly; RLS live-confirmed in 09-04 (`relrowsecurity=true`, anon select denied) |
| 09-01 | POST /webhook/SetReplyMode upserts one row per profileId (ON and OFF both write) | ✓ VERIFIED | Live curl (a)/(b) both `{"success":true,"written":1}` (09-04) |
| 09-01 | Malformed body rejected before any DB write | ✓ VERIFIED | Live curl (c) `{"success":false,"error":"bad_request"}`; `count(*)` for probe = 2 (only the two valid writes, no partial row) |
| 09-02 | Each bot template reads suppression flag after group-chat If, dead-ends reply when suppressed | ✓ VERIFIED | Both templates: `If.main[0]→Read Reply Mode→Suppressed?`; `Suppressed?` TRUE branch = `[]` (empty, dead-end) |
| 09-02 | Suppressed message never marked read | ✓ VERIFIED | Dead-end array means nothing downstream of `Suppressed?` TRUE runs, including `Mark Read`; runData (09-04) confirms `Mark Read` absent on a suppressed run |
| 09-02 | Gate fails closed on Postgres error | ✓ VERIFIED | `grep -c continueOnFail` = 0 in both templates; no error-tolerance options anywhere on `Read Reply Mode` |
| 09-03 | Flipping a chat's «Вместе» toggle POSTs for the active channel's profile + real chatId | ✓ VERIFIED | `HandleToggle` → `PushReplyModeForActiveChat(desiredOn)` (code read directly); UAT scenarios 1/2/4 confirm end-to-end |
| 09-03 | Flipping a bot's default POSTs the '*' row for every authed profile of that bot | ✓ VERIFIED | `OnBotReplyModeChanged` → `SyncReplyMode(AuthedProfileIds(bot), "*", …)` (code read directly); UAT scenario 3 confirms |
| 09-03 | Re-opening a suppressed chat re-asserts suppressed=true (self-heal) | ✓ VERIFIED | `RestoreForActiveChat` → `PushReplyModeForActiveChat(true)` inside `if (_semiAutoOn)`; UAT scenario 1 sub-check PASS |
| 09-03 | Sentinel/blank profile ids never write; live poll never writes | ✓ VERIFIED | `IsRealProfileId` filters `Bot.UnauthedProfileSentinel`/blank; `HandleLive` (grep) contains no `PushReplyModeForActiveChat` call — only `HandleToggle` and `RestoreForActiveChat` do |
| 09-04 | reply_mode_flags exists on dev DB the gate reads; probe round-trips | ✓ VERIFIED | 09-04 Task 1: insert→select=`true`→delete, owner-run through the live cred |
| 09-04 | Webhook upserts default+override rows, rejects malformed with no partial write | ✓ VERIFIED | Curl matrix as above |
| 09-04 | runData shows suppressed dead-end / non-suppressed full path | ✓ VERIFIED | Both channels, recorded in 09-04-SUMMARY.md |
| 09-04 | Fresh bot's cloned workflow contains the gate | ✓ VERIFIED | REST grep on fresh clones |
| 09-05 | On-device: «Вместе» → no reply + unread + suggestions populate; «Авто» restores — both channels | ✓ VERIFIED | 09-HUMAN-UAT.md, all 5 scenarios PASS, 2026-07-22 |

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` | reply_mode_flags DDL + default-deny RLS | ✓ VERIFIED | Present, matches locked shape exactly; applied live (09-04) |
| `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` | id-finalized canonical Set Reply Mode workflow | ✓ VERIFIED | Present; `active:true`; Webhook→Validate→If invalid?→Upsert(cred `vvRrFiEXzLVqKjOx`)→Respond graph intact; provisional `Set_Reply_Mode.json` correctly removed |
| `Tools/n8n/build-set-reply-mode.py` | REST deployer, explicit Postgres-cred override | ✓ VERIFIED | Compiles; `DEFAULT_POSTGRES_CRED_ID = "vvRrFiEXzLVqKjOx"` |
| `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` | Read Reply Mode + Suppressed? gate on If.main[0] | ✓ VERIFIED | Gate present, wired, cred consolidated to `vvRrFiEXzLVqKjOx`, `Suppressed?` FALSE→`Debounce Wait` (Phase-10 chain, expected) |
| `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` | identical gate on If.main[0] | ✓ VERIFIED | Same as WhatsApp, byte-structurally identical gate |
| `Assets/Scripts/Main/Manager.ReplyModeSync.cs` | BuildReplyModePayload + AuthedProfileIds + SyncReplyMode + OnReplyModeChanged hook | ✓ VERIFIED | 107 lines; all pieces present and match plan spec exactly |
| `Assets/Tests/Editor/Chat/ReplyModeSyncPayloadTests.cs` | pure payload + sentinel-filter EditMode tests | ✓ VERIFIED | 5 tests present, cover both payload shapes and all 3 AuthedProfileIds sentinel/blank cases |
| `Assets/Scripts/Main/ChatManager.Channel.cs` | public ActiveChannelProfileId() accessor | ✓ VERIFIED | `public string ActiveChannelProfileId() => GetActiveProfileId();` present |
| `.planning/phases/09-semi-auto-suppression/09-HUMAN-UAT.md` | 5-scenario owner runbook + verdicts | ✓ VERIFIED | All 5 scenarios recorded PASS, verdict table filled, disposition ALL PASS |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| Set_Reply_Mode.json Upsert node | reply_mode_flags | Postgres executeQuery on conflict do update | ✓ WIRED | Cred `vvRrFiEXzLVqKjOx` (consolidated from plan's `1H5xlpFSESU4w6JH` — documented, intentional; both always targeted the same Supabase DB per 09-RESEARCH A3) |
| Validate Code node | If invalid? | one item per surviving profileId, or invalid flag | ✓ WIRED | jsCode confirmed by direct read; curl (c) confirms the invalid path |
| group-chat If (main[0]) | Read Reply Mode | rewired connection | ✓ WIRED | Confirmed in both templates via JSON connections object |
| Read Reply Mode | reply_mode_flags | coalesce resolve query, cred, no continueOnFail | ✓ WIRED | Query text verified verbatim; live runData confirms real reads |
| Suppressed? (false) | Input type (via Debounce Wait, Phase 10) | existing reply path | ✓ WIRED | Confirmed by connections object + runData |
| SuggestionsController.HandleToggle | Manager.SyncReplyMode | PushReplyModeForActiveChat after SemiAutoStore.Set | ✓ WIRED | Direct code read confirms call site + ordering |
| ReplyModeToggleBinder.OnReplyModeChanged | Manager.OnBotReplyModeChanged | static event subscription in Manager partial | ✓ WIRED | `OnEnable`/`OnDisable` subscribe/unsubscribe pair present, exactly once each |
| Manager.SyncReplyMode | /webhook/SetReplyMode | UnityWebRequest POST coroutine | ✓ WIRED | `SyncReplyModeRoutine` posts to `{n8nBaseUrl}/webhook/SetReplyMode`; UAT scenarios confirm live delivery (no 404s reported) |

### Data-Flow Trace (Level 4)

Not applicable in the standard sense (this phase has no UI component rendering dynamic data) — the equivalent trace is the write→read round trip through the live DB, which was directly exercised: client write (Manager.SyncReplyMode) → webhook upsert → Postgres row → gate's `Read Reply Mode` query → `Suppressed?` branch → observable no-reply/reply behavior. This full chain was proven live in 09-04 (curl + runData) and 09-05 (on-device UAT), not just structurally wired.

### Behavioral Spot-Checks

Not run as fresh automated checks this session — the equivalent behavioral proof already exists and is stronger than a spot-check: the full 5-scenario on-device UAT (09-HUMAN-UAT.md) exercises the exact behaviors an automated spot-check would approximate, on a real device, both channels, with real WhatsApp/Telegram profiles. Re-running is unnecessary since zero `.cs` files and no n8n workflow JSON changed since the UAT was recorded (git-verified: `git log` shows the last touch to the bot templates was `605e399`/`ec15832`, both from 09-04, predating the 09-05 UAT run).

### Requirements Coverage

SUP-01 through SUP-05 are defined in `09-CONTEXT.md` and are **not yet present in `.planning/REQUIREMENTS.md`** (that file is still the v1.1 Telegram-parity requirements doc; the v1.2 REQUIREMENTS.md has not been formalized yet — expected per phase context, not a traceability gap).

| Requirement | Definition (09-CONTEXT.md) | Status | Evidence |
|---|---|---|---|
| SUP-01 | reply_mode_flags table, pk(profile_id, chat_id), default-deny RLS | ✓ SATISFIED | DDL authored + applied live + RLS confirmed |
| SUP-02 | Shared /webhook/SetReplyMode upserts flags from 3 client write sites | ✓ SATISFIED | Webhook deployed+active; all 3 write sites wired and code-confirmed; UAT confirms end-to-end delivery. Note: WR-01/WR-02 (see Anti-Patterns) are narrower edge-case gaps in this requirement's write-path completeness — not required by any must_have truth, tracked as debt |
| SUP-03 | Gate in both templates, dead-ends suppressed replies, stays unread | ✓ SATISFIED | Gate present + fail-closed + runData + UAT proven both channels |
| SUP-04 | Precedence resolve (override > default > absence=false), fail-closed | ✓ SATISFIED | Precedence SQL + curl matrix + no continueOnFail all confirmed |
| SUP-05 | Propagation via template cloning; existing clones recreated; prod dormant | ✓ SATISFIED | Fresh-bot grep passed; dev clones recreated (10-03); prod stays dormant per standing project decision (documented, not a gap) |

### Anti-Patterns Found

Sourced primarily from `09-REVIEW.md` (code review, status `issues_found`, 0 critical / 4 warnings / 5 info) and independently re-verified by direct code reads during this verification pass (not taken on faith).

| File | Pattern | Severity | Impact |
|---|---|---|---|
| `Assets/Scripts/Chat/SuggestionsController.cs:114-120` | WR-01: on-open heal collapses tri-state `SemiAutoStore` into boolean, writing a sticky per-chat override for a merely-*opened* chat when the bot default is «Вместе» — that row outlives a later default flip back to «Авто», silently suppressing that chat forever | ⚠️ Warning (edge-case, non-blocking) | Independently re-confirmed by direct read: `RestoreForActiveChat` computes `_semiAutoOn = SemiAutoStore.IsOn(...)` (collapses inherited-default state 0 into the same boolean as explicit ON) and unconditionally calls `PushReplyModeForActiveChat(true)` when true — no distinction between "explicit per-chat ON" and "inherited from bot default." Real defect, but: (a) no must_have truth in any of the 5 plans is contradicted — the literal truth "re-opening a suppressed chat re-asserts suppressed=true" IS true; (b) no ROADMAP SC wording is violated on its exact terms; (c) not exercised by any of the 5 UAT scenarios (scenario 3 tests a *never-opened* chat only); (d) already flagged + fix proposed in 09-REVIEW.md. **Classified as tracked-debt, not a blocking gap** — recommend addressing via `/gsd-code-review-fix 09` or a small follow-up plan before the bot-wide-default UX pattern (set default → browse chats → flip back) becomes common in production |
| `Assets/Scripts/Main/Manager.ReplyModeSync.cs:82-87` (+ `ReplyModeToggleBinder.cs:155`) | WR-02: a channel authed *after* the owner set the bot default to «Вместе» never receives the `'*'` row for its new profile id — the gate coalesces to `false` for that profile, so the newly-authed channel auto-replies while the app displays the bot-level toggle as «Вместе» | ⚠️ Warning (edge-case, one-directional fail-open, non-blocking) | Independently re-confirmed: `OnBotReplyModeChanged` is the only bot-default write site and only fires on an explicit flip event; no re-assert exists in the WhatsApp/Telegram auth-completion path. Real defect (double-reply risk on late channel auth), but narrow in practice (requires: bot default already Вместе + a *second* channel authed afterward) and not required by any must_have truth or UAT scenario. **Classified as tracked-debt** |
| `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json:23` (Validate jsCode) | WR-03: Validate does not reject a comma inside `chatId`/`profileIds`, which could shift n8n's positional `queryReplacement` comma-split params | ⚠️ Warning (unauthenticated endpoint, low practical likelihood — legit ids never contain commas) | Independently re-confirmed via direct read of the Validate node's jsCode — no `,`-rejection present. **Classified as tracked-debt**; one-line fix proposed in 09-REVIEW.md, low urgency since legitimate Wappi/tapi ids never contain commas |
| `Assets/Scripts/Main/Manager.cs` (pre-existing) | WR-04: pre-existing silent-failure network paths (unrelated to Phase 9 logic, file only in scope because it gained the `partial` keyword) | ℹ️ Info | Legacy debt, explicitly out of Phase 9 scope per review; not evaluated further here |
| Various | IN-01..IN-05 (defense-in-depth null guards, fan-out cap, deployer duplicate-guard, dead legacy upload path) | ℹ️ Info | Advisory only, no functional impact on the phase goal |

No 🛑 Blocker-severity anti-patterns found. No stub implementations, no placeholder returns, no orphaned wiring.

### Human Verification Required

None outstanding. The phase's user-observable behavior was already confirmed via the owner-run `09-HUMAN-UAT.md` gate (2026-07-22, all 5 scenarios PASS, both channels, one device build) — this verification pass re-confirmed that no code or n8n workflow changed since that UAT run (git-verified: zero `.cs` diffs since Phase 10 close; the bot templates' last edits were 09-04's `605e399`/`ec15832`, both predating the UAT).

### Gaps Summary

No blocking gaps. All 5 plans' must_haves (truths, artifacts, key_links) are verified against the actual codebase — not just SUMMARY claims. All 5 ROADMAP Phase 9 success criteria are proven both structurally (DDL, workflow JSON, curl matrix, precedence SQL, runData) and behaviorally (real on-device UAT across WhatsApp and Telegram).

Three warning-level findings from `09-REVIEW.md` (WR-01, WR-02, WR-03) were independently re-verified against the live code/workflow during this pass and are real. They describe narrower edge-case interactions than what any must_have truth, ROADMAP success-criterion wording, or UAT scenario requires:
- WR-01 (sticky per-chat suppression surviving a bot-default flip-back) and WR-02 (late-authed channel missing the bot-default row) both concern *compounding* usage sequences beyond the 5 tested scenarios.
- WR-03 (comma-injection hardening) concerns an unauthenticated-webhook input-validation gap with low practical likelihood given real chat/profile id formats.

These are recommended as tracked debt for a follow-up (`/gsd-code-review-fix 09` or a small closure plan) rather than phase-blocking gaps, since fixing them does not require re-touching the phase's core architecture and the phase's actual delivered promise — «Вместе» suppresses with self-heal on the primary flows, «Авто» restores, bot-wide default suppresses never-opened chats, fail-closed on DB error, propagates to new bots — is proven true by both structural verification and live owner UAT.

---

_Verified: 2026-07-22T16:18:18Z_
_Verifier: Claude (gsd-verifier)_
