# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — Reply Suggestions

**Shipped:** 2026-07-11
**Phases:** 2 | **Plans:** 8 | **Timeline:** 2026-06-23 → 2026-07-11 (~18 days, part-time)

### What Was Built
- Per-chat «Вместе» (semi-auto) mode: a Reply Suggestions Panel proposing 4 ranked, intent-labeled reply moves the owner picks, refines in the composer, and sends — never auto-sending.
- Shared always-active n8n workflow `Suggest Replies` (webhook, tenant-scoped RAG pre-retrieval, one gpt-4o-mini strict-json_schema call, closed 6-move enum, Code-node validation + retry) on dev.
- `N8nSuggestionsProvider` behind the Phase-1 `ISuggestionsProvider` seam via a single-line swap; drain-gated serial pull; pure-static payload/mapping seams with 27 EditMode tests (787 full suite green).
- Security contract `02-SECURITY.md`: 14/14 threats verified closed, 2 formally accepted risks; adversarial e2e matrix (11 curl cases) green with zero fixes needed.

### What Worked
- **The seam-first sequencing was the milestone's best decision.** Phase 1 built the entire UI on a mock; Phase 2's live swap was literally 1 insertion / 1 deletion, verified at the git level three separate times (executor, verifier, auditor). Zero UI rework.
- **Freezing the wire contract in CONTEXT.md let Wave 1 build server and client in parallel** (file-disjoint plans) — both sides implemented the byte-identical field list independently and met in the middle on first contact.
- **Design-spec → PRD express path**: the approved brainstorm spec became CONTEXT.md directly, skipping a redundant discuss-phase round while keeping every product decision locked for the planner.
- **Evidence-graded verification paid off**: the verifier and security auditor re-derived evidence (fresh curls, cold headless test runs, re-computed diffs) instead of trusting summaries — and still passed, which is what makes the green trustworthy.
- **Adversarial hardening as its own plan** (02-03) cost little (all 11 cases passed unchanged) but converted "should be safe" into recorded proof with execution-runData structure checks.

### What Was Inefficient
- **n8n MCP tooling was flaky/unavailable during execution** — executors fell back to the REST API + reading installed node source. It worked (arguably grounded better), but the plans had assumed MCP; make REST-first the documented default for n8n work.
- **`roadmap.update-plan-progress` / `state.begin-phase` SDK format drift** caused repeated manual tracking repairs across plans (orchestrator arg mis-parse seeded a broken STATE.md that a later executor had to fix).
- **Review-then-fix as separate passes** re-read the same 9 files twice; for a 4-warning outcome a single review+fix agent would have been cheaper.
- Device UAT remains the structural bottleneck: two milestones' worth of detail scenarios are deferred because owner device time is scarce — plans should keep device asks down to one consolidated session.

### Patterns Established
- Seam + mock-first phase pair for any feature with a backend dependency (Phase 1 UI on mock → Phase 2 one-line live swap).
- Shared on-demand webhook workflows (DashboardOutcomes → Suggest Replies) instead of per-bot template changes; deploy via committed Python builder (`build-suggest-replies.py`) with creds resolved by NAME; canonical export must be byte-identical to the live instance.
- Two-layer LLM output defense: strict json_schema enum at the model + Code-node count/distinct/clamp validation (strict schema cannot express those).
- App sends conversation context to stateless AI workflows (don't read `n8n_chat_histories` — stale for paused bots).
- Network coroutines for chat-adjacent features host on `ChatManager.Instance`, never on possibly-inactive controllers.
- Grounding rule as a hard prompt invariant: missing fact converts the card's move («Уточнить»/«Отложить») rather than inventing a number.

### Key Lessons
1. A frozen, versioned wire contract written into planning artifacts is what makes parallel waves and independent verification possible — invest in it before splitting work.
2. Adversarial cases are cheap once the harness exists (curl + execution-runData introspection); schedule them as a dedicated plan, not an afterthought.
3. Honest gate-closing beats optimistic gate-closing: recording the device gate as "owner smoke pass, details deferred" kept the milestone auditable instead of silently green.
4. gpt-4o-mini + strict schema + server-side validation is sufficient for structured RU suggestion generation at ~2-4s — no agent loop needed.

### Cost Observations
- Model mix: opus for research/planning/execution agents, sonnet for checker/verifier/auditor — the split held up; no checker ever needed opus.
- Sessions: ~2 major build sessions for Phase 2 (plan→execute→review→secure→close in one), Phase 1 across prior sessions.
- Notable: plan-checker passed first-iteration and the adversarial matrix needed zero fixes — front-loaded research/pattern-mapping (RESEARCH.md + PATTERNS.md) appears to convert directly into first-pass quality.

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Phases | Plans | Key Change |
|-----------|--------|-------|------------|
| v1.0 | 2 | 8 | First full GSD cycle: seam-first phasing, PRD express context, evidence-graded verification, security gate |

### Cumulative Quality

| Milestone | Tests (EditMode) | Security | Deferred at close |
|-----------|------------------|----------|-------------------|
| v1.0 | 787 total / 27 feature | 14/14 closed, 2 accepted risks | 3 device-UAT items |

### Top Lessons (Verified Across Milestones)

1. (Single milestone so far — candidates to re-verify in v1.x: seam-first phasing, frozen wire contracts, adversarial plans as cheap insurance.)
