using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyProductivityServiceTests
{
    private readonly StudyProductivityService _service = new();
    private static StudyData DataWithCourse()
    {
        var data = new StudyData();
        data.Courses.Add(new Course { Id = "course-1", Name = "Analisi", Cfu = 9 });
        return data;
    }

    [Fact]
    public void WeeklyGoalAndStreakUseOnlyRecordedFocus()
    {
        var data = DataWithCourse();
        _service.SetGoals(data, 30, 180);
        _service.RecordFocus(data, new DateOnly(2026, 11, 9), 45);
        _service.RecordFocus(data, new DateOnly(2026, 11, 10), 35);

        var result = _service.GetStreak(data, new DateOnly(2026, 11, 10));

        Assert.Equal(2, result.CurrentStreakDays);
        Assert.Equal(2, result.LongestStreakDays);
        Assert.Equal(80, result.MinutesThisWeek);
        Assert.Equal(35, result.TodayMinutes);
    }

    [Fact]
    public void StreakCanContinueUntilTheEndOfTheCurrentDay()
    {
        var data = DataWithCourse();
        _service.RecordFocus(data, new DateOnly(2026, 11, 9), 60);

        Assert.Equal(1, _service.GetStreak(data, new DateOnly(2026, 11, 10)).CurrentStreakDays);
    }

    [Fact]
    public void ReminderIsScopedToWindowAndCanBeMarkedNotified()
    {
        var data = DataWithCourse();
        var now = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var reminder = _service.AddReminder(data, "Esame", now.AddMinutes(30), courseId: "course-1");

        Assert.Single(_service.GetDueReminders(data, now, TimeSpan.FromHours(1)));
        _service.MarkReminderNotified(data, reminder.Id, now);
        Assert.Empty(_service.GetDueReminders(data, now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void QuickNoteIsLinkedToCourseAndTopicAndCanBeUpdated()
    {
        var data = DataWithCourse();
        var note = _service.SaveQuickNote(data, null, "course-1", "Derivate", "Regola della catena");
        var updated = _service.SaveQuickNote(data, note.Id, "course-1", "Derivate", "Regola della catena e prodotto");

        Assert.Same(note, updated);
        Assert.Single(data.Productivity.QuickNotes);
        Assert.Contains("prodotto", updated.Content);
    }

    [Fact]
    public void AttendanceCanBeRecordedAndExportedAsCsv()
    {
        var data = DataWithCourse();
        _service.RecordAttendance(data, "course-1", "Lezione 1", new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), 90, true, "Appunti");

        var csv = _service.ExportAttendanceCsv(data);

        Assert.Contains("Lezione 1", csv);
        Assert.Contains("true", csv);
        Assert.Contains("Appunti", csv);
    }

    [Fact]
    public void ExamModeCountsDownAndCanBeStopped()
    {
        var data = DataWithCourse();
        var now = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        _service.StartExamMode(data, "exam-1", "Prova scritta", now.AddHours(2), now);

        var snapshot = _service.GetExamMode(data, now.AddMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(90), snapshot!.Remaining);
        Assert.False(snapshot.IsExpired);
        Assert.True(_service.GetExamMode(data, now.AddHours(3))!.IsExpired);

        _service.StopExamMode(data);
        Assert.Null(_service.GetExamMode(data, now));
    }

    [Fact]
    public void DashboardPreferencesAreOrderedAndDefaultWhenUnset()
    {
        var data = DataWithCourse();
        Assert.Equal("next-activity", _service.GetDashboardWidgets(data)[0].Key);
        _service.SetDashboardWidgets(data, new[]
        {
            new DashboardWidgetPreferenceData { Key = "streak", Order = 1 },
            new DashboardWidgetPreferenceData { Key = "next-activity", Order = 0, IsVisible = false }
        });

        var widgets = _service.GetDashboardWidgets(data);
        Assert.Equal("next-activity", widgets[0].Key);
        Assert.False(widgets[0].IsVisible);
        Assert.Equal("streak", widgets[1].Key);
    }
    [Fact]
    public void PlannedExamsSynchronizeIntoSingleUpdatableReminderRecords()
    {
        var data = DataWithCourse();
        var exam = new Exam
        {
            Id = "exam-1",
            CourseId = "course-1",
            Name = "Analisi I",
            PlannedDate = new DateTime(2026, 11, 12, 14, 30, 0)
        };

        data.Exams.Add(exam);
        Assert.True(_service.SynchronizeExamReminders(data));
        var reminder = Assert.Single(data.Productivity.Reminders);
        Assert.Equal("Esame: Analisi I", reminder.Title);
        Assert.Equal("exam-1", reminder.SourceExamId);
        Assert.Equal(9, reminder.DueAt.Hour);
        Assert.False(_service.SynchronizeExamReminders(data));

        reminder.LastNotifiedAt = DateTimeOffset.Now;
        exam.PlannedDate = new DateTime(2026, 11, 13, 16, 0, 0);
        Assert.True(_service.SynchronizeExamReminders(data));
        Assert.Same(reminder, Assert.Single(data.Productivity.Reminders));
        Assert.Equal(13, reminder.DueAt.Day);
        Assert.Null(reminder.LastNotifiedAt);

        exam.Completed = true;
        Assert.True(_service.SynchronizeExamReminders(data));
        Assert.False(reminder.Enabled);
    }

    [Fact]
    public void FocusCannotPushADayPastTwentyFourHours()
    {
        var data = DataWithCourse();
        _service.RecordFocus(data, new DateOnly(2026, 11, 10), 1400);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.RecordFocus(data, new DateOnly(2026, 11, 10), 41));
        Assert.Equal(1400, data.Productivity.StudyDays.Single().FocusMinutes);
    }

    [Fact]
    public void LongestStreakAggregatesDuplicateRowsForTheSameCalendarDay()
    {
        var data = DataWithCourse();
        data.Productivity.StudyDays.Add(new StudyDayLogData { Date = new DateOnly(2026, 11, 9), FocusMinutes = 30 });
        data.Productivity.StudyDays.Add(new StudyDayLogData { Date = new DateOnly(2026, 11, 9), FocusMinutes = 30 });
        data.Productivity.StudyDays.Add(new StudyDayLogData { Date = new DateOnly(2026, 11, 10), FocusMinutes = 60 });

        var result = _service.GetStreak(data, new DateOnly(2026, 11, 10));

        Assert.Equal(2, result.CurrentStreakDays);
        Assert.Equal(2, result.LongestStreakDays);
    }

}
