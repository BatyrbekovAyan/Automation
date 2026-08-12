#!/usr/bin/env python3
"""
probe-group-mark-read.py — settle empirically what message/mark/read does to a WhatsApp
GROUP chat's unread_count.

WHY THIS EXISTS
---------------
On 2026-08-12 exactly one chat on the owner's account carried unread_count=2, it was a
group (@g.us), and messages/get showed 3 incoming items with isRead=false while 47 older
ones were isRead=true. The app opens that chat and POSTs

    /api/sync/message/mark/read?profile_id=...&mark_all=true   body {"message_id": <newest incoming>}

...yet the count survives into the next launch (the local ReadAckLedger hides it only until
LoadChatsForActiveBot clears the ledger).

The obvious explanation — "mark_all is documented not to work for groups" — does NOT hold up
against the docs. On https://wappi.pro/api-documentation the sentence

    "Работает только для личных чатов, для групп не работает."

appears EXACTLY ONCE, and it is attached to messages/get's mark_all, a different lever on a
different endpoint. message/mark/read's own mark_all carries no group restriction at all.
So docs and observation disagree, and only a live probe can settle it.

VERDICT (2026-08-12) — BOTH HYPOTHESES BELOW ARE FALSIFIED. READ THIS FIRST.
---------------------------------------------------------------------------
Ran against a purpose-made test group (3 incoming messages + one reaction), posting the
exact id ChatManager.SelectChat would post — an OUTGOING reaction's id:

    POST message/mark/read?mark_all=true  body {"message_id": <outgoing reaction id>}
    -> {"status": "done"}
    -> unread_count 3 -> 0, all THREE incoming messages flipped isRead false -> true

So on WhatsApp GROUPS: mark_all=true works, one call is enough, and the body's message_id
behaves as a trigger/watermark rather than as the thing being marked — it need not be
incoming, need not be a real message, and need not be unread. There is no group-aware
per-message path to build, and no bug in the app's mark/read call.

What that leaves: a chat still showing unread_count on next launch was most likely never
OPENED in the app since those messages arrived (SelectChat is the only thing that acks).
The one untested difference between the reproduction and the original «Семья» observation is
message AGE — minutes vs a day. If a recency window is ever suspected, that is the thing to
probe next.

The two hypotheses below are kept for the record; both are now disproven.

FORMER HYPOTHESIS 2 — "IT IS THE REACTION" (read-only evidence, 2026-08-12)
--------------------------------------------------------------------------
Inspecting the offending chat («Семья») without writing anything:

    unread_count      = 2          <- but THREE incoming rows carry isRead=false
    last_message_id   = 3AC6634A71A725A2E95D
    last_message_type = "reaction"                 (a 😂 from another participant)

The two numbers disagree because the server does not count a reaction as unread. And
chats/filter's last_message_id — the id ChatManager.SelectChat feeds to mark/read — IS that
reaction's id. Our own pipeline never renders reaction rows (all three ingest paths do
`if (messageType == Reaction) { HandleReactionEvent(); continue; }`), so:

    MarkOpenChatArrivalsRead  -> ReadAckLedger.NewestIncomingId(batch)  -> never a reaction ✅
    SelectChat                -> vm.LastMessageId (straight off chats/filter) -> CAN be one ✗

So on chat-open we may be acking a reaction's id while the two real unread messages are
never marked — and ReadAckLedger then records that same id, so EffectiveUnread returns 0
and the badge hides locally while the server count survives to the next launch. That
reproduces every reported symptom without groups entering into it (reactions are simply far
more common in group chats). Use --target to separate the two explanations.

WHAT IT DISTINGUISHES
---------------------
After one mark/read with mark_all=true against a group:
  unread_count -> 0        mark_all DOES work for groups; the 2026-08-12 residue had another
                           cause (e.g. the ack never fired, or fired with a stale id).
  unread_count -> N-1      only the body's message_id was marked -> group-aware path needed.
  unread_count unchanged   the call is a no-op for groups.

And the per-message isRead diff separates the two shapes a fix could take:
  every older incoming flipped  -> the newest id acts as a WATERMARK (one call is enough)
  only the body's id flipped    -> per-message marking is required

SAFETY
------
Read-only by default. Every write mode needs an explicit flag AND a typed confirmation.
A write here is real and user-visible: it clears the unread badge in the owner's own
WhatsApp and sends read receipts (blue ticks) to that group's participants. It cannot be
undone from the API (WhatsApp's own "mark as unread" is a manual, app-side action).

USAGE
-----
  # 1. survey (READ ONLY) — which groups currently carry unread messages
  Tools/wappi/probe-group-mark-read.py --profile <profile_id>

  # 2. inspect one chat (READ ONLY) — the isRead breakdown that forms the baseline
  Tools/wappi/probe-group-mark-read.py --profile <id> --chat <chat_id> --inspect

  # 3. THE PROBE (WRITES) — baseline, one mark/read, re-poll, diff
  Tools/wappi/probe-group-mark-read.py --profile <id> --chat <chat_id> --probe-mark-read

  # 4. second lever (WRITES) — does messages/get?mark_all=true touch a group at all?
  #    Docs say no; cheap to falsify since we already call this endpoint.
  Tools/wappi/probe-group-mark-read.py --profile <id> --chat <chat_id> --probe-messages-get

The token is read from Assets/StreamingAssets/secrets.json (wappiAuthToken) and never printed.
"""

