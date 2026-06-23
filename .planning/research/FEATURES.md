# Feature Research

**Domain:** Semi-auto agent-assist / AI reply-suggestion panel for a mobile WhatsApp business-chat client (owner-in-control, never auto-send)
**Researched:** 2026-06-23
**Confidence:** HIGH for prior-art patterns and confidence-display science (multiple converging sources incl. peer-reviewed research); MEDIUM for exact mobile-layout specifics (most prior art is desktop helpdesk).

## Scope & Framing

This milestone adds a **Reply Suggestions Panel** to the existing WhatsApp chat client: a bottom sheet above the composer showing **4 cards** (reply text + intent label + confidence), gated by a **per-chat semi-auto toggle**. Tapping a card loads its text into the composer to edit (never auto-sends); picking re-clusters a fresh set of 4 toward the pick; auto-populates on incoming message + manual refresh.

The dominant pattern across every credible product studied (Gmail, Intercom Copilot/Fin, Zendesk Auto Assist, Help Scout AI Drafts, Front Copilot) is **draft-then-review, human approves before send**. None of the serious helpdesk tools auto-send agent-assist drafts. This validates the project's core value (owner stays in control / trust). The research below is organized so requirements can be lifted directly.

### Key prior-art summary (what each does)

| Product | How suggestions appear | Count | Confidence shown? | Edit-before-send? | Refine loop |
|---------|------------------------|-------|-------------------|-------------------|-------------|
| **Gmail Smart Reply** | Chips/cards below the email | Up to **3** | No | Tap inserts into composer, fully editable | None (pick = insert) |
| **Gmail Smart Compose** | Inline grey ghost text, Tab to accept | 1 (inline) | No | Yes, it's in the draft | Continuous as you type |
| **Intercom Fin Copilot** | AI assistant panel in inbox; generates editable draft | 1 draft | No (drafts only when "confident") | Yes, review/edit | Custom prompt iterate |
| **Zendesk Auto Assist** | Editor Draft (in composer) OR Internal Note Draft; notifies if a new suggestion arrives while editing | 1 draft | No | Yes, review/edit/approve | Tone-of-voice, regenerate |
| **Help Scout AI Drafts** | Suggested reply text as a draft; accept/edit/ignore | 1 draft | No | Yes | Custom prompt: change tone/length/start over |
| **Front Copilot** | Compose draft, refine existing text | 1 draft | No | Yes | Iterate multiple times w/ custom prompts |

**The single most important finding:** mature agent-assist products show **one** editable draft, not a multi-card menu, and **none of them surface a numeric confidence score on the reply**. The project's 4-card multi-option layout is closer to **Gmail Smart Reply** (3 chips) than to the helpdesk copilots — a deliberate differentiator that fits the "spectrum of control / pick-and-refine" core value, but it should borrow Smart Reply's restraint on confidence display.

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = panel feels broken or untrustworthy.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Tap card → load into composer, fully editable, never auto-send** | Universal across all studied tools; it IS the trust contract. Auto-send would violate the core value. | LOW | Wire card tap → `ExpandableInput` text set + focus. Reuse existing composer (`MessagesBottomPanel`/`ExpandableInput`). |
| **Reply text shown as the primary, scannable content** | The reply is what the owner is choosing; it must be readable at a glance. | LOW | TMP text, left-aligned, generous line height. |
| **Truncation of long replies with graceful overflow** | Replies vary in length; a card can't grow unbounded in a bottom sheet. | MEDIUM | Clamp to ~2–3 lines + ellipsis on the card; full text appears in composer on tap. (See "Truncation" below — do NOT make the card expand; tap-to-load is the reveal.) |
| **Intent / category label per card** | Helps the owner scan 4 options fast ("Pricing", "Order status", "Greeting"). Zendesk/BIK/Cobbai all tag intents; it's how you make 4 options skimmable. | MEDIUM | Small pill/tag above or beside the reply. Backend (n8n) supplies label; UI just renders. Keep taxonomy short & stable. |
| **Auto-populate on incoming customer message** | The panel must feel proactive; this is the semi-auto promise. | MEDIUM | Hook `ChatManager.OnLiveMessagesReceived`. Respect Wappi serial-fetch / cross-response guards (known constraint). |
| **Manual refresh / regenerate affordance** | Users rarely accept the first AI output; a refresh is expected. Regenerate is near-universal in AI tools. | LOW | One-tap icon button (circular-arrow). Visible, not buried. |
| **Loading state while suggestions fetch** | Network is async; a blank panel reads as broken. | LOW | Skeleton cards (4 shimmer placeholders) or a spinner; keep panel height stable to avoid composer jump. |
| **Empty state ("no suggestions")** | The model may return nothing (off-topic message, low-signal). | LOW | "No suggestions — tap refresh or type your own." Never an empty silent panel. |
| **Error state (fetch failed)** | n8n/network can fail; Wappi has known flakiness. | LOW | Inline "Couldn't load suggestions. Retry." with a retry button. Don't block the composer — owner can always type. |
| **Per-chat semi-auto toggle that shows/hides the panel** | The panel must be opt-in per conversation (project decision). | MEDIUM | Persist per-chat in PlayerPrefs (existing convention). Toggle in chat header or overflow. |
| **Dismiss / collapse the panel** | Owner often wants to just type. The panel must never trap the composer. | LOW | Swipe-down or a collapse handle on the bottom sheet (reuse `SheetDragDismiss`). |
| **Owner can always type a freehand reply regardless of panel state** | Control = the human is never forced down a suggested path. | LOW | Composer stays primary and always available. |

