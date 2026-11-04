using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class CareerCalculatorServiceTests
{
    private static CareerExam Recorded(string id, int cfu, int grade)
    {
        var exam = new CareerExam($"course-{id}", $"Exam {id}", cfu, id: id);
        var attempt = new ExamAttempt(exam.Id, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        attempt.Complete(ExamOutcome.Passed, GradeDecision.Accepted, GradeValue.Numeric(grade), new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero));
        exam.AddAttempt(attempt);
        return exam;
    }

    [Fact]
    public void Calculate_ReturnsNullWhenNoOfficialNumericGradeExists()
    {
        var service = new CareerCalculatorService();
        var exam = new CareerExam("course-1", "Unrecorded", 6);

        var summary = service.Calculate(new[] { exam });

        Assert.Null(summary.WeightedAverage);
        Assert.Null(summary.BaseDegreeGrade);
        Assert.Equal(0, summary.AcquiredCfu);
    }

    [Fact]
    public void Calculate_UsesOneOfficialGradeAndCountsCfuOnce()
    {
        var first = Recorded("exam-1", 6, 24);
        var second = Recorded("exam-2", 12, 30);
        var service = new CareerCalculatorService();

        var summary = service.Calculate(new[] { first, second });

        Assert.Equal(28m, summary.WeightedAverage);
        Assert.Equal(18, summary.AcquiredCfu);
        Assert.Equal(18, summary.TotalCfu);
    }

    [Fact]
    public void SimulateMany_ReplacesExistingExamAndAddsNewExamWithoutMutatingCareer()
    {
        var first = Recorded("exam-1", 6, 24);
        var second = Recorded("exam-2", 12, 30);
        var service = new CareerCalculatorService();
        var scenario = new[]
        {
            new WhatIfExam("exam-1", "Exam 1", 6, GradeValue.Numeric(27)),
            new WhatIfExam(null, "Exam 3", 6, GradeValue.Numeric(27))
        };

        var result = service.Simulate(new[] { first, second }, scenario);

        Assert.Equal(28m, result.Current.WeightedAverage);
        Assert.Equal(28.5m, result.Simulated.WeightedAverage);
        Assert.Equal(18, result.Current.AcquiredCfu);
        Assert.Equal(24, result.Simulated.AcquiredCfu);
        Assert.Equal(24, first.GetOfficialAttempt()!.Grade!.NumericValue);
        Assert.Equal(30, second.GetOfficialAttempt()!.Grade!.NumericValue);
        Assert.Equal(0.5m, result.AverageDelta);
    }

    [Fact]
    public void SimulateMany_RejectsDuplicateOverrideForSameExam()
    {
        var first = Recorded("exam-1", 6, 24);
        var scenario = new[]
        {
            new WhatIfExam("exam-1", "First", 6, GradeValue.Numeric(25)),
            new WhatIfExam("exam-1", "Second", 6, GradeValue.Numeric(26))
        };

        Assert.Throws<ArgumentException>(() => new CareerCalculatorService().Simulate(new[] { first }, scenario));
    }
}