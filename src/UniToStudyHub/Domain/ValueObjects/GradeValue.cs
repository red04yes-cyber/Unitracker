namespace UniToStudyHub.Domain.ValueObjects;
public sealed record GradeValue
{
    public int? NumericValue { get; }
    public bool IsHonors { get; }
    public bool IsPassFail { get; }

    private GradeValue(int? numericValue, bool isHonors, bool isPassFail)
    {
        NumericValue = numericValue;
        IsHonors = isHonors;
        IsPassFail = isPassFail;
    }

    public static GradeValue Numeric(int value, bool honors = false)
    {
        if (value is < 18 or > 30) throw new ArgumentOutOfRangeException(nameof(value), "A numeric grade must be between 18 and 30.");
        if (honors && value != 30) throw new ArgumentException("Honors can only be assigned to a grade of 30.", nameof(honors));
        return new GradeValue(value, honors, false);
    }

    public static GradeValue Eligibility() => new(null, false, true);

    public static GradeValue Restore(int? value, bool honors, bool isPassFail)
    {
        if (isPassFail)
        {
            if (value is not null || honors) throw new ArgumentException("An eligibility grade cannot have a numeric value or honors.");
            return Eligibility();
        }
        if (value is null) throw new ArgumentException("A numeric grade must have a value.");
        return Numeric(value.Value, honors);
    }
}