# Student productivity features — technical slice

This slice adds a persisted productivity aggregate without introducing a second database. The data is stored as a JSON payload in the existing ProfileSettings row and is covered by the additive migration AddProductivityState. Existing course, grade, planned-exam and career-history rows are left untouched by the migration.

## Implemented application contracts

- Study goals and streaks: configurable daily/weekly minute goals, additive focus logging, weekly totals, current streak and longest streak. A day counts only when recorded focus meets the daily threshold. A single calendar day is capped at 1,440 focus minutes, including across multiple logging calls; streak calculations aggregate rows by calendar date. The current streak may include yesterday while the current day is still in progress.
- Reminders: dated reminders with optional course links, due-window lookup and a 12-hour notification suppression marker. The WPF host polls once per minute while running and displays due reminders through the Windows notification area; notifications are not scheduled while the app is closed.
- Next activity: chooses the nearest future planned exam or enabled reminder. A separate always-on-top, taskbar-hidden WPF desktop widget can be opened from the home dashboard and refreshes its displayed activity every 30 seconds.
- Exam mode: persists the selected exam, start instant and end instant. Remaining time is derived from timestamps, so it survives restarts without relying on a running timer loop.
- Quick notes: notes are linked to an existing course and a required topic; an existing note can be updated by ID.
- Attendance: lecture records link to existing courses and retain title, date/time, duration, attended state and optional notes. Attendance CSV generation is available.
- Dashboard preferences: widget visibility and order are persisted; deterministic defaults are supplied before the user customizes the dashboard. The WPF Study Lab exposes visibility and order controls, and the home dashboard renders the selected widgets with live values.
- Course archive: the existing Course.Completed field remains the archive flag; no duplicate archive state is added.
- WPF Study Lab: the Studio Lab tab exposes daily/weekly goal editing, additive focus logging, current streak and weekly minutes, course-linked quick notes, lecture attendance, reminder creation/listing and a next-activity summary. It also exposes a timestamp-derived exam countdown with a live one-second display refresh, dashboard widget visibility/order controls and a Save File workflow for attendance CSV. The home dashboard renders configurable widgets with live values, and a dedicated always-on-top desktop companion displays the next activity. Productivity actions persist through the existing data store.

## Not included / integration still required

- Reminder notifications are delivered only while the app is running; there is no Windows background task or notification scheduling after the app exits.
- PDF and XLSX export, and a unified export workflow. Attendance CSV can now be saved from the WPF screen.
- An OS background scheduler. Reminder lookup is deterministic and should be called by a bounded host scheduler.

## Persistence and verification

StudyDataEntityMapper validates course references, IDs, date/minute ranges, goals and dashboard preference uniqueness before saving. Productivity JSON is stored in the existing ProfileSettings row. The migration adds a non-null column with a default empty JSON object so existing profiles remain readable.

Focused tests cover weekly goal/streak calculations, reminder suppression, topic-linked notes, attendance CSV, exam-mode countdown, dashboard preference ordering, the daily focus cap and duplicate-date streak aggregation. A GitHub Actions workflow now runs the cross-platform tests and a Windows WPF build for this branch/PR. Run the repository's cross-platform test project locally with:

```powershell
dotnet test .\tests\UniToStudyHub.Tests\UniToStudyHub.Tests.csproj
```
