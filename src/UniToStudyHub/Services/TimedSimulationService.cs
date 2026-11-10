namespace UniToStudyHub.Services;

/// <summary>
/// Clock-independent rules for a timed exam simulation. The absolute deadline
/// is authoritative; UI refresh ticks are only for display and never decide
/// whether a submitted answer is accepted.
/// </summary>
public sealed class TimedSimulationService
{
    public DateTimeOffset? CreateDeadline(DateTimeOffset startedAt, int limitMinutes)
    {
        if (limitMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(limitMinutes), "The time limit must be between 0 and 1440 minutes.");
        return limitMinutes == 0 ? null : startedAt.AddMinutes(limitMinutes);
    }

    public bool IsExpired(DateTimeOffset? deadline, DateTimeOffset now) =>
        deadline is DateTimeOffset value && now >= value;

    public int GetRemainingSeconds(DateTimeOffset? deadline, DateTimeOffset now) =>
        deadline is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((deadline.Value - now).TotalSeconds));

    public string FormatCountdown(DateTimeOffset? deadline, DateTimeOffset now)
    {
        if (deadline is null) return "Senza limite";
        if (IsExpired(deadline, now)) return "Tempo scaduto";

        var seconds = GetRemainingSeconds(deadline, now);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
