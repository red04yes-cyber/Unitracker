using System.IO;
using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Projects the persisted career DTO onto the domain aggregate. Kept outside
/// Infrastructure so application services and calculations cannot bypass the
/// domain validation rules.
/// </summary>
public static class CareerDataProjection
{
    public static IReadOnlyList<CareerExam> ToDomainExams(IEnumerable<CareerExamData> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(data =>
        {
            if (string.IsNullOrWhiteSpace(data.CourseId) || string.IsNullOrWhiteSpace(data.Name))
                throw new InvalidDataException($"Career exam '{data.Id}' has an invalid course reference or name.");
            if (data.Cfu <= 0)
                throw new InvalidDataException($"Career exam '{data.Id}' must have positive CFU.");

            var exam = new CareerExam(
                data.CourseId,
                data.Name,
                data.Cfu,
                data.CountsTowardAverage,
                data.Id);

            foreach (var item in data.Attempts ?? Enumerable.Empty<ExamAttemptData>())
            {
                var status = ParseEnum<AttemptStatus>(item.Status, "attempt status", item.Id);
                var attempt = new ExamAttempt(exam.Id, item.AttemptedAt, item.Id);
                switch (status)
                {
                    case AttemptStatus.Scheduled:
                        break;
                    case AttemptStatus.Booked:
                        attempt.Book();
                        break;
                    case AttemptStatus.InProgress:
                        attempt.Start();
                        break;
                    case AttemptStatus.Withdrawn:
                        attempt.Withdraw();
                        break;
                    case AttemptStatus.Completed:
                        var outcome = ParseEnum<ExamOutcome>(item.Outcome, "exam outcome", item.Id);
                        var decision = ParseEnum<GradeDecision>(item.GradeDecision, "grade decision", item.Id);
                        GradeValue? grade = item.IsPassFail
                            ? GradeValue.Eligibility()
                            : item.NumericGrade is int value ? GradeValue.Numeric(value, item.IsHonors) : null;
                        attempt.Complete(outcome, decision, grade, item.RecordedAt);
                        break;
                    default:
                        throw new InvalidDataException($"Attempt '{item.Id}' has an unknown status '{item.Status}'.");
                }

                exam.AddAttempt(attempt);
            }

            return exam;
        }).ToList();
    }

    public static TEnum ParseEnum<TEnum>(string value, string label, string attemptId)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            throw new InvalidDataException($"Attempt '{attemptId}' has an unknown {label} '{value}'.");
        return parsed;
    }
}
