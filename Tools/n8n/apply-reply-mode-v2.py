#!/usr/bin/env python3
"""Idempotent migration (A2, audit stage 2): make the reply mode CONFIRMABLE server-side.

Set Reply Mode (SCLcpn6DMDG3Z4VN):
  * Validate v2 — same body + optional `version` (client desired-state version, ms timestamp with a
    local monotonic guard); ONE item out (no per-profile fan-out).
  * Upsert Reply Mode — ONE atomic statement for every profile in the request, applying a row only
    when the stored version is older; returns a per-profile report (applied / current suppressed /
    current version) so a stale desire is answered with the server's newer truth, never silently.
  * Shape Response — `{success:true, chatId, requestedVersion, allApplied, applied:[...], written}` or
    `{success:false,error:"db_error"}` with HTTP 500; malformed body → HTTP 400 (was 200 + success:false).
Create WhatsApp/Telegram workflow:
  * `Seed Reply Mode` between `Get Created Workflow Id` and `Activate Created Workflow`: the profile's
    initial '*' row lands BEFORE the clone can auto-reply (the gate reads absence as «reply»). Only
    a client that states `ReplyMode` (semi|auto) writes; a legacy client keeps the old semantics.

    python3 Tools/n8n/apply-reply-mode-v2.py            # patch in place (re-runnable)
    python3 Tools/n8n/apply-reply-mode-v2.py --check
"""
import json
import os
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
WF = os.path.join(HERE, "workflows")
PG_CRED = {"id": "vvRrFiEXzLVqKjOx", "name": "Postgres"}
SRM = "SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json"
CREATES = {"XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json": "WhatsappProfileId",
           "Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json": "TelegramProfileId"}

VALIDATE_JS = r"""const b = $json.body || {};
const sentinel = (id) => !id || id === "-1";
// Harden against the Postgres node's queryReplacement comma-split (WR-03): a comma inside a
// value shifts the positional parameters. Legit Wappi ids never contain commas.
// Length cap + slice(0, 10) bound the fan-out (IN-03); legit client sends <= 2.
const clean = (s) => typeof s === "string" && s.length > 0 && s.length <= 128 && !s.includes(",");
let ids = Array.isArray(b.profileIds) ? b.profileIds.filter(x => clean(x) && !sentinel(x)).slice(0, 10) : [];
ids = Array.from(new Set(ids));
const okChat = clean(b.chatId);
const okSup  = typeof b.suppressed === "boolean";
// A2 (2026-09-17): the desired-state VERSION. The client mints a per-(profile,chat) monotonic number
// (ms timestamp with a local monotonic guard); the upsert applies a row only when the stored version
// is OLDER, so a delayed retry or a reordered response can never overwrite a newer desire. A legacy
// client sends none: the server assigns "now", which keeps its last-arrival semantics.
const hasVersion = (Number.isInteger(b.version) && b.version > 0) || (typeof b.version === "string" && /^[1-9]\d{0,15}$/.test(b.version));
const version = hasVersion ? Number(b.version) : Date.now();
if (ids.length === 0 || !okChat || !okSup) return [{ json: { invalid: true } }];
return [{ json: { invalid: false, profileIds: ids, chatId: b.chatId, suppressed: b.suppressed, version, legacy: !hasVersion } }];"""

UPSERT_SQL = """-- A2: ONE statement for every requested profile — all rows or none (a partial write can never read
-- as «the whole bot switched»). A row is applied only when the stored version is OLDER than the
-- request's; for a stale request the report carries the server's CURRENT value + version so the
-- client adopts it instead of retrying forever. `cur` reads the pre-statement snapshot on purpose
-- (a data-modifying CTE is invisible to the rest of the same statement), which is exactly the
-- "what the server holds if we were not applied" value.
with req as (
  select jsonb_array_elements_text($1::jsonb) as profile_id, $2::text as chat_id, $3::boolean as suppressed, $4::bigint as version
), up as (
  insert into reply_mode_flags (profile_id, chat_id, suppressed, version, updated_at)
  select profile_id, chat_id, suppressed, version, now() from req
  on conflict (profile_id, chat_id) do update
    set suppressed = excluded.suppressed, version = excluded.version, updated_at = now()
    where reply_mode_flags.version < excluded.version
  returning profile_id, suppressed, version
), unpause as (
  -- A5 (stage 4): the owner's explicit «Авто» for THIS chat is the sanctioned resume — it lifts the
  -- handoff pause and closes the chat's pending requests. A bot-wide '*' write never does.
  update chat_reply_state c set handoff_paused = false
  from req where c.profile_id = req.profile_id and c.chat_id = req.chat_id and req.chat_id <> '*' and req.suppressed = false
  returning 1
), closed as (
  update handoff_requests h set status = 'done', done_at = now(), updated_at = now()
  from req where h.profile_id = req.profile_id and h.chat_id = req.chat_id and req.chat_id <> '*' and req.suppressed = false and h.status <> 'done'
  returning 1
)
select r.profile_id as "profileId",
       (up.profile_id is not null) as applied,
       coalesce(up.suppressed, cur.suppressed) as suppressed,
       coalesce(up.version, cur.version, 0)::bigint as version
from req r
left join up on up.profile_id = r.profile_id
left join reply_mode_flags cur on cur.profile_id = r.profile_id and cur.chat_id = r.chat_id;"""

