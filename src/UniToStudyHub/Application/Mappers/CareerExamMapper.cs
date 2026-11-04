using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Models;

namespace UniToStudyHub.Application.Mappers;

public static class CareerExamMapper
{
    public static IReadOnlyList<CareerExam> ToDomain(IEnumerable<CareerExamData> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Select(ToDomain).ToList();
    }

    public static CareerExam ToDomain(CareerExamData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var exam = new CareerExam(data.CourseId, data.Name, data.Cfu, data.CountsTowardAverage, data.Id);
        foreach (var item in data.Attempts)
        {
            var status = Enum.Parse<AttemptStatus>(item.Status, true);
            var outcome = Enum.Parse<ExamOutcome>(item.Outcome, true);
            var decision = Enum.Parse<GradeDecision>(item.GradeDecision, true);
            var attempt = new ExamAttempt(data.Id, item.AttemptedAt, item.Id);
            switch (status)
            {
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
                    GradeValue? grade = item.IsPassFail
                        ? GradeValue.Eligibility()
                        : item.NumericGrade is int value ? GradeValue.Numeric(value, item.IsHonors) : null;
                    attempt.Complete(outcome, decision, grade, item.RecordedAt);
                    break;
            }
            exam.AddAttempt(attempt);
        }
        return exam;
    }
}