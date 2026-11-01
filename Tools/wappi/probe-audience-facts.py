#!/usr/bin/env python3
"""Read-only probe for the «Личные и рабочие чаты» live checks (spec §7, plan Task 0).

Prints SIGNS only — never a name, a message text, a caption or a phone number:
booleans (is there a contact name), key sets, categorical values (type, chat_type),
id formats with every digit shown as N and every letter as a, and times relative to
the connection moment or to now. A chat is referred to by a six-character handle
(an HMAC of its id under a key kept on this Mac), stable between runs here and
meaningless anywhere else.

Wappi calls are GET only and never pass mark_all, so reading marks nothing read.
The Wappi token comes from $WAPPI_TOKEN or ~/.config/choosereply/wappi-token
(outside the repository: the repository is public) and is never printed.

  profiles                              every profile on the token
  status   --ch wa|tg --profile P       one profile's connection state
  chats    --ch wa|tg --profile P       the newest chats: contact / user flags / type / id format
  chat     --ch wa|tg --profile P (--chat ID | --handle H)
                                        the chat-list request the server makes for this chat
                                        (does it find the chat, what the card says), the chat's
                                        oldest message against the connection moment, and its
                                        newest page: order, keys, from == chatId
  journal  (--workflow W | --profile P) incoming webhooks of a bot workflow from the n8n
                                        execution journal (PRODUCTION, read-only); the key
                                        comes from $N8N_API_KEY or --n8n-key-file
  selftest                              offline: runs every extractor on synthetic answers and
                                        fails if a name, a text or a number reaches the output
"""

import argparse
import hashlib
import hmac
import json
import os
import re
import secrets
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

CONFIG_DIR = os.path.expanduser("~/.config/choosereply")
TOKEN_FILE = os.path.join(CONFIG_DIR, "wappi-token")
SALT_FILE = os.path.join(CONFIG_DIR, "probe-salt")
BASES = {"wa": "https://wappi.pro/api/", "tg": "https://wappi.pro/tapi/"}
N8N_BASE = "https://n8n.choosereply.com"

KNOWN_DOMAINS = {"c.us", "s.whatsapp.net", "g.us", "lid", "newsletter", "broadcast"}
KNOWN_LITERALS = {"status@broadcast", "777000"}          # public service ids, not anyone's data
CATEGORY = re.compile(r"^[A-Za-z_]{1,32}$")
TIME_KEYS = ("time", "timestamp", "last_timestamp", "last_time", "last_message_time")
MESSAGE_LIST_KEYS = ("messages", "dialogs", "chats", "profiles", "data")

_salt = None
_emit_sink = None


def emit(line=""):
    if _emit_sink is not None:
        _emit_sink.append(line)
    print(line)


# ---- redaction: everything printed goes through these ----

def shape(text):
    # letters first: the N that stands for a digit is a letter too
    return re.sub(r"\d", "N", re.sub(r"[^\W\d_]", "a", text))


def fmt_id(value):
    """The id's format: digits → N, letters → a; a known WhatsApp domain and public service ids are kept."""
    if value is None:
        return "<none>"
    text = str(value)
    if text == "":
        return "<empty>"
    if text in KNOWN_LITERALS:
        return text
    local, at, domain = text.partition("@")
    if not at:
        return shape(local)
    return f"{shape(local)}@{domain if domain in KNOWN_DOMAINS else shape(domain)}"


def cat(value):
    """A categorical value (type, chat_type, wh_type) printed only when it looks like an enum word."""
    if value is None:
        return "<none>"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, str) and CATEGORY.match(value):
        return value
    return "<other>"


def flag(value):
    if value is None:
        return "?"
    if isinstance(value, bool):
        return "1" if value else "0"
    return "<non-bool>"


def filled(obj, key):
    """yes = a non-empty value, empty = the key with an empty value, - = no such key."""
    if not isinstance(obj, dict) or key not in obj:
        return "-"
    value = obj[key]
    if value is None or (isinstance(value, str) and value.strip() == ""):
        return "empty"
    return "yes"


def keys_of(obj):
    return ",".join(sorted(shape(k) if re.search(r"\d", k) else k for k in obj)) if isinstance(obj, dict) else "<not an object>"


