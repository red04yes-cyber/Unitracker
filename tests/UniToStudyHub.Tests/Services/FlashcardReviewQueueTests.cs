using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class FlashcardReviewQueueTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(1));

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 },
            new() { Id = "fisica", Name = "Fisica", Cfu = 9 }
        },
        SyllabusTopics = new ObservableCollection<SyllabusTopicData>
        {
            new() { Id = "limiti", CourseId = "analisi", Title = "Limiti" },
            new() { Id = "meccanica", CourseId = "fisica", Title = "Meccanica" }
        }
    };

    private static FlashcardData Card(
        string id,
        DateTimeOffset? dueAt = null,
        string courseId = "analisi") => new()
    {
        Id = id,
        CourseId = courseId,
        Front = $"Front {id}",
        Back = $"Back {id}",
        DueAt = dueAt ?? Now,
        CreatedAt = Now.AddDays(-30),
        UpdatedAt = Now.AddDays(-1)
    };

    [Fact]
    public void AddRejectsSyllabusTopicFromAnotherCourse()
    {
        var data = Data();
        var card = Card("card-1");
        card.SyllabusTopicId = "meccanica";

        Assert.Throws<ArgumentException>(() => new FlashcardService().Add(data, card, Now));
        Assert.Empty(data.Flashcards);
    }

    [Fact]
    public void AddRejectsTopicWithoutCourseAttribution()
    {
        var data = Data();
        var card = Card("card-1");
        card.CourseId = string.Empty;
        card.SyllabusTopicId = "limiti";

        Assert.Throws<ArgumentException>(() => new FlashcardService().Add(data, card, Now));
        Assert.Empty(data.Flashcards);
    }

    [Fact]
    public void AddRejectsMissingCourseAndMissingTopic()
    {
        var service = new FlashcardService();
        var missingCourse = Card("card-1");
        missingCourse.CourseId = "missing";
        Assert.Throws<ArgumentException>(() => service.Add(Data(), missingCourse, Now));

        var missingTopic = Card("card-2");
        missingTopic.SyllabusTopicId = "missing";
        Assert.Throws<ArgumentException>(() => service.Add(Data(), missingTopic, Now));
    }

    [Fact]
    public void AddNormalizesImportedSchedulingValuesAndUsesProvidedClock()
    {
        var data = Data();
        var card = Card("card-1");
        card.CreatedAt = default;
        card.DueAt = null;
        card.EaseFactor = 3.4m;
        card.IntervalDays = int.MaxValue;
        card.Repetitions = -5;
        card.Lapses = -2;

        new FlashcardService().Add(data, card, Now);

        Assert.Equal(2.5m, card.EaseFactor);
        Assert.Equal(FlashcardService.MaximumIntervalDays, card.IntervalDays);
        Assert.Equal(0, card.Repetitions);
        Assert.Equal(0, card.Lapses);
        Assert.Equal(Now, card.DueAt);
        Assert.Equal(Now, card.CreatedAt);
        Assert.Equal(Now, card.UpdatedAt);
    }

    [Fact]
    public void AddRejectsDuplicateIds()
    {
        var data = Data();
        var service = new FlashcardService();
        service.Add(data, Card("same"), Now);

        Assert.Throws<ArgumentException>(() => service.Add(data, Card("same"), Now));
        Assert.Single(data.Flashcards);
    }

    [Fact]
    public void UpdateChangesContentAndAttributionWithoutReplacingScheduleOrHistory()
    {
        var data = Data();
        var service = new FlashcardService();
        var existing = Card("card-1", Now.AddDays(12));
        existing.SyllabusTopicId = "limiti";
        existing.EaseFactor = 2.2m;
        existing.IntervalDays = 12;
        existing.Repetitions = 5;
        existing.Lapses = 1;
        existing.LastReviewedAt = Now.AddDays(-1);
        service.Add(data, existing, Now.AddDays(-20));
        var createdAt = existing.CreatedAt;
        var dueAt = existing.DueAt;
        var lastReviewedAt = existing.LastReviewedAt;
        var history = new FlashcardReviewData
        {
            Id = "review-1",
            FlashcardId = existing.Id,
            ReviewedAt = Now.AddDays(-1),
            Rating = RecallRating.Good,
            PreviousIntervalDays = 6,
            NewIntervalDays = 12,
            EaseFactorAfter = 2.2m
        };
        data.FlashcardReviews.Add(history);

        service.Update(data, new FlashcardData
        {
            Id = existing.Id,
            CourseId = "fisica",
            SyllabusTopicId = "meccanica",
            Front = "  Nuova domanda  ",
            Back = "  Nuova risposta  ",
            Source = "  Dispense  "
        }, Now);

        Assert.Equal("Nuova domanda", existing.Front);
        Assert.Equal("Nuova risposta", existing.Back);
        Assert.Equal("Dispense", existing.Source);
        Assert.Equal("fisica", existing.CourseId);
        Assert.Equal("meccanica", existing.SyllabusTopicId);
        Assert.Equal(createdAt, existing.CreatedAt);
        Assert.Equal(dueAt, existing.DueAt);
        Assert.Equal(lastReviewedAt, existing.LastReviewedAt);
        Assert.Equal(2.2m, existing.EaseFactor);
        Assert.Equal(12, existing.IntervalDays);
        Assert.Equal(5, existing.Repetitions);
        Assert.Equal(1, existing.Lapses);
        Assert.Same(history, Assert.Single(data.FlashcardReviews));
        Assert.Equal(Now, existing.UpdatedAt);
    }

    [Fact]
    public void UpdateRejectsCrossCourseTopicWithoutChangingExistingCard()
    {
        var data = Data();
        var service = new FlashcardService();
        var existing = service.Add(data, Card("card-1"), Now);

        Assert.Throws<ArgumentException>(() => service.Update(data, new FlashcardData
        {
            Id = existing.Id,
            CourseId = "analisi",
            SyllabusTopicId = "meccanica",
            Front = "Changed",
            Back = "Changed"
        }, Now.AddMinutes(1)));

        Assert.Equal("Front card-1", existing.Front);
        Assert.Equal("analisi", existing.CourseId);
    }

    [Fact]
    public void SetSuspendedCanPauseAndResumeReviewsWithoutChangingDueDate()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = service.Add(data, Card("card-1", Now.AddDays(-1)), Now);
        var dueAt = card.DueAt;

        service.SetSuspended(data, card.Id, true, Now.AddMinutes(1));
        Assert.True(card.IsSuspended);
        Assert.Empty(service.GetDue(data, Now.AddDays(1)));

        service.SetSuspended(data, card.Id, false, Now.AddMinutes(2));
        Assert.False(card.IsSuspended);
        Assert.Equal(dueAt, card.DueAt);
        Assert.Single(service.GetDue(data, Now.AddDays(1)));
    }

    [Fact]
    public void SetSuspendedIsIdempotent()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = service.Add(data, Card("card-1"), Now);
        var updatedAt = card.UpdatedAt;

        service.SetSuspended(data, card.Id, false, Now.AddDays(1));

        Assert.Equal(updatedAt, card.UpdatedAt);
    }

    [Fact]
    public void ResetScheduleMakesCardDueNowAndPreservesReviewHistory()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1", Now.AddDays(30));
        card.EaseFactor = 2.7m;
        card.IntervalDays = 30;
        card.Repetitions = 8;
        card.Lapses = 2;
        card.LastReviewedAt = Now.AddDays(-5);
        service.Add(data, card, Now.AddDays(-40));
        var history = new FlashcardReviewData
        {
            Id = "review-1",
            FlashcardId = card.Id,
            ReviewedAt = Now.AddDays(-5),
            Rating = RecallRating.Easy,
            PreviousIntervalDays = 20,
            NewIntervalDays = 30,
            EaseFactorAfter = 2.7m
        };
        data.FlashcardReviews.Add(history);

        service.ResetSchedule(data, card.Id, Now);

        Assert.Equal(2.5m, card.EaseFactor);
        Assert.Equal(0, card.IntervalDays);
        Assert.Equal(0, card.Repetitions);
        Assert.Equal(0, card.Lapses);
        Assert.Equal(Now, card.DueAt);
        Assert.Null(card.LastReviewedAt);
        Assert.Same(history, Assert.Single(data.FlashcardReviews));
        Assert.Single(service.GetDue(data, Now));
    }

    [Fact]
    public void GetForCourseReturnsOnlyThatCoursesCardsInDueOrder()
    {
        var data = Data();
        var service = new FlashcardService();
        service.Add(data, Card("later", Now.AddDays(2)), Now);
        service.Add(data, Card("earlier", Now.AddDays(1)), Now);
        service.Add(data, Card("fisica", Now, "fisica"), Now);

        var result = service.GetForCourse(data, " analisi ");

        Assert.Equal(new[] { "earlier", "later" }, result.Select(x => x.Id));
    }

    [Fact]
    public void GetDueForCourseDoesNotLeakCardsFromOtherCourses()
    {
        var data = Data();
        var service = new FlashcardService();
        service.Add(data, Card("analisi", Now.AddDays(-1)), Now);
        service.Add(data, Card("fisica", Now.AddDays(-1), "fisica"), Now);

        var result = service.GetDueForCourse(data, "analisi", Now);

        Assert.Equal("analisi", Assert.Single(result).CourseId);
    }

    [Fact]
    public void GetReviewHistoryReturnsChronologicalOrder()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = service.Add(data, Card("card-1"), Now);
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-later",
            FlashcardId = card.Id,
            ReviewedAt = Now.AddDays(2),
            Rating = RecallRating.Good
        });
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-earlier",
            FlashcardId = card.Id,
            ReviewedAt = Now.AddDays(1),
            Rating = RecallRating.Hard
        });

        var result = service.GetReviewHistory(data, card.Id);

        Assert.Equal(new[] { "review-earlier", "review-later" }, result.Select(x => x.Id));
    }

    [Fact]
    public void ReviewQueuePartitionsDueUpcomingAndSuspendedCards()
    {
        var data = Data();
        var service = new FlashcardService();
        data.Flashcards.Add(Card("overdue", Now.AddDays(-2)));
        data.Flashcards.Add(Card("due-now", Now));
        data.Flashcards.Add(Card("later-today", Now.AddHours(2)));
        data.Flashcards.Add(Card("upcoming", Now.AddDays(3)));
        data.Flashcards.Add(Card("outside", Now.AddDays(8)));
        var suspended = Card("suspended", Now.AddDays(-1));
        suspended.IsSuspended = true;
        data.Flashcards.Add(suspended);
        var unscheduled = Card("unscheduled");
        unscheduled.DueAt = null;
        data.Flashcards.Add(unscheduled);

        var queue = service.GetReviewQueue(data, Now, upcomingDays: 7);

        Assert.Equal(new[] { "overdue", "due-now" }, queue.Due.Select(x => x.Id));
        Assert.Equal(new[] { "later-today", "upcoming" }, queue.Upcoming.Select(x => x.Id));
        Assert.Equal(2, queue.DueCount);
        Assert.Equal(2, queue.UpcomingCount);
        Assert.Equal(1, queue.OverdueCount);
        Assert.Equal(1, queue.SuspendedCount);
        Assert.Equal(1, queue.UnscheduledCount);
        Assert.Equal(6, queue.ActiveCount);
        Assert.Equal(Now, queue.AsOf);
        Assert.Equal(Now.ToUniversalTime().AddDays(7), queue.Through);
    }

    [Fact]
    public void ReviewQueueCanBeFilteredByCourse()
    {
        var data = Data();
        var service = new FlashcardService();
        data.Flashcards.Add(Card("analisi", Now.AddDays(-1)));
        data.Flashcards.Add(Card("fisica", Now.AddDays(-1), "fisica"));

        var queue = service.GetReviewQueue(data, Now, upcomingDays: 7, courseId: "analisi");

        Assert.Equal("analisi", Assert.Single(queue.Due).CourseId);
        Assert.Equal(1, queue.ActiveCount);
    }

    [Fact]
    public void ReviewQueueRejectsUnknownCourseAndOutOfRangeHorizon()
    {
        var service = new FlashcardService();
        var data = Data();

        Assert.Throws<ArgumentException>(() =>
            service.GetReviewQueue(data, Now, courseId: "missing"));
        Assert.Throws<ArgumentException>(() =>
            service.GetReviewQueue(data, Now, courseId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.GetReviewQueue(data, Now, upcomingDays: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.GetReviewQueue(data, Now, upcomingDays: 91));
    }

    [Fact]
    public void GoodRatingUsesOneDayThenSixDayIntervals()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = service.Add(data, Card("card-1"), Now);

        var first = service.Review(data, card.Id, RecallRating.Good, Now);
        var second = service.Review(data, card.Id, RecallRating.Good, Now.AddDays(1));

        Assert.Equal(1, first.NewIntervalDays);
        Assert.Equal(1, second.PreviousIntervalDays);
        Assert.Equal(6, second.NewIntervalDays);
        Assert.Equal(2, card.Repetitions);
        Assert.Equal(Now.AddDays(7), card.DueAt);
    }

    [Fact]
    public void HardRatingReducesEaseAndUsesBoundedRoundedInterval()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1");
        card.IntervalDays = 10;
        service.Add(data, card, Now);

        var review = service.Review(data, card.Id, RecallRating.Hard, Now);

        Assert.Equal(2.35m, review.EaseFactorAfter);
        Assert.Equal(12, review.NewIntervalDays);
        Assert.Equal(12, card.IntervalDays);
    }

    [Fact]
    public void EasyRatingIncreasesEaseAndUsesRoundedInterval()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1");
        card.IntervalDays = 10;
        service.Add(data, card, Now);

        var review = service.Review(data, card.Id, RecallRating.Easy, Now);

        Assert.Equal(2.65m, review.EaseFactorAfter);
        Assert.Equal(34, review.NewIntervalDays);
    }

    [Fact]
    public void AgainRatingResetsRepetitionsAndSaturatesLapses()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1");
        card.IntervalDays = 10;
        card.Repetitions = 7;
        card.Lapses = int.MaxValue;
        service.Add(data, card, Now);

        var review = service.Review(data, card.Id, RecallRating.Again, Now);

        Assert.Equal(0, card.Repetitions);
        Assert.Equal(int.MaxValue, card.Lapses);
        Assert.Equal(1, card.IntervalDays);
        Assert.Equal(1, review.NewIntervalDays);
        Assert.Equal(2.3m, card.EaseFactor);
    }

    [Fact]
    public void ReviewClampsLegacyIntervalAndSaturatesRepetitionCounter()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("legacy-card");
        card.IntervalDays = int.MaxValue;
        card.EaseFactor = 2.8m;
        card.Repetitions = int.MaxValue;
        data.Flashcards.Add(card);

        var review = service.Review(data, card.Id, RecallRating.Hard, Now);

        Assert.Equal(FlashcardService.MaximumIntervalDays, review.PreviousIntervalDays);
        Assert.Equal(FlashcardService.MaximumIntervalDays, review.NewIntervalDays);
        Assert.Equal(FlashcardService.MaximumIntervalDays, card.IntervalDays);
        Assert.Equal(int.MaxValue, card.Repetitions);
        Assert.Equal(2.65m, card.EaseFactor);
    }

    [Fact]
    public void ReviewRejectsDateOverflowWithoutPartiallyChangingCardOrHistory()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("near-max");
        card.CreatedAt = DateTimeOffset.MinValue;
        card.IntervalDays = 0;
        card.Repetitions = 4;
        card.Lapses = 2;
        card.EaseFactor = 2.4m;
        data.Flashcards.Add(card);
        var originalDueAt = card.DueAt;
        var at = DateTimeOffset.MaxValue.AddHours(-12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Review(data, card.Id, RecallRating.Good, at));

        Assert.Equal(2.4m, card.EaseFactor);
        Assert.Equal(0, card.IntervalDays);
        Assert.Equal(4, card.Repetitions);
        Assert.Equal(2, card.Lapses);
        Assert.Equal(originalDueAt, card.DueAt);
        Assert.Null(card.LastReviewedAt);
        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewRejectsClockRollbackWithoutMutation()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1");
        card.LastReviewedAt = Now;
        data.Flashcards.Add(card);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Review(data, card.Id, RecallRating.Good, Now.AddSeconds(-1)));

        Assert.Equal(0, card.IntervalDays);
        Assert.Equal(0, card.Repetitions);
        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewRejectsSuspendedCardsAndUnknownRatings()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = Card("card-1");
        card.IsSuspended = true;
        data.Flashcards.Add(card);

        Assert.Throws<InvalidOperationException>(() =>
            service.Review(data, card.Id, RecallRating.Good, Now));
        card.IsSuspended = false;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Review(data, card.Id, (RecallRating)999, Now));

        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewRejectsMissingCardAndNullData()
    {
        var service = new FlashcardService();

        Assert.Throws<ArgumentNullException>(() => service.GetDue(null!));
        Assert.Throws<ArgumentNullException>(() => service.Review(null!, "card", RecallRating.Good));
        Assert.Throws<ArgumentException>(() => service.Review(Data(), "missing", RecallRating.Good));
    }
}
