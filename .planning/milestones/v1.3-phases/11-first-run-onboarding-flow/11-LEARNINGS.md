---
phase: 11
phase_name: "first-run-onboarding-flow"
project: "Automation — WhatsApp/Telegram AI Bot Manager"
generated: "2026-07-28"
counts:
  decisions: 8
  lessons: 8
  patterns: 8
  surprises: 7
missing_artifacts: []
---

# Phase 11 Learnings: First-Run Onboarding Flow

**The one-line story:** plans 01–06 took ~1h43m of autonomous authoring; the phase then ran from
2026-07-17 to 2026-07-28 through two UAT rounds, a gap round, a code review, four fix passes and a
security re-audit. The authoring was never the work.

Extracted 2026-07-28 from the archived artifacts by a different session than the one that executed
the phase. Deduplicated: each event is owned by ONE section and cross-referenced from the others.

---

## Decisions

### D2 — the success moment became a Canvas-level standalone overlay
The «Бот подключён!» sheet was first built as a child of each auth screen's nested `SuccessOverlay`
panel, and `ShowInteractiveSuccessMoment` even called `authPage.SetActive(true)` to un-hide its host.
Round-1 UAT found it rendering *stacked on* the still-visible code-entry UI. It was relocated to a new
full-screen overlay built as the LAST direct child of the ROOT Canvas — a sibling of `ScreenContainer`
— with both auth hierarchies deactivated up front. Ten per-channel `waSuccess*`/`tgSuccess*` fields
collapsed to one channel-agnostic set of six.

**Rationale:** `NavRestructureBuilder.ReorderScreens` pins the auth pages LAST inside `ScreenContainer`,
so the only thing that can render above them is a Canvas-level sibling. See PATTERN 4 for the reusable
rule and SURPRISE 1 for the non-obvious part.
**Source:** 11-HUMAN-UAT.md Round 1 / D2; 11-08-PLAN.md; commits fc7a55e / 7808aa0 / a4fba79

### Checklist rows 2 and 3 became milestone latches — overturning a locked decision
`11-CONTEXT.md` locked *"step states always derived live from facts, never stored per-step"*, enforced
as threat T-11-06-01. The owner overturned it for rows 2–3 after seeing the checklist running:
`FirstStepsChecklist.Milestone(latched, liveFact) => latched || liveFact`, two new global keys, and a
`FirstStepsCard.LatchedFact` helper. The same commit made "connected" toggle-independent — ANY authed
profile counts.

**Rationale:** non-regression beat freshness. Turning a messenger off or deleting a price-list file
must not un-check a step the user genuinely completed; the channel toggles mean "use this channel",
not "connected". Cost: the checklist is no longer a pure mirror of reality.
**Source:** 11-CONTEXT.md locked decisions; commit 5bda504; 11-SECURITY.md post-approval additions

### Carousel paging shipped as a new `OnboardingPager`, not the locked `SnappyFlickScrollRect`
`11-CONTEXT.md` locked reuse of `SnappyFlickScrollRect`. Research read the class and found it is a
*vertical* flick-momentum enhancer with no paging at all, and flagged the locked decision as a verified
contradiction. `OnboardingPager` is a `ScrollRect` subclass locking horizontal-only / `Clamped` /
inertia-off, tweening to `OnboardingPageMath.NearestPage` over 0.3s on `IEndDrag`.

**Rationale:** reusing the named class would have shipped a carousel resting half-way between slides.
Kept a `ScrollRect` subclass so the builder could drop it in as the viewport's scroll component; all
arithmetic stayed in a unit-tested pure class.
**Source:** 11-RESEARCH.md Pitfall 1 + Alternatives; 11-02-SUMMARY.md

### Trust cards appended as the LAST child of each auth code panel
`Manager` addresses the WhatsApp code panel by hardcoded `GetChild(3)/(4)/(5)` (and `GetChild(3)` for
Telegram). The builder injects «Это безопасно» with `DestroyAllByName` + `SetAsLastSibling` under panels
that already use a `VerticalLayoutGroup`, so the card lands visually at the bottom AND no pre-existing
sibling index moves.

