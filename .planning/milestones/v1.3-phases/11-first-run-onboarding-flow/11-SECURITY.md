---
phase: 11
slug: first-run-onboarding-flow
status: verified
threats_open: 0
asvs_level: 1
created: 2026-07-23
reaudited: 2026-07-27
---

# Phase 11 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.
> First-Run Onboarding is a **client-only** phase — PlayerPrefs flags, scene UI, and a
> read-only message-event latch. No server/n8n changes, no secrets touched, no new
> network endpoints. The single load-bearing risk is regressing the live auth code
> flow; that is proven byte-identical below.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| editor builder → scene | `ReorderScreens` / card builders mutate `Main.unity` at edit time | none at runtime (compile-time only) |
| onboarding UI → auth screens | Trust cards + relocated success overlay sit alongside the existing WhatsApp/Telegram code panels | none — the auth request code is untouched |
| ChatManager events → first-reply latch | `OnBatchMessagesLoaded`/`OnLiveMessagesReceived` observed to set the row-4 flag | in-app message view-models (already in memory); no new source |
| onboarding flags → PlayerPrefs | 5 global 0/1 flags outside the per-bot key namespace | app-written ints, no PII/secrets |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-11-01-01 | Tampering | NavRestructureBuilder.ReorderScreens | mitigate | Auth pages last in `order[]`, Screen_Onboarding before them (`NavRestructureBuilder.cs:462-464`) | closed |
| T-11-01-02 | Tampering | Bot.OpenSettings exposure | mitigate | `OpenSettings` stays private (`Bot.cs:100`); only tab wrappers public | closed |
| T-11-01-03 | Information disclosure | OnboardingKeys PlayerPrefs flags | accept | App-written 0/1 ints, no PII; wiped by `PlayerPrefs.DeleteAll()` | closed |
| T-11-02-01 | Denial of service | OnboardingScreen CTA never fires | mitigate | Flag set before `StartNewBot()` (null-conditional) — user never trapped (`OnboardingScreen.cs:55-61`) | closed |
| T-11-02-02 | Tampering | Pager snap drifts | mitigate | `Clamped` + `inertia=false`; snap via unit-tested OnboardingPageMath | closed |
| T-11-02-03 | Information disclosure | OnboardingSeen flag | accept | App-written 0/1 int, no PII | closed |
| T-11-03-01 | Tampering | RefreshEmptyState gate | mitigate | Single chokepoint; `null`/`seen` falls back to `StartNewBot()` (`BotsPage.cs:42,59,65`) | closed |
| T-11-03-02 | Tampering | Existing-user auto-flag fact | mitigate | Live `BotsParent.transform.childCount`, never the monotonic `id` (`Manager.cs:451`) | closed |
| T-11-03-03 | Tampering | Screen ordering | mitigate | Onboarding-aware ReorderScreens; auth pages last | closed |
| T-11-03-04 | Repudiation | Uncommitted scene clobber | mitigate | Scene committed in-phase (e.g. `96f6ee1`) | closed |
| T-11-03-05 | Information disclosure | OnboardingSeen flag | accept | App-written 0/1 int, no PII | closed |
| T-11-04-01 | Tampering | ShowAuthSuccess re-sequence breaks auth | mitigate | Auth code flow byte-identical (git blame; all lines predate Phase 11) | closed |
| T-11-04-02 | Denial of service | Success moment null/blank/never dismisses | mitigate | `bot==null`/`SuccessOverlay==null` yield-break; `dismissed` set by both buttons; overlay always hidden | closed |
| T-11-04-03 | Repudiation | Success double-fires for "both" | mitigate | Exactly 2 sites (creation + settings), creation else-branch gated `!isCreatingBot` | closed |
| T-11-04-04 | Tampering | Wrong channel field set / blank | mitigate | Superseded by D2: single channel-agnostic standalone overlay (no per-channel blank possible) | closed |
| T-11-04-05 | Trust/Repudiation | Over-promising copy | mitigate | Owner-approved verbatim deck; UAT Round 2 approved | closed |
| T-11-05-01 | Tampering | GetChild index shift breaks auth | mitigate | Trust card appended `SetAsLastSibling`; GetChild(3/4/5) unchanged | closed |
| T-11-05-02 | Repudiation/trust | Over-promising security copy | mitigate | Verbatim reassurance copy; no e2e/QR claims | closed |
| T-11-05-03 | Repudiation | Uncommitted scene clobber | mitigate | Scene committed in-phase | closed |
| T-11-05-04 | Tampering | Success buttons blank on one channel | mitigate | Superseded by D2 single-hierarchy overlay | closed |
| T-11-06-01 | Tampering | Per-step state stored/stale | mitigate | States derived LIVE every Refresh; only milestones latch (`FirstStepsCard.cs:140`,`167-177`) | closed |
| T-11-06-02 | Denial of service | Card never hides / resurrects | mitigate | `ShouldShow` false when `ChecklistDone` latched; CanvasGroup hide, root reachable | closed |
| T-11-06-03 | Information disclosure | First-reply proxy conflates bot vs owner | accept | `isIncoming==false` documented proxy; write is a 0/1 flag | closed |
| T-11-06-04 | Repudiation | Uncommitted scene clobber | mitigate | Scene committed in-phase | closed |
| T-11-07-01 | Tampering | Auth regression slips past EditMode | mitigate | Owner device gate re-ran both auth flows incl. Telegram 2FA (Round 2 approved) | closed |
| T-11-07-02 | Repudiation | Trust copy over-promising on device | mitigate | Copy verified at real size in owner UAT | closed |
| T-11-08-01 | Tampering | D2 relocation breaks auth code flow | mitigate | Only success methods/fields changed; GetChild(3/4/5) unchanged (blame) | closed |
| T-11-08-02 | Denial of service | Standalone overlay stuck | mitigate | Canvas-root overlay; `dismissed` both buttons; `CloseSuccessAndOverlay` always `SetActive(false)` | closed |
| T-11-08-03 | Elevation/Info | 2s transient breaks after SuccessCta teardown | accept | Nested panels retained; `moreAuthSteps` 2s path unchanged | closed |
| T-11-09-01 | Denial of service | RefreshFromFacts on null/destroyed card | mitigate | `Instance?.RefreshFromFacts()` null-conditional; Refresh null-guards refs | closed |
| T-11-09-02 | Tampering | A hook alters an auth code path | mitigate | Latch installed additively in `ChatManager.cs:235-236`, outside GetChild/auth code | closed |
| T-11-09-03 | Information disclosure | Card resurrects after 4/4 hide | accept | `ShouldShow` false when `ChecklistDone` latched (monotonic, never un-set) | closed |
| T-11-10-01 | Tampering | Round-1 UAT history overwritten | mitigate | Append-only; Round-1 defect table + Overall line preserved | closed |
| T-11-10-02 | Repudiation | Owner verdict ambiguous | accept | Each item is a PASS/FAIL tied to a requirement; Round-2 overall recorded | closed |
| T-11-11-01 | Denial of service | WhatsApp de-auth probe lacks the Telegram twin's `isOnWhatsapp == 1` gate | accept | Added by the 2026-07-27 re-audit. Post-WR-03 the WhatsApp destructive path is strictly NARROWER than the old scan (malformed bodies can no longer delete), but its exposure widened in exactly one direction: a pretty-printed `authorized:false` previously threw before reaching the delete and now performs it. Requires Wappi switching the `api/sync` body to pretty-print AND a transient/mid-pairing `authorized:false`. The twin gate was a deliberately-not-taken hardening (recorded in `11-REVIEW-FIX.md`) because adding it would change behaviour beyond the finding. | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

