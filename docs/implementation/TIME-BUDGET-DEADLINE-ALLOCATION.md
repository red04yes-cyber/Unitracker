# Time budget — deadline-aware allocation

## Scope

This change corrects the existing `TimeBudgetService` estimate. It does not add a
second planner, change the calendar, create events, persist generated allocations,
or modify the WPF views.

## Inputs and capacity

The service uses the profile's configured `WeeklyStudyHours` and
`MaxDailyStudyMinutes`. Values outside the supported profile ranges
(0–168 hours/week and 0–1440 minutes/day) are clamped for calculation and
reported in `Warnings`.

For an exam with a planned date, the service uses the exam's positive
`EstimatedStudyHours`; when absent, it falls back to the course estimate.
Negative fallback estimates are treated as missing and reported.

The date distance is measured in calendar days from the calculation date,
with a minimum of one day for an exam scheduled today. Capacity available by a
deadline is the lower of:

- weekly capacity prorated to the remaining days:
  `floor(WeeklyMinutes * DaysAvailable / 7)`;
- daily capacity:
  `MaxDailyStudyMinutes * DaysAvailable`.

The plan's `AvailableMinutes` is the capacity through the latest included exam
date. It is not an assertion that those minutes are free in the calendar; fixed
commitments and actual availability are handled by the separate Study Planner.

## Allocation rules

1. Exams are processed by ascending planned date.
2. Earlier deadlines consume capacity before later deadlines.
3. Exams on the same date share a shortage proportionally to their estimates.
   The largest-remainder method assigns leftover whole minutes deterministically
   by exam ID.
4. `IsOverCapacity` is true when total demand exceeds the horizon capacity or
   when any individual deadline cannot be covered, even if unused capacity
   remains after that deadline.
5. `StudyAllocation.HasEstimate` distinguishes an unknown estimate from a
   zero-minute requirement. Missing estimates receive an explicit explanation
   and are not presented as covered.
6. The service only returns a proposal. It never changes tasks, exams, or
   calendar events.

## Verification

`SyllabusQuestionAndTimeBudgetTests` covers:
- backward-compatible full-week capacity;
- total shortage and missing future exams;
- earliest-deadline-first allocation;
- partial-week proration;
- the daily limit when it is more restrictive;
- proportional distribution for exams sharing a date;
- an infeasible early deadline despite sufficient total horizon capacity;
- clamping invalid profile values without integer overflow;
- missing estimates remaining visibly unknown.

No UI, persistence schema, database migration, external dependency, or AI
integration is changed. A UI binding can use `StudyAllocation.HasEstimate`,
`IsAtRisk`, and `Explanation` to distinguish covered, at-risk, and unknown
estimates.
