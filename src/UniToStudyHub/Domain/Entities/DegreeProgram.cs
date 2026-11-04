using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Domain.Rules;

namespace UniToStudyHub.Domain.Entities;

public sealed class DegreeProgram
{
    public string Id { get; }
    public string Name { get; }
    public Cfu TotalCfu { get; }
    public DateOnly? ExpectedGraduationDate { get; }
    public AcademicRules Rules { get; }

    public DegreeProgram(string name, Cfu totalCfu, DateOnly? expectedGraduationDate = null, string? id = null, AcademicRules? rules = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Degree program name is required.", nameof(name));
        if (totalCfu.Value <= 0) throw new ArgumentOutOfRangeException(nameof(totalCfu), "A degree program must have positive total CFU.");
        Name = name.Trim();
        TotalCfu = totalCfu;
        ExpectedGraduationDate = expectedGraduationDate;
        Rules = rules ?? new AcademicRules();
        Rules.Validate();
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }
}