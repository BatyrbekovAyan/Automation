# Switchable n8n Endpoint + Local Tunnel — Design

- **Date:** 2026-06-29
- **Status:** Approved (design)
- **Sub-project:** 1 of 3 in the "wire app to local n8n for development" effort

## Background

The app's n8n workflows were consolidated (commit `bb98fd4`): 37 downloaded Cloud
workflows triaged to the **7 canonical** ones, now seeded into the local DEV n8n
(`~/.n8n`, n8n 2.27.4) and committed to `Tools/n8n/workflows/`.

Production n8n runs on **n8n Cloud** (`https://bagkz.app.n8n.cloud`) and the app is
hardwired to it: the base URL is a **hardcoded string literal in ~12 places** across
`Manager.cs` and `BotSettings.Auth.cs`. To develop against local n8n we need to flip
the app to a local endpoint without disturbing production, and make local n8n reachable
from a **physical device over Wi-Fi**.

This is the foundation sub-project. Two follow-ups depend on it and are **out of scope**
here:
- **Sub-project 2:** per-bot runtime replies locally (OpenAI/Supabase/Cohere/Postgres
  credentials, incoming Wappi webhooks, the RAG stack).
- **Sub-project 3:** the new semi-auto **suggestions** workflow.

## Goal

Flip the app between Cloud and local n8n with a **single config line**, and make local
n8n reachable from the physical test device, so the full bot-lifecycle plumbing
round-trips against the Mac.

### Definition of done
1. Setting `n8nBaseUrl` in `secrets.json` points the app at local n8n; leaving it empty
   keeps the app on Cloud (production unchanged).
2. From the app on a physical device: **create a bot** → its per-bot workflow is cloned
   **and activated in local n8n**; **edit**, **activate/deactivate**, and **delete** all
   round-trip against local n8n.
3. No cleartext-HTTP exceptions needed on iOS/Android (tunnel provides HTTPS).

> Note: the *per-bot bot actually answering an AI message* (needs OpenAI etc.) is
> sub-project 2. Done-for-#1 = the lifecycle plumbing, verified by the cloned workflow
> appearing active in local n8n.

## Approach (chosen)

**Public tunnel (cloudflared, named) + configurable base URL.** Rejected alternatives:
- *Mac LAN IP + cleartext HTTP* — needs iOS ATS / Android cleartext exceptions, IP drifts
  with DHCP, ties phone+Mac to one Wi-Fi, and doesn't cover incoming webhooks (needed in
  sub-project 2 anyway).
- *Editor-only localhost* — contradicts how the app is actually tested (physical device).

The tunnel gives one stable HTTPS URL reachable by the device from any network and, later,
by Wappi/Telegram for incoming webhooks. Internal handler→n8n-API calls stay on
`localhost` so the clone step doesn't depend on the tunnel.

## Design

### Component A — App: configurable base URL

Files: `Assets/Scripts/Main/Secrets.cs`, `Assets/Scripts/Main/Manager.cs`,
`Assets/Scripts/Main/BotSettings.Auth.cs`, `Assets/StreamingAssets/secrets.json.example`.

1. **`SecretsData`** — add `public string n8nBaseUrl;` (optional field; JsonUtility leaves
   it null/empty when absent).
2. **`Manager`** — add an accessor next to the existing n8n statics
   (`Manager.cs:146`):
   ```csharp
   public static string n8nBaseUrl =>
       string.IsNullOrEmpty(Secrets.Data.n8nBaseUrl)
           ? "https://bagkz.app.n8n.cloud"
           : Secrets.Data.n8nBaseUrl.TrimEnd('/');
   ```
   The default preserves production behavior; `TrimEnd('/')` makes `"…/"` and `"…"` both
   safe so call sites always use `$"{n8nBaseUrl}/path"`.
3. **Replace every hardcoded `https://bagkz.app.n8n.cloud` literal** with
   `$"{n8nBaseUrl}/…"` (or `$"{Manager.n8nBaseUrl}/…"` from `BotSettings.Auth.cs`, since
   the accessor is `public static`). Known sites:

   | File:line | Call |
   |-----------|------|
   | `Manager.cs:2458` | POST `/webhook/CreateWhatsappWorkflow` |
   | `Manager.cs:2601` | POST `/webhook/CreateTelegramWorkflow` |
   | `Manager.cs:2641` | POST `/api/v1/workflows/{id}/activate\|deactivate` |
   | `Manager.cs:2671` | POST `/api/v1/workflows/{id}/activate\|deactivate` |
   | `Manager.cs:2692` | DELETE `/api/v1/workflows/{whatsappWorkflowId}` |
   | `Manager.cs:2710` | DELETE `/api/v1/workflows/{telegramWorkflowId}` |
   | `Manager.cs:2812` | POST `/webhook/EditWhatsappWorkflow` |
   | `Manager.cs:2849` | POST `/webhook/EditTelegramWorkflow` |
   | `Manager.cs:3066` | POST UploadFile |
   | `BotSettings.Auth.cs:510` | POST UploadFile |
   | `Manager.cs:2366`, `Manager.cs:2508` | commented-out duplicates — update or delete for consistency |

   Implementation will re-grep `bagkz.app.n8n.cloud` at execution time and assert **zero
   remaining literals** after the change.
