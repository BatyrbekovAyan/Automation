#!/usr/bin/env bash
# Run the STAGING n8n (private home ~/.n8n-staging, prod-matching version when installed there).
#
#   Tools/n8n/staging/n8n-staging.sh start            # foreground server (webhooks at http://localhost:5678)
#   Tools/n8n/staging/n8n-staging.sh <any n8n CLI>    # e.g. import:workflow --separate --input=Tools/n8n/workflows-staging/
#   Tools/n8n/staging/n8n-staging.sh version
#
# Isolation guarantees (see staging/README.md): its own SQLite + encryption key, its own Postgres
# (staging/pg.sh), Wappi replaced by the mock — the canonical workflows are never imported here
# unmodified (derive-staging.py rewrites every external host and refuses otherwise).
set -euo pipefail
HOME_DIR="${N8N_STAGING_HOME:-$HOME/.n8n-staging}"
PORT="${N8N_STAGING_PORT:-5678}"
BIN="${HOME_DIR}/app/node_modules/.bin/n8n"
[ -x "${BIN}" ] || BIN="$(command -v n8n)"   # fallback: the global dev install (older version)
mkdir -p "${HOME_DIR}"
export N8N_USER_FOLDER="${HOME_DIR}"
export N8N_PORT="${PORT}"
export N8N_HOST=localhost
export N8N_PROTOCOL=http
export WEBHOOK_URL="http://localhost:${PORT}/"
export N8N_SECURE_COOKIE=false
export GENERIC_TIMEZONE=Asia/Almaty
export TZ=Asia/Almaty
export EXECUTIONS_DATA_PRUNE=false
export N8N_DIAGNOSTICS_ENABLED=false
export N8N_VERSION_NOTIFICATIONS_ENABLED=false
export N8N_TEMPLATES_ENABLED=false
export N8N_HIRING_BANNER_ENABLED=false
export N8N_PERSONALIZATION_ENABLED=false
export N8N_LOG_LEVEL="${N8N_LOG_LEVEL:-info}"
# A1 auth (see docs/superpowers/specs/2026-09-17-a1-server-identity-design.md): the same three env
# vars prod needs in docker-compose. The secret lives in the gitignored staging secrets file.
SECRETS_JSON="$(cd "${HOME_DIR}/.." >/dev/null 2>&1; dirname "${BASH_SOURCE[0]}")/../.secrets/staging-n8n.json"
export NODE_FUNCTION_ALLOW_BUILTIN=crypto
export N8N_BLOCK_ENV_ACCESS_IN_NODE=false
export CR_AUTH_SECRET="${CR_AUTH_SECRET:-$(python3 -c "import json,sys;print(json.load(open(sys.argv[1])).get('authSecret',''))" "${SECRETS_JSON}" 2>/dev/null)}"
export CR_AUTH_COMPAT_UNTIL="${CR_AUTH_COMPAT_UNTIL:-$(python3 -c "import json,sys;print(json.load(open(sys.argv[1])).get('compatUntil',''))" "${SECRETS_JSON}" 2>/dev/null)}"
case "${1:-}" in
  version) exec "${BIN}" --version ;;
  start) shift; exec "${BIN}" start "$@" ;;
  *) exec "${BIN}" "$@" ;;
esac