def id_forms(obj):
    """Which fields of an object hold an id-like value (a jid or a long digit run) and in what format — never the value."""
    if not isinstance(obj, dict):
        return "-"
    parts = []
    for key, value in sorted(obj.items()):
        if isinstance(value, bool) or not isinstance(value, (str, int)):
            continue
        if re.fullmatch(r"[\w.+-]*@[\w.]+|\d{5,}", str(value)):
            parts.append(f"{shape(key) if re.search(r'[0-9]', key) else key}={fmt_id(value)}")
    return ",".join(parts) or "none"


def digit_count(obj, key):
    """yes/empty/- like filled(), but a filled value is told by its digit count (11d) — never the digits."""
    value = obj.get(key) if isinstance(obj, dict) else None
    if value is None or value == "":
        return filled(obj, key)
    digits = re.sub(r"[^0-9]", "", str(value))
    return f"{len(digits)}d"


def handle(chat_id):
    return hmac.new(salt(), str(chat_id).encode("utf-8"), hashlib.sha256).hexdigest()[:6]


def salt():
    global _salt
    if _salt is None:
        os.makedirs(CONFIG_DIR, exist_ok=True)
        if not os.path.exists(SALT_FILE):
            fd = os.open(SALT_FILE, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, "w") as fh:
                fh.write(secrets.token_hex(32))
        with open(SALT_FILE, encoding="utf-8") as fh:
            _salt = fh.read().strip().encode("utf-8")
    return _salt


# ---- time ----

def to_epoch(value):
    """Unix seconds from seconds, milliseconds or an ISO / «YYYY-MM-DD HH:MM:SS» string; None when unreadable."""
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return value / 1000.0 if value > 1e12 else float(value)
    text = str(value).strip()
    if re.fullmatch(r"\d+(\.\d+)?", text):
        return to_epoch(float(text))
    try:
        parsed = datetime.fromisoformat(text.replace("Z", "+00:00").replace(" ", "T", 1))
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.timestamp()


def first_time(obj):
    if not isinstance(obj, dict):
        return None
    for key in TIME_KEYS:
        moment = to_epoch(obj.get(key))
        if moment:
            return moment
    return None


def rel(moment, reference, unit="min"):
    if moment is None or reference is None:
        return "?"
    delta = moment - reference
    return f"{delta / 60:+.0f} min" if unit == "min" else f"{delta:+.0f} s"


def now():
    return datetime.now(timezone.utc).timestamp()


# ---- extractors (pure: the selftest drives them) ----

def describe_wa_dialog(dialog, at):
    chat_id = dialog.get("id") or dialog.get("chatId") or dialog.get("chat_id")
    contact = dialog.get("contact")
    return (f"{handle(chat_id)}  id={fmt_id(chat_id)}  contact.FullName={filled(contact, 'FullName')}"
            f"  contact.PushName={filled(contact, 'PushName')}"
            f"  name={filled(dialog, 'name')}  deleted={flag(dialog.get('isDeleted'))}"
            f"  blacklist={flag(dialog.get('is_blacklist'))}  unread={dialog.get('unread_count', '?')}"
            f"  last={rel(first_time(dialog), at)}  chat.ids={id_forms(dialog.get('chat'))}"
            f"  contact.ids={id_forms(contact)}")


def describe_tg_dialog(dialog, at):
    chat_id = dialog.get("id") or dialog.get("chatId") or dialog.get("chat_id")
    user = dialog.get("user") if isinstance(dialog.get("user"), dict) else None
    flags = "user=-" if user is None else (
        f"user: Contact={flag(user.get('Contact'))} Bot={flag(user.get('Bot'))}"
        f" Support={flag(user.get('Support'))} Self={flag(user.get('Self'))}")
    return (f"{handle(chat_id)}  id={fmt_id(chat_id)}  type={cat(dialog.get('type'))}  {flags}"
            f"  deleted={flag(dialog.get('isDeleted'))}  blacklist={flag(dialog.get('is_blacklist'))}"
            f"  unread={dialog.get('unread_count', '?')}  last={rel(first_time(dialog), at)}")


