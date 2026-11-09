using System.Globalization;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record DashboardDeadlineItem(
    string Id,
    string Title,
    string CourseName,
    DateTimeOffset DueAt,
    bool IsAllDay,
    bool IsTask,
    string Source,
    string Verification)
{
    public string DueDisplay
    {
        get
        {
            var format = IsAllDay ? "ddd d MMM" : "ddd d MMM · HH:mm";
            return DueAt.ToLocalTime().ToString(format, CultureInfo.GetCultureInfo("it-IT"));
        }
    }

    public string SourceDisplay => IsTask
        ? "Attività"
        : Source switch
        {
            CalendarEventSource.Moodle => "Moodle",
            CalendarEventSource.Ics => "Calendario importato",
            CalendarEventSource.Planner => "Pianificatore",
            _ => "Personale"
        };

    public string VerificationDisplay => IsTask
        ? "Da completare"
        : Verification switch
        {
            CalendarEventVerification.Official => "Fonte ufficiale",
            CalendarEventVerification.Indicative => "Data indicativa",
            _ => "Non verificata"
        };
}

public sealed record DashboardStudySuggestion(
    string Kind,
    string Title,
    string Reason,
    string CourseId = "",
    string TaskId = "");

public sealed record DashboardOverview(
    Exam? NextExam,
    int? DaysUntilNextExam,
    IReadOnlyList<StudyTaskData> TodayTasks,
    IReadOnlyList<DashboardDeadlineItem> UpcomingDeadlines,
    int RecordedStudyMinutes,
    int WeeklyGoalMinutes,
    int WeeklyGoalProgressPercent,
    DashboardStudySuggestion Suggestion)
{
    public bool HasWeeklyGoal => WeeklyGoalMinutes > 0;
    public double RecordedStudyHours => RecordedStudyMinutes / 60d;
    public double WeeklyGoalHours => WeeklyGoalMinutes / 60d;
}

/// <summary>
/// Builds a read-only, deterministic overview for the Today dashboard.
/// All values come from the local StudyData snapshot; no network or AI provider
/// is involved. An injected instant makes date boundaries testable.
/// </summary>
public sealed class DashboardOverviewService(ProgressService progress)
{
    private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

