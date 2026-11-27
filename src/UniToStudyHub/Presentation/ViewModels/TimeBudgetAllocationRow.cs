namespace UniToStudyHub.Presentation.ViewModels;

/// <summary>
/// Binding-safe projection of a study allocation. Missing estimates are
/// represented explicitly so a zero-minute allocation is never shown as
/// confirmed coverage.
/// </summary>
public sealed record TimeBudgetAllocationRow(
    string CourseId,
    string CourseName,
    DateTime? Deadline,
    int EstimatedStudyMinutes,
    int AllocatedMinutes,
    int WeeksAvailable,
    bool HasEstimate,
    bool IsAtRisk,
    string Explanation);
