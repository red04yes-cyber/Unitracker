# Student productivity features — technical slice

This slice adds a persisted productivity aggregate without introducing a second database. The data is stored as a JSON payload in the existing ProfileSettings row and is covered by the additive migration AddProductivityState. Existing course, grade, planned-exam and career-history rows are left untouched by the migration.

## Implemented application contracts

- Study goals and streaks: configurable daily/weekly minute goals, additive focus logging, weekly totals, current streak and longest streak. A day counts only when recorded focus meets the daily threshold. A single calendar day is capped at 1,440 focus minutes, including across multiple logging calls; streak calculations aggregate rows by calendar date. The current streak may include yesterday while the current day is still in progress.
- Reminders: dated reminders with optional course links, due-window lookup and a 12-hour notification suppression marker. The service supplies notification candidates; an OS notification host still needs to display them.
- Next activity: chooses the nearest future planned exam or enabled reminder. The desktop companion preference is persisted, but a separate Windows desktop widget host is not part of this slice.
- Exam mode: persists the selected exam, start instant and end instant. Remaining time is derived from timestamps, so it survives restarts without relying on a running timer loop.
- Quick notes: notes are linked to an existing course and a required topic; an existing note can be updated by ID.
- Attendance: lecture records link to existing courses and retain title, date/time, duration, attended state and optional notes. Attendance CSV generation is available.
- Dashboard preferences: widget visibility and order are persisted; deterministic defaults are supplied before the user customizes the dashboard.
- Course archive: the existing Course.Completed field remains the archive flag; no duplicate archive state is added.
- WPF Study Lab: the Studio Lab tab now exposes daily/weekly goal editing, additive focus logging, current streak and weekly minutes, course-linked quick notes, and a lecture attendance form with recent-note and attendance tables. These actions save through the existing data store.

## Not included / integration still required

- Native Windows toast scheduling and a true always-on desktop widget host.
- WPF controls for reminders, exam-mode countdown, configurable dashboard widgets and next-activity display are still pending. The initial Study Lab UI covers focus, notes and attendance.
- PDF and XLSX export, and a unified export workflow. Only attendance CSV is implemented here.
- An OS background scheduler. Reminder lookup is deterministic and should be called by a bounded host scheduler.

## Persistence and verification

StudyDataEntityMapper validates course references, IDs, date/minute ranges, goals and dashboard preference uniqueness before saving. Productivity JSON is stored in the existing ProfileSettings row. The migration adds a non-null column with a default empty JSON object so existing profiles remain readable.

Focused tests cover weekly goal/streak calculations, reminder suppression, topic-linked notes, attendance CSV, exam-mode countdown, dashboard preference ordering, the daily focus cap and duplicate-date streak aggregation. A GitHub Actions workflow now runs the cross-platform tests and a Windows WPF build for this branch/PR. Run the repository's cross-platform test project locally with:

```powershell
dotnet test .\tests\UniToStudyHub.Tests\UniToStudyHub.Tests.csproj
```