### Differentiators (Competitive Advantage)

Features that set this apart from generic copilots. These align with the core value: **spectrum of control, pick-and-refine, trust**.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **4-option card set (vs. single draft)** | Gives the owner *choice* and a sense of ranking, not a take-it-or-leave-it draft. This is the "semi-auto" identity — closer to Gmail Smart Reply's menu than to a copilot's single draft. | MEDIUM | 4 is reasonable: Gmail uses 3; 4 fits a mobile sheet if cards are compact. Watch vertical budget on small screens (see anti-features re: too-tall sheet). |
| **Re-cluster on pick: choosing one regenerates 4 re-ranked toward it** | The standout interaction. It turns "pick" into a *steering* gesture — the owner narrows toward their intent without typing a prompt. No studied product does exactly this; it's a genuine differentiator. | HIGH | Backend (n8n) must accept "anchor toward picked reply" and return a fresh 4. UI: animate the swap; make it obvious a new set arrived. Avoid making it feel like the pick "didn't work" — see pitfalls. Consider a subtle "refined toward your pick" cue. |
| **Intent labels as a fast-scan ranking aid** | With 4 options, labels are what make scanning fast; they double as a trust signal ("the bot understood this is a pricing question"). | MEDIUM | Differentiator only if the labels are *accurate and short*. Bad labels erode trust faster than no labels. |
| **Seamless pick→edit→send in the owner's own composer** | The owner's edits become the final message on their own number — preserves their voice. Reinforces "it's still you talking." | LOW | Already implied by table stakes; the differentiator is doing it smoothly with no mode switch. |
| **Tone/length quick-adjust before regenerating** (v1.x) | Help Scout/Front/Zendesk all offer tone control; it's the natural next refine axis after re-cluster. | MEDIUM | Defer to after core loop validated. Could be a row of chips: "Shorter / Warmer / More formal." |

### Confidence Display — the critical decision (tied to the Trust core value)

The PROJECT decision lists "confidence" on each card. The research is strongly cautionary, so this gets its own section.

**Empirical finding (peer-reviewed, arXiv 2402.07632):** Showing a **miscalibrated** confidence score actively *harms* decision quality. Overconfident scores cause over-reliance on wrong suggestions (41% relied on wrong advice); underconfident scores cause users to reject correct suggestions (18%). Disclosing the calibration level helps users *detect* miscalibration but *worsens outcomes* and erodes trust. **Bottom line: do not display a confidence number unless the underlying score is well-calibrated** — and an early-stage n8n/LLM pipeline almost certainly is not.

**Design-pattern finding (AI UX guides):** Hiding uncertainty can erode trust, but **over-exposing raw metrics creates cognitive overload and false precision.** Recommended representations, in order of safety:

1. **None / implicit** — rely on *ranking order* (best suggestion first). Gmail, Intercom, Help Scout, Front, Zendesk all do this. **Safest, and matches every shipped agent-assist product.**
2. **Coarse tiers** — a 3-level signal (e.g., a subtle "Recommended" badge on the top card only, or High/Med/Low dot). Low cognitive load, no false precision.
3. **Visual bar / color** — more prominent, implies more precision than tiers; only safe if calibrated.
4. **Numeric % — NOT recommended.** Highest false-precision risk; the research shows it backfires when miscalibrated. A "87%" on a chatbot reply invites the owner to either over-trust it or distrust the whole feature.

