using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Entities;

/// <summary>
/// A course/teaching unit belonging to a degree program. Legacy <c>Course</c>
/// rows are projected onto this entity preserving their identifier, so that
/// <see cref="CareerExam.TeachingUnitId"/> remains stable.
/// </summary>
public sealed class TeachingUnit
{
    public string Id { get; }
    public string DegreeProgramId { get; }
    public string Name { get; }
    public string Code { get; }
    public Cfu Cfu { get; }
    public bool CountsTowardAverage { get; }

    public TeachingUnit(
        string degreeProgramId,
        string name,
        string code,
        Cfu cfu,
        bool countsTowardAverage = true,
        string? id = null)
    {
        if (string.IsNullOrWhiteSpace(degreeProgramId)) throw new ArgumentException("Degree program id is required.", nameof(degreeProgramId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Teaching unit name is required.", nameof(name));
        if (cfu.Value <= 0) throw new ArgumentOutOfRangeException(nameof(cfu), "A teaching unit must have positive CFU.");

        DegreeProgramId = degreeProgramId;
        Name = name.Trim();
        Code = code?.Trim() ?? string.Empty;
        Cfu = cfu;
        CountsTowardAverage = countsTowardAverage;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }
}
