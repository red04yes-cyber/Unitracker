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