from __future__ import annotations  # `dict | None` annotations on the system python (< 3.10)

import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request

API = "https://wappi.pro/api/sync"
PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SECRETS = os.path.join(PROJECT, "Assets", "StreamingAssets", "secrets.json")


def token() -> str:
    try:
        with open(SECRETS, encoding="utf-8") as fh:
            tok = json.load(fh).get("wappiAuthToken", "")
    except FileNotFoundError:
        sys.exit(f"ERROR: {SECRETS} not found (copy secrets.json.example and fill it in).")
    if not tok or tok.startswith("YOUR_"):
        sys.exit("ERROR: wappiAuthToken missing or still the placeholder in secrets.json.")
    return tok


def call(method: str, path: str, params: dict, body: dict | None = None) -> dict:
    url = f"{API}/{path}?{urllib.parse.urlencode(params)}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", token())
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode(errors="replace")[:400]
        sys.exit(f"ERROR: HTTP {exc.code} on {method} {path}\n{detail}")


# --- reads -------------------------------------------------------------------------------

def dialogs(profile: str) -> list:
    return call("GET", "chats/filter", {"profile_id": profile}).get("dialogs", []) or []


def unread_row(profile: str, chat_id: str) -> dict | None:
    for d in dialogs(profile):
        if d.get("id") == chat_id:
            return d
    return None


def messages(profile: str, chat_id: str, limit: int = 60) -> list:
    """messages/get WITHOUT mark_all — this read must not mutate read state."""
    resp = call("GET", "messages/get", {
        "profile_id": profile, "chat_id": chat_id,
        "limit": limit, "offset": 0, "order": "desc",
    })
    return resp.get("messages", []) or []


def incoming_unread(msgs: list) -> list:
    return [m for m in msgs if not m.get("fromMe") and not m.get("isRead")]


def summarize(msgs: list) -> str:
    inc = [m for m in msgs if not m.get("fromMe")]
    unread = incoming_unread(msgs)
    return (f"{len(msgs)} fetched | incoming {len(inc)} "
            f"| incoming unread {len(unread)} | incoming read {len(inc) - len(unread)}")


def show(profile: str, chat_id: str, label: str) -> tuple:
    row = unread_row(profile, chat_id)
    msgs = messages(profile, chat_id)
    count = row.get("unread_count") if row else None
    print(f"\n--- {label} ---")
    print(f"  chats/filter unread_count : {count}")
    print(f"  messages/get              : {summarize(msgs)}")
    unread_ids = [m.get("id") for m in incoming_unread(msgs)]
    if unread_ids:
        print(f"  unread incoming ids       : {', '.join(unread_ids[:8])}"
              + (" …" if len(unread_ids) > 8 else ""))
    return count, {m.get("id"): bool(m.get("isRead")) for m in msgs if not m.get("fromMe")}


def profiles() -> list:
    """All profiles on this account token (read-only) — how to find a profile_id without
    digging PlayerPrefs out of the Unity plist."""
    url = "https://wappi.pro/api/profile/all/get"
    req = urllib.request.Request(url, method="GET")
    req.add_header("Authorization", token())
    with urllib.request.urlopen(req, timeout=30) as resp:
        payload = json.loads(resp.read().decode())
    return payload.get("profiles", payload.get("data", [])) or []


def confirm(what: str, assume_yes: bool) -> None:
    print(f"\n!!! THIS WRITES TO THE OWNER'S REAL WHATSAPP ACCOUNT !!!\n    {what}")
    print("    Effect: clears the unread badge in WhatsApp and sends read receipts to the group.")
    if assume_yes:
        print("    --yes given: proceeding without the interactive prompt.")
        return
    if input("    Type 'yes' to proceed: ").strip().lower() != "yes":
        sys.exit("Aborted — nothing was written.")


