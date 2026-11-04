using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class GradeCalculatorTests
{
    [Fact]
    public void WeightedAverage_UsesOnlyLatestGradeForEachCourse()
    {
        var grades = new[]
        {
            new Grade { Id = "old", CourseId = "c1", CourseName = "Analisi", Value = 18, Cfu = 6, Date = new DateTime(2026, 1, 1) },
            new Grade { Id = "new", CourseId = "c1", CourseName = "Analisi", Value = 30, Cfu = 6, Date = new DateTime(2026, 2, 1) },
            new Grade { Id = "physics", CourseId = "c2", CourseName = "Fisica", Value = 24, Cfu = 12, Date = new DateTime(2026, 1, 1) }
        };

        Assert.Equal(26m, GradeCalculator.WeightedAverage(grades));
        Assert.Equal(18, GradeCalculator.AcquiredCfu(grades));
    }

    [Fact]
    public void SimulateMany_DoesNotMutateOriginalGrades()
    {
        var grade = new Grade { Id = "g1", CourseId = "c1", CourseName = "Analisi", Value = 24, Cfu = 6 };
        var source = new List<Grade> { grade };
        var result = GradeCalculator.SimulateMany(source, new[]
        {
            new GradeSimulationInput("c1", "Analisi", 6, 29),
            new GradeSimulationInput("c2", "Fisica", 12, 27)
        });

        Assert.Equal(24, grade.Value);
        Assert.Single(source);
        Assert.Equal(27.666666666666666666666666667m, result.SimulatedAverage);
    }
}