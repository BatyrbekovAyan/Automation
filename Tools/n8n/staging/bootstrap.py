#!/usr/bin/env python3
"""Bring up the isolated STAGING n8n idempotently (audit stage 0.3). See staging/README.md.

Steps (each skipped when already done):
  1. credentials [server stopped]: import the encrypted, name-filtered dev export (OpenAi account,
     Supabase, WappiAuthToken, RevenueCat Webhook — same encryptionKey, values never decrypted here)
     + create «Postgres Staging» from Tools/n8n/.secrets/staging-postgres.json.
  2. server: start, wait for /healthz.
  3. owner + API key: POST /rest/owner/setup (first run only), /rest/login, POST /rest/api-keys
     (all scopes) → Tools/n8n/.secrets/staging-n8n.json.
  4. n8n-API credentials [server stopped]: «n8n account» (n8nApi), «n8nAPIKey» + «n8n Admin API»
     (httpHeaderAuth X-N8N-API-KEY) bound to the STAGING key and http://localhost:<port>/api/v1; «RevenueCat API»
     (httpHeaderAuth Authorization, a dummy value — derive-staging.py points the RevenueCat host at the mock, A3).
  5. workflows: derive-staging.py (+ --bot specs) → import:workflow → publish the ones prod runs
     active (the two clone templates stay inactive, as on prod) → start server.

    python3 Tools/n8n/staging/bootstrap.py --creds-file <encrypted-export.json> --bot wa:stgwa001 --bot tg:stgtg001
    python3 Tools/n8n/staging/bootstrap.py --restart          # just (re)start the server
    python3 Tools/n8n/staging/bootstrap.py --bot wa:stgwa001 --bot tg:stgtg001 --canon-ref 92a7caa2   # the canon at a ref
"""
import argparse
import http.cookiejar
import json
import os
import signal
import sqlite3
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
N8N_DIR = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(N8N_DIR))
HOME = os.path.expanduser(os.environ.get("N8N_STAGING_HOME", "~/.n8n-staging"))
PORT = os.environ.get("N8N_STAGING_PORT", "5678")
BASE = f"http://localhost:{PORT}"
WRAP = os.path.join(HERE, "n8n-staging.sh")
SECRETS = os.path.join(N8N_DIR, ".secrets")
PG_SECRETS = os.path.join(SECRETS, "staging-postgres.json")
N8N_SECRETS = os.path.join(SECRETS, "staging-n8n.json")
PIDFILE = os.path.join(HOME, "n8n.pid")
LOGFILE = os.path.join(HOME, "n8n.log")
# N8N_USER_FOLDER=<HOME> makes n8n keep its state under <HOME>/.n8n/ (not <HOME> itself).
STATE_DIR = os.path.join(HOME, ".n8n")
DB = os.path.join(STATE_DIR, "database.sqlite")
DEV_CONFIG = os.path.expanduser("~/.n8n/config")
STAGING_CONFIG = os.path.join(STATE_DIR, "config")

# Same ACTIVE set as prod (2026-09-17 inventory): everything except the two clone templates.
ACTIVE_CANONICAL = ["0DP79STI3pafXO9B", "2htWSV5IHO8E2CgB", "2islisFH7jjLoPQM", "3qax5J9u2qsT9Vao", "9PTyYcelRQI7bGDb",
                    "KoTuIlk4LMrlvnWI", "SCLcpn6DMDG3Z4VN", "TwWPW3gIyjZS3foR", "Uz6HBBUpAiUqVysB", "XuvOp7TxOImOAmlj",
                    "ZGYr6srzS3rSSXHp", "ZTqpumOpL1rNDOp6", "fXYpCXPKw92EzRz8", "hHwvfOvTCS42pXnq", "jtbssfzXbOxwTK4k",
                    "lmjYsdNcQA2IE5rl", "olmqWMfcFpCEcpiE",
                    "crAuthSession001", "crWappiProxy0001",   # A1 (stage 1): Auth Session + Wappi Proxy
                    "crHandoffReq0001", "crHandoffNtfy001", "crHandoffsApi001",   # A5 (stage 4): handoff tool target, notifier, app webhook
                    "crTestDialog0001", "crHandoffDry0001",   # stage 7.1: the safe test dialog + its dry handoff target
                    "crPriceSearch001",   # V9 (2026-09-28): the reconciling price-list search (bots' + test dialog's tool)
                    "crVerifyIdent001",   # A3 «защита платящих» (2026-10-14): identity-heal after a reinstall (/webhook/VerifyIdentity)
                    "crChannelWatch001",  # A4 «защита платящих» (2026-10-14): «канал отключился» notices, every 10 min (e2e pauses it around its probes)
                    "crSetSchedule001",   # B1 «расписание бота» (2026-10-14): the owner's schedule (/webhook/SetSchedule)
                    "stgEvalRunner001"]   # STAGING-ONLY (stage 6): completions proxy for the eval runner


def sh(*args, check=True, quiet=False):
    r = subprocess.run(list(args), capture_output=True, text=True)
    if not quiet:
        out = (r.stdout + r.stderr).strip()
        if out:
            print("   " + out.replace("\n", "\n   ")[-2000:])
    if check and r.returncode != 0:
        sys.exit(f"command failed ({r.returncode}): {' '.join(args)}")
    return r


