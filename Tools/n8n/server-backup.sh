#!/bin/sh
# Nightly n8n backup on the choosereply VPS (installed at ~/choosereply/backup.sh, cron 03:15).
# Exports workflows (json) + credentials (ENCRYPTED) + the encryption-key config from the
# running container, tars them into ~/backups/n8n-YYYY-MM-DD.tar.gz, keeps the last 14.
# Restore path = n8n import:credentials / import:workflow — same flow as the 2026-08-27 migration.
# Limitation: backups live on the same disk; off-site copy is a follow-up.
set -e
cd /home/ubuntu/choosereply
mkdir -p /home/ubuntu/backups
STAMP=$(date +%F)
docker compose exec -T n8n sh -c 'rm -rf /tmp/bk && mkdir -p /tmp/bk && n8n export:workflow --backup --output=/tmp/bk/workflows/ >/dev/null 2>&1 && n8n export:credentials --all --output=/tmp/bk/credentials.json >/dev/null 2>&1 && cp /home/node/.n8n/config /tmp/bk/config'
rm -rf "/home/ubuntu/backups/n8n-$STAMP"
docker compose cp n8n:/tmp/bk "/home/ubuntu/backups/n8n-$STAMP"
docker compose exec -T n8n rm -rf /tmp/bk
tar -C /home/ubuntu/backups -czf "/home/ubuntu/backups/n8n-$STAMP.tar.gz" "n8n-$STAMP"
rm -rf "/home/ubuntu/backups/n8n-$STAMP"
chmod 600 "/home/ubuntu/backups/n8n-$STAMP.tar.gz"
ls -1t /home/ubuntu/backups/n8n-*.tar.gz | tail -n +15 | xargs -r rm -f
echo "$(date -Is) backup ok: n8n-$STAMP.tar.gz $(du -h "/home/ubuntu/backups/n8n-$STAMP.tar.gz" | cut -f1)"