    public DashboardOverview Calculate(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(progress);

        var instant = now ?? DateTimeOffset.Now;
        var localNow = instant.ToLocalTime();
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var lastDeadlineDay = today.AddDays(14);
        var weekStart = today.AddDays(-(((int)localNow.DayOfWeek + 6) % 7));

        var nextExam = data.Exams
            .Where(exam => !exam.Completed && exam.PlannedDate.HasValue &&
                           DateOnly.FromDateTime(exam.PlannedDate.Value.Date) >= today)
            .OrderBy(exam => exam.PlannedDate!.Value.Date)
            .ThenBy(exam => exam.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(exam => exam.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var daysUntilExam = nextExam?.PlannedDate is { } examDate
            ? DateOnly.FromDateTime(examDate.Date).DayNumber - today.DayNumber
            : null;

        var activeTasks = data.StudyTasks
            .Where(task => task.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress)
            .ToList();

        var todayTasks = activeTasks
            .Where(task => task.DueAt is { } dueAt && LocalDate(dueAt) <= today)
            .OrderBy(task => task.DueAt)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .ToList();

        var deadlines = new List<DashboardDeadlineItem>();
        var coursesById = data.Courses
            .GroupBy(course => course.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var task in activeTasks.Where(task =>
                     task.DueAt is { } dueAt &&
                     LocalDate(dueAt) >= today &&
                     LocalDate(dueAt) <= lastDeadlineDay))
        {
            var courseName = coursesById.TryGetValue(task.CourseId, out var course)
                ? course.Name
                : string.Empty;
            deadlines.Add(new DashboardDeadlineItem(
                task.Id,
                task.Title,
                courseName,
                task.DueAt!.Value,
                false,
                true,
                "Task",
                "Pending"));
        }

        foreach (var calendarEvent in data.CalendarEvents.Where(item =>
                     !item.IsCancelled &&
                     string.Equals(item.Category, CalendarEventCategory.Deadline, StringComparison.Ordinal) &&
                     LocalDate(item.StartsAt) >= today &&
                     LocalDate(item.StartsAt) <= lastDeadlineDay))
        {
            deadlines.Add(new DashboardDeadlineItem(
                calendarEvent.Id,
                calendarEvent.Title,
                calendarEvent.CourseName,
                calendarEvent.StartsAt,
                calendarEvent.IsAllDay,
                false,
                calendarEvent.Source,
                calendarEvent.Verification));
        }

        var upcomingDeadlines = deadlines
            .OrderBy(item => item.DueAt)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var weeklyProgress = progress.Calculate(data, weekStart, today, instant);
        var weeklyGoalMinutes = Math.Max(0, weeklyProgress.WeeklyGoalMinutes);
        var recordedMinutes = Math.Max(0, weeklyProgress.RecordedMinutes);
        var goalPercent = weeklyGoalMinutes == 0
            ? 0
            : Math.Clamp((int)Math.Round(recordedMinutes * 100d / weeklyGoalMinutes), 0, 100);

        var suggestion = BuildSuggestion(data, activeTasks, nextExam, today, instant, coursesById);

        return new DashboardOverview(
            nextExam,
            daysUntilExam,
            todayTasks,
            upcomingDeadlines,
            recordedMinutes,
            weeklyGoalMinutes,
            goalPercent,
            suggestion);
    }

    private static DashboardStudySuggestion BuildSuggestion(
        StudyData data,
        IReadOnlyCollection<StudyTaskData> activeTasks,
        Exam? nextExam,
        DateOnly today,
        DateTimeOffset now,
        IReadOnlyDictionary<string, Course> coursesById)
    {
        var nextTask = activeTasks
            .OrderBy(task => TaskUrgency(task.DueAt, today))
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (nextTask is not null)
        {
            var courseName = coursesById.TryGetValue(nextTask.CourseId, out var course)
                ? course.Name
                : "la materia collegata";
            var reason = nextTask.DueAt is { } dueAt
                ? LocalDate(dueAt) < today
                    ? $"È in ritardo e ha priorità {nextTask.Priority}/5."
                    : LocalDate(dueAt) == today
                        ? $"Scade oggi e ha priorità {nextTask.Priority}/5."
                        : $"È la prossima attività in scadenza e ha priorità {nextTask.Priority}/5."
                : $"È un'attività aperta di {courseName} con priorità {nextTask.Priority}/5.";

            return new DashboardStudySuggestion(
                "task",
                $"Porta avanti: {nextTask.Title}",
                reason,
                nextTask.CourseId,
                nextTask.Id);
        }

        var dueCards = data.Flashcards
            .Where(card => !card.IsSuspended &&
                           card.DueAt is { } dueAt &&
                           LocalDate(dueAt) <= today)
            .ToList();

        if (dueCards.Count > 0)
        {
            var mostRelevantCourse = dueCards
                .GroupBy(card => card.CourseId, StringComparer.Ordinal)
                .Select(group => new
                {
                    CourseId = group.Key,
                    Count = group.Count(),
                    Name = coursesById.TryGetValue(group.Key, out var course) ? course.Name : string.Empty
                })
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.CourseId, StringComparer.Ordinal)
                .First();

            var subject = string.IsNullOrWhiteSpace(mostRelevantCourse.Name)
                ? "le flashcard in scadenza"
                : $"le flashcard di {mostRelevantCourse.Name}";
            return new DashboardStudySuggestion(
                "flashcards",
                $"Ripassa {subject}",
                $"{mostRelevantCourse.Count} schede risultano da ripassare secondo il calendario di ripetizione.",
                mostRelevantCourse.CourseId);
        }

        if (nextExam is not null)
        {
            var courseName = string.IsNullOrWhiteSpace(nextExam.CourseName)
                ? nextExam.Name
                : nextExam.CourseName;
            var days = DateOnly.FromDateTime(nextExam.PlannedDate!.Value.Date).DayNumber - today.DayNumber;
            var distance = days switch
            {
                0 => "è previsto oggi",
                1 => "è previsto domani",
                _ => $"è previsto tra {days} giorni"
            };
            return new DashboardStudySuggestion(
                "exam",
                $"Dedica un blocco a {courseName}",
                $"È il prossimo esame pianificato e {distance}. La data è quella salvata nel planner, non una verifica dell'appello ufficiale.",
                nextExam.CourseId);
        }

        return new DashboardStudySuggestion(
            "setup",
            "Organizza il prossimo passo",
            "Non risultano attività aperte, ripassi dovuti o esami futuri. Aggiungi un'attività o registra una data per ricevere un suggerimento contestuale.");
    }

    private static int TaskUrgency(DateTimeOffset? dueAt, DateOnly today)
    {
        if (dueAt is null) return 3;
        var dueDay = LocalDate(dueAt.Value);
        if (dueDay < today) return 0;
        if (dueDay == today) return 1;
        return dueDay <= today.AddDays(7) ? 2 : 4;
    }

    private static DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToLocalTime().DateTime);
}
