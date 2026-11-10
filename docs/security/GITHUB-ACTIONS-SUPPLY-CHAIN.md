# GitHub Actions supply-chain policy

## Immutable action references

All third-party GitHub Actions used by repository workflows must be pinned to a
full 40-character commit SHA. A version tag may be kept as an adjacent comment
for readability, but it is not the reference used by the runner.

The current workflow set uses only these upstream actions:

| Action | Human-readable release line | Immutable commit |
| --- | --- | --- |
| `actions/checkout` | `v6` | `d23441a48e516b6c34aea4fa41551a30e30af803` |
| `actions/setup-dotnet` | `v6` | `0f4a354dd5547a28c70b7853dbd95d5b67886e73` |
| `actions/upload-artifact` | `v6` | `b7c566a772e6b6bfb58ed0dc250532a479d7789f` |

The references above were resolved from the corresponding official
`actions/*` repository `v6` refs on 10 November 2026. Their `action.yml`
metadata declares `runs.using: node24`; the workflow runner uses the commit
SHA, not the mutable tag.

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
