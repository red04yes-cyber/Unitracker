# Study statistics dashboard

## Scope

The Progress view now supports three rolling date windows:
- 7 days (default)
- 30 days
- 90 days

Changing the selector recalculates the same persisted progress snapshot for the selected inclusive date range. The XLSX export uses that same range.

## Metrics

- Total focus minutes and completed sessions from the existing progress calculation.
- Completed tasks, due flashcards and open error-journal entries for the selected range.
- Focus streak and course-level focus distribution from the persisted progress snapshot.
- Average focus minutes per calendar day (zero-study days are included in the denominator).
- Active study days compared with the selected number of calendar days.
- Average focus minutes per recorded, non-cancelled session with recorded focus time.
- Highest-focus day in the selected range.
- Daily focus bars for every date in the selected range; the 30/90-day view is vertically scrollable.

The weekly-goal card deliberately remains anchored to the current Monday-through-today week, independently of the selected reporting window.

## Data integrity

All figures are derived from the existing local StudyHub records. Daily totals use persisted `StudySessionData.AccumulatedFocusSeconds`, grouped by the session's local start date, and exclude cancelled sessions. No synthetic history, estimated study time, or sample data is introduced. Empty metrics remain neutral rather than being filled with invented values.

## Files

- `src/UniToStudyHub/Presentation/ViewModels/StudyWorkspaceViewModel.cs`
- `src/UniToStudyHub/MainWindow.xaml`

## Verification

The Windows CI build/test and security workflows must complete on the PR head before the change is considered verified. Manual visual validation of the 90-day scrollable chart at different window sizes remains recommended.