def diff_reads(before: dict, after: dict, target: str | None) -> None:
    flipped = [mid for mid, was in before.items() if not was and after.get(mid)]
    print("\n--- isRead diff (incoming only) ---")
    if not flipped:
        print("  nothing flipped to read.")
    else:
        print(f"  {len(flipped)} message(s) flipped unread -> read:")
        for mid in flipped:
            print(f"    {mid}{'   <- the id we posted' if mid == target else ''}")
    if target and len(flipped) == 1 and flipped[0] == target:
        print("\n  VERDICT: per-message only — mark_all did NOT cascade. A group path must mark each id.")
    elif len(flipped) > 1:
        print("\n  VERDICT: cascaded — the posted id acts as a WATERMARK. One call per open is enough.")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--profile", help="Wappi profile_id (WhatsApp); omit with --list-profiles")
    ap.add_argument("--chat", help="chat id, e.g. 1203630xxxxxxxxx@g.us")
    ap.add_argument("--list-profiles", action="store_true", help="READ ONLY: profiles on this token")
    ap.add_argument("--inspect", action="store_true", help="READ ONLY isRead breakdown")
    ap.add_argument("--probe-mark-read", action="store_true", help="WRITES: mark/read + mark_all=true")
    ap.add_argument("--probe-messages-get", action="store_true", help="WRITES: messages/get + mark_all=true")
    ap.add_argument("--target", choices=("reaction", "newest-real"), default="reaction",
                    help="which unread id to post: 'reaction' = today's SelectChat behaviour "
                         "(newest row, reaction included); 'newest-real' = newest non-reaction")
    ap.add_argument("--message-id", help="post this exact id instead of picking one")
    ap.add_argument("--yes", action="store_true", help="skip the typed confirmation (non-tty callers)")
    args = ap.parse_args()

    if args.list_profiles:
        rows = profiles()
        print(f"\nProfiles on this token ({len(rows)}):\n")
        for p in rows:
            print(f"  profile_id={p.get('profile_id', p.get('uuid', '?'))}  "
                  f"authorized={p.get('authorized')}  name={p.get('name', '')}  "
                  f"phone={p.get('phone', '')}")
        return

    if not args.profile:
        sys.exit("ERROR: --profile is required (or use --list-profiles to find one).")

    if not args.chat:
        rows = [d for d in dialogs(args.profile) if (d.get("unread_count") or 0) > 0]
        print(f"\nChats with unread_count > 0 ({len(rows)}):\n")
        for d in sorted(rows, key=lambda r: -(r.get("unread_count") or 0)):
            cid = d.get("id", "")
            kind = "GROUP" if cid.endswith("@g.us") else "1:1  "
            print(f"  [{kind}] unread={d.get('unread_count'):<4} {cid}   {d.get('name', '')[:40]}")
        if not rows:
            print("  (none — nothing to probe right now)")
        print("\nRe-run with --chat <id> --inspect to see one chat's isRead breakdown.")
        return

    if args.probe_mark_read:
        before_count, before_reads = show(args.profile, args.chat, "BASELINE")
        unread = incoming_unread(messages(args.profile, args.chat))
        if not unread:
            sys.exit("\nNothing unread in this chat — pick one with unread_count > 0.")

        # WHICH id we post is the whole experiment (see --target).
        #   reaction    : reproduces today's SelectChat bug — it acks chats/filter's
        #                 last_message_id, which CAN be a type:"reaction" row.
        #   newest-real : what the fix would post — newest non-reaction incoming, the same
        #                 choice ReadAckLedger.NewestIncomingId already makes for arrivals.
        real = [m for m in unread if m.get("type") != "reaction"]
        if args.message_id:
            target = args.message_id
        elif args.target == "newest-real":
            if not real:
                sys.exit("\nNo non-reaction unread message to post — nothing to compare against.")
            target = real[0].get("id")
        else:
            target = unread[0].get("id")      # order=desc, so newest — reaction included
        kind = next((m.get("type") for m in unread if m.get("id") == target), "?")
        print(f"\n  posting id {target}  (type={kind})")
        confirm(f"POST message/mark/read?mark_all=true  body {{message_id: {target}}}  on {args.chat}", args.yes)

        resp = call("POST", "message/mark/read",
                    {"profile_id": args.profile, "mark_all": "true"},
                    {"message_id": target})
        print(f"\n  response: {json.dumps(resp, ensure_ascii=False)[:300]}")

        time.sleep(4)  # chats/filter lags the ack briefly (the very lag ReadAckLedger exists for)
        after_count, after_reads = show(args.profile, args.chat, "AFTER mark/read")
        diff_reads(before_reads, after_reads, target)
        print(f"\n  unread_count: {before_count} -> {after_count}")
        return

    if args.probe_messages_get:
        before_count, before_reads = show(args.profile, args.chat, "BASELINE")
        confirm(f"GET messages/get?mark_all=true on {args.chat}  (docs say this one is 1:1-only)", args.yes)
        call("GET", "messages/get", {
            "profile_id": args.profile, "chat_id": args.chat,
            "limit": 60, "offset": 0, "order": "desc", "mark_all": "true",
        })
        time.sleep(4)
        after_count, after_reads = show(args.profile, args.chat, "AFTER messages/get?mark_all=true")
        diff_reads(before_reads, after_reads, None)
        print(f"\n  unread_count: {before_count} -> {after_count}")
        return

    show(args.profile, args.chat, "INSPECT (read only)")


if __name__ == "__main__":
    main()
