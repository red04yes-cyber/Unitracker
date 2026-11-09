using System.Globalization;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record DashboardExamItem(
    string CourseId,
    string Title,
    string CourseName,
    string Subtitle,
    string DateText,
    string CountdownText,
    string VerificationLabel,
    bool IsOfficial,
    int DaysRemaining);

public sealed record DashboardTaskItem(
    string Id,
    string Title,
    string Subtitle,
    string TypeLabel,
    string DueLabel,
    DateTimeOffset? DueAt,
    int Priority,
    int EstimatedMinutes,
    bool IsOverdue,
    bool CanComplete);

public sealed record DashboardDeadlineItem(
    string Id,
    string Title,
    string Context,
    string TypeLabel,
    string DueLabel,
    DateTimeOffset DueAt,
    bool IsOverdue);

public sealed record DashboardRecommendation(
    string Title,
    string CourseId,
    string CourseName,
    string Reason,
    string PriorityLabel,
    int DurationMinutes)
{
    public string DurationText => DurationMinutes + " minuti";
}

public sealed record DashboardSnapshot(
    DateOnly Today,
    IReadOnlyList<DashboardExamItem> Exams,
    IReadOnlyList<DashboardTaskItem> Tasks,
    IReadOnlyList<DashboardDeadlineItem> Deadlines,
    int WeeklyRecordedSeconds,
    int WeeklyGoalSeconds,
    int WeeklyStudyProgressPercent,
    int TodayCompletedTasks,
    int WeeklyMissionCompletedSteps,
    int WeeklyMissionTotalSteps,
    DashboardRecommendation Recommendation)
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public string TodayDateDisplay => Today.ToDateTime(TimeOnly.MinValue)
        .ToString("dddd d MMMM yyyy", Italian);

    public string NextExamTitle => Exams.Count > 0 ? Exams[0].Title : "Nessun esame in programma";
    public string NextExamCountdown => Exams.Count > 0 ? Exams[0].CountdownText : "Aggiungi o importa un appello";
    public string NextExamSubtitle => Exams.Count > 0 ? Exams[0].Subtitle : "Le date ufficiali e quelle indicative sono distinte";

    public string WeeklyStudyDisplay => FormatDuration(WeeklyRecordedSeconds);
    public string WeeklyGoalDisplay => WeeklyGoalSeconds > 0
        ? FormatDuration(WeeklyRecordedSeconds) + " / " + FormatDuration(WeeklyGoalSeconds)
        : "Obiettivo settimanale non configurato";

    public string TodayTaskSummary => Tasks.Count == 0
        ? TodayCompletedTasks + " completate oggi · nessuna attività in scadenza"
        : Tasks.Count + " da completare · " + TodayCompletedTasks + " completate oggi";

    public string WeeklyMissionDisplay => WeeklyMissionTotalSteps == 0
        ? "Nessuna missione settimanale"
        : WeeklyMissionCompletedSteps + " / " + WeeklyMissionTotalSteps + " passi completati";

    public static DashboardSnapshot Empty => new(
        DateOnly.FromDateTime(DateTime.Today),
        Array.Empty<DashboardExamItem>(),
        Array.Empty<DashboardTaskItem>(),
        Array.Empty<DashboardDeadlineItem>(),
        0,
        0,
        0,
        0,
        0,
        0,
        new DashboardRecommendation(
            "Organizza il prossimo passo",
            string.Empty,
            string.Empty,
            "Aggiungi un esame, un'attività o un argomento del programma per ricevere un suggerimento basato sui tuoi dati.",
            "Normale",
            25));

    private static string FormatDuration(int seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        var hours = minutes / 60;
        return hours > 0 ? hours + " h " + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + " min"
            : minutes + " min";
    }
}

/// <summary>
/// Builds the six dashboard widgets from the shared StudyData snapshot.
/// It is read-only: it never creates dates, tasks, activities or study plans.
/// </summary>
public sealed class DashboardService
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly ActivityLedgerService _ledger = new();

    public DashboardSnapshot Build(
        StudyData data,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var zone = ResolveTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(at, zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var todayEnd = StartOfDay(today.AddDays(1), zone);
        var weekStart = MondayOf(today);
        var weekEnd = weekStart.AddDays(6);

        var examItems = BuildExams(data, today, zone);
        var taskItems = BuildTodayTasks(data, today, todayEnd, at, zone);
        var deadlineItems = BuildDeadlines(data, today, todayEnd, at, zone);
        var weekly = _ledger.Summarize(data, weekStart, weekEnd, at, zone.Id);
        var weeklyGoalHours = Math.Clamp(data.WeeklyStudyHours, 0, 168);
        var weeklyGoalSeconds = weeklyGoalHours * 3600;
        var progress = weeklyGoalSeconds == 0
            ? 0
            : Math.Clamp((int)Math.Round(weekly.RecordedSeconds * 100d / weeklyGoalSeconds), 0, 100);

        var completedToday = data.StudyTasks.Count(x =>
                x.Status == StudyTaskStatus.Completed &&
                x.CompletedAt is { } completedAt &&
                LocalDate(completedAt, zone) == today)
            + data.StudyMissions.Count(x =>
                x.Status == StudyMissionStatus.Completed &&
                x.CompletedAt is { } completedAt &&
                LocalDate(completedAt, zone) == today);

        var weeklyMissions = data.StudyMissions
            .Where(x => x.Period == StudyMissionPeriod.Weekly &&
                        x.Status != StudyMissionStatus.Cancelled &&
                        IsInRange(LocalDate(x.StartsAt, zone), weekStart, weekEnd))
            .ToList();
        var missionSteps = weeklyMissions.Sum(x => x.Steps.Count);
        var completedMissionSteps = weeklyMissions.Sum(x => x.Steps.Count(step => step.IsCompleted));

        var recommendation = BuildRecommendation(
            data,
            today,
            at,
            zone,
            examItems,
            taskItems,
            weekly.ByCourse);

        return new DashboardSnapshot(
            today,
            examItems,
            taskItems,
            deadlineItems,
            weekly.RecordedSeconds,
            weeklyGoalSeconds,
            progress,
            completedToday,
            completedMissionSteps,
            missionSteps,
            recommendation);
    }

    private static IReadOnlyList<DashboardExamItem> BuildExams(
        StudyData data,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var candidates = new List<DashboardExamItem>();

        foreach (var item in data.CalendarEvents.Where(x =>
                     !x.IsCancelled &&
                     string.Equals(x.Category, CalendarEventCategory.Exam, StringComparison.OrdinalIgnoreCase)))
        {
            var day = LocalDate(item.StartsAt, zone);
            if (day < today) continue;
            var courseName = !string.IsNullOrWhiteSpace(item.CourseName)
                ? item.CourseName
                : data.Courses.FirstOrDefault(x => x.Id == item.CourseId)?.Name ?? string.Empty;
            var verification = string.Equals(item.Verification, CalendarEventVerification.Official, StringComparison.OrdinalIgnoreCase)
                ? "Appello ufficiale"
                : string.Equals(item.Verification, CalendarEventVerification.Indicative, StringComparison.OrdinalIgnoreCase)
                    ? "Data indicativa"
                    : "Da verificare";
            candidates.Add(CreateExam(
                item.CourseId ?? string.Empty,
                item.Title,
                courseName,
                item.StartsAt,
                verification,
                string.Equals(item.Verification, CalendarEventVerification.Official, StringComparison.OrdinalIgnoreCase),
                today,
                zone));
        }

        foreach (var exam in data.Exams.Where(x => !x.Completed && x.PlannedDate.HasValue))
        {
            var localDate = DateTime.SpecifyKind(exam.PlannedDate!.Value, DateTimeKind.Unspecified);
            var startsAt = AtLocalTime(localDate.Date.AddHours(9), zone);
            if (LocalDate(startsAt, zone) < today) continue;
            var courseName = !string.IsNullOrWhiteSpace(exam.CourseName)
                ? exam.CourseName
                : data.Courses.FirstOrDefault(x => x.Id == exam.CourseId)?.Name ?? string.Empty;
            var title = string.IsNullOrWhiteSpace(exam.Name) ? "Esame: " + courseName : exam.Name;
            candidates.Add(CreateExam(
                exam.CourseId,
                title,
                courseName,
                startsAt,
                "Pianificazione indicativa",
                false,
                today,
                zone));
        }

        foreach (var course in data.Courses.Where(x => !x.Completed && !x.Archived && x.AppealDate.HasValue))
        {
            var startsAt = AtLocalTime(
                DateTime.SpecifyKind(course.AppealDate!.Value.Date.AddHours(9), DateTimeKind.Unspecified),
                zone);
            if (LocalDate(startsAt, zone) < today) continue;
            candidates.Add(CreateExam(
                course.Id,
                "Esame: " + course.Name,
                course.Name,
                startsAt,
                "Data da verificare",
                false,
                today,
                zone));
        }

        return candidates
            .GroupBy(x => ExamDeduplicationKey(x, zone), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(x => x.IsOfficial)
                .ThenBy(x => x.StartsAt)
                .First())
            .OrderBy(x => x.StartsAt)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static DashboardExamItem CreateExam(
        string courseId,
        string title,
        string courseName,
        DateTimeOffset startsAt,
        string verification,
        bool official,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(startsAt, zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        var days = Math.Max(0, day.DayNumber - today.DayNumber);
        var countdown = days switch
        {
            0 => "Oggi",
            1 => "Domani",
            _ => "Tra " + days + " giorni"
        };
        var dateText = local.ToString("dddd d MMMM", Italian);
        if (!string.IsNullOrWhiteSpace(courseName))
            dateText += " · " + local.ToString("HH:mm", Italian);
        var subtitle = string.IsNullOrWhiteSpace(courseName)
            ? dateText
            : courseName + " · " + dateText;
        return new DashboardExamItem(
            courseId,
            string.IsNullOrWhiteSpace(title) ? "Esame universitario" : title,
            courseName,
            subtitle,
            dateText,
            countdown,
            verification,
            official,
            days);
    }

    private static string ExamDeduplicationKey(DashboardExamItem item, TimeZoneInfo zone)
    {
        var courseKey = !string.IsNullOrWhiteSpace(item.CourseId)
            ? item.CourseId.Trim()
            : item.CourseName.Trim().ToUpperInvariant();
        return courseKey + "|" + LocalDate(item.StartsAt, zone).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<DashboardTaskItem> BuildTodayTasks(
        StudyData data,
        DateOnly today,
        DateTimeOffset todayEnd,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var items = new List<DashboardTaskItem>();
        foreach (var task in data.StudyTasks.Where(x =>
                     x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress &&
                     x.DueAt.HasValue &&
                     LocalDate(x.DueAt.Value, zone) <= today))
        {
            var due = task.DueAt!.Value;
            var courseName = data.Courses.FirstOrDefault(x => x.Id == task.CourseId)?.Name ?? string.Empty;
            var overdue = IsOverdue(due, today, now, zone);
            var dueLabel = FormatDueLabel(due, today, now, zone);
            var subtitle = courseName;
            if (task.Priority > 0)
                subtitle = string.IsNullOrWhiteSpace(subtitle)
                    ? "Priorità " + task.Priority + "/5"
                    : subtitle + " · Priorità " + task.Priority + "/5";
            items.Add(new DashboardTaskItem(
                task.Id,
                task.Title,
                subtitle,
                "Attività",
                dueLabel,
                due,
                task.Priority,
                task.EstimatedMinutes,
                overdue,
                true));
        }

        foreach (var mission in data.StudyMissions.Where(x =>
                     x.Status is StudyMissionStatus.Planned or StudyMissionStatus.InProgress &&
                     x.DueAt.HasValue &&
                     x.DueAt.Value <= todayEnd))
        {
            var due = mission.DueAt!.Value;
            var courseName = data.Courses.FirstOrDefault(x => x.Id == mission.CourseId)?.Name ?? string.Empty;
            var subtitle = string.IsNullOrWhiteSpace(courseName)
                ? mission.CompletedStepCount + "/" + mission.Steps.Count + " passi"
                : courseName + " · " + mission.CompletedStepCount + "/" + mission.Steps.Count + " passi";
            items.Add(new DashboardTaskItem(
                "mission:" + mission.Id,
                mission.Title,
                subtitle,
                "Missione",
                FormatDueLabel(due, today, now, zone),
                due,
                mission.Difficulty,
                0,
                IsOverdue(due, today, now, zone),
                false));
        }

        return items
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.DueAt)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<DashboardDeadlineItem> BuildDeadlines(
        StudyData data,
        DateOnly today,
        DateTimeOffset todayEnd,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var items = new List<DashboardDeadlineItem>();
        var windowEnd = StartOfDay(today.AddDays(15), zone);
        var oldestCalendarStart = StartOfDay(today.AddDays(-14), zone);

        foreach (var task in data.StudyTasks.Where(x =>
                     x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress &&
                     x.DueAt.HasValue &&
                     x.DueAt.Value < windowEnd))
        {
            var due = task.DueAt!.Value;
            var courseName = data.Courses.FirstOrDefault(x => x.Id == task.CourseId)?.Name ?? string.Empty;
            items.Add(new DashboardDeadlineItem(
                task.Id,
                task.Title,
                courseName,
                "Attività",
                FormatDueLabel(due, today, now, zone),
                due,
                IsOverdue(due, today, now, zone)));
        }

        foreach (var mission in data.StudyMissions.Where(x =>
                     x.Status is StudyMissionStatus.Planned or StudyMissionStatus.InProgress &&
                     x.DueAt.HasValue &&
                     x.DueAt.Value < windowEnd))
        {
            var due = mission.DueAt!.Value;
            var courseName = data.Courses.FirstOrDefault(x => x.Id == mission.CourseId)?.Name ?? string.Empty;
            items.Add(new DashboardDeadlineItem(
                "mission:" + mission.Id,
                mission.Title,
                courseName,
                "Missione",
                FormatDueLabel(due, today, now, zone),
                due,
                IsOverdue(due, today, now, zone)));
        }

        foreach (var calendarEvent in data.CalendarEvents.Where(x =>
                     !x.IsCancelled &&
                     string.Equals(x.Category, CalendarEventCategory.Deadline, StringComparison.OrdinalIgnoreCase) &&
                     x.StartsAt < windowEnd &&
                     x.EndsAt >= oldestCalendarStart))
        {
            var due = calendarEvent.StartsAt;
            var context = !string.IsNullOrWhiteSpace(calendarEvent.CourseName)
                ? calendarEvent.CourseName
                : data.Courses.FirstOrDefault(x => x.Id == calendarEvent.CourseId)?.Name ?? string.Empty;
            items.Add(new DashboardDeadlineItem(
                calendarEvent.Id,
                calendarEvent.Title,
                context,
                "Scadenza",
                FormatDueLabel(due, today, now, zone),
                due,
                IsOverdue(due, today, now, zone)));
        }

        return items
            .OrderBy(x => x.DueAt)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private static DashboardRecommendation BuildRecommendation(
        StudyData data,
        DateOnly today,
        DateTimeOffset now,
        TimeZoneInfo zone,
        IReadOnlyList<DashboardExamItem> exams,
        IReadOnlyList<DashboardTaskItem> tasks,
        IReadOnlyDictionary<string, int> recordedSecondsByCourse)
    {
        var overdueTask = tasks
            .Where(x => x.CanComplete && x.IsOverdue)
            .OrderBy(x => x.DueAt)
            .ThenByDescending(x => x.Priority)
            .FirstOrDefault();
        if (overdueTask is not null)
        {
            var course = data.Courses.FirstOrDefault(x => x.Id == data.StudyTasks.FirstOrDefault(t => t.Id == overdueTask.Id)?.CourseId);
            return new DashboardRecommendation(
                overdueTask.Title,
                course?.Id ?? string.Empty,
                course?.Name ?? string.Empty,
                "Questa attività ha già superato la scadenza: conviene recuperarla prima di aggiungere altro carico.",
                "Alta",
                ClampDuration(overdueTask.EstimatedMinutes, 30));
        }

        var nearestExam = exams.FirstOrDefault(x => x.DaysRemaining <= 14 && !string.IsNullOrWhiteSpace(x.CourseId));
        if (nearestExam is not null && nearestExam.DaysRemaining <= 7)
        {
            var topic = GetNextIncompleteTopic(data, nearestExam.CourseId);
            if (topic is not null)
            {
                var remaining = data.SyllabusTopics.Count(x =>
                    string.Equals(x.CourseId, nearestExam.CourseId, StringComparison.Ordinal) &&
                    x.Status != SyllabusTopicStatus.Completed);
                return new DashboardRecommendation(
                    "Ripassa: " + topic.Title,
                    nearestExam.CourseId,
                    nearestExam.CourseName,
                    "L'esame di " + nearestExam.CourseName + " è " + nearestExam.CountdownText.ToLowerInvariant() +
                    " e ci sono " + remaining + " argomenti del programma ancora da completare.",
                    "Alta",
                    ClampDuration(topic.EstimatedMinutes, 50));
            }
            return new DashboardRecommendation(
                "Ripassa " + nearestExam.CourseName,
                nearestExam.CourseId,
                nearestExam.CourseName,
                "L'esame è " + nearestExam.CountdownText.ToLowerInvariant() +
                ". Dedica una sessione al ripasso e verifica di aver completato il programma.",
                "Alta",
                50);
        }

        var dueToday = tasks
            .Where(x => x.CanComplete && !x.IsOverdue)
            .OrderBy(x => x.DueAt)
            .ThenByDescending(x => x.Priority)
            .FirstOrDefault();
        if (dueToday is not null)
        {
            var task = data.StudyTasks.FirstOrDefault(x => x.Id == dueToday.Id);
            var courseName = data.Courses.FirstOrDefault(x => x.Id == task?.CourseId)?.Name ?? string.Empty;
            return new DashboardRecommendation(
                dueToday.Title,
                task?.CourseId ?? string.Empty,
                courseName,
                "Ha una scadenza prevista per oggi: completarla libera spazio per la preparazione degli esami.",
                "Alta",
                ClampDuration(dueToday.EstimatedMinutes, 30));
        }

        if (nearestExam is not null)
        {
            var topic = GetNextIncompleteTopic(data, nearestExam.CourseId);
            if (topic is not null)
            {
                var remaining = data.SyllabusTopics.Count(x =>
                    string.Equals(x.CourseId, nearestExam.CourseId, StringComparison.Ordinal) &&
                    x.Status != SyllabusTopicStatus.Completed);
                return new DashboardRecommendation(
                    "Avanza in " + topic.Title,
                    nearestExam.CourseId,
                    nearestExam.CourseName,
                    "L'esame è " + nearestExam.CountdownText.ToLowerInvariant() +
                    "; puoi ridurre il programma rimanente affrontando questo argomento.",
                    "Media",
                    ClampDuration(topic.EstimatedMinutes, 45));
            }
        }

        var dueFlashcards = data.Flashcards
            .Where(x => !x.IsSuspended && x.DueAt is { } due && due <= now)
            .OrderBy(x => x.DueAt)
            .FirstOrDefault();
        if (dueFlashcards is not null)
        {
            var course = data.Courses.FirstOrDefault(x => x.Id == dueFlashcards.CourseId);
            return new DashboardRecommendation(
                "Ripassa le flashcard in scadenza",
                course?.Id ?? string.Empty,
                course?.Name ?? string.Empty,
                "Hai schede da ripassare secondo il calendario di spaced repetition già registrato.",
                "Media",
                25);
        }

        var dueError = data.ErrorJournalEntries
            .Where(x => x.Status != ErrorReviewStatus.Resolved && x.NextReviewAt is { } due && due <= now)
            .OrderBy(x => x.NextReviewAt)
            .FirstOrDefault();
        if (dueError is not null)
        {
            var course = data.Courses.FirstOrDefault(x => x.Id == dueError.CourseId);
            var title = string.IsNullOrWhiteSpace(dueError.Topic)
                ? "Rivedi un errore registrato"
                : "Rivedi: " + dueError.Topic;
            return new DashboardRecommendation(
                title,
                course?.Id ?? string.Empty,
                course?.Name ?? string.Empty,
                "Il diario degli errori segnala un ripasso programmato per oggi o già scaduto.",
                "Media",
                25);
        }

        var incompleteTopic = data.SyllabusTopics
            .Where(x => x.Status != SyllabusTopicStatus.Completed)
            .OrderBy(x => x.Status == SyllabusTopicStatus.InProgress ? 0 : 1)
            .ThenBy(x => x.LastStudiedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.EstimatedMinutes)
            .FirstOrDefault();
        if (incompleteTopic is not null)
        {
            var course = data.Courses.FirstOrDefault(x => x.Id == incompleteTopic.CourseId);
            var courseMinutes = recordedSecondsByCourse.GetValueOrDefault(incompleteTopic.CourseId);
            var reason = courseMinutes == 0
                ? "Non risultano sessioni di studio registrate per questa materia nella settimana corrente."
                : "Questo argomento risulta ancora aperto nel programma della materia.";
            return new DashboardRecommendation(
                "Studia: " + incompleteTopic.Title,
                incompleteTopic.CourseId,
                course?.Name ?? string.Empty,
                reason,
                "Normale",
                ClampDuration(incompleteTopic.EstimatedMinutes, 40));
        }

        return new DashboardRecommendation(
            "Organizza il prossimo passo",
            string.Empty,
            string.Empty,
            "Non risultano scadenze o ripassi urgenti. Puoi aggiungere un argomento o pianificare una sessione quando hai deciso cosa preparare.",
            "Normale",
            25);
    }

    private static SyllabusTopicData? GetNextIncompleteTopic(StudyData data, string courseId) =>
        data.SyllabusTopics
            .Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal) &&
                        x.Status != SyllabusTopicStatus.Completed)
            .OrderBy(x => x.Status == SyllabusTopicStatus.InProgress ? 0 : 1)
            .ThenByDescending(x => x.EstimatedMinutes)
            .FirstOrDefault();

    private static int ClampDuration(int minutes, int fallback) =>
        Math.Clamp(minutes > 0 ? minutes : fallback, 15, 90);

    private static bool IsOverdue(DateTimeOffset due, DateOnly today, DateTimeOffset now, TimeZoneInfo zone)
    {
        var dueDay = LocalDate(due, zone);
        return dueDay < today || (dueDay == today && due < now);
    }

    private static string FormatDueLabel(DateTimeOffset due, DateOnly today, DateTimeOffset now, TimeZoneInfo zone)
    {
        var dueDay = LocalDate(due, zone);
        var days = dueDay.DayNumber - today.DayNumber;
        if (days < 0) return "Scaduta da " + Math.Abs(days) + " giorni";
        if (days == 0) return due < now ? "Scaduta oggi" : "Oggi";
        if (days == 1) return "Domani";
        return "Tra " + days + " giorni";
    }

    private static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static bool IsInRange(DateOnly value, DateOnly from, DateOnly to) =>
        value >= from && value <= to;

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTimeOffset AtLocalTime(DateTime local, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException("Fuso orario non trovato: " + timeZoneId, nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException("Fuso orario non valido: " + timeZoneId, nameof(timeZoneId), ex);
        }
    }
}
