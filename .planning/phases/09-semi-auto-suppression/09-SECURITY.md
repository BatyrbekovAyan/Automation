---
phase: 9
slug: semi-auto-suppression
status: secured
threats_open: 0
asvs_level: 1
created: 2026-07-22
---

# Phase 9 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.
> Verified 2026-07-22 by gsd-security-auditor against the live repo state (post WR-01..04 review-fix commits: 97cc79b/3b580f7/34e186b/332f3a4), the committed canonical `Set_Reply_Mode.json` (id `SCLcpn6DMDG3Z4VN`) + both gated bot templates, and the recorded live-instance evidence in 09-04-SUMMARY.md / 09-05-SUMMARY.md / 09-HUMAN-UAT.md.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| internet/mobile app → `/webhook/SetReplyMode` | Untrusted, unauthenticated HTTP request enters the write path (no API key, by design — same class as every app `/webhook/*`) | `{ profileIds:[...], chatId, suppressed }` |
| n8n → Supabase Postgres (`Upsert Reply Mode`) | Server-side write to `reply_mode_flags` | profile/chat ids + suppression boolean |
| mobile binary (anon key) → Supabase Data API | The shipped anon key must never read/write `reply_mode_flags` | n/a — must be zero access |
| incoming customer message (wappi/tapi) → n8n Webhook → `Read Reply Mode` gate | Untrusted `messages[0].from` / `profile_id` enter the resolve query | chat/profile identifiers |
| bot template → Supabase Postgres (`Read Reply Mode`) | Server-side read gating the reply path | suppression boolean, always-one-row coalesce |
| Unity client (`SuggestionsController` / `Manager.ReplyModeSync`) → `/webhook/SetReplyMode` | The app POSTs on toggle flip / bot-default flip / re-assert-on-open | profile/chat ids + suppression boolean, no auth header |
| owner tooling → dev Supabase (cred `vvRrFiEXzLVqKjOx`) | DDL + probe rows applied through the gate's own cred | schema + probe data |
| real customer message → live gated bot (dev clone, active for test window only) | Behavioral suppression exercised with real WhatsApp/Telegram contacts | live conversation |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation (verified evidence) | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-09-01 | Tampering | `Upsert Reply Mode` Postgres node | mitigate | `options.queryReplacement` binds `$1,$2,$3::boolean` positionally; query text has zero string-concatenation of `profileId`/`chatId` — `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json:87-89` | closed |
| T-09-02 | Tampering / DoS | `/webhook/SetReplyMode` unauthenticated | accept | See Accepted Risks Log R-09-01 | closed |
| T-09-03 | Tampering | `Validate` + `If invalid?` | mitigate | Malformed body → `{invalid:true}` → `Respond Error` BEFORE `Upsert Reply Mode` runs (graph: `Webhook→Validate→If invalid?→[true]Respond Error / [false]Upsert`). Hardened post-review (WR-03, commit `34e186b`): every profileId + chatId run through `clean(s)` — type-check, 1–128 char length cap, comma-rejection (blocks the `queryReplacement` comma-split attack) — plus `.slice(0,10)` fan-out cap. Verified in the live jsCode, `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json:23`. Live curl matrix (09-04-SUMMARY): malformed body → `{"success":false,"error":"bad_request"}`, `count(*)` for the probe profile stayed at 2 (no partial row) | closed |
| T-09-04 | Information Disclosure | `reply_mode_flags` RLS | mitigate | DDL: `alter table ... enable row level security` + `revoke all on table public.reply_mode_flags from anon, authenticated`, no policies (default-deny) — `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql:31-32`. Owner-verified live (09-04-SUMMARY): `relrowsecurity=true`; `has_table_privilege('anon','public.reply_mode_flags','select')=false` | closed |
| T-09-05 | Tampering | `Read Reply Mode` query (both templates) | mitigate | `options.queryReplacement` binds `$1`/`$2` to `messages[0].profile_id` / `messages[0].from` positionally — verified identical in both `4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `4VN3gsFaC2HUYmcc-Telegram_Bot.json`; no string-concatenation of the untrusted `from`/`profile_id` into the SQL text | closed |
| T-09-06 | Repudiation / Safety | `Read Reply Mode` error handling | mitigate | Fail-closed by construction: `grep -c "continueOnFail\|retryOnFail"` = 0 in both template files; no `onError`/`alwaysOutputData` on the node — a genuine Postgres error throws and halts the execution, so a «Вместе» chat is never silently auto-answered | closed |
| T-09-07 | DoS / availability | Gate on the reply hot path | accept | See Accepted Risks Log R-09-02 | closed |
| T-09-08 | Tampering | `Suppressed?` boolean coercion (A1) | mitigate | `typeValidation:"loose"` boolean-true condition on `{{ $json.suppressed }}` in both templates. Live runData 2026-07-22 (09-04-SUMMARY, Task 3): "A1: the boolean came through CLEAN — no `suppressed::boolean` cast needed, the branch routed correctly as-is" on both channels | closed |
| T-09-09 | Information Disclosure | client `SyncReplyModeRoutine` POST | accept | See Accepted Risks Log R-09-01 | closed |
| T-09-10 | Tampering | `AuthedProfileIds` / `PushReplyModeForActiveChat` | mitigate | `AuthedProfileIds` filters both `""` and the `"-1"` sentinel via `IsRealProfileId` (`Manager.ReplyModeSync.cs:48-57`); `PushReplyModeForActiveChat` independently guards `string.IsNullOrEmpty(profileId) \|\| profileId == Bot.UnauthedProfileSentinel` before ever calling `SyncReplyMode` (`SuggestionsController.cs:158-167`); `SyncReplyMode` itself no-ops on an empty/null array (`Manager.ReplyModeSync.cs:63-67`) | closed |
| T-09-11 | DoS | re-assert-on-open write | mitigate | `PushReplyModeForActiveChat` is called from exactly two sites: `HandleToggle` (explicit user action) and inside `RestoreForActiveChat`'s `if (SemiAutoStore.TryGetOverride(...))` block (once per `OnChatSelected`) — `SuggestionsController.cs:120-121,137`. `HandleLive` (the 3s open-chat `LivePoll` handler, `SuggestionsController.cs:212-218`) contains no call to `PushReplyModeForActiveChat` or `SyncReplyMode` — confirmed by direct read, no POST-every-3s storm | closed |
| T-09-12 | Tampering | live `/webhook/SetReplyMode` (dev) | accept | See Accepted Risks Log R-09-01. Live curl matrix (09-04-SUMMARY): (a)/(b) valid upserts → `{"success":true,"written":1}`; (c) malformed → `{"success":false,"error":"bad_request"}` with no partial row (`count(*)`=2 for the 2 legit probe writes only) | closed |
| T-09-13 | Repudiation / Safety | live gate fail-closed | mitigate | Owner-recorded runData both channels (09-04-SUMMARY, Task 3): with `suppressed:true` seeded, only `Webhook→…→Read Reply Mode→Suppressed?` executed; the entire downstream (debounce chain, `Input type`, `Mark Read`, agent, send) was absent — no reply, chat stayed unread; with `suppressed:false` the full reply path ran. No `continueOnFail` present (static verification above) | closed |
| T-09-14 | Elevation of Privilege | 09-04 test clone left ACTIVE | mitigate | Owner-recorded (09-04-SUMMARY, Task 3 + Self-Check): "Test clones deactivated after the window; suppression rows deleted (table back to empty = fail-open default)" | closed |
| T-09-15 | Information Disclosure | `reply_mode_flags` via anon key | mitigate | Owner-recorded (09-04-SUMMARY, Task 1): RLS default-deny confirmed (`relrowsecurity=true`, anon `select` privilege denied) BEFORE any live write against the table | closed |
| T-09-16 | Elevation of Privilege | 09-05 e2e clone left ACTIVE | mitigate | Owner-recorded verbatim resume signal (09-05-SUMMARY): "UAT pass — clone deactivated"; post-run checkbox ticked, prod bagkz confirmed untouched/dormant | closed |
| T-09-17 | Repudiation / Safety | «Вместе» chat silently auto-answered | mitigate | Owner-recorded 5-scenario UAT, all PASS (09-05-SUMMARY / 09-HUMAN-UAT.md): Scenario 1 (WhatsApp) — no auto-reply, stayed unread, suggestions still populated, re-open heal held; Scenario 4 (Telegram) — identical parity; Scenario 5 — never-toggled chat replied normally (absence→reply), confirming suppression is never silently mis-applied to the common case | closed |
| T-09-18 | Tampering | prod bagkz accidental target | accept | See Accepted Risks Log R-09-03 | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| R-09-01 | T-09-02, T-09-09, T-09-12 | `/webhook/SetReplyMode` is unauthenticated, consistent with every other app `/webhook/*` endpoint (extends the precedent set by R-02-01 in `02-SECURITY.md`). Blast radius is bounded: flipping a bot's reply suppression flag — no data exfiltration, no LLM spend, no message content exposed. The unauthenticated fan-out is additionally bounded by the WR-03 hardening (length cap, comma-rejection, 10-id slice cap) so a malicious caller cannot abuse the endpoint for a large write or a `queryReplacement` parameter-shift. The client (`Manager.ReplyModeSync.SyncReplyModeRoutine`) sends no auth header and no secret over this path by design (same as `DeleteBotFilesOnServer`). Authentication for `/webhook/*` remains a deferred, milestone-level decision, not a per-phase fix. | Owner (via 09-01/09-03/09-04-PLAN threat models + secure-phase run) | 2026-07-22 |
| R-09-02 | T-09-07 | The suppression gate (`Read Reply Mode`) sits on the reply hot path with no error tolerance (fail-closed by design, SUP-04): a Postgres outage halts ALL replies rather than risk a silent auto-answer in a «Вместе» chat. This introduces no NEW point of failure — both bot templates already depend on this same Postgres credential for Chat Memory, so an outage that would break the gate already breaks the reply path upstream. The fail-closed/fail-available tradeoff was a deliberate design decision (SUP-04), not an oversight. | Owner (via 09-02-PLAN threat model + secure-phase run) | 2026-07-22 |
| R-09-03 | T-09-18 | Prod (`bagkz`) stays dormant for the entirety of Phase 9 — all DDL apply, webhook deploy, curl/runData verification, and the 5-scenario behavioral UAT targeted dev exclusively (owner-confirmed in 09-04-SUMMARY and 09-05-SUMMARY: "prod bagkz untouched/dormant"). The suppression gate + the Postgres cred consolidation (`vvRrFiEXzLVqKjOx`) are explicitly folded into a future one-shot prod bulk-copy (SUP-05), not executed this phase. Accidental-prod-target risk is operationally mitigated by dormancy, not by a code-level guard — tracked here as accepted pending that future replication task. | Owner (via 09-04/09-05-PLAN threat models + secure-phase run) | 2026-07-22 |

*Accepted risks do not resurface in future audit runs.*

---

## Residual Notes (non-blocking)

- **Cred consolidation (09-04 Deviation 1, commit `ec15832`):** the plan/research bound the Postgres credential by explicit id `1H5xlpFSESU4w6JH`; ground-truthing on the live dev instance found only `vvRrFiEXzLVqKjOx` exists there. Repo-wide consolidated to the id that actually exists (both templates, the deployer, the SQL header, README). Both ids always targeted the same Supabase DB (RESEARCH A3) — reference hygiene, not a data-exposure or access-control change. Prod replication must bind to whichever id exists on the prod instance (tracked under R-09-03).
- **WR-02 late-auth seed (`SeedReplyModeDefaultForProfile`, commit `3b580f7`):** closes a real correctness gap (a channel authed after a Вместе default previously reached the reply path unsuppressed on that channel) — not itself a new threat surface; it reuses the same `SyncReplyMode` write path already covered by T-09-10/T-09-11's mitigations (sentinel filtering, no live-poll writes).
- **WR-01 heal scope narrowing (commit `97cc79b`):** the on-open heal now reads the raw tri-state (`SemiAutoStore.TryGetOverride`) instead of the collapsed boolean, so merely opening a chat that inherits the bot default no longer mints a sticky per-chat override row on the server. This narrows write volume/scope in the client's favor; no new threat introduced.
- **Unregistered threat flags:** none. All five plan SUMMARYs (`09-01` through `09-05`) were checked for a `## Threat Flags` section — none present.

---

## Security Audit 2026-07-22

| Metric | Count |
|--------|-------|
| Threats found | 18 |
| Closed | 18 |
| Open | 0 |

Auditor: gsd-security-auditor (first `SECURITY.md` for this phase, built from the 5 plans' threat models — 09-01 through 09-05). Verification was evidence-grounded: implementation files (workflow JSON, SQL DDL, C# source) read directly against every `mitigate` threat's declared mitigation pattern; `accept`-disposition threats closed by authoring this Accepted Risks Log per the R-02-01 precedent established in `02-SECURITY.md`; live-instance-only claims (RLS live state, runData branch confirmation, clone deactivation, 5-scenario behavioral UAT) accepted from the owner-recorded evidence in `09-04-SUMMARY.md`, `09-05-SUMMARY.md`, and `09-HUMAN-UAT.md` per the audit's environment constraints — no live n8n/Supabase/device access from this session.

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: secured` set in frontmatter

**Approval:** verified 2026-07-22
