using UniToStudyHub.Domain.Services;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class PrerequisiteGraphServiceTests
{
    [Fact]
    public void TopologicalOrderPlacesPrerequisitesBeforeDependants()
    {
        var graph = new PrerequisiteGraphService(new[]
        {
            ("analisi", "matematica-base"),
            ("fisica", "analisi"),
            ("fisica", "matematica-base")
        });

        var order = graph.TopologicalOrder(new[] { "fisica", "analisi", "matematica-base" }).ToList();

        Assert.True(order.IndexOf("matematica-base") < order.IndexOf("analisi"));
        Assert.True(order.IndexOf("analisi") < order.IndexOf("fisica"));
        Assert.Equal(new[] { "analisi", "matematica-base" }, graph.AllPrerequisites("fisica")
            .OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DirectAndClosureQueriesAreDistinct()
    {
        var graph = new PrerequisiteGraphService(new[]
        {
            ("c", "b"),
            ("b", "a")
        });

        Assert.Equal(new[] { "b" }, graph.DirectPrerequisites("c"));
        Assert.Equal(new[] { "a", "b" }, graph.AllPrerequisites("c"));
        Assert.Empty(graph.AllPrerequisites("a"));
    }

    [Fact]
    public void SelfEdgeAndCycleAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new PrerequisiteGraphService(new[] { ("a", "a") }));

        Assert.Throws<ArgumentException>(() =>
            new PrerequisiteGraphService(new[]
            {
                ("a", "b"),
                ("b", "c"),
                ("c", "a")
            }));
    }

    [Fact]
    public void DuplicateEdgeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new PrerequisiteGraphService(new[]
            {
                ("a", "b"),
                ("a", "b")
            }));
    }
}