**Rationale:** inserting at any other index would silently break the live auth flow. Zero edits to
`Manager.cs`. The `GetChild == 21` grep became the phase's standing auth-regression tripwire — see
PATTERN 10 for why a count alone was not enough.
**Source:** 11-RESEARCH.md Pitfall 2; 11-05-SUMMARY.md; 11-VERIFICATION.md truth 2

### Owner-locked UX trio: no «Пропустить», no QR, price-list ask strictly after auth
The carousel has no skip; the QR path is not offered during onboarding; the price-list request comes
only after the channel is authed.

**Rationale:** a first-run flow that can be skipped is not a first-run flow; QR is the higher-friction
path for a phone-first audience; asking for a price list before there is a working bot inverts the
value order. Locked by the owner before planning, and never revisited.
**Source:** 11-CONTEXT.md locked decisions

### Row 2 went channel-neutral, deleting the `ChannelLabel` derivation
`ChannelLabel` (with WhatsApp deliberately winning the dual case) shipped in 11-01 with 4 EditMode
tests. Owner polish replaced it with the static «Подключить мессенджер» and deleted the derivation and
its tests as dead code; a scene rebuild followed so the seeded label matched.

**Rationale:** naming one channel is wrong for a two-channel product and ambiguous for a both-channel
bot — the tiebreak was a guess with no user-facing justification.
**Source:** commits 84f38d0 + 96f6ee1

### Three review findings accepted rather than fixed, with preconditions recorded
Rather than fixing everything, three findings were dispositioned as accepted with the conditions under
which they would become real — including the success overlay having **no Android hardware-back escape**
(its only exits are two buttons). Android is the primary target, so this is a live constraint for
whoever adds a back handler.

**Rationale:** an accepted finding with written preconditions is cheaper than a speculative fix and
survives as a warning. See OPEN ITEMS.
**Source:** 11-REVIEW.md IN-03; 11-REVIEW-FIX.md

### The security certificate was re-audited at HEAD rather than carried forward
Instead of trusting the audit stamped 12 commits earlier, the phase re-ran it at HEAD — and that
re-audit found the crash fix had *widened* a destructive path (SURPRISE 5).

**Rationale:** a certificate describes the code as of its stamp, not as of now. This decision is the
single most transferable thing in the phase — and REPEAT 1 shows the sibling verification certificate
did **not** get the same treatment and is stale today.
**Source:** 11-SECURITY.md re-audit; commit 36a43e6

---

## Lessons

### An overlay parented inside a screen inherits that screen's activation and teardown
A child cannot cover its own ancestors' siblings. Any celebration/modal that must sit above everything
has to be a Canvas-level sibling. **Context:** this is the mechanism behind D2 — and the fix the
research itself prescribed is what produced the defect (SURPRISE 1).
**Source:** 11-HUMAN-UAT.md Round 1 / D2; 11-08-SUMMARY.md

### The phase's zero-regression proof was grep COUNTS — and a count can hold while a line changes
`GetChild == 21` and `auth/code + auth/2fa == 7` were the standing auth tripwires. A count is invariant
under substitution: 21 can still be 21 while one line's right-hand side differs. **Context:** cheap
tripwires are the right default, but the moment you actually edit near the guarded flow, you owe a
line-level diff. See PATTERN 10.
**Source:** 11-VERIFICATION.md truth 2; 11-REVIEW-FIX.md

### `OnEnable`-driven refresh is permanently stale when the trigger lives in an overlay
The checklist only refreshed in `OnEnable`, but `AddBotPanel` is an **overlay** — `Screen_Bots` never
disables, so it never re-enables after a bot is created. Rows stayed unchecked until you tapped a row
and navigated away and back. **Context:** D3. Fixed with five fire-and-forget `RefreshFromFacts()` hooks
at the real mutation points. Overlay-based navigation silently breaks every lifecycle-driven refresh.
**Source:** 11-HUMAN-UAT.md Round 1 / D3; 11-09-SUMMARY.md

### "Can it throw?" is the wrong bar for a hand-rolled response parse — "what does a mis-parse DO?" is
The review named 2 unsafe `get/status` parses. The fix pass ran its own adversarial sweep and found
**4**, including one the review missed — and the missed one was the **destructive** one (it gates a
profile delete). **Context:** ranking by throw-probability finds crashes; ranking by consequence finds
the ones that silently destroy data. See PATTERN 9.
**Source:** 11-REVIEW.md WR-03; 11-REVIEW-FIX.md; commit 28f3a84

