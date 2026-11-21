#!/usr/bin/env bash
# Fails when a workflow uses a third-party action that is not pinned to a full 40-character commit SHA.
set -euo pipefail

bad=0
while IFS= read -r line; do
  file="${line%%:*}"
  uses="$(printf '%s' "$line" | sed -E 's/^[^:]+:[0-9]+:\s*-?\s*uses:\s*//')"
  case "$uses" in
    ./*) continue ;;                                   # a local action
  esac
  ref="${uses#*@}"
  ref="${ref%% *}"
  if ! printf '%s' "$ref" | grep -Eq '^[0-9a-f]{40}$'; then
    echo "Unpinned action in ${file}: ${uses}" >&2
    bad=1
  fi
done < <(grep -rEn '^\s*-?\s*uses:' .github/workflows || true)

exit "$bad"
