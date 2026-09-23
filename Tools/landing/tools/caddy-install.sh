#!/usr/bin/env bash
# Runs ON THE VPS, from ~/choosereply, as step 3 of tools/deploy.sh --go:
#   bash caddy-install.sh <candidate> <backup> <live-sha256>
# Puts the candidate Caddyfile (rendered locally by caddy-block.py) in place and reloads Caddy — and on
# every failure path leaves the file on disk equal to the one Caddy is running. The same Caddyfile also
# serves n8n.choosereply.com, the app's production backend: a broken file left on disk would take both
# sites down at the next container or VPS restart, even though the running Caddy keeps the old config
# (финальное ревью, I-2).
#   1. the live file is still the one the deploy read (sha256) and the backup is byte-equal to it;
#   2. Caddy validates the CANDIDATE inside the container (a copy in the container's /tmp) — until this
#      passes nothing is written;
#   3. the candidate is written over the live file IN PLACE (same inode: a single-file bind mount keeps
#      seeing a renamed-over file's old inode), the container must see exactly those bytes, Caddy
#      validates the live path once more, then reloads;
#   4. any failure after the write restores the backup in place (reload failed = Caddy still runs the
#      old config, so the old file is the one that matches) and exits non-zero; deploy.sh then rolls the
#      whole deploy back.
# Exit: 0 installed + reloaded · 4 precondition (nothing written) · 5 candidate invalid (nothing written) ·
# 6 install/reload failed, backup restored · 7 install failed AND the restore did not verify (manual).
set -Eeuo pipefail
CAND=${1:?candidate}; BAK=${2:?backup}; LIVE_SHA=${3:?live sha256}
LIVE=Caddyfile
IN_CONTAINER=/etc/caddy/Caddyfile
PROBE=/tmp/cr-Caddyfile.candidate
dc() { docker compose exec -T caddy "$@"; }
sha() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1; }
say() { echo "caddy-install: $*" >&2; }

[[ -f $CAND && -f $BAK && -f $LIVE ]] || { say "missing file (candidate $CAND, backup $BAK or $LIVE)"; exit 4; }
[[ $(sha "$LIVE") == "$LIVE_SHA" ]] || { say "$LIVE changed since the deploy read it — nothing written"; exit 4; }
cmp -s "$LIVE" "$BAK" || { say "backup $BAK differs from $LIVE — nothing written"; exit 4; }

# 2. validate the candidate BEFORE it can replace anything
dc sh -c "cat > $PROBE" < "$CAND" || { say "could not copy the candidate into the container — nothing written"; exit 5; }
if ! dc caddy validate --adapter caddyfile --config "$PROBE"; then
  dc rm -f "$PROBE" || true
  say "candidate is INVALID — $LIVE untouched, Caddy not reloaded"
  exit 5
fi
dc rm -f "$PROBE" || true

# 3/4. install in place; from here on every failure restores the backup
restore() {
  trap - ERR
  set +e
  cat "$BAK" > "$LIVE"
  if cmp -s "$LIVE" "$BAK"; then say "restored $LIVE from $BAK (in place)"; exit 6; fi
  say "RESTORE DID NOT VERIFY — copy $BAK over $LIVE by hand NOW"; exit 7
}
trap restore ERR
cat "$CAND" > "$LIVE"
seen=$(dc sha256sum "$IN_CONTAINER" | cut -d' ' -f1)
[[ $seen == "$(sha "$CAND")" ]] || { say "the container does not see the new file at $IN_CONTAINER (mount?)"; restore; }
dc caddy validate --config "$IN_CONTAINER"
dc caddy reload --config "$IN_CONTAINER"
trap - ERR
say "installed and reloaded"
