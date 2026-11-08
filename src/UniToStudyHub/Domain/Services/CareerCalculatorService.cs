using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Services;

public sealed record CareerCalculationOptions
{
    /// <summary>Numeric value assigned to 30 e lode. Configurable because universities differ.</summary>
    public decimal HonorsValue { get; init; } = 30m;
    public int AverageDecimalPlaces { get; init; } = 4;
    public int BaseGradeDecimalPlaces { get; init; } = 4;
    public MidpointRounding Rounding { get; init; } = MidpointRounding.ToEven;

    public void Validate()
    {
        // 30 e lode can be worth 30, 30.5 or 31 at different universities; the
        // bonus is configurable but remains on a 30-point scale.
        if (HonorsValue is < 30m or > 33m) throw new ArgumentOutOfRangeException(nameof(HonorsValue));
        if (AverageDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(AverageDecimalPlaces));
        if (BaseGradeDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(BaseGradeDecimalPlaces));
        if (Rounding is not (MidpointRounding.ToEven or MidpointRounding.AwayFromZero))
            throw new ArgumentOutOfRangeException(nameof(Rounding), "Only banker's or away-from-zero rounding is supported.");
    }
}

public sealed record CareerSummary(
    decimal? WeightedAverage,
    decimal? BaseDegreeGrade,
    int AcquiredCfu,
    int TotalCfu,
    int PassedExams,
    int RemainingExams)
{
    public int RemainingCfu => Math.Max(0, TotalCfu - AcquiredCfu);
}

/// <summary>
/// A hypothetical exam result. <paramref name="ExistingExamId"/> replaces the
/// result of an exam already present in the career; when null the exam is new.
/// </summary>
public sealed record WhatIfExam(
    string? ExistingExamId,
    string Name,
    int Cfu,
    GradeValue Grade,
    bool? CountsTowardAverage = null,
    string? TeachingUnitId = null);

public sealed record WhatIfResult(
    CareerSummary Current,
    CareerSummary Simulated,
    decimal? AverageDelta,
    decimal? BaseGradeDelta);

/// <summary>
/// Single source of truth for every career calculation. The legacy
/// <c>GradeCalculator</c> delegates here so the dashboard and the domain can
/// never disagree.
/// </summary>
public sealed class CareerCalculatorService
{
    private readonly CareerCalculationOptions _options;

    public CareerCalculatorService(CareerCalculationOptions? options = null)
    {
        _options = options ?? new CareerCalculationOptions();
        _options.Validate();
    }

    /// <summary>
    /// Calculates the summary using the exams that are part of the career. Total
    /// CFU is the sum of the known exam CFU; use the degree-program overload when
    /// the official degree total is available.
    /// </summary>
    public CareerSummary Calculate(IEnumerable<CareerExam> exams)
    {
        var list = CareerIntegrity.Materialize(exams, nameof(exams));
        CareerIntegrity.Validate(list);
        return Calculate(list, totalCfu: null, remainingExamCount: null);
    }

