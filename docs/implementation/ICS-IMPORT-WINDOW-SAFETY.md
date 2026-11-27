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

Regression tests cover direct parser limits, the hard event cap, oversized
VEVENT rejection with continued parsing, bounded file size, malformed UTF-8,
optional BOM, empty files, cancellation, and stopping at the requested
occurrence limit. The byte cap is enforced on the stream itself;
the parser cap also protects callers that bypass file import.

## Named time-zone recurrence

For a timed event with an explicit `DTSTART;TZID=...`, recurrence instances
are generated from the original local wall-clock date/time. The parser resolves
the zone offset separately for every occurrence, so a weekly lecture at 09:00
remains at 09:00 after a daylight-saving transition.

- `BYDAY` and `EXDATE` date comparisons use the calendar's local wall-clock
  date, not the machine's converted local date.
- An ambiguous local time uses the first occurrence (the larger UTC offset).
- A nonexistent initial `DTSTART` or recurrence instance uses the offset in
  effect immediately before the gap, as specified by RFC 5545 §3.3.5 and
  verified Erratum 4271. The resulting instant is shifted forward by the gap
  when converted back to the named zone; the importer emits a warning.
- A nonexistent recurrence instance remains part of the generated recurrence
  sequence and consumes `COUNT`; `EXDATE` is applied afterwards.
- A UTC DATE-TIME ending in `Z` cannot also declare `TZID`.
- Floating times without `TZID`, custom `VTIMEZONE`, `RDATE`, and
  `BYSETPOS` remain outside the supported subset.

Regression tests cover weekly and monthly Europe/Rome transitions in 2026,
host-time-zone-independent `BYDAY`, ambiguous and nonexistent local times,
a 30-minute Australia/Lord_Howe gap, UTC UNTIL across DST, recurring
ambiguous-time resolution, and invalid UTC/`TZID` combinations.

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
