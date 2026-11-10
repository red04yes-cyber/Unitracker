# GitHub Actions supply-chain policy

## Immutable action references

All third-party GitHub Actions used by repository workflows must be pinned to a
full 40-character commit SHA. A version tag may be kept as an adjacent comment
for readability, but it is not the reference used by the runner.

The current workflow set uses only these upstream actions:

| Action | Human-readable release line | Immutable commit |
| --- | --- | --- |
| `actions/checkout` | `v4` | `11d5960a326750d5838078e36cf38b85af677262` |
| `actions/setup-dotnet` | `v4` | `67a3573c9a986a3f9c594539f4ab511d57bb3ce9` |
| `actions/upload-artifact` | `v4` | `ea165f8d65b6e75b540449e92b4886f43607fa02` |

The references above were resolved from the corresponding official
`actions/*` repository `v4` refs on 10 November 2026. The workflow runner
uses the commit SHA, not the mutable tag.

## Updating an action

1. Review the action's official repository and release notes.
2. Resolve the intended release ref in the official repository and verify that
   it resolves to a commit object.
3. Replace the workflow reference with the complete 40-character SHA. Keep the
   release line in a comment, for example `# v4`.
4. Run `bash scripts/verify-github-actions-pinning.sh`.
5. Confirm the Windows build and security-scan workflows on the pull request.

Do not use a short SHA, branch name, version tag, or a URL as a substitute for
the immutable commit reference. Local composite actions under `./` are not
third-party dependencies and are allowed by the guard.

## Credential handling

Workflows should declare the smallest required permissions. Current build and
validation jobs need only `contents: read`; checkout steps disable persisted
Git credentials because no later step pushes to the repository. Artifact
upload uses the workflow runtime's scoped artifact mechanism and does not
require repository write permission.
