#!/usr/bin/env bash
# E2E test for the hardened Upload File / Delete File workflows (2026-07-02).
#
# Run against LOCAL dev n8n (default) after applying apply-upload-hardening.py
# to the instance and creating the price-lists bucket
# (supabase/2026-07-02-price-list-originals-bucket.sql).
#
# Mimics Unity's WWWForm quirk exactly: every AddField text part carries an
# explicit ";type=text/plain" so it lands in n8n as a BINARY part — the
# workflow's extractFromFile id-readers depend on that (verified mimic from
# the 2026-07-02 rollout; never "fix" the readers to $json.body).
#
# Usage: Tools/n8n/test-upload-e2e.sh [base-url]
set -uo pipefail

BASE="${1:-http://localhost:5678}"
STAMP="$(date +%s)"
FILE_ID_1="e2e-${STAMP}-lower"
FILE_ID_2="e2e-${STAMP}-upper"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT
FAILS=0

printf 'товар: Тестовый прайс %s; цена: 5000 тг\n' "$STAMP" > "$TMP_DIR/price.txt"
printf 'garbage' > "$TMP_DIR/price.foo"

upload() { # $1=fileId $2=file-path $3=upload-filename $4=mime
  curl -s -o "$TMP_DIR/resp.json" -w '%{http_code}' -X POST "$BASE/webhook/UploadFile" \
    -F "whatsappWorkflowId=-1;type=text/plain" \
    -F "telegramWorkflowId=-1;type=text/plain" \
    -F "contentType=product;type=text/plain" \
    -F "fileId=$1;type=text/plain" \
    -F "data=@$2;filename=$3;type=$4"
}

check() { # $1=label $2=expected-code $3=actual-code $4=expect-substring-in-body
  local body; body="$(cat "$TMP_DIR/resp.json")"
  if [[ "$3" == "$2" && "$body" == *"$4"* ]]; then
    echo "PASS  $1 (HTTP $3) $body"
  else
    echo "FAIL  $1 — expected HTTP $2 + '$4', got HTTP $3: $body"
    FAILS=$((FAILS + 1))
  fi
}

echo "== 1. lowercase .txt upload → 200 success =="
code="$(upload "$FILE_ID_1" "$TMP_DIR/price.txt" "price.txt" "text/plain")"
check "txt upload" 200 "$code" '"success":true'

echo "== 2. UPPERCASE .TXT filename → 200 (case-insensitive Switch) =="
code="$(upload "$FILE_ID_2" "$TMP_DIR/price.txt" "PRICE.TXT" "text/plain")"
check "TXT upload" 200 "$code" '"success":true'

echo "== 3. unsupported .foo → explicit 415 =="
code="$(upload "e2e-${STAMP}-foo" "$TMP_DIR/price.foo" "price.foo" "application/octet-stream")"
check "foo upload" 415 "$code" '"success":false'

delete_file() { # $1=fileId
  curl -s -o "$TMP_DIR/resp.json" -w '%{http_code}' -X POST "$BASE/webhook/DeleteFile" \
    -H 'Content-Type: application/json' -d "{\"fileId\":\"$1\"}"
}

echo "== 4. DeleteFile removes chunks (proves ingestion) + stored original =="
code="$(delete_file "$FILE_ID_1")"
check "delete 1" 200 "$code" '"deletedChunks":1'
code="$(delete_file "$FILE_ID_2")"
check "delete 2" 200 "$code" '"deletedChunks":1'

echo
if [[ "$FAILS" -eq 0 ]]; then
  echo "E2E OK — also confirm in n8n executions that 'Store Original File' returned"
  echo "a Key (bucket write) and 'Delete Stored Original' ran (404 ok for pre-bucket files)."
else
  echo "E2E FAILED: $FAILS check(s)"
  exit 1
fi
