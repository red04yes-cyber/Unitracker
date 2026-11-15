# Constraint-aware exam session planner — 2026-11-15

## Scope

This slice strengthens the existing `ExamPlanner` used by the WPF
`PlanExamsAsync` command. It does not introduce a second planner, a database
table, a migration, a dependency, a network endpoint or a new source of
university dates.

The fixed session anchors (20 January, 20 June and 15 September) remain
indicative placeholders. They are not official UniTo appeal dates.

## Implemented behavior

- A preview is deterministic for the same exam snapshot, options and reference
  date. Open exams are ordered by prerequisite topology, then descending CFU,
  estimated study hours and ordinal exam ID.
- Every session has a hard CFU ceiling. An exam that cannot fit is reported as
  unscheduled instead of being placed into an overloaded session.
- The estimated workload assigned to a session is checked against the user's
  maximum average weekly hours available up to that session.
- User-supplied blackout periods exclude session anchors inside the inclusive
  date range.
- The `StudyData` overload reads mandatory prerequisites and passed courses
  from the existing shared snapshot. A dependent exam must be assigned to a
  strictly later session than each uncompleted prerequisite.
- Missing prerequisites, prerequisite cycles, past targets and impossible
  capacity are reported explicitly. A past graduation target is never silently
  extended.
- Completed exams are left unchanged. Applying a preview clears stale dates on
  unfinished exams that have no feasible assignment.
- The legacy `AssignIndicativeSessions(IList<Exam>, ...)` entry point remains
  source-compatible and now returns the planning result. Its current WPF caller
  uses this list-only overload; prerequisite-aware planning is available through
  `AssignIndicativeSessions(StudyData, ...)` for the next binding integration.

## Five additional capabilities

1. Conservative, balanced and intensive alternatives (18/10, 24/20 and
   30/30 CFU-per-session / average-hours-per-week limits).
2. Per-session load summaries with CFU, estimated hours, weeks available and
   average weekly workload.
3. Explicit unscheduled-exam reasons rather than an apparently successful but
   overloaded plan.
4. Inclusive blackout periods for unavailable exam sessions.
5. Mandatory prerequisite ordering using the existing
   `PrerequisiteGraphService` and passed-course state.

## Bounds and validation

- 500 exams per preview.
- 2,000 course rows and 2,000 prerequisite rows.
- 64 blackout periods.
- Planning horizon no longer than 10 years.
- 1–60 CFU per session and per exam; 1–168 average study hours per week.
- 0–2,000 estimated study hours per exam.
- Exam IDs and open course assignments must be unique.
- Invalid or missing exam identifiers, course IDs, CFU and hour estimates are
  reported as unscheduled rather than being silently normalized.
- Applying a preview validates that every assignment still refers to an
  existing, unfinished exam and matches its course, CFU and estimate.

## Security and integrity review — 30 targeted controls

### Input and resource boundaries

1. Null `StudyData` is rejected.
2. Null exam, course or prerequisite collections are rejected; course lists above 2,000 rows are rejected.
3. Null rows inside those collections are rejected before dereference.
4. An arbitrary exam enumerable is stopped at item 501, before unbounded materialization.
5. Prerequisite collections above 2,000 rows are rejected before graph construction.
6. Blackout collections above 64 rows are rejected.
7. Planning horizons beyond 10 years are rejected.
8. A past target is reported; the planner does not silently extend it.
9. CFU limits outside 1–60 are rejected.
10. Weekly workload limits outside 1–168 hours are rejected.
11. Reversed blackout date ranges are rejected.
12. Blackout labels longer than 100 characters are rejected.

### Domain integrity

13. Blank exam IDs are not used as dictionary keys or assignments.
14. Duplicate non-empty exam IDs are rejected.
15. Duplicate open course assignments are rejected to prevent double-counting.
16. Exams with non-positive or excessive CFU are not scheduled.
17. Negative or excessive study-hour estimates are not scheduled.
18. Completed exams are excluded from new assignments.
19. Only mandatory prerequisite edges constrain scheduling.
20. Duplicate prerequisite edges are rejected by the existing graph service.
21. Self-referential prerequisite edges are rejected by the graph service.
22. Prerequisite cycles are rejected by the graph service.
23. Missing, uncompleted prerequisites produce an explicit unscheduled reason.
24. A prerequisite must be assigned to a strictly earlier session.
25. A session cannot exceed the configured CFU ceiling.
26. A session cannot exceed the configured average weekly workload.

### Preview and application integrity

27. Preview creation does not mutate exam dates or session labels.
28. Assignment application rejects unknown or completed exam references.
29. Assignment application rejects duplicate or mismatched course/CFU/hour data.
30. Assignment dates must remain within the preview's reference and target range.

All 30 controls passed by code inspection and focused regression tests. They are
planner-boundary and integrity checks, not a claim that unrelated application
subsystems were re-audited. No new security vulnerability was found in this
slice and no security-specific code correction was necessary.

## Tests

`ExamPlannerTests` covers hard CFU capacity, weekly workload, passed and missing
prerequisites, prerequisite order and cycles, blackout periods, past targets,
side-effect-free previews, completed-exam preservation, alternatives, invalid
budgets, oversized input, duplicate course assignments, invalid exam values
and tampered/foreign assignments.

The Windows build/test and security-scan workflows remain the final verification
gate. A local .NET SDK build is not claimed unless it is actually run.

## UI binding contract and remaining integration

The existing WPF command still calls the compatible list-only overload, so the
current screen immediately benefits from hard CFU/weekly limits and explicit
"Da pianificare" fallbacks. To expose the complete prerequisite-aware preview,
the UI workstream should call `ExamPlanner.CreatePlan(StudyData, options)` and
bind `Assignments`, `SessionLoads`, `UnscheduledExams`, `Warnings` and
`IsFeasible`. The visual layout and `MainViewModel` were intentionally not
changed because open PR #89 already owns `MainViewModel`.

No XAML, code-behind, AI provider, AI Study Director, credential, database,
migration, package or external-service code was modified.
