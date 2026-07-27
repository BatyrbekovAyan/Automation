---
phase: 10
phase_name: "message-batching-debounce"
project: "Automation"
generated: "2026-07-27"
counts:
  decisions: 7
  lessons: 8
  patterns: 7
  surprises: 8
missing_artifacts: []
---

# Phase 10 Learnings: message-batching-debounce

> Extracted from all 16 phase artifacts plus the phase's git history. Items are merged across
> categories (the same event usually taught a decision, a lesson and a surprise — it is recorded
> once, under the lens that owns it). Where a claim was verified directly against code or the dev
> n8n database during extraction, that is stated.

**One-line phase story:** the two autonomous authoring plans took 9 and 15 minutes; everything
else — two owner gates, two cross-phase repairs, a code review, two fix passes, and three
post-UAT content fixes — spanned 2026-07-21 → 2026-07-27. The authoring was never the work.

---

## Decisions

### Debounce is a NEW pre-generation Wait, never a repurposed humanizer pause

The AI Agent generates BEFORE the humanizer pauses run, so any fragment that reaches those pauses
has already produced a reply. Phase 10 therefore splices a new `Debounce Wait` (8s) onto the
`Suppressed?` FALSE branch (`main[1]`), ahead of `Input type`/AI Agent, in both bot templates.

**Rationale:** repurposing an existing pause would debounce *after* generation — too late to
coalesce anything. The accepted trade-off is that every auto-reply, including a single complete
message, now waits the full window; `10-HUMAN-UAT.md` explicitly instructs the owner not to log
that latency as a defect.
**Source:** 10-CONTEXT.md (locked decisions), 10-RESEARCH.md (alternatives considered)

---

### The Phase-9 suppression gate stays BEFORE the debounce — the staleness race is refused, not fixed

Pipeline order is locked: `group If → Read Reply Mode → Suppressed? → Debounce Wait → Fetch Recent
→ Latest+Combine → Is Latest? → Input type`. Review finding IN-04 observed that reading the reply-mode
flag pre-wait means an owner who flips a chat to «Вместе» mid-window still gets that one fragment
auto-replied. The fix pass deliberately refused it (`1e12e94`).

**Rationale:** gate-before-debounce is the mitigation for threat T-10-01-03 (a debounce spliced
*before* the gate would be an outright suppression bypass), it is asserted structurally by
`verify-message-batching.py`, and it was proven behaviorally at UAT scenario 5. "Fixing" the
one-message-deep race would invalidate both the verifier evidence and a closed security audit.
If ever revisited, the only sanctioned shape is an ADDITIONAL re-read on the winner branch — never
a moved gate.
**Source:** 10-CONTEXT.md `<deferred>`, 10-REVIEW.md IN-04, 10-SECURITY.md T-10-01-03

---

### One `messages/get` fetch drives both the dedupe and the combine — limit-only, no `mark_all`, time-sorted

`Fetch Recent` carries only `profile_id`, `chat_id`, `limit=15`, reusing the existing Wappi
credential. Its single response feeds both `abort = newestIncoming.id !== triggeringId` and the
combine walk, sorted by `time` descending rather than trusting array order.

**Rationale:** copying `Mark Read`'s `mark_all=true` would mark the chat read *during* the wait,
before the bot has decided to reply — defeating the deliberate downstream `Mark Read` (threats
T-10-01-02 / T-10-03-03). Wappi's default ordering is undocumented per channel, and a wrong
"newest" breaks both the abort decision and the combine boundary.
**Source:** 10-01-SUMMARY.md, 10-RESEARCH.md Pitfalls 3 and 5

---

### `Latest+Combine` re-emits the webhook body rather than repointing downstream nodes

The Code node returns `{ ...$('Webhook').first().json, abort, combinedText, foreignFetched }`.

**Rationale:** the inserted HTTP fetch + Code node would otherwise replace the item and `$json.body`
would evaporate — but `Input type` reads `$json.body.messages[0].type` and `Download Audio` reads
`.file_link`, so every text message would misroute to the `Ask to Send Text` fallback. Re-emitting is
one edit instead of many, keeps the designed nodes untouched, and sidesteps fragile paired-item
resolution across Wait + HTTP + Code.
**Source:** 10-RESEARCH.md Pitfall 1, 10-01-SUMMARY.md

---

### Two single tunable windows — 8s server / 2.5s client — both survived their live gates unchanged