**Recommendation for THIS product (ranked):**

- **v1 (mock + first live):** Surface confidence as **ordering only** (card 1 = most confident) plus an optional single **"Recommended" badge** on the top card. No per-card number, no bar. This is the trust-preserving default and the universal industry pattern.
- **If the team insists on a per-card signal:** use **coarse tiers** (e.g., a small colored dot: green/amber, never red — a "red/low" reply shouldn't be shown at all). Three tiers max.
- **Avoid numeric % entirely** until the pipeline's confidence is measured and shown to be calibrated. Premature numbers are the single biggest trust risk in this feature.

The PROJECT.md decision "text + intent label + confidence" should be read as **confidence-as-ranking/badge, not confidence-as-number.** Flag this for the requirements/design phase — it's a decision that needs an explicit call, and the safe default is "no number."

### Refine / Regenerate Loop — what makes it feel good

From the Regenerate UX pattern research (shapeof.ai) and the refine-loop literature:

**Makes it feel good:**
- **One-tap, highly visible** refresh/regenerate — not buried.
- **Transparent about what changes** — owner should sense a *new* set arrived (animate the swap; don't silently replace identical-looking cards).
- **Previous picks recoverable** — at minimum, the composer draft is never lost when new suggestions arrive (Zendesk explicitly notifies the agent if a new suggestion lands while they're editing, rather than stomping the draft). **Critical:** auto-populate on a *new incoming message* must NOT wipe an in-progress composer edit.
- **Guided variants beat blind reruns** for convergent tasks — re-cluster-toward-pick IS a guided variant (steer by example), which is why it's a strong differentiator vs. a dumb "regenerate."

**Makes it frustrating (avoid):**
- Unclear whether a regenerate overwrote something the owner liked.
- Random/identical-feeling regenerations (no perceptible change → "it's broken").
- Losing the in-progress draft to an auto-refresh.
- Latency with no feedback (always show the loading skeleton).

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **Auto-send the top suggestion** | "Save the owner a tap" | Destroys the entire core value (owner control/trust); no serious agent-assist product does this. One bad auto-sent reply on the owner's *own* number is a brand/relationship risk. | Always require an explicit tap to load + a separate send. That IS the product. (Fully autonomous mode already exists separately.) |
| **Numeric confidence % on each card** | "More information = more trust" | Research shows miscalibrated numbers harm decisions and erode trust; false precision; cognitive load on a small mobile card. | Ranking order + optional "Recommended" badge, or coarse tiers. No number until calibrated. |
| **More than 4 cards (5–6+)** | "More options = better" | Bottom sheet eats vertical space above the keyboard on mobile; choice overload; slower to scan; pushes the composer/messages off-screen. Gmail caps at 3 for a reason. | Keep 4 (already decided). If anything, allow showing fewer when the model returns fewer good options. |
| **Expandable cards that grow to full text in-place** | "Let me read the whole reply" | A card expanding inside a bottom sheet reflows the sheet, jumps the composer, and fights the keyboard. | Truncate to 2–3 lines on the card; tap loads the **full** text into the composer where it's editable anyway. The composer is the "full view." |
| **Per-card thumbs up/down rating UI in v1** | "Collect feedback to improve the model" | Adds clutter to a dense mobile card; the *pick itself* is already the strongest implicit signal; n8n feedback loop isn't in scope this milestone. | Treat the owner's pick (and which one they edited/sent) as the implicit signal. Add explicit rating later if a training loop is built. |
| **Always-on panel for every chat (global, not per-chat)** | "Simpler than a per-chat toggle" | Steals composer space in chats where the owner just wants to type; ignores the per-conversation spectrum-of-control design. | Per-chat semi-auto toggle (already decided). Default OFF. |
| **Streaming/typewriter animation of each suggestion** | "Looks modern/AI-ish" | 4 cards streaming simultaneously is visual chaos on mobile and delays scannability; the owner wants to *scan and pick*, not watch. | Show a clean loading skeleton, then present all 4 finished cards at once. |
| **Regenerating silently in place with identical-looking results** | "Refresh should be invisible" | Owner can't tell anything happened → reads as broken. | Animate the swap; ensure the new set is perceptibly different; show the loading state during fetch. |

## Feature Dependencies

```
[Per-chat semi-auto toggle]
    └──gates──> [Reply Suggestions Panel (bottom sheet)]
                    └──requires──> [Suggestion data source]
                                       ├── v1: [Mock/stub provider]   (no n8n dep — decouples UI polish)
                                       └── live: [n8n emits text/intent/confidence]
                    │
                    ├──contains──> [4 suggestion cards: text + intent label + confidence-as-ranking]
                    │                   └──requires──> [Truncation/clamp on card text]
                    │
                    ├──triggered by──> [Auto-populate on OnLiveMessagesReceived]
                    │                   └──must-not──> [stomp in-progress composer draft]
                    │
                    ├──triggered by──> [Manual refresh / regenerate]
                    │
                    └──on pick──> [Load text into composer (editable, never send)]
                                      └──triggers──> [Re-cluster: regenerate 4 toward pick]
                                                         └──requires──> [n8n "anchor toward pick" support]

[Loading / Empty / Error states] ──required-by──> [Reply Suggestions Panel]
[Tone/length quick-adjust] ──enhances──> [Re-cluster loop]   (v1.x, deferred)
```

### Dependency Notes

- **Panel requires a suggestion data source, but v1 uses a mock provider** so UI/interaction polish is fully decoupled from n8n (matches PROJECT build order). Design the provider as an interface so mock → live is a swap.
- **Re-cluster requires n8n "anchor toward pick" support** — this is the highest-complexity backend dependency and the differentiator; it must be in a later phase than the mock UI.
- **Auto-populate conflicts with in-progress composer edits** — the resolution (notify, don't stomp; à la Zendesk) is a hard requirement, not optional polish.
- **Confidence-as-number conflicts with the trust core value** — resolve to ranking/badge before building any numeric UI.

## MVP Definition

### Launch With (v1)

The trust-preserving, polish-first slice. Built against **mock data** per the project decision.

- [ ] **Per-chat semi-auto toggle** (default OFF, persisted) — gates the whole feature.
- [ ] **Bottom-sheet panel above composer with 4 cards** — text + intent label; confidence as **ordering + optional "Recommended" badge on top card** (NO number).
- [ ] **Card text truncation** (~2–3 lines + ellipsis) — full text revealed by loading into composer.
- [ ] **Tap card → load full text into composer, editable, never auto-send** — the trust contract.
- [ ] **Manual refresh / regenerate** (one-tap, visible).
- [ ] **Auto-populate on incoming message** — without stomping an in-progress draft.
- [ ] **Loading (skeleton), empty, and error states** — panel never reads as silently broken.
- [ ] **Collapse/dismiss the panel** — composer is never trapped.

### Add After Validation (v1.x)

- [ ] **Re-cluster on pick (regenerate 4 toward the chosen reply)** — the differentiator. Deferred only because it needs the n8n "anchor" backend; if the live wiring phase lands together, promote into v1. Trigger to add: mock UI loop validated + n8n anchor endpoint ready.
- [ ] **Live n8n wiring** (real text/intent/confidence) — swap the mock provider.
- [ ] **Tone/length quick-adjust chips before regenerate** — natural next refine axis (Help Scout/Front/Zendesk precedent).

### Future Consideration (v2+)

- [ ] **Coarse confidence tiers (colored dot)** — only after pipeline confidence is measured & shown calibrated.
- [ ] **Explicit per-card feedback (thumbs)** — only if a model-training loop is built.
- [ ] **Branching/history of regenerated sets** — recover a previous set of 4. Defer until users ask.
- [ ] **Telegram chat surface** — explicitly out of scope this milestone.

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Tap card → load into composer, never auto-send | HIGH | LOW | P1 |
| 4 cards: text + intent label | HIGH | MEDIUM | P1 |
| Per-chat semi-auto toggle | HIGH | MEDIUM | P1 |
| Loading / empty / error states | HIGH | LOW | P1 |
| Card text truncation | MEDIUM | MEDIUM | P1 |
| Manual refresh / regenerate | HIGH | LOW | P1 |
| Auto-populate on incoming (no draft stomp) | HIGH | MEDIUM | P1 |
| Collapse/dismiss panel | MEDIUM | LOW | P1 |
| Confidence as ranking + "Recommended" badge | MEDIUM | LOW | P1 |
| Re-cluster on pick (regenerate toward pick) | HIGH | HIGH | P2 |
| Live n8n wiring | HIGH | HIGH | P2 |
| Tone/length quick-adjust | MEDIUM | MEDIUM | P2 |
| Numeric confidence % | LOW (net-negative) | MEDIUM | P3 (avoid) |
| Per-card thumbs feedback | LOW | MEDIUM | P3 |
| Branching/history of sets | LOW | HIGH | P3 |

**Priority key:** P1 = must have for launch · P2 = should have, add when possible · P3 = nice to have / future / avoid.

## Competitor Feature Analysis

| Feature | Gmail Smart Reply | Helpdesk copilots (Intercom/Zendesk/Help Scout/Front) | Our Approach |
|---------|-------------------|-------------------------------------------------------|--------------|
| Number of suggestions | 3 chips | 1 editable draft | **4 cards** (our differentiator: choice + steer) |
| Confidence shown | No | No | **No number** — ranking + optional "Recommended" badge |
| Intent label | No | Some (intent tags exist separately) | **Yes, per card** — makes 4 options scannable |
| Pick action | Insert into composer, editable | Insert draft, editable | **Load into composer, editable, never send** |
| Refine loop | None | Regenerate / custom-prompt / tone | **Re-cluster toward pick** (novel steer-by-example) |
| Auto-send | No | No | **No** (autonomous mode is a separate existing path) |
| Draft protection | N/A | Zendesk notifies on new suggestion vs. stomping | **Don't stomp in-progress draft** on auto-refresh |
| Surface | Inline chips | Side/inline panel (desktop) | **Bottom sheet above composer** (mobile thumb-zone) |

## Sources

- Gmail Smart Reply / Smart Compose (up to 3 suggestions, chip/card layout, no confidence shown): https://support.google.com/mail/answer/9116836 ; https://blog.google/products/gmail/gmail-ai-features/ — HIGH
- Intercom Fin AI Copilot (single editable draft, "draft when confident", human-in-loop): https://www.intercom.com/blog/announcing-fin-ai-copilot/ ; https://www.intercom.com/suite/helpdesk/copilot ; https://fin.ai/help/en/articles/11333598-how-to-use-copilot — HIGH
- Zendesk Auto Assist (Editor Draft vs Internal Note Draft, notify-don't-stomp on new suggestion, tone-of-voice, review/edit/approve): https://support.zendesk.com/hc/en-us/articles/10140103140122 ; https://swifteq.com/post/zendesk-auto-assist — HIGH
- Help Scout AI Drafts (accept/edit/ignore, custom-prompt tone/length/restart, "assist not automate"): https://docs.helpscout.com/article/1570-ai-drafts ; https://docs.helpscout.com/article/1513-use-ai-assist ; https://www.helpscout.com/ai-features/ — HIGH
- Front Copilot (compose/refine/iterate with custom prompts, human review before send): https://help.front.com/en/articles/2344960 — HIGH
- AI confidence display research — miscalibrated confidence harms decisions; disclosing calibration backfires (peer-reviewed): https://arxiv.org/html/2402.07632v4 — HIGH
- Confidence-visualization UI patterns (tiers vs % vs bar vs none; avoid false precision/overload): https://www.aiuxdesign.guide/patterns/confidence-visualization ; https://www.aiuxdesign.guide/patterns/trust-calibration — MEDIUM
- Regenerate UX pattern (one-tap, transparent change, recoverable, guided > blind): https://www.shapeof.ai/patterns/regenerate ; https://thepromptbench.com/ai-product-ux/regenerate-undo-branch-conversation-mechanics/ — MEDIUM
- Intent/label tagging on support reply cards (taxonomy short & stable, labels aid scanning): https://help.bik.ai/en/articles/10229390-ai-intent-labels-in-helpdesk ; https://cobbai.com/blog/ai-intent-tagging-support — MEDIUM
- WhatsApp Business quick replies (existing chip/quick-reply convention in the channel): https://faq.whatsapp.com/1791149784551042 ; https://aunoa.ai/en/blog/how-to-use-whatsapp-business-quick-replies-to-improve-uxi/ — MEDIUM

---
*Feature research for: semi-auto AI reply-suggestion panel (owner-in-control WhatsApp business chat)*
*Researched: 2026-06-23*