    /// <summary>
    /// Calculates the career against an explicit degree-program total. This is
    /// the overload used by the application profile; when the profile has not
    /// configured a total, the curriculum total is derived from its courses.
    /// </summary>
    public CareerSummary Calculate(IEnumerable<CareerExam> exams, int totalCfu)
    {
        if (totalCfu <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalCfu), "The degree total must be positive.");
        var list = CareerIntegrity.Materialize(exams, nameof(exams));
        CareerIntegrity.Validate(list);
        return Calculate(list, totalCfu, remainingExamCount: null);
    }

    /// <summary>
    /// Calculates the summary against the official degree program total and
    /// verifies that every exam belongs to one of its teaching units.
    /// </summary>
    public CareerSummary Calculate(DegreeProgram program, IEnumerable<CareerExam> exams)
    {
        ArgumentNullException.ThrowIfNull(program);
        var list = CareerIntegrity.Materialize(exams, nameof(exams));
        CareerIntegrity.Validate(list);
        foreach (var exam in list)
            program.ValidateExam(exam);

        var passed = list.Count(x => x.HasOfficialResult());
        var remaining = Math.Max(0, program.TeachingUnits.Count - passed);
        return Calculate(list, program.TotalCfu.Value, remaining);
    }

    public WhatIfResult Simulate(IEnumerable<CareerExam> exams, IEnumerable<WhatIfExam> scenario)
    {
        var examList = CareerIntegrity.Materialize(exams, nameof(exams));
        CareerIntegrity.Validate(examList);
        ArgumentNullException.ThrowIfNull(scenario);
        var changes = scenario.ToList();
        if (changes.Any(c => c is null))
            throw new ArgumentException("A scenario contains a null item.", nameof(scenario));

        var examById = examList.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var unitsById = examList.ToDictionary(e => e.TeachingUnitId, StringComparer.Ordinal);
        var duplicates = changes
            .Where(c => c.ExistingExamId is not null)
            .GroupBy(c => c.ExistingExamId!, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicates is not null)
            throw new ArgumentException($"Exam '{duplicates.Key}' is overridden more than once.", nameof(scenario));

        var newUnitKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Name))
                throw new ArgumentException("Every simulated exam requires a name.", nameof(scenario));
            if (change.Grade is null)
                throw new ArgumentException("Every simulated exam requires a grade or eligibility result.", nameof(scenario));
            if (change.Cfu <= 0)
                throw new ArgumentOutOfRangeException(nameof(scenario), "Scenario CFU must be positive.");

            if (change.ExistingExamId is not null)
            {
                if (string.IsNullOrWhiteSpace(change.ExistingExamId) || !examById.TryGetValue(change.ExistingExamId, out var existing))
                    throw new ArgumentException($"Exam '{change.ExistingExamId}' does not exist.", nameof(scenario));
                if (change.Cfu != existing.Cfu)
                    throw new ArgumentException($"Scenario CFU for exam '{existing.Id}' must match its {existing.Cfu} CFU.", nameof(scenario));
                if (change.TeachingUnitId is not null &&
                    !string.Equals(change.TeachingUnitId, existing.TeachingUnitId, StringComparison.Ordinal))
                    throw new ArgumentException($"Exam '{existing.Id}' belongs to teaching unit '{existing.TeachingUnitId}'.", nameof(scenario));
                continue;
            }

            var unitKey = change.TeachingUnitId ?? change.Name.Trim();
            if (!newUnitKeys.Add(unitKey))
                throw new ArgumentException($"Simulated exam '{unitKey}' occurs more than once.", nameof(scenario));
            if (change.TeachingUnitId is not null && unitsById.ContainsKey(change.TeachingUnitId))
                throw new ArgumentException(
                    $"Teaching unit '{change.TeachingUnitId}' is already part of the career; use ExistingExamId to replace its grade.",
                    nameof(scenario));
        }

        var byTarget = changes
            .Where(c => c.ExistingExamId is not null)
            .ToDictionary(c => c.ExistingExamId!, StringComparer.Ordinal);
        var contributions = new List<Contribution>(examList.Count + changes.Count);
        foreach (var exam in examList)
        {
            if (byTarget.TryGetValue(exam.Id, out var replacement))
            {
                contributions.Add(new Contribution(
                    exam.Cfu,
                    replacement.CountsTowardAverage ?? exam.CountsTowardAverage,
                    replacement.Grade,
                    IsRecorded: true));
                continue;
            }

            var official = exam.GetOfficialAttempt();
            contributions.Add(new Contribution(
                exam.Cfu,
                exam.CountsTowardAverage,
                official?.Grade,
                IsRecorded: official is not null));
        }

        foreach (var addition in changes.Where(c => c.ExistingExamId is null))
            contributions.Add(new Contribution(
                addition.Cfu,
                addition.CountsTowardAverage ?? true,
                addition.Grade,
                IsRecorded: true));

        var current = Calculate(examList, totalCfu: null, remainingExamCount: null);
        var simulated = CalculateContributions(contributions, totalCfu: null, remainingExamCount: null);

        // The simulation operates on immutable contributions; validate the real
        // career again so a future regression cannot silently alter the history.
        CareerIntegrity.Validate(examList);

        return new WhatIfResult(
            current,
            simulated,
            current.WeightedAverage is null || simulated.WeightedAverage is null
                ? null
                : simulated.WeightedAverage - current.WeightedAverage,
            current.BaseDegreeGrade is null || simulated.BaseDegreeGrade is null
                ? null
                : simulated.BaseDegreeGrade - current.BaseDegreeGrade);
    }

    private CareerSummary Calculate(
        IReadOnlyCollection<CareerExam> exams,
        int? totalCfu,
        int? remainingExamCount) =>
        CalculateContributions(
            exams.Select(exam =>
            {
                var official = exam.GetOfficialAttempt();
                return new Contribution(exam.Cfu, exam.CountsTowardAverage, official?.Grade, official is not null);
            }).ToList(),
            totalCfu,
            remainingExamCount);

    private CareerSummary CalculateContributions(
        IReadOnlyCollection<Contribution> contributions,
        int? totalCfu,
        int? remainingExamCount)
    {
        var acquiredCfu = contributions.Where(c => c.IsRecorded).Sum(c => c.Cfu);
        var passedExams = contributions.Count(c => c.IsRecorded);
        var knownTotalCfu = contributions.Sum(c => c.Cfu);
        var total = totalCfu ?? knownTotalCfu;
        var remaining = remainingExamCount ?? contributions.Count(c => !c.IsRecorded);

        var averageContributions = contributions
            .Where(c => c.IsRecorded &&
                        c.CountsTowardAverage &&
                        c.Grade is { IsPassFail: false, NumericValue: not null })
            .ToList();
        var averageCfu = averageContributions.Sum(c => c.Cfu);

        decimal? average = averageCfu == 0
            ? null
            : Math.Round(
                averageContributions.Sum(c =>
                    (c.Grade!.IsHonors ? _options.HonorsValue : c.Grade.NumericValue!.Value) * c.Cfu) / averageCfu,
                _options.AverageDecimalPlaces,
                _options.Rounding);

        decimal? baseGrade = average is null
            ? null
            : Math.Round(average.Value * 110m / 30m, _options.BaseGradeDecimalPlaces, _options.Rounding);

        return new CareerSummary(
            average,
            baseGrade,
            acquiredCfu,
            total,
            passedExams,
            Math.Max(0, remaining));
    }

    private sealed record Contribution(int Cfu, bool CountsTowardAverage, GradeValue? Grade, bool IsRecorded);
}
