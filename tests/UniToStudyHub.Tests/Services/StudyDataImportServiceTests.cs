using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyDataImportServiceTests
{
    private static StudyData Data(params (string Id, string Name)[] courses)
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>(courses.Select(x =>
                new Course { Id = x.Id, Name = x.Name, Cfu = 6 }))
        };
        return data;
    }

    private static byte[] Export(StudyData data) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = PrivacyService.ExportSchemaVersion,
            generatedAt = DateTimeOffset.Parse("2026-11-08T12:00:00Z"),
            data
        });

    [Fact]
    public async Task PreviewReportsCountsAndExistingIdConflictsWithoutChangingTheStore()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data(("course-1", "Locale")));
        var service = new StudyDataImportService(store);

        var preview = await service.PreviewAsync(Export(Data(
            ("course-1", "Esportato"),
            ("course-2", "Nuovo"))));

        Assert.Equal(2, preview.ItemCounts["Corsi"]);
        Assert.Equal(1, preview.DuplicateCounts["Corsi"]);
        Assert.Contains(preview.Warnings, warning => warning.Contains("non sovrascriveranno", StringComparison.Ordinal));
        Assert.Equal("Locale", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task MergeAddsMissingRecordsAndPreservesExistingRecordsAndSettings()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var existing = Data(("course-1", "Locale"));
        existing.DegreeProgramName = "Informatica";
        existing.MoodleBaseUrl = "https://moodle.local.example";
        await store.SaveAsync(existing);

        var imported = Data(("course-1", "Esportato"), ("course-2", "Nuovo"));
        imported.Grades.Add(new Grade
        {
            Id = "grade-2", CourseId = "course-2", CourseName = "Nuovo",
            Value = 28, Cfu = 6, Date = new DateTime(2026, 6, 1)
        });
        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(imported));

        var result = await service.ApplyAsync(preview, StudyDataImportStrategy.Merge);

        var saved = await store.LoadAsync();
        Assert.Equal(2, saved.Courses.Count);
        Assert.Equal("Locale", saved.Courses.Single(x => x.Id == "course-1").Name);
        Assert.Equal("Nuovo", saved.Courses.Single(x => x.Id == "course-2").Name);
        Assert.Single(saved.Grades);
        Assert.Equal("Informatica", saved.DegreeProgramName);
        Assert.Equal("https://moodle.local.example", saved.MoodleBaseUrl);
        Assert.Equal(4, result.AddedRecords);
        Assert.Null(result.SafetyBackupPath);
    }

    [Fact]
    public async Task ReplaceCreatesSafetyBackupBeforeReplacingTheDatabase()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data(("course-local", "Locale")));
        var backupService = new BackupService(database.Factory, path,
            Path.Combine(Path.GetDirectoryName(path)!, "backups"), new TestBackupKeyProtector());
        var service = new StudyDataImportService(store, backupService);
        var preview = await service.PreviewAsync(Export(Data(("course-import", "Importato"))));

        var result = await service.ApplyAsync(preview, StudyDataImportStrategy.Replace,
            "test passphrase");

        Assert.NotNull(result.SafetyBackupPath);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal("Importato", (await store.LoadAsync()).Courses.Single().Name);

        await backupService.RestoreAsync(result.SafetyBackupPath!, "test passphrase");
        Assert.Equal("Locale", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task ReplaceIsRejectedWhenNoBackupServiceIsConfigured()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(Data(("course-1", "Importato"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(preview, StudyDataImportStrategy.Replace));
        Assert.Empty((await store.LoadAsync()).Courses);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":\"99.0\",\"data\":{}}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"data\":null}")]
    public async Task RejectsMalformedOrUnsupportedExport(string json)
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var service = new StudyDataImportService(new SqliteStudyDataStore(database.Factory));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PreviewAsync(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task RejectsInvalidRelationshipsAndOversizedPayloads()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var service = new StudyDataImportService(new SqliteStudyDataStore(database.Factory));
        var invalid = Data(("course-1", "Analisi"));
        invalid.Grades.Add(new Grade
        {
            Id = "grade-1", CourseId = "missing", Value = 28, Cfu = 6
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(Export(invalid)));

        var oversized = new byte[StudyDataImportService.MaximumImportBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(oversized));
    } 
    [Fact]
    public async Task PreviewRejectsExcessiveNestedCollectionItemsBeforeLoadingTheStore()
    {
        var store = new TrackingStore();
        var service = new StudyDataImportService(store);
        var payload = ExportWithNestedCollectionItems(StudyDataImportService.MaximumImportedRecords + 1);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(payload));

        Assert.Contains("elementi nelle collezioni", exception.Message, StringComparison.Ordinal);
        Assert.False(store.LoadCalled);
    }

    [Fact]
    public async Task PreviewHonorsCancellationBeforeScanningThePayload()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new TrackingStore();
        var service = new StudyDataImportService(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(Export(Data(("course-1", "Analisi"))), cancellation.Token));

        Assert.False(store.LoadCalled);
    }

    [Fact]
    public async Task MergeAddsMissingNestedRecordsAndPreservesExistingParents()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var start = Start;
        var existing = Data(("course-1", "Corso locale"));
        existing.CareerExams.Add(new CareerExamData
        {
            Id = "career-1",
            CourseId = "course-1",
            Name = "Esame locale",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new() { Id = "attempt-1", AttemptedAt = start }
            }
        });
        existing.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            CourseId = "course-1",
            Topic = "Argomento locale",
            PlannedMinutes = 25,
            StartedAt = start,
            LastResumedAt = start.AddMinutes(15),
            EndedAt = start.AddMinutes(15),
            AccumulatedFocusSeconds = 900,
            State = StudySessionState.Completed,
            CreatedAt = start,
            UpdatedAt = start.AddMinutes(15),
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new() { Id = "segment-1", Type = StudySessionSegmentType.Focus, StartedAt = start, EndedAt = start.AddMinutes(5) },
                new() { Id = "segment-3", Type = StudySessionSegmentType.Focus, StartedAt = start.AddMinutes(10), EndedAt = start.AddMinutes(15) }
            }
        });
        existing.StudyMissions.Add(new StudyMissionData
        {
            Id = "mission-1",
            Title = "Missione locale",
            CourseId = "course-1",
            StartsAt = start,
            DueAt = start.AddDays(1),
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-1", Title = "Passo locale", Position = 0 }
            }
        });
        await store.SaveAsync(existing);

        var imported = Data(("course-1", "Corso importato"));
        imported.CareerExams.Add(new CareerExamData
        {
            Id = "career-1",
            CourseId = "course-1",
            Name = "Esame importato",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new() { Id = "attempt-1", AttemptedAt = start },
                new() { Id = "attempt-2", AttemptedAt = start.AddDays(1) }
            }
        });
        imported.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            CourseId = "course-1",
            Topic = "Argomento importato",
            PlannedMinutes = 50,
            StartedAt = start,
            LastResumedAt = start.AddMinutes(15),
            EndedAt = start.AddMinutes(15),
            AccumulatedFocusSeconds = 900,
            State = StudySessionState.Completed,
            CreatedAt = start,
            UpdatedAt = start.AddMinutes(15),
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new() { Id = "segment-1", Type = StudySessionSegmentType.Focus, StartedAt = start, EndedAt = start.AddMinutes(5) },
                new() { Id = "segment-2", Type = StudySessionSegmentType.Focus, StartedAt = start.AddMinutes(5), EndedAt = start.AddMinutes(10) },
                new() { Id = "segment-3", Type = StudySessionSegmentType.Focus, StartedAt = start.AddMinutes(10), EndedAt = start.AddMinutes(15) }
            }
        });
        imported.StudyMissions.Add(new StudyMissionData
        {
            Id = "mission-1",
            Title = "Missione importata",
            CourseId = "course-1",
            StartsAt = start,
            DueAt = start.AddDays(2),
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-1", Title = "Passo importato", Position = 0 },
                new() { Id = "step-2", Title = "Nuovo passo", Position = 1 }
            }
        });

        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(imported));

        Assert.Equal(2, preview.ItemCounts["Tentativi d'esame"]);
        Assert.Equal(3, preview.ItemCounts["Segmenti sessione"]);
        Assert.Equal(2, preview.ItemCounts["Passi missione"]);
        Assert.Equal(1, preview.DuplicateCounts["Tentativi d'esame"]);
        Assert.Equal(2, preview.DuplicateCounts["Segmenti sessione"]);
        Assert.Equal(1, preview.DuplicateCounts["Passi missione"]);

        var firstApply = await service.ApplyAsync(preview, StudyDataImportStrategy.Merge);
        var saved = await store.LoadAsync();
        var savedExam = Assert.Single(saved.CareerExams);
        var savedSession = Assert.Single(saved.StudySessions);
        var savedMission = Assert.Single(saved.StudyMissions);

        Assert.Equal("Esame locale", savedExam.Name);
        Assert.Equal(new[] { "attempt-1", "attempt-2" }, savedExam.Attempts.Select(x => x.Id).ToArray());
        Assert.Equal("Argomento locale", savedSession.Topic);
        Assert.Equal(new[] { "segment-1", "segment-2", "segment-3" },
            savedSession.Segments.Select(x => x.Id).ToArray());
        Assert.Equal("Missione locale", savedMission.Title);
        Assert.Equal(new[] { "step-1", "step-2" }, savedMission.Steps.Select(x => x.Id).ToArray());
        Assert.Equal(3, firstApply.AddedRecords);

        var secondApply = await service.ApplyAsync(preview, StudyDataImportStrategy.Merge);
        saved = await store.LoadAsync();
        Assert.Equal(0, secondApply.AddedRecords);
        Assert.Single(saved.CareerExams);
        Assert.Equal(2, saved.CareerExams.Single().Attempts.Count);
        Assert.Equal(3, saved.StudySessions.Single().Segments.Count);
        Assert.Equal(2, saved.StudyMissions.Single().Steps.Count);
    }


    [Fact]
    public async Task MergeSkipsNestedIdsAlreadyOwnedByAnotherParent()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var existing = Data(("course-1", "Corso uno"), ("course-2", "Corso due"));
        existing.CareerExams.Add(new CareerExamData
        {
            Id = "career-1",
            CourseId = "course-1",
            Name = "Esame uno",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new() { Id = "attempt-shared", AttemptedAt = Start }
            }
        });
        existing.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            CourseId = "course-1",
            PlannedMinutes = 25,
            StartedAt = Start,
            LastResumedAt = Start.AddMinutes(5),
            EndedAt = Start.AddMinutes(5),
            AccumulatedFocusSeconds = 300,
            State = StudySessionState.Completed,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-shared",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = Start,
                    EndedAt = Start.AddMinutes(5)
                }
            }
        });
        await store.SaveAsync(existing);

        var imported = Data(("course-1", "Corso uno importato"), ("course-2", "Corso due importato"));
        imported.CareerExams.Add(new CareerExamData
        {
            Id = "career-2",
            CourseId = "course-2",
            Name = "Esame due",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new() { Id = "attempt-shared", AttemptedAt = Start },
                new() { Id = "attempt-new", AttemptedAt = Start.AddDays(1) }
            }
        });
        imported.StudySessions.Add(new StudySessionData
        {
            Id = "session-2",
            CourseId = "course-2",
            PlannedMinutes = 25,
            StartedAt = Start.AddHours(1),
            LastResumedAt = Start.AddHours(1).AddMinutes(10),
            EndedAt = Start.AddHours(1).AddMinutes(10),
            AccumulatedFocusSeconds = 300,
            State = StudySessionState.Completed,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-shared",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = Start.AddHours(1),
                    EndedAt = Start.AddHours(1).AddMinutes(5)
                },
                new()
                {
                    Id = "segment-new",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = Start.AddHours(1).AddMinutes(5),
                    EndedAt = Start.AddHours(1).AddMinutes(10)
                }
            }
        });

        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(imported));
        Assert.Equal(1, preview.DuplicateCounts["Tentativi d'esame"]);
        Assert.Equal(1, preview.DuplicateCounts["Segmenti sessione"]);

        await service.ApplyAsync(preview, StudyDataImportStrategy.Merge);

        var saved = await store.LoadAsync();
        var secondExam = Assert.Single(saved.CareerExams.Where(x => x.CourseId == "course-2"));
        var secondSession = Assert.Single(saved.StudySessions.Where(x => x.Id == "session-2"));
        Assert.Equal(new[] { "attempt-new" }, secondExam.Attempts.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { "segment-new" }, secondSession.Segments.Select(x => x.Id).ToArray());
    }

    private static byte[] ExportWithNestedCollectionItems(int count)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", PrivacyService.ExportSchemaVersion);
            writer.WriteString("generatedAt", Start);
            writer.WritePropertyName("data");
            writer.WriteStartObject();
            writer.WritePropertyName("studySessions");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("id", "session-oversized");
            writer.WritePropertyName("segments");
            writer.WriteStartArray();
            for (var index = 0; index < count; index++)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private sealed class TrackingStore : IStudyDataStore
    {
        public bool LoadCalled { get; private set; }

        public Task<StudyData> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCalled = true;
            return Task.FromResult(new StudyData());
        }

        public Task SaveAsync(StudyData data, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
