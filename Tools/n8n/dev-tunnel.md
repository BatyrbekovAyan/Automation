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
Each restart you must update `secrets.json` → `n8nBaseUrl` with the new URL.

## Wire the app

In `Assets/StreamingAssets/secrets.json` (gitignored):
- `n8nBaseUrl` = your tunnel HTTPS URL (e.g. `https://bagkz-dev.<yourdomain>`).
  Leave it `""` to target production Cloud instead.
- `n8nAPIKey` = the LOCAL n8n API key (n8n → Settings → n8n API → create).

The app reads `n8nBaseUrl` via `Manager.n8nBaseUrl` (empty → Cloud default).

## Importing the dev-config workflows

After creating local credentials (`n8nAPIKey`, `WappiAuthToken`, …), run:
```bash
python3 Tools/n8n/apply-dev-config.py        # derives Tools/n8n/workflows-local/
n8n import:workflow --separate --input=Tools/n8n/workflows-local/
```
Then in the n8n UI activate the handler workflows (`CreateWhatsappWorkflow`,
`CreateTelegramWorkflow`, `Edit Whatsapp Workflow`, `Edit Telegram Workflow`,
`Upload File`); leave the two clone sources (`WhatsApp Bot`, `Telegram Bot`) inactive.

> Stop n8n before any `n8n import:workflow` (the CLI writes the SQLite DB directly).