UPSERT_PARAMS = "={{ [ JSON.stringify($json.profileIds), $json.chatId, $json.suppressed, $json.version ] }}"

SHAPE_JS = r"""// A2: the confirmation the client acts on. Every requested profile is reported; `allApplied` is
// the only thing that may be read as «the switch is confirmed». A DB error never reads as success.
const v = $('Validate').first().json;
const rows = $input.all().map(i => i.json).filter(r => r && typeof r === 'object');
if (rows.some(r => r.error)) {
  return [{ json: { success: false, error: 'db_error', _status: 500 }, pairedItem: { item: 0 } }];
}
const applied = rows.filter(r => r.profileId !== undefined).map(r => ({
  profileId: String(r.profileId), applied: r.applied === true,
  suppressed: r.suppressed === true, version: Number(r.version) || 0,
}));
const requested = Array.isArray(v.profileIds) ? v.profileIds : [];
const allApplied = requested.length > 0 && applied.length === requested.length && applied.every(a => a.applied);
return [{ json: {
  success: true, chatId: v.chatId, requestedVersion: v.version, legacy: v.legacy === true,
  allApplied, applied, written: applied.filter(a => a.applied).length, _status: 200,
}, pairedItem: { item: 0 } }];"""

SEED_SQL = """-- A2: the profile's initial reply mode lands BEFORE the clone is activated (the gate reads absence
-- as «reply», so activating first opened a window in which a «Вместе» bot answered). Only a client
-- that states its mode writes; a legacy client (no ReplyMode field) keeps the old absence semantics.
-- Version-guarded like Set Reply Mode: a newer explicit choice already stored is never downgraded.
insert into reply_mode_flags (profile_id, chat_id, suppressed, version, updated_at)
select $1, '*', ($2 = 'semi'), $3::bigint, now()
where nullif($1, '') is not null and $1 <> '-1' and $2 in ('semi', 'auto')
on conflict (profile_id, chat_id) do update
  set suppressed = excluded.suppressed, version = excluded.version, updated_at = now()
  where reply_mode_flags.version < excluded.version;"""


def nid(*parts):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "reply-mode-v2:" + ":".join(parts)))


def node(wf, name):
    return next((n for n in wf["nodes"] if n["name"] == name), None)


def edges(wf, src):
    return wf["connections"].setdefault(src, {"main": [[]]})["main"]


def set_param(n, key, value, changed):
    if n["parameters"].get(key) != value:
        n["parameters"][key] = value
        return True
    return changed


