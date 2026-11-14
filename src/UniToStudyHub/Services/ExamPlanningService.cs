using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Application use case that keeps algorithmic exam proposals and their
/// calendar representation in sync without presenting proposals as official
/// university exam sessions.
/// </summary>
public sealed class ExamPlanningService
{
    private readonly CalendarApplicationService _calendar;

    public ExamPlanningService(CalendarApplicationService calendar)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    public IReadOnlyList<CalendarEvent> PlanAndLink(
        StudyData data,
        DateTime? referenceDate = null,
        DateTime? targetDate = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ExamPlanner.AssignIndicativeSessions(data.Exams, referenceDate, targetDate);
        return _calendar.LinkPlannedExams(data, now);
    }
}
