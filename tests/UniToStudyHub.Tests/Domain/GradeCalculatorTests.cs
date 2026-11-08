using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class GradeCalculatorTests
{
    private static Grade Grade(string courseId, int value, int cfu, DateTime? date = null, bool honors = false) =>
        new()
        {
            Id = $"{courseId}-{value}-{date?.Ticks ?? 0}",
            CourseId = courseId,
            CourseName = $"Corso {courseId}",
            Value = value,
            Cfu = cfu,
            Honors = honors,
            Date = date ?? new DateTime(2026, 1, 1)
        };

    [Fact]
    public void WeightedAverageAndBaseMatchTheDomainService()
    {
        var grades = new[] { Grade("c1", 24, 6), Grade("c2", 30, 12) };

        Assert.Equal(28m, GradeCalculator.WeightedAverage(grades));
        Assert.Equal(102.6667m, GradeCalculator.ProjectedBase(grades));
        Assert.Equal(18, GradeCalculator.AcquiredCfu(grades));
    }

    [Fact]
    public void RepeatedGradeRowsForTheSameCourseCountOnce()
    {
        var grades = new[]
        {
            Grade("c1", 22, 6, new DateTime(2026, 1, 1)),
            Grade("c1", 27, 6, new DateTime(2026, 3, 1))
        };

        Assert.Equal(27m, GradeCalculator.WeightedAverage(grades));
        Assert.Equal(6, GradeCalculator.AcquiredCfu(grades));
    }

    [Fact]
    public void SimulateManyReplacesAndAddsWithoutMutatingTheOriginalCollection()
    {
        var original = new List<Grade> { Grade("c1", 20, 6) };
        var result = GradeCalculator.SimulateMany(
            original,
            new[]
            {
                new GradeSimulationInput("c1", "Corso c1", 6, 30),
                new GradeSimulationInput("c2", "Corso c2", 6, 28)
            });

        Assert.Equal(20m, result.CurrentAverage);
        Assert.Equal(29m, result.SimulatedAverage);
        Assert.Equal(9m, result.AverageDelta);
        Assert.Equal(6, GradeCalculator.AcquiredCfu(original));
        Assert.Single(original);
        Assert.Equal(20, original[0].Value);
    }

    [Fact]
    public void SimulateManyRejectsCfuMismatchForAnExistingCourse()
    {
        var grades = new[] { Grade("c1", 20, 6) };

        Assert.Throws<ArgumentException>(() => GradeCalculator.SimulateMany(
            grades,
            new[] { new GradeSimulationInput("c1", "Corso c1", 9, 30) }));
    }

    [Fact]
    public void NoValidGradeReturnsNullableAverageAndBase()
    {
        var grades = Array.Empty<Grade>();

        Assert.Null(GradeCalculator.WeightedAverage(grades));
        Assert.Null(GradeCalculator.ProjectedBase(grades));
        Assert.Equal(0, GradeCalculator.AcquiredCfu(grades));

        var simulation = GradeCalculator.SimulateMany(
            grades,
            new[] { new GradeSimulationInput("c1", "Corso c1", 6, 28) });
        Assert.Null(simulation.CurrentAverage);
        Assert.Equal(28m, simulation.SimulatedAverage);
        Assert.Null(simulation.AverageDelta);
    }

    [Fact]
    public void LegacySimulateReturnsHypotheticalAverage()
    {
        var grades = new[] { Grade("c1", 20, 6) };

        var average = GradeCalculator.Simulate(
            grades,
            new Course { Id = "c1", Name = "Corso c1", Cfu = 6 },
            30,
            honors: false);

        Assert.Equal(30m, average);
    }

    [Fact]
    public void HonorsAndInvalidRowsAreHandledConsistently()
    {
        var grades = new[]
        {
            Grade("c1", 30, 6, honors: true),
            Grade("c2", 24, 6),
            Grade("c3", 18, 0)
        };

        Assert.Equal(27m, GradeCalculator.WeightedAverage(grades));
        Assert.Equal(12, GradeCalculator.AcquiredCfu(grades));
    }

    [Fact]
    public void FormatUsesTwoDecimalsAndDashForNull()
    {
        Assert.Equal("—", GradeCalculator.Format(null));
        Assert.Equal("27.33", GradeCalculator.Format(27.3333m));
    }
}