def n8n(*args, **kw):
    return sh(WRAP, *args, **kw)


def cred_names():
    if not os.path.exists(DB):
        return {}
    con = sqlite3.connect(DB)
    try:
        return {n: i for i, n in con.execute("select id, name from credentials_entity")}
    except sqlite3.OperationalError:
        return {}
    finally:
        con.close()


def server_running():
    # /healthz answers BEFORE the REST controllers are mounted (observed 2026-09-17: owner/setup
    # 404 right after healthz went 200); /rest/settings is served only once n8n is really ready.
    try:
        with urllib.request.urlopen(f"{BASE}/rest/settings", timeout=3) as r:
            return r.status == 200
    except Exception:
        return False


def start_server():
    if server_running():
        print("2. server already running")
        return
    os.makedirs(HOME, exist_ok=True)
    log = open(LOGFILE, "ab")
    p = subprocess.Popen([WRAP, "start"], stdout=log, stderr=log, start_new_session=True)
    open(PIDFILE, "w").write(str(p.pid))
    for _ in range(120):
        if server_running():
            print(f"2. server up at {BASE} (pid {p.pid}, log {LOGFILE})")
            return
        time.sleep(1)
    sys.exit(f"server did not come up; see {LOGFILE}")


def stop_server():
    if os.path.exists(PIDFILE):
        try:
            pid = int(open(PIDFILE).read().strip())
            os.killpg(pid, signal.SIGTERM)
        except Exception:
            pass
    for _ in range(60):
        if not server_running():
            time.sleep(1)
            return
        time.sleep(1)
    sys.exit("server did not stop")


