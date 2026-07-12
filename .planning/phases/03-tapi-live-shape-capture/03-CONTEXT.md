# Phase 3: tapi Live-Shape Capture - Context

**Gathered:** 2026-07-12
**Status:** Ready for planning
**Source:** Design-spec express path (docs/superpowers/specs/2026-07-12-telegram-parity-design.md §D6, §6; autonomous session)

<domain>
## Phase Boundary

Deliver the OWNER-runnable capture tooling + verdict checklist that grounds all Telegram parser/media work in real Wappi tapi response shapes. This phase builds the script and the checklist; RUNNING the capture is the owner's step (user-assisted gate). The phase is "code-complete" when the tooling exists, is safe, and is documented; it is "closed" when the owner has run it and SHAPES.md verdicts are recorded.

NOT in this phase: any ChatManager/parser changes (Phase 5), any n8n changes (Phase 4).

</domain>

<decisions>
## Implementation Decisions

### Capture script (VER-01)
- Location: `Tools/tapi/capture-shapes.sh` (bash + curl + jq; same Tools/ conventions as `run-tests-headless.sh`).
- READ-ONLY invariant (locked): the script may call ONLY GET/list endpoints — `tapi/profile/all/get`, `tapi/sync/get/status`, `tapi/sync/chats/get`, `tapi/sync/chats/filter`, `tapi/sync/chats/days/get`, `tapi/sync/messages/get`, `tapi/sync/messages/id/get`, `tapi/sync/contact/get`. NEVER profile/add|delete|logout, auth/*, message/send|reply|reaction, webhook/*.
- Token handling (locked): reads the Wappi token from `Assets/StreamingAssets/secrets.json` LOCALLY at runtime (jq). The token never appears in output files, argv, or logs. Claude never reads secrets.json — the owner runs the script.
- Sanitization: output JSON is scrubbed before writing — the Authorization token cannot appear in payloads, but phone numbers / names may; keep raw payloads intact (they're needed for parser work) and store samples under `Tools/tapi/samples/` which MUST be gitignored (add `Tools/tapi/samples/` to .gitignore). SHAPES.md (committed) contains only structural verdicts + fully redacted mini-excerpts.
- Coverage: for the first authorized Telegram profile found (or `--profile <id>` override): status, chat list via all 3 list endpoints, `messages/get` for up to N chats (default 5, `--chats` override), auto-detecting media variety (walk messages; record one full sample per distinct `type` value encountered), one `messages/id/get` for a reply message (isReply=true) and one for a reacted message if findable, `contact/get` for one dialog.
- Degrade gracefully: no authorized TG profile → exit with a clear RU/EN message telling the owner to authorize one in-app first; missing jq → clear install hint.
- Also emit a machine-readable `Tools/tapi/samples/INDEX.json` (which sample file answers which §11 question) to make the verdict step mechanical.

### Verdict checklist (VER-02)
- `Tools/tapi/SHAPES.md` (committed): the 13 questions from `.planning/research/telegram-parity/tapi-shapes.md` §11, each with: question, sample evidence pointer, VERDICT (confirmed shape / divergence / not-observed), and downstream impact note (which Phase-5 work consumes it).
- Template ships pre-filled with the questions and "PENDING CAPTURE" verdicts so the owner's run + a short Claude pass completes it.
- The reactions-receive go/no-go decision is an explicit final section (feeds Phase 5 scope and the v2 TG-REACT-RECV requirement).

### Claude's Discretion
- Script structure, arg parsing, output file naming, jq scrubbing implementation.
- Whether to include a tiny `--dry-run` that prints the endpoint plan without calling.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Design + research
- `docs/superpowers/specs/2026-07-12-telegram-parity-design.md` — design decisions D1–D7; §D6 defines this phase's contract
- `.planning/research/telegram-parity/tapi-shapes.md` — full tapi endpoint map (§1) + the 13 MUST-VERIFY questions (§11) this phase answers
- `.planning/research/telegram-parity/tg-docs.txt` — extracted Wappi Telegram API documentation (endpoint names/params ground truth)

### Conventions
- `Tools/run-tests-headless.sh` — existing Tools/ bash script conventions (arg style, output dirs, guard checks)
- `CLAUDE.md` — secrets policy (never hardcode; secrets.json is the only source)

</canonical_refs>

<specifics>
## Specific Ideas

- The script's banner must state loudly that it is read-only and what it will NOT do (no sends, no profile mutations) — owner trust matters.
- Wappi tapi auth header = `Authorization: <token>` (same single token as WhatsApp api).
- `messages/get` params: `profile_id`, `chat_id`, `limit` (≤100), `offset`, `order` — do NOT pass `mark_all` (it would mark chats read on the owner's real account: state mutation via a GET-shaped endpoint).
- Docs quirk to verify while capturing: `chats/filter` example shows `"id": ""` and empty `name` for user dialogs; `thumbnail` only appears in the `chats/days/get` example.
</specifics>

<deferred>
## Deferred Ideas

- Automated webhook-payload capture (needs a live n8n tunnel + incoming traffic) — Phase 4's e2e will observe webhook shapes instead.
- Any parser code changes — Phase 5.
</deferred>

---

*Phase: 03-tapi-live-shape-capture*
*Context gathered: 2026-07-12 via design-spec express path*
