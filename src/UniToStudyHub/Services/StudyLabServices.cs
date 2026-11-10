using System.Collections.ObjectModel;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record FocusSnapshot(
    StudySessionData Session,
    StudySessionState State,
    int ElapsedSeconds,
    int RemainingSeconds,
    bool IsExpired);

/// <summary>
/// Focus sessions are reconstructed from persisted timestamps and accumulated
/// seconds. The service owns no timer: a UI may poll <see cref="Snapshot"/> or
/// calculate a display value, but closing the app cannot lose the session.
/// </summary>
public sealed class FocusSessionService
{
    public StudySessionData Start(
        StudyData data,
        string? courseId,
        string topic,
        int plannedMinutes,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        ValidateCourse(data, courseId);
        if (plannedMinutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(plannedMinutes), "Planned minutes must be between 1 and 1440.");
        if (data.StudySessions.Any(x => x.State is StudySessionState.Running or StudySessionState.Paused))
            throw new InvalidOperationException("A focus session is already active. Finish or cancel it before starting another.");

        var session = new StudySessionData
        {
            Id = Guid.NewGuid().ToString("N"),
            CourseId = courseId?.Trim() ?? string.Empty,
            Topic = topic?.Trim() ?? string.Empty,
            PlannedMinutes = plannedMinutes,
            StartedAt = at,
            LastResumedAt = at,
            State = StudySessionState.Running,
            CreatedAt = at,
            UpdatedAt = at
        };
        session.Segments.Add(new StudySessionSegmentData
        {
            Type = StudySessionSegmentType.Focus,
            StartedAt = at
        });
        data.StudySessions.Add(session);
        return session;
    }

    public StudySessionData Pause(StudyData data, string sessionId, DateTimeOffset? now = null)
    {
        var session = Find(data, sessionId);
        if (session.State != StudySessionState.Running || session.LastResumedAt is null)
            throw new InvalidOperationException("Only a running focus session can be paused.");
        var at = now ?? DateTimeOffset.Now;
        if (at < session.LastResumedAt.Value)
            throw new ArgumentOutOfRangeException(nameof(now), "The clock moved before the session start.");
        session.AccumulatedFocusSeconds += SecondsBetween(session.LastResumedAt.Value, at);
        EnsureOpenSegment(session, StudySessionSegmentType.Focus, session.LastResumedAt.Value, at);
        CloseOpenSegment(session, at);
        session.Segments.Add(new StudySessionSegmentData
        {
            Type = StudySessionSegmentType.Pause,
            StartedAt = at
        });
        session.PausedAt = at;
        session.LastResumedAt = null;
        session.State = StudySessionState.Paused;
        session.UpdatedAt = at;
        return session;
    }

    public StudySessionData Resume(StudyData data, string sessionId, DateTimeOffset? now = null)
    {
        var session = Find(data, sessionId);
        if (session.State != StudySessionState.Paused || session.PausedAt is null)
            throw new InvalidOperationException("Only a paused focus session can be resumed.");
        var at = now ?? DateTimeOffset.Now;
        if (at < session.PausedAt.Value)
            throw new ArgumentOutOfRangeException(nameof(now), "The clock moved before the pause.");
        session.AccumulatedPauseSeconds += SecondsBetween(session.PausedAt.Value, at);
        EnsureOpenSegment(session, StudySessionSegmentType.Pause, session.PausedAt.Value, at);
        CloseOpenSegment(session, at);
        session.Segments.Add(new StudySessionSegmentData
        {
            Type = StudySessionSegmentType.Focus,
            StartedAt = at
        });
        session.PausedAt = null;
        session.LastResumedAt = at;
        session.State = StudySessionState.Running;
        session.UpdatedAt = at;
        return session;
    }

