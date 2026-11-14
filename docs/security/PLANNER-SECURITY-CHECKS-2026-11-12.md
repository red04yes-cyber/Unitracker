# Planner security and integrity checks — 2026-11-12

Scope: `StudyPlannerService`, `StudyPlannerModels`,
`ActivityLedgerService`, and their planner tests. These checks cover
untrusted/malformed in-memory preview data, persisted legacy values, resource
bounds, and integrity of recorded study progress. They do not claim a review
of unrelated Moodle, credential-vault, PDF, or AI-provider code.

## Thirty targeted checks performed

### Input and resource bounds

1. Planning end date must not precede the start date.
2. Planning windows are limited to 181 calendar days.
3. The plan end leaves seven days of `DateOnly` headroom for weekly range and next-local-day calculations.
4. Time-zone identifiers are resolved before wall-clock conversion.
5. Weekly availability is capped at 64 windows.
6. Null availability-window entries are rejected.
7. Availability day values must be defined `DayOfWeek` values.
8. Availability windows must end strictly after they start; cross-midnight
   windows must be split into two dates.
9. Preferred block duration is restricted to 15–180 minutes.
10. Minimum block duration, break duration, daily/weekly budgets, and
    blocks-per-day have explicit supported ranges.

### Persisted values and task eligibility

11. Persisted weekly hours are clamped to 0–168 hours before conversion.
12. Persisted daily minutes are clamped to 0–1,440 minutes.
13. Invalid persisted budget values produce explicit preview warnings.
14. Only Todo/InProgress tasks enter the planner.
15. Null task rows are ignored rather than dereferenced.
16. Tasks without identifiers are not linked to generated calendar events.
17. Blank task titles are not scheduled.
18. Zero/negative estimates remain unscheduled and require user input.
19. Task priority is clamped to the supported 1–5 range.
20. Past deadlines are reported; future deadlines constrain the proposed block.

### Ledger, preview and apply integrity

21. Batch task IDs are de-duplicated with ordinal comparison.
22. Unknown requested task IDs are rejected by the ledger query.
23. Null activity rows are ignored during interval construction.
24. Soft-deleted activities do not contribute to task progress.
25. Cancelled manual activities do not contribute to task progress.
26. Actual focus segments from a cancelled timer remain credited.
27. Overlapping manual/timer intervals are unioned and not double-counted.
28. Remaining partial minutes are rounded up, not silently truncated.
29. Preview progress snapshots reject null/duplicate IDs, negative values and
    out-of-range percentages; incomplete snapshot sets are not accepted.
30. Apply rechecks progress against current ledger state, limits previews to
    3,620 blocks, and rejects aggregate blocks exceeding the remaining estimate.

## Corrections applied in this slice

- Replaced original-estimate scheduling with task-progress-aware remaining
  minutes.
- Added a batch ledger query that scans recorded intervals once and indexes task
  lookups, avoiding one full ledger scan per task.
- Exposed per-task snapshots and aggregate recorded/remaining minutes on the
  preview contract without adding persisted counters or database migrations.
- Rejected stale previews when new study time or an estimate change is recorded
  after preview generation.
- Rejected incomplete/duplicate snapshot sets and proposals exceeding the
  current remaining estimate.
- Bounded applied preview size and weekly availability count.
- Rejected null availability windows and filtered null task/activity/calendar/
  course entries at the planner boundaries touched by this slice.
- Clamped out-of-range legacy weekly/daily budgets and explained the clamp in
  the preview warnings.

## Findings not changed

No new network endpoint, credential handling, external service, dependency,
database schema, or AI-provider code was introduced or modified. The checks in
this document do not substitute for the repository's Gitleaks and NuGet audit
workflows; their outcomes are reported separately from code-level findings.
