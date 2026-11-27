# Flashcard review forecast and lifecycle contract

## Scope

The flashcard service exposes a read-only forecast of persisted due dates for up to 90 days, with optional course filtering. It does not simulate future review ratings or move dates.

- Cards due before the requested range are counted as overdue on the first forecast day.
- Cards due within the range are counted on their converted calendar date.
- Suspended cards and cards without a due date are excluded.
- Due timestamps are converted through the supplied `TimeZoneInfo`; the default is the current local time zone.
- `TotalReviewCount` is the scheduled count plus the overdue bucket for that date.
- The forecast returns a zero row for every requested date so the consumer can draw continuous calendar/graph views.

## Service API

- `FlashcardService.GetReviewForecast(data, startDate, dayCount, timeZone, courseId)`: count existing due dates without mutating records.
- `GetDue(data, now, courseId)` and `GetDueCount`: optional course-specific due queue.
- `Update`: edit prompt/answer/source/course/topic without resetting the scheduler state.
- `SetSuspended`: hide cards from the due queue and block reviews; resuming preserves the schedule.
- `Remove`: deletes only unreviewed cards. Reviewed cards are retained and should be suspended instead.

## Scheduler hardening

- Review interval is bounded to 36,500 days (100 years); imported/legacy counters are normalized and increments saturate at 1,000,000.
- Review validates enum and rejects timestamps earlier than the previous review.
- The next due timestamp is calculated before any card/history mutation, preventing a date overflow from leaving a partial review.
- Flashcard front/back text is limited to 10,000 characters; source is limited to 512 characters.

## Tests

`FlashcardReviewForecastTests` covers forecast bucketing, overdue handling, timezone conversion, course filtering, no-mutation semantics, range validation, due queue filtering, editing, suspend/resume, safe removal, bounded scheduler state, overflow and wall-clock rollback.

## UI binding contract

The UI can bind forecast rows to `Date`, `DueCount`, `OverdueCount`, and `TotalReviewCount`. No XAML or visual changes are included in this technical slice; UI implementation remains with the UI owner.
