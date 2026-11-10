using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class ProductivityPersistenceTests
{
    [Fact]
    public async Task ProductivityStateSurvivesSqliteMigrationAndRoundTrip()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new StudyHubDbContext(options);
        await db.Database.MigrateAsync();

        var data = new StudyData();
        data.Courses.Add(new Course { Id = "course-1", Name = "Analisi", Cfu = 9 });
        data.Productivity.DailyStudyGoalMinutes = 45;
        data.Productivity.WeeklyStudyGoalMinutes = 300;
        data.Productivity.QuickNotes.Add(new QuickStudyNoteData
        {
            Id = "note-1", CourseId = "course-1", Topic = "Limiti", Content = "Definizione epsilon-delta"
        });
        data.Productivity.Attendance.Add(new LectureAttendanceData
        {
            Id = "attendance-1", CourseId = "course-1", Title = "Lezione introduttiva",
            StartsAt = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), DurationMinutes = 90, Attended = true
        });

        StudyDataEntityMapper.NormalizeAndValidate(data);
        StudyDataEntityMapper.AddToContext(db, data);
        await db.SaveChangesAsync();

        var restored = await StudyDataEntityMapper.ReadFromContextAsync(db, CancellationToken.None);

        Assert.Equal(45, restored.Productivity.DailyStudyGoalMinutes);
        Assert.Equal(300, restored.Productivity.WeeklyStudyGoalMinutes);
        Assert.Single(restored.Productivity.QuickNotes);
        Assert.Equal("Limiti", restored.Productivity.QuickNotes[0].Topic);
        Assert.Single(restored.Productivity.Attendance);
        Assert.True(restored.Productivity.Attendance[0].Attended);
    }
}
