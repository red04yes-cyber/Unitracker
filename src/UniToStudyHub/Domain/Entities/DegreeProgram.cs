using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Entities;

/// <summary>
/// Root of the career definition. Teaching units belong to exactly one degree
/// program, and career exams refer to a teaching unit of the same program.
/// </summary>
public sealed class DegreeProgram
{
    private readonly List<TeachingUnit> _teachingUnits = new();

    public string Id { get; }
    public string Name { get; }
    public Cfu TotalCfu { get; }
    public DateOnly? ExpectedGraduationDate { get; }
    public IReadOnlyCollection<TeachingUnit> TeachingUnits => _teachingUnits.AsReadOnly();

    public DegreeProgram(string name, Cfu totalCfu, DateOnly? expectedGraduationDate = null, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Degree program name is required.", nameof(name));
        if (totalCfu.Value <= 0) throw new ArgumentOutOfRangeException(nameof(totalCfu), "A degree program must have positive total CFU.");

        Name = name.Trim();
        TotalCfu = totalCfu;
        ExpectedGraduationDate = expectedGraduationDate;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    public void AddTeachingUnit(TeachingUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!string.Equals(unit.DegreeProgramId, Id, StringComparison.Ordinal))
            throw new ArgumentException("The teaching unit belongs to a different degree program.", nameof(unit));
        if (_teachingUnits.Any(x => string.Equals(x.Id, unit.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Teaching unit id '{unit.Id}' already exists in the degree program.");
        if (!string.IsNullOrWhiteSpace(unit.Code) &&
            _teachingUnits.Any(x => string.Equals(x.Code, unit.Code, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Teaching unit code '{unit.Code}' already exists in the degree program.");

        _teachingUnits.Add(unit);
    }

    public TeachingUnit? FindTeachingUnit(string teachingUnitId) =>
        _teachingUnits.FirstOrDefault(x => string.Equals(x.Id, teachingUnitId, StringComparison.Ordinal));

    /// <summary>
    /// Ensures an exam belongs to a teaching unit of this program and keeps the
    /// CFU aligned with the unit. This prevents a program and its exams from
    /// silently describing different courses.
    /// </summary>
    public void ValidateExam(CareerExam exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        var unit = FindTeachingUnit(exam.TeachingUnitId)
            ?? throw new InvalidOperationException(
                $"Exam '{exam.Id}' refers to teaching unit '{exam.TeachingUnitId}', which does not belong to degree program '{Id}'.");
        if (unit.Cfu.Value != exam.Cfu)
            throw new InvalidOperationException(
                $"Exam '{exam.Id}' has {exam.Cfu} CFU but teaching unit '{unit.Id}' has {unit.Cfu.Value} CFU.");
    }
}
