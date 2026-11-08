namespace UniToStudyHub.Domain.ValueObjects;

public readonly record struct AcademicYear
{
    public int StartYear { get; }
    public int EndYear => StartYear + 1;
    public string Label => $"{StartYear}/{EndYear}";
    public AcademicYear(int startYear)
    {
        if (startYear is < 1900 or > 2200) throw new ArgumentOutOfRangeException(nameof(startYear));
        StartYear = startYear;
    }
    public override string ToString() => Label;
}