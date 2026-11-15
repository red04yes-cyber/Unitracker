using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record ReadinessComponent(
    string Key,
    string Label,
    decimal Score,
    decimal Weight,
    bool HasData,
    string Explanation)
{
    public int SampleCount { get; init; }
}

public sealed record ExamReadinessResult(
    string CourseId,
    string CourseName,
    decimal? Score,
    bool IsComplete,
    IReadOnlyList<ReadinessComponent> Components,
    IReadOnlyList<string> MissingData,
    string Formula,
    SyllabusCoverage Coverage)
{
    public string EvidenceSummary { get; init; } = string.Empty;
    public string? OfficialResultContext { get; init; }
}

/// <summary>
/// Transparent, non-AI readiness estimate. It uses syllabus coverage, recent
/// flashcard recall and the review state of recorded errors. An official grade
/// is exposed as context only: it is not evidence of current preparation.
/// Missing samples are excluded from the weighted score and reported explicitly.
/// </summary>
public sealed class ExamReadinessService(CareerApplicationService career)
{
    public const int EvidenceWindowDays = 30;
    public const int MinimumFlashcardReviews = 3;
    public const int MinimumErrorEntries = 3;

    public const decimal SyllabusWeight = 0.50m;
    public const decimal FlashcardRecallWeight = 0.35m;
    public const decimal ErrorReviewWeight = 0.15m;

    private readonly CareerApplicationService _career =
        career ?? throw new ArgumentNullException(nameof(career));

    public ExamReadinessResult Calculate(StudyData data, string courseId, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(courseId))
            throw new ArgumentException("Course id is required.", nameof(courseId));

        var course = data.Courses.FirstOrDefault(x =>
            string.Equals(x.Id, courseId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));

        var referenceDay = DateOnly.FromDateTime((today ?? DateTime.Today).Date);
        var windowStart = referenceDay.AddDays(-(EvidenceWindowDays - 1));
        var coverage = new SyllabusTrackerService().GetCoverage(data, courseId);
        var components = new List<ReadinessComponent>(3);
        var missing = new List<string>();

        components.Add(CreateSyllabusComponent(coverage, missing));

        var activeFlashcardIds = (data.Flashcards ?? new())
            .Where(x => x is not null &&
                        !x.IsSuspended &&
                        string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        var reviews = (data.FlashcardReviews ?? new())
            .Where(x => x is not null &&
                        activeFlashcardIds.Contains(x.FlashcardId) &&
                        Enum.IsDefined(x.Rating) &&
                        IsWithinWindow(x.ReviewedAt, windowStart, referenceDay))
            .ToList();

        components.Add(CreateFlashcardComponent(reviews, missing));

        var errorEntries = (data.ErrorJournalEntries ?? new())
            .Where(x => x is not null &&
                        string.Equals(x.CourseId, courseId, StringComparison.Ordinal) &&
                        Enum.IsDefined(x.Status) &&
                        IsWithinWindow(x.UpdatedAt, windowStart, referenceDay))
            .ToList();

        components.Add(CreateErrorComponent(errorEntries, missing));

        var available = components
            .Where(x => x.HasData && x.Weight > 0m)
            .ToList();
        decimal? score = available.Count == 0
            ? null
            : Math.Round(
                available.Sum(x => x.Score * x.Weight) / available.Sum(x => x.Weight),
                2,
                MidpointRounding.AwayFromZero);

        var officialResult = GetLatestOfficialResultContext(data, courseId);
        var evidenceSummary =
            $"{coverage.TotalTopics} argomenti syllabus · {reviews.Count} revisioni flashcard · " +
            $"{errorEntries.Count} errori aggiornati negli ultimi {EvidenceWindowDays} giorni";

        return new ExamReadinessResult(
            course.Id,
            course.Name,
            score,
            components.All(x => x.HasData),
            components,
            missing,
            $"Media pesata dei soli dati disponibili: syllabus {SyllabusWeight:P0}, " +
            $"richiamo flashcard {FlashcardRecallWeight:P0}, revisione errori {ErrorReviewWeight:P0}. " +
            $"Finestra mobile di {EvidenceWindowDays} giorni; servono almeno " +
            $"{MinimumFlashcardReviews} revisioni flashcard e {MinimumErrorEntries} voci del diario aggiornate. " +
            "I campioni insufficienti sono esclusi e segnalati; l'esito ufficiale è solo contesto.",
            coverage)
        {
            EvidenceSummary = evidenceSummary,
            OfficialResultContext = officialResult
        };
    }