**Post-approval additions (verified under the accept-class rationale of T-11-01-03 / T-11-06-03):**
- `OnboardingChannelConnectedSeen` + `OnboardingPriceListSeen` (row-2/3 milestone latches, `OnboardingKeys.cs:23,27`) — app-written 0/1 flags, no PII, wiped by DeleteAll.
- First-reply latch relocated to `ChatManager.Awake` (`OnboardingFirstReplyLatch`, installed once, never unsubscribed) — no new trust boundary, same `isIncoming==false` proxy, single 0/1 flag write.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-11-01 | T-11-01-03, T-11-02-03, T-11-03-05 | `OnboardingSeen` and the onboarding milestone/first-reply flags (incl. `OnboardingChecklistDone`, `FirstBotReplySeen`, `OnboardingChannelConnectedSeen`, `OnboardingPriceListSeen`) are app-written 0/1 ints — no PII, no secrets, no auth material — wiped by the existing full-wipe path («Удалить все данные» → `PlayerPrefs.DeleteAll()`). | Owner (design spec §State) | 2026-07-23 |
| AR-11-02 | T-11-06-03 | The row-4 "bot replied" signal uses `isIncoming==false` as a demonstrative proxy; it also latches on the owner's own outgoing message. A stricter bot-vs-owner distinction needs out-of-scope server metadata (Pitfall 5, spec-accepted). | Owner (design spec) | 2026-07-23 |
| AR-11-03 | T-11-08-03 | The `moreAuthSteps` 2-second transient checkmark inside the nested per-channel panels is retained unchanged after the D2 relocation removed only the injected `SuccessCta` children. | Owner (D2 gap decision) | 2026-07-23 |
| AR-11-04 | T-11-09-03, T-11-10-02 | The checklist's 4/4 permanent-hide latch is a monotonic PlayerPref (never un-set), so the card cannot resurrect; and the owner UAT verdict is recorded as PASS/FAIL per item in `11-HUMAN-UAT.md`. | Owner (UAT Round 2) | 2026-07-23 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-07-23 | 34 | 34 | 0 | gsd-security-auditor (State B, from PLAN threat models + current code) |
| 2026-07-27 | 35 | 35 | 0 | gsd-security-auditor (State A re-audit at HEAD `36e846e` — 12 code commits had landed since the original pass, several rewriting auth-adjacent parsing) |

### Re-audit 2026-07-27 — why, and what was proven