`DEBOUNCE_SECONDS = 8` and `IncomingDebounceGate.WindowSeconds = 2.5f` are the only knobs.

**Rationale:** sub-65s is a hard n8n constraint, not a preference — a Wait under 65s resumes in
memory with no DB offload, making one cheap waiting execution per fragment viable. Retuning is a
one-line change that re-enters via a small gap plan, not through the UAT gate. Note the coupling:
pushing the server window ≥65s flips n8n to webhook-resume + DB offload, which is exactly why IN-03
later added a `webhookId`.
**Source:** 10-01-SUMMARY.md, 10-03-SUMMARY.md, 10-HUMAN-UAT.md scenarios 2/4

---

### Two review findings were accepted rather than fixed, each with a named blocker

**WR-02** (mixed-type bursts permanently drop the losing fragment — voice+text, text+image) is
accepted v1 scope: the locked scope is text-only, and the proposed fix is a behavioral redesign
that would invalidate the passed runData and UAT evidence. **IN-05** (Telegram's losing burst
fragments stay unread) is deferred to the prod pass: tapi's `mark/read` has no `mark_all`, the
alternative lever is an unprobed open question in `Tools/tapi/SHAPES.md`, and attaching it to
`Fetch Recent` is forbidden by *two* existing verifiers holding competing invariants on that node.

**Rationale:** both are recorded in `10-CONTEXT.md` `<deferred>` with the blocker named, so a future
reader does not mistake them for oversights — or "fix" them into a regression.
**Source:** 10-REVIEW.md WR-02/IN-05, 10-REVIEW-FIX.md, 10-CONTEXT.md `<deferred>`

---

### Close the UAT gate as `partial` with tracked debt rather than marking unobserved scenarios passed

At the 2026-07-22 close, scenarios 1–3 passed on device while scenario 4 was blocked by an open
Phase-9 gate and scenario 5 was deferred by owner decision. Both were written into STATE.md as
`uat_gap` rows and the verification passed with two explicit frontmatter overrides.

**Rationale:** the debt stayed visible in `/gsd-progress` and `/gsd-audit-uat` until genuinely
re-verified on 2026-07-27 — at which point scenario 4 immediately surfaced three real defects.
Marking them passed would have buried exactly the work that mattered.
**Source:** 10-04-SUMMARY.md, 10-HUMAN-UAT.md, 10-VERIFICATION.md frontmatter

---

## Lessons

### Four verification layers ran, and none asserted the composed text — the phase's defining failure

`verify-message-batching.py` (shape), the 10-03 runData matrix (branch behavior), 10-VERIFICATION.md
(artifact + link tracing) and the 10-04 device UAT (user-visible shape) all passed. Every functional
defect the phase produced was nevertheless *"the right branch ran, carrying the wrong text"*:
WR-01 (winner branch, empty prompt), `da9d476` (right request, prompt anchored on one fragment),
`da884dd` (right fire, a fragment present in neither history nor tail), `14b049f` (right second
fire, first fragment gone). The runData matrix even had `combinedText` in front of it and only ever
checked id-equality and abort.

**Context:** when a phase's whole job is *composing text*, at least one gate must assert the composed
string itself — the matrix should require `combinedText` to be non-null and to contain every fragment
sent, and the «Вместе» scenario should require `lastIncomingText` to carry all burst lines. That single
assertion would have caught all four defects.
**Source:** 10-REVIEW.md, 10-HUMAN-UAT.md resolution addendum, 10-VERIFICATION.md, commits da9d476/da884dd/14b049f

---

### The suggestions payload's history snapshot re-syncs on chat FETCH, not on live poll

During a burst, newly-arrived fragments are not yet in the payload's message history. The only
carrier was the last-wins `lastIncomingText` field — so a fragment could be in *neither* place and
vanish silently. Dev execution 1103 lost «бампер на бмв х5» entirely.

**Context:** this is why the first (server-side) fix was insufficient: a server-side run-walk can only
walk what it was sent. The durable fix was client-side accumulation of the whole burst
(`SuggestionsController.AppendBurst`). Any future feature that reasons about "recent messages" from
this payload must check whether it needs live-poll data that the snapshot does not yet contain.
**Source:** 10-HUMAN-UAT.md resolution addendum, commit da884dd

---

### Clearing debounce state after a fire caused a straddle regression — clear on the semantic boundary instead

The first accumulation fix cleared `_pendingIncomingText` after each fire. A burst spanning the 2.5s
window fires twice, so fire #2 carried only the newest fragment and the cards degraded to generic
smalltalk (dev exec 1168). The correct rule: the pending burst *survives* fires (it mirrors the
un-replied run, and the server dedups re-sent lines) and clears on the **outgoing-reply boundary** —
an outgoing echo in the live batch — plus the four lifecycle sites.

**Context:** the timing window and the content boundary are different mechanisms. Treating the timer
as a content boundary is what caused the bug. Note the semantic shift this created: outgoing echoes
went from *ignored* (pre-phase, "Pitfall 7") to *run boundary that also suppresses the poke* — anyone
editing `HandleLive` needs to know that.
**Source:** 10-HUMAN-UAT.md scenario 4 notes, commit 14b049f, SuggestionsController.cs `HandleLive`

---

### Coalescing text on the client does not anchor the prompt on it

Even with the burst correctly delivered, `Suggest Replies`' `Prep` node made the single last fragment
the `queryText` driving *both* the RAG retrieval and the prompt's `lastClientMessage`. The cards
answered only the final fragment while the history in the payload covered the whole burst.

**Context:** a client-side coalesce is only half a feature — the consuming server node must be
changed to match, or the composed text is assembled and then ignored. The fix walks the trailing
client run bounded by the last business reply, mirroring `Latest+Combine`'s semantics so both halves
of the product agree on what "one conversation turn" means.
**Source:** 10-HUMAN-UAT.md resolution addendum, commit da9d476

---

### "Idempotent by-node-name" with *guarded adds* silently no-ops every later spec edit

`apply-message-batching.py` added each managed node under `if find(nodes, name=...) is None:`. Once
the nodes existed, editing `LATEST_COMBINE_JS` and re-running changed nothing — and both health
signals stayed green (the script exits 0; the verifier passes because the *old* nodes still satisfy
the old asserts). All three iteration-1 review fixes would have been phantom fixes living only in
Python.

**Context:** idempotence must mean *converges to the spec*, not *skips if present*. The fix
(`104ead8`) replaced the guards with a `managed()` upsert that rewrites the four nodes in place,
preserving stable uuid5 ids and positions — still zero-diff on a re-run. **The general rule: after a
migration edit, prove the change materialized in the target artifact; never trust "script updated".**
**Source:** 10-REVIEW-FIX.md, commit 104ead8

---

### A live gate for phase N is often the FIRST real execution of phase N−1's "code-complete" work

