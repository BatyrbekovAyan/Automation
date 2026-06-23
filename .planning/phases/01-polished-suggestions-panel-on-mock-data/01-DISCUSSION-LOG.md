# Phase 1: Polished Suggestions Panel on Mock Data - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-23
**Phase:** 01-polished-suggestions-panel-on-mock-data
**Mode:** discuss
**Areas discussed:** Pick/edit/re-cluster · Card layout & anatomy · Toggle & mode indicator · States, motion & mock data

---

## Pick / edit / re-cluster

### Tap model
| Option | Description | Selected |
|--------|-------------|----------|
| Two affordances | Card body = load to composer; separate steer control = re-cluster (reuses QuickReplyButton dual-action) | |
| Single tap does both | One tap loads text into composer AND regenerates a steered set of 4 | ✓ |
| Tap loads; send steers | Tap = load only; re-cluster fires on Send | |

**User's choice:** Single tap does both.
**Notes:** Removes the need for a separate steer affordance; simplifies the card. Raises the composer-overwrite question below.

### Overwrite policy (tap vs in-progress draft)
| Option | Description | Selected |
|--------|-------------|----------|
| Tap replaces the draft | Explicit tap overwrites composer + re-clusters; auto-populate-on-incoming still never overwrites a dirty draft (INT-02) | ✓ |
| Protect the draft | Tap appends or asks first if composer is dirty | |
| Append to draft | Tapped text appended to existing composer text | |

**User's choice:** Tap replaces the draft.
**Notes:** Rule established — deliberate action overwrites, automatic action defers (INT-02 preserved).

---

## Card layout & anatomy

### Layout
| Option | Description | Selected |
|--------|-------------|----------|
| Vertical stack of 4 | Best-first top-to-bottom, full width per card, badge reads naturally; ~2-line truncation | ✓ |
| 2×2 grid | Compact, reuses QuickReplyPanel, but ranking less obvious and text cramped | |
| Horizontal scroll row | Space-efficient, but only ~1.5 cards visible; hard to compare/see ranking | |

**User's choice:** Vertical stack of 4.

### Intent label treatment
| Option | Description | Selected |
|--------|-------------|----------|
| Subtle single-accent chip | One consistent muted accent pill + label; clean and scannable | ✓ |
| Category-colored chip | Per-intent colors; more scannable but noisy and adds palette overhead | |
| Plain secondary text | Muted text, no pill; lightest but reads less like a category tag | |

**User's choice:** Subtle single-accent chip.

---

## Toggle & mode indicator

### Toggle location
| Option | Description | Selected |
|--------|-------------|----------|
| Chat top bar | Icon toggle in the header; discoverable, persistent, doubles as mode indicator | ✓ |
| 3-dot overflow menu | Uncluttered but hides a core feature; no at-a-glance indicator | |
| Control near composer | Close to the panel but competes with send controls in the thumb zone | |

**User's choice:** Chat top bar.

### Collapsed-state indicator / reopen
| Option | Description | Selected |
|--------|-------------|----------|
| Lit toggle + reopen handle | Toggle stays lit; slim handle above composer reopens the panel | ✓ (later superseded) |
| Lit toggle only | Toggle on-state is the only indicator; reopen is clunky | |
| Persistent banner/pill | Separate always-visible pill; competes with the toggle indicator | |

**User's choice:** Lit toggle + reopen handle — **later superseded** by the "always expanded" decision (see next section).

---

## States, motion & mock data

### Loading visual
| Option | Description | Selected |
|--------|-------------|----------|
| 4 skeleton cards | Shimmer placeholders matching real card shape; no layout pop; re-cluster shimmers in place | ✓ |
| Single spinner | Simple but generic; visible resize when real cards arrive | |
| Keep old cards dimmed | Smooth for re-cluster but needs separate first-load treatment | |

**User's choice:** 4 skeleton cards.

### Collapsed + incoming behavior → revised to "always expanded"
| Option | Description | Selected |
|--------|-------------|----------|
| Stay collapsed + 'new' hint | Refresh behind handle, badge on handle | |
| Auto-expand the panel | Incoming auto-expands collapsed panel | |
| Stay collapsed, no hint | Refresh silently, no indicator | |
| **Other (user typed)** | **"on second thought i want panel to be always expanded"** | ✓ |

**User's choice (free text):** Panel should always be expanded.
**Notes:** Triggered a follow-up to reconcile with PANEL-05 (which had a collapse/dismiss).

### Free-typing model (after "always expanded")
| Option | Description | Selected |
|--------|-------------|----------|
| Toggle off = the only hide | Panel always visible while ON; composer always available for free typing; toggle OFF removes it; no collapse/handle | ✓ |
| Keep a manual collapse too | Always-expand by default but retain a manual collapse-to-handle | |

**User's choice:** Toggle off = the only hide.
**Notes:** Reinterprets PANEL-05 ("dismiss" = toggle off). Removes the reopen handle decided earlier.

### Mock data language & scenarios
| Option | Description | Selected |
|--------|-------------|----------|
| Russian, small-business scenarios | RU replies; greeting/price/availability/booking/decline; ≥1 long reply for truncation | ✓ |
| Bilingual RU + EN mix | Stress-tests layout but less representative | |
| English placeholder | Fastest but not market-representative; Cyrillic layout risk later | |

**User's choice:** Russian, small-business scenarios.

---

## Claude's Discretion

- Empty/error-state visuals within PANEL-04; "Recommended" badge placement/styling; chip accent color; card padding/spacing; skeleton timing; panel motion; manual-refresh affordance placement; the exact `ISuggestionsProvider`/guard/controller shapes.

## Deferred Ideas

- None new — v2 items (FB-01/02, POL-01/02) and Phase-2 n8n wiring already tracked in REQUIREMENTS.md / STATE.md.
