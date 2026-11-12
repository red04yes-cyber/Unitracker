# ICS import window safety

## Problem

The ICS importer materializes recurring events only inside the requested date
window, but its reconciliation step previously compared that partial result
against every stored occurrence sharing the same UID. Re-importing a bounded
window could therefore delete valid occurrences stored before or after that
window.

## Behavior

- The parser and importer now resolve the effective date window through one
  shared method, including the existing default window (one year before today
  through three years after its start).
- Reconciliation removes an obsolete stored occurrence only when that
  occurrence overlaps the current import window and is absent from the parsed
  result.
- Matching occurrences inside the window are updated as before.
- Stored occurrences outside the selected window are preserved.

No schema, database migration, external dependency, network behavior, or AI
component is changed.

## Regression coverage

`CalendarAndStudyLabTests` verifies:
- a bounded recurring-series import updates matching occurrences, removes a
  stale occurrence inside the window, and preserves occurrences before and
  after the window;
- an import using the default window also preserves occurrences outside it.

The Windows build workflow runs the complete .NET 10 test suite, WPF Release
build, self-contained win-x64 publish, startup smoke test, and portable package
creation. The security workflow independently runs Gitleaks and blocking
NuGet audits.
