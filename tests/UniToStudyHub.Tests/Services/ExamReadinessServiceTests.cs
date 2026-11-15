using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ExamReadinessServiceTests
{
    private static readonly DateTime ReferenceDay = new(2026, 11, 15);

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 },
            new() { Id = "fisica", Name = "Fisica", Cfu = 9 }
        }
    };

    private static ExamReadinessService Service() =>
        new(new CareerApplicationService(new CareerCalculatorService()));

    private static DateTimeOffset At(int daysFromReference, int hour = 12) =>
        new DateTimeOffset(ReferenceDay.Year, ReferenceDay.Month, ReferenceDay.Day, hour, 0, 0, TimeSpan.FromHours(1))
            .AddDays(daysFromReference);

    private static FlashcardData AddFlashcard(
        StudyData data,
        string id,
        string courseId = "analisi",
        bool suspended = false)
    {
        var card = new FlashcardData
        {
            Id = id,
            CourseId = courseId,
            Front = $"Domanda {id}",
            Back = $"Risposta {id}",
            IsSuspended = suspended
        };
        data.Flashcards.Add(card);
        return card;
    }

    private static void AddReview(
        StudyData data,
        string flashcardId,
        RecallRating rating,
        int daysFromReference = 0)
    {
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = Guid.NewGuid().ToString("N"),
            FlashcardId = flashcardId,
            Rating = rating,
            ReviewedAt = At(daysFromReference)
        });
    }

    private static void AddError(
        StudyData data,
        string id,
        ErrorReviewStatus status,
        int daysFromReference = 0,
        string courseId = "analisi",
        int? updatedDaysFromReference = null)
    {
        data.ErrorJournalEntries.Add(new ErrorJournalEntryData
        {
            Id = id,
            CourseId = courseId,
            Question = $"Domanda {id}",
            CorrectAnswer = "Risposta corretta",
            Status = status,
            CreatedAt = At(daysFromReference),
            UpdatedAt = At(updatedDaysFromReference ?? daysFromReference)
        });
    }

    private static void AddHalfCompleteSyllabus(StudyData data)
    {
        var syllabus = new SyllabusTrackerService();
        var completed = syllabus.Add(data, "analisi", "Limiti", estimatedMinutes: 60);
        syllabus.Add(data, "analisi", "Derivate", estimatedMinutes: 90);
        syllabus.UpdateStatus(data, completed.Id, SyllabusTopicStatus.Completed, ReferenceDay);
    }

    [Fact]
    public void NoEvidenceReturnsNullAndReportsAllMissingComponents()
    {
        var result = Service().Calculate(Data(), "analisi", ReferenceDay);

        Assert.Null(result.Score);
        Assert.False(result.IsComplete);
        Assert.Equal(3, result.Components.Count);
        Assert.All(result.Components, component => Assert.False(component.HasData));
        Assert.Equal(3, result.MissingData.Count);
    }

    [Fact]
    public void SyllabusOnlyProducesAnExplicitPartialScore()
    {
        var data = Data();
        var syllabus = new SyllabusTrackerService();
        var topic = syllabus.Add(data, "analisi", "Limiti");
        syllabus.UpdateStatus(data, topic.Id, SyllabusTopicStatus.Completed, ReferenceDay);

        var result = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.Equal(100m, result.Score);
        Assert.False(result.IsComplete);
        Assert.Equal(0.50m, result.Components.Single(x => x.Key == "syllabus").Weight);
        Assert.Contains(result.MissingData, x => x.Contains("Flashcard", StringComparison.Ordinal));
        Assert.Contains(result.MissingData, x => x.Contains("Diario errori", StringComparison.Ordinal));
    }

    [Fact]
    public void FlashcardRecallMapsRatingsAndScopesReviewsToActiveCourseCards()
    {
        var data = Data();
        AddFlashcard(data, "active");
        AddFlashcard(data, "other-course", "fisica");
        AddFlashcard(data, "suspended", suspended: true);

        AddReview(data, "active", RecallRating.Again);
        AddReview(data, "active", RecallRating.Hard, -1);
        AddReview(data, "active", RecallRating.Good, -2);
        AddReview(data, "active", RecallRating.Easy, -3);
        AddReview(data, "other-course", RecallRating.Easy);
        AddReview(data, "suspended", RecallRating.Easy);
        AddReview(data, "active", RecallRating.Easy, -30);
        AddReview(data, "active", RecallRating.Easy, 1);

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "flashcard-recall");

        Assert.True(component.HasData);
        Assert.Equal(4, component.SampleCount);
        Assert.Equal(57.5m, component.Score);
        Assert.Equal(57.5m, result.Score);
    }

    [Fact]
    public void FlashcardRecallRequiresThreeValidRecentReviews()
    {
        var data = Data();
        AddFlashcard(data, "active");
        AddReview(data, "active", RecallRating.Good);
        AddReview(data, "active", RecallRating.Easy, -1);

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "flashcard-recall");

        Assert.False(component.HasData);
        Assert.Equal(2, component.SampleCount);
        Assert.Null(result.Score);
        Assert.Contains(result.MissingData, x => x.Contains("2/3", StringComparison.Ordinal));
    }

    [Fact]
    public void FlashcardReviewAtTheFirstDayOfTheThirtyDayWindowIsIncluded()
    {
        var data = Data();
        AddFlashcard(data, "active");
        AddReview(data, "active", RecallRating.Easy, -29);
        AddReview(data, "active", RecallRating.Easy, -10);
        AddReview(data, "active", RecallRating.Easy);

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "flashcard-recall");

        Assert.True(component.HasData);
        Assert.Equal(3, component.SampleCount);
        Assert.Equal(100m, component.Score);
    }

    [Fact]
    public void ErrorReviewScoreMapsOpenReviewingAndResolvedStates()
    {
        var data = Data();
        AddError(data, "open", ErrorReviewStatus.Open);
        AddError(data, "reviewing", ErrorReviewStatus.Reviewing, -1);
        AddError(data, "resolved", ErrorReviewStatus.Resolved, -2);

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "error-review");

        Assert.True(component.HasData);
        Assert.Equal(3, component.SampleCount);
        Assert.Equal(50m, component.Score);
        Assert.Equal(50m, result.Score);
    }

    [Fact]
    public void ErrorReviewEvidenceIsCourseScopedAndExcludesOldAndFutureEntries()
    {
        var data = Data();
        AddError(data, "one", ErrorReviewStatus.Resolved);
        AddError(data, "two", ErrorReviewStatus.Resolved, -1);
        AddError(data, "old", ErrorReviewStatus.Resolved, -30);
        AddError(data, "future", ErrorReviewStatus.Resolved, 1);
        AddError(data, "other", ErrorReviewStatus.Resolved, 0, "fisica");

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "error-review");

        Assert.False(component.HasData);
        Assert.Equal(2, component.SampleCount);
        Assert.Null(result.Score);
        Assert.Contains(result.MissingData, x => x.Contains("2/3", StringComparison.Ordinal));
    }

    [Fact]
    public void RecentlyReviewedOldErrorCountsAsCurrentEvidence()
    {
        var data = Data();
        AddError(data, "old-reviewed", ErrorReviewStatus.Resolved, -60, updatedDaysFromReference: -1);
        AddError(data, "recent-one", ErrorReviewStatus.Resolved, -2);
        AddError(data, "recent-two", ErrorReviewStatus.Resolved);

        var result = Service().Calculate(data, "analisi", ReferenceDay);
        var component = result.Components.Single(x => x.Key == "error-review");

        Assert.True(component.HasData);
        Assert.Equal(3, component.SampleCount);
        Assert.Equal(100m, component.Score);
    }

    [Fact]
    public void CombinedScoreUsesTheDocumentedWeights()
    {
        var data = Data();
        AddHalfCompleteSyllabus(data);
        AddFlashcard(data, "active");
        AddReview(data, "active", RecallRating.Good);
        AddReview(data, "active", RecallRating.Good, -1);
        AddReview(data, "active", RecallRating.Good, -2);
        AddError(data, "open", ErrorReviewStatus.Open);
        AddError(data, "reviewing", ErrorReviewStatus.Reviewing, -1);
        AddError(data, "resolved", ErrorReviewStatus.Resolved, -2);

        var result = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.True(result.IsComplete);
        Assert.Equal(60.5m, result.Score);
        Assert.Equal(0.50m, result.Components.Single(x => x.Key == "syllabus").Weight);
        Assert.Equal(0.35m, result.Components.Single(x => x.Key == "flashcard-recall").Weight);
        Assert.Equal(0.15m, result.Components.Single(x => x.Key == "error-review").Weight);
    }

    [Fact]
    public void MissingErrorEvidenceIsExcludedAndAvailableWeightsAreRenormalized()
    {
        var data = Data();
        AddHalfCompleteSyllabus(data);
        AddFlashcard(data, "active");
        AddReview(data, "active", RecallRating.Good);
        AddReview(data, "active", RecallRating.Good, -1);
        AddReview(data, "active", RecallRating.Good, -2);
        AddError(data, "one", ErrorReviewStatus.Open);
        AddError(data, "two", ErrorReviewStatus.Resolved, -1);

        var result = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.False(result.IsComplete);
        Assert.Equal(62.35m, result.Score);
        Assert.Contains(result.MissingData, x => x.Contains("Diario errori", StringComparison.Ordinal));
    }

    [Fact]
    public void OfficialGradeIsContextOnlyAndDoesNotChangeTheReadinessScore()
    {
        var lowGradeData = Data();
        var highGradeData = Data();
        AddHalfCompleteSyllabus(lowGradeData);
        AddHalfCompleteSyllabus(highGradeData);
        AddFlashcard(lowGradeData, "active");
        AddFlashcard(highGradeData, "active");
        foreach (var data in new[] { lowGradeData, highGradeData })
        {
            AddReview(data, "active", RecallRating.Good);
            AddReview(data, "active", RecallRating.Good, -1);
            AddReview(data, "active", RecallRating.Good, -2);
        }

        var career = new CareerApplicationService(new CareerCalculatorService());
        career.RecordResult(lowGradeData, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            18, false, false, At(-100), At(-99));
        career.RecordResult(highGradeData, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            30, false, false, At(-100), At(-99));

        var service = Service();
        var low = service.Calculate(lowGradeData, "analisi", ReferenceDay);
        var high = service.Calculate(highGradeData, "analisi", ReferenceDay);

        Assert.Equal(low.Score, high.Score);
        Assert.NotNull(low.OfficialResultContext);
        Assert.NotNull(high.OfficialResultContext);
        Assert.Contains("18", low.OfficialResultContext!, StringComparison.Ordinal);
        Assert.Contains("30", high.OfficialResultContext!, StringComparison.Ordinal);
        Assert.Contains("escluso dal punteggio", low.OfficialResultContext!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnrecordedOrInconsistentAttemptIsNotPresentedAsAnOfficialResult()
    {
        var data = Data();
        data.CareerExams.Add(new CareerExamData
        {
            CourseId = "analisi",
            Name = "Analisi",
            Cfu = 6,
            Attempts = new ObservableCollection<ExamAttemptData>
            {
                new()
                {
                    AttemptedAt = At(-10),
                    Status = "Completed",
                    Outcome = "Passed",
                    GradeDecision = "Accepted",
                    NumericGrade = 30,
                    RecordedAt = null
                },
                new()
                {
                    AttemptedAt = At(-2),
                    Status = "Completed",
                    Outcome = "Passed",
                    GradeDecision = "NotApplicable",
                    NumericGrade = 30,
                    RecordedAt = At(-1)
                }
            }
        });

        var result = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.Null(result.OfficialResultContext);
        Assert.Null(result.Score);
    }

    [Fact]
    public void InvalidPersistedEnumValuesAreIgnoredAsEvidence()
    {
        var data = Data();
        AddFlashcard(data, "active");
        for (var i = 0; i < 3; i++)
        {
            AddReview(data, "active", (RecallRating)999, -i);
            AddError(data, $"invalid-{i}", (ErrorReviewStatus)999, -i);
        }

        var result = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.Null(result.Score);
        Assert.All(result.Components, component => Assert.False(component.HasData));
    }

    [Fact]
    public void CalculationDoesNotMutateStudyData()
    {
        var data = Data();
        AddHalfCompleteSyllabus(data);
        AddFlashcard(data, "active");
        AddReview(data, "active", RecallRating.Good);
        AddReview(data, "active", RecallRating.Easy, -1);
        AddReview(data, "active", RecallRating.Hard, -2);
        AddError(data, "one", ErrorReviewStatus.Open);
        AddError(data, "two", ErrorReviewStatus.Reviewing, -1);
        AddError(data, "three", ErrorReviewStatus.Resolved, -2);
        var courseCount = data.Courses.Count;
        var topicStatuses = data.SyllabusTopics.Select(x => x.Status).ToArray();
        var reviewCount = data.FlashcardReviews.Count;
        var errorStatuses = data.ErrorJournalEntries.Select(x => x.Status).ToArray();

        _ = Service().Calculate(data, "analisi", ReferenceDay);

        Assert.Equal(courseCount, data.Courses.Count);
        Assert.Equal(topicStatuses, data.SyllabusTopics.Select(x => x.Status));
        Assert.Equal(reviewCount, data.FlashcardReviews.Count);
        Assert.Equal(errorStatuses, data.ErrorJournalEntries.Select(x => x.Status));
    }

    [Fact]
    public void UnknownCourseIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Service().Calculate(Data(), "missing", ReferenceDay));
    }

    [Fact]
    public void BlankCourseIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Service().Calculate(Data(), " ", ReferenceDay));
    }

    [Fact]
    public void NullStudyDataIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Service().Calculate(null!, "analisi", ReferenceDay));
    }
}
