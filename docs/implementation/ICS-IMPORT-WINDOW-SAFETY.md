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


## Input resource limits

File imports now enforce the 10 MiB limit while reading the already-open
stream, rather than trusting a FileInfo.Length pre-check followed by an
unbounded ReadAllTextAsync call. Reads remain asynchronous and cancellable;
the Windows read share mode prevents new write/delete handles while the file
is being read.

The importer accepts UTF-8 only, rejects malformed byte sequences, and strips
one optional UTF-8 BOM. Empty files are rejected. Direct text imports and
direct parser callers are independently capped at 10 Mi characters, and the
parser hard-caps materialized occurrences at 1,000 even if a caller requests a
larger value. Parsing stops once the occurrence cap is reached. A single
VEVENT is also limited to 10,000 logical property lines; an oversized event is
skipped with a warning and later events can still be imported.

ICS line unfolding is lazy, avoiding an additional normalized full-file copy,
split array, and list of every physical line. This reduces peak allocation
without changing the supported event/property semantics.

Regression tests cover direct parser limits, the hard event cap, bounded file
size, malformed UTF-8, optional BOM, empty files, cancellation, and stopping at
the requested occurrence limit. The byte cap is enforced on the stream itself;
the parser cap also protects callers that bypass file import.

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
