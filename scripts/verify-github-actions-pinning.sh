#!/usr/bin/env bash
set -euo pipefail

workflow_directory="${1:-.github/workflows}"
if [[ ! -d "$workflow_directory" ]]; then
  printf 'Workflow directory not found: %s\n' "$workflow_directory" >&2
  exit 1
fi

mapfile -t action_refs < <(
  grep -RhoE '^[[:space:]]*uses:[[:space:]]*[^[:space:]]+' "$workflow_directory" \
    --include='*.yml' --include='*.yaml' \
    | sed -E 's/^[[:space:]]*uses:[[:space:]]*//'
)

if ( ${#action_refs[@]} == 0 )); then
  printf 'No GitHub Action references found in %s\n' "$workflow_directory" >&2
  exit 1
fi

invalid=0
for reference in "${action_refs[@]}"; do
  if [[ "$reference" == ./* ]]; then
    continue
  fi

  if [[ "$reference" =~ ^[^@]+@[[:xdigit:]]{40}$ ]]; then
    continue
  fi

  printf 'Unpinned or mutable GitHub Action reference: %s\n' "$reference" >&2
  invalid=1
done

if (( invalid != 0 )); then
  exit 1
fi

printf 'Verified %d immutable GitHub Action references.\n' "${#action_refs[@]}"