def describe_message(message, reference, unit="min"):
    sender, chat = message.get("from"), message.get("chatId")
    same = "?" if sender is None or chat is None else ("1" if str(sender) == str(chat) else "0")
    return (f"t={rel(first_time(message), reference, unit)}  type={cat(message.get('type'))}"
            f"  fromMe={flag(message.get('fromMe'))}  from==chatId={same}  from={fmt_id(sender)}"
            f"  chatId={fmt_id(chat)}  to={fmt_id(message.get('to'))}  phone={digit_count(message, 'phone')}"
            f"  contact_name={filled(message, 'contact_name')}"
            f"  senderName={filled(message, 'senderName')}  chat_type={cat(message.get('chat_type'))}"
            f"  is_bot={cat(message.get('is_bot'))}  wh_type={cat(message.get('wh_type'))}"
            f"  from_where={cat(message.get('from_where'))}  task_id={filled(message, 'task_id')}")


def key_labels(rows, prefix):
    """Distinct key sets, labelled once: K1 = a,b,c …; returns {id(row): label}."""
    labels, seen = {}, {}
    for row in rows:
        keys = keys_of(row)
        if keys not in seen:
            seen[keys] = f"{prefix}{len(seen) + 1}"
            emit(f"  {seen[keys]}: {keys}")
        labels[id(row)] = seen[keys]
    return labels


def webhook_messages(body):
    if isinstance(body, dict) and isinstance(body.get("messages"), list):
        return [m for m in body["messages"] if isinstance(m, dict)]
    return [body] if isinstance(body, dict) else []


# ---- transport ----

def token():
    value = os.environ.get("WAPPI_TOKEN", "").strip()
    if not value and os.path.exists(TOKEN_FILE):
        with open(TOKEN_FILE, encoding="utf-8") as fh:
            value = fh.read().strip()
    if not value:
        sys.exit(f"ERROR: no Wappi token. Put it in {TOKEN_FILE} (chmod 600) or $WAPPI_TOKEN — never inside the repository.")
    return value


def http_get(url, headers):
    request = urllib.request.Request(url, method="GET")
    for name, value in headers.items():
        request.add_header(name, value)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read().decode("utf-8", "replace")
            status = response.status
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8", "replace")
        status = error.code
    except urllib.error.URLError as error:
        sys.exit(f"ERROR: no answer ({type(error.reason).__name__})")
    try:
        return status, json.loads(raw) if raw else None
    except ValueError:
        return status, None


def wappi(channel, path, params):
    if any(key.lower() == "mark_all" for key in params):
        sys.exit("refused: mark_all marks messages read — this probe only reads")
    status, payload = http_get(f"{BASES[channel]}{path}?{urllib.parse.urlencode(params)}", {"Authorization": token()})
    if status != 200:
        emit(f"  HTTP {status}; answer keys: {keys_of(payload)}")
    return status, payload


def rows_of(payload):
    if isinstance(payload, list):
        return [r for r in payload if isinstance(r, dict)]
    if isinstance(payload, dict):
        for key in MESSAGE_LIST_KEYS:
            if isinstance(payload.get(key), list):
                return [r for r in payload[key] if isinstance(r, dict)]
    return []


def profile_entry(channel, profile_id):
    _, payload = wappi(channel, "profile/all/get", {})
    for entry in rows_of(payload):
        if (entry.get("profile_id") or entry.get("uuid") or entry.get("id")) == profile_id:
            return entry
    return None


def chat_rows(channel, profile_id, source):
    if channel == "tg" and source == "get":
        return wappi("tg", "sync/chats/get", {"profile_id": profile_id, "limit": 20, "offset": 0, "order": "desc"})
    return wappi(channel, "sync/chats/filter", {"profile_id": profile_id})


def card_request(channel, chat_id):
    """The one chat-list request the server makes for a chat without a decision: Telegram reads the first page
    of chats/get; WhatsApp filters chats/filter by the digits of the chat id (the filter also matches chat names
    and last messages, so the caller still picks the dialog whose id equals the chat id)."""
    if channel == "tg":
        return "sync/chats/get", {"limit": 20, "offset": 0, "order": "desc"}
    return "sync/chats/filter", {"client_name": re.sub(r"\D", "", str(chat_id))}


# ---- commands ----

def cmd_profiles(_args):
    at = now()
    for channel in ("wa", "tg"):
        _, payload = wappi(channel, "profile/all/get", {})
        rows = rows_of(payload)
        emit(f"{channel}: {len(rows)} profile(s)")
        labels = key_labels(rows, "P")
        for entry in rows:
            profile_id = entry.get("profile_id") or entry.get("uuid") or entry.get("id")
            phone = re.sub(r"\D", "", str(entry.get("phone") or ""))
            emit(f"  {profile_id}  your name for it: {entry.get('name', '')!s:.24}  authorized={flag(entry.get('authorized'))}"
                 f"  authorized_at={rel(to_epoch(entry.get('authorized_at')), at)}"
                 f"  logouted_at={rel(to_epoch(entry.get('logouted_at')), at)}"
                 f"  phone={len(phone)} digits  keys={labels[id(entry)]}")


