#!/usr/bin/env bash
# Deploy dist/ to the VPS. Default is a DRY RUN: prints «DRY: <command>», touches nothing, no network
# (only the local lint of tools/Caddyfile.site runs). --go deploys — only with the owner's yes.
# macOS bash 3.2 compatible. The VPS Caddyfile ALSO serves n8n.choosereply.com (the app's production
# backend), so every failure path below ends with the file on disk equal to the config Caddy runs.
#
#  0. preflight — read-only on the VPS, stops before anything is changed:
#     · the live Caddyfile is read (ssh cat) and transformed LOCALLY by tools/caddy-block.py: it stops
#       (and prints the diff) if the live choosereply.com block carries a directive tools/Caddyfile.site
#       would drop — /review/<hash>/ (the App Review video) lives there; `caddy validate` cannot know a
#       dropped `file_server browse`/`handle`/header was wanted (финальное ревью, I-3). Only that one
#       block changes; the www redirect and the n8n block stay byte-identical (whole-file diff printed);
#     · the caddy container reads exactly ~/choosereply/Caddyfile at /etc/caddy/Caddyfile and serves
#       ~/choosereply/site at /srv/site (sha256 on both sides) — the script's assumptions, checked;
#     · the LIVE config validates (it is the rollback target) and the NEW one validates in the container
#       BEFORE anything is written; the HTTP codes of every /review/<dir>/, of every file in it (HEAD —
#       the App Review video is not downloaded) and of n8n /healthz are recorded.
#  1. backup ~/choosereply/site → site.bak-<stamp>, Caddyfile → Caddyfile.bak-<stamp>; the rollback
#     command is printed right here. From this point ANY failure — an error, a failed smoke check,
#     Ctrl-C, a dropped ssh session — runs that rollback automatically (EXIT trap).
#  2. rsync dist/assets/ first, then the root files, both WITHOUT --delete: index.html never lands
#     before the assets/<hash>/ it points to (M-3); review/ and old asset dirs stay.
#  3. tools/caddy-install.sh on the VPS: candidate validated again, written IN PLACE, the container must
#     see those bytes, validate, reload; any failure there restores Caddyfile.bak-<stamp> in place.
#  4. smoke: /, /privacy.html, /terms.html answer 200; every /review/<dir>/, every review/*/* file and
#     n8n /healthz answer what they answered before the deploy. A miss rolls everything back.
# Restores are IN PLACE (rsync --delete into site/, cp over the Caddyfile): a bind-mounted directory or
# file keeps its inode — `rm -rf site && mv …` left the container serving a deleted directory (I-2).
set -Eeuo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; DIST="$HERE/../dist"; HOST=choosereply
SITE_URL=https://choosereply.com; N8N_URL=https://n8n.choosereply.com
GO=0
for a in "$@"; do
  case "$a" in
    --go) GO=1 ;;
    *) echo "unknown option: $a (use --go)"; exit 2 ;;
  esac
done
STAMP=$(date +%Y%m%d-%H%M%S); RTMP="/tmp/cr-deploy-$STAMP"
test -f "$DIST/index.html" || { echo "no dist/ — run node tools/build.mjs"; exit 1; }
test -d "$DIST/assets" || { echo "no dist/assets/ — run node tools/build.mjs"; exit 1; }
LTMP=$(mktemp -d "${TMPDIR:-/tmp}/cr-deploy.XXXXXX")

