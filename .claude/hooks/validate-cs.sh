#!/bin/bash
# Post-edit hook: warns about common Unity C# mistakes
INPUT=$(cat)
FILE=$(echo "$INPUT" | jq -r '.tool_input.file_path // .tool_input.filePath // empty' 2>/dev/null)

# Only check .cs files
if [[ "$FILE" != *.cs ]]; then
  exit 0
fi

# Skip if file doesn't exist (was deleted)
if [[ ! -f "$FILE" ]]; then
  exit 0
fi

WARNINGS=""

# Check for async/await in MonoBehaviour (should use coroutines)
if grep -q "async Task\|async void\|await " "$FILE" 2>/dev/null; then
  if grep -q "MonoBehaviour" "$FILE" 2>/dev/null; then
    WARNINGS="${WARNINGS}WARNING: async/await in MonoBehaviour — use coroutines instead.\n"
  fi
fi

# Check for hardcoded API keys/tokens
if grep -qE '(api_key|apikey|token|secret|password)\s*=\s*"[^"]{8,}"' "$FILE" 2>/dev/null; then
  WARNINGS="${WARNINGS}WARNING: Possible hardcoded secret detected — use Secrets class.\n"
fi

# Check for legacy UI Text instead of TMPro
if grep -q "using UnityEngine.UI;\|Text " "$FILE" 2>/dev/null; then
  if ! grep -q "TMPro\|TextMeshPro" "$FILE" 2>/dev/null; then
    WARNINGS="${WARNINGS}NOTE: Consider using TMPro instead of legacy UI Text.\n"
  fi
fi

# Check for Destroy on UI elements (should use SetActive)
if grep -q "Destroy(.*gameObject\|Destroy(.*GameObject" "$FILE" 2>/dev/null; then
  if grep -q "Canvas\|Button\|Panel\|Screen\|Page\|View" "$FILE" 2>/dev/null; then
    WARNINGS="${WARNINGS}NOTE: Consider SetActive(false) instead of Destroy for UI elements.\n"
  fi
fi

if [[ -n "$WARNINGS" ]]; then
  echo -e "$WARNINGS"
fi

exit 0