def cmd_status(args):
    status, payload = wappi(args.ch, "sync/get/status", {"profile_id": args.profile})
    if status != 200 or not isinstance(payload, dict):
        return
    account = payload.get("account") if isinstance(payload.get("account"), dict) else None
    echo = payload.get("profile_id") or payload.get("uuid")
    emit(f"authorized={flag(payload.get('authorized'))}  echoes profile={'1' if echo == args.profile else '0'}"
         f"  phone={filled(payload, 'phone')}  account.phone={filled(account, 'phone') if account else '-'}")
    emit(f"keys: {keys_of(payload)}")
    entry = profile_entry(args.ch, args.profile)
    if entry is not None:
        emit(f"authorized_at={rel(to_epoch(entry.get('authorized_at')), now())} (from profile/all/get)")


def cmd_chats(args):
    status, payload = chat_rows(args.ch, args.profile, args.source)
    rows = rows_of(payload)[: args.limit]
    emit(f"{len(rows)} chat(s), newest first as Wappi returned them (handle  id-format  signs)")
    if status != 200:
        return
    labels = key_labels(rows, "K")
    at = now()
    describe = describe_tg_dialog if args.ch == "tg" else describe_wa_dialog
    for index, dialog in enumerate(rows):
        emit(f"#{index:<2} {describe(dialog, at)}  keys={labels[id(dialog)]}")


def resolve_chat(args):
    if args.chat:
        return args.chat
    _, payload = chat_rows(args.ch, args.profile, args.source)
    for dialog in rows_of(payload):
        chat_id = dialog.get("id") or dialog.get("chatId") or dialog.get("chat_id")
        if chat_id and handle(chat_id) == args.handle:
            return chat_id
    sys.exit(f"ERROR: no chat with handle {args.handle} on this profile (run `chats` first).")


def cmd_chat(args):
    chat_id = resolve_chat(args)
    entry = profile_entry(args.ch, args.profile)
    connected = to_epoch(args.connected_at) if args.connected_at else to_epoch((entry or {}).get("authorized_at"))
    emit(f"chat {handle(chat_id)}  id={fmt_id(chat_id)}  connection moment known={'1' if connected else '0'}")

    path, params = card_request(args.ch, chat_id)
    _, card_payload = wappi(args.ch, path, {"profile_id": args.profile, **params})
    dialogs = rows_of(card_payload)
    card = next((d for d in dialogs if str(d.get("id") or d.get("chatId") or d.get("chat_id")) == str(chat_id)), None)
    emit(f"card ({path}, the server's request): {len(dialogs)} dialog(s), this chat found={'1' if card else '0'}")
    if card is not None:
        emit(f"  {(describe_tg_dialog if args.ch == 'tg' else describe_wa_dialog)(card, now())}")

    _, oldest_payload = wappi(args.ch, "sync/messages/get",
                              {"profile_id": args.profile, "chat_id": chat_id, "limit": 1, "order": "asc"})
    oldest = rows_of(oldest_payload)
    emit("oldest (limit=1&order=asc), time against the connection moment:")
    for message in oldest:
        emit(f"  {describe_message(message, connected)}  keys: {keys_of(message)}")

    _, page_payload = wappi(args.ch, "sync/messages/get",
                            {"profile_id": args.profile, "chat_id": chat_id, "limit": args.limit, "order": "desc"})
    page = rows_of(page_payload)
    times = [first_time(m) for m in page]
    known = [t for t in times if t is not None]
    newest_first = all(a >= b for a, b in zip(known, known[1:]))
    emit(f"newest page (limit={args.limit}&order=desc): {len(page)} row(s), newest first={'1' if newest_first else '0'}")
    labels = key_labels(page, "M")
    at = now()
    for index, message in enumerate(page):
        twin = " same-second-as-previous" if index and times[index] is not None and times[index] == times[index - 1] else ""
        emit(f"  #{index:<2} {describe_message(message, at, 's')}  keys={labels[id(message)]}{twin}")
    if oldest and page and first_time(oldest[0]) is not None and known:
        emit(f"order=asc honoured (oldest not newer than the page's last row)="
             f"{'1' if first_time(oldest[0]) <= min(known) else '0'}")
    # The chat list may name a chat by its number while its messages carry another id (an @lid): does the
    # card request the server would build from the MESSAGE's chatId still find the chat?
    others = sorted({str(m.get("chatId")) for m in oldest + page if m.get("chatId") and str(m.get("chatId")) != str(chat_id)})
    for other in others[:1]:
        other_path, other_params = card_request(args.ch, other)
        _, other_payload = wappi(args.ch, other_path, {"profile_id": args.profile, **other_params})
        other_rows = rows_of(other_payload)
        hit = next((d for d in other_rows
                    if str(d.get("id") or d.get("chatId") or d.get("chat_id")) in (str(chat_id), other)), None)
        emit(f"card (by the messages' chatId {fmt_id(other)}): {len(other_rows)} dialog(s), this chat found="
             f"{'1' if hit else '0'}{' as ' + fmt_id(hit.get('id')) if hit else ''}")


