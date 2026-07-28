# Roadmap: Automation — WhatsApp/Telegram AI Bot Manager

## Overview

A no-code AI chatbot the owner runs on their OWN WhatsApp and Telegram, for CIS small businesses. The north star is trust + control: the bot sits on a spectrum from fully autonomous («Авто») to owner-in-the-loop («Вместе»), and the owner can always take over.

Four milestones have shipped. **v1.0** delivered the semi-auto «Вместе» reply path on WhatsApp. **v1.1** brought Telegram to parity on the Wappi tapi API (chat client, n8n auto-replies, suggestions, dashboard). **v1.2** made the bot fire at the right moment — server-side suppression for semi-auto chats, plus message batching so a burst of fragments produces one combined reply. **v1.3** added the first-run onboarding journey.

No milestone is currently active — start the next one with `/gsd-new-milestone`.

## Milestones

- ✅ **v1.0 Reply Suggestions** — Phases 1-2 (shipped 2026-07-11) — [archive](milestones/v1.0-ROADMAP.md)
- ✅ **v1.1 Telegram Parity** — Phases 3-8 (shipped 2026-07-21) — [archive](milestones/v1.1-ROADMAP.md)
- ✅ **v1.2 Reply-Trigger Discipline** — Phases 9-10 (shipped 2026-07-27) — [archive](milestones/v1.2-ROADMAP.md)
- ✅ **v1.3 First-Run Onboarding** — Phase 11 (shipped 2026-07-23) — [archive](milestones/v1.3-ROADMAP.md)

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Continuous numbering across milestones — the next milestone starts at Phase 12.

<details>
<summary>✅ v1.0 Reply Suggestions (Phases 1-2) — SHIPPED 2026-07-11</summary>

- [x] Phase 1: Polished Suggestions Panel on Mock Data (4/4 plans) — completed 2026-06-25
- [x] Phase 2: n8n Live Wiring (4/4 plans) — completed 2026-07-10

Full details: `.planning/milestones/v1.0-ROADMAP.md` · phases: `.planning/milestones/v1.0-phases/`

</details>

<details>
<summary>✅ v1.1 Telegram Parity (Phases 3-8) — SHIPPED 2026-07-21</summary>

- [x] Phase 3: tapi Live-Shape Capture (1/1 plans) — completed 2026-07-12
- [x] Phase 4: n8n Telegram Template Parity, dev (2/2 plans) — completed 2026-07-12
- [x] Phase 5: Channel-Aware ChatManager Core (12/12 plans) — completed 2026-07-14
- [x] Phase 6: Channel Switcher UI (2/2 plans) — completed 2026-07-13
- [x] Phase 7: «Вместе» Suggestions + Dashboard on Telegram (2/2 plans) — completed 2026-07-13
- [x] Phase 8: Device UAT + Milestone Closeout (35/35 plans) — completed 2026-07-21, **GATE A PASSED round 7**

Prod bagkz replication PARKED indefinitely per owner — not pending work.

Full details: `.planning/milestones/v1.1-ROADMAP.md` · phases: `.planning/milestones/v1.1-phases/` · requirements: `.planning/milestones/v1.1-REQUIREMENTS.md`

</details>

<details>
<summary>✅ v1.2 Reply-Trigger Discipline (Phases 9-10) — SHIPPED 2026-07-27</summary>

- [x] Phase 9: Semi-Auto Suppression Flag (5/5 plans) — completed 2026-07-22
- [x] Phase 10: Message Batching / Debounce (4/4 plans) — completed 2026-07-27

Phase 10 closed with verification 5/5, UAT 5/5, security 14/14 (threats_open 0), code review dispositioned (8 fixed / 3 accepted), and `10-LEARNINGS.md` extracted.

Full details: `.planning/milestones/v1.2-ROADMAP.md` · phases: `.planning/milestones/v1.2-phases/`

</details>

<details>
<summary>✅ v1.3 First-Run Onboarding (Phase 11) — SHIPPED 2026-07-23</summary>

- [x] Phase 11: First-Run Onboarding Flow (10/10 plans) — completed 2026-07-23

Verification 10/10; security 34/34 (threats_open 0); code review 12/12 findings closed; UAT Round-2 owner-approved.

Full details: `.planning/milestones/v1.3-ROADMAP.md` · phases: `.planning/milestones/v1.3-phases/`

</details>

### ▶ Next Milestone

None active. Run `/gsd-new-milestone` to start the next one (questioning → research → requirements → roadmap). A fresh `REQUIREMENTS.md` is created there — v1.1's is archived at `.planning/milestones/v1.1-REQUIREMENTS.md`, and v1.2/v1.3 shipped without a formalized one (their ids are traced in the phase artifacts).

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Polished Suggestions Panel on Mock Data | v1.0 | 4/4 | Complete | 2026-06-25 |
| 2. n8n Live Wiring | v1.0 | 4/4 | Complete | 2026-07-10 |
| 3. tapi Live-Shape Capture | v1.1 | 1/1 | Complete | 2026-07-12 |
| 4. n8n Telegram Template Parity (dev) | v1.1 | 2/2 | Complete | 2026-07-12 |
| 5. Channel-Aware ChatManager Core | v1.1 | 12/12 | Complete | 2026-07-14 |
| 6. Channel Switcher UI | v1.1 | 2/2 | Complete | 2026-07-13 |
| 7. «Вместе» Suggestions + Dashboard on Telegram | v1.1 | 2/2 | Complete | 2026-07-13 |
| 8. Device UAT + Milestone Closeout | v1.1 | 35/35 | Complete (Gate A passed round 7) | 2026-07-21 |
| 9. Semi-Auto Suppression Flag | v1.2 | 5/5 | Complete | 2026-07-22 |
| 10. Message Batching / Debounce | v1.2 | 4/4 | Complete | 2026-07-27 |
| 11. First-Run Onboarding Flow | v1.3 | 10/10 | Complete | 2026-07-23 |

**Totals:** 11 phases · 81 plans · 4 milestones shipped.

## Carried Open Items

Not closed by any milestone — carried forward:

- ~~**v1.0 UAT debt**~~ — **CLOSED 2026-07-28.** All three artifacts (`01-HUMAN-UAT.md`, `02-HUMAN-UAT.md`, `01-VERIFICATION.md`) are now `resolved`/`passed`. Run against current behavior rather than the stale v1.0 scripts: owner verified the airplane-mode error path (never tested by any phase before), stale/crossed-response discard under rapid picks, and card-tap/manual-refresh immediacy — which also closed Phase 10's last two open sub-checks. One accepted deviation recorded: suggestion cards do not truncate long replies (SC-2/PANEL-04 asked for ~2 lines + ellipsis); owner accepted current behavior, so it is a deliberate deviation, not a defect.
- **D15 (WhatsApp in-app reaction removal):** open-deferred follow-up. The probe returned `reactionsKey=True`, so an absence-based reconcile IS possible — capture the `reactions[]` shape first.
- **Prod bagkz replication:** PARKED indefinitely per owner. Not pending work; listed only so it is not rediscovered as a surprise.
