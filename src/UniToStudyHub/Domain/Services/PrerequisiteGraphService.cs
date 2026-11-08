namespace UniToStudyHub.Domain.Services;

/// <summary>
/// Directed graph of course prerequisites. An edge course -> prerequisite means
/// the prerequisite must be passed before the course. Cycles are rejected because
/// they make a degree plan impossible to satisfy.
/// </summary>
public sealed class PrerequisiteGraphService
{
    private readonly Dictionary<string, IReadOnlyList<string>> _prerequisites;

    public PrerequisiteGraphService(IEnumerable<(string CourseId, string PrerequisiteCourseId)> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (courseId, prerequisiteId) in edges)
        {
            if (string.IsNullOrWhiteSpace(courseId))
                throw new ArgumentException("A prerequisite edge has an empty course id.", nameof(edges));
            if (string.IsNullOrWhiteSpace(prerequisiteId))
                throw new ArgumentException("A prerequisite edge has an empty prerequisite id.", nameof(edges));
            if (string.Equals(courseId, prerequisiteId, StringComparison.Ordinal))
                throw new ArgumentException($"Course '{courseId}' cannot be its own prerequisite.", nameof(edges));
            if (!map.TryGetValue(courseId, out var set))
                map[courseId] = set = new HashSet<string>(StringComparer.Ordinal);
            if (!set.Add(prerequisiteId))
                throw new ArgumentException($"Duplicate prerequisite edge '{courseId}' -> '{prerequisiteId}'.", nameof(edges));
        }

        _prerequisites = map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var courseId in _prerequisites.Keys)
            Visit(courseId, state);
    }

    public IReadOnlyList<string> DirectPrerequisites(string courseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId);
        return _prerequisites.TryGetValue(courseId, out var prerequisites) ? prerequisites : Array.Empty<string>();
    }

    public IReadOnlyList<string> AllPrerequisites(string courseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(DirectPrerequisites(courseId));
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            foreach (var next in DirectPrerequisites(current)) stack.Push(next);
        }
        seen.Remove(courseId);
        return seen.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Returns courses in an order where every prerequisite appears before the
    /// course that requires it. Disconnected courses are included and sorted by id.
    /// </summary>
    public IReadOnlyList<string> TopologicalOrder(IEnumerable<string> allCourseIds)
    {
        ArgumentNullException.ThrowIfNull(allCourseIds);
        var ids = allCourseIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var result = new List<string>(ids.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            VisitOrder(id, visited, result, new HashSet<string>(StringComparer.Ordinal));
        return result;
    }

    private void Visit(string courseId, Dictionary<string, int> state)
    {
        if (state.TryGetValue(courseId, out var value))
        {
            if (value == 1)
                throw new ArgumentException($"Prerequisite cycle detected at course '{courseId}'.");
            if (value == 2) return;
        }

        state[courseId] = 1;
        if (_prerequisites.TryGetValue(courseId, out var prerequisites))
            foreach (var prerequisite in prerequisites)
                Visit(prerequisite, state);
        state[courseId] = 2;
    }

    private void VisitOrder(
        string courseId,
        HashSet<string> visited,
        List<string> result,
        HashSet<string> visiting)
    {
        if (visited.Contains(courseId)) return;
        if (!visiting.Add(courseId))
            throw new ArgumentException($"Prerequisite cycle detected at course '{courseId}'.");

        if (_prerequisites.TryGetValue(courseId, out var prerequisites))
            foreach (var prerequisite in prerequisites)
                VisitOrder(prerequisite, visited, result, visiting);

        visiting.Remove(courseId);
        visited.Add(courseId);
        result.Add(courseId);
    }
}