def n8n_key(args):
    value = os.environ.get("N8N_API_KEY", "").strip()
    if not value and args.n8n_key_file:
        with open(os.path.expanduser(args.n8n_key_file), encoding="utf-8") as fh:
            value = fh.read().strip()
    if not value:
        sys.exit("ERROR: no n8n key — $N8N_API_KEY or --n8n-key-file")
    return value


def trigger_body(execution):
    run_data = (((execution.get("data") or {}).get("resultData") or {}).get("runData")) or {}
    for runs in run_data.values():
        for run in runs or []:
            for branch in ((run.get("data") or {}).get("main") or []):
                for item in branch or []:
                    body = (item or {}).get("json", {}).get("body")
                    if isinstance(body, dict):
                        return body
    return None


def bot_workflow(key, profile_id):
    """The bot clone whose Webhook path is the profile id (read-only listing)."""
    cursor = None
    while True:
        query = urllib.parse.urlencode({"limit": 100, **({"cursor": cursor} if cursor else {})})
        status, payload = http_get(f"{N8N_BASE}/api/v1/workflows?{query}", {"X-N8N-API-KEY": key})
        if status != 200:
            sys.exit(f"ERROR: workflow listing answered HTTP {status}")
        for workflow in (payload or {}).get("data") or []:
            for node in workflow.get("nodes") or []:
                if node.get("type") == "n8n-nodes-base.webhook" and (node.get("parameters") or {}).get("path") == profile_id:
                    return workflow.get("id")
        cursor = (payload or {}).get("nextCursor")
        if not cursor:
            sys.exit("ERROR: no workflow listens on this profile's webhook path")


def cmd_journal(args):
    emit("PRODUCTION n8n execution journal — read-only")
    key = n8n_key(args)
    workflow = args.workflow or bot_workflow(key, args.profile)
    emit(f"workflow {workflow}")
    query = urllib.parse.urlencode({"workflowId": workflow, "includeData": "true", "limit": args.limit})
    status, payload = http_get(f"{N8N_BASE}/api/v1/executions?{query}", {"X-N8N-API-KEY": key})
    if status != 200:
        emit(f"  HTTP {status}; answer keys: {keys_of(payload)}")
        return
    executions = (payload or {}).get("data") or []
    emit(f"{len(executions)} execution(s), newest first")
    listing = {}
    at = now()
    for execution in executions:
        started = to_epoch(execution.get("startedAt"))
        body = trigger_body(execution)
        emit(f"exec {execution.get('id')}  started={rel(started, at)}  status={cat(execution.get('status'))}"
             f"  webhook body={'yes' if body else 'not saved / not found'}")
        if body:
            emit(f"  body keys: {keys_of(body)}")
            for message in webhook_messages(body):
                emit(f"  {describe_message(message, started, 's')}")
                emit(f"    message keys: {keys_of(message)}")
                emit(f"    whatsapp_chat_id={fmt_id(message.get('whatsapp_chat_id'))}"
                     f"  contact_phone={digit_count(message, 'contact_phone')}  is_me={flag(message.get('is_me'))}"
                     f"  contact_username={filled(message, 'contact_username')}  platform={cat(message.get('platform'))}"
                     f"  from_where={cat(message.get('from_where'))}"
                     f"  contact_name==senderName={same_value(message.get('contact_name'), message.get('senderName'))}")
                if args.profile:
                    own = own_number(args, listing)
                    emit("    own number (get/status) == " + "  ".join(
                        f"{field}:{same_value(digits_of(message.get(field)), own)}"
                        for field in ("chatId", "from", "to", "whatsapp_chat_id")))
                    emit(f"    chatId {handle(message.get('chatId'))}: {list_match(args, message.get('chatId'), listing)}")
                    card = listing.get("dialogs_by_id", {}).get(str(message.get("chatId")))
                    if card is not None and args.ch == "tg":
                        emit(f"    card now: {describe_tg_dialog(card, now())}")
                    elif card is not None:
                        book = (card.get("contact") or {}).get("FullName") if isinstance(card.get("contact"), dict) else None
                        emit(f"    card contact.FullName={filled(card.get('contact'), 'FullName')}"
                             f"  contact_name==card.FullName={same_value(message.get('contact_name'), book)}")


