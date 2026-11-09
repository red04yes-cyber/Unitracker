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
    string Explanation);

public sealed record ExamReadinessResult(
    string CourseId,
    string CourseName,
    decimal? Score,
    bool IsComplete,
    IReadOnlyList<ReadinessComponent> Components,
    IReadOnlyList<string> MissingData,
    string Formula,
    SyllabusCoverage Coverage);

/// <summary>
/// Aggregate view of exam preparation across active courses. Aggregate scores
/// use only available course scores and remain indicative, never official grades.
/// </summary>
public sealed record ExamReadinessOverview(
    int TotalCourses,
    int CoursesWithScore,
    int CoursesWithoutScore,
    int FullyAssessedCourses,
    int EstimatedRemainingSyllabusMinutes,
    decimal? AverageAvailableScore,
    decimal? CfuWeightedAvailableScore,
    IReadOnlyList<ExamReadinessResult> Courses);

/// <summary>
/// Transparent readiness estimate. It only uses data actually stored in the
/// workspace. Missing components are reported instead of being replaced with
/// invented values. This is an organizational estimate, not an official grade.
/// </summary>
public sealed class ExamReadinessService(CareerApplicationService career)
{
    public const decimal SyllabusWeight = 0.60m;
    public const decimal PerformanceWeight = 0.40m;

    private readonly CareerApplicationService _career = career ?? throw new ArgumentNullException(nameof(career));

    public ExamReadinessResult Calculate(StudyData data, string courseId, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var course = data.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
        var coverage = new SyllabusTrackerService().GetCoverage(data, courseId);
        var components = new List<ReadinessComponent>();
        var missing = new List<string>();

        if (coverage.TotalTopics > 0)
        {
            components.Add(new ReadinessComponent(
                "syllabus",
                "Copertura del programma",
                coverage.CompletedPercent,
                SyllabusWeight,
                true,
                $"{coverage.CompletedTopics}/{coverage.TotalTopics} argomenti completati."));
        }
        else
        {
            missing.Add("Syllabus: nessun argomento registrato.");
            components.Add(new ReadinessComponent(
                "syllabus",
                "Copertura del programma",
                0m,
                SyllabusWeight,
                false,
                "Nessun argomento del syllabus è stato registrato."));
        }

        var attempts = CareerDataProjection.ToDomainExams(data.CareerExams)
            .FirstOrDefault(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            ?.Attempts
            .Where(x => x.IsOfficialResult)
            .ToList() ?? new();
        var official = attempts
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.AttemptedAt)
            .FirstOrDefault();
        if (official?.Grade is { } grade)
        {
            var performance = grade.IsPassFail
                ? 100m
                : Math.Clamp(((grade.NumericValue!.Value - 18m) / 12m) * 100m, 0m, 100m);
            components.Add(new ReadinessComponent(
                "performance",
                "Ultimo risultato ufficiale",
                Math.Round(performance, 2, MidpointRounding.AwayFromZero),
                PerformanceWeight,
                true,
                grade.IsPassFail
                    ? "Idoneità registrata; il risultato non entra nella media numerica."
                    : $"Voto {grade.NumericValue}{(grade.IsHonors ? " e lode" : string.Empty)} normalizzato sulla scala 18–30."));
        }
        else
        {
            missing.Add("Risultati: nessun esito ufficiale registrato.");
            components.Add(new ReadinessComponent(
                "performance",
                "Ultimo risultato ufficiale",
                0m,
                PerformanceWeight,
                false,
                "Non esiste ancora un esito ufficiale per questa materia."));
        }

        // A score is returned only when at least one component has real data.
        // Partial scores are explicitly marked incomplete; they are not official.
        var available = components.Where(x => x.HasData).ToList();
        decimal? score = available.Count == 0
            ? null
            : Math.Round(
                available.Sum(x => x.Score * x.Weight) / available.Sum(x => x.Weight),
                2,
                MidpointRounding.AwayFromZero);

        return new ExamReadinessResult(
            course.Id,
            course.Name,
            score,
            missing.Count == 0,
            components,
            missing,
            "Somma pesata dei componenti disponibili: syllabus 60%, ultimo esito ufficiale 40%. I componenti mancanti sono esclusi e segnalati.",
            coverage);
    }

    /// <summary>
    /// Calculates a read-only overview for every non-archived course. Available
    /// scores (including partial scores) are averaged separately from the
    /// complete-assessment count; courses with no evidence remain unscored.
    /// </summary>
    public ExamReadinessOverview CalculateOverview(StudyData data, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var calculated = data.Courses
            .Where(course => !course.Archived && course.Status != CourseStatus.Archived)
            .Select(course => (Course: course, Result: Calculate(data, course.Id, today)))
            .OrderBy(item => item.Result.Score.HasValue ? 0 : 1)
            .ThenBy(item => item.Result.Score ?? decimal.MaxValue)
            .ThenBy(item => item.Course.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Course.Id, StringComparer.Ordinal)
            .ToList();

        var scored = calculated.Where(item => item.Result.Score.HasValue).ToList();
        decimal? average = scored.Count == 0
            ? null
            : Math.Round(scored.Sum(item => item.Result.Score!.Value) / scored.Count,
                2, MidpointRounding.AwayFromZero);

        var weighted = scored.Where(item => item.Course.Cfu > 0).ToList();
        var totalWeightedCfu = weighted.Sum(item => item.Course.Cfu);
        decimal? cfuWeightedAverage = totalWeightedCfu == 0
            ? null
            : Math.Round(
                weighted.Sum(item => item.Result.Score!.Value * item.Course.Cfu) / totalWeightedCfu,
                2,
                MidpointRounding.AwayFromZero);

        return new ExamReadinessOverview(
            calculated.Count,
            scored.Count,
            calculated.Count - scored.Count,
            calculated.Count(item => item.Result.IsComplete),
            calculated.Sum(item => Math.Max(0, item.Result.Coverage.EstimatedRemainingMinutes)),
            average,
            cfuWeightedAverage,
            calculated.Select(item => item.Result).ToList());
    }
}
