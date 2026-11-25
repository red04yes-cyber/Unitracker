using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class SyllabusTrackerServiceTests
{
    private static StudyData CreateData() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = "Analisi", Cfu = 6 }
        }
    };

    private readonly SyllabusTrackerService _service = new();

    [Fact]
    public void AddNormalizesTitleAndDescriptionControlCharacters()
    {
        var data = CreateData();

        var topic = _service.Add(data, "course-1", "  Limiti\0  ", "Prima riga\0\nSeconda riga");

        Assert.Equal("Limiti", topic.Title);
        Assert.Equal("Prima riga\nSeconda riga", topic.Description);
    }

    [Fact]
    public void AddAcceptsSafeHttpAndHttpsSourceUrlsAndNormalizesThem()
    {
        var data = CreateData();

        var https = _service.Add(data, "course-1", "HTTPS", sourceUrl: "https://moodle.example/course/view.php?id=42");
        var http = _service.Add(data, "course-1", "HTTP", sourceUrl: "http://localhost:8080/course?id=42");

        Assert.Equal("https://moodle.example/course/view.php?id=42", https.SourceUrl);
        Assert.Equal("http://localhost:8080/course?id=42", http.SourceUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private.pdf")]
    [InlineData("https://user:password@moodle.example/course?id=42")]
    [InlineData("https://moodle.example/course?id=42#token=secret")]
    [InlineData("https://moodle.example/course?token=secret")]
    [InlineData("https://moodle.example/course?wstoken=secret")]
    [InlineData("https://moodle.example/course?access_token=secret")]
    [InlineData("https://moodle.example/course?api_key=secret")]
    [InlineData("https://moodle.example/course?%77stoken=secret")]
    public void AddRejectsUnsafeSourceUrls(string url)
    {
        var data = CreateData();

        Assert.Throws<ArgumentException>(() => _service.Add(data, "course-1", "Topic", sourceUrl: url));
        Assert.Empty(data.SyllabusTopics);
    }

    [Fact]
    public void AddRejectsControlCharactersAndOversizedSourceUrls()
    {
        var data = CreateData();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Add(data, "course-1", "Topic", sourceUrl: "https://example.com/\npath"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Add(data, "course-1", "Topic", sourceUrl: "https://example.com/" + new string('a', SyllabusTrackerService.MaximumSourceUrlLength)));
    }

    [Fact]
    public void AddRejectsOversizedTopicText()
    {
        var data = CreateData();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Add(data, "course-1", new string('t', SyllabusTrackerService.MaximumTitleLength + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Add(data, "course-1", "Topic", new string('d', SyllabusTrackerService.MaximumDescriptionLength + 1)));
        Assert.Empty(data.SyllabusTopics);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(525601)]
    public void AddRejectsEstimatedMinutesOutsideSupportedRange(int minutes)
    {
        var data = CreateData();

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Add(data, "course-1", "Topic", estimatedMinutes: minutes));
        Assert.Empty(data.SyllabusTopics);
    }

    [Fact]
    public void CoverageSaturatesInsteadOfOverflowingAndIgnoresLegacyNegativeEstimates()
    {
        var data = CreateData();
        data.SyllabusTopics.Add(new SyllabusTopicData { Id = "large-1", CourseId = "course-1", EstimatedMinutes = int.MaxValue });
        data.SyllabusTopics.Add(new SyllabusTopicData { Id = "large-2", CourseId = "course-1", EstimatedMinutes = int.MaxValue });
        data.SyllabusTopics.Add(new SyllabusTopicData { Id = "negative", CourseId = "course-1", EstimatedMinutes = -500 });

        var coverage = _service.GetCoverage(data, "course-1");

        Assert.Equal(3, coverage.TotalTopics);
        Assert.Equal(int.MaxValue, coverage.EstimatedRemainingMinutes);
        Assert.Equal(0m, coverage.CompletedPercent);
    }

    [Fact]
    public void GetTopicsRejectsUnknownCourseInsteadOfReturningEmptyList()
    {
        var data = CreateData();

        Assert.Throws<ArgumentException>(() => _service.GetTopics(data, "missing-course"));
    }

    [Fact]
    public void UpdateStatusRejectsInvalidEnumAndNormalizesUnspecifiedStudyDateToUtc()
    {
        var data = CreateData();
        var topic = _service.Add(data, "course-1", "Topic");
        var unspecified = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.UpdateStatus(data, topic.Id, (SyllabusTopicStatus)999));
        _service.UpdateStatus(data, topic.Id, SyllabusTopicStatus.Completed, unspecified);

        Assert.Equal(DateTimeKind.Utc, topic.LastStudiedAt!.Value.Kind);
        Assert.Equal(unspecified, topic.LastStudiedAt.Value);
    }
}