def digits_of(value):
    return re.sub(r"[^0-9]", "", str(value)) if value else ""


def own_number(args, cache):
    """The connected account's own number, digits only, from get/status (Telegram: account.phone first)."""
    if "own" not in cache:
        _, payload = wappi(args.ch, "sync/get/status", {"profile_id": args.profile})
        account = payload.get("account") if isinstance(payload, dict) and isinstance(payload.get("account"), dict) else {}
        phone = (account.get("phone") if args.ch == "tg" else None) or (payload or {}).get("phone")
        cache["own"] = digits_of(phone)
    return cache["own"]


def same_value(left, right):
    """1 / 0 when both are non-empty strings, '-' otherwise — a comparison, never the values."""
    if not isinstance(left, str) or not isinstance(right, str) or not left or not right:
        return "-"
    return "1" if left.strip() == right.strip() else "0"


def list_match(args, chat_id, cache):
    """Where the webhook's chatId sits in the chat list: the same id, or a row whose chat.LID is that id."""
    if not chat_id:
        return "no chatId"
    if "rows" not in cache:
        _, payload = chat_rows(args.ch, args.profile, "filter")
        cache["rows"] = rows_of(payload)
        cache["dialogs_by_id"] = {str(d.get("id")): d for d in cache["rows"] if d.get("id")}
    for dialog in cache["rows"]:
        list_id = dialog.get("id") or dialog.get("chatId") or dialog.get("chat_id")
        lid = (dialog.get("chat") or {}).get("LID") if isinstance(dialog.get("chat"), dict) else None
        if str(list_id) == str(chat_id):
            return f"in the list as {handle(list_id)} (the same id)"
        if lid and str(lid) == str(chat_id):
            return f"in the list as {handle(list_id)} via chat.LID (list id {fmt_id(list_id)})"
    return f"not in the list ({len(cache['rows'])} rows read)"


# ---- selftest ----

SENTINELS = ("Сауле", "Темирова", "Secret", "СЕКРЕТ", "секрет", "7701234567", "FL00D", "caption-text", "Петров", "T-9f8e7d")