def patch_set_reply_mode():
    path = os.path.join(WF, SRM)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    v = node(wf, "Validate")
    changed = set_param(v, "jsCode", VALIDATE_JS, changed)
    up = node(wf, "Upsert Reply Mode")
    changed = set_param(up, "query", UPSERT_SQL, changed)
    if up["parameters"].get("options", {}).get("queryReplacement") != UPSERT_PARAMS:
        up["parameters"]["options"] = {"queryReplacement": UPSERT_PARAMS}
        changed = True
    for k, val in (("alwaysOutputData", True), ("onError", "continueRegularOutput")):
        if up.get(k) != val:
            up[k] = val
            changed = True
    if node(wf, "Shape Response") is None:
        wf["nodes"].append({"parameters": {"jsCode": SHAPE_JS}, "id": nid("shape"), "name": "Shape Response",
                            "type": "n8n-nodes-base.code", "typeVersion": 2,
                            "position": [up["position"][0] + 220, up["position"][1]]})
        changed = True
    elif node(wf, "Shape Response")["parameters"]["jsCode"] != SHAPE_JS:
        node(wf, "Shape Response")["parameters"]["jsCode"] = SHAPE_JS
        changed = True
    resp = node(wf, "Respond")
    want_body = "={{ Object.fromEntries(Object.entries($json).filter(([k]) => k !== '_status')) }}"
    if resp["parameters"].get("responseBody") != want_body or resp["parameters"].get("options", {}).get("responseCode") != "={{ $json._status || 200 }}":
        resp["parameters"]["responseBody"] = want_body
        resp["parameters"]["options"] = {"responseCode": "={{ $json._status || 200 }}"}
        changed = True
    err = node(wf, "Respond Error")
    if err["parameters"].get("options", {}).get("responseCode") != 400:
        err["parameters"]["options"] = {"responseCode": 400}
        changed = True
    if edges(wf, "Upsert Reply Mode")[0] != [{"node": "Shape Response", "type": "main", "index": 0}]:
        wf["connections"]["Upsert Reply Mode"] = {"main": [[{"node": "Shape Response", "type": "main", "index": 0}]]}
        changed = True
    if edges(wf, "Shape Response")[0] != [{"node": "Respond", "type": "main", "index": 0}]:
        wf["connections"]["Shape Response"] = {"main": [[{"node": "Respond", "type": "main", "index": 0}]]}
        changed = True
    if changed:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def patch_create(fname, profile_field):
    path = os.path.join(WF, fname)
    wf = json.load(open(path, encoding="utf-8"))
    changed = False
    gci = node(wf, "Get Created Workflow Id")
    act = node(wf, "Activate Created Workflow")
    params = "={{ [ $('Unity Webhook').first().json.body." + profile_field + ", String($('Unity Webhook').first().json.body.ReplyMode || ''), String($('Unity Webhook').first().json.body.ReplyModeVersion || '0') ] }}"
    seed = node(wf, "Seed Reply Mode")
    if seed is None:
        wf["nodes"].append({"parameters": {"operation": "executeQuery", "query": SEED_SQL, "options": {"queryReplacement": params}},
                            "id": nid(fname, "seed"), "name": "Seed Reply Mode", "type": "n8n-nodes-base.postgres", "typeVersion": 2.6,
                            "position": [gci["position"][0] + 200, gci["position"][1] + 220],
                            "credentials": {"postgres": dict(PG_CRED)}, "alwaysOutputData": True, "onError": "continueRegularOutput"})
        changed = True
    else:
        changed = set_param(seed, "query", SEED_SQL, changed)
        if seed["parameters"].get("options", {}).get("queryReplacement") != params:
            seed["parameters"]["options"] = {"queryReplacement": params}
            changed = True
    want_url = "=https://n8n.choosereply.com/api/v1/workflows/{{ $('Get Created Workflow Id').first().json.id }}/activate"
    changed = set_param(act, "url", want_url, changed)
    if edges(wf, "Get Created Workflow Id")[0] != [{"node": "Seed Reply Mode", "type": "main", "index": 0}]:
        wf["connections"]["Get Created Workflow Id"] = {"main": [[{"node": "Seed Reply Mode", "type": "main", "index": 0}]]}
        changed = True
    if edges(wf, "Seed Reply Mode")[0] != [{"node": "Activate Created Workflow", "type": "main", "index": 0}]:
        wf["connections"]["Seed Reply Mode"] = {"main": [[{"node": "Activate Created Workflow", "type": "main", "index": 0}]]}
        changed = True
    if changed:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(wf, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
    return changed


def check():
    bad = []
    wf = json.load(open(os.path.join(WF, SRM), encoding="utf-8"))
    if node(wf, "Validate")["parameters"]["jsCode"] != VALIDATE_JS or node(wf, "Upsert Reply Mode")["parameters"]["query"] != UPSERT_SQL \
            or node(wf, "Shape Response") is None or node(wf, "Shape Response")["parameters"]["jsCode"] != SHAPE_JS:
        bad.append(SRM)
    for f, field in CREATES.items():
        wf = json.load(open(os.path.join(WF, f), encoding="utf-8"))
        s = node(wf, "Seed Reply Mode")
        if s is None or s["parameters"]["query"] != SEED_SQL or edges(wf, "Seed Reply Mode")[0][0]["node"] != "Activate Created Workflow":
            bad.append(f)
    return bad


def main():
    if "--check" in sys.argv:
        bad = check()
        print("reply-mode v2:", "OK" if not bad else f"STALE in {bad}")
        sys.exit(2 if bad else 0)
    print(f"{'patched' if patch_set_reply_mode() else 'up to date'}  {SRM}")
    for f, field in CREATES.items():
        print(f"{'patched' if patch_create(f, field) else 'up to date'}  {f}")


if __name__ == "__main__":
    main()
