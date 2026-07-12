# tapi Live-Shape Capture

Owner-run tooling to capture **real** Wappi tapi (Telegram) response shapes so the
Phase-5 Telegram parser / media work is grounded in observed JSON instead of
undocumented guesses. This is the human gate that closes **Phase 3** (see
`.planning/phases/03-tapi-live-shape-capture/03-HUMAN-UAT.md`).

## Why this is owner-run (not Claude-run)

`Assets/StreamingAssets/secrets.json` is deny-ruled for Claude, so an agent
session cannot probe the live API. Instead **you** run a safe, transparent,
**read-only** script. The token stays on your machine.

## What the script guarantees

- **Read-only.** It calls ONLY 8 GET / list endpoints. It never sends, replies,
  reacts, adds/deletes/logs-out a profile, runs an auth step, changes a webhook,
  or passes `mark_all` (which would mark your real chats read).
- **Token stays local — it never leaves the machine** except inside the HTTPS
  `Authorization` header to wappi.pro. The token is read at runtime from
  `secrets.json`; it is never printed, logged, passed as an argument, or written
  to any sample file.
- **Samples are gitignored — not committed.** Raw payloads may contain phone
  numbers / names, so `Tools/tapi/samples/` is excluded in `.gitignore`. Only
  `SHAPES.md` (structural verdicts + redacted excerpts) is committed.

## Prerequisites

1. **An authorized dev Telegram profile.** If you don't have one, authorize a
   Telegram profile in-app first (Settings → Telegram auth). Use a **dev**
   account — the script reads real chats.
2. **`jq`** installed — `brew install jq` (macOS) / `sudo apt-get install jq` (Linux).
3. **`curl`** (present by default on macOS/Linux).

## How to run

```bash
# See exactly what it would do — no network call, no token read:
Tools/tapi/capture-shapes.sh --dry-run

# Capture (auto-detects the first authorized Telegram profile):
Tools/tapi/capture-shapes.sh

# Options:
Tools/tapi/capture-shapes.sh --profile <id>   # override profile auto-detection
Tools/tapi/capture-shapes.sh --chats 8        # sample more chats (default 5)
Tools/tapi/capture-shapes.sh --help
```

If no authorized Telegram profile is found it exits with a clear message telling
you to authorize one in-app first (exit code 3). Missing `jq` gives an install
hint (exit code 2).

## What it produces

Under `Tools/tapi/samples/` (gitignored):

- `status.json`, `chats_get.json`, `chats_filter.json`, `chats_days_get.json`
- `messages_<chatId>.json` — history per sampled chat
- `message_type_<type>.json` — one full sample per distinct media `type` seen
- `message_id_reply.json` / `message_id_reactions.json` / `message_id_full.json`
- `contact.json` — native-avatar evidence
- **`INDEX.json`** — a machine-readable map of which sample answers which of the
  13 `SHAPES.md` questions (makes the verdict step mechanical)

## Fill in the verdicts (this closes the phase)

1. Open `Tools/tapi/SHAPES.md`.
2. For each of the 13 questions, use `samples/INDEX.json` to find the relevant
   sample file(s), inspect the JSON, and set the **VERDICT** from
   `PENDING CAPTURE` to `confirmed shape` / `divergence` / `not-observed`
   (paste a fully-redacted mini-excerpt if useful — **no raw PII**).
3. Set the final **Reactions-receive go/no-go** decision.
4. Questions 9–13 are pre-marked `DEFERRED` (not observable via a read-only
   capture) — leave them unless a later phase resolves them.
5. Tick the boxes in `03-HUMAN-UAT.md`.