def cmd_selftest(_args):
    global _salt, _emit_sink
    _salt = b"selftest-key"
    _emit_sink = []
    wa_dialog = {"id": "77012345678@c.us", "contact": {"FullName": "Сауле Темирова", "PushName": "Secret"},
                 "name": "Сауле", "isDeleted": False, "unread_count": 2, "last_timestamp": 1761400000,
                 "chat": {"id": "77012345678@c.us", "lid": "123456789012345@lid", "name": "Сауле"}}
    wa_group = {"id": "120363012345678@g.us", "name": "Семья Петров", "isDeleted": False}
    wa_lid = {"id": "123456789012345@lid", "contact": {"FullName": ""}}
    tg_dialog = {"id": "7701234567", "type": "user", "name": "Сауле",
                 "user": {"Contact": True, "Bot": False, "Support": False, "Self": False, "FirstName": "Сауле"},
                 "last_timestamp": "2026-10-25 10:00:00"}
    tg_service = {"id": "777000", "type": "user", "user": {"Contact": False, "Bot": False, "Support": True, "Self": False}}
    message = {"id": "ABCD1234", "type": "chat", "body": "СЕКРЕТНЫЙ текст", "caption": "caption-text",
               "from": "77012345678@c.us", "chatId": "77012345678@c.us", "fromMe": False,
               "contact_name": "Сауле Темирова", "senderName": "Secret", "time": 1761400100,
               "chat_type": "dialog", "is_bot": False, "phone": "77012345678", "to": "77019876543@c.us"}
    tg_message = {"id": 55, "type": "text", "body": "секрет", "from": "FL00D", "chatId": "7701234567", "time": 1761400100}
    api_send = dict(message, fromMe=True, from_where="api", task_id="T-9f8e7d")    # a send through the API, as tapi marks it
    webhook = {"messages": [dict(message, wh_type="incoming_message")]}

    at = 1761400200.0
    key_labels([wa_dialog, wa_group, wa_lid], "K")
    for dialog in (wa_dialog, wa_group, wa_lid):
        emit(describe_wa_dialog(dialog, at))
    for dialog in (tg_dialog, tg_service):
        emit(describe_tg_dialog(dialog, at))
    for row in (message, tg_message, api_send):
        emit(describe_message(row, at, "s"))
        emit(keys_of(row))
    for row in webhook_messages(webhook):
        emit(describe_message(row, at))

    output = "\n".join(_emit_sink)
    leaks = [s for s in SENTINELS if s in output]
    expectations = {
        "WhatsApp person id keeps its domain": "NNNNNNNNNNN@c.us" in output,
        "group domain kept": "@g.us" in output,
        "@lid kept": "@lid" in output,
        "777000 kept": "777000" in output,
        "a username is masked to its shape": "aaNNa" in output,
        "contact name presence is told": "contact.FullName=yes" in output and "contact.FullName=empty" in output,
        "Telegram flags are told": "Contact=1" in output and "Support=1" in output,
        "from == chatId is told": "from==chatId=1" in output and "from==chatId=0" in output,
        "a stranger's profile name is told apart from the book name": "contact.PushName=yes" in output,
        "the dialog's other ids are told by format": "chat.ids=id=NNNNNNNNNNN@c.us,lid=NNNNNNNNNNNNNNN@lid" in output,
        "a message's phone is told by its digit count": "phone=11d" in output,
        "an API send's marks are told (the value of from_where, task_id only as present)":
            "from_where=api" in output and "task_id=yes" in output and "from_where=<none>" in output and "task_id=-" in output,
        "the WhatsApp card is filtered by the chat id's digits":
            card_request("wa", wa_dialog["id"]) == ("sync/chats/filter", {"client_name": "77012345678"}),
        "the Telegram card is the first chats/get page":
            card_request("tg", tg_dialog["id"]) == ("sync/chats/get", {"limit": 20, "offset": 0, "order": "desc"}),
    }
    _emit_sink = None
    failed = [name for name, ok in expectations.items() if not ok]
    print("\nselftest:", "LEAK " + ", ".join(leaks) if leaks else "no name / text / number in the output",
          "|", "missing: " + ", ".join(failed) if failed else "every sign present")
    sys.exit(1 if leaks or failed else 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("profiles")
    sub.add_parser("selftest")
    for name in ("status", "chats", "chat"):
        command = sub.add_parser(name)
        command.add_argument("--ch", choices=("wa", "tg"), required=True)
        command.add_argument("--profile", required=True)
        command.add_argument("--source", choices=("get", "filter"), default="get",
                             help="Telegram chat list: chats/get (default) or chats/filter")
        command.add_argument("--limit", type=int, default=20 if name == "chats" else 10)
        if name == "chat":
            target = command.add_mutually_exclusive_group(required=True)
            target.add_argument("--chat")
            target.add_argument("--handle")
            command.add_argument("--connected-at", help="override the connection moment (ISO or unix seconds)")
    journal = sub.add_parser("journal")
    owner = journal.add_mutually_exclusive_group(required=True)
    owner.add_argument("--workflow", help="the bot clone's workflow id")
    owner.add_argument("--profile", help="find the bot clone by its Webhook path (= the profile id)")
    journal.add_argument("--limit", type=int, default=10)
    journal.add_argument("--ch", choices=("wa", "tg"), default="wa",
                         help="with --profile: the channel whose chat list the webhook chatId is matched against")
    journal.add_argument("--n8n-key-file")
    args = parser.parse_args()
    {"profiles": cmd_profiles, "status": cmd_status, "chats": cmd_chats, "chat": cmd_chat,
     "journal": cmd_journal, "selftest": cmd_selftest}[args.command](args)


if __name__ == "__main__":
    main()