### Two UI covers were silently doing input-blocking duty
Removing/relocating them re-opened live input windows — a UI change re-opened a *functional* race that
an animation had been incidentally hiding. **Context:** always ask what a removed visual element was
*also* guaranteeing. This is the phase's instance of the project-wide "one layer too shallow" pattern
(REPEAT 3).
**Source:** 11-REVIEW.md WR-02; 11-REVIEW-FIX.md; commit c4ea246

### "Full-screen" is not screen-sized — `ScreenContainer` is inset 208 at the bottom for the nav
The first attempt made the carousel full-screen by *hiding the bottom nav*; that was reverted in favour
of oversizing the screen to cover it. **Context:** the inset is structural, so anything that must
genuinely fill the display has to account for it rather than hide the chrome.
**Source:** 11-RESEARCH.md; commits (nav-hide revert)

### Verifying a scene payload needs escaped-unicode probes, fold normalization and `m_Children`
Unity's scene-YAML encoding silently defeats the obvious greps: Cyrillic strings are escaped, long
values fold across lines, and parent/child structure lives in `m_Children`/`m_Father` rather than
indentation. A naive `grep "Подключить"` returns nothing on a scene that contains it. **Context:** every
scene-verification step in this phase had to be written against the encoded form. See PATTERN 12.
**Source:** 11-03/11-08-SUMMARY.md verification sections

### Absolute suite-count targets go stale mid-phase; gate on the delta
An approved plan specified an acceptance criterion of a test count that had become **unreachable**
because a parallel milestone moved the baseline. The corrected baseline was ~1118 → **1136**; 1165 was
the *post-11-01* count, not the starting point. **Context:** with parallel sessions adding tests, "suite
must equal N" is a promise about someone else's work. Gate on "+N new tests, 0 failures".
**Source:** 11-01-SUMMARY.md; 11-04-SUMMARY.md

---

## Patterns

### 4. Celebration/modal as a Canvas-level standalone overlay
Build it as the LAST direct child of the ROOT Canvas, sibling to `ScreenContainer`, and deactivate the
underlying hierarchies explicitly. **When:** anything that must cover screens which are themselves
pinned last in the container (here: the auth pages). **When not:** transient in-screen affordances that
*should* die with their host — the 2s `moreAuthSteps` transient deliberately stayed nested.
**Caution:** two pre-existing GameObjects were already named `SuccessOverlay` — see SURPRISE 7.

### 7. Live-mirror UI: static `Instance` + public `RefreshFromFacts()` + `CanvasGroup` hide
Never let such a view `SetActive(false)` its own root — a self-deactivated root can never be re-shown by
an external hook. Hide by `CanvasGroup` (alpha + `blocksRaycasts` + `interactable`), cache it in
`Awake`, and route every hide reason through ONE predicate at the top of `Refresh()`.
**When:** any always-present card whose visibility is derived from data that changes elsewhere.
**Gotcha:** C#'s `?.` bypasses Unity's null-equality overload, so a static `Instance` must be cleared in
`OnDestroy` or `Instance?.` will call into a destroyed object.

### 8. Latch at the event SOURCE, not in a view that can be inactive when the event fires
The first-reply latch was fixed once exactly as the review prescribed — and was **still dead**, because
the observer's active window and the emitter's fire window never overlap. Moving the latch to the event
source fixed it. **When:** any "has X ever happened?" flag. **Design-time test:** *can the observer be
inactive at the moment the emitter fires?* If yes, the observer cannot own the latch.

### 9. Bounds-checked parser seams for external JSON — migrate sites by consequence
Replace hand-rolled `Substring`/`IndexOf` scans of a server body with a tested seam
(`WappiStatusParser`, `ExtractDetail`). Rank migration by **what a wrong answer does**, not by whether
the line can throw. **When:** any parse of a response you do not control. **Scope reached here:** ~14
hand-rolled scrapes in one file, ending with zero remaining in `Manager.cs`.