class Rest:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))

    def call(self, method, path, body=None, headers=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(BASE + path, data=data, method=method,
                                     headers={"Content-Type": "application/json", **(headers or {})})
        try:
            with self.opener.open(req, timeout=30) as r:
                raw = r.read().decode()
                return r.status, (json.loads(raw) if raw else None)
        except urllib.error.HTTPError as e:
            raw = e.read().decode()
            try:
                return e.code, json.loads(raw)
            except Exception:
                return e.code, raw


def ensure_owner_and_key():
    sec = json.load(open(N8N_SECRETS)) if os.path.exists(N8N_SECRETS) else {}
    if sec.get("apiKey"):
        print("3. API key already present")
        return sec
    email = sec.get("email") or "staging-owner@example.invalid"
    password = sec.get("password") or ("Stg-" + os.urandom(12).hex() + "-1A")
    rest = Rest()
    st, body = rest.call("POST", "/rest/owner/setup", {"email": email, "firstName": "Staging", "lastName": "Owner", "password": password})
    print(f"   owner/setup -> {st}")
    st, body = rest.call("POST", "/rest/login", {"emailOrLdapLoginId": email, "password": password})
    if st != 200:
        sys.exit(f"login failed: {st} {body}")
    st, scopes = rest.call("GET", "/rest/api-keys/scopes")
    scopes = scopes.get("data", scopes) if isinstance(scopes, dict) else scopes
    st, key = rest.call("POST", "/rest/api-keys", {"label": "staging-bootstrap", "scopes": scopes, "expiresAt": None})
    if st not in (200, 201):
        sys.exit(f"api key creation failed: {st} {key}")
    key = key.get("data", key)
    sec.update({"email": email, "password": password, "apiKey": key["rawApiKey"], "apiKeyId": key.get("id"), "base": BASE})
    os.makedirs(SECRETS, exist_ok=True)
    with open(N8N_SECRETS, "w") as fh:
        json.dump(sec, fh, indent=1)
    os.chmod(N8N_SECRETS, 0o600)
    print(f"3. owner set up + API key created -> {N8N_SECRETS}")
    return sec


def import_decrypted_creds(entries):
    path = os.path.join(HOME, "creds-import.json")
    with open(path, "w") as fh:
        json.dump(entries, fh)
    os.chmod(path, 0o600)
    n8n("import:credentials", f"--input={path}")
    os.remove(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--creds-file", help="encrypted, name-filtered dev credential export (step 1)")
    ap.add_argument("--bot", action="append", default=[], help="passed to derive-staging.py")
    ap.add_argument("--prompt-file", help="passed to derive-staging.py: the systemMessage the staging bot clones carry "
                                          "(default: the template's own prompt; vertical-bots.py composes a Create-style one)")
    ap.add_argument("--restart", action="store_true")
    ap.add_argument("--stop", action="store_true")
    ap.add_argument("--skip-workflows", action="store_true")
    ap.add_argument("--canon-ref", help="passed to derive-staging.py: import the canon committed at this git ref (a scenario's "
                                        "«before» run, F1F2-plan T10); restore with a plain bootstrap afterwards")
    a = ap.parse_args()
    if a.stop:
        stop_server(); print("stopped"); return
    if a.restart:
        stop_server(); start_server(); return

    # 0. encryption key: the encrypted dev credential export only decrypts under the DEV key, so the
    #    staging state dir must carry the same `config` BEFORE the first import (n8n otherwise mints a
    #    fresh key on first start and every imported blob becomes unreadable — hit on 2026-09-17).
    os.makedirs(STATE_DIR, exist_ok=True)
    if not os.path.exists(STAGING_CONFIG):
        import shutil
        shutil.copy(DEV_CONFIG, STAGING_CONFIG)
        os.chmod(STAGING_CONFIG, 0o600)
        print("0. copied the dev encryptionKey into the staging state dir")
    else:
        dev_key = json.load(open(DEV_CONFIG)).get("encryptionKey")
        stg_key = json.load(open(STAGING_CONFIG)).get("encryptionKey")
        if dev_key != stg_key:
            sys.exit(f"REFUSED: {STAGING_CONFIG} carries a different encryptionKey than {DEV_CONFIG}; the encrypted "
                     f"credential import would be unreadable. Wipe {STATE_DIR} (and .secrets/staging-n8n.json) and re-run.")
    # 1. credentials (server stopped)
    stop_server()
    names = cred_names()
    if a.creds_file and not {"OpenAi account", "Supabase", "WappiAuthToken", "RevenueCat Webhook"} <= set(names):
        n8n("import:credentials", f"--input={a.creds_file}")
        print("1. imported encrypted dev credentials")
    if "Postgres Staging" not in cred_names():
        pg = json.load(open(PG_SECRETS))
        import_decrypted_creds([{"id": "stgPostgres000001", "name": "Postgres Staging", "type": "postgres",
                                 "data": {"host": pg["host"], "port": pg["port"], "database": pg["database"],
                                          "user": pg["user"], "password": pg["password"], "ssl": "disable"}}])
        print("1. created «Postgres Staging»")
    # 2-3. server + owner + key
    start_server()
    sec = ensure_owner_and_key()
    # 4. n8n-API credentials bound to the staging key (server stopped)
    if not {"n8n account", "n8nAPIKey", "n8n Admin API", "Bearer Auth account"} <= set(cred_names()):
        stop_server()
        import_decrypted_creds([
            {"id": "stgN8nAccount0001", "name": "n8n account", "type": "n8nApi", "data": {"apiKey": sec["apiKey"], "baseUrl": f"{BASE}/api/v1"}},
            {"id": "stgN8nApiKey00001", "name": "n8nAPIKey", "type": "httpHeaderAuth", "data": {"name": "X-N8N-API-KEY", "value": sec["apiKey"]}},
            {"id": "stgN8nAdminApi001", "name": "n8n Admin API", "type": "httpHeaderAuth", "data": {"name": "X-N8N-API-KEY", "value": sec["apiKey"]}},
            # Leftover unused binding on the orchestrators' API nodes (README follow-up 4); bound so the
            # import does not dangle. The nodes authenticate with the header credential above.
            {"id": "stgN8nBearer00001", "name": "Bearer Auth account", "type": "httpBearerAuth", "data": {"token": sec["apiKey"]}},
        ])
        print("4. created n8n-API credentials bound to the staging key")
    # A3 (2026-10-14): Verify Identity asks RevenueCat for a customer's aliases with the «RevenueCat API» Header-Auth
    # credential. On staging the host is the mock (derive-staging.py) — the value is a dummy, never a RevenueCat key. Its id
    # is the CANONICAL one (A7, 2026-10-15: the package creates the prod credential with this same fixed id through
    # `n8n import:credentials`), so the derived Verify Identity keeps the canon's binding byte for byte; a stand bootstrapped
    # earlier may still carry `stgRevenueCatApi1` — derive-staging.py re-binds by name, either works.
    if "RevenueCat API" not in cred_names():
        stop_server()
        import_decrypted_creds([{"id": "crRevenueCatApi1", "name": "RevenueCat API", "type": "httpHeaderAuth",
                                 "data": {"name": "Authorization", "value": "Bearer stg-mock-not-a-revenuecat-key"}}])
        print("4. created «RevenueCat API» (dummy, the staging RevenueCat host is the mock)")
    # 5. workflows
    if not a.skip_workflows:
        stop_server()
        r = sh(sys.executable, os.path.join(HERE, "derive-staging.py"), *[x for b in a.bot for x in ("--bot", b)],
               *(("--prompt-file", a.prompt_file) if a.prompt_file else ()), *(("--canon-ref", a.canon_ref) if a.canon_ref else ()))
        n8n("import:workflow", "--separate", f"--input={os.path.join(N8N_DIR, 'workflows-staging')}")
        manifest = json.load(open(os.path.join(N8N_DIR, "workflows-staging", "_manifest.json")))
        for wid in ACTIVE_CANONICAL + [b["workflowId"] for b in manifest["bots"]]:
            n8n("publish:workflow", f"--id={wid}", quiet=True)
        print(f"5. imported {len(manifest['files'])} workflows + {len(manifest['bots'])} staging bots; published {len(ACTIVE_CANONICAL) + len(manifest['bots'])}")
    start_server()
    print("staging ready:", BASE)


if __name__ == "__main__":
    main()
