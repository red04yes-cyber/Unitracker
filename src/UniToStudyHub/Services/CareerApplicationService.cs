using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record WhatIfCourseInput(
    string CourseId,
    string Name,
    int Cfu,
    int? NumericGrade,
    bool Honors = false,
    bool IsEligibility = false,
    bool CountsTowardAverage = true);

public sealed record CareerOverview(
    CareerSummary Summary,
    decimal? ArithmeticAverage,
    int AttemptCount,
    int OfficialResultCount);

/// <summary>
/// Career use cases. It operates on the shared <see cref="StudyData"/> snapshot;
/// persistence remains an explicit caller responsibility so an operation can be
/// validated and shown to the user before touching the database.
/// </summary>
public sealed class CareerApplicationService(CareerCalculatorService calculator)
{
    private readonly CareerCalculatorService _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));

    public CareerSummary GetSummary(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var exams = CareerDataProjection.ToDomainExams(data.CareerExams);
        return data.DegreeTotalCfu > 0
            ? _calculator.Calculate(exams, data.DegreeTotalCfu)
            : _calculator.Calculate(exams);
    }

    /// <summary>
    /// Arithmetic average of the latest official numeric result for each exam.
    /// Eligibility results and rejected grades are excluded by the domain flags.
    /// </summary>
    public decimal? GetArithmeticAverage(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var values = CareerDataProjection.ToDomainExams(data.CareerExams)
            .Select(exam => exam.GetOfficialAttempt()?.Grade)
            .Where(grade => grade is { IsPassFail: false, NumericValue: not null })
            .Select(grade => grade!.NumericValue!.Value)
            .ToList();
        return values.Count == 0 ? null : Math.Round((decimal)values.Average(), 2, MidpointRounding.AwayFromZero);
    }

    public CareerOverview GetOverview(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var exams = CareerDataProjection.ToDomainExams(data.CareerExams);
        var attempts = exams.Sum(x => x.Attempts.Count);
        var official = exams.Count(x => x.GetOfficialAttempt() is not null);
        return new CareerOverview(GetSummary(data), GetArithmeticAverage(data), attempts, official);
    }

    public CareerExamData EnsureCareerExam(StudyData data, Course course)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(course);
        if (course.Cfu <= 0) throw new ArgumentOutOfRangeException(nameof(course), "Only courses with positive CFU can have an exam.");
        var existing = data.CareerExams.FirstOrDefault(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.Cfu != course.Cfu && existing.Attempts.Count > 0)
                throw new InvalidOperationException(
                    $"Course '{course.Id}' already has exam history with {existing.Cfu} CFU; the CFU cannot be changed without an explicit migration.");
            existing.Name = course.Name;
            existing.Cfu = course.Cfu;
            return existing;
        }

        var created = new CareerExamData
        {
            Id = Guid.NewGuid().ToString("N"),
            CourseId = course.Id,
            Name = course.Name,
            Cfu = course.Cfu,
            CountsTowardAverage = true
        };
        data.CareerExams.Add(created);
        return created;
    }

    /// <summary>
    /// Records a result while preserving every previous attempt. The domain
    /// object enforces the transition and grade rules before the DTO is added.
    /// </summary>
    public ExamAttemptData RecordResult(
        StudyData data,
        string courseId,
        ExamOutcome outcome,
        GradeDecision decision,
        int? numericGrade,
        bool honors,
        bool isPassFail,
        DateTimeOffset attemptedAt,
        DateTimeOffset? recordedAt = null,
        string? attemptId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var course = data.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
        var examData = EnsureCareerExam(data, course);
        var id = string.IsNullOrWhiteSpace(attemptId) ? Guid.NewGuid().ToString("N") : attemptId;
        if (data.CareerExams.SelectMany(x => x.Attempts).Any(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Attempt id '{id}' already exists.");

        var attempt = new ExamAttempt(examData.Id, attemptedAt, id);
        var grade = isPassFail
            ? GradeValue.Eligibility()
            : numericGrade is int value ? GradeValue.Numeric(value, honors) : null;
        attempt.Complete(outcome, decision, grade, recordedAt);

        var dto = new ExamAttemptData
        {
            Id = attempt.Id,
            AttemptedAt = attempt.AttemptedAt,
            Status = attempt.Status.ToString(),
            Outcome = attempt.Outcome.ToString(),
            GradeDecision = attempt.GradeDecision.ToString(),
            NumericGrade = attempt.Grade?.NumericValue,
            IsHonors = attempt.Grade?.IsHonors ?? false,
            IsPassFail = attempt.Grade?.IsPassFail ?? false,
            RecordedAt = attempt.RecordedAt
        };
        examData.Attempts.Add(dto);
        ApplyCourseOutcome(course, outcome, decision);
        return dto;
    }

    public ExamAttemptData ScheduleAttempt(StudyData data, string courseId, DateTimeOffset attemptedAt, string? attemptId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var course = data.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
        var examData = EnsureCareerExam(data, course);
        var id = string.IsNullOrWhiteSpace(attemptId) ? Guid.NewGuid().ToString("N") : attemptId;
        if (data.CareerExams.SelectMany(x => x.Attempts).Any(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Attempt id '{id}' already exists.");
        var attempt = new ExamAttempt(examData.Id, attemptedAt, id);
        var dto = new ExamAttemptData
        {
            Id = attempt.Id,
            AttemptedAt = attempt.AttemptedAt,
            Status = attempt.Status.ToString(),
            Outcome = attempt.Outcome.ToString(),
            GradeDecision = attempt.GradeDecision.ToString()
        };
        examData.Attempts.Add(dto);
        course.Status = CourseStatus.ExamScheduled;
        return dto;
    }

    public WhatIfResult Simulate(StudyData data, IEnumerable<WhatIfCourseInput> scenario)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(scenario);
        var exams = CareerDataProjection.ToDomainExams(data.CareerExams);
        var byCourse = exams.ToDictionary(x => x.CourseId, StringComparer.Ordinal);
        var changes = scenario.ToList();
        if (changes.Any(x => string.IsNullOrWhiteSpace(x.CourseId) || string.IsNullOrWhiteSpace(x.Name) || x.Cfu <= 0))
            throw new ArgumentException("Every simulated course needs an id, name and positive CFU.", nameof(scenario));
        if (changes.GroupBy(x => x.CourseId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("A course can be simulated only once.", nameof(scenario));

        var whatIf = changes.Select(item =>
        {
            var grade = item.IsEligibility
                ? GradeValue.Eligibility()
                : item.NumericGrade is int value
                    ? GradeValue.Numeric(value, item.Honors)
                    : throw new ArgumentException($"Course '{item.CourseId}' needs a numeric grade or eligibility.");
            var existing = byCourse.TryGetValue(item.CourseId, out var exam);
            return new WhatIfExam(
                existing ? exam!.Id : null,
                item.Name,
                item.Cfu,
                grade,
                item.CountsTowardAverage,
                existing ? null : $"whatif:{item.CourseId}");
        }).ToList();

        return _calculator.Simulate(exams, whatIf);
    }

    public IReadOnlyList<ExamAttemptData> GetAttempts(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var exam = data.CareerExams.FirstOrDefault(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal));
        return exam is null
            ? Array.Empty<ExamAttemptData>()
            : exam.Attempts.OrderByDescending(x => x.AttemptedAt).ToList();
    }

    private static void ApplyCourseOutcome(Course course, ExamOutcome outcome, GradeDecision decision)
    {
        if (outcome is ExamOutcome.Passed or ExamOutcome.EligibilityPassed &&
            decision is GradeDecision.Accepted or GradeDecision.NotApplicable)
        {
            course.Completed = true;
            course.Status = CourseStatus.Passed;
        }
        else if (outcome == ExamOutcome.Failed)
        {
            course.Completed = false;
            course.Status = CourseStatus.Failed;
        }
    }
}