### 10. Cheap grep tripwires + a line-level diff when you edit near the guarded flow
Standing counts (`GetChild == 21`) are a good always-on guard. They are necessary but not sufficient:
prove the actual lines when a change lands nearby.

### 6. Derived-state checklist: persist only terminal milestones — and ask whether each fact can regress
Derive live by default; latch a step ONLY where the underlying fact can legitimately go back to false
(a channel toggled off, a file deleted) without the user having un-done the accomplishment.
**When:** any progress checklist over mutable state.

### 1. Idempotent `[MenuItem]` + `BuildHeadless` scene builder
The project's UI-construction pattern: a builder that tears down and rebuilds deterministically, is
callable headlessly, and stamps `[SerializeField]`s via `SerializedObject`. **Teardown must be
direct-children-only** and the root canvas resolved explicitly. Cross-wave contract: a later plan's
builder stamps the null-guarded fields an earlier plan's component declares (`pageCount`,
`OnPageChanged`).
**Unity gotcha:** `ScrollRect` ships a custom editor (`ScrollRectEditor`) that renders only its own
properties, so subclass fields are invisible in the Inspector — `OnboardingPager` needed its own
`OnboardingPagerEditor.cs` to expose `pageCount`.

### 11. Gap round: one plan per UAT defect, closed by an append-only Round-2 addendum
Round-1 defects were logged as screen + expected-vs-actual + severity, which seeded
`/gsd-plan-phase 11 --gaps` into three waved plans (11-08 wave 1; 11-09 wave 2 `depends_on: [11-08]`
because its hooks anchor on post-11-08 method bodies; 11-10 doc-only). Round 2 then re-tested **13**
items rather than re-running all 36. **When:** any UAT round that finds more than one defect.

---

## Surprises

### 1. The research's own mitigation is what produced defect D2
The nested-panel construction that failed was not an oversight — it was the prescribed approach. The
mitigation for one hazard created another. **Impact:** the single highest-severity defect of the phase,
found only when a human looked at the running app.

### 2. The deleted 2-second success panel was load-bearing in three undocumented ways
Removing it took out an unrelated UX affordance (the in-box checkmark) and input-blocking behavior;
restoring it took a three-commit chain in which each fix created the next defect. **Impact:** three
extra commits and a re-verification cycle for what looked like a deletion.

### 3. The first-reply latch was unreachable by construction — after two wrong diagnoses
It was diagnosed and "fixed" twice before the real cause (disjoint active windows) was found.
**Impact:** two fix passes spent on the symptom. Became PATTERN 8.

### 4. The Wappi parse defect had 4 sites, not 2 — and the missed one was the destructive one
**Impact:** had the fix pass stopped at the review's list, the destructive path would have kept its
unsafe parse. None of this was Phase-11 code: blame evidence shows every auth-critical line predates
2026-07-17. A first-run-onboarding UI phase's review surfaced and closed a latent crash class in the
auth layer it had promised not to touch.

### 5. The re-audit at HEAD found the crash fix had WIDENED a destructive path
And the parser's existing 42 tests did **not** cover the inputs that path's safety depends on
(`authorized: null/0/"yes"`, nested-only). **Impact:** the phase's strongest argument for re-auditing at
HEAD instead of carrying a certificate forward — and the direct ancestor of Phase 10's R-10-02
amendment (REPEAT 1).

### 6. A second advisory: `ExtractDetail` widens server text into rich-text TMP labels
More server-controlled error text now renders than under the old token-bounded scrape, into a
rich-text-enabled `TextMeshProUGUI` — a theoretical tag-injection/spoofing surface, Info-level.
**Impact:** low today; worth knowing that a hardening pass *widened* an output surface while narrowing
a parse.

### 7. Two pre-existing GameObjects were already named `SuccessOverlay`
A naive idempotent `DestroyAllByName` teardown would have deleted them. The 11-08 greps were tightened
**before** execution (`d509f2d`) precisely because they would otherwise have matched the pre-existing
same-named nodes. **Impact:** avoided — but only because someone checked the name space first. This is
why PATTERN 1's teardown is direct-children-only.

---

## Repeats — project-level failure modes (vs `10-LEARNINGS.md`)

These recurred across two consecutive phases. A recurrence is worth more than any one-off.

