using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Deterministic study planner over real availability and calendar events.
/// It creates proposals only when the whole estimated workload, including the
/// review reserve, fits before the exam. A failed allocation never writes
/// calendar sessions; it records the deficit and the missing assumptions.
/// </summary>
public sealed class UniversityPlannerService
{
    private readonly FocusSessionService _focus = new();
    private readonly ActivityLedgerService _ledger = new();

    public PlannerResult CreatePlan(StudyData data, PlannerRequest request, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CourseId))
            throw new ArgumentException("A course is required for a study plan.", nameof(request));
        var course = data.Courses.FirstOrDefault(x =>
            string.Equals(x.Id, request.CourseId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The plan course does not exist.", nameof(request));

        var exam = string.IsNullOrWhiteSpace(request.ExamId)
            ? null
            : data.Exams.FirstOrDefault(x => string.Equals(x.Id, request.ExamId, StringComparison.Ordinal))
              ?? throw new ArgumentException("The plan exam does not exist.", nameof(request));
        if (exam is not null && !string.Equals(exam.CourseId, course.Id, StringComparison.Ordinal))
            throw new ArgumentException("The exam belongs to a different course.", nameof(request));

        var examDate = (request.ExamDate ?? exam?.PlannedDate)
            ?? throw new ArgumentException("An exam date is required for a study plan.", nameof(request));
        examDate = examDate.Date;
        var at = now ?? DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(at.LocalDateTime);

        var existing = data.StudyPlans
            .Where(x => x.Status is StudyPlanStatus.Active or StudyPlanStatus.Draft &&
                        string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) &&
                        (string.IsNullOrWhiteSpace(exam?.Id) || string.Equals(x.ExamId, exam.Id, StringComparison.Ordinal)))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (existing is not null && !request.ReplaceExistingPlan)
            throw new InvalidOperationException(
                "An active plan already exists for this course and exam. Archive or replace it explicitly.");

        ValidateSettings(data.PlannerSettings);
        var topics = SelectTopics(data, course.Id, request.TopicIds);
        if (topics.Count == 0)
        {
            var emptyPlan = NewPlan(course, exam, examDate, at);
            emptyPlan.Status = StudyPlanStatus.InsufficientTime;
            emptyPlan.Warnings.Add(
                "Nessun argomento non completato con un tempo stimato positivo: non è possibile calcolare un piano.");
            emptyPlan.Explanation = "La pianificazione richiede almeno un argomento con tempo stimato.";
            emptyPlan.UpdatedAt = at;
            data.StudyPlans.Add(emptyPlan);
            return new PlannerResult(emptyPlan, Array.Empty<CalendarEvent>(), 0);
        }

        var required = topics.Sum(x => x.EstimatedMinutes);
        var reserve = data.PlannerSettings.ReviewReservePercent == 0
            ? 0
            : (int)Math.Ceiling(required * data.PlannerSettings.ReviewReservePercent / 100m);
        var plan = NewPlan(course, exam, examDate, at);
        plan.RequiredMinutes = required;
        plan.ReviewReserveMinutes = reserve;
        plan.Explanation = BuildExplanation(data.PlannerSettings, required, reserve);

        var deadline = examDate.AddDays(-data.PlannerSettings.ReviewReserveDays);
        var lastStudyDay = DateOnly.FromDateTime(deadline).AddDays(-1);
        if (lastStudyDay < today)
        {
            return Infeasible(
                data, plan, required + reserve, at,
                $"La data dell'esame lascia meno di un giorno utile prima del margine di {data.PlannerSettings.ReviewReserveDays} giorni.");
        }

        var enabledWindows = data.PlannerSettings.Availability
            .Where(x => x.IsEnabled)
            .Where(x => data.PlannerSettings.IncludeWeekends ||
                        x.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToList();
        if (enabledWindows.Count == 0)
        {
            return Infeasible(
                data, plan, required + reserve, at,
                "Nessuna disponibilità configurata: imposta almeno una finestra settimanale prima di generare il piano.");
        }

        var excludedPlanId = request.ReplaceExistingPlan ? existing?.Id : null;
        var busy = data.CalendarEvents
            .Where(x => !x.IsCancelled)
            .Where(x => excludedPlanId is null || !string.Equals(x.PlanId, excludedPlanId, StringComparison.Ordinal))
            .ToList();
        var work = topics
            .OrderByDescending(x => PriorityScore(x))
            .ThenBy(x => x.Position)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new WorkItem(x, x.Title, x.EstimatedMinutes, PriorityScore(x), false))
            .ToList();
        if (reserve > 0)
            work.Add(new WorkItem(null, $"Ripasso: {course.Name}", reserve, 0, true));

        var dailyMinutes = new Dictionary<DateOnly, int>();
        var dailySessions = new Dictionary<DateOnly, int>();
        foreach (var item in busy.Where(x => x.Source == CalendarEventSource.Planner && !x.IsCancelled))
        {
            var day = DateOnly.FromDateTime(item.StartsAt.LocalDateTime);
            dailyMinutes[day] = dailyMinutes.GetValueOrDefault(day) + DurationMinutes(item);
            dailySessions[day] = dailySessions.GetValueOrDefault(day) + 1;
        }

        var allocations = new List<Allocation>();
        var maxDailyMinutes = data.MaxDailyStudyMinutes > 0 ? data.MaxDailyStudyMinutes : int.MaxValue;
        for (var day = today; day <= lastStudyDay; day = day.AddDays(1))
        {
            var windows = FreeWindows(data, day, enabledWindows, busy, at);
            var dayStart = AtLocal(day, TimeOnly.MinValue);
            var dayEnd = AtLocal(day.AddDays(1), TimeOnly.MinValue);
            if (dayEnd <= at) continue;
            var remainingDailyMinutes = Math.Max(0, maxDailyMinutes - dailyMinutes.GetValueOrDefault(day));
            var remainingDailySessions = Math.Max(0,
                data.PlannerSettings.MaxSessionsPerDay - dailySessions.GetValueOrDefault(day));
            if (remainingDailyMinutes < 1 || remainingDailySessions < 1) continue;

            foreach (var window in windows)
            {
                var cursor = window.Start < at ? at : window.Start;
                while (cursor < window.End && remainingDailySessions > 0 && remainingDailyMinutes > 0)
                {
                    var item = work.FirstOrDefault(x => x.RemainingMinutes > 0);
                    if (item is null) break;
                    var available = (int)Math.Floor((window.End - cursor).TotalMinutes);
                    if (available <= 0) break;
                    var take = Math.Min(data.PlannerSettings.PreferredSessionMinutes, item.RemainingMinutes);
                    take = Math.Min(take, available);
                    take = Math.Min(take, remainingDailyMinutes);
                    if (take <= 0) break;

                    // A short final tail is allowed; a short arbitrary slice is
                    // not. This avoids sessions that are too small to be useful.
                    var isFinalTail = take == item.RemainingMinutes;
                    if (take < data.PlannerSettings.MinimumSessionMinutes && !isFinalTail) break;

                    var start = cursor;
                    var end = start.AddMinutes(take);
                    allocations.Add(new Allocation(item, start, end, take));
                    item.RemainingMinutes -= take;
                    remainingDailyMinutes -= take;
                    remainingDailySessions--;
                    dailyMinutes[day] = dailyMinutes.GetValueOrDefault(day) + take;
                    dailySessions[day] = dailySessions.GetValueOrDefault(day) + 1;
                    cursor = end.AddMinutes(data.PlannerSettings.BreakBetweenSessionsMinutes);
                }
                if (remainingDailySessions < 1 || remainingDailyMinutes < 1) break;
            }
        }

        var scheduled = allocations.Sum(x => x.Minutes);
        var expected = required + reserve;
        if (scheduled < expected)
        {
            var missing = expected - scheduled;
            return Infeasible(
                data, plan, missing, at,
                $"Mancano {missing} minuti di disponibilità prima del margine d'esame. " +
                "Il piano non è stato creato: aggiungi finestre disponibili o riduci il carico stimato.");
        }

        var events = new List<CalendarEvent>();
        for (var index = 0; index < allocations.Count; index++)
        {
            var allocation = allocations[index];
            var topicTitle = allocation.Item.Topic?.Title ?? allocation.Item.Title;
            var session = new CalendarEvent
            {
                Id = $"plan:{plan.Id}:{index:D3}",
                Title = Truncate(
                    allocation.Item.IsReview ? allocation.Item.Title : $"Studio: {topicTitle}", 400),
                Category = allocation.Item.IsReview ? CalendarEventCategory.Review : CalendarEventCategory.Study,
                StartsAt = allocation.Start,
                EndsAt = allocation.End,
                CourseId = course.Id,
                CourseName = course.Name,
                Source = CalendarEventSource.Planner,
                Verification = CalendarEventVerification.Indicative,
                TimeZoneId = TimeZoneInfo.Local.Id,
                ExternalId = $"planner:{plan.Id}:{index:D3}",
                PlanId = plan.Id,
                SyllabusTopicId = allocation.Item.Topic?.Id ?? string.Empty,
                PlannerStatus = PlannedSessionStatus.Scheduled,
                PriorityScore = allocation.Item.PriorityScore,
                Notes = allocation.Item.IsReview
                    ? "Ripasso proposto dal pianificatore. Non è un appello o un impegno ufficiale."
                    : "Sessione proposta dal pianificatore. Non è un impegno ufficiale.",
                CreatedAt = at,
                UpdatedAt = at
            };
            CalendarService.Validate(session);
            events.Add(session);
        }

        if (existing is not null) ArchivePlanInternal(data, existing, at);
        plan.ScheduledMinutes = scheduled;
        plan.Status = StudyPlanStatus.Active;
        data.StudyPlans.Add(plan);
        foreach (var session in events) data.CalendarEvents.Add(session);
        return new PlannerResult(plan, events, 0);
    }

    public IReadOnlyList<CalendarEvent> GetPlanSessions(StudyData data, string planId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.CalendarEvents
            .Where(x => string.Equals(x.PlanId, planId, StringComparison.Ordinal) && !x.IsCancelled)
            .OrderBy(x => x.StartsAt)
            .ToList();
    }

    /// <summary>
    /// Marks expired planner sessions as missed and, when applying, moves each
    /// one to the first free slot before its plan deadline. Lectures, exams and
    /// completed sessions are never moved. The move is recorded append-only.
    /// </summary>
    public IReadOnlyList<PlannerRescheduleProposal> RescheduleMissed(
        StudyData data,
        DateTimeOffset? now = null,
        bool? apply = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var shouldApply = apply ?? data.PlannerSettings.AutoRescheduleMissed;
        var plans = data.StudyPlans.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var missed = data.CalendarEvents
            .Where(x => !x.IsCancelled &&
                        x.Source == CalendarEventSource.Planner &&
                        x.PlannerStatus == PlannedSessionStatus.Scheduled &&
                        x.EndsAt < at &&
                        !string.IsNullOrWhiteSpace(x.PlanId) &&
                        plans.TryGetValue(x.PlanId, out var plan) &&
                        plan.Status is StudyPlanStatus.Active)
            .OrderBy(x => x.EndsAt)
            .ToList();
        if (!shouldApply)
        {
            return missed.Select(x => new PlannerRescheduleProposal(
                x, x.StartsAt, x.EndsAt, null, null,
                "Sessione scaduta non completata. Anteprima: nessuna modifica applicata.")).ToList();
        }

        var proposals = new List<PlannerRescheduleProposal>();
        var previewBusy = new List<CalendarEvent>();
        foreach (var session in missed)
        {
            var plan = plans[session.PlanId];
            session.PlannerStatus = PlannedSessionStatus.Missed;
            session.UpdatedAt = at;
            var slot = FindNextSlot(data, plan, session, at, previewBusy);
            if (slot is null)
            {
                session.PlannerStatus = PlannedSessionStatus.NeedsAttention;
                AddWarning(plan, $"Nessuno slot libero per la sessione \"{session.Title}\" prima dell'esame.");
                proposals.Add(new PlannerRescheduleProposal(
                    session, session.StartsAt, session.EndsAt, null, null,
                    "Nessuno slot compatibile prima del margine d'esame."));
                continue;
            }

            var previousStart = session.StartsAt;
            var previousEnd = session.EndsAt;
            session.StartsAt = slot.Value.Start;
            session.EndsAt = slot.Value.End;
            session.PlannerStatus = PlannedSessionStatus.Scheduled;
            session.RescheduleCount++;
            session.UpdatedAt = at;
            data.PlannerReschedules.Add(new PlannerRescheduleData
            {
                PlanId = plan.Id,
                CalendarEventId = session.Id,
                PreviousStartsAt = previousStart,
                PreviousEndsAt = previousEnd,
                NewStartsAt = session.StartsAt,
                NewEndsAt = session.EndsAt,
                Reason = "Sessione scaduta non completata",
                MovedAt = at
            });
            previewBusy.Add(session);
            proposals.Add(new PlannerRescheduleProposal(
                session, previousStart, previousEnd, session.StartsAt, session.EndsAt,
                "Sessione spostata in un nuovo slot libero."));
        }
        return proposals;
    }

    /// <summary>
    /// Reverts a move only while the previous slot is still collision-free. The
    /// audit row is retained and marked reversed; it is never deleted.
    /// </summary>
    public CalendarEvent RevertReschedule(
        StudyData data,
        string rescheduleId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var history = data.PlannerReschedules.FirstOrDefault(x =>
            string.Equals(x.Id, rescheduleId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The reschedule record does not exist.", nameof(rescheduleId));
        if (history.IsReversed)
            throw new InvalidOperationException("This reschedule has already been reverted.");
        var session = data.CalendarEvents.FirstOrDefault(x =>
            string.Equals(x.Id, history.CalendarEventId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The rescheduled session no longer exists.");
        if (session.StartsAt != history.NewStartsAt || session.EndsAt != history.NewEndsAt)
            throw new InvalidOperationException("The session changed after the reschedule and cannot be reverted safely.");
        if (data.CalendarEvents.Any(x =>
                !x.IsCancelled &&
                !string.Equals(x.Id, session.Id, StringComparison.Ordinal) &&
                x.StartsAt < history.PreviousEndsAt &&
                x.EndsAt > history.PreviousStartsAt))
            throw new InvalidOperationException("The previous slot is no longer free.");

        var at = now ?? DateTimeOffset.Now;
        session.StartsAt = history.PreviousStartsAt;
        session.EndsAt = history.PreviousEndsAt;
        session.PlannerStatus = history.PreviousStartsAt < at
            ? PlannedSessionStatus.Missed
            : PlannedSessionStatus.Scheduled;
        session.UpdatedAt = at;
        history.IsReversed = true;
        history.ReversedAt = at;
        return session;
    }

    public CalendarEvent CompletePlannedSession(
        StudyData data,
        string calendarEventId,
        int? actualMinutes = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var session = data.CalendarEvents.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.PlanId) &&
            string.Equals(x.Id, calendarEventId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The planner session does not exist.", nameof(calendarEventId));
        if (session.IsCancelled) throw new InvalidOperationException("A cancelled session cannot be completed.");
        var at = now ?? DateTimeOffset.Now;
        var minutes = actualMinutes ?? DurationMinutes(session);
        if (minutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(actualMinutes), "Actual minutes must be between 0 and 1440.");
        if (session.PlannerStatus == PlannedSessionStatus.Completed) return session;

        session.PlannerStatus = PlannedSessionStatus.Completed;
        session.ActualMinutes = minutes;
        session.CompletedAt = at;
        session.UpdatedAt = at;

        var plan = data.StudyPlans.FirstOrDefault(x => string.Equals(x.Id, session.PlanId, StringComparison.Ordinal));
        if (plan is not null)
        {
            UpdateTopicAfterCompletion(data, session, at);
            var sessions = GetPlanSessions(data, plan.Id);
            if (sessions.Count > 0 && sessions.All(x => x.PlannerStatus == PlannedSessionStatus.Completed))
                plan.Status = StudyPlanStatus.Completed;
            plan.UpdatedAt = at;
        }
        return session;
    }

    /// <summary>
    /// Links the existing persistent focus timer to a planned session. The timer
    /// remains timestamp-based and survives a restart; no second countdown is
    /// introduced.
    /// </summary>
    public StudySessionData StartFocus(
        StudyData data,
        string calendarEventId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var session = data.CalendarEvents.FirstOrDefault(x =>
            string.Equals(x.Id, calendarEventId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(x.PlanId))
            ?? throw new ArgumentException("The planner session does not exist.", nameof(calendarEventId));
        if (session.IsCancelled || session.PlannerStatus == PlannedSessionStatus.Cancelled)
            throw new InvalidOperationException("A cancelled session cannot be started.");
        var at = now ?? DateTimeOffset.Now;
        var topic = data.SyllabusTopics.FirstOrDefault(x =>
            string.Equals(x.Id, session.SyllabusTopicId, StringComparison.Ordinal));
        var focus = _focus.Start(
            data,
            string.IsNullOrWhiteSpace(session.CourseId) ? null : session.CourseId,
            topic?.Title ?? session.Title,
            Math.Clamp(DurationMinutes(session), 1, 1440),
            at);
        focus.SyllabusTopicId = session.SyllabusTopicId;
        focus.CalendarEventId = session.Id;
        return focus;
    }

    public StudySessionData FinishFocus(
        StudyData data,
        string focusSessionId,
        string? outcome = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var focus = data.StudySessions.FirstOrDefault(x =>
            string.Equals(x.Id, focusSessionId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The focus session does not exist.", nameof(focusSessionId));
        var at = now ?? DateTimeOffset.Now;
        var finished = _focus.Finish(data, focusSessionId, outcome, at);
        _ledger.SyncFocusSession(data, finished, at);
        if (!string.IsNullOrWhiteSpace(finished.CalendarEventId))
        {
            var minutes = Math.Max(0, FocusSessionService.ElapsedSeconds(finished, at) / 60);
            CompletePlannedSession(data, finished.CalendarEventId, minutes, at);
        }
        return finished;
    }

    public StudySessionData CancelFocus(
        StudyData data,
        string focusSessionId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var focus = data.StudySessions.FirstOrDefault(x =>
            string.Equals(x.Id, focusSessionId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The focus session does not exist.", nameof(focusSessionId));
        var at = now ?? DateTimeOffset.Now;
        var cancelled = _focus.Cancel(data, focusSessionId, at);
        _ledger.SyncFocusSession(data, cancelled, at);
        if (!string.IsNullOrWhiteSpace(cancelled.CalendarEventId))
        {
            var session = data.CalendarEvents.FirstOrDefault(x =>
                string.Equals(x.Id, cancelled.CalendarEventId, StringComparison.Ordinal));
            if (session is not null && session.PlannerStatus != PlannedSessionStatus.Completed)
            {
                session.PlannerStatus = PlannedSessionStatus.NeedsAttention;
                session.UpdatedAt = at;
            }
        }
        return cancelled;
    }

    public PlannerDashboard BuildDashboard(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var day = DateOnly.FromDateTime(at.LocalDateTime);
        var dayStart = AtLocal(day, TimeOnly.MinValue);
        var dayEnd = AtLocal(day.AddDays(1), TimeOnly.MinValue);
        var todayEvents = CalendarService.GetEventsInRange(data.CalendarEvents, dayStart, dayEnd);
        var horizon = at.AddDays(30);
        var nextEvents = data.CalendarEvents
            .Where(x => !x.IsCancelled && x.EndsAt > at && x.StartsAt <= horizon)
            .OrderBy(x => x.StartsAt)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();
        var nextSessions = data.CalendarEvents
            .Where(x => !x.IsCancelled &&
                        !string.IsNullOrWhiteSpace(x.PlanId) &&
                        x.PlannerStatus == PlannedSessionStatus.Scheduled &&
                        x.EndsAt > at)
            .OrderBy(x => x.StartsAt)
            .Take(5)
            .ToList();
        var nextExam = data.Exams
            .Where(x => !x.Completed && x.PlannedDate is not null && x.PlannedDate.Value.Date >= day.ToDateTime(TimeOnly.MinValue))
            .OrderBy(x => x.PlannedDate)
            .FirstOrDefault();
        var weekStart = CalendarService.StartOfWeek(day.ToDateTime(TimeOnly.MinValue));
        var progress = new ProgressService().Calculate(
            data,
            DateOnly.FromDateTime(weekStart),
            DateOnly.FromDateTime(weekStart.AddDays(6)),
            at);
        var courses = data.Courses
            .Where(x => !x.Archived)
            .Select(course =>
            {
                var topics = data.SyllabusTopics
                    .Where(x => string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) && !x.IsArchived)
                    .ToList();
                var sessions = data.CalendarEvents
                    .Where(x => !x.IsCancelled && string.Equals(x.CourseId, course.Id, StringComparison.Ordinal) &&
                                !string.IsNullOrWhiteSpace(x.PlanId))
                    .ToList();
                return new CoursePlanProgress(
                    course.Id,
                    course.Name,
                    topics.Count(x => x.Status is SyllabusTopicStatus.Completed),
                    topics.Count,
                    topics.Where(x => x.Status != SyllabusTopicStatus.Completed)
                        .Sum(x => x.EstimatedMinutes),
                    sessions.Sum(DurationMinutes),
                    sessions.Where(x => x.PlannerStatus == PlannedSessionStatus.Completed)
                        .Sum(x => x.ActualMinutes));
            })
            .OrderBy(x => x.CourseName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var warnings = data.StudyPlans
            .Where(x => x.Status is StudyPlanStatus.Active or StudyPlanStatus.InsufficientTime)
            .SelectMany(x => x.Warnings)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PlannerDashboard(
            day, todayEvents, nextEvents, nextSessions, nextExam, courses,
            progress.RecordedMinutes, warnings);
    }

    private PlannerResult Infeasible(
        StudyData data,
        StudyPlanData plan,
        int deficit,
        DateTimeOffset at,
        string warning)
    {
        plan.Status = StudyPlanStatus.InsufficientTime;
        plan.ScheduledMinutes = 0;
        plan.Warnings.Add(warning);
        plan.UpdatedAt = at;
        data.StudyPlans.Add(plan);
        return new PlannerResult(plan, Array.Empty<CalendarEvent>(), Math.Max(0, deficit));
    }

    private static StudyPlanData NewPlan(
        Course course,
        Exam? exam,
        DateTime examDate,
        DateTimeOffset at) => new()
    {
        CourseId = course.Id,
        CourseName = course.Name,
        ExamId = exam?.Id ?? string.Empty,
        ExamTitle = exam?.Name ?? string.Empty,
        ExamDate = examDate,
        CreatedAt = at,
        UpdatedAt = at
    };

    private static List<SyllabusTopicData> SelectTopics(
        StudyData data,
        string courseId,
        IReadOnlyCollection<string> requestedIds)
    {
        var available = data.SyllabusTopics
            .Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal) && !x.IsArchived)
            .Where(x => x.Status != SyllabusTopicStatus.Completed)
            .ToList();
        if (requestedIds.Count == 0) return available.Where(x => x.EstimatedMinutes > 0).ToList();
        var requested = new HashSet<string>(requestedIds, StringComparer.Ordinal);
        if (requested.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A requested syllabus topic id is empty.", nameof(requestedIds));
        var missing = requested.Where(id => available.All(x => !string.Equals(x.Id, id, StringComparison.Ordinal))).ToList();
        if (missing.Count > 0)
            throw new ArgumentException("A requested topic does not belong to the course or is already completed.",
                nameof(requestedIds));
        return available.Where(x => requested.Contains(x.Id) && x.EstimatedMinutes > 0).ToList();
    }

    private static int PriorityScore(SyllabusTopicData topic)
    {
        var preparation = topic.PreparationLevel ?? 3;
        var priority = topic.Priority is < 1 or > 5 ? 3 : topic.Priority;
        var difficulty = topic.Difficulty is < 1 or > 5 ? 3 : topic.Difficulty;
        // Higher means earlier. Transparency is part of the contract.
        return difficulty * 3 + (6 - preparation) * 2 + priority;
    }

    private static void ValidateSettings(PlannerSettingsData settings)
    {
        if (settings.PreferredSessionMinutes is < 15 or > 240)
            throw new ArgumentOutOfRangeException(nameof(settings), "Preferred session minutes must be between 15 and 240.");
        if (settings.MinimumSessionMinutes is < 10 or > 240 ||
            settings.MinimumSessionMinutes > settings.PreferredSessionMinutes)
            throw new ArgumentOutOfRangeException(nameof(settings), "Minimum session minutes are outside the supported range.");
        if (settings.BreakBetweenSessionsMinutes is < 0 or > 60 ||
            settings.MaxSessionsPerDay is < 1 or > 12 ||
            settings.ReviewReservePercent is < 0 or > 100 ||
            settings.ReviewReserveDays is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(settings), "Planner settings are outside the supported range.");
        if (settings.Availability.Count > 28)
            throw new ArgumentOutOfRangeException(nameof(settings), "Too many availability windows.");
        foreach (var window in settings.Availability)
        {
            if (!Enum.IsDefined(window.DayOfWeek) || window.EndsAt <= window.StartsAt)
                throw new ArgumentException("An availability window has an invalid day or interval.", nameof(settings));
        }
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> FreeWindows(
        StudyData data,
        DateOnly day,
        IReadOnlyList<PlannerAvailabilityWindowData> availability,
        IReadOnlyList<CalendarEvent> busy,
        DateTimeOffset now)
    {
        var windows = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var item in availability.Where(x => x.DayOfWeek == day.DayOfWeek))
        {
            var start = AtLocal(day, item.StartsAt);
            var end = AtLocal(day, item.EndsAt);
            if (end <= start) continue;
            if (start < now) start = now;
            if (end <= start) continue;
            windows.Add((start, end));
        }
        if (windows.Count == 0) return windows;
        windows.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var window in windows)
        {
            if (merged.Count == 0) { merged.Add(window); continue; }
            var last = merged[^1];
            if (window.Start <= last.End)
                merged[^1] = (last.Start, window.End > last.End ? window.End : last.End);
            else
                merged.Add(window);
        }

        var buffer = TimeSpan.FromMinutes(data.PlannerSettings.BreakBetweenSessionsMinutes);
        var padded = busy
            .Where(x => !x.IsCancelled)
            .Select(x => (Start: x.StartsAt - buffer, End: x.EndsAt + buffer))
            .OrderBy(x => x.Start)
            .ToList();
        var free = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var window in merged)
        {
            var cursor = window.Start;
            foreach (var occupied in padded)
            {
                if (occupied.End <= cursor) continue;
                if (occupied.Start >= window.End) break;
                if (occupied.Start > cursor)
                    free.Add((cursor, occupied.Start < window.End ? occupied.Start : window.End));
                if (occupied.End > cursor) cursor = occupied.End;
                if (cursor >= window.End) break;
            }
            if (cursor < window.End) free.Add((cursor, window.End));
        }
        return free.Where(x => x.End > x.Start).ToList();
    }

    private static (DateTimeOffset Start, DateTimeOffset End)? FindNextSlot(
        StudyData data,
        StudyPlanData plan,
        CalendarEvent moved,
        DateTimeOffset now,
        IReadOnlyList<CalendarEvent> previewBusy)
    {
        var settings = data.PlannerSettings;
        var duration = Math.Max(1, DurationMinutes(moved));
        var deadline = DateOnly.FromDateTime(plan.ExamDate.AddDays(-settings.ReviewReserveDays)).AddDays(-1);
        var firstDay = DateOnly.FromDateTime(now.LocalDateTime);
        if (deadline < firstDay) return null;
        var busy = data.CalendarEvents
            .Where(x => !x.IsCancelled && !string.Equals(x.Id, moved.Id, StringComparison.Ordinal))
            .Concat(previewBusy)
            .ToList();
        var plannerCount = busy
            .Where(x => x.Source == CalendarEventSource.Planner)
            .GroupBy(x => DateOnly.FromDateTime(x.StartsAt.LocalDateTime))
            .ToDictionary(g => g.Key, g => g.Count());
        var plannerMinutes = busy
            .Where(x => x.Source == CalendarEventSource.Planner)
            .GroupBy(x => DateOnly.FromDateTime(x.StartsAt.LocalDateTime))
            .ToDictionary(g => g.Key, g => g.Sum(DurationMinutes));
        var maxDaily = data.MaxDailyStudyMinutes > 0 ? data.MaxDailyStudyMinutes : int.MaxValue;
        for (var day = firstDay; day <= deadline; day = day.AddDays(1))
        {
            if (!settings.IncludeWeekends && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (plannerCount.GetValueOrDefault(day) >= settings.MaxSessionsPerDay) continue;
            if (plannerMinutes.GetValueOrDefault(day) + duration > maxDaily) continue;
            var windows = FreeWindows(data, day, settings.Availability, busy, now);
            foreach (var window in windows)
            {
                if ((window.End - window.Start).TotalMinutes >= duration)
                    return (window.Start, window.Start.AddMinutes(duration));
            }
        }
        return null;
    }

    private static void ArchivePlanInternal(StudyData data, StudyPlanData plan, DateTimeOffset at)
    {
        plan.Status = StudyPlanStatus.Archived;
        plan.UpdatedAt = at;
        foreach (var session in data.CalendarEvents.Where(x =>
                     string.Equals(x.PlanId, plan.Id, StringComparison.Ordinal) &&
                     x.PlannerStatus != PlannedSessionStatus.Completed))
        {
            session.PlannerStatus = PlannedSessionStatus.Cancelled;
            session.IsCancelled = true;
            session.UpdatedAt = at;
        }
    }

    private static void UpdateTopicAfterCompletion(StudyData data, CalendarEvent session, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(session.SyllabusTopicId)) return;
        var topic = data.SyllabusTopics.FirstOrDefault(x =>
            string.Equals(x.Id, session.SyllabusTopicId, StringComparison.Ordinal));
        if (topic is null) return;
        topic.LastStudiedAt = at.LocalDateTime;
        var topicSessions = data.CalendarEvents
            .Where(x => !x.IsCancelled &&
                        string.Equals(x.SyllabusTopicId, topic.Id, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(x.PlanId))
            .ToList();
        if (topicSessions.Count > 0 && topicSessions.All(x => x.PlannerStatus == PlannedSessionStatus.Completed) &&
            topicSessions.Sum(x => x.ActualMinutes) >= topic.EstimatedMinutes)
            topic.Status = SyllabusTopicStatus.Completed;
        else
            topic.Status = SyllabusTopicStatus.InProgress;
        topic.UpdatedAt = at.LocalDateTime;
    }

    private static void AddWarning(StudyPlanData plan, string warning)
    {
        if (!plan.Warnings.Contains(warning, StringComparer.CurrentCultureIgnoreCase))
            plan.Warnings.Add(warning);
    }

    private static string BuildExplanation(PlannerSettingsData settings, int required, int reserve) =>
        $"Carico stimato {required} minuti + riserva di ripasso {reserve} minuti " +
        $"({settings.ReviewReservePercent}%). Priorità: difficoltà×3 + (6−preparazione)×2 + priorità. " +
        $"Sessioni da {settings.MinimumSessionMinutes}–{settings.PreferredSessionMinutes} minuti, " +
        $"pausa {settings.BreakBetweenSessionsMinutes} minuti, massimo {settings.MaxSessionsPerDay} sessioni al giorno. " +
        $"Nessuna sessione negli ultimi {settings.ReviewReserveDays} giorni prima dell'esame.";

    private static DateTimeOffset AtLocal(DateOnly day, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static int DurationMinutes(CalendarEvent item) =>
        Math.Max(0, (int)Math.Floor((item.EndsAt - item.StartsAt).TotalMinutes));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class WorkItem(
        SyllabusTopicData? topic,
        string title,
        int remainingMinutes,
        int priorityScore,
        bool isReview)
    {
        public SyllabusTopicData? Topic { get; } = topic;
        public string Title { get; } = title;
        public int RemainingMinutes { get; set; } = remainingMinutes;
        public int PriorityScore { get; } = priorityScore;
        public bool IsReview { get; } = isReview;
    }

    private sealed record Allocation(
        WorkItem Item,
        DateTimeOffset Start,
        DateTimeOffset End,
        int Minutes);
}
