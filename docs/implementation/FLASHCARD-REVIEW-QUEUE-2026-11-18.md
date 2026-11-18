# Flashcard review queue and scheduling hardening — 2026-11-18

## Scope

This slice extends the existing deterministic `FlashcardService` in
`StudyLabServices.cs`. It does not add AI, network calls, dependencies,
database columns, migrations, or visual controls.

## Complex feature: bounded review queue

`GetReviewQueue(data, now, upcomingDays, courseId)` returns a read-only
snapshot containing:

- `Due`: active cards whose due timestamp is at or before `now`;
- `Upcoming`: active cards due after `now`, within the bounded horizon;
- `OverdueCount`: due cards whose due instant precedes the start of the
  supplied offset's local calendar day;
- `SuspendedCount`, `UnscheduledCount`, and `ActiveCount`;
- the exact `AsOf` and `Through` instants used for the snapshot.

The horizon is limited to 0–90 days. Results are ordered by due instant and
then stable card ID. Course filtering rejects unknown course IDs instead of
silently returning an empty queue.

## Simple service operations

- `Update` edits prompt/answer/source/course/topic without replacing the
  existing schedule or review history.
- `SetSuspended` pauses or resumes a card without changing its due timestamp.
- `ResetSchedule` resets interval/ease/counters and makes the card due now,
  while retaining its review history.
- `GetForCourse` and `GetDueForCourse` provide course-scoped lists.
- `GetReviewHistory` returns chronological review records for an existing card.

## Scheduling safety

- Maximum interval: 3,650 days (10 years).
- Ease factor is normalized to 2.5 when imported state is outside 1.3–2.8.
- Interval arithmetic is performed in decimal and clamped before conversion.
- Repetition and lapse counters saturate at `int.MaxValue`.
- A review timestamp cannot precede the previous review.
- The next due timestamp is computed before mutating the card. A date overflow
  therefore leaves the card and review history unchanged.
- Flashcard topic references must exist and belong to the selected course.
- Open-ended answers remain outside this service; no AI evaluator was added.

## Persistence and UI contract

All data continues to live in the existing `StudyData.Flashcards` and
`StudyData.FlashcardReviews` collections and the existing SQLite mapping.
No schema migration is required.

A future ViewModel can expose `FlashcardReviewQueue.Due`,
`Upcoming`, `OverdueCount`, `SuspendedCount`, and `UnscheduledCount`.
The UI should show the `AsOf` time and treat upcoming items as planned
reviews, not as reviews already completed. Visual/XAML files were not changed
because the repository assigns those files to the UI workstream.

## Verification

The new test file covers course/topic integrity, normalization, update
preservation, suspend/resume, schedule reset, queue partitioning, course
filters, review history, all recall ratings, counter/interval bounds, clock
rollback, unknown ratings, and date-overflow atomicity.

The Windows .NET 10 build/test and security workflows are the authoritative
verification. Their outcomes are reported separately in the pull request and
must not be inferred from the presence of test source files.
