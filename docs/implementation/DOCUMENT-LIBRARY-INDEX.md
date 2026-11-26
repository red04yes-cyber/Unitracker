# Moodle document library index

## Scope

The document library is a read-only reconciliation between the current Moodle
course file descriptors and the local course download folder. It does not
create a second database or modify downloaded content. Moodle remains the
source of remote metadata; the filesystem remains the source of local files.

The service is available through dependency injection as
`DocumentLibraryService` and through
`MoodleApplicationService.GetCourseDocumentLibraryAsync`.

## Data returned

Each `DocumentLibraryEntry` contains:

- local course ID/name, Moodle section/module, category, file name and extension;
- remote and local sizes and modification timestamps (UTC);
- SHA-256 for files that were safely read within the scan budget (a local fingerprint, not a checksum supplied or authenticated by Moodle);
- expected path and actual local path, when available;
- status, moved-file flag and ambiguity flag.

Raw Moodle file URLs are deliberately not returned. They may contain signed
parameters or credentials. A future "show source" action must resolve a
credential-free Moodle module URL separately and must not reuse a download URL.

## Status semantics

| Status | Meaning |
|---|---|
| `Available` | Expected local path exists and its known remote size/time do not indicate an update |
| `PathConflict` | Distinct Moodle resources resolve to the same local destination; none is treated as available or downloaded |
| `Missing` | Expected path and a unique moved-file candidate were not found |
| `Moved` | Expected path is missing, but exactly one same-name file was found inside the course folder |
| `UpdateRequired` | Local size differs from the known remote size or local timestamp is older than Moodle metadata |
| `Unsupported` | Executable/macro extension or a file above the existing 250 MiB download limit |
| `UnsafePath` | A reparse point/link or a path outside the course boundary was detected |
| `Unavailable` | Metadata or local file could not be read/verified |

A moved file is never selected when the bounded local scan is incomplete or
when multiple same-name candidates exist. In those cases the entry remains
missing and carries an ambiguity flag when appropriate.

## Bounds and privacy

- At most 500 sections, 5,000 modules, 5,000 source descriptors and 1,000 unique Moodle files per course.
- Source URL length at most 8 KiB, file name at most 1 KiB, relative path at
  most 2 KiB and 32 segments.
- Local moved-file discovery examines at most 5,000 filesystem entries.
- Each file is limited by the existing 250 MiB Moodle download ceiling.
- SHA-256 reads at most 1 GiB per course scan. When the hash budget is reached,
  metadata remains available and the checksum is omitted with a warning.
- Exact URL identity is ordinal; Moodle path casing is not folded. Distinct URLs that resolve to the same destination are surfaced as one `PathConflict` row, and the downloader skips all conflicting sources instead of overwriting one with another.
- Local reparse points are excluded and paths are revalidated before and after
  hashing. File content is never opened for execution or modified.
- Search and metadata filters operate on the returned snapshot and do not
  rescan or rehash the filesystem.

## Calling contract

`GetCourseDocumentLibraryAsync` accepts an existing section snapshot to avoid
another Moodle request and optional activity attachment descriptors from
`GetCourseActivitySnapshotAsync`. If sections are omitted, it fetches the
course contents using the stored Moodle token. The course must have a positive
`MoodleCourseId`.

`DocumentLibraryService.Filter` combines free-text, course, category,
extension, status and inclusive UTC modification-date filters.

## UI binding contract

The UI owner can bind a collection of `DocumentLibraryEntry` and the warnings
from `DocumentLibraryIndexResult`. Keep the following distinctions visible:

- `Missing` is not a download failure; it means the expected local copy is absent.
- `Moved` exposes `LocalPath` and `ExpectedPath`; do not silently move files.
- `UpdateRequired` is an indication from remote size/time metadata, not a
  verified content difference.
- A null `Sha256` means the checksum was not available (missing, unreadable,
  unsupported or scan hash budget exhausted); it must not be displayed as a
  successful integrity check.
- `LocationScanComplete=false` means missing/moved conclusions may be
  provisional.
- Opening a file/folder requires a separate user action and must revalidate the
  selected path with `LocalFileSecurity.IsInside` immediately before use.

## Limitations

This slice provides the service and binding contract only. It does not add a
WPF document-library screen, persist a second index, automatically move/repair
files, or perform a download retry. Those actions require explicit UI work and
user confirmation. Issue #21 remains open until the UI actions and full
acceptance criteria are completed.
