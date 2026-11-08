using System.Collections.ObjectModel;
using System.IO;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class SprintADatabaseQaTests
{
    [Fact]
    public async Task MigrationFromThePreviousSchemaKeepsExistingCourses()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261105011642_AddFocusSessionSegments");
        await using (var db = database.CreateContext())
        {
            db.Courses.Add(new CourseEntity { Id = "legacy", Name = "Storia", Cfu = 9 });
            await db.SaveChangesAsync();
        }

        await database.MigrateAsync();

        var store = new SqliteStudyDataStore(database.Factory);
        var loaded = await store.LoadAsync();
        var course = Assert.Single(loaded.Courses);
        Assert.Equal("legacy", course.Id);
        Assert.Equal("Storia", course.Name);
        Assert.Equal(9, course.Cfu);

        var tables = await database.QueryScalarsAsync(
            "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('PomodoroSettings','PomodoroTimer','PomodoroCycles') ORDER BY name");
        Assert.Equal(3, tables.Count);
    }

    [Fact]
    public async Task RejectedSaveLeavesThePreviousDatasetIntact()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
        });

        var invalid = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "duplicato", Name = "Prima", Cfu = 6 },
                new() { Id = "duplicato", Name = "Seconda", Cfu = 6 }
            }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.Equal("analisi", Assert.Single(loaded.Courses).Id);
        Assert.Equal(6, loaded.Courses[0].Cfu);
    }
}
