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
    [InlineData("https://moodle.example/course?api_key=secret")]\n    [InlineData("https://moodle.example/course?sesskey=secret")]\n    [InlineData("https://moodle.example/course?key=secret")]\n    [InlineData("https://moodle.example/course?signature=secret")]
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
    public void GetTopicsDueForReviewReturnsCompletedTopicsAtOrBeforeCutoffInOldestFirstOrder()
    {
        var data = CreateData();
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "oldest",
            CourseId = "course-1",
            Title = "Oldest",
            Status = SyllabusTopicStatus.Completed,
            LastStudiedAt = now.AddDays(-10).UtcDateTime
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "boundary",
            CourseId = "course-1",
            Title = "Boundary",
            Status = SyllabusTopicStatus.Completed,
            LastStudiedAt = now.AddDays(-7).UtcDateTime
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "recent",
            CourseId = "course-1",
            Title = "Recent",
            Status = SyllabusTopicStatus.Completed,
            LastStudiedAt = now.AddDays(-2).UtcDateTime
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "in-progress",
            CourseId = "course-1",
            Title = "In progress",
            Status = SyllabusTopicStatus.InProgress,
            LastStudiedAt = now.AddDays(-30).UtcDateTime
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "not-studied",
            CourseId = "course-1",
            Title = "Not studied",
            Status = SyllabusTopicStatus.Completed
        });

        var due = _service.GetTopicsDueForReview(data, "course-1", TimeSpan.FromDays(7), now);

        Assert.Equal(new[] { "oldest", "boundary" }, due.Select(x => x.Id));
    }

    [Fact]
    public void GetTopicsDueForReviewTreatsUnspecifiedLegacyTimestampAsUtc()
    {
        var data = CreateData();
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "legacy",
            CourseId = "course-1",
            Status = SyllabusTopicStatus.Completed,
            LastStudiedAt = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Unspecified)
        });

        var due = _service.GetTopicsDueForReview(data, "course-1", TimeSpan.FromDays(7), now);

        Assert.Single(due);
        Assert.Equal("legacy", due[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void GetTopicsDueForReviewRejectsInvalidIntervals(int days)
    {
        var data = CreateData();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.GetTopicsDueForReview(data, "course-1", TimeSpan.FromDays(days)));
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
