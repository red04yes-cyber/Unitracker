# Flashcard service security and integrity checks — 2026-11-18

This is a source-level review of the local flashcard service and its existing
model/persistence contract. It is not a penetration test. Checks below are
separated from code changes; passing a check does not imply a vulnerability.

## 30 targeted checks

| # | Area | Check | Result |
|---:|---|---|---|
| 1 | Input | Null StudyData on Add | Guarded |
| 2 | Input | Null card on Add | Guarded |
| 3 | Input | Null StudyData on Review | Guarded |
| 4 | Input | Null StudyData on due query | Guarded |
| 5 | Identity | Empty card ID generation | Guarded |
| 6 | Identity | Duplicate card ID | Guarded |
| 7 | Relations | Course reference exists | Guarded |
| 8 | Relations | Syllabus topic reference exists | Hardened |
| 9 | Relations | Topic belongs to selected course | Hardened |
| 10 | Content | Front prompt is non-empty | Guarded |
| 11 | Content | Back answer is non-empty | Guarded |
| 12 | Content | Source and content are trimmed | Guarded |
| 13 | Schedule | Ease factor lower bound | Hardened |
| 14 | Schedule | Ease factor upper bound | Hardened |
| 15 | Schedule | Negative interval | Hardened |
| 16 | Schedule | Excessive interval | Hardened |
| 17 | Counters | Negative repetition count | Hardened |
| 18 | Counters | Negative lapse count | Hardened |
| 19 | Queue | Suspended cards excluded from due queue | Guarded |
| 20 | Queue | Unscheduled cards not treated as due | Guarded |
| 21 | Queue | Upcoming horizon lower bound | Hardened |
| 22 | Queue | Upcoming horizon upper bound | Hardened |
| 23 | Queue | Unknown course filter | Hardened |
| 24 | Review | Undefined recall enum | Guarded |
| 25 | Review | Suspended card review | Guarded |
| 26 | Arithmetic | Large interval multiplication | Hardened |
| 27 | Arithmetic | Repetition counter overflow | Hardened |
| 28 | Arithmetic | Lapse counter overflow | Hardened |
| 29 | Time | DateTimeOffset due-date overflow | Hardened atomically |
| 30 | History | Review history remains linked and ordered | Guarded |

## Findings and actual corrections

1. **Topic attribution was not validated by FlashcardService.** A card could be
   assigned a missing topic or a topic belonging to another course. Add and
   Update now validate both the topic and its course ownership.
2. **Intervals accepted any non-negative Int32.** Large imported intervals
   could overflow multiplication/conversion during review. Intervals are now
   capped at 3,650 days and arithmetic is clamped before conversion.
3. **Counters could overflow on increment.** Repetition and lapse counters now
   saturate at `int.MaxValue`.
4. **Date arithmetic occurred after some card fields had changed.** The due
   timestamp is now computed before mutations; a date overflow leaves the card
   and history unchanged.
5. **Review time could move backwards.** A review earlier than creation or the
   previous review is now rejected.
6. **The service had no queue or history query contract.** Bounded queue,
   course-scoped queries, suspension, schedule reset, content update and
   chronological history operations were added.

## Existing protections confirmed

The existing model already stores flashcards and review history in the shared
StudyData/SQLite persistence model, enforces unique IDs, validates review
foreign keys, and rejects negative scheduling values during normalization.
Those checks were not duplicated or replaced here.

## Residual scope

The persistence mapper is currently being changed by another open PR (#87),
so this slice deliberately does not edit it. The service validates topic
ownership for its own Add/Update operations; a future persistence-boundary
change may add the same topic/course relationship invariant after #87 is
integrated and reconciled.

No AI/provider code, UI files, database schema, migrations, or dependencies
were changed.