Phase 10's runData gate failed at the Phase-9 `Read Reply Mode` node: `relation "reply_mode_flags"
does not exist`. Phase 9's DDL was a still-open owner gate (09-04), and until this redeploy the live
clones were frozen pre-Phase-9 copies, so the gate had never actually run.

**Context:** "code-complete" for a phase with owner-run live gates means *nothing has executed yet*.
When phase N composes with phase N−1's un-gated work, expect to discover N−1's gaps inside N's gate,
and budget for it rather than treating it as a blocker.
**Source:** 10-03-SUMMARY.md deviations #2, .planning/STATE.md

---

### Two of the project's own features interact to make an empty LLM prompt routine (WR-01)

If the newest fetched message is *outgoing*, the combine loop breaks immediately, `parts` stays empty,
and `[].join('\n')` yields `""` — not `null`. The Text node's `??` fallback is nullish-only, so `""`
wins and the AI Agent receives an empty prompt. This is not exotic: the humanizer's own pauses mean a
reply to an earlier message routinely lands *inside* a later fragment's 8s window.

**Context:** whenever a fallback uses `??`, every producer upstream must return `null` and never an
empty string. Two independently-correct features (humanizer pauses, debounce window) composed into a
defect that neither one contains.
**Source:** 10-REVIEW.md WR-01, commit 686e7bf

---

### Moving a client call server-side inherits the client's known platform bugs

The Unity client has long defended against Wappi crossing concurrent same-endpoint `messages/get`
responses (serial-fetch gate + `CrossChatResponseGuard`). `Fetch Recent` issues that same endpoint
from n8n, concurrently, with no such guard — and hardening for it revealed the risk was worse than
reviewed: a foreign row time-ordered *between* two fragments was concatenated straight into the
prompt. An availability concern was also a **confidentiality** one.

**Context:** when relocating an API call across a boundary, port the *defenses* with it, not just the
call. Search the codebase for existing guards on that endpoint before assuming the new caller is safe.
**Source:** 10-REVIEW.md WR-03, commit 201e209, Assets/Scripts/Chat/OpenChatLivePollGate.cs

---

## Patterns

### Managed-node UPSERT for config migrations

Author the migration so each managed node is *rewritten* from spec on every run — preserving only
stable identity (uuid5-derived id) and cosmetics (position) — rather than added-if-absent.

**When to use:** any migration over a long-lived config artifact (n8n workflows, CI configs, IaC)
that will be edited more than once. Not needed for a one-shot data migration. The tell that you need
it: your migration's second run is described as "a no-op" rather than "converges".
**Source:** commit 104ead8, Tools/n8n/apply-message-batching.py

---

### Separate the migration from a structural verifier, and prove every assert BITES

`apply-message-batching.py` writes; `verify-message-batching.py` independently asserts the result
(node presence, wiring, absence of `mark_all`, cross-template identity). Every new assert was proven
by mutating a scratch copy until it exited 1, then deleting the copy.

**When to use:** whenever a generated artifact is deployed somewhere you cannot easily inspect. The
verifier's `--dir` flag also makes it a re-export gate later. An assert that has never failed is an
assert you have not tested — and assert *shape* rather than exact value where an importer may re-mint
the field (as n8n does with `webhookId`).
**Source:** 10-01-SUMMARY.md, 10-REVIEW-FIX.md verification sections

---

### Execution runData introspection as the behavioral gate a structural check cannot reach

Prove branch behavior by reading the platform's own execution records — which nodes ran, what each
emitted — rather than by inspecting configuration. Phase 10 used it to prove that losing fragments
dead-end at `Is Latest?` and that suppressed messages stop at `Suppressed?` before the debounce.

**When to use:** any workflow/pipeline change where "the right branch ran" is the property under test.
**Its limit, learned the hard way:** it proves *which* nodes ran, and will happily pass while the data
flowing through them is wrong — so pair it with at least one content assertion.
**Source:** 10-CONTEXT.md, 10-03-PLAN.md Task 2, 10-03-SUMMARY.md

---

### Pure, stateful, injectable-clock gate as the client-side testability seam

`IncomingDebounceGate` holds a `float` deadline and a `bool` armed flag, takes the clock as a
parameter, and has no `using UnityEngine`. A thin always-running coroutine polls it. This makes "3
rapid pokes → 1 fire after the window" EditMode-provable with no real time.

**When to use:** any time-based UI behavior in Unity. Extend the same seam to **content** decisions,
not just cadence — the later `AppendBurst` accumulator was extracted as a second pure static for
exactly this reason, and got 5 tests. Anything left inside the MonoBehaviour stays untested by
construction.
**Source:** 10-02-SUMMARY.md, 10-PATTERNS.md, IncomingDebounceGate.cs

---

### Lifecycle-symmetric cancel: add the new guard ALONGSIDE the existing one

The debounce cancel had to be added at all four request-context boundaries — chat close, bot switch,
same-bot chat switch, toggle-off — *in addition to* the pre-existing sequence guard, not instead of
it. The same-bot chat switch (`RestoreForActiveChat`) was the load-bearing one: the seq guard catches
a chat-switched *render*, but not a stale text baked into the request payload at fire time.

**When to use:** whenever you add deferred state to a component that already has staleness guards.
Enumerate every path that changes the request's context and verify each one clears the *new* state
too; existing guards were designed for a different failure mode.
**Source:** 10-02-SUMMARY.md, 10-SECURITY.md T-10-02-01

---

### Owner-gated checkpoint plans: accepted-cost note first, copy-pasteable runbook, explicit resume signal

Plans that need the human are marked `autonomous: false` and hand over a numbered runbook with exact
commands, expected outputs, and a named resume word — with an "accepted cost, do not log this as a
defect" note placed *before* the scenarios.

**When to use:** any gate requiring credentials, real devices, or production-adjacent systems. The
accepted-cost note is what stops a deliberate trade-off (here: ~8s added latency on every reply) from
being reported as a bug and re-litigated.
**Source:** 10-03-PLAN.md, 10-04-PLAN.md, 10-HUMAN-UAT.md

---

### A refused review finding gets its own disposition commit naming what a "fix" would invalidate

Skipping a finding is recorded as a commit against the phase's decision record, stating the blocker
and what evidence a "fix" would destroy — not left as a silent omission in the fix report.

**When to use:** every time a review finding is not fixed. The failure mode this prevents is a future
engineer (or agent) "helpfully" fixing IN-04 and silently breaking a closed security mitigation.
**Source:** 10-REVIEW-FIX.md skipped issues, 10-CONTEXT.md `<deferred>`, commits 1e12e94/60fb569/2bcc1e3

---

## Surprises

### The phase's own redeploy broke a *different* workflow family — invisible to every JSON check

n8n 2.27.4 stamps `"binaryMode":"separate"` into a workflow's stored settings on save. The 10-03
template redeploy therefore mutated the stored settings; the Create/Edit orchestrators pass settings
through verbatim into the write API, which rejects the unknown property — so **bot creation started
failing with HTTP 400** while every canonical JSON check stayed green.

**Impact:** discovered only because the owner tried to create a test bot. Cost a mid-gate deviation
(`d594f17`, a two-mode `--canonical`/`--live` fixer) and revealed that the live dev copies and the
canonical exports legitimately diverge (localhost vs prod URLs), so the live repair had to be
surgical rather than a canonical re-import. A platform's own save behavior is part of your contract.
**Source:** 10-03-SUMMARY.md deviations #1, dev n8n executions 831/832

---

### Verification passed 5/5 with two overrides — and the overridden half was hiding three real defects

The 2026-07-22 verification passed with explicit overrides for the two unobserved UAT scenarios,
reasoning that BATCH-03 retained full EditMode coverage. When scenario 4 was finally observed on
2026-07-27, it surfaced three distinct content defects in a row.

**Impact:** the overrides were honestly recorded and correctly tracked as debt — that process worked.
What did not work was the inference *"automated coverage stands, so the unobserved half is probably
fine."* The coverage was real but tested cadence, not content. Treat "covered by tests" as a claim
about a specific property, and name that property before accepting it as a substitute for observation.
**Source:** 10-VERIFICATION.md frontmatter overrides, 10-HUMAN-UAT.md resolution addendum

---

### The phase was closed three times and the running artifact still moved afterwards

Sequence: UAT resolved 5/5 and "zero outstanding debt" (`772adab`) → then a code review that had run
five days earlier landed three behavior-changing fixes into the deployed reply path (WR-01/03/04) →
then a second `--all` pass landed five more, including a `webhookId` on the Wait node.

**Impact:** the runData matrix, VERIFICATION, SECURITY audit and UAT all describe a template that had
since changed. The gap was closed only because the owner redeployed and re-created test bots, and
that smoke run was verified live (dev execs 1323–1330: `abort=false`, `foreignFetched=0`, and
`Mark Read`/`Typing`/`Chat Memory`/send all resolving — plus suppressed execs 1329/1330 dead-ending
correctly). **That verification exists only in the session transcript, not in any artifact** — which
is itself the lesson. Countermeasure: any post-close change to a deployed template should re-open the
smallest live check covering it (one burst per channel), and the result should land in the record.
**Source:** commit order vs artifact dates; dev n8n execution data 1323–1330 (verified during extraction)

---

### A closed accepted-risk's rationale was silently invalidated by a later fix

`10-SECURITY.md` R-10-02 accepts the rapid-fragment-flood DoS *explicitly because* "each aborts after
a single `Fetch Recent` call (**no retry, no loop**)". Commit `201e209` then added
`retryOnFail: true, maxTries: 3, waitBetweenTries: 1000` to that exact node.

**Impact:** a flood can now cost up to 3× the Wappi calls per fragment, and the audit that closed
`threats_open: 0` on 2026-07-22 was never revisited. The risk is still plausibly acceptable
(dev-only, prod dormant) — but its *stated reasoning* is now false, which is worse than an open risk
because it reads as settled. **Accepted risks need re-checking whenever the code their rationale
cites is changed.** Verified during extraction by direct comparison of R-10-02's text against
`apply-message-batching.py`.
**Source:** 10-SECURITY.md R-10-02 vs commit 201e209

---

### Evidence that looked like a defect was the mechanism working correctly

Scenario A's winner produced a 4-line `combinedText` — the two fragments *repeated*. It looked like
per-message duplication. It was the run-walk correctly spanning an earlier, un-replied round: a prior
attempt had died before replying, so those fragments were still un-answered and legitimately belonged
to the run. Scenario B's clean single line immediately after confirmed the boundary resets once a
reply lands.

**Impact:** nearly logged as a regression. When a combine/aggregation feature produces "too much",
check what the *boundary* signal says before assuming duplication — and record the explanation in the
artifact so the next reader does not re-raise it.
**Source:** 10-03-SUMMARY.md analysis note

---

### Batching collapsed N executions into 1 and exposed a latent WhatsApp/Telegram asymmetry

Pre-splice, every fragment ran its own execution and got its own `Mark Read`. Post-splice only the
winner reaches it. WhatsApp's `mark_all: true` sweeps the whole burst; Telegram marks only the winning
message id — so losing TG fragments stay unread.

**Impact:** cosmetic (read receipts), but it had no safe fix: tapi has no `mark_all`, and the obvious
alternative placement is forbidden by two existing verifiers. A change that *reduces* executions can
surface per-channel asymmetries that were previously masked by repetition.
**Source:** 10-REVIEW.md IN-05, 10-REVIEW-FIX.md

---

### Importing a Wait node without a `webhookId` makes n8n mint one server-side

IN-03 added a stable uuid5 `webhookId` to `Debounce Wait` so the canonical files stay byte-stable.
Checking the live instance afterwards showed both templates already had working ids — n8n had
generated them at import (`01c91ccf…` WhatsApp, `01589a2c…` Telegram), different from the committed
values.

**Impact:** the fix's practical benefit is canonical-file stability, not a live repair — so the
follow-up redeploy was optional rather than urgent. Also noticed: all four *pre-existing* Wait nodes in
both templates share a single `webhookId` (a UI-duplication artifact), left deliberately alone.
Verified during extraction against the dev n8n database.
**Source:** 10-REVIEW-FIX.md IN-03, commit 4755dc6, dev n8n workflow records

---

### The subtlest rule of the phase shipped with zero tests

`da884dd` extracted the burst accumulator as a pure static (`AppendBurst`) and added 5 EditMode cases
— the test file now holds 11 tests. But `14b049f` — *the pending burst survives a fire and clears on
the outgoing-reply boundary* — changed only `SuggestionsController.cs`, added no tests, and lives
inside a MonoBehaviour method the pure seam does not reach. Its only evidence is dev execs 1315/1316.

**Impact:** the phase's hardest-won rule is also its least-protected one; a future refactor of
`HandleLive` could silently reintroduce the straddle regression. The fix is cheap — extract the
boundary decision into the same pure seam that already covers accumulation.
**Source:** `git show --stat da884dd 14b049f`, IncomingDebounceGateTests.cs (11 tests, verified during extraction)

---

## Open items this extraction surfaced

Not defects, but things a future session should know are genuinely unresolved:

1. **R-10-02's rationale is stale** (see Surprises). Either re-word the accepted risk or re-audit it.
2. **`14b049f` is untested** (see Surprises). ~30 minutes of work to extract and cover.
3. **UAT scenario 4's two sub-checks were never observed** — manual-refresh-immediate and
   card-pick-immediate still read "☐ not reached" in `10-HUMAN-UAT.md`, and the Telegram half is
   marked PASS by argument (channel-agnostic client logic) rather than by observation. They are the
   behavior threat T-10-02-02 mitigates.
4. **Assumption A5 was never verified** — the whole "one waiting execution per fragment, newest wins"
   design assumes unbounded concurrent webhook executions. `10-RESEARCH.md` logs it as A5 and notes
   that a concurrency limit would skew the abort/win timing. A3 got promoted into a gate assertion
   with a stop-rule; A5 got nothing.
5. **`limit=15` silently truncates the combine** — a burst longer than 15 messages with no
   intervening reply drops the oldest fragments with no signal (10-RESEARCH.md A4, accepted for v1).
6. **The `Suggest Replies` workflow changes were never reviewed or audited** — `da9d476`/`da884dd`
   edit `9PTyYcelRQI7bGDb-Suggest_Replies.json`, which appears in none of `10-REVIEW.md`'s 13 reviewed
   files and none of `10-SECURITY.md`'s trust boundaries. It entered the phase record only through the
   UAT resolution addendum.
7. **The `.item` ban is scoped to the Code node only** — `verify-message-batching.py` asserts
   `"$('Webhook').item" not in js` for `Latest+Combine`, but `Fetch Recent`'s own query parameters
   deliberately use `$('Webhook').item.json.body.messages[0]...`. The templates are not `.item`-free.