The original certificate was stamped at `dfdfa1b`. Twelve commits landed after it (`5013eac` … `6539959`),
including the WR-02 cancel-race fix, the WR-03 migration of **all four** WhatsApp `get/status` parses,
new `WappiStatusParser` readers (code / profile_id / QR base64 / status), the `ExtractDetail` migration,
and the eight Info fixes. Those touch the trust boundaries this register covers, so the certificate was
re-verified rather than assumed to carry forward.

All 34 prior dispositions **remain CLOSED**; one new accept-class row (T-11-11-01) was added. Evidence
was produced empirically, not by inspection alone:

- **Destructive delete path** (`CheckWhatsappUnauthorizationOutsideApp` — deletes the Wappi profile,
  clears the number, sets `isOnWhatsapp=0`): the shipped `WappiStatusParser` was compiled against the
  project's real Newtonsoft 13.0.4 and the exact caller predicate run over **28 adversarial bodies**.
  The delete fires on **exactly 5**, all definitively-parsed `authorized:false` (bool, bool-parseable
  string, pretty-printed, duplicate-key last-wins). `null`, `0`, `1`, `"yes"`, `""`, `{}`, `[]`,
  nested-only `account.authorized`, wrong-case, array root, malformed, empty and C# `null` **never**
  delete. A 5000-deep nested body returns false without crashing (Newtonsoft's `MaxDepth=64` throws,
  swallowed by `TryParse`) — no stack-overflow DoS from a hostile status body.
- **Base64 decode** (`TryGetQrPng`): 16 payloads, **no exception escaped**. Worst case is `false` (loop
  retries) or garbage bytes rejected by the existing `LoadImage` check. Allocation is proportional (3/4),
  not amplifying. Strictly better than the old unguarded path, which threw and killed the coroutine.
- **Auth REQUEST invariant** — proven by **line-level diff**, not by counting (a count can hold while a
  line changes): all **15** Wappi request-construction lines (`auth/code`, `auth/2fa`, `auth/phone`,
  `auth/qr`, `qr/get`, `get/status`, `profile/add|delete|logout`) are **byte-identical** to `dfdfa1b`.
  `GetChild(3/4/5)` is 21 vs 21 with **exactly one** line differing — the pairing-code label's RHS
  (`Substring(startIndex, 9)` → `pairingCode`). Scene child order re-parsed: auth pages still last.
- **Cancel race**: `isCreatingBot` is cleared at exactly two sites, so the new post-loop re-checks can
  only follow `CancelBotCreation`, which already deletes the profiles and settles the ledger — no
  stranded profile, no bot persisting against a deleted profile.
- **Dwell input block**: scene-verified that both auth back buttons live inside their auth-page subtrees
  with no `ignoreParentGroups` CanvasGroup and no non-`Selectable` pointer handlers, so
  `authCg.interactable = false` genuinely blocks them; the restore always runs.

**Test coverage added as a result** (`WappiStatusParserTests`): the re-audit noted the 42 existing tests
did **not** cover `authorized: null` / `0` / non-parseable strings / nested-only — the exact inputs whose
safety the destructive path depends on. 14 cases now lock that property in, so a future refactor of
`TryGetAuthorized` cannot silently start deleting authorized users' profiles. Suite **1260/1260 green**
(cold headless, compiled from source).

### Advisories from the re-audit (non-blocking, below `block_on: high`)

1. **`ExtractDetail` widens server text reaching TMP labels.** More error bodies now render than under the
   old token-bounded scrape; server-controlled text into a rich-text-enabled `TextMeshProUGUI` is a
   theoretical tag-injection/spoofing surface. Pre-existing in kind; source is wappi.pro over TLS with an
   `Authorization` header. Info-level.
2. **`{bot}WhatsappNumber` self-heal gap** — the Telegram number is sanitised through `IsPlausiblePhone`
   on load, WhatsApp's is not, so a legacy `+`-prefixed value could sit permanently dirty against the new
   normalised read. Latent only; both documented samples are bare digits. Also recorded in `11-REVIEW-FIX.md`.

**Load-bearing evidence — auth flow byte-identical through Phase 11:** `git blame` on every auth-critical line in `Manager.cs` shows all predate Phase 11's first commit (2026-07-17): `auth/code` GET (line 2045, 2026-04-13), `auth/code` POST (2540, 2026-04-07), `auth/2fa` POST (2660, 2026-07-12 Phase 8), `auth/phone` POST (2435, pre-11), and the `WhatsappCodePanel.GetChild(3/4/5)` state toggles (1823-1825/2079-2081/2091, 2026-04-13/15). The onboarding trust cards, relocated success overlay, and first-reply latch are all additive and never touch these lines.

**Advisory (non-blocking, outside the threat register):** code review WR-02 — `CreateBotFromForm` lacks an `isCreatingBot` re-check after the auth-poll gap, so a back-press in that ~0.5s window can delete the Wappi profile yet still create the bot. Pre-existing functional cancel-race, not a Phase 11 regression and not a security trust-boundary breach; tracked in `11-REVIEW.md`, fixable via `/gsd-code-review-fix 11`.

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter
