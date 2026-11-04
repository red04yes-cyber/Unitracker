using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;

namespace UniToStudyHub.Domain.Services;

/// <summary>
/// Validates the integrity of a career collection before it is used for
/// calculation. The calculator must never silently repair duplicate ids or
/// count the same teaching unit twice.
/// </summary>
public static class CareerIntegrity
{
    public static IReadOnlyList<CareerExam> Materialize(IEnumerable<CareerExam> exams, string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(exams, parameterName);
        var list = exams.ToList();
        if (list.Any(x => x is null))
            throw new ArgumentException("The career contains a null exam.", parameterName);
        return list;
    }

    public static void Validate(IReadOnlyCollection<CareerExam> exams)
    {
        ArgumentNullException.ThrowIfNull(exams);

        var examIds = new HashSet<string>(StringComparer.Ordinal);
        var courseIds = new HashSet<string>(StringComparer.Ordinal);
        var teachingUnitIds = new HashSet<string>(StringComparer.Ordinal);
        var attemptIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var exam in exams)
        {
            if (string.IsNullOrWhiteSpace(exam.Id))
                throw new InvalidOperationException("An exam has an empty identifier.");
            if (!examIds.Add(exam.Id))
                throw new InvalidOperationException($"Exam id '{exam.Id}' occurs more than once.");
            if (!courseIds.Add(exam.CourseId))
                throw new InvalidOperationException(
                    $"Course '{exam.CourseId}' is linked to more than one exam; its CFU would be counted twice.");
            if (!teachingUnitIds.Add(exam.TeachingUnitId))
                throw new InvalidOperationException(
                    $"Teaching unit '{exam.TeachingUnitId}' is linked to more than one exam; its CFU would be counted twice.");

            foreach (var attempt in exam.Attempts)
            {
                if (!string.Equals(attempt.ExamId, exam.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Attempt '{attempt.Id}' belongs to exam '{attempt.ExamId}' but is stored under '{exam.Id}'.");
                if (!attemptIds.Add(attempt.Id))
                    throw new InvalidOperationException($"Attempt id '{attempt.Id}' occurs more than once.");

                ValidateAttempt(attempt);
            }
        }
    }

    private static void ValidateAttempt(ExamAttempt attempt)
    {
        if (attempt.AttemptedAt == default)
            throw new InvalidOperationException($"Attempt '{attempt.Id}' has no valid attempt date.");
        if (attempt.RecordedAt is not null && attempt.RecordedAt.Value < attempt.AttemptedAt)
            throw new InvalidOperationException(
                $"Attempt '{attempt.Id}' was recorded before it took place.");

        if (attempt.Status == AttemptStatus.Completed)
        {
            if (attempt.Outcome == ExamOutcome.Pending)
                throw new InvalidOperationException($"Completed attempt '{attempt.Id}' has no final outcome.");

            var requiresRecording = attempt.GradeDecision == GradeDecision.Accepted ||
                attempt.Outcome == ExamOutcome.EligibilityPassed;
            if (requiresRecording != attempt.RecordedAt.HasValue)
                throw new InvalidOperationException(
                    $"Attempt '{attempt.Id}' has an inconsistent recording timestamp.");
        }
        else
        {
            if (attempt.Outcome != ExamOutcome.Pending ||
                attempt.GradeDecision != GradeDecision.Pending ||
                attempt.Grade is not null ||
                attempt.RecordedAt is not null)
                throw new InvalidOperationException(
                    $"Open or withdrawn attempt '{attempt.Id}' carries a final result.");
        }
    }
}
