# Branching and release policy

## Canonical UI implementation line

The canonical WPF UI line is the stacked sequence:

1. `work/chatgpt-study-planner-engine` (PR #42): persisted Planner engine and domain tests.
2. `work/chatgpt-ui-complete` (PR #43): the current StudyHub workspace UI.
3. `work/chatgpt-ai-progress-status` (PR #44): Academy naming and the explicit AI in-progress state.
4. `work/unitracker-p0-p2-hardening`: integration hardening based on the canonical tip.

Keep this sequence intact while integrating. The alternative UI branches `work/chatgpt-uiux-phase2` (PR #38), `work/calendar-functional` (PR #36), and `work/studyhub-uiux` (PR #35) overlap in `MainWindow.xaml`, `MainWindow.xaml.cs`, and/or `MainViewModel.cs`; do not merge them into the canonical line without manually reconciling their changes.

## Environments

- Use `develop` as the integration/staging branch.
- Pull requests targeting either `develop` or `main` must run the Windows build, test suite, publish, WPF startup smoke test, secret scan, and dependency vulnerability audit.
- Promote from `develop` to `main` only after review and all required checks pass.
- Do not use a draft or stacked work branch as a production release branch.

## Required repository settings

The workflow files make secret scanning and NuGet vulnerability auditing fail the workflow when a finding is detected. GitHub branch protection must additionally require the relevant check suites before merging into `develop` and `main`.

At the time this policy was added, the repository API reported no rulesets. Configure branch protection / rulesets in GitHub repository settings with pull requests required, stale approvals dismissed after new commits, and these required checks:

- `Build Windows desktop app / build`
- `Security scan / Secret scanning (history + working tree)`
- `Security scan / Vulnerable package audit (blocking)`

Keep force pushes and branch deletion disabled on `develop` and `main`. This settings step requires repository administrator permissions and is not performed by this source change.
