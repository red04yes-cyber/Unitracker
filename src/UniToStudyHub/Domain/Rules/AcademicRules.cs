namespace UniToStudyHub.Domain.Rules;

public sealed record AcademicRules
{
    // Default: honors contributes as 30. Degree programmes can configure a different rule.
    public decimal HonorsAverageValue { get; init; } = 30m;
    public int AverageDecimalPlaces { get; init; } = 4;
    public int BaseDegreeDecimalPlaces { get; init; } = 4;
    public MidpointRounding Rounding { get; init; } = MidpointRounding.ToEven;

    public void Validate()
    {
        if (HonorsAverageValue is < 18m or > 30m) throw new ArgumentOutOfRangeException(nameof(HonorsAverageValue));
        if (AverageDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(AverageDecimalPlaces));
        if (BaseDegreeDecimalPlaces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(BaseDegreeDecimalPlaces));
    }
}