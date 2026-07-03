# Local n8n + cloudflared tunnel (dev)

Make the local DEV n8n (`~/.n8n`, `localhost:5678`) reachable from the physical
test device over a stable HTTPS URL, and wire the app to it via `secrets.json`.

Prereqs: `brew install cloudflared` (or download the binary). n8n already installed (2.27.4).

## Option 1 — named tunnel (stable URL, recommended)

Requires a domain you control on Cloudflare.

One-time:
1. `cloudflared tunnel login`
2. `cloudflared tunnel create bagkz-dev`   → note the tunnel UUID
3. `cloudflared tunnel route dns bagkz-dev bagkz-dev.<yourdomain>`
4. Create `~/.cloudflared/config.yml`:
   ```yaml
   tunnel: bagkz-dev
   credentials-file: /Users/ayan/.cloudflared/<UUID>.json
   ingress:
     - hostname: bagkz-dev.<yourdomain>
       service: http://localhost:5678
     - service: http_status:404
   ```

Each dev session:
1. Terminal A: `WEBHOOK_URL=https://bagkz-dev.<yourdomain> n8n start`
2. Terminal B: `cloudflared tunnel run bagkz-dev`
3. Verify: `curl -o /dev/null -w '%{http_code}\n' https://bagkz-dev.<yourdomain>/healthz` → `200`

## Option 2 — quick tunnel (no domain, URL rotates)

```bash
# Terminal A
cloudflared tunnel --url http://localhost:5678
# → prints https://<random>.trycloudflare.com  (changes every run)

# Terminal B — restart n8n with that URL so webhooks register publicly
WEBHOOK_URL=https://<random>.trycloudflare.com n8n start
```
**Each restart the hostname rotates — run the rotation script, don't do it by hand:**

```bash
python3 Tools/n8n/rotate-tunnel.py            # or --dry-run first
```

It auto-detects the new hostname from cloudflared's metrics endpoint
(`127.0.0.1:20241/quicktunnel`) and updates all four stale spots:
1. `secrets.json` → `n8nBaseUrl`
2. the live local Create handlers' "Set Wappi Webhook" callback URL (REST PUT)
3. every existing bot's Wappi webhook registration (`webhook/url/set` per profile,
   WhatsApp `/api/` + Telegram `/tapi/`, incl. inactive bots so reactivation keeps working)
4. checks the running n8n's `WEBHOOK_URL` env and tells you to restart if stale
   (that part it cannot do for you: `WEBHOOK_URL=<new-url> n8n start`)

Then it verifies: tunnel `/healthz`, Wappi `url/get` per profile, and a GET probe of
every webhook through the tunnel (expects the 404 "not registered for GET" hint).
Idempotent — re-run any time; exit 0 = all green. Skipping a step by hand is what
caused the 2026-07-03 "bots stopped replying" outage.

## Wire the app

In `Assets/StreamingAssets/secrets.json` (gitignored):
- `n8nBaseUrl` = your tunnel HTTPS URL (e.g. `https://bagkz-dev.<yourdomain>`).
  Leave it `""` to target production Cloud instead.
- `n8nAPIKey` = the LOCAL n8n API key (n8n → Settings → n8n API → create).

The app reads `n8nBaseUrl` via `Manager.n8nBaseUrl` (empty → Cloud default).

## Importing the dev-config workflows

After creating local credentials (`n8n account`, `n8nAPIKey`, `WappiAuthToken`, …):

```bash
# 1) derive — N8N_PUBLIC_URL = your tunnel host so the Wappi callback host points at the
#    tunnel (NOT Cloud); internal /api/v1 calls are rewritten to localhost automatically.
N8N_PUBLIC_URL=https://<tunnel-host> python3 Tools/n8n/apply-dev-config.py

# 2) stop n8n (the CLI writes the SQLite DB directly), then import (upserts by id)
n8n import:workflow --separate --input=Tools/n8n/workflows-local/

# 3) activate ONLY the 5 handlers (publish). Leave the 2 clone sources inactive —
#    they share webhook path 0091024b-7b46 and would collide if activated.
for id in XuvOp7TxOImOAmlj Uz6HBBUpAiUqVysB 3qax5J9u2qsT9Vao TwWPW3gIyjZS3foR KoTuIlk4LMrlvnWI; do
  n8n publish:workflow --id="$id"
done

# 4) restart
WEBHOOK_URL=https://<tunnel-host> n8n start
```

Verify: `sqlite3 ~/.n8n/database.sqlite "SELECT active,name FROM workflow_entity ORDER BY active DESC;"`
→ 5 handlers `active=1`, `WhatsApp Bot` / `Telegram Bot` `active=0`.

> Note (zsh): `for id in $VAR` does NOT word-split in zsh — use a literal list as above.
> Note: `update:workflow --active` is deprecated → use `publish:workflow`.
