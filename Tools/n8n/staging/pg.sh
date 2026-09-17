#!/usr/bin/env bash
# Private PostgreSQL for the isolated STAGING n8n (audit remediation, stage 0.3).
#
# Uses the self-contained Postgres bundled by the `pgserver` wheel (installed into
# ~/.venvs/pgstaging — no Homebrew build, no admin rights, nothing system-wide). Data dir and
# password live OUTSIDE the repo; the password is generated once into the gitignored
# Tools/n8n/.secrets/staging-postgres.json (the same file the n8n credential import reads).
#
#   Tools/n8n/staging/pg.sh init      # initdb + role/db + migrations (idempotent)
#   Tools/n8n/staging/pg.sh start|stop|status
#   Tools/n8n/staging/pg.sh psql [args]   # psql into choosereply_staging as the n8n role
#   Tools/n8n/staging/pg.sh migrate   # (re)apply Tools/n8n/{sql,supabase} migrations (idempotent)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
N8N_DIR="$(cd "${HERE}/.." && pwd)"
PGB="${PG_BIN:-$HOME/.venvs/pgstaging/lib/python3.9/site-packages/pgserver/pginstall/bin}"
PGDATA="${STAGING_PGDATA:-$HOME/.n8n-staging/pgdata}"
PGPORT="${STAGING_PGPORT:-5433}"
PGHOST=127.0.0.1
DB=choosereply_staging
ROLE=n8n_staging
SECRETS="${N8N_DIR}/.secrets/staging-postgres.json"
SOCKDIR="$HOME/.n8n-staging/pgsock"

need() { [ -x "${PGB}/$1" ] || { echo "missing ${PGB}/$1 — pip install pgserver into ~/.venvs/pgstaging first" >&2; exit 2; }; }
need initdb; need pg_ctl; need psql

password() {
  if [ ! -s "${SECRETS}" ]; then
    mkdir -p "$(dirname "${SECRETS}")"; chmod 700 "$(dirname "${SECRETS}")"
    local pw; pw="$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32)"
    printf '{"host":"%s","port":%s,"database":"%s","user":"%s","password":"%s"}\n' "${PGHOST}" "${PGPORT}" "${DB}" "${ROLE}" "${pw}" > "${SECRETS}"
    chmod 600 "${SECRETS}"
  fi
  python3 -c "import json;print(json.load(open('${SECRETS}'))['password'])"
}

cmd_status() { "${PGB}/pg_ctl" -D "${PGDATA}" status; }
cmd_start() {
  mkdir -p "${SOCKDIR}"
  if "${PGB}/pg_ctl" -D "${PGDATA}" status >/dev/null 2>&1; then echo "already running"; return; fi
  "${PGB}/pg_ctl" -D "${PGDATA}" -l "${PGDATA}/server.log" -o "-p ${PGPORT} -k ${SOCKDIR} -h ${PGHOST}" -w start
}
cmd_stop() { "${PGB}/pg_ctl" -D "${PGDATA}" -m fast -w stop; }
cmd_psql() { PGPASSWORD="$(password)" "${PGB}/psql" -h "${PGHOST}" -p "${PGPORT}" -U "${ROLE}" -d "${DB}" -v ON_ERROR_STOP=1 "$@"; }

cmd_init() {
  mkdir -p "$(dirname "${PGDATA}")"
  if [ ! -s "${PGDATA}/PG_VERSION" ]; then
    local pwfile; pwfile="$(mktemp)"; password > "${pwfile}"
    "${PGB}/initdb" -D "${PGDATA}" -U postgres --auth=scram-sha-256 --pwfile="${pwfile}" -E UTF8 --locale=C >/dev/null
    rm -f "${pwfile}"
    echo "initdb done -> ${PGDATA}"
  fi
  cmd_start
  local pw; pw="$(password)"
  PGPASSWORD="${pw}" "${PGB}/psql" -h "${PGHOST}" -p "${PGPORT}" -U postgres -d postgres -v ON_ERROR_STOP=1 -q <<SQL
do \$\$ begin
  if not exists (select 1 from pg_roles where rolname = '${ROLE}') then
    create role ${ROLE} login password '${pw}';
  end if;
  -- Supabase's client roles exist so the RLS/revoke migrations apply verbatim (NOLOGIN here).
  if not exists (select 1 from pg_roles where rolname = 'anon') then create role anon nologin; end if;
  if not exists (select 1 from pg_roles where rolname = 'authenticated') then create role authenticated nologin; end if;
  if not exists (select 1 from pg_roles where rolname = 'service_role') then create role service_role nologin bypassrls; end if;
end \$\$;
SQL
  if ! PGPASSWORD="${pw}" "${PGB}/psql" -h "${PGHOST}" -p "${PGPORT}" -U postgres -d postgres -tAc "select 1 from pg_database where datname='${DB}'" | grep -q 1; then
    PGPASSWORD="${pw}" "${PGB}/psql" -h "${PGHOST}" -p "${PGPORT}" -U postgres -d postgres -q -c "create database ${DB} owner ${ROLE};"
    echo "database ${DB} created (owner ${ROLE} — the n8n credential user, table owner like prod)"
  fi
  cmd_migrate
}

cmd_migrate() {
  # Prelude: the LangChain Postgres memory node creates n8n_chat_histories lazily on the first
  # reply; the conversation-outcomes migration ALTERs it, so staging creates it up front with
  # the node's own shape (id / session_id / message jsonb — flat {type,content} rows).
  cmd_psql -q -c "create table if not exists public.n8n_chat_histories (id serial primary key, session_id varchar(255) not null, message jsonb not null);"
  # Order matters: base tables first, then the additive column/RLS migrations.
  local files=(
    "${N8N_DIR}/sql/2026-08-21-billing-schema.sql"
    "${N8N_DIR}/sql/2026-08-21-billing-rls.sql"
    "${N8N_DIR}/sql/2026-08-21-bot-key.sql"
    "${N8N_DIR}/sql/2026-08-21-deleted-reason.sql"
    "${N8N_DIR}/sql/2026-08-22-product-id.sql"
    "${N8N_DIR}/sql/2026-08-26-suggestion-counts.sql"
    "${N8N_DIR}/sql/2026-08-27-landing-leads.sql"
    "${N8N_DIR}/supabase/2026-07-19-reply-mode-flags.sql"
    "${N8N_DIR}/supabase/2026-07-07-conversation-outcomes.sql"
    "${N8N_DIR}/sql/2026-09-17-auth-installs.sql"
  )
  for f in "${files[@]}"; do
    [ -s "$f" ] || { echo "skip (missing) $f"; continue; }
    echo "apply $(basename "$f")"
    cmd_psql -q -f "$f"
  done
  # Staging-only migrations (new tables introduced by the remediation stages) live here:
  if [ -d "${HERE}/sql" ]; then
    for f in "${HERE}"/sql/*.sql; do [ -e "$f" ] || continue; echo "apply staging $(basename "$f")"; cmd_psql -q -f "$f"; done
  fi
  echo "migrations applied to ${DB}@${PGHOST}:${PGPORT}"
}

case "${1:-}" in
  init) cmd_init ;;
  start) cmd_start ;;
  stop) cmd_stop ;;
  status) cmd_status ;;
  migrate) cmd_migrate ;;
  psql) shift; cmd_psql "$@" ;;
  *) echo "usage: $0 init|start|stop|status|migrate|psql [args]" >&2; exit 2 ;;
esac