1. **A closed certificate is invalidated by later commits.** Phase 10: `R-10-02`'s accepted-risk
   rationale ("no retry, no loop") was falsified by a later retry fix. Phase 11: the security cert was
   caught by re-audit — but `11-VERIFICATION.md` was **not**, and is stale today (see OPEN ITEMS).
   **Countermeasure:** on any post-close commit, re-open every certificate whose evidence names the
   changed code — not only SECURITY.
2. **A gate passes while the property it exists to protect goes untested.** Phase 10: four verification
   layers, none asserted the composed `combinedText`. Phase 11: grep counts held while a line changed;
   42 parser tests missed exactly the inputs the destructive path depends on; the test bridge's
   freshness gate false-passed against a stale summary. **Countermeasure:** name the property, then
   check that the check bites.
3. **The first fix targets the symptom because the diagnosis is one layer too shallow.** Phase 10 needed
   three chained content fixes; Phase 11 fixed the first-reply latch as prescribed and it stayed dead,
   and had a three-commit chain where each fix caused the next. **Countermeasure:** ask "what did the
   thing I removed or patched *also* guarantee?"
4. **"Code-complete" means nothing has executed.** Phase 10: a live gate for phase N is often the first
   real execution of phase N−1's work. Phase 11: ~1h43m of authoring, then eleven days of gates.
   **Countermeasure:** treat the first human run as the start of the work, not the end.

**Correction to the received story:** Round 1 was run in the **Unity Editor Game view at 1080×2400**,
not on a device build. That matters — the three defects were reachable in ten minutes by any executor
session, so the missing step was cheap and skippable, not expensive. Through 11-04 and 11-05 the phase
had 1165/1165 green, every self-check passing and every scene stamp verified — **and no session had
ever run the app.**

---

## Open items this extraction surfaced

1. **`11-VERIFICATION.md` is stale at HEAD** — it asserts row 2's label comes from
   `FirstStepsChecklist.ChannelLabel` (deleted; `grep -rn ChannelLabel Assets/` returns nothing) and
   that `OnboardingKeys.cs` holds 3 key constants (it holds 6). Stamped `2026-07-23T18:00:00Z`;
   invalidated by `84f38d0` (19:20) and `5bda504` (20:01) the same evening. `v1.3-ROADMAP.md` success
   criterion 4 is stale for the same reason. *Amended 2026-07-28 — see the amendment note in that file.*
2. **A "Claude's Discretion" item vanished without a disposition** — whether the carousel re-entry from
   «О приложении» ships was listed in `11-CONTEXT.md` and `11-RESEARCH.md` and never resolved anywhere.
   No code implements it. Discretion items need an explicit "dropped" line or they leave no trace.
3. **The success overlay has no Android hardware-back escape** — accepted per design, but Android is the
   primary target and a full-screen modal with only two button exits is a live constraint.
4. **T-11-11-01's preconditions** — recorded as accepted-with-conditions; those conditions were never
   re-checked after the later commits.
5. **`{bot}WhatsappNumber` self-heal gap** — noted as a security advisory; bot activation state lives
   under TWO PlayerPrefs keys and delete only cleaned one.
6. **`11-08-SUMMARY.md` ends with literal `</content>` / `</invoke>` write-tool tags** — the identical
   corruption `11-07-SUMMARY.md` records having stripped from `11-HUMAN-UAT.md`. Second occurrence,
   uncaught. Cosmetic, but it means the stripping fix did not generalize.

**Audit note for anyone tracing what shipped:** every Phase-11 fix commit exists **twice** in history —
identical subject and author-date, different hashes (`0c4eb02`≡`627f494`, `6539959`≡`6df88a3`,
`28f3a84`≡`ddce7f0`, `381c3cf`≡`8a40b8a`, `2cd53a2`≡`703533c`, `ea0e248`≡`a8ee5f1`, `c4ea246`≡`daa464b`,
`36e846e`≡`8f12dbb`). This is **not** a double-application: the executing session was mirroring its
commits onto `main` with `commit-tree` + `update-ref` while the branch also carried them, so each
logical fix was built twice onto two different parents. Diagnosed 2026-07-28; both lineages were merged
and the duplicate history is now joined under one `main`. The hashes the artifacts cite are one of each
pair.
