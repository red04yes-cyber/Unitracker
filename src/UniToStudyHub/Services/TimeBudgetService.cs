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
    string Explanation)
{
    /// <summary>False when neither the exam nor its course provides an estimate.</summary>
    public bool HasEstimate => EstimatedStudyMinutes > 0;
}

public sealed record TimeBudgetPlan(
    int WeeklyMinutes,
    int AvailableMinutes,
    int RequiredMinutes,
    IReadOnlyList<StudyAllocation> Allocations,
    IReadOnlyList<string> Warnings)
{
    public int UnallocatedMinutes => (int)Math.Clamp(
        (long)AvailableMinutes - Allocations.Sum(x => (long)x.AllocatedMinutes),
        0L,
        int.MaxValue);

    /// <summary>
    /// True when total demand exceeds the horizon capacity or at least one
    /// deadline cannot be met, even if later capacity remains unused.
    /// </summary>
    public bool IsOverCapacity =>
        RequiredMinutes > AvailableMinutes || Allocations.Any(x => x.IsAtRisk);
}

/// <summary>
/// Produces a deadline-aware estimate of study capacity. Earlier deadlines
/// consume capacity first; exams sharing a deadline split a shortage
/// proportionally. Both weekly and daily limits are respected. The result is a
/// proposal only: this service never creates or changes calendar events.
/// </summary>
public sealed class TimeBudgetService
{
    private const int MaximumWeeklyHours = 168;
    private const int MaximumDailyMinutes = 1440;

    public TimeBudgetPlan Calculate(StudyData data, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var start = (today ?? DateTime.Today).Date;
        var warnings = new List<string>();

        var weeklyHours = Math.Clamp(data.WeeklyStudyHours, 0, MaximumWeeklyHours);
        if (weeklyHours != data.WeeklyStudyHours)
            warnings.Add("Le ore settimanali fuori intervallo sono state limitate a 0–168.");
        var dailyMinutes = Math.Clamp(data.MaxDailyStudyMinutes, 0, MaximumDailyMinutes);
        if (dailyMinutes != data.MaxDailyStudyMinutes)
            warnings.Add("Il limite giornaliero fuori intervallo è stato limitato a 0–1440 minuti.");

        var weeklyMinutes = weeklyHours * 60;
        var exams = data.Exams
            .Where(x => !x.Completed && x.PlannedDate.HasValue && x.PlannedDate.Value.Date >= start)
            .OrderBy(x => x.PlannedDate!.Value.Date)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        if (exams.Count == 0)
        {
            warnings.Add("Nessun esame futuro con una data pianificata: il budget non può essere distribuito.");
            return new TimeBudgetPlan(weeklyMinutes, 0, 0, Array.Empty<StudyAllocation>(), warnings);
        }

        var negativeEstimateFound = false;
        var requirements = exams.Select(exam =>
        {
            var course = data.Courses.FirstOrDefault(x =>
                string.Equals(x.Id, exam.CourseId, StringComparison.Ordinal));
            var estimateHours = exam.EstimatedStudyHours > 0
                ? exam.EstimatedStudyHours
                : course?.EstimatedStudyHours ?? 0;
            if (estimateHours < 0)
            {
                negativeEstimateFound = true;
                estimateHours = 0;
            }

            var days = Math.Max(1, (exam.PlannedDate!.Value.Date - start).Days);
            var weeks = Math.Max(1, (int)Math.Ceiling(days / 7d));
            return new Requirement(exam, exam.PlannedDate.Value.Date, (long)estimateHours * 60, days, weeks);
        }).ToList();

        if (negativeEstimateFound)
            warnings.Add("Una o più stime negative sono state ignorate.");

        var required = requirements.Sum(x => x.Minutes);
        var latestDeadlineDays = requirements.Max(x => x.Days);
        var available = CapacityForDays(latestDeadlineDays, weeklyMinutes, dailyMinutes);
        if (required == 0)
            warnings.Add("Nessuna stima delle ore di studio è disponibile per gli esami pianificati.");
        if (required > available)
            warnings.Add($"Il carico stimato supera la capacità disponibile di {required - available} minuti.");
        if (weeklyMinutes == 0)
            warnings.Add("Le ore settimanali configurate sono zero: nessuna allocazione è possibile.");
        if (dailyMinutes == 0)
            warnings.Add("Il limite giornaliero configurato è zero: nessuna allocazione è possibile.");

        var allocatedItems = new List<(Requirement Item, long AllocatedMinutes)>(requirements.Count);
        long allocatedBeforeDeadline = 0;

        foreach (var deadlineGroup in requirements.GroupBy(x => x.Deadline).OrderBy(x => x.Key))
        {
            var items = deadlineGroup
                .OrderBy(x => x.Exam.Id, StringComparer.Ordinal)
                .ThenBy(x => x.Exam.Name, StringComparer.Ordinal)
                .ToList();
            var cumulativeCapacity = CapacityForDays(items[0].Days, weeklyMinutes, dailyMinutes);
            var capacityForGroup = Math.Max(0L, cumulativeCapacity - allocatedBeforeDeadline);
            var groupDemand = items.Sum(x => x.Minutes);
            var groupBudget = Math.Min(groupDemand, capacityForGroup);
            var shares = AllocateProportionally(items, groupBudget, groupDemand);

            for (var index = 0; index < items.Count; index++)
                allocatedItems.Add((items[index], shares[index]));

            allocatedBeforeDeadline += groupBudget;
        }

        var allocations = allocatedItems.Select(item =>
        {
            var allocated = item.AllocatedMinutes;
            var hasEstimate = item.Item.Minutes > 0;
            var isAtRisk = hasEstimate && allocated < item.Item.Minutes;
            var remaining = item.Item.Minutes - allocated;
            var explanation = !hasEstimate
                ? "Stima non disponibile: inserisci le ore previste per valutare il carico."
                : isAtRisk
                    ? $"Mancano {remaining} minuti per coprire la stima entro {item.Item.Weeks} settimane."
                    : "La stima è coperta entro la scadenza pianificata.";

            return new StudyAllocation(
                item.Item.Exam.CourseId,
                item.Item.Exam.Name,
                item.Item.Exam.PlannedDate,
                ToInt32Saturated(item.Item.Minutes),
                ToInt32Saturated(allocated),
                item.Item.Weeks,
                isAtRisk,
                explanation);
        }).ToList();

        if (required <= available && allocations.Any(x => x.IsAtRisk))
            warnings.Add("Una o più scadenze sono troppo ravvicinate: la capacità complessiva basta, ma non quella disponibile prima di ogni singola data.");

        return new TimeBudgetPlan(
            weeklyMinutes,
            ToInt32Saturated(available),
            ToInt32Saturated(required),
            allocations,
            warnings);
    }

