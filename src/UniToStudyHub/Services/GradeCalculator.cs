using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record GradeSimulationInput(
    string CourseId,
    string CourseName,
    int Cfu,
    int Value,
    bool Honors = false,
    bool CountsTowardAverage = true);

public sealed record GradeSimulationResult(
    decimal? CurrentAverage,
    decimal? SimulatedAverage,
    decimal? AverageDelta,
    decimal? CurrentBaseGrade,
    decimal? SimulatedBaseGrade,
    decimal? BaseGradeDelta);

public static class GradeCalculator
{
    // Legacy Grade rows are attempts. Only the most recent attempt for each course
    // represents the current recorded grade, so repeated exams cannot be counted twice.
    public static decimal? WeightedAverage(IEnumerable<Grade> grades)
    {
        ArgumentNullException.ThrowIfNull(grades);
        var latest = LatestPerCourse(grades)
            .Where(g => g.CountsTowardAverage && g.Cfu > 0 && g.Value is >= 18 and <= 30)
            .ToList();
        var totalCfu = latest.Sum(g => g.Cfu);
        return totalCfu == 0
            ? null
            : latest.Sum(g => (decimal)g.Value * g.Cfu) / totalCfu;
    }

    public static int AcquiredCfu(IEnumerable<Grade> grades)
    {
        ArgumentNullException.ThrowIfNull(grades);
        return LatestPerCourse(grades).Where(g => g.Cfu > 0).Sum(g => g.Cfu);
    }

    public static decimal? ProjectedBase(IEnumerable<Grade> grades)
    {
        var average = WeightedAverage(grades);
        return average is null ? null : average.Value * 110m / 30m;
    }

    public static decimal? Simulate(IEnumerable<Grade> grades, Course course, int hypotheticalGrade, bool honors)
    {
        var result = SimulateMany(grades, new[]
        {
            new GradeSimulationInput(course.Id, course.Name, course.Cfu, hypotheticalGrade, honors)
        });
        return result.SimulatedAverage;
    }

    // A scenario replaces the latest grade of an existing course. It never mutates
    // the supplied collection and supports multiple distinct courses in one run.
    public static GradeSimulationResult SimulateMany(IEnumerable<Grade> grades, IEnumerable<GradeSimulationInput> scenario)
    {
        ArgumentNullException.ThrowIfNull(grades);
        ArgumentNullException.ThrowIfNull(scenario);
        var original = grades.ToList();
        var changes = scenario.ToList();

        if (changes.GroupBy(x => x.CourseId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("A course can be simulated only once per scenario.", nameof(scenario));

        foreach (var item in changes)
        {
            if (string.IsNullOrWhiteSpace(item.CourseId) || string.IsNullOrWhiteSpace(item.CourseName))
                throw new ArgumentException("Every simulated course requires an id and a name.", nameof(scenario));
            if (item.Cfu <= 0) throw new ArgumentOutOfRangeException(nameof(scenario), "CFU must be positive.");
            if (item.Value is < 18 or > 30) throw new ArgumentOutOfRangeException(nameof(scenario), "Hypothetical grades must be between 18 and 30.");
            if (item.Honors && item.Value != 30) throw new ArgumentException("Honors can only be assigned to a grade of 30.", nameof(scenario));
        }

        var simulated = original.ToList();
        foreach (var item in changes)
        {
            simulated.RemoveAll(g => string.Equals(g.CourseId, item.CourseId, StringComparison.Ordinal));
            simulated.Add(new Grade
            {
                Id = $"whatif-{item.CourseId}",
                CourseId = item.CourseId,
                CourseName = item.CourseName,
                Cfu = item.Cfu,
                Value = item.Value,
                Honors = item.Honors,
                Date = DateTime.MaxValue,
                CountsTowardAverage = item.CountsTowardAverage
            });
        }

        var currentAverage = WeightedAverage(original);
        var simulatedAverage = WeightedAverage(simulated);
        var currentBase = currentAverage is null ? null : currentAverage.Value * 110m / 30m;
        var simulatedBase = simulatedAverage is null ? null : simulatedAverage.Value * 110m / 30m;
        return new GradeSimulationResult(
            currentAverage,
            simulatedAverage,
            currentAverage is null || simulatedAverage is null ? null : simulatedAverage - currentAverage,
            currentBase,
            simulatedBase,
            currentBase is null || simulatedBase is null ? null : simulatedBase - currentBase);
    }

    public static string Format(decimal? value, string suffix = "") =>
        value is null ? "—" : value.Value.ToString("0.00") + suffix;

    private static IEnumerable<Grade> LatestPerCourse(IEnumerable<Grade> grades) =>
        grades.GroupBy(g => string.IsNullOrWhiteSpace(g.CourseId) ? $"grade:{g.Id}" : g.CourseId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(g => g.Date)
                .ThenByDescending(g => g.Id, StringComparer.Ordinal)
                .First());
}