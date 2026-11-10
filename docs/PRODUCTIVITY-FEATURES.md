# Student productivity features — technical slice

This slice adds a persisted productivity aggregate without introducing a second database. The data is stored as a JSON payload in the existing ProfileSettings row and is covered by the additive migration AddProductivityState. Existing course, grade, planned-exam and career-history rows are left untouched by the migration.

## Implemented application contracts

- Study goals and streaks: configurable daily/weekly minute goals, additive focus logging, weekly totals, current streak and longest streak. A day counts only when recorded focus meets the daily threshold. A single calendar day is capped at 1,440 focus minutes, including across multiple logging calls; streak calculations aggregate rows by calendar date. The current streak may include yesterday while the current day is still in progress.
- Reminders: dated reminders with optional course links, due-window lookup and a 12-hour notification suppression marker. The service supplies notification candidates; an OS notification host still needs to display them.
- Next activity: chooses the nearest future planned exam or enabled reminder. The desktop companion preference is persisted, but a separate Windows desktop widget host is not part of this slice.
- Exam mode: persists the selected exam, start instant and end instant. Remaining time is derived from timestamps, so it survives restarts without relying on a running timer loop.
- Quick notes: notes are linked to an existing course and a required topic; an existing note can be updated by ID.
- Attendance: lecture records link to existing courses and retain title, date/time, duration, attended state and optional notes. Attendance CSV generation is available.
- Dashboard preferences: widget visibility and order are persisted; deterministic defaults are supplied before the user customizes the dashboard. The WPF Study Lab now exposes visibility toggles for the five built-in widgets.
- Course archive: the existing Course.Completed field remains the archive flag; no duplicate archive state is added.
- WPF Study Lab: the Studio Lab tab now exposes daily/weekly goal editing, additive focus logging, current streak and weekly minutes, course-linked quick notes, lecture attendance, reminder creation/listing and a next-activity summary. It also exposes a timestamp-derived exam countdown with a live one-second display refresh, dashboard widget visibility controls and a Save File workflow for attendance CSV. These actions save through the existing data store.

## Not included / integration still required

- Native Windows toast scheduling and a true always-on desktop widget host.
- PDF and XLSX export, and a unified export workflow. Attendance CSV can now be saved from the WPF screen.
- An OS background scheduler. Reminder lookup is deterministic and should be called by a bounded host scheduler.

## Persistence and verification

StudyDataEntityMapper validates course references, IDs, date/minute ranges, goals and dashboard preference uniqueness before saving. Productivity JSON is stored in the existing ProfileSettings row. The migration adds a non-null column with a default empty JSON object so existing profiles remain readable.

Focused tests cover weekly goal/streak calculations, reminder suppression, topic-linked notes, attendance CSV, exam-mode countdown, dashboard preference ordering, the daily focus cap and duplicate-date streak aggregation. A GitHub Actions workflow now runs the cross-platform tests and a Windows WPF build for this branch/PR. Run the repository's cross-platform test project locally with:

```powershell
dotnet test .\tests\UniToStudyHub.Tests\UniToStudyHub.Tests.csproj
```