    private static ReadinessComponent CreateSyllabusComponent(
        SyllabusCoverage coverage,
        ICollection<string> missing)
    {
        var hasData = coverage.TotalTopics > 0;
        if (!hasData)
            missing.Add("Syllabus: nessun argomento registrato.");

        return new ReadinessComponent(
            "syllabus",
            "Copertura del programma",
            hasData ? coverage.CompletedPercent : 0m,
            SyllabusWeight,
            hasData,
            hasData
                ? $"{coverage.CompletedTopics}/{coverage.TotalTopics} argomenti completati."
                : "Nessun argomento del syllabus è stato registrato.")
        {
            SampleCount = coverage.TotalTopics
        };
    }

    private static ReadinessComponent CreateFlashcardComponent(
        IReadOnlyCollection<FlashcardReviewData> reviews,
        ICollection<string> missing)
    {
        var hasEnoughData = reviews.Count >= MinimumFlashcardReviews;
        if (!hasEnoughData)
            missing.Add(
                $"Flashcard: {reviews.Count}/{MinimumFlashcardReviews} revisioni valide negli ultimi {EvidenceWindowDays} giorni.");

        var score = hasEnoughData
            ? Math.Round(reviews.Average(x => RecallScore(x.Rating)), 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ReadinessComponent(
            "flashcard-recall",
            "Richiamo attivo (flashcard)",
            score,
            FlashcardRecallWeight,
            hasEnoughData,
            hasEnoughData
                ? $"{reviews.Count} revisioni recenti; media delle autovalutazioni: {score:0.##}/100."
                : "Servono almeno tre revisioni recenti su flashcard attive della materia.")
        {
            SampleCount = reviews.Count
        };
    }

    private static ReadinessComponent CreateErrorComponent(
        IReadOnlyCollection<ErrorJournalEntryData> entries,
        ICollection<string> missing)
    {
        var hasEnoughData = entries.Count >= MinimumErrorEntries;
        if (!hasEnoughData)
            missing.Add(
                $"Diario errori: {entries.Count}/{MinimumErrorEntries} voci aggiornate negli ultimi {EvidenceWindowDays} giorni.");

        var score = hasEnoughData
            ? Math.Round(entries.Average(x => ErrorReviewScore(x.Status)), 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ReadinessComponent(
            "error-review",
            "Revisione degli errori registrati",
            score,
            ErrorReviewWeight,
            hasEnoughData,
            hasEnoughData
                ? $"{entries.Count} errori recenti; aperto 0, in revisione 50, risolto 100."
                : "Servono almeno tre voci del diario aggiornate di recente per valutarne lo stato di revisione.")
        {
            SampleCount = entries.Count
        };
    }

    private string? GetLatestOfficialResultContext(StudyData data, string courseId)
    {
        var official = _career.GetAttempts(data, courseId)
            .Where(x => string.Equals(x.Status, AttemptStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
            .Where(x => x.RecordedAt is not null)
            .Where(x =>
                (string.Equals(x.Outcome, ExamOutcome.Passed.ToString(), StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(x.GradeDecision, GradeDecision.Accepted.ToString(), StringComparison.OrdinalIgnoreCase) &&
                 !x.IsPassFail &&
                 x.NumericGrade is not null) ||
                (string.Equals(x.Outcome, ExamOutcome.EligibilityPassed.ToString(), StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(x.GradeDecision, GradeDecision.NotApplicable.ToString(), StringComparison.OrdinalIgnoreCase) &&
                 x.IsPassFail &&
                 x.NumericGrade is null))
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.AttemptedAt)
            .FirstOrDefault();

        if (official is null)
            return null;
        if (official.IsPassFail)
            return "Idoneità ufficiale registrata (solo contesto, esclusa dal punteggio).";
        if (official.NumericGrade is not int grade)
            return "Ultimo esito ufficiale registrato (solo contesto, escluso dal punteggio).";

        return $"Ultimo voto ufficiale: {grade}{(official.IsHonors ? " e lode" : string.Empty)} " +
               "(solo contesto, escluso dal punteggio).";
    }

    private static decimal RecallScore(RecallRating rating) => rating switch
    {
        RecallRating.Again => 0m,
        RecallRating.Hard => 50m,
        RecallRating.Good => 80m,
        RecallRating.Easy => 100m,
        _ => throw new ArgumentOutOfRangeException(nameof(rating))
    };

    private static decimal ErrorReviewScore(ErrorReviewStatus status) => status switch
    {
        ErrorReviewStatus.Open => 0m,
        ErrorReviewStatus.Reviewing => 50m,
        ErrorReviewStatus.Resolved => 100m,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static bool IsWithinWindow(DateTimeOffset value, DateOnly start, DateOnly end)
    {
        var localCalendarDay = DateOnly.FromDateTime(value.Date);
        return localCalendarDay >= start && localCalendarDay <= end;
    }
}