    public StudySessionData Finish(
        StudyData data,
        string sessionId,
        string? outcome = null,
        DateTimeOffset? now = null)
    {
        var session = Find(data, sessionId);
        if (session.State is StudySessionState.Completed or StudySessionState.Cancelled)
            throw new InvalidOperationException("The focus session is already closed.");
        var at = now ?? DateTimeOffset.Now;
        AccumulateUntil(session, at);
        if (session.State == StudySessionState.Running && session.LastResumedAt is { } resumed)
            EnsureOpenSegment(session, StudySessionSegmentType.Focus, resumed, at);
        else if (session.State == StudySessionState.Paused && session.PausedAt is { } paused)
            EnsureOpenSegment(session, StudySessionSegmentType.Pause, paused, at);
        CloseOpenSegment(session, at);
        session.EndedAt = at;
        session.Outcome = outcome?.Trim() ?? string.Empty;
        session.State = StudySessionState.Completed;
        session.LastResumedAt = null;
        session.PausedAt = null;
        session.UpdatedAt = at;
        return session;
    }

    public StudySessionData Reset(StudyData data, string sessionId, DateTimeOffset? now = null) =>
        Cancel(data, sessionId, now);

    public StudySessionData Cancel(StudyData data, string sessionId, DateTimeOffset? now = null)
    {
        var session = Find(data, sessionId);
        if (session.State is StudySessionState.Completed or StudySessionState.Cancelled)
            throw new InvalidOperationException("The focus session is already closed.");
        var at = now ?? DateTimeOffset.Now;
        AccumulateUntil(session, at);
        if (session.State == StudySessionState.Running && session.LastResumedAt is { } resumed)
            EnsureOpenSegment(session, StudySessionSegmentType.Focus, resumed, at);
        else if (session.State == StudySessionState.Paused && session.PausedAt is { } paused)
            EnsureOpenSegment(session, StudySessionSegmentType.Pause, paused, at);
        CloseOpenSegment(session, at);
        session.EndedAt = at;
        session.State = StudySessionState.Cancelled;
        session.LastResumedAt = null;
        session.PausedAt = null;
        session.UpdatedAt = at;
        return session;
    }

    public StudySessionData? GetActive(StudyData data) =>
        data.StudySessions
            .Where(x => x.State is StudySessionState.Running or StudySessionState.Paused)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();

    public FocusSnapshot Snapshot(StudyData data, string sessionId, DateTimeOffset? now = null)
    {
        var session = Find(data, sessionId);
        var elapsed = ElapsedSeconds(session, now ?? DateTimeOffset.Now);
        var remaining = Math.Max(0, session.PlannedMinutes * 60 - elapsed);
        return new FocusSnapshot(session, session.State, elapsed, remaining, remaining == 0);
    }

    public static int ElapsedSeconds(StudySessionData session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        var elapsed = Math.Max(0, session.AccumulatedFocusSeconds);
        if (session.State == StudySessionState.Running && session.LastResumedAt is not null)
            elapsed += SecondsBetween(session.LastResumedAt.Value, now);
        return elapsed;
    }

    private static void AccumulateUntil(StudySessionData session, DateTimeOffset at)
    {
        if (session.State == StudySessionState.Running && session.LastResumedAt is not null)
            session.AccumulatedFocusSeconds += SecondsBetween(session.LastResumedAt.Value, at);
    }

    private static void EnsureOpenSegment(
        StudySessionData session,
        StudySessionSegmentType type,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        session.Segments ??= new ObservableCollection<StudySessionSegmentData>();
        if (session.Segments.Any(x => x.EndedAt is null)) return;
        if (endedAt <= startedAt) return;
        session.Segments.Add(new StudySessionSegmentData
        {
            Type = type,
            StartedAt = startedAt,
            EndedAt = endedAt
        });
    }

