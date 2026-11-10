using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ExportAndImportSafetyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly StudyExportRequest Request =
        new(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), "UTC");

    private static StudyData HostileData()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
        };
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "attivita-1",
            Type = StudyActivityType.Study,
            Title = "<script>alert('x')</script>",
            CourseId = "analisi",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            TimeZoneId = "UTC",
            EffectiveSeconds = 1800,
            Note = "=cmd|' /C calc'!A0",
            Tags = new ObservableCollection<string> { "=SUM(1,2)", "@user" },
            IsPrivate = false
        });
        return data;
    }

    private static StudyData LinkedSessionData(
        DateTimeOffset sessionStart,
        DateTimeOffset sessionEnd,
        string sessionCourseId,
        string sessionTopic)
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "course-a", Name = "Corso A", Cfu = 6 },
                new() { Id = "course-b", Name = "Corso B", Cfu = 6 }
            }
        };
        var session = new StudySessionData
        {
            Id = "session-course-b",
            CourseId = sessionCourseId,
            Topic = sessionTopic,
            PlannedMinutes = 30,
            StartedAt = sessionStart,
            EndedAt = sessionEnd,
            State = StudySessionState.Completed,
            Notes = "nota riservata corso B",
            NotesArePrivate = false
        };
        session.Segments.Add(new StudySessionSegmentData
        {
            Id = "segment-course-b",
            Type = StudySessionSegmentType.Focus,
            StartedAt = sessionStart,
            EndedAt = sessionEnd
        });
        data.StudySessions.Add(session);
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "activity-course-a",
            Title = "Attività corso A",
            CourseId = "course-a",
            SessionId = session.Id,
            StartedAt = Start.AddMinutes(5),
            EndedAt = Start.AddMinutes(25),
            TimeZoneId = "UTC",
            EffectiveSeconds = 1200
        });
        return data;
    }

    private static string ExportText(StudyData data, StudyExportRequest request, StudyExportFormat format)
    {
        var bundle = new StudyDataExportService().Export(data, request, format, Start.AddHours(1));
        if (format != StudyExportFormat.Xlsx)
            return string.Join(Environment.NewLine,
                bundle.Files.Select(file => Encoding.UTF8.GetString(file.Content)));

        using var archive = new ZipArchive(new MemoryStream(bundle.Files.Single().Content), ZipArchiveMode.Read);
        return string.Join(Environment.NewLine, archive.Entries.Select(entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }));
    }

    [Theory]
    [InlineData(StudyExportFormat.Csv)]
    [InlineData(StudyExportFormat.Xlsx)]
    [InlineData(StudyExportFormat.Json)]
    public void LinkedSessionFromAnotherCourseIsExcludedFromScopedExports(StudyExportFormat format)
    {
        var data = LinkedSessionData(Start, Start.AddMinutes(30), "course-b", "Sessione corso B");
        var request = Request with
        {
            CourseIds = new[] { "course-a" },
            IncludePrivateNotes = true
        };

        var exported = ExportText(data, request, format);

        Assert.Contains("Attività corso A", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("Sessione corso B", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("nota riservata corso B", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("segment-course-b", exported, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StudyExportFormat.Csv)]
    [InlineData(StudyExportFormat.Xlsx)]
    [InlineData(StudyExportFormat.Json)]
    public void LinkedSessionOutsideDateRangeIsExcludedFromScopedExports(StudyExportFormat format)
    {
        var sessionStart = Start.AddDays(-2);
        var data = LinkedSessionData(
            sessionStart,
            sessionStart.AddMinutes(30),
            "course-a",
            "Sessione fuori intervallo");
        var request = Request with { IncludePrivateNotes = true };

        var exported = ExportText(data, request, format);

        Assert.Contains("Attività corso A", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("Sessione fuori intervallo", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("nota riservata corso B", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("segment-course-b", exported, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExportEscapesHtmlSensitiveText()
    {
        var json = Encoding.UTF8.GetString(new StudyDataExportService()
            .Export(HostileData(), Request, StudyExportFormat.Json, Start.AddHours(1))
            .Files.Single().Content);

        Assert.DoesNotContain("<script>", json, StringComparison.Ordinal);
        Assert.Contains("\\u003C", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvRejectsFormulaInjectionInNotesAndTags()
    {
        var csv = Encoding.UTF8.GetString(new StudyDataExportService()
            .Export(HostileData(), Request, StudyExportFormat.Csv, Start.AddHours(1))
            .Files.Single(file => file.FileName == "activities.csv").Content);

        Assert.Contains("'=cmd|", csv, StringComparison.Ordinal);
        Assert.Contains("'=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains(", @user", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\"=cmd|", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxUsesInlineStringsAndNeverFormulaElements()
    {
        var file = new StudyDataExportService()
            .Export(HostileData(), Request, StudyExportFormat.Xlsx, Start.AddHours(1))
            .Files.Single();
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("&lt;script&gt;", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<f>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void IcsImportRejectsOversizedContent()
    {
        var oversized = new string('A', (int)IcsImportService.MaximumFileBytes + 1);
        Assert.Throws<InvalidDataException>(() =>
            new IcsImportService().ImportText(oversized, new StudyData()));
    }

    [Fact]
    public void IcsImportTruncatesOverlongFieldsAndWarnsOnIncompleteEvents()
    {
        var data = new StudyData();
        var longTitle = new string('x', 600);
        var content = string.Join("\r\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "DTSTART:20261104T090000Z",
            "SUMMARY:" + longTitle,
            "DESCRIPTION:=cmd|' /C calc'!A0",
            "END:VEVENT",
            "BEGIN:VEVENT",
            "SUMMARY:Evento senza data",
            "END:VEVENT",
            "END:VCALENDAR");

        var result = new IcsImportService().ImportText(content, data);

        var imported = Assert.Single(data.CalendarEvents);
        Assert.Equal(400, imported.Title.Length);
        Assert.Contains("DTSTART", string.Join(" ", result.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IcsImportSanitizesControlCharacters()
    {
        var data = new StudyData();
        var content = string.Join("\r\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "DTSTART:20261104T090000Z",
            "SUMMARY:Lezione\u0007 di analisi",
            "END:VEVENT",
            "END:VCALENDAR");

        new IcsImportService().ImportText(content, data);

        var imported = Assert.Single(data.CalendarEvents);
        Assert.DoesNotContain('\u0007', imported.Title);
        Assert.Contains("Lezione", imported.Title, StringComparison.Ordinal);
    }
}