run() { if [[ $GO == 1 ]]; then "$@"; else echo "DRY: $*"; fi; }
# /review/ is asked with HEAD: the App Review video is recorded and re-checked without downloading it
code() {
  if [[ $1 == */review/* ]]; then curl -s -I -o /dev/null -w '%{http_code}' --max-time 20 "$1" || true
  else curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$1" || true; fi
}
urlpath() { python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "$1"; }
sha() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1; }
stop() { echo "STOP: $*" >&2; exit 1; }
DC='docker compose exec -T caddy'
VALIDATE="$DC caddy validate --config /etc/caddy/Caddyfile"
RELOAD="$DC caddy reload --config /etc/caddy/Caddyfile"
ROLLBACK_REMOTE="cd ~/choosereply && rsync -a --delete site.bak-$STAMP/ site/ && cp Caddyfile.bak-$STAMP Caddyfile && $VALIDATE && $RELOAD"
ROLLBACK="ssh $HOST '$ROLLBACK_REMOTE'"

PHASE=preflight; BACKED_UP=0; DONE=0
# ${NAME} whenever a non-ASCII byte follows (the «» below): macOS /bin/bash 3.2 in a UTF-8 locale reads
# that byte as part of the name, `set -u` then kills this trap before its rollback (повторное ревью, N-1;
# tools/test/deploy-go.test.mjs runs --go under en_US.UTF-8 against a fake VPS).
finish() {
  local rc=$?
  trap - EXIT INT TERM HUP
  set +e
  if [[ $GO == 1 && $DONE == 0 ]]; then
    [[ $rc == 0 ]] && rc=1
    if [[ $BACKED_UP == 1 ]]; then
      echo >&2
      echo "FAILED during «${PHASE}» (exit $rc) — rolling back automatically:" >&2
      echo "  $ROLLBACK" >&2
      if ssh "$HOST" "$ROLLBACK_REMOTE"; then
        echo "rolled back: site/ and Caddyfile restored in place from *.bak-$STAMP, Caddy reloaded" >&2
      else
        echo "ROLLBACK DID NOT COMPLETE — run it by hand NOW: $ROLLBACK" >&2
      fi
    else
      echo "FAILED during «${PHASE}» (exit $rc) — nothing on the VPS was changed" >&2
    fi
  fi
  rm -rf "$LTMP"
  exit "$rc"
}
trap finish EXIT
trap 'exit 130' INT; trap 'exit 143' TERM; trap 'exit 129' HUP

echo "== 0. preflight (read-only on the VPS; stops before anything is changed)"
python3 "$HERE/caddy-block.py" lint "$HERE/Caddyfile.site"
BEFORE=""; LIVE_SHA='<sha256 of the live Caddyfile>'
if [[ $GO == 1 ]]; then
  ssh "$HOST" 'cat ~/choosereply/Caddyfile' > "$LTMP/Caddyfile.live"
  python3 "$HERE/caddy-block.py" check "$LTMP/Caddyfile.live" "$HERE/Caddyfile.site" \
    || stop "the live choosereply.com block does not match what tools/Caddyfile.site replaces (see above)"
  python3 "$HERE/caddy-block.py" render "$LTMP/Caddyfile.live" "$HERE/Caddyfile.site" "$LTMP/Caddyfile.new"
  echo "--- whole Caddyfile, live → new (only the choosereply.com block may change):"
  diff -u "$LTMP/Caddyfile.live" "$LTMP/Caddyfile.new" || true
  LIVE_SHA=$(sha "$LTMP/Caddyfile.live")
  M=$(ssh "$HOST" "cd ~/choosereply && { sha256sum Caddyfile site/index.html; $DC sha256sum /etc/caddy/Caddyfile /srv/site/index.html; } | cut -d' ' -f1 | tr '\n' ' '")
  read -r H_CF H_IX C_CF C_IX <<< "$M"
  [[ ${H_CF:-} == "$LIVE_SHA" && ${C_CF:-} == "$LIVE_SHA" ]] || stop "the caddy container does not read ~/choosereply/Caddyfile at /etc/caddy/Caddyfile (host ${H_CF:-?}, container ${C_CF:-?}) — fix deploy.sh/caddy-install.sh first"
  [[ -n ${H_IX:-} && ${H_IX:-} == "${C_IX:-}" ]] || stop "the caddy container does not serve ~/choosereply/site at /srv/site — fix root in tools/Caddyfile.site first"
  ssh "$HOST" "cd ~/choosereply && $VALIDATE" || stop "the LIVE Caddyfile does not validate — it is the rollback target, fix it first"
  ssh "$HOST" "mkdir -p $RTMP"
  scp -q "$LTMP/Caddyfile.new" "$HERE/caddy-install.sh" "$HOST:$RTMP/"
  ssh "$HOST" "cd ~/choosereply && $DC sh -c 'cat > /tmp/cr-Caddyfile.candidate' < $RTMP/Caddyfile.new && $DC caddy validate --adapter caddyfile --config /tmp/cr-Caddyfile.candidate; rc=\$?; $DC rm -f /tmp/cr-Caddyfile.candidate; exit \$rc" \
    || stop "the NEW Caddyfile does not validate — nothing was changed"
  # every review/<dir>/ AND every file in it (review/*/*): v1 has no `browse`, so the directory answers
  # 404 before and after and would never notice the video itself going missing (повторное ревью, N-3)
  REVIEW=$(ssh "$HOST" 'cd ~/choosereply/site 2>/dev/null && { ls -d review/*/ 2>/dev/null; find review -mindepth 2 -maxdepth 2 -type f 2>/dev/null; } | LC_ALL=C sort || true')
  while IFS= read -r p; do
    [[ -n $p ]] || continue
    u="$SITE_URL/$(urlpath "$p")"; BEFORE="$BEFORE $u=$(code "$u")"
  done <<< "$REVIEW"
  BEFORE="$BEFORE $N8N_URL/healthz=$(code "$N8N_URL/healthz")"
  echo "before:$BEFORE"
else
  echo "DRY: ssh $HOST 'cat ~/choosereply/Caddyfile' > <local tmp>/Caddyfile.live"
  echo "DRY: python3 tools/caddy-block.py check  <live> tools/Caddyfile.site   (prints the diff; STOPS if a live directive would be dropped)"
  echo "DRY: python3 tools/caddy-block.py render <live> tools/Caddyfile.site <local tmp>/Caddyfile.new   (only the choosereply.com block changes)"
  echo "DRY: ssh $HOST sha256 of Caddyfile + site/index.html on the host == in the container at /etc/caddy/Caddyfile + /srv/site/index.html"
  echo "DRY: ssh $HOST 'cd ~/choosereply && $VALIDATE'   (the live config — the rollback target — must be valid)"
  echo "DRY: scp Caddyfile.new caddy-install.sh $HOST:$RTMP/; validate the NEW file in the container (--adapter caddyfile) — nothing written yet"
  echo "DRY: record HTTP codes of $SITE_URL/review/<dir>/, of every review/<dir>/<file> (HEAD) and of $N8N_URL/healthz"
fi

echo "== 1. backup"
PHASE=backup
run ssh "$HOST" "cd ~/choosereply && cp -a site site.bak-$STAMP && cp -a Caddyfile Caddyfile.bak-$STAMP"
if [[ $GO == 1 ]]; then BACKED_UP=1; fi
echo "rollback (keep this line): $ROLLBACK"
echo "   from here on ANY failure (error, smoke miss, Ctrl-C, lost ssh) runs it automatically"

echo "== 2. files (assets first, then the pages that point at them; no --delete)"
PHASE=files
run rsync -rlptvz --exclude '.DS_Store' "$DIST/assets/" "$HOST:choosereply/site/assets/"
run rsync -rlptvz --exclude '.DS_Store' --exclude '/assets/' "$DIST/" "$HOST:choosereply/site/"

echo "== 3. Caddy (tools/caddy-install.sh: validate, write in place, verify the container sees it, reload; restores on failure)"
PHASE=caddy
run ssh "$HOST" "cd ~/choosereply && bash $RTMP/caddy-install.sh $RTMP/Caddyfile.new Caddyfile.bak-$STAMP $LIVE_SHA"

echo "== 4. smoke"
PHASE=smoke
if [[ $GO == 1 ]]; then
  bad=0
  for u in / /privacy.html /terms.html; do
    c=$(code "$SITE_URL$u"); echo "$c $SITE_URL$u"; [[ $c == 200 ]] || bad=1
  done
  for pair in $BEFORE; do
    u=${pair%=*}; was=${pair##*=}; c=$(code "$u")
    echo "$c $u (before: $was)"; [[ $c == "$was" ]] || bad=1
  done
  [[ $bad == 0 ]] || stop "smoke check failed"
  DONE=1
  ssh "$HOST" "rm -rf $RTMP" || true
else
  echo "DRY: expect 200 on $SITE_URL/ /privacy.html /terms.html; review/ dirs + files and $N8N_URL/healthz as before; a miss rolls back"
fi
echo "done — rollback, if ever needed: $ROLLBACK"