    private static void CloseOpenSegment(StudySessionData session, DateTimeOffset at)
    {
        session.Segments ??= new ObservableCollection<StudySessionSegmentData>();
        var open = session.Segments.LastOrDefault(x => x.EndedAt is null);
        if (open is not null)
        {
            if (at < open.StartedAt)
                throw new ArgumentOutOfRangeException(nameof(at), "The clock moved before the open segment start.");
            if (at == open.StartedAt)
            {
                session.Segments.Remove(open);
                return;
            }
            open.EndedAt = at;
        }
    }

    private static int SecondsBetween(DateTimeOffset from, DateTimeOffset to) =>
        to <= from ? 0 : (int)Math.Min(int.MaxValue, Math.Floor((to - from).TotalSeconds));

    private static StudySessionData Find(StudyData data, string sessionId) =>
        data.StudySessions.FirstOrDefault(x => string.Equals(x.Id, sessionId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Study session '{sessionId}' does not exist.", nameof(sessionId));

    private static void ValidateCourse(StudyData data, string? courseId)
    {
        if (!string.IsNullOrWhiteSpace(courseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, courseId, StringComparison.Ordinal)))
            throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
    }
}

public sealed class StudyTaskService
{
    public StudyTaskData Add(StudyData data, StudyTaskData task)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.Id)) task.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(task.Title))
            throw new ArgumentException("Task title is required.", nameof(task));
        if (task.EstimatedMinutes is < 0 or > 14400)
            throw new ArgumentOutOfRangeException(nameof(task), "Estimated minutes are outside the supported range.");
        if (task.Priority is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(task), "Priority must be between 1 and 5.");
        if (!string.IsNullOrWhiteSpace(task.CourseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, task.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The task course does not exist.", nameof(task));
        if (data.StudyTasks.Any(x => string.Equals(x.Id, task.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Task '{task.Id}' already exists.", nameof(task));
        task.Title = task.Title.Trim();
        task.Description = task.Description?.Trim() ?? string.Empty;
        task.Source = string.IsNullOrWhiteSpace(task.Source) ? "manual" : task.Source.Trim();
        task.Status = StudyTaskStatus.Todo;
        task.CompletedAt = null;
        task.CreatedAt = task.CreatedAt == default ? DateTimeOffset.Now : task.CreatedAt;
        task.UpdatedAt = DateTimeOffset.Now;
        data.StudyTasks.Add(task);
        return task;
    }

    public void Complete(StudyData data, string taskId, DateTimeOffset? now = null)
    {
        var task = Find(data, taskId);
        if (task.Status == StudyTaskStatus.Cancelled)
            throw new InvalidOperationException("A cancelled task cannot be completed.");
        task.Status = StudyTaskStatus.Completed;
        task.CompletedAt = now ?? DateTimeOffset.Now;
        task.UpdatedAt = task.CompletedAt.Value;
    }

    public void Cancel(StudyData data, string taskId, DateTimeOffset? now = null)
    {
        var task = Find(data, taskId);
        task.Status = StudyTaskStatus.Cancelled;
        task.UpdatedAt = now ?? DateTimeOffset.Now;
    }

    public void Remove(StudyData data, string taskId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var task = Find(data, taskId);
        if (data.StudyActivities.Any(x => string.Equals(x.TaskId, task.Id, StringComparison.Ordinal)))
        {
            // Keep the task as a historical reference for recorded activities.
            // It disappears from the open-task list without orphaning the ledger.
            if (task.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress)
            {
                task.Status = StudyTaskStatus.Cancelled;
                task.CompletedAt = null;
            }
            task.UpdatedAt = now ?? DateTimeOffset.Now;
            return;
        }
        data.StudyTasks.Remove(task);
    }

    private static StudyTaskData Find(StudyData data, string taskId) =>
        data.StudyTasks.FirstOrDefault(x => string.Equals(x.Id, taskId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Task '{taskId}' does not exist.", nameof(taskId));
}

public sealed class ErrorJournalService
{
    public ErrorJournalEntryData Add(StudyData data, ErrorJournalEntryData entry)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(entry.Question) || string.IsNullOrWhiteSpace(entry.CorrectAnswer))
            throw new ArgumentException("The error journal needs the question and the correct answer.", nameof(entry));
        if (!string.IsNullOrWhiteSpace(entry.CourseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, entry.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The error course does not exist.", nameof(entry));
        if (!string.IsNullOrWhiteSpace(entry.SyllabusTopicId) &&
            !data.SyllabusTopics.Any(x => string.Equals(x.Id, entry.SyllabusTopicId, StringComparison.Ordinal)))
            throw new ArgumentException("The syllabus topic does not exist.", nameof(entry));
        if (data.ErrorJournalEntries.Any(x => string.Equals(x.Id, entry.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Error entry '{entry.Id}' already exists.", nameof(entry));
        entry.Topic = entry.Topic?.Trim() ?? string.Empty;
        entry.IncorrectAnswer = entry.IncorrectAnswer?.Trim() ?? string.Empty;
        entry.CorrectAnswer = entry.CorrectAnswer.Trim();
        entry.ErrorType = string.IsNullOrWhiteSpace(entry.ErrorType) ? "Concetto" : entry.ErrorType.Trim();
        entry.RootCause = entry.RootCause?.Trim() ?? string.Empty;
        entry.Source = string.IsNullOrWhiteSpace(entry.Source) ? "manual" : entry.Source.Trim();
        entry.Status = ErrorReviewStatus.Open;
        entry.ReviewCount = 0;
        entry.NextReviewAt = entry.NextReviewAt ?? DateTimeOffset.Now.AddDays(1);
        entry.CreatedAt = entry.CreatedAt == default ? DateTimeOffset.Now : entry.CreatedAt;
        entry.UpdatedAt = DateTimeOffset.Now;
        data.ErrorJournalEntries.Add(entry);
        return entry;
    }

    /// <summary>
    /// A review schedules the next check using a deliberately simple, visible
    /// interval. It is not presented as an AI diagnosis.
    /// </summary>
    public void Review(StudyData data, string entryId, bool recalled, DateTimeOffset? now = null)
    {
        var entry = Find(data, entryId);
        var at = now ?? DateTimeOffset.Now;
        entry.ReviewCount++;
        if (recalled)
        {
            entry.NextReviewAt = at.AddDays(Math.Min(30, Math.Max(2, entry.ReviewCount * 3)));
            if (entry.ReviewCount >= 3) entry.Status = ErrorReviewStatus.Resolved;
        }
        else
        {
            entry.NextReviewAt = at.AddDays(1);
            entry.Status = ErrorReviewStatus.Reviewing;
        }
        entry.UpdatedAt = at;
    }

    public IReadOnlyList<ErrorJournalEntryData> GetDue(StudyData data, DateTimeOffset? now = null) =>
        data.ErrorJournalEntries
            .Where(x => x.Status != ErrorReviewStatus.Resolved && x.NextReviewAt is not null && x.NextReviewAt <= (now ?? DateTimeOffset.Now))
            .OrderBy(x => x.NextReviewAt)
            .ToList();

    private static ErrorJournalEntryData Find(StudyData data, string entryId) =>
        data.ErrorJournalEntries.FirstOrDefault(x => string.Equals(x.Id, entryId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Error entry '{entryId}' does not exist.", nameof(entryId));
}

public sealed class FlashcardService
{
    public FlashcardData Add(StudyData data, FlashcardData card)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.Id)) card.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(card.Front) || string.IsNullOrWhiteSpace(card.Back))
            throw new ArgumentException("A flashcard needs a front and a back.", nameof(card));
        if (!string.IsNullOrWhiteSpace(card.CourseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, card.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The flashcard course does not exist.", nameof(card));
        if (data.Flashcards.Any(x => string.Equals(x.Id, card.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Flashcard '{card.Id}' already exists.", nameof(card));
        card.Front = card.Front.Trim();
        card.Back = card.Back.Trim();
        card.Source = string.IsNullOrWhiteSpace(card.Source) ? "manual" : card.Source.Trim();
        card.EaseFactor = card.EaseFactor is < 1.3m or > 2.8m ? 2.5m : card.EaseFactor;
        card.IntervalDays = Math.Max(0, card.IntervalDays);
        card.Repetitions = Math.Max(0, card.Repetitions);
        card.Lapses = Math.Max(0, card.Lapses);
        card.DueAt ??= DateTimeOffset.Now;
        card.CreatedAt = card.CreatedAt == default ? DateTimeOffset.Now : card.CreatedAt;
        card.UpdatedAt = DateTimeOffset.Now;
        data.Flashcards.Add(card);
        return card;
    }

    public IReadOnlyList<FlashcardData> GetDue(StudyData data, DateTimeOffset? now = null) =>
        data.Flashcards
            .Where(x => !x.IsSuspended && x.DueAt is not null && x.DueAt <= (now ?? DateTimeOffset.Now))
            .OrderBy(x => x.DueAt)
            .ToList();

    public FlashcardReviewData Review(
        StudyData data,
        string flashcardId,
        RecallRating rating,
        DateTimeOffset? now = null)
    {
        var card = data.Flashcards.FirstOrDefault(x => string.Equals(x.Id, flashcardId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Flashcard '{flashcardId}' does not exist.", nameof(flashcardId));
        if (card.IsSuspended) throw new InvalidOperationException("A suspended flashcard cannot be reviewed.");
        if (!Enum.IsDefined(rating)) throw new ArgumentOutOfRangeException(nameof(rating));

        var at = now ?? DateTimeOffset.Now;
        var previousInterval = card.IntervalDays;
        var ease = card.EaseFactor;
        int nextInterval;
        switch (rating)
        {
            case RecallRating.Again:
                ease = Math.Max(1.3m, ease - 0.2m);
                nextInterval = 1;
                card.Repetitions = 0;
                card.Lapses++;
                break;
            case RecallRating.Hard:
                ease = Math.Max(1.3m, ease - 0.15m);
                nextInterval = Math.Max(1, (int)Math.Round(Math.Max(1, previousInterval) * 1.2m));
                card.Repetitions++;
                break;
            case RecallRating.Easy:
                ease = Math.Min(2.8m, ease + 0.15m);
                nextInterval = previousInterval <= 0
                    ? 4
                    : Math.Max(1, (int)Math.Round(previousInterval * ease * 1.3m));
                card.Repetitions++;
                break;
            default:
                nextInterval = previousInterval <= 0
                    ? 1
                    : previousInterval == 1
                        ? 6
                        : Math.Max(1, (int)Math.Round(previousInterval * ease));
                card.Repetitions++;
                break;
        }

        card.EaseFactor = ease;
        card.IntervalDays = nextInterval;
        card.DueAt = at.AddDays(nextInterval);
        card.LastReviewedAt = at;
        card.UpdatedAt = at;
        var review = new FlashcardReviewData
        {
            Id = Guid.NewGuid().ToString("N"),
            FlashcardId = card.Id,
            ReviewedAt = at,
            Rating = rating,
            PreviousIntervalDays = previousInterval,
            NewIntervalDays = nextInterval,
            EaseFactorAfter = ease
        };
        data.FlashcardReviews.Add(review);
        return review;
    }
}

public sealed class DistractionService
{
    public DistractionData Add(StudyData data, string text, string? sessionId = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Distraction text is required.", nameof(text));
        if (!string.IsNullOrWhiteSpace(sessionId) &&
            !data.StudySessions.Any(x => string.Equals(x.Id, sessionId, StringComparison.Ordinal)))
            throw new ArgumentException("The session does not exist.", nameof(sessionId));
        var at = now ?? DateTimeOffset.Now;
        var item = new DistractionData { Text = text.Trim(), SessionId = sessionId, CreatedAt = at };
        data.Distractions.Add(item);
        return item;
    }

    public void Resolve(StudyData data, string id, DateTimeOffset? now = null)
    {
        var item = data.Distractions.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Distraction '{id}' does not exist.", nameof(id));
        item.ResolvedAt = now ?? DateTimeOffset.Now;
    }
}

public sealed class EnergyService
{
    public EnergyCheckInData Add(
        StudyData data,
        int energyLevel,
        int focusLevel,
        string? note = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (energyLevel is < 1 or > 5 || focusLevel is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(energyLevel), "Energy and focus must be between 1 and 5.");
        var item = new EnergyCheckInData
        {
            EnergyLevel = energyLevel,
            FocusLevel = focusLevel,
            Note = note?.Trim() ?? string.Empty,
            RecordedAt = now ?? DateTimeOffset.Now
        };
        data.EnergyCheckIns.Add(item);
        return item;
    }
}

public sealed record CourseProgress(
    string CourseId,
    string CourseName,
    int FocusMinutes,
    int CompletedSessions,
    int CompletedTasks,
    int DueFlashcards,
    int OpenErrors)
{
    public int RecordedSeconds { get; init; }
    public int EstimatedSessionMinutes { get; init; }
}

public sealed record DailyProgressMetric(
    DateOnly Day,
    int RecordedSeconds,
    int OverlapSeconds,
    int CompletedSessions,
    int PlannedSessionMinutes,
    int CompletedPomodoroCycles);

public sealed record ProgressSnapshot(
    DateOnly From,
    DateOnly To,
    int FocusMinutes,
    int CompletedSessions,
    int CompletedTasks,
    int DueFlashcards,
    int OpenErrors,
    int FocusStreakDays,
    int WeeklyGoalMinutes,
    IReadOnlyList<CourseProgress> Courses)
{
    public bool WeeklyGoalConfigured => WeeklyGoalMinutes > 0;
    public int FocusMinutesRemaining => WeeklyGoalConfigured ? Math.Max(0, WeeklyGoalMinutes - FocusMinutes) : 0;

    /// <summary>Unique seconds from the activity ledger, including manual and timer work.</summary>
    public int RecordedSeconds { get; init; }
    public int RecordedMinutes => RecordedSeconds / 60;
    public int OverlapSeconds { get; init; }
    public int RecordedMinutesRemaining => WeeklyGoalConfigured
        ? Math.Max(0, WeeklyGoalMinutes - RecordedMinutes)
        : 0;
    public IReadOnlyDictionary<StudyActivityType, int> RecordedSecondsByType { get; init; } =
        new Dictionary<StudyActivityType, int>();
    public IReadOnlyDictionary<string, int> RecordedSecondsByCourse { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> RecordedSecondsByTopic { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyList<DailyProgressMetric> Daily { get; init; } = Array.Empty<DailyProgressMetric>();
    public int EstimatedSessionMinutes { get; init; }
    public int CompletedTaskEstimatedMinutes { get; init; }
    public int CompletedPomodoroCycles { get; init; }
}

/// <summary>
/// Metrics use only stored sessions, tasks, cards and errors. The denominator
/// and date range are explicit; no synthetic history is generated.
/// </summary>
public sealed class ProgressService
{
    public ProgressSnapshot Calculate(
        StudyData data,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");
        var at = now ?? DateTimeOffset.Now;
        var sessions = data.StudySessions
            .Where(x => InRange(LocalDate(x.StartedAt), from, to))
            .ToList();
        var completed = sessions.Where(x => x.State == StudySessionState.Completed).ToList();
        var focusSeconds = sessions.Sum(x => FocusSessionService.ElapsedSeconds(x, at));
        var ledgerSummary = new ActivityLedgerService().Summarize(data, from, to, at, TimeZoneInfo.Local.Id);
        var cycles = data.PomodoroCycles
            .Where(x => x.IsCompleted && x.CompletedAt is { } completedAt &&
                        InRange(LocalDate(completedAt), from, to))
            .ToList();
        var daily = new List<DailyProgressMetric>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var daySummary = new ActivityLedgerService().Summarize(data, day, day, at, TimeZoneInfo.Local.Id);
            daily.Add(new DailyProgressMetric(
                day,
                daySummary.RecordedSeconds,
                daySummary.OverlapSeconds,
                sessions.Count(x => x.State == StudySessionState.Completed && LocalDate(x.StartedAt) == day),
                sessions.Where(x => LocalDate(x.StartedAt) == day).Sum(x => x.PlannedMinutes),
                cycles.Count(x => x.CompletedAt is { } completedAt && LocalDate(completedAt) == day)));
        }
        var tasks = data.StudyTasks
            .Where(x => x.CompletedAt is not null && InRange(LocalDate(x.CompletedAt.Value), from, to))
            .ToList();
        var dueCards = data.Flashcards.Count(x => !x.IsSuspended && x.DueAt is not null && x.DueAt <= at);
        var openErrors = data.ErrorJournalEntries.Count(x => x.Status != ErrorReviewStatus.Resolved);
        var courses = data.Courses
            .Select(course =>
            {
                var courseSessions = sessions.Where(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal)).ToList();
                return new CourseProgress(
                    course.Id,
                    course.Name,
                    courseSessions.Sum(x => FocusSessionService.ElapsedSeconds(x, at)) / 60,
                    courseSessions.Count(x => x.State == StudySessionState.Completed),
                    tasks.Count(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal)),
                    data.Flashcards.Count(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) &&
                                               !x.IsSuspended && x.DueAt is not null && x.DueAt <= at),
                    data.ErrorJournalEntries.Count(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) &&
                                                        x.Status != ErrorReviewStatus.Resolved))
                {
                    RecordedSeconds = ledgerSummary.ByCourse.GetValueOrDefault(course.Id),
                    EstimatedSessionMinutes = courseSessions.Sum(x => x.PlannedMinutes)
                };
            })
            .OrderBy(x => x.CourseName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ProgressSnapshot(
            from,
            to,
            focusSeconds / 60,
            completed.Count,
            tasks.Count,
            dueCards,
            openErrors,
            CalculateStreak(data, at),
            Math.Max(0, data.WeeklyStudyHours) * 60,
            courses)
        {
            RecordedSeconds = ledgerSummary.RecordedSeconds,
            OverlapSeconds = ledgerSummary.OverlapSeconds,
            RecordedSecondsByType = ledgerSummary.ByType,
            RecordedSecondsByCourse = ledgerSummary.ByCourse,
            RecordedSecondsByTopic = ledgerSummary.ByTopic,
            Daily = daily,
            EstimatedSessionMinutes = sessions.Sum(x => x.PlannedMinutes),
            CompletedTaskEstimatedMinutes = tasks.Sum(x => x.EstimatedMinutes),
            CompletedPomodoroCycles = cycles.Count
        };
    }

    /// <summary>
    /// A streak is consecutive local calendar days with at least one completed
    /// focus session, counted backwards from today. It is not awarded for
    /// opening the app or for planned sessions.
    /// </summary>
    public int CalculateStreak(StudyData data, DateTimeOffset now)
    {
        var days = data.StudySessions
            .Where(x => x.State == StudySessionState.Completed)
            .Select(x => LocalDate(x.StartedAt))
            .ToHashSet();
        var cursor = LocalDate(now);
        if (!days.Contains(cursor) && days.Contains(cursor.AddDays(-1))) cursor = cursor.AddDays(-1);
        if (!days.Contains(cursor)) return 0;
        var streak = 0;
        while (days.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    private static bool InRange(DateOnly value, DateOnly from, DateOnly to) => value >= from && value <= to;
    private static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.LocalDateTime);
}

public sealed record ErrorPattern(
    string CourseId,
    string Topic,
    string ErrorType,
    int Count,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Finds repeated errors without an AI diagnosis. Evidence is the stored
/// question/answer pairs, so the user can inspect and correct the pattern.
/// </summary>
public sealed class ErrorPatternService
{
    public IReadOnlyList<ErrorPattern> Detect(StudyData data, int minimumOccurrences = 2)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (minimumOccurrences < 2) minimumOccurrences = 2;
        return data.ErrorJournalEntries
            .Where(x => x.Status != ErrorReviewStatus.Resolved)
            .GroupBy(x => new { x.CourseId, x.Topic, x.ErrorType })
            .Where(g => g.Count() >= minimumOccurrences)
            .Select(g => new ErrorPattern(
                g.Key.CourseId,
                g.Key.Topic,
                g.Key.ErrorType,
                g.Count(),
                g.Select(x => x.Question).Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToArray()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Topic, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

public sealed record WeeklyReview(
    DateOnly WeekStart,
    ProgressSnapshot Progress,
    IReadOnlyList<ErrorPattern> ErrorPatterns,
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> SuggestedActions);

public sealed class WeeklyReviewService
{
    public WeeklyReview Create(StudyData data, DateOnly weekStart, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var start = weekStart;
        var end = weekStart.AddDays(6);
        var progress = new ProgressService().Calculate(data, start, end, now);
        var patterns = new ErrorPatternService().Detect(data);
        var observations = new List<string>
        {
            $"Tempo Focus (sessioni): {progress.FocusMinutes} minuti in {progress.CompletedSessions} sessioni.",
            $"Tempo di studio registrato unico: {progress.RecordedMinutes} minuti; sovrapposizioni: {progress.OverlapSeconds / 60} minuti.",
            $"Task completati: {progress.CompletedTasks}; flashcard in scadenza: {progress.DueFlashcards}.",
            $"Cicli Pomodoro completati: {progress.CompletedPomodoroCycles}."
        };
        if (progress.EstimatedSessionMinutes > 0)
            observations.Add(
                $"Tempo pianificato nelle sessioni: {progress.EstimatedSessionMinutes} minuti; tempo di studio registrato unico: {progress.RecordedMinutes} minuti.");
        if (progress.WeeklyGoalConfigured)
            observations.Add(progress.RecordedMinutes >= progress.WeeklyGoalMinutes
                ? $"Obiettivo settimanale: {progress.WeeklyGoalMinutes} minuti; raggiunto con {progress.RecordedMinutes} minuti di studio registrato unico. Tempo Focus: {progress.FocusMinutes} minuti; sovrapposizioni: {progress.OverlapSeconds / 60} minuti."
                : $"Obiettivo settimanale: {progress.WeeklyGoalMinutes} minuti; mancano {progress.RecordedMinutesRemaining} minuti. Tempo di studio registrato unico: {progress.RecordedMinutes} minuti; tempo Focus: {progress.FocusMinutes} minuti; sovrapposizioni: {progress.OverlapSeconds / 60} minuti.");
        var suggestions = new List<string>();
        if (progress.DueFlashcards > 0) suggestions.Add("Programma una sessione di ripasso per le flashcard scadute.");
        if (progress.OpenErrors > 0) suggestions.Add("Rivedi il diario errori e collega gli errori ricorrenti a un ripasso.");
        if (patterns.Count > 0) suggestions.Add("Approfondisci il pattern di errore con più evidenze prima del prossimo esame.");
        if (progress.OverlapSeconds > 0)
            suggestions.Add("Controlla le attività sovrapposte: il totale unico non le somma, ma la loro pianificazione può essere corretta.");
        if (suggestions.Count == 0) suggestions.Add("Mantieni il ritmo attuale e registra solo attività realmente svolte.");
        return new WeeklyReview(start, progress, patterns, observations, suggestions);
    }
}
