# Privacy operations and diagnostic bundles

## Selective reset

`PrivacyService.ResetAsync` is a confirmed, local-only operation. Callers must pass `confirmed: true` only after presenting a confirmation to the user. It supports:

- `StudyLab`: removes focus sessions, tasks, activities, missions, Pomodoro timer/cycles, error journal, flashcards/reviews, distractions and energy check-ins. Career courses and grades remain.
- `Calendar`: removes local calendar events only.
- `Documents`: removes PDF metadata and files inside the managed Moodle documents directory. It does not search or delete arbitrary files elsewhere.
- `MoodleConnection`: clears the Moodle base URL and course external IDs, then removes the local DPAPI token. Grades and academic history remain.

An optional safety backup is created before any reset. Database changes are committed before credentials or managed files are deleted, avoiding irreversible cleanup when the SQLite write fails.

Complete deletion remains available through `DeleteAllAsync`; it removes local records and, when requested, backups, legacy files, logs, managed Moodle downloads and stored credentials. It also revokes the persisted AI consent before deleting credentials, so a restart cannot silently re-enable a provider.

## Diagnostic bundle

The diagnostic flow is two-step:

1. `PreviewDiagnosticBundle()` returns app version, OS/runtime, number and size of eligible local log files. It does not read or expose personal database content.
2. `CreateDiagnosticBundleAsync(userConsent: true, includeLogs: ...)` creates an in-memory ZIP only after explicit consent.

The archive contains `diagnostics.json` and, only when selected, `redacted-application.log`. Log lines are redacted again at export time. User name, email, credentials, database, PDFs, Moodle downloads and arbitrary environment variables are excluded. At most three rotated logs are considered; each is capped at 512 KiB and the combined log text at 2 MiB.

The bundle is returned as bytes; the caller must show the preview and let the user choose where to save or share it. There is no automatic upload or telemetry.