4. **UploadFile path fix:** the app posts to `…/webhook-test/UploadFile`, but the
   workflow registers `…/webhook/UploadFile` when active. Change both upload sites
   (`Manager.cs:3066`, `BotSettings.Auth.cs:510`) to `/webhook/UploadFile` so it works
   against an active workflow behind the tunnel.
5. **`secrets.json.example`** — add `"n8nBaseUrl": ""` with a comment-style hint in the
   PR/README that empty = Cloud, a tunnel URL = local dev. (`secrets.json` itself is
   gitignored; the developer fills it in.)

No other behavior changes. The API key continues to come from `Secrets.Data.n8nAPIKey`.

### Component B — Local n8n infra (no app code)

Lives as docs + a helper script under `Tools/n8n/` (e.g. `Tools/n8n/dev-tunnel.md` and an
optional `Tools/n8n/start-dev.sh`).

1. `n8n start` (editor at `localhost:5678`, data in `~/.n8n`).
2. **Named cloudflared tunnel** for a stable URL (free): `cloudflared tunnel create`,
   route a hostname, run `cloudflared tunnel run`. (Quick alternative for one-off:
   `cloudflared tunnel --url http://localhost:5678`, but the URL rotates — document both,
   recommend named.)
3. Start n8n with `WEBHOOK_URL=<tunnel-https-url>` so webhook nodes register the public
   address (needed for sub-project 2's incoming webhooks; harmless now).
4. Put the tunnel URL into `secrets.json` → `n8nBaseUrl`.

### Component C — Local n8n API key + handler repoint

1. Generate a local n8n API key (Settings → n8n API) → set it in `secrets.json`
   `n8nAPIKey` **and** create a matching local `n8nAPIKey` httpHeaderAuth credential
   (header `X-N8N-API-KEY`) used by the handler workflows.
2. **Repoint the 4 handler workflows' internal n8n-API calls** from
   `https://bagkz.app.n8n.cloud/api/v1/…` to **`http://localhost:5678/api/v1/…`** — they
   execute inside n8n, so localhost is correct and tunnel-independent. Affected:
   `CreateWhatsappWorkflow`, `CreateTelegramWorkflow`, `Edit Whatsapp Workflow`,
   `Edit Telegram Workflow`. Also fix the **trailing-space `/activate ` bug** in
   `CreateWhatsappWorkflow` here.
3. **Credential id remap:** recreated local credentials get new ids, so the imported
   workflows' credential references dangle. Resolve by: create the credential → read its
   id → rewrite the workflow JSON's `credentials.<type>.id` → re-import. For sub-project 1
   only the **n8nAPIKey** credential (handlers) and **WappiAuthToken** (clone sources)
   matter; the rest are deferred to sub-project 2.
4. **Source-of-truth strategy (decided):** `Tools/n8n/workflows/` stays **Cloud-shaped and
   canonical** (committed). The local deltas — `localhost` API URLs + remapped local
   credential ids — are produced by a reproducible, committed transform script
   `Tools/n8n/apply-dev-config.py` that reads the canonical JSONs, rewrites
   `bagkz.app.n8n.cloud/api/v1` → `localhost:5678/api/v1` (and fixes the `/activate ` typo),
   remaps credential ids from a local name→id map, writes to a **gitignored**
   `Tools/n8n/workflows-local/`, and imports that into local n8n. The derived local
   workflows are never committed; only the script is. This keeps git clean and the canonical
   set deployable to Cloud unchanged.

## Risks & edge cases

- **Production safety:** empty `n8nBaseUrl` → Cloud default, so a normal build is
  unaffected. The change is additive.
- **Tunnel URL churn:** a *named* cloudflared tunnel keeps the URL stable; with the quick
  tunnel the URL rotates and `secrets.json` must be updated each run — documented.
- **HTTPS:** tunnel is HTTPS, so no iOS ATS / Android `cleartextTrafficPermitted` changes.
- **webhook vs webhook-test:** fixed by switching the app to `/webhook/UploadFile`;
  requires the Upload File workflow to be **active** in local n8n.
- **Repo cleanliness:** `secrets.json` stays gitignored; the tunnel URL and any local
  credential ids live only in `secrets.json` / the dev n8n DB, never committed.
- **Don't break the clone-by-id contract:** `4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc` ids
  stay fixed; only their internal API URLs/credential refs change.

## Testing

- **App (EditMode, test bridge):** unit-test `Manager.n8nBaseUrl` resolution — empty →
  Cloud default; value → trimmed value; trailing slash handled. Add to
  `Assets/Tests/Editor/Chat/` (compiles into `Assembly-CSharp-Editor`).
- **Static guard:** grep asserts no `bagkz.app.n8n.cloud` literal remains in app source
  after the change.
- **Manual round-trip:** with tunnel + local n8n, from the app on the device — create a
  bot → confirm the cloned per-bot workflow appears **active** in local n8n; edit prompt →
  confirm update; toggle activation; delete → confirm removal. This is the
  definition-of-done check.

## Resolved decisions

1. **Source of truth:** `Tools/n8n/workflows/` stays Cloud-canonical; local deltas are
   derived by the committed `Tools/n8n/apply-dev-config.py` into a gitignored
   `Tools/n8n/workflows-local/` (see Component C.4). Add `Tools/n8n/workflows-local/` to
   `.gitignore`.
2. **Upload File runs active in dev** so the `/webhook/UploadFile` path resolves (not the
   editor "Test URL"). Its webhook path is unique, so no collision.
