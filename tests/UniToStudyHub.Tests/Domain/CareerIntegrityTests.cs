using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class CareerIntegrityTests
{
    private static readonly DateTimeOffset BaseDate =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CareerExam Recorded(
        string courseId,
        string examId,
        int cfu,
        int grade,
        string? teachingUnitId = null,
        bool honors = false)
    {
        var exam = new CareerExam(courseId, $"Exam {examId}", cfu, id: examId, teachingUnitId: teachingUnitId);
        var attempt = new ExamAttempt(exam.Id, BaseDate);
        attempt.Complete(
            ExamOutcome.Passed,
            GradeDecision.Accepted,
            GradeValue.Numeric(grade, honors),
            BaseDate.AddDays(1));
        exam.AddAttempt(attempt);
        return exam;
    }

    [Fact]
    public void Validate_RejectsDuplicateExamIds()
    {
        var first = Recorded("course-1", "duplicate", 6, 24);
        var second = Recorded("course-2", "duplicate", 9, 25);

        Assert.Throws<InvalidOperationException>(() =>
            CareerIntegrity.Validate(new[] { first, second }));
    }

    [Fact]
    public void Validate_RejectsTwoExamsForTheSameCourse()
    {
        var first = Recorded("course-1", "exam-1", 6, 24);
        var second = Recorded("course-1", "exam-2", 6, 25);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CareerIntegrity.Validate(new[] { first, second }));
        Assert.Contains("CFU", ex.Message);
    }

    [Fact]
    public void Validate_RejectsTwoExamsForTheSameTeachingUnit()
    {
        var first = Recorded("course-1", "exam-1", 6, 24, teachingUnitId: "unit-1");
        var second = Recorded("course-2", "exam-2", 9, 25, teachingUnitId: "unit-1");

        Assert.Throws<InvalidOperationException>(() =>
            CareerIntegrity.Validate(new[] { first, second }));
    }

    [Fact]
    public void Validate_RejectsDuplicateAttemptIdsAcrossExams()
    {
        var first = new CareerExam("course-1", "Analisi", 6, id: "exam-1");
        var firstAttempt = new ExamAttempt(first.Id, BaseDate, "attempt-1");
        first.AddAttempt(firstAttempt);

        var second = new CareerExam("course-2", "Fisica", 9, id: "exam-2");
        var secondAttempt = new ExamAttempt(second.Id, BaseDate, "attempt-1");
        second.AddAttempt(secondAttempt);

        Assert.Throws<InvalidOperationException>(() =>
            CareerIntegrity.Validate(new[] { first, second }));
    }

    [Fact]
    public void LatestAttemptIsNotTheOfficialResultWhenItIsStillOpen()
    {
        var exam = new CareerExam("course-1", "Analisi", 6, id: "exam-1");
        var official = new ExamAttempt(exam.Id, BaseDate, "attempt-official");
        official.Complete(
            ExamOutcome.Passed,
            GradeDecision.Accepted,
            GradeValue.Numeric(24),
            BaseDate.AddDays(1));
        exam.AddAttempt(official);

        var laterOpen = new ExamAttempt(exam.Id, BaseDate.AddDays(20), "attempt-open");
        laterOpen.Book();
        exam.AddAttempt(laterOpen);

        Assert.Equal("attempt-open", exam.GetLatestAttempt()!.Id);
        Assert.Equal("attempt-official", exam.GetOfficialAttempt()!.Id);
        Assert.Equal(2, exam.Attempts.Count);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });
        Assert.Equal(6, summary.AcquiredCfu);
        Assert.Equal(24m, summary.WeightedAverage);
    }

    [Fact]
    public void HonorsValueIsConfigurableAndAffectsAverage()
    {
        var honors = Recorded("course-1", "exam-1", 6, 30, honors: true);
        var normal = Recorded("course-2", "exam-2", 6, 24);
        var service = new CareerCalculatorService(new CareerCalculationOptions { HonorsValue = 31m });

        var summary = service.Calculate(new[] { honors, normal });

        Assert.Equal(27.5m, summary.WeightedAverage);
    }

    [Fact]
    public void DefaultHonorsValueUsesThirty()
    {
        var honors = Recorded("course-1", "exam-1", 6, 30, honors: true);
        var normal = Recorded("course-2", "exam-2", 6, 24);

        var summary = new CareerCalculatorService().Calculate(new[] { honors, normal });

        Assert.Equal(27m, summary.WeightedAverage);
    }

    [Fact]
    public void ExamWithCountsTowardAverageFalseAcquiresCfuWithoutAffectingAverage()
    {
        var exam = new CareerExam("course-1", "Idoneità", 3, countsTowardAverage: false, id: "exam-1");
        var attempt = new ExamAttempt(exam.Id, BaseDate);
        attempt.Complete(
            ExamOutcome.EligibilityPassed,
            GradeDecision.NotApplicable,
            GradeValue.Eligibility(),
            BaseDate.AddDays(1));
        exam.AddAttempt(attempt);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });

        Assert.Equal(3, summary.AcquiredCfu);
        Assert.Null(summary.WeightedAverage);
    }

    [Fact]
    public void DegreeProgramSummaryUsesOfficialTotalAndCountsMissingUnitsAsRemaining()
    {
        var program = new DegreeProgram("Informatica", new Cfu(180), id: "program-1");
        program.AddTeachingUnit(new TeachingUnit(program.Id, "Analisi", "MAT1", new Cfu(6), id: "unit-1"));
        program.AddTeachingUnit(new TeachingUnit(program.Id, "Fisica", "FIS1", new Cfu(9), id: "unit-2"));
        program.AddTeachingUnit(new TeachingUnit(program.Id, "Chimica", "CHI1", new Cfu(12), id: "unit-3"));

        var exam = new CareerExam("course-1", "Analisi", 6, id: "exam-1", teachingUnitId: "unit-1");
        var attempt = new ExamAttempt(exam.Id, BaseDate);
        attempt.Complete(
            ExamOutcome.Passed,
            GradeDecision.Accepted,
            GradeValue.Numeric(24),
            BaseDate.AddDays(1));
        exam.AddAttempt(attempt);

        var summary = new CareerCalculatorService().Calculate(program, new[] { exam });

        Assert.Equal(24m, summary.WeightedAverage);
        Assert.Equal(6, summary.AcquiredCfu);
        Assert.Equal(180, summary.TotalCfu);
        Assert.Equal(174, summary.RemainingCfu);
        Assert.Equal(1, summary.PassedExams);
        Assert.Equal(2, summary.RemainingExams);
    }

    [Fact]
    public void DegreeProgramRejectsExamForUnknownTeachingUnit()
    {
        var program = new DegreeProgram("Informatica", new Cfu(180), id: "program-1");
        var exam = Recorded("course-1", "exam-1", 6, 24, teachingUnitId: "missing-unit");

        Assert.Throws<InvalidOperationException>(() =>
            new CareerCalculatorService().Calculate(program, new[] { exam }));
    }

    [Fact]
    public void DegreeProgramRejectsExamWithMismatchedCfu()
    {
        var program = new DegreeProgram("Informatica", new Cfu(180), id: "program-1");
        program.AddTeachingUnit(new TeachingUnit(program.Id, "Analisi", "MAT1", new Cfu(6), id: "unit-1"));
        var exam = Recorded("course-1", "exam-1", 9, 24, teachingUnitId: "unit-1");

        Assert.Throws<InvalidOperationException>(() =>
            new CareerCalculatorService().Calculate(program, new[] { exam }));
    }

    [Fact]
    public void DegreeProgramRejectsForeignDuplicateAndDuplicateCodeUnits()
    {
        var first = new DegreeProgram("Informatica", new Cfu(180), id: "program-1");
        var second = new DegreeProgram("Matematica", new Cfu(180), id: "program-2");
        var foreign = new TeachingUnit(second.Id, "Analisi", "MAT1", new Cfu(6), id: "unit-1");

        Assert.Throws<ArgumentException>(() => first.AddTeachingUnit(foreign));

        first.AddTeachingUnit(new TeachingUnit(first.Id, "Analisi", "MAT1", new Cfu(6), id: "unit-1"));
        Assert.Throws<InvalidOperationException>(() =>
            first.AddTeachingUnit(new TeachingUnit(first.Id, "Analisi II", "MAT1", new Cfu(6), id: "unit-2")));
        Assert.Throws<InvalidOperationException>(() =>
            first.AddTeachingUnit(new TeachingUnit(first.Id, "Fisica", "FIS1", new Cfu(9), id: "unit-1")));
    }

    [Fact]
    public void SimulateRejectsDuplicateNewTeachingUnitAndCfuMismatch()
    {
        var existing = Recorded("course-1", "exam-1", 6, 24);
        var service = new CareerCalculatorService();

        Assert.Throws<ArgumentException>(() => service.Simulate(
            new[] { existing },
            new[]
            {
                new WhatIfExam(null, "Nuovo", 6, GradeValue.Numeric(28), TeachingUnitId: "unit-new"),
                new WhatIfExam(null, "Nuovo 2", 6, GradeValue.Numeric(29), TeachingUnitId: "unit-new")
            }));

        Assert.Throws<ArgumentException>(() => service.Simulate(
            new[] { existing },
            new[] { new WhatIfExam(existing.Id, existing.Name, 9, GradeValue.Numeric(30)) }));
    }

    [Fact]
    public void AttemptMustHaveARealDate()
    {
        Assert.Throws<ArgumentException>(() => new ExamAttempt("exam-1", default));
    }

    [Fact]
    public void InvalidRoundingOptionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CareerCalculatorService(new CareerCalculationOptions { Rounding = MidpointRounding.ToZero }));
    }
}
