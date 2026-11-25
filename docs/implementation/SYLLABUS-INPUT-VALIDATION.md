# Syllabus input validation and coverage bounds

## Scope

`SyllabusTrackerService` is the service boundary for manually entered or imported syllabus topics. This slice hardens it without changing the stored model or the WPF layout.

## Rules

- Topic titles are limited to 300 characters; descriptions to 4,000. Control characters are removed, except line breaks and tabs in descriptions. Whitespace is trimmed.
- Source links are optional, limited to 2,048 characters, and must be absolute HTTP(S) URIs without user information, fragments, control characters, or common credential/signature query keys (`token`, `wstoken`, `password`, `api_key`, `access_token`, `signature`, etc.). Other safe query parameters such as course IDs are retained.
- Per-topic study estimates are limited to 0–525,600 minutes (one year). Negative values and implausibly large inputs are rejected.
- Legacy or imported negative estimates are ignored by the remaining-time summary. Aggregation uses a wide accumulator and saturates the existing `int` DTO property at `Int32.MaxValue` instead of overflowing to a negative value.
- `GetTopics` validates that the course exists, matching the contract of `Add` and `GetCoverage`.
- A completion timestamp with `DateTimeKind.Unspecified` is treated as UTC; local values are converted to UTC. No historical timestamps are rewritten unless the topic is marked completed.

## Compatibility

No schema change, migration, dependency, UI, or network request is introduced. Existing safe source URLs remain; invalid legacy links are not changed by this slice because it validates new topic input only. The 525,600-minute ceiling is applied to new entries; existing persisted estimates remain readable, with coverage safely bounded.

## Verification

The new unit tests cover URL schemes and credential-like query parameters, length/control bounds, text normalization, estimate limits, overflow handling, unknown-course validation, and timestamp normalization. Windows build/test/security workflows are the verification source; local .NET SDK execution may not be available.
