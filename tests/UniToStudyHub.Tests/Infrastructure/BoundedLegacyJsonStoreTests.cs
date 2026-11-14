using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

[Collection("AppDiagnostics")]
public sealed class BoundedLegacyJsonStoreTests
{
    [Fact]
    public async Task CalendarStoreLoadsValidLegacyCalendar()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "calendar-events.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[]
            {
                new CalendarEvent { Id = "event-1", Title = "Lezione di Analisi" }
            }));

            var events = await new CalendarEventStore(directory).LoadAsync();

            var item = Assert.Single(events);
            Assert.Equal("event-1", item.Id);
            Assert.Equal("Lezione di Analisi", item.Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CalendarStorePreservesOversizedLegacyCalendar()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "calendar-events.json");
        try
        {
            CreateSparseFile(path, CalendarEventStore.MaximumFileBytes + 1);

            var events = await new CalendarEventStore(directory).LoadAsync();

            Assert.Empty(events);
            Assert.True(File.Exists(path));
            Assert.Equal(CalendarEventStore.MaximumFileBytes + 1, new FileInfo(path).Length);
            Assert.Empty(Directory.GetFiles(directory, "calendar-events.json.invalid-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CalendarStoreParksMalformedJsonWithoutImportingIt()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "calendar-events.json");
        const string invalid = "{not-json";
        try
        {
            await File.WriteAllTextAsync(path, invalid);

            var events = await new CalendarEventStore(directory).LoadAsync();

            Assert.Empty(events);
            Assert.False(File.Exists(path));
            var parked = Assert.Single(Directory.GetFiles(directory, "calendar-events.json.invalid-*"));
            Assert.Equal(invalid, await File.ReadAllTextAsync(parked));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalDataStoreLoadsValidLegacyData()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "data.json");
            var data = new StudyData
            {
                Courses = new ObservableCollection<Course>
                {
                    new() { Id = "course-1", Name = "Analisi", Cfu = 9 }
                }
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));

            var loaded = await new LocalDataStore(directory).LoadAsync();

            var course = Assert.Single(loaded.Courses);
            Assert.Equal("course-1", course.Id);
            Assert.Equal("Analisi", course.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalDataStorePreservesOversizedLegacyData()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "data.json");
        try
        {
            CreateSparseFile(path, LocalDataStore.MaximumFileBytes + 1);

            var loaded = await new LocalDataStore(directory).LoadAsync();

            Assert.Empty(loaded.Courses);
            Assert.True(File.Exists(path));
            Assert.Equal(LocalDataStore.MaximumFileBytes + 1, new FileInfo(path).Length);
            Assert.Empty(Directory.GetFiles(directory, "data.json.invalid-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateSparseFile(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }
}
