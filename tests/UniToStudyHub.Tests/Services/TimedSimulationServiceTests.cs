using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class TimedSimulationServiceTests
{
    private readonly TimedSimulationService _service = new();

    [Fact]
    public void ZeroMinutesMeansNoDeadline()
    {
        var started = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

        Assert.Null(_service.CreateDeadline(started, 0));
        Assert.False(_service.IsExpired(null, started.AddDays(1)));
        Assert.Equal("Senza limite", _service.FormatCountdown(null, started));
    }

    [Fact]
    public void DeadlineIsCreatedFromTheStartTimestamp()
    {
        var started = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

        var deadline = _service.CreateDeadline(started, 30);

        Assert.Equal(started.AddMinutes(30), deadline);
        Assert.False(_service.IsExpired(deadline, started.AddMinutes(29)));
        Assert.Equal("01:00", _service.FormatCountdown(deadline, started.AddMinutes(29)));
    }

    [Fact]
    public void DeadlineExpiresAtTheExactBoundary()
    {
        var started = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var deadline = _service.CreateDeadline(started, 10)!.Value;

        Assert.False(_service.IsExpired(deadline, deadline.AddTicks(-1)));
        Assert.True(_service.IsExpired(deadline, deadline));
        Assert.Equal(0, _service.GetRemainingSeconds(deadline, deadline));
        Assert.Equal("Tempo scaduto", _service.FormatCountdown(deadline, deadline));
    }

    [Fact]
    public void RemainingSecondsRoundUpForDisplay()
    {
        var now = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var deadline = now.AddSeconds(60).AddMilliseconds(1);

        Assert.Equal(61, _service.GetRemainingSeconds(deadline, now));
        Assert.Equal("01:01", _service.FormatCountdown(deadline, now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void RejectsOutOfRangeLimits(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.CreateDeadline(DateTimeOffset.UtcNow, minutes));
    }
}
