using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Entities;

public sealed class TeachingUnit
{
    public string Id { get; }
    public string DegreeProgramId { get; }
    public string Name { get; }
    public string Code { get; }
    public Cfu Cfu { get; }
    public bool CountsTowardAverage { get; }

    public TeachingUnit(string degreeProgramId, string name, string code, Cfu cfu, bool countsTowardAverage = true, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(degreeProgramId)) throw new ArgumentException("Degree program id is required.", nameof(degreeProgramId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Teaching unit name is required.", nameof(name));
        DegreeProgramId = degreeProgramId;
        Name = name.Trim();
        Code = code?.Trim() ?? string.Empty;
        Cfu = cfu;
        CountsTowardAverage = countsTowardAverage;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }
}