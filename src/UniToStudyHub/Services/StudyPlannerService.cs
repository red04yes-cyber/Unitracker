using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Builds and applies deterministic study plans using the user's explicit
/// recurring availability, existing commitments, task estimates and study
/// budgets. Preview is side-effect free; Apply writes indicative CalendarEvents.
/// </summary>
public sealed class StudyPlannerService
{
    private const string GeneratedExternalIdPrefix = "study-plan:v1:";
    private static readonly TimeSpan PreviewStartGrace = TimeSpan.FromMinutes(2);
    private readonly ActivityLedgerService _ledger = new();

    public StudyPlanPreview CreatePreview(
        StudyData data,
        StudyPlannerOptions options,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        var now = generatedAt ?? DateTimeOffset.Now;
        var normalized = ValidateOptions(options);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(normalized.TimeZoneId);
        var weeklyBudget = normalized.WeeklyStudyMinutes ??
            (int)Math.Clamp((long)data.WeeklyStudyHours * 60L, 0L, 10080L);
        var dailyBudget = normalized.MaxDailyStudyMinutes ?? Math.Clamp(data.MaxDailyStudyMinutes, 0, 1440);
        var warnings = new List<string>();

        if (normalized.WeeklyStudyMinutes is null && (data.WeeklyStudyHours is < 0 or > 168))
            warnings.Add("Le ore settimanali salvate erano fuori dal limite 0–168 e sono state limitate per questa anteprima.");
        if (normalized.MaxDailyStudyMinutes is null && (data.MaxDailyStudyMinutes is < 0 or > 1440))
            warnings.Add("Il limite giornaliero salvato era fuori dal limite 0–1440 e sono stati applicati i limiti supportati.");

        if (normalized.WeeklyAvailability.Count == 0)
            warnings.Add("Nessuna disponibilità settimanale configurata: il planner non inventa fasce orarie.");
        if (weeklyBudget == 0)
            warnings.Add("Il budget settimanale è zero: aumenta le ore disponibili nelle impostazioni o nelle opzioni.");
        if (dailyBudget == 0)
            warnings.Add("Il limite giornaliero è zero: non è possibile pianificare blocchi di studio.");

        var commitments = data.CalendarEvents
            .Where(x => x is not null && !x.IsCancelled)
            .Where(x => !normalized.ReplaceExistingGeneratedBlocks || !IsUnchangedGeneratedPlannerBlock(x))
            .OrderBy(x => x.StartsAt)
            .ToList();

        int RecordedMinutes(DateOnly from, DateOnly to) =>
            _ledger.Summarize(data, from, to, now, normalized.TimeZoneId).RecordedSeconds / 60;

        int FutureStudyMinutes(DateOnly from, DateOnly to)
        {
            var start = StartOfLocalDay(from, zone);
            var end = StartOfLocalDay(to.AddDays(1), zone);
            var intervals = commitments
                .Where(IsStudyCommitment)
                .Select(x => new TimeRange(x.StartsAt < now ? now : x.StartsAt, x.EndsAt))
                .Where(x => x.End > x.Start && x.End > start && x.Start < end)
                .Select(x => new TimeRange(x.Start < start ? start : x.Start, x.End > end ? end : x.End));
            return UnionMinutes(intervals);
        }

        var dailyUsed = new Dictionary<DateOnly, int>();
        var weeklyUsed = new Dictionary<DateOnly, int>();
        var recordedByWeek = new Dictionary<DateOnly, int>();
        var futureStudyByWeek = new Dictionary<DateOnly, int>();
        for (var day = normalized.From; day <= normalized.To; day = day.AddDays(1))
        {
            var week = MondayOf(day);
            dailyUsed[day] = RecordedMinutes(day, day) + FutureStudyMinutes(day, day);
            if (!recordedByWeek.ContainsKey(week))
            {
                recordedByWeek[week] = RecordedMinutes(week, week.AddDays(6));
                futureStudyByWeek[week] = FutureStudyMinutes(week, week.AddDays(6));
                weeklyUsed[week] = recordedByWeek[week] + futureStudyByWeek[week];
            }
        }

        var freeByDay = new Dictionary<DateOnly, List<TimeRange>>();
        for (var day = normalized.From; day <= normalized.To; day = day.AddDays(1))
        {
            var windows = BuildAvailability(day, normalized.WeeklyAvailability, zone, warnings);
            var free = new List<TimeRange>();
            foreach (var window in MergeRanges(windows))
            {
                // Never schedule into the past, including when the requested
                // planning range starts before today's local date.
                var start = window.Start < now ? now : window.Start;
                if (window.End <= start) continue;
                free.AddRange(SubtractCommitments(new TimeRange(start, window.End), commitments));
            }
            freeByDay[day] = free.OrderBy(x => x.Start).ToList();
        }

        var blocks = new List<StudyPlanBlock>();
        var unscheduled = new List<StudyPlanUnscheduledTask>();
        var blocksByDay = new Dictionary<DateOnly, int>();

        var tasks = data.StudyTasks
            .Where(x => x is not null && (x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress))
            .OrderBy(x => x.DueAt?.UtcDateTime ?? DateTime.MaxValue)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var taskProgressById = _ledger.GetTaskProgressSnapshots(
            data,
            tasks.Where(x => !string.IsNullOrWhiteSpace(x.Id)).Select(x => x.Id),
            now);
        var taskProgressRows = tasks
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && taskProgressById.ContainsKey(x.Id))
            .Select(x => taskProgressById[x.Id])
            .ToArray();

        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                unscheduled.Add(new StudyPlanUnscheduledTask(
                    task.Id,
                    task.Title,
                    Math.Max(0, task.EstimatedMinutes),
                    "Il task non ha un identificativo valido e non può essere collegato al calendario."));
                continue;
            }

            var progress = taskProgressById[task.Id];
            if (string.IsNullOrWhiteSpace(task.Title))
            {
                unscheduled.Add(new StudyPlanUnscheduledTask(task.Id, task.Title, progress.RemainingMinutes,
                    "Il task non ha un titolo valido."));
                continue;
            }
            if (task.EstimatedMinutes <= 0)
            {
                unscheduled.Add(new StudyPlanUnscheduledTask(task.Id, task.Title, 0,
                    "Manca una stima della durata: inserisci i minuti previsti prima di pianificarlo."));
                continue;
            }

            var remaining = progress.RemainingMinutes;
            if (remaining == 0)
            {
                var reason = progress.IsOverEstimate
                    ? "Il tempo registrato supera la stima, ma il task è ancora aperto: aggiorna la stima o completa il task."
                    : "La stima è stata interamente registrata, ma il task è ancora aperto: completa il task o aggiorna la stima.";
                unscheduled.Add(new StudyPlanUnscheduledTask(task.Id, task.Title, 0, reason));
                warnings.Add(reason);
                continue;
            }

            if (task.DueAt is { } dueAt && dueAt < now)
                warnings.Add($"Il task \"{task.Title}\" è già oltre la scadenza: i blocchi proposti sono recuperi, non rispettano la scadenza originale.");

            var courseName = data.Courses.FirstOrDefault(x =>
                x is not null && string.Equals(x.Id, task.CourseId, StringComparison.Ordinal))?.Name ?? string.Empty;

            for (var day = normalized.From; day <= normalized.To && remaining > 0; day = day.AddDays(1))
            {
                var week = MondayOf(day);
                var dayRemaining = dailyBudget - dailyUsed.GetValueOrDefault(day);
                var weekRemaining = weeklyBudget - weeklyUsed.GetValueOrDefault(week);
                if (dayRemaining <= 0 || weekRemaining <= 0 ||
                    blocksByDay.GetValueOrDefault(day) >= normalized.MaxBlocksPerDay)
                    continue;

                var free = freeByDay[day];
                for (var index = 0; index < free.Count && remaining > 0; index++)
                {
                    var range = free[index];
                    var start = range.Start < now ? now : range.Start;
                    if (start >= range.End) continue;

                    var rangeMinutes = (int)Math.Floor((range.End - start).TotalMinutes);
                    var maxMinutes = Math.Min(
                        normalized.PreferredBlockMinutes,
                        Math.Min(remaining, Math.Min(rangeMinutes, Math.Min(dayRemaining, weekRemaining))));

                    if (task.DueAt is { } deadline && deadline > now)
                    {
                        if (start >= deadline) continue;
                        var beforeDeadline = (int)Math.Floor((deadline - start).TotalMinutes);
                        maxMinutes = Math.Min(maxMinutes, beforeDeadline);
                    }

                    if (maxMinutes <= 0) continue;
                    if (maxMinutes < normalized.MinimumBlockMinutes && maxMinutes < remaining)
                        continue;

                    var end = start.AddMinutes(maxMinutes);
                    if (task.DueAt is { } due && due > now && end > due) continue;

                    blocks.Add(new StudyPlanBlock(
                        task.Id,
                        task.Title,
                        task.CourseId,
                        courseName,
                        start,
                        end,
                        maxMinutes,
                        Math.Clamp(task.Priority, 1, 5),
                        task.DueAt));

                    remaining -= maxMinutes;
                    dailyUsed[day] = dailyUsed.GetValueOrDefault(day) + maxMinutes;
                    weeklyUsed[week] = weeklyUsed.GetValueOrDefault(week) + maxMinutes;
                    dayRemaining -= maxMinutes;
                    weekRemaining -= maxMinutes;
                    blocksByDay[day] = blocksByDay.GetValueOrDefault(day) + 1;

                    var nextStart = end.AddMinutes(normalized.BreakMinutes);
                    if (nextStart >= range.End)
                    {
                        free.RemoveAt(index);
                        index--;
                    }
                    else
                    {
                        free[index] = new TimeRange(nextStart, range.End);
                    }

                    if (blocksByDay[day] >= normalized.MaxBlocksPerDay || dayRemaining <= 0 || weekRemaining <= 0)
                        break;
                }
            }

            if (remaining > 0)
            {
                var reason = normalized.WeeklyAvailability.Count == 0
                    ? "Non hai configurato fasce di disponibilità."
                    : weeklyBudget == 0 || dailyBudget == 0
                        ? "Il budget settimanale o il limite giornaliero non consente altre sessioni."
                        : "Le fasce libere e i limiti giornalieri/settimanali non bastano entro il periodo selezionato.";
                unscheduled.Add(new StudyPlanUnscheduledTask(task.Id, task.Title, remaining, reason));
            }
        }

        if (blocks.Count == 0 && tasks.Count == 0)
            warnings.Add("Non ci sono task aperti da pianificare.");
        if (unscheduled.Count > 0)
            warnings.Add($"{unscheduled.Count} task non sono stati pianificati completamente.");

        return new StudyPlanPreview(
            normalized.From,
            normalized.To,
            normalized.TimeZoneId,
            now,
            weeklyBudget,
            dailyBudget,
            normalized,
            blocks.OrderBy(x => x.StartsAt).ThenByDescending(x => x.Priority).ToArray(),
            unscheduled,
            warnings.Distinct(StringComparer.Ordinal).ToArray())
        {
            TaskProgress = taskProgressRows
        };
    }

    /// <summary>
    /// Adds preview blocks to the shared calendar snapshot. Existing commitments
    /// are rechecked because the calendar may have changed after preview.
    /// </summary>
    public StudyPlanApplyResult Apply(StudyData data, StudyPlanPreview preview, DateTimeOffset? appliedAt = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(preview.Options);
        ArgumentNullException.ThrowIfNull(preview.Blocks);
        ArgumentNullException.ThrowIfNull(preview.Warnings);
        ArgumentNullException.ThrowIfNull(preview.TaskProgress);

        var blocks = preview.Blocks.ToArray();
        if (blocks.Length > 3620)
            throw new ArgumentOutOfRangeException(nameof(preview), "A preview cannot contain more than 3,620 blocks.");
        var previewProgressByTask = new Dictionary<string, TaskProgressSnapshot>(StringComparer.Ordinal);
        foreach (var snapshot in preview.TaskProgress)
        {
            if (snapshot is null ||
                string.IsNullOrWhiteSpace(snapshot.TaskId) ||
                snapshot.EstimatedMinutes < 0 ||
                snapshot.RecordedSeconds < 0 ||
                snapshot.RemainingSeconds < 0 ||
                snapshot.ProgressPercent is < 0 or > 100 ||
                !previewProgressByTask.TryAdd(snapshot.TaskId, snapshot))
                throw new ArgumentException("The preview contains invalid or duplicate task progress.", nameof(preview));
        }

        var normalizedOptions = ValidateOptions(preview.Options);
        if (preview.From != normalizedOptions.From ||
            preview.To != normalizedOptions.To ||
            !string.Equals(preview.TimeZoneId, normalizedOptions.TimeZoneId, StringComparison.Ordinal))
            throw new ArgumentException("The preview metadata does not match its validated options.", nameof(preview));

        var now = appliedAt ?? DateTimeOffset.Now;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(normalizedOptions.TimeZoneId);
        var periodStart = StartOfLocalDay(preview.From, zone);
        var periodEnd = StartOfLocalDay(preview.To.AddDays(1), zone);
        var warnings = new List<string>(preview.Warnings);

        // A replacement is staged against the calendar without the unchanged
        // generated blocks that it intends to replace. Nothing is removed until
        // every proposed block has passed revalidation.
        var replaceableBlocks = normalizedOptions.ReplaceExistingGeneratedBlocks
            ? FindUnchangedGeneratedBlocks(data, preview.From, preview.To, normalizedOptions.TimeZoneId)
            : Array.Empty<CalendarEvent>();
        var replaceableSet = new HashSet<CalendarEvent>(replaceableBlocks);
        var existingTaskIds = data.StudyTasks
            .Where(x => x is not null)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var progressTaskIds = blocks
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.TaskId) && existingTaskIds.Contains(x.TaskId))
            .Select(x => x.TaskId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currentProgressByTask = _ledger.GetTaskProgressSnapshots(data, progressTaskIds, now);

        var plannedMinutesByTask = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var candidate in blocks)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.TaskId)) continue;
            plannedMinutesByTask[candidate.TaskId] =
                plannedMinutesByTask.GetValueOrDefault(candidate.TaskId) + candidate.PlannedMinutes;
        }

        var staged = new List<CalendarEvent>();
        var stagedExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var alreadyPresent = 0;
        var conflicts = 0;
        var skipped = 0;

        foreach (var block in blocks)
        {
            if (block is null)
            {
                skipped++;
                warnings.Add("Un blocco nullo nell'anteprima è stato ignorato.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(block.TaskId))
            {
                skipped++;
                warnings.Add("Un blocco senza identificativo task è stato ignorato.");
                continue;
            }

            var task = data.StudyTasks.FirstOrDefault(x =>
                x is not null && string.Equals(x.Id, block.TaskId, StringComparison.Ordinal));
            if (task is null || task.Status is StudyTaskStatus.Completed or StudyTaskStatus.Cancelled)
            {
                skipped++;
                warnings.Add($"Il task \"{block.Title}\" non esiste più o non è più aperto; blocco ignorato.");
                continue;
            }

            var currentPriority = Math.Clamp(task.Priority, 1, 5);
            if (!string.Equals(task.Title, block.Title, StringComparison.Ordinal) ||
                !string.Equals(task.CourseId, block.CourseId, StringComparison.Ordinal) ||
                task.DueAt != block.DueAt ||
                currentPriority != block.Priority)
            {
                skipped++;
                warnings.Add($"Il task \"{task.Title}\" è cambiato dopo l'anteprima; rigenera il piano prima di applicarlo.");
                continue;
            }

            if (previewProgressByTask.Count > 0 && !previewProgressByTask.ContainsKey(task.Id))
            {
                skipped++;
                warnings.Add($"L'anteprima non contiene il progresso aggiornato del task \"{task.Title}\"; rigenera il piano.");
                continue;
            }

            if (previewProgressByTask.TryGetValue(task.Id, out var expectedProgress) &&
                (!currentProgressByTask.TryGetValue(task.Id, out var currentProgress) ||
                 currentProgress != expectedProgress))
            {
                skipped++;
                warnings.Add($"La stima del task \"{task.Title}\" o il tempo registrato è cambiato dopo l'anteprima; rigenera il piano.");
                continue;
            }

            var allowedMinutes = previewProgressByTask.ContainsKey(task.Id)
                ? currentProgressByTask[task.Id].RemainingMinutes
                : Math.Max(0, task.EstimatedMinutes);
            if (plannedMinutesByTask.GetValueOrDefault(task.Id) > allowedMinutes)
            {
                skipped++;
                warnings.Add($"I blocchi proposti superano la stima residua del task \"{task.Title}\"; rigenera il piano.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(block.Title) ||
                block.PlannedMinutes is < 1 or > 180 ||
                block.EndsAt <= block.StartsAt ||
                block.EndsAt - block.StartsAt != TimeSpan.FromMinutes(block.PlannedMinutes) ||
                block.StartsAt < periodStart ||
                block.StartsAt >= periodEnd ||
                block.EndsAt > periodEnd ||
                block.StartsAt < now - PreviewStartGrace)
            {
                skipped++;
                warnings.Add($"Il blocco di \"{block.Title}\" è scaduto, fuori intervallo o non valido; rigenera il piano.");
                continue;
            }

            var externalId = BuildExternalId(block);
            if (stagedExternalIds.Contains(externalId) ||
                data.CalendarEvents.Any(x =>
                    x is not null &&
                    !replaceableSet.Contains(x) &&
                    string.Equals(x.ExternalId, externalId, StringComparison.Ordinal)))
            {
                alreadyPresent++;
                continue;
            }

            var overlapsExisting = data.CalendarEvents.Any(x =>
                x is not null &&
                !x.IsCancelled &&
                !replaceableSet.Contains(x) &&
                x.StartsAt < block.EndsAt &&
                x.EndsAt > block.StartsAt);
            var overlapsStaged = staged.Any(x =>
                x.StartsAt < block.EndsAt && x.EndsAt > block.StartsAt);
            if (overlapsExisting || overlapsStaged)
            {
                conflicts++;
                warnings.Add($"Il calendario è cambiato: il blocco di \"{block.Title}\" alle {block.StartsAt:t} è in conflitto e non è stato salvato.");
                continue;
            }

            var courseName = data.Courses.FirstOrDefault(x =>
                x is not null && string.Equals(x.Id, task.CourseId, StringComparison.Ordinal))?.Name ?? string.Empty;
            var item = new CalendarEvent
            {
                Id = BuildEventId(externalId),
                Title = task.Title,
                Category = CalendarEventCategory.Study,
                StartsAt = block.StartsAt,
                EndsAt = block.EndsAt,
                CourseId = string.IsNullOrWhiteSpace(task.CourseId) ? null : task.CourseId,
                CourseName = courseName,
                Source = CalendarEventSource.Planner,
                Verification = CalendarEventVerification.Indicative,
                TimeZoneId = normalizedOptions.TimeZoneId,
                ExternalId = externalId,
                Notes = $"Blocco di studio indicativo collegato al task {task.Id}.",
                CreatedAt = now,
                UpdatedAt = now
            };
            CalendarService.Validate(item);
            staged.Add(item);
            stagedExternalIds.Add(externalId);
        }

        if (normalizedOptions.ReplaceExistingGeneratedBlocks && blocks.Length == 0)
        {
            warnings.Add("L'anteprima non contiene blocchi: il piano esistente è stato conservato. Usa la rimozione esplicita per svuotarlo.");
            return new StudyPlanApplyResult(0, alreadyPresent, conflicts, skipped, warnings);
        }

        if (normalizedOptions.ReplaceExistingGeneratedBlocks && (conflicts > 0 || skipped > 0))
        {
            // A partial replacement could silently erase a valid previous plan
            // while applying only a subset of the new one. Abort it as a unit.
            skipped += staged.Count;
            warnings.Add("Sostituzione annullata: il piano precedente è stato conservato perché almeno un blocco non è più applicabile.");
            return new StudyPlanApplyResult(0, alreadyPresent, conflicts, skipped, warnings);
        }

        if (normalizedOptions.ReplaceExistingGeneratedBlocks)
        {
            foreach (var item in replaceableBlocks)
                data.CalendarEvents.Remove(item);
        }

        foreach (var item in staged)
            data.CalendarEvents.Add(item);

        return new StudyPlanApplyResult(staged.Count, alreadyPresent, conflicts, skipped, warnings);
    }

    /// <summary>
    /// Applies and persists the plan through the existing atomic SQLite store.
    /// If persistence fails, the in-memory calendar snapshot is restored too.
    /// </summary>
    public async Task<StudyPlanApplyResult> ApplyAndSaveAsync(
        StudyData data,
        StudyPlanPreview preview,
        IStudyDataStore store,
        DateTimeOffset? appliedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(store);

        var originalEvents = data.CalendarEvents.ToArray();
        try
        {
            var result = Apply(data, preview, appliedAt);
            await store.SaveAsync(data, cancellationToken);
            return result;
        }
        catch
        {
            data.CalendarEvents.Clear();
            foreach (var item in originalEvents)
                data.CalendarEvents.Add(item);
            throw;
        }
    }

    /// <summary>
    /// Explicitly removes only unmodified blocks generated by this planner in
    /// the selected range. User-edited planner blocks are preserved.
    /// </summary>
    public int ClearGeneratedPlan(
        StudyData data,
        DateOnly from,
        DateOnly to,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var remove = FindUnchangedGeneratedBlocks(data, from, to, timeZoneId);
        foreach (var item in remove)
            data.CalendarEvents.Remove(item);
        return remove.Length;
    }

    private static CalendarEvent[] FindUnchangedGeneratedBlocks(
        StudyData data,
        DateOnly from,
        DateOnly to,
        string? timeZoneId)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        if (to == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(to), "The plan end date cannot be DateOnly.MaxValue.");

        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local.Id : timeZoneId);
        var start = StartOfLocalDay(from, zone);
        var end = StartOfLocalDay(to.AddDays(1), zone);
        return data.CalendarEvents
            .Where(x => x is not null && IsUnchangedGeneratedPlannerBlock(x))
            .Where(x => x.StartsAt < end && x.EndsAt > start)
            .ToArray();
    }

    private static StudyPlannerOptions ValidateOptions(StudyPlannerOptions options)
    {
        if (options.To < options.From)
            throw new ArgumentOutOfRangeException(nameof(options), "The plan end date must not precede its start date.");
        if (options.To == DateOnly.MaxValue || options.To.DayNumber - options.From.DayNumber > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "A plan can cover at most 181 calendar days and cannot end at DateOnly.MaxValue.");
        if (options.PreferredBlockMinutes is < 15 or > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "Preferred block length must be between 15 and 180 minutes.");
        if (options.MinimumBlockMinutes is < 5 or > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "Minimum block length must be between 5 and 180 minutes.");
        if (options.BreakMinutes is < 0 or > 60)
            throw new ArgumentOutOfRangeException(nameof(options), "Break length must be between 0 and 60 minutes.");
        if (options.MaxBlocksPerDay is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum blocks per day must be between 1 and 20.");
        if (options.WeeklyStudyMinutes is < 0 or > 10080)
            throw new ArgumentOutOfRangeException(nameof(options), "Weekly study budget is outside the supported range.");
        if (options.MaxDailyStudyMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(options), "Daily study budget is outside the supported range.");

        var zoneId = string.IsNullOrWhiteSpace(options.TimeZoneId) ? TimeZoneInfo.Local.Id : options.TimeZoneId.Trim();
        _ = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var availability = options.WeeklyAvailability ?? Array.Empty<StudyAvailabilityWindow>();
        if (availability.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "A weekly schedule cannot contain more than 64 availability windows.");
        foreach (var window in availability)
        {
            if (window is null || !Enum.IsDefined(window.Day) || window.End <= window.Start)
                throw new ArgumentException("Availability windows must have a valid day and end after start.", nameof(options));
        }

        return new StudyPlannerOptions
        {
            From = options.From,
            To = options.To,
            TimeZoneId = zoneId,
            WeeklyAvailability = availability.ToArray(),
            WeeklyStudyMinutes = options.WeeklyStudyMinutes,
            MaxDailyStudyMinutes = options.MaxDailyStudyMinutes,
            PreferredBlockMinutes = options.PreferredBlockMinutes,
            MinimumBlockMinutes = options.MinimumBlockMinutes,
            BreakMinutes = options.BreakMinutes,
            MaxBlocksPerDay = options.MaxBlocksPerDay,
            ReplaceExistingGeneratedBlocks = options.ReplaceExistingGeneratedBlocks
        };
    }

    private static List<TimeRange> BuildAvailability(
        DateOnly day,
        IReadOnlyList<StudyAvailabilityWindow> windows,
        TimeZoneInfo zone,
        List<string> warnings)
    {
        var result = new List<TimeRange>();
        foreach (var window in windows.Where(x => x.Day == day.DayOfWeek))
        {
            var start = ResolveWallClock(day, window.Start, zone, isStart: true, out var startAdjusted);
            var end = ResolveWallClock(day, window.End, zone, isStart: false, out var endAdjusted);
            if (startAdjusted || endAdjusted)
                warnings.Add($"Una fascia di disponibilità del {day:yyyy-MM-dd} è stata adattata al cambio di orario legale/solare.");
            if (end > start)
                result.Add(new TimeRange(start, end));
        }
        return result;
    }

    private static DateTimeOffset ResolveWallClock(
        DateOnly day,
        TimeOnly time,
        TimeZoneInfo zone,
        bool isStart,
        out bool adjusted)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        adjusted = false;
        var minutes = 0;
        while (zone.IsInvalidTime(local) && minutes < 180)
        {
            local = local.AddMinutes(1);
            minutes++;
            adjusted = true;
        }
        if (zone.IsInvalidTime(local))
            throw new ArgumentException("The availability window falls inside an invalid local-time interval.");

        TimeSpan offset;
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            offset = isStart ? offsets.Max() : offsets.Min();
            adjusted = true;
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }
        return new DateTimeOffset(local, offset);
    }

    private static List<TimeRange> MergeRanges(IEnumerable<TimeRange> source)
    {
        var sorted = source.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
        var merged = new List<TimeRange>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
            {
                merged.Add(range);
                continue;
            }
            var previous = merged[^1];
            merged[^1] = new TimeRange(previous.Start, range.End > previous.End ? range.End : previous.End);
        }
        return merged;
    }

    private static IEnumerable<TimeRange> SubtractCommitments(TimeRange window, IReadOnlyList<CalendarEvent> commitments)
    {
        var free = new List<TimeRange> { window };
        foreach (var eventItem in commitments)
        {
            if (eventItem.EndsAt <= window.Start || eventItem.StartsAt >= window.End)
                continue;

            var next = new List<TimeRange>();
            foreach (var range in free)
            {
                if (eventItem.EndsAt <= range.Start || eventItem.StartsAt >= range.End)
                {
                    next.Add(range);
                    continue;
                }
                if (eventItem.StartsAt > range.Start)
                    next.Add(new TimeRange(range.Start, eventItem.StartsAt));
                if (eventItem.EndsAt < range.End)
                    next.Add(new TimeRange(eventItem.EndsAt, range.End));
            }
            free = next;
        }
        return free.Where(x => x.End > x.Start);
    }

    private static int UnionMinutes(IEnumerable<TimeRange> ranges) =>
        MergeRanges(ranges).Sum(x => Math.Max(0, (int)Math.Floor((x.End - x.Start).TotalMinutes)));

    private static bool IsStudyCommitment(CalendarEvent item) =>
        !item.IsCancelled &&
        (string.Equals(item.Category, CalendarEventCategory.Study, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(item.Category, CalendarEventCategory.Review, StringComparison.OrdinalIgnoreCase));

    private static bool IsGeneratedPlannerBlock(CalendarEvent item) =>
        item is not null &&
        string.Equals(item.Source, CalendarEventSource.Planner, StringComparison.OrdinalIgnoreCase) &&
        item.ExternalId?.StartsWith(GeneratedExternalIdPrefix, StringComparison.Ordinal) == true;

    private static bool IsUnchangedGeneratedPlannerBlock(CalendarEvent item) =>
        item is not null && IsGeneratedPlannerBlock(item) && item.UpdatedAt <= item.CreatedAt;

    private static string BuildExternalId(StudyPlanBlock block) =>
        $"{GeneratedExternalIdPrefix}{block.TaskId}:{block.StartsAt.UtcDateTime.Ticks}";

    private static string BuildEventId(string externalId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(externalId));
        return $"planner-{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date, TimeZoneInfo zone) =>
        ResolveWallClock(date, TimeOnly.MinValue, zone, isStart: true, out _);

    private static DateOnly MondayOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End);
}
