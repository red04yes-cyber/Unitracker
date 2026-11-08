using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record StudyAllocation(
    string CourseId,
    string CourseName,
    DateTime? Deadline,
    int EstimatedStudyMinutes,
    int AllocatedMinutes,
    int WeeksAvailable,
    bool IsAtRisk,
    string Explanation);

public sealed record TimeBudgetPlan(
    int WeeklyMinutes,
    int AvailableMinutes,
    int RequiredMinutes,
    IReadOnlyList<StudyAllocation> Allocations,
    IReadOnlyList<string> Warnings)
{
    public int UnallocatedMinutes => Math.Max(0, AvailableMinutes - Allocations.Sum(x => x.AllocatedMinutes));
    public bool IsOverCapacity => RequiredMinutes > AvailableMinutes;
}

/// <summary>
/// Calculates whether the configured weekly time can cover the estimated study
/// load before each planned exam. It only proposes an allocation; it never
/// writes calendar events or overwrites existing commitments.
/// </summary>
public sealed class TimeBudgetService
{
    public TimeBudgetPlan Calculate(StudyData data, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var start = (today ?? DateTime.Today).Date;
        var weeklyMinutes = Math.Max(0, data.WeeklyStudyHours) * 60;
        var exams = data.Exams
            .Where(x => !x.Completed && x.PlannedDate.HasValue && x.PlannedDate.Value.Date >= start)
            .OrderBy(x => x.PlannedDate)
            .ToList();
        if (exams.Count == 0)
        {
            return new TimeBudgetPlan(
                weeklyMinutes,
                0,
                0,
                Array.Empty<StudyAllocation>(),
                new[] { "Nessun esame futuro con una data pianificata: il budget non può essere distribuito." });
        }

        var requirements = exams.Select(exam =>
        {
            var course = data.Courses.FirstOrDefault(x => string.Equals(x.Id, exam.CourseId, StringComparison.Ordinal));
            var estimated = exam.EstimatedStudyHours > 0
                ? exam.EstimatedStudyHours
                : course?.EstimatedStudyHours ?? 0;
            var days = Math.Max(1, (exam.PlannedDate!.Value.Date - start).Days);
            var weeks = Math.Max(1, (int)Math.Ceiling(days / 7d));
            return new Requirement(exam, estimated * 60, weeks);
        }).ToList();

        var required = requirements.Sum(x => x.Minutes);
        var available = weeklyMinutes * Math.Max(1, requirements.Max(x => x.Weeks));
        var warnings = new List<string>();
        if (required == 0)
            warnings.Add("Nessuna stima delle ore di studio è disponibile per gli esami pianificati.");
        if (available < required)
            warnings.Add($"Il carico stimato supera la capacità disponibile di {required - available} minuti.");
        if (weeklyMinutes == 0)
            warnings.Add("Le ore settimanali configurate sono zero: nessuna allocazione è possibile.");

        var denominator = required > 0 ? required : 1;
        var allocations = requirements.Select(item =>
        {
            var allocated = available == 0 || required == 0
                ? 0
                : (int)Math.Floor((decimal)item.Minutes / denominator * Math.Min(available, required));
            return new StudyAllocation(
                item.Exam.CourseId,
                item.Exam.Name,
                item.Exam.PlannedDate,
                item.Minutes,
                allocated,
                item.Weeks,
                allocated < item.Minutes,
                allocated < item.Minutes
                    ? $"Mancano {item.Minutes - allocated} minuti per coprire la stima entro {item.Weeks} settimane."
                    : "La stima è coperta dal budget configurato.");
        }).ToList();

        return new TimeBudgetPlan(weeklyMinutes, available, required, allocations, warnings);
    }

    private sealed record Requirement(Exam Exam, int Minutes, int Weeks);
}
