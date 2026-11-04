namespace UniToStudyHub.Domain.ValueObjects;

public readonly record struct Cfu
{
    public int Value { get; }
    public Cfu(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "CFU cannot be negative.");
        Value = value;
    }
    public static Cfu Positive(int value) => value <= 0
        ? throw new ArgumentOutOfRangeException(nameof(value), "CFU must be positive.")
        : new Cfu(value);
    public override string ToString() => Value.ToString();
}