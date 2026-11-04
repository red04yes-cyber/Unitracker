using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Services;

public sealed record CareerCalculationOptions
{
    public decimal HonorsValue { get; init; } = 30m;
    public int AverageDecimalPlaces { get; init; } = 4;
    public int BaseGradeDecimalPlaces { get; init; } = 4;
    public MidpointRounding Rounding { get; init; } = MidpointRounding.ToEven;

    public void Validate()
    {
        if (HonorsValue is < 18m or > 30m) throw new ArgumentOutOfRangeException(nameof(HonorsValue));
        if (AverageDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(AverageDecimalPlaces));
        if (BaseGradeDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(BaseGradeDecimalPlaces));
    }
}

public sealed record CareerSummary(
    decimal? WeightedAverage,
    decimal? BaseDegreeGrade,
    int AcquiredCfu,
    int TotalCfu,
    int PassedExams,
    int RemainingExams);

public sealed record WhatIfExam(
    string? ExistingExamId,
    string Name,
    int Cfu,
    GradeValue Grade,
    bool CountsTowardAverage = true);

public sealed record WhatIfResult(
    CareerSummary Current,
    CareerSummary Simulated,
    decimal? AverageDelta,
    decimal? BaseGradeDelta);

public sealed class CareerCalculatorService
{
    private readonly CareerCalculationOptions _options;
    public CareerCalculatorService(CareerCalculationOptions? options = null)
    {
        _options = options ?? new CareerCalculationOptions();
        _options.Validate();
    }

    public CareerSummary Calculate(IEnumerable<CareerExam> exams)
    {
        ArgumentNullException.ThrowIfNull(exams);
        return CalculateContributions(exams.Select(e =>
        {
            var official = e.GetOfficialAttempt();
            return new Contribution(e.Cfu, e.CountsTowardAverage, official?.Grade, official is not null);
        }).ToList());
    }

    public WhatIfResult Simulate(IEnumerable<CareerExam> exams, IEnumerable<WhatIfExam> scenario)
    {
        ArgumentNullException.ThrowIfNull(exams);
        ArgumentNullException.ThrowIfNull(scenario);
        var examList = exams.ToList();
        if (examList.Any(e => e is null))
            throw new ArgumentException("The career contains a null exam.", nameof(exams));
        var duplicateExam = examList.GroupBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateExam is not null)
            throw new ArgumentException($"Exam id '{duplicateExam.Key}' occurs more than once.", nameof(exams));

        var changes = scenario.ToList();
        if (changes.Any(c => c is null))
            throw new ArgumentException("A scenario contains a null item.", nameof(scenario));
        var currentIds = examList.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var duplicateTargets = changes.Where(c => c.ExistingExamId is not null)
            .GroupBy(c => c.ExistingExamId!, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateTargets is not null) throw new ArgumentException($"Exam '{duplicateTargets.Key}' is overridden more than once.", nameof(scenario));
        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Name))
                throw new ArgumentException("Every simulated exam requires a name.", nameof(scenario));
            if (change.Grade is null)
                throw new ArgumentException("Every simulated exam requires a grade or eligibility result.", nameof(scenario));
            if (change.Cfu <= 0)
                throw new ArgumentOutOfRangeException(nameof(scenario), "Scenario CFU must be positive.");
            if (change.ExistingExamId is not null && !currentIds.Contains(change.ExistingExamId))
                throw new ArgumentException($"Exam '{change.ExistingExamId}' does not exist.", nameof(scenario));
        }

        var byTarget = changes.Where(c => c.ExistingExamId is not null).ToDictionary(c => c.ExistingExamId!, StringComparer.Ordinal);
        var contributions = new List<Contribution>();
        foreach (var exam in examList)
        {
            if (byTarget.TryGetValue(exam.Id, out var replacement))
            {
                if (replacement.Cfu != exam.Cfu) throw new ArgumentException($"Scenario CFU do not match exam '{exam.Id}'.", nameof(scenario));
                contributions.Add(new Contribution(exam.Cfu, replacement.CountsTowardAverage, replacement.Grade, true));
                continue;
            }
            var official = exam.GetOfficialAttempt();
            contributions.Add(new Contribution(exam.Cfu, exam.CountsTowardAverage, official?.Grade, official is not null));
        }

        foreach (var addition in changes.Where(c => c.ExistingExamId is null))
            contributions.Add(new Contribution(addition.Cfu, addition.CountsTowardAverage, addition.Grade, true));

        var current = Calculate(examList);
        var simulated = CalculateContributions(contributions);
        return new WhatIfResult(
            current,
            simulated,
            current.WeightedAverage is null || simulated.WeightedAverage is null ? null : simulated.WeightedAverage - current.WeightedAverage,
            current.BaseDegreeGrade is null || simulated.BaseDegreeGrade is null ? null : simulated.BaseDegreeGrade - current.BaseDegreeGrade);
    }

    private CareerSummary CalculateContributions(IReadOnlyCollection<Contribution> contributions)
    {
        var acquiredCfu = contributions.Where(c => c.IsRecorded).Sum(c => c.Cfu);
        var passedExams = contributions.Count(c => c.IsRecorded);
        var totalCfu = contributions.Sum(c => c.Cfu);
        var valid = contributions.Where(c => c.IsRecorded && c.CountsTowardAverage && c.Grade is { IsPassFail: false, NumericValue: not null }).ToList();
        var averageCfu = valid.Sum(c => c.Cfu);
        decimal? average = averageCfu == 0
            ? null
            : Math.Round(valid.Sum(c => (c.Grade!.IsHonors ? _options.HonorsValue : c.Grade.NumericValue!.Value) * c.Cfu) / averageCfu, _options.AverageDecimalPlaces, _options.Rounding);
        decimal? baseGrade = average is null ? null : Math.Round(average.Value * 110m / 30m, _options.BaseGradeDecimalPlaces, _options.Rounding);
        return new CareerSummary(average, baseGrade, acquiredCfu, totalCfu, passedExams, Math.Max(0, contributions.Count - passedExams));
    }

    private sealed record Contribution(int Cfu, bool CountsTowardAverage, GradeValue? Grade, bool IsRecorded);
}