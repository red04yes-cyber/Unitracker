using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record DailyStudyMetric(
    DateOnly Day,
    int RecordedMinutes,
    int PlannedMinutes,
    int CompletedPlannerMinutes,
    int CompletedSessions);

public sealed record CourseStudyMetric(
    string CourseId,
    string CourseName,
    int RecordedMinutes,
    int PlannedMinutes,
    int ActualPlannerMinutes,
    int CompletedSessions,
    int InterruptedSessions,
    int SyllabusTopics,
    int CompletedSyllabusTopics,
    int EstimatedRemainingMinutes);

public sealed record StudyAnalyticsSnapshot(
    DateOnly From,
    DateOnly To,
    int RecordedMinutes,
    int PlannedMinutes,
    int ActualPlannerMinutes,
    int CompletedSessions,
    int InterruptedSessions,
    int CompletedTasks,
    int CompletedMissions,
    int DueFlashcards,
    int OpenErrors,
    int StudyDays,
    int ElapsedDays,
    int WeeklyGoalMinutes,
    IReadOnlyList<DailyStudyMetric> Daily,
    IReadOnlyList<CourseStudyMetric> Courses)
{
    public decimal ConsistencyPercent => ElapsedDays == 0
        ? 0
        : Math.Round((decimal)StudyDays / ElapsedDays * 100, 1);

    public int PlannedVsActualMinutes => ActualPlannerMinutes - PlannedMinutes;
}

public sealed record StudyAnalyticsOverview(
    StudyAnalyticsSnapshot Today,
    StudyAnalyticsSnapshot Week,
    StudyAnalyticsSnapshot Month);

/// <summary>
/// Analytics computed only from persisted ledger, calendar, sessions, tasks,
/// missions and syllabus data. Planned and actual time are deliberately kept
/// separate; the service never treats an estimate as recorded study.
/// </summary>
public sealed class StudyAnalyticsService
{
    public StudyAnalyticsOverview Calculate(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(at.LocalDateTime);
        var weekStart = DateOnly.FromDateTime(CalendarService.StartOfWeek(today.ToDateTime(TimeOnly.MinValue)));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return new StudyAnalyticsOverview(
            CalculateRange(data, today, today, at),
            CalculateRange(data, weekStart, weekStart.AddDays(6), at),
            CalculateRange(data, monthStart, monthEnd, at));
    }

    public StudyAnalyticsSnapshot CalculateRange(
        StudyData data,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start.");
        var at = now ?? DateTimeOffset.Now;
        var progress = new ProgressService().Calculate(data, from, to, at);
        var plannerEvents = data.CalendarEvents
            .Where(x => !x.IsCancelled && !string.IsNullOrWhiteSpace(x.PlanId))
            .Select(x => (Event: x, Day: DateOnly.FromDateTime(x.StartsAt.LocalDateTime)))
            .Where(x => x.Day >= from && x.Day <= to)
            .ToList();
        var sessions = data.StudySessions
            .Where(x => InRange(DateOnly.FromDateTime(x.StartedAt.LocalDateTime), from, to))
            .ToList();
        var missions = data.StudyMissions.Where(x =>
            x.CompletedAt is { } completedAt &&
            InRange(DateOnly.FromDateTime(completedAt.LocalDateTime), from, to)).ToList();
        var elapsedEnd = to < DateOnly.FromDateTime(at.LocalDateTime)
            ? to
            : DateOnly.FromDateTime(at.LocalDateTime);
        var elapsedDays = elapsedEnd < from ? 0 : elapsedEnd.DayNumber - from.DayNumber + 1;
        var daily = progress.Daily
            .Select(day =>
            {
                var planner = plannerEvents.Where(x => x.Day == day.Day).ToList();
                return new DailyStudyMetric(
                    day.Day,
                    day.RecordedSeconds / 60,
                    planner.Sum(x => Minutes(x.Event)),
                    planner.Where(x => x.Event.PlannerStatus == PlannedSessionStatus.Completed)
                        .Sum(x => x.Event.ActualMinutes),
                    day.CompletedSessions);
            })
            .ToList();
        var courses = data.Courses
            .Where(x => !x.Archived)
            .Select(course =>
            {
                var courseProgress = progress.Courses.FirstOrDefault(x =>
                    string.Equals(x.CourseId, course.Id, StringComparison.Ordinal));
                var planned = plannerEvents
                    .Where(x => string.Equals(x.Event.CourseId, course.Id, StringComparison.Ordinal))
                    .ToList();
                var courseSessions = sessions
                    .Where(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal))
                    .ToList();
                var topics = data.SyllabusTopics
                    .Where(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) && !x.IsArchived)
                    .ToList();
                return new CourseStudyMetric(
                    course.Id,
                    course.Name,
                    (courseProgress?.RecordedSeconds ?? 0) / 60,
                    planned.Sum(x => Minutes(x.Event)),
                    planned.Where(x => x.Event.PlannerStatus == PlannedSessionStatus.Completed)
                        .Sum(x => x.Event.ActualMinutes),
                    courseSessions.Count(x => x.State == StudySessionState.Completed),
                    courseSessions.Count(x => x.State == StudySessionState.Cancelled),
                    topics.Count,
                    topics.Count(x => x.Status == Domain.Enums.SyllabusTopicStatus.Completed),
                    topics.Where(x => x.Status != Domain.Enums.SyllabusTopicStatus.Completed)
                        .Sum(x => x.EstimatedMinutes));
            })
            .OrderBy(x => x.CourseName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new StudyAnalyticsSnapshot(
            from,
            to,
            progress.RecordedMinutes,
            plannerEvents.Sum(x => Minutes(x.Event)),
            plannerEvents.Where(x => x.Event.PlannerStatus == PlannedSessionStatus.Completed)
                .Sum(x => x.Event.ActualMinutes),
            sessions.Count(x => x.State == StudySessionState.Completed),
            sessions.Count(x => x.State == StudySessionState.Cancelled),
            progress.CompletedTasks,
            missions.Count,
            progress.DueFlashcards,
            progress.OpenErrors,
            daily.Count(x => x.RecordedMinutes > 0),
            elapsedDays,
            progress.WeeklyGoalMinutes,
            daily,
            courses);
    }

    private static bool InRange(DateOnly value, DateOnly from, DateOnly to) => value >= from && value <= to;

    private static int Minutes(CalendarEvent item) =>
        Math.Max(0, (int)Math.Floor((item.EndsAt - item.StartsAt).TotalMinutes));
}
