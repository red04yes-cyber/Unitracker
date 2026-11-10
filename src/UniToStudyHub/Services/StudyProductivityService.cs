using System.Globalization;
using System.Text;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record StudyStreakSnapshot(int CurrentStreakDays, int LongestStreakDays, int MinutesThisWeek, int WeeklyGoalMinutes, int TodayMinutes);
public sealed record StudyNextActivity(string Title, DateTimeOffset StartsAt, string Kind, string? CourseId);
public sealed record ExamModeSnapshot(string Title, TimeSpan Remaining, bool IsExpired);

/// <summary>
/// Business rules for reminders, study goals, quick notes, attendance, exam mode and dashboard preferences.
/// Mutations are made against the shared StudyData aggregate; callers persist it through IStudyDataStore.
/// </summary>
public sealed class StudyProductivityService
{
    private static readonly string[] DefaultWidgetKeys = ["next-activity", "weekly-goal", "streak", "upcoming-exams", "recent-notes"];

    public void SetGoals(StudyData data, int dailyMinutes, int weeklyMinutes)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (dailyMinutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(dailyMinutes));
        if (weeklyMinutes < dailyMinutes || weeklyMinutes > 10080) throw new ArgumentOutOfRangeException(nameof(weeklyMinutes));
        data.Productivity.DailyStudyGoalMinutes = dailyMinutes;
        data.Productivity.WeeklyStudyGoalMinutes = weeklyMinutes;
    }

    public void RecordFocus(StudyData data, DateOnly date, int minutes)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (minutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(minutes));
        var row = data.Productivity.StudyDays.FirstOrDefault(x => x.Date == date);
        if (row is null)
        {
            row = new StudyDayLogData { Date = date };
            data.Productivity.StudyDays.Add(row);
        }
        var total = checked(row.FocusMinutes + minutes);
        if (total > 1440)
            throw new ArgumentOutOfRangeException(nameof(minutes), "A study day cannot contain more than 24 hours of focus time.");
        row.FocusMinutes = total;
    }

    public StudyStreakSnapshot GetStreak(StudyData data, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(data);
        var daily = data.Productivity.StudyDays.GroupBy(x => x.Date)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.FocusMinutes));
        var dailyGoal = data.Productivity.DailyStudyGoalMinutes;
        var current = 0;
        var anchor = daily.TryGetValue(today, out var todayValue) && todayValue >= dailyGoal
            ? today
            : today.AddDays(-1);
        for (var day = anchor; daily.TryGetValue(day, out var value) && value >= dailyGoal; day = day.AddDays(-1))
            current++;
        var longest = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in daily.OrderBy(x => x.Key))
        {
            if (day.Value < dailyGoal) { run = 0; previous = day.Key; continue; }
            run = previous is not null && day.Key == previous.Value.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day.Key;
        }
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekMinutes = daily.Where(x => x.Key >= weekStart && x.Key <= today).Sum(x => x.Value);
        return new StudyStreakSnapshot(current, longest, weekMinutes,
            data.Productivity.WeeklyStudyGoalMinutes, daily.GetValueOrDefault(today));
    }

    public StudyReminderData AddReminder(StudyData data, string title, DateTimeOffset dueAt, string kind = "Deadline", string? courseId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Reminder title is required.", nameof(title));
        if (courseId is not null && data.Courses.All(x => x.Id != courseId))
            throw new ArgumentException("The reminder course does not exist.", nameof(courseId));
        var reminder = new StudyReminderData { Title = title.Trim(), DueAt = dueAt, Kind = kind.Trim(), CourseId = courseId };
        data.Productivity.Reminders.Add(reminder);
        return reminder;
    }

    public bool SynchronizeExamReminders(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var changed = false;
        foreach (var exam in data.Exams)
        {
            var reminder = data.Productivity.Reminders.FirstOrDefault(x => x.SourceExamId == exam.Id);
            if (exam.Completed || !exam.PlannedDate.HasValue)
            {
                if (reminder is { Enabled: true })
                {
                    reminder.Enabled = false;
                    changed = true;
                }
                continue;
            }

            var dueAt = new DateTimeOffset(exam.PlannedDate.GetValueOrDefault().Date.AddHours(9));
            var title = $"Esame: {exam.Name}";
            if (reminder is null)
            {
                data.Productivity.Reminders.Add(new StudyReminderData
                {
                    Title = title,
                    CourseId = exam.CourseId,
                    SourceExamId = exam.Id,
                    DueAt = dueAt,
                    Kind = "Esame"
                });
                changed = true;
                continue;
            }

            if (reminder.Title != title || reminder.CourseId != exam.CourseId ||
                reminder.DueAt != dueAt || !reminder.Enabled)
            {
                reminder.Title = title;
                reminder.CourseId = exam.CourseId;
                reminder.DueAt = dueAt;
                reminder.Enabled = true;
                reminder.LastNotifiedAt = null;
                changed = true;
            }
        }
        return changed;
    }

    public IReadOnlyList<StudyReminderData> GetDueReminders(StudyData data, DateTimeOffset now, TimeSpan lookAhead)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (lookAhead < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lookAhead));
        return data.Productivity.Reminders.Where(x => x.Enabled && x.DueAt <= now + lookAhead && x.DueAt >= now - TimeSpan.FromDays(1) &&
            (x.LastNotifiedAt is null || x.LastNotifiedAt < now.AddHours(-12)))
            .OrderBy(x => x.DueAt).ToArray();
    }

    public void MarkReminderNotified(StudyData data, string reminderId, DateTimeOffset notifiedAt)
    {
        var reminder = data.Productivity.Reminders.FirstOrDefault(x => x.Id == reminderId)
            ?? throw new KeyNotFoundException("Reminder not found.");
        reminder.LastNotifiedAt = notifiedAt;
    }

    public QuickStudyNoteData SaveQuickNote(StudyData data, string? noteId, string courseId, string topic, string content, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Courses.All(x => x.Id != courseId)) throw new ArgumentException("The note course does not exist.", nameof(courseId));
        if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("A topic is required.", nameof(topic));
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Note content is required.", nameof(content));
        var note = noteId is null ? null : data.Productivity.QuickNotes.FirstOrDefault(x => x.Id == noteId);
        if (note is null)
        {
            note = new QuickStudyNoteData { CourseId = courseId };
            data.Productivity.QuickNotes.Add(note);
        }
        note.CourseId = courseId;
        note.Topic = topic.Trim();
        note.Content = content.Trim();
        note.UpdatedAt = now ?? DateTimeOffset.Now;
        return note;
    }

    public LectureAttendanceData RecordAttendance(StudyData data, string courseId, string title, DateTimeOffset startsAt, int durationMinutes, bool attended, string? notes = null, string? recordId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Courses.All(x => x.Id != courseId)) throw new ArgumentException("The attendance course does not exist.", nameof(courseId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Lecture title is required.", nameof(title));
        if (durationMinutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        var row = recordId is null ? null : data.Productivity.Attendance.FirstOrDefault(x => x.Id == recordId);
        if (row is null)
        {
            row = new LectureAttendanceData();
            data.Productivity.Attendance.Add(row);
        }
        row.CourseId = courseId;
        row.Title = title.Trim();
        row.StartsAt = startsAt;
        row.DurationMinutes = durationMinutes;
        row.Attended = attended;
        row.Notes = notes?.Trim();
        return row;
    }

    public ExamModeSnapshot StartExamMode(StudyData data, string examId, string title, DateTimeOffset endsAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(examId) || string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Exam and title are required.");
        if (endsAt <= now) throw new ArgumentOutOfRangeException(nameof(endsAt), "Exam end must be in the future.");
        data.Productivity.ActiveExamMode = new ExamModeData { ExamId = examId, Title = title.Trim(), StartedAt = now, EndsAt = endsAt };
        return GetExamMode(data, now)!;
    }

    public ExamModeSnapshot? GetExamMode(StudyData data, DateTimeOffset now)
    {
        var mode = data.Productivity.ActiveExamMode;
        if (mode is null) return null;
        var remaining = mode.EndsAt - now;
        return new ExamModeSnapshot(mode.Title, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, remaining <= TimeSpan.Zero);
    }

    public void StopExamMode(StudyData data) => data.Productivity.ActiveExamMode = null;

    public void SetDashboardWidgets(StudyData data, IEnumerable<DashboardWidgetPreferenceData> preferences)
    {
        ArgumentNullException.ThrowIfNull(data);
        var rows = preferences.ToList();
        if (rows.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new ArgumentException("Dashboard widget keys must be unique.", nameof(preferences));
        if (rows.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Order < 0))
            throw new ArgumentException("Dashboard widget keys and order must be valid.", nameof(preferences));
        data.Productivity.DashboardWidgets = rows.OrderBy(x => x.Order).ToList();
    }

    public IReadOnlyList<DashboardWidgetPreferenceData> GetDashboardWidgets(StudyData data)
    {
        if (data.Productivity.DashboardWidgets.Count == 0)
            return DefaultWidgetKeys.Select((key, index) => new DashboardWidgetPreferenceData { Key = key, Order = index }).ToArray();
        return data.Productivity.DashboardWidgets.OrderBy(x => x.Order).ToArray();
    }

    public StudyNextActivity? GetNextActivity(StudyData data, DateTimeOffset now)
    {
        var exam = data.Exams.Where(x => !x.Completed && x.PlannedDate.HasValue)
            .Select(x => new StudyNextActivity(x.Name, new DateTimeOffset(x.PlannedDate.GetValueOrDefault()), "Exam", x.CourseId))
            .Where(x => x.StartsAt >= now);
        var reminder = data.Productivity.Reminders.Where(x => x.Enabled && x.DueAt >= now)
            .Select(x => new StudyNextActivity(x.Title, x.DueAt, x.Kind, x.CourseId));
        return exam.Concat(reminder).OrderBy(x => x.StartsAt).FirstOrDefault();
    }

    public string ExportAttendanceCsv(StudyData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CourseId,Title,StartsAt,DurationMinutes,Attended,Notes");
        foreach (var row in data.Productivity.Attendance.OrderBy(x => x.StartsAt))
            builder.AppendLine(string.Join(",", new[] { row.CourseId, row.Title, row.StartsAt.ToString("O", CultureInfo.InvariantCulture),
                row.DurationMinutes.ToString(CultureInfo.InvariantCulture), row.Attended ? "true" : "false", row.Notes ?? string.Empty }.Select(EscapeCsv)));
        return builder.ToString();
    }

    private static string EscapeCsv(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
