using System;
using System.Collections.Generic;
using System.Linq;
using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record GradeSimulationInput(
    string CourseId,
    string CourseName,
    int Cfu,
    int Value,
    bool Honors = false,
    bool CountsTowardAverage = true);

public sealed record GradeSimulationResult(
    decimal? CurrentAverage,
    decimal? SimulatedAverage,
    decimal? AverageDelta,
    decimal? CurrentBaseGrade,
    decimal? SimulatedBaseGrade,
    decimal? BaseGradeDelta);

/// <summary>
/// Backward-compatible facade over <see cref="CareerCalculatorService"/>.
/// Legacy Grade rows are attempts; only the most recent grade for each course
/// is projected onto the domain, so repeated exams cannot duplicate CFU.
/// </summary>
public static class GradeCalculator
{
    public static decimal? WeightedAverage(IEnumerable<Grade> grades, CareerCalculatorService? calculator = null)
    {
        ArgumentNullException.ThrowIfNull(grades);
        return Summarize(grades, calculator).WeightedAverage;
    }

    public static int AcquiredCfu(IEnumerable<Grade> grades, CareerCalculatorService? calculator = null)
    {
        ArgumentNullException.ThrowIfNull(grades);
        return Summarize(grades, calculator).AcquiredCfu;
    }

    public static decimal? ProjectedBase(IEnumerable<Grade> grades, CareerCalculatorService? calculator = null) =>
        Summarize(grades, calculator).BaseDegreeGrade;

    public static decimal? Simulate(IEnumerable<Grade> grades, Course course, int hypotheticalGrade, bool honors)
    {
        ArgumentNullException.ThrowIfNull(course);
        var result = SimulateMany(grades, new[]
        {
            new GradeSimulationInput(course.Id, course.Name, course.Cfu, hypotheticalGrade, honors)
        });
        return result.SimulatedAverage;
    }

    /// <summary>
    /// A scenario replaces the latest grade of an existing course and/or adds a
    /// course not yet taken. The supplied collection is never mutated.
    /// </summary>
    public static GradeSimulationResult SimulateMany(
        IEnumerable<Grade> grades,
        IEnumerable<GradeSimulationInput> scenario,
        CareerCalculatorService? calculator = null)
    {
        ArgumentNullException.ThrowIfNull(grades);
        ArgumentNullException.ThrowIfNull(scenario);
        var changes = scenario.ToList();
        if (changes.GroupBy(x => x.CourseId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("A course can be simulated only once per scenario.", nameof(scenario));

        var (exams, examIdByCourse) = BuildCareer(grades);
        var whatIf = new List<WhatIfExam>(changes.Count);
        foreach (var item in changes)
        {
            if (string.IsNullOrWhiteSpace(item.CourseId) || string.IsNullOrWhiteSpace(item.CourseName))
                throw new ArgumentException("Every simulated course requires an id and a name.", nameof(scenario));
            if (item.Cfu <= 0) throw new ArgumentOutOfRangeException(nameof(scenario), "CFU must be positive.");
            if (item.Value is < 18 or > 30) throw new ArgumentOutOfRangeException(nameof(scenario), "Hypothetical grades must be between 18 and 30.");

            var grade = GradeValue.Numeric(item.Value, item.Honors);
            var existingId = examIdByCourse.TryGetValue(item.CourseId, out var id) ? id : null;
            whatIf.Add(new WhatIfExam(
                existingId,
                item.CourseName,
                item.Cfu,
                grade,
                item.CountsTowardAverage,
                TeachingUnitId: existingId is null ? $"whatif:{item.CourseId}" : null));
        }

        var service = calculator ?? new CareerCalculatorService();
        var result = service.Simulate(exams, whatIf);
        return new GradeSimulationResult(
            result.Current.WeightedAverage,
            result.Simulated.WeightedAverage,
            result.AverageDelta,
            result.Current.BaseDegreeGrade,
            result.Simulated.BaseDegreeGrade,
            result.BaseGradeDelta);
    }

    public static string Format(decimal? value, string suffix = "") =>
        value is null ? "—" : value.Value.ToString("0.00") + suffix;

    private static CareerSummary Summarize(IEnumerable<Grade> grades, CareerCalculatorService? calculator)
    {
        var (exams, _) = BuildCareer(grades);
        return (calculator ?? new CareerCalculatorService()).Calculate(exams);
    }

    private static (List<CareerExam> Exams, Dictionary<string, string> ExamIdByCourse) BuildCareer(IEnumerable<Grade> grades)
    {
        var exams = new List<CareerExam>();
        var examIdByCourse = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in grades
                     .GroupBy(GradeKey, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var latest = group
                .OrderByDescending(g => g.Date)
                .ThenByDescending(g => g.Id, StringComparer.Ordinal)
                .First();

            // Rows with invalid CFU or grade cannot contribute to the career.
            // They are ignored rather than causing the dashboard to fail.
            if (latest.Cfu <= 0 || latest.Value is < 18 or > 30)
                continue;

            var courseId = string.IsNullOrWhiteSpace(latest.CourseId)
                ? $"grade:{latest.Id}"
                : latest.CourseId;
            var name = string.IsNullOrWhiteSpace(latest.CourseName) ? "Esame" : latest.CourseName.Trim();
            var exam = new CareerExam(
                courseId,
                name,
                latest.Cfu,
                latest.CountsTowardAverage,
                id: $"legacy-exam-{group.Key}");

            var attemptedAt = ToTimestamp(latest.Date);
            var attempt = new ExamAttempt(exam.Id, attemptedAt);
            attempt.Complete(
                ExamOutcome.Passed,
                GradeDecision.Accepted,
                GradeValue.Numeric(latest.Value, latest.Honors),
                attemptedAt);
            exam.AddAttempt(attempt);

            exams.Add(exam);
            examIdByCourse[group.Key] = exam.Id;
        }

        return (exams, examIdByCourse);
    }

    private static string GradeKey(Grade grade) =>
        string.IsNullOrWhiteSpace(grade.CourseId) ? $"grade:{grade.Id}" : grade.CourseId;

    private static DateTimeOffset ToTimestamp(DateTime date)
    {
        if (date == default) return DateTimeOffset.UnixEpoch;
        var utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }
}
