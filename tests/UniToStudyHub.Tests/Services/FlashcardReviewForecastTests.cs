using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class FlashcardReviewForecastTests
{
    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 },
            new() { Id = "micro", Name = "Microeconomia", Cfu = 9 }
        }
    };

    private static FlashcardData AddCard(StudyData data, string id, string course, DateTimeOffset dueAt) =>
        new FlashcardService().Add(data, new FlashcardData
        {
            Id = id,
            CourseId = course,
            Front = "Domanda " + id,
            Back = "Risposta " + id,
            DueAt = dueAt
        });

    [Fact]
    public void AddNormalizesIntervalsAndReviewCountersToSupportedBounds()
    {
        var data = Data();
        var card = new FlashcardService().Add(data, new FlashcardData
        {
            Id = "bounded",
            CourseId = "analisi",
            Front = "Q",
            Back = "A",
            IntervalDays = int.MaxValue,
            Repetitions = int.MaxValue,
            Lapses = int.MaxValue
        });

        Assert.Equal(FlashcardService.MaximumIntervalDays, card.IntervalDays);
        Assert.Equal(1_000_000, card.Repetitions);
        Assert.Equal(1_000_000, card.Lapses);
    }

    [Fact]
    public void DueQueueCanBeFilteredByCourseAndCount()
    {
        var data = Data();
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        AddCard(data, "a", "analisi", now.AddDays(-1));
        AddCard(data, "b", "micro", now);
        AddCard(data, "c", "analisi", now.AddDays(1));
        var service = new FlashcardService();

        Assert.Equal(2, service.GetDue(data, now).Count);
        Assert.Single(service.GetDue(data, now, "analisi"));
        Assert.Equal(1, service.GetDueCount(data, now, "analisi"));
    }

    [Fact]
    public void UpdateChangesTextAndCourseWithoutResettingReviewState()
    {
        var data = Data();
        var card = AddCard(data, "card", "analisi", DateTimeOffset.UtcNow);
        card.IntervalDays = 6;
        card.Repetitions = 3;
        var at = new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero);

        var updated = new FlashcardService().Update(data, card.Id, "  Nuova domanda  ", " Nuova risposta ",
            "micro", "topic-2", " import ", at);

        Assert.Same(card, updated);
        Assert.Equal("Nuova domanda", card.Front);
        Assert.Equal("Nuova risposta", card.Back);
        Assert.Equal("micro", card.CourseId);
        Assert.Equal("topic-2", card.SyllabusTopicId);
        Assert.Equal("import", card.Source);
        Assert.Equal(6, card.IntervalDays);
        Assert.Equal(3, card.Repetitions);
        Assert.Equal(at, card.UpdatedAt);
    }

    [Fact]
    public void UpdatePreservesOptionalMetadataWhenItIsOmitted()
    {
        var data = Data();
        var card = AddCard(data, "card", "analisi", DateTimeOffset.UtcNow);
        card.SyllabusTopicId = "topic-1";
        card.Source = "imported";
        var service = new FlashcardService();

        service.Update(data, card.Id, "Question updated", "Answer updated");

        Assert.Equal("analisi", card.CourseId);
        Assert.Equal("topic-1", card.SyllabusTopicId);
        Assert.Equal("imported", card.Source);
    }

    [Fact]
    public void UpdateRejectsBlankTextAndUnknownCourseWithoutMutation()
    {
        var data = Data();
        var card = AddCard(data, "card", "analisi", DateTimeOffset.UtcNow);
        var beforeFront = card.Front;
        var service = new FlashcardService();

        Assert.Throws<ArgumentException>(() => service.Update(data, card.Id, " ", "answer", "micro"));
        Assert.Throws<ArgumentException>(() => service.Update(data, card.Id, "question", "answer", "missing"));
        Assert.Equal(beforeFront, card.Front);
        Assert.Equal("analisi", card.CourseId);
    }

    [Fact]
    public void SuspensionHidesDueCardAndBlocksReviewUntilResumed()
    {
        var data = Data();
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        var card = AddCard(data, "card", "analisi", now);
        var service = new FlashcardService();

        service.SetSuspended(data, card.Id, true, now.AddMinutes(1));
        Assert.Empty(service.GetDue(data, now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => service.Review(data, card.Id, RecallRating.Good, now.AddDays(1)));

        service.SetSuspended(data, card.Id, false, now.AddMinutes(2));
        Assert.Single(service.GetDue(data, now.AddDays(1)));
    }

    [Fact]
    public void RemoveRefusesCardsWithReviewHistoryToPreserveAuditRecords()
    {
        var data = Data();
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        var card = AddCard(data, "card", "analisi", now);
        var service = new FlashcardService();
        service.Review(data, card.Id, RecallRating.Good, now);

        Assert.Throws<InvalidOperationException>(() => service.Remove(data, card.Id));
        Assert.Contains(data.Flashcards, x => x.Id == card.Id);
        Assert.Single(data.FlashcardReviews);
    }

    [Fact]
    public void RemoveDeletesOnlyAnUnreviewedCard()
    {
        var data = Data();
        var card = AddCard(data, "card", "analisi", DateTimeOffset.UtcNow);

        new FlashcardService().Remove(data, card.Id);

        Assert.Empty(data.Flashcards);
        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ForecastSeparatesOverdueFromScheduledAndExcludesSuspendedCards()
    {
        var data = Data();
        AddCard(data, "overdue", "analisi", new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        AddCard(data, "today", "analisi", new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
        AddCard(data, "tomorrow", "analisi", new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero));
        var suspended = AddCard(data, "suspended", "analisi", new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        new FlashcardService().SetSuspended(data, suspended.Id, true);

        var forecast = new FlashcardService().GetReviewForecast(data, new DateOnly(2026, 11, 27), 3, TimeZoneInfo.Utc);

        Assert.Equal(3, forecast.Count);
        Assert.Equal(new FlashcardReviewForecastDay(new DateOnly(2026, 11, 27), 1, 1), forecast[0]);
        Assert.Equal(new FlashcardReviewForecastDay(new DateOnly(2026, 11, 28), 1, 0), forecast[1]);
        Assert.Equal(new FlashcardReviewForecastDay(new DateOnly(2026, 11, 29), 0, 0), forecast[2]);
        Assert.Equal(3, forecast.Sum(x => x.TotalReviewCount));
    }

    [Fact]
    public void ForecastUsesSpecifiedTimezoneAndOptionalCourseFilter()
    {
        var data = Data();
        AddCard(data, "utc-shift", "analisi", new DateTimeOffset(2026, 11, 28, 1, 0, 0, TimeSpan.FromHours(2)));
        AddCard(data, "other-course", "micro", new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

        var forecast = new FlashcardService().GetReviewForecast(data, new DateOnly(2026, 11, 27), 2,
            TimeZoneInfo.Utc, "analisi");

        Assert.Equal(1, forecast[0].DueCount);
        Assert.Equal(0, forecast[0].OverdueCount);
        Assert.Equal(0, forecast[1].DueCount);
    }

    [Fact]
    public void ForecastIsReadOnlyAndRejectsInvalidHorizon()
    {
        var data = Data();
        var due = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        var card = AddCard(data, "card", "analisi", due);
        var originalDue = card.DueAt;
        var originalRepetitions = card.Repetitions;
        var service = new FlashcardService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetReviewForecast(data, new DateOnly(2026, 11, 27), 91));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetReviewForecast(data, DateOnly.MaxValue, 2));
        var forecast = service.GetReviewForecast(data, new DateOnly(2026, 11, 27), 1);

        Assert.Single(forecast);
        Assert.Equal(originalDue, card.DueAt);
        Assert.Equal(originalRepetitions, card.Repetitions);
        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewClampsLegacySchedulerValuesAndSaturatesCounters()
    {
        var data = Data();
        var at = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        var card = new FlashcardData
        {
            Id = "legacy",
            CourseId = "analisi",
            Front = "Q",
            Back = "A",
            IntervalDays = int.MaxValue,
            Repetitions = int.MaxValue,
            Lapses = int.MaxValue,
            EaseFactor = 2.9m,
            LastReviewedAt = at.AddDays(-1)
        };
        data.Flashcards.Add(card);

        var review = new FlashcardService().Review(data, card.Id, RecallRating.Easy, at);

        Assert.Equal(FlashcardService.MaximumIntervalDays, review.NewIntervalDays);
        Assert.Equal(FlashcardService.MaximumIntervalDays, card.IntervalDays);
        Assert.Equal(1_000_000, card.Repetitions);
        Assert.Equal(1_000_000, card.Lapses);
        Assert.Equal(2.65m, card.EaseFactor);
        Assert.Single(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewDateOverflowDoesNotMutateCardOrHistory()
    {
        var data = Data();
        var at = DateTimeOffset.MaxValue.AddHours(-12);
        var card = AddCard(data, "overflow", "analisi", at.AddDays(-1));
        card.IntervalDays = 1;
        card.Repetitions = 4;
        card.Lapses = 2;
        var dueBefore = card.DueAt;
        var lastReviewedBefore = card.LastReviewedAt;
        var service = new FlashcardService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Review(data, card.Id, RecallRating.Good, at));

        Assert.Equal(1, card.IntervalDays);
        Assert.Equal(4, card.Repetitions);
        Assert.Equal(2, card.Lapses);
        Assert.Equal(dueBefore, card.DueAt);
        Assert.Equal(lastReviewedBefore, card.LastReviewedAt);
        Assert.Empty(data.FlashcardReviews);
    }

    [Fact]
    public void ReviewRejectsClockRollbackWithoutMutation()
    {
        var data = Data();
        var previous = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        var card = AddCard(data, "rollback", "analisi", previous);
        card.LastReviewedAt = previous;
        card.IntervalDays = 6;
        card.Repetitions = 5;
        var service = new FlashcardService();

        Assert.Throws<InvalidOperationException>(() => service.Review(data, card.Id, RecallRating.Good, previous.AddMinutes(-1)));

        Assert.Equal(6, card.IntervalDays);
        Assert.Equal(5, card.Repetitions);
        Assert.Equal(previous, card.LastReviewedAt);
        Assert.Empty(data.FlashcardReviews);
    }
}
