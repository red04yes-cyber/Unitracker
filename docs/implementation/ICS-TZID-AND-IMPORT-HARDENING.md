# ICS TZID recurrence and bounded import

Date: 13 November 2026

## Behavior

The parser now expands recurring events with an explicit `TZID` as local wall-clock
dates and times, resolving the zone offset for each occurrence. A weekly class at
09:00 therefore remains at 09:00 after the spring daylight-saving transition.

- An ambiguous local time during the autumn overlap uses the first occurrence
  (the numerically larger UTC offset), consistent with RFC 5545 DATE-TIME handling.
- A nonexistent initial DTSTART is rejected as a malformed event.
- A nonexistent later recurrence (for example 02:30 during the spring clock jump)
  is skipped and reported in the import warnings.
- BYDAY and EXDATE comparisons use the calendar's wall-clock date, not the host
  machine's converted local date.
- UTC and floating values without an explicit TZID retain their existing behavior.

The parser still does not interpret custom VTIMEZONE definitions, RDATE or
BYSETPOS. Floating-time DST edge cases remain an explicit limitation rather
than being guessed.

## Import resource limits

- `IcsImportService.ImportFileAsync` opens the selected file once and enforces
  the 10 MiB byte limit while streaming. It no longer relies only on a
  `FileInfo.Length` pre-check followed by an unbounded `ReadAllTextAsync`.
- The stream is read asynchronously with cancellation and `FileShare.Read`,
  preventing a concurrent writer from changing the opened file during the read
  on Windows.
- File imports require valid UTF-8; an optional UTF-8 BOM is removed.
- `IcsCalendarParser.Parse` independently caps input at 10 Mi characters and
  emitted events at 1,000, even when called without `IcsImportService`.
- Recurrence expansion retains its 20,000-iteration guard.

## Targeted security and integrity review

Thirty checks were performed against the import/parser path. “Pass” means the
control was present and verified in code; “hardened” identifies behavior added
in this slice.

| # | Control checked | Result |
|---:|---|---|
| 1 | Empty or missing file path is rejected | Pass |
| 2 | Missing selected file is rejected | Pass |
| 3 | Empty file is rejected after opening the stream | Pass |
| 4 | Actual bytes read cannot exceed 10 MiB | Hardened |
| 5 | The size limit is enforced on the opened stream, not only metadata | Hardened |
| 6 | Concurrent writers are excluded by the read share mode on Windows | Hardened |
| 7 | File reads remain asynchronous | Pass |
| 8 | Cancellation is passed to every stream read | Pass |
| 9 | Invalid UTF-8 is rejected instead of silently replaced | Hardened |
| 10 | UTF-8 BOM is handled without becoming part of the first property | Hardened |
| 11 | Direct parser calls have an input-character limit | Hardened |
| 12 | Event limit must be positive and cannot exceed 1,000 | Hardened |
| 13 | Reversed or empty date windows are rejected | Pass |
| 14 | Default materialization window is finite | Pass |
| 15 | Recurrence iteration has a hard upper bound | Pass |
| 16 | Materialized occurrences cannot exceed the requested event cap | Pass |
| 17 | Non-positive RRULE INTERVAL does not create a zero-step loop | Pass |
| 18 | Non-positive RRULE COUNT does not create an unbounded count | Pass |
| 19 | Invalid UNTIL values produce a warning instead of aborting the whole import | Pass |
| 20 | Unsupported recurrence frequencies do not generate occurrences | Pass |
| 21 | BYDAY values are mapped through a fixed weekday allowlist | Pass |
| 22 | Malformed EXDATE values are ignored with a warning | Pass |
| 23 | Unknown/invalid TZID values and UTC DATE-TIME values that also declare TZID reject only the affected event | Pass |
| 24 | Nonexistent initial TZID wall times are rejected | Hardened |
| 25 | Nonexistent later TZID occurrences are skipped with a warning | Hardened |
| 26 | Ambiguous TZID wall times resolve deterministically to the first occurrence | Hardened |
| 27 | Imported titles have control characters removed and a 400-character cap | Pass |
| 28 | Imported locations have control characters removed and a 400-character cap | Pass |
| 29 | Imported notes have control characters removed and a 12,000-character cap | Pass |
| 30 | Stable IDs, sequence checks, cancellation handling and bounded-window reconciliation preserve import integrity | Pass |

### Corrections applied

- Enforced the ICS byte limit during the actual file read.
- Added a parser-level input and event cap so direct callers cannot bypass the
  import service's limits.
- Rejected malformed UTF-8 rather than accepting replacement characters.
- Resolved named-zone recurrence offsets per occurrence and made DST gap/overlap
  behavior explicit.

The review found no need for a new package, database migration, external
service, or UI change. No AI code or provider was changed.

## Regression tests

Ten new tests cover weekly and monthly DST transitions, BYDAY host-timezone
independence, ambiguous and nonexistent local times, direct parser limits,
oversized file reads, invalid UTF-8, and cancellation.

The repository's Windows GitHub Actions workflow remains the authoritative
.NET 10/WPF build and runtime verification. Local compilation was not available
through the GitHub source integration; the CI result is reported separately.