    private static long CapacityForDays(int days, int weeklyMinutes, int dailyMinutes)
    {
        var weeklyCapacity = (long)weeklyMinutes * days / 7;
        var dailyCapacity = (long)dailyMinutes * days;
        return Math.Min(weeklyCapacity, dailyCapacity);
    }

    private static long[] AllocateProportionally(
        IReadOnlyList<Requirement> items,
        long budget,
        long totalDemand)
    {
        var allocations = new long[items.Count];
        if (budget <= 0 || totalDemand <= 0)
            return allocations;
        if (budget >= totalDemand)
        {
            for (var index = 0; index < items.Count; index++)
                allocations[index] = items[index].Minutes;
            return allocations;
        }

        var remainders = new List<(int Index, decimal Fraction, string Id)>(items.Count);
        long allocated = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var exactShare = (decimal)items[index].Minutes * budget / totalDemand;
            var wholeMinutes = (long)decimal.Floor(exactShare);
            allocations[index] = wholeMinutes;
            allocated += wholeMinutes;
            remainders.Add((index, exactShare - wholeMinutes, items[index].Exam.Id));
        }

        // Largest-remainder distribution makes equal-deadline shortages fair
        // while keeping the result deterministic across collection ordering.
        var remainingMinutes = (int)(budget - allocated);
        foreach (var remainder in remainders
                     .OrderByDescending(x => x.Fraction)
                     .ThenBy(x => x.Id, StringComparer.Ordinal)
                     .Take(remainingMinutes))
        {
            allocations[remainder.Index]++;
        }

        return allocations;
    }

    private static int ToInt32Saturated(long value) =>
        value >= int.MaxValue ? int.MaxValue : value <= 0 ? 0 : (int)value;

    private sealed class Requirement(
        Exam exam,
        DateTime deadline,
        long minutes,
        int days,
        int weeks)
    {
        public Exam Exam { get; } = exam;
        public DateTime Deadline { get; } = deadline;
        public long Minutes { get; } = minutes;
        public int Days { get; } = days;
        public int Weeks { get; } = weeks;
    }
}
