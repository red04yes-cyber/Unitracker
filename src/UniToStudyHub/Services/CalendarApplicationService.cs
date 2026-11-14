using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Calendar use cases over the shared study snapshot. Saving is an explicit
/// caller responsibility; no operation silently moves user-created events.
/// </summary>
public sealed class CalendarApplicationService
{
    public CalendarEvent Add(StudyData data, CalendarEvent item)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
        if (data.CalendarEvents.Any(x => string.Equals(x.Id, item.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Calendar event '{item.Id}' already exists.", nameof(item));
        CalendarService.Validate(item);
        item.Title = item.Title.Trim();
        item.CourseName = item.CourseName?.Trim() ?? string.Empty;
        item.Location = item.Location?.Trim() ?? string.Empty;
        item.Notes = item.Notes?.Trim() ?? string.Empty;
        item.Source = string.IsNullOrWhiteSpace(item.Source) ? CalendarEventSource.Manual : item.Source.Trim();
        item.Verification = string.IsNullOrWhiteSpace(item.Verification)
            ? CalendarEventVerification.Unverified
            : item.Verification.Trim();
        item.TimeZoneId = string.IsNullOrWhiteSpace(item.TimeZoneId) ? TimeZoneInfo.Local.Id : item.TimeZoneId;
        item.CreatedAt = item.CreatedAt == default ? DateTimeOffset.Now : item.CreatedAt;
        item.UpdatedAt = DateTimeOffset.Now;
        data.CalendarEvents.Add(item);
        return item;
    }

    public void Update(StudyData data, CalendarEvent updated)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(updated);
        var existing = data.CalendarEvents.FirstOrDefault(x => string.Equals(x.Id, updated.Id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Calendar event '{updated.Id}' does not exist.", nameof(updated));
        CalendarService.Validate(updated);
        existing.Title = updated.Title.Trim();
        existing.Category = updated.Category;
        existing.StartsAt = updated.StartsAt;
        existing.EndsAt = updated.EndsAt;
        existing.IsAllDay = updated.IsAllDay;
        existing.CourseId = updated.CourseId;
        existing.CourseName = updated.CourseName?.Trim() ?? string.Empty;
        existing.Location = updated.Location?.Trim() ?? string.Empty;
        existing.Notes = updated.Notes?.Trim() ?? string.Empty;
        existing.Source = updated.Source;
        existing.Verification = updated.Verification;
        existing.TimeZoneId = string.IsNullOrWhiteSpace(updated.TimeZoneId) ? TimeZoneInfo.Local.Id : updated.TimeZoneId;
        existing.ExternalId = updated.ExternalId;
        existing.ExternalSequence = updated.ExternalSequence;
        existing.IsCancelled = updated.IsCancelled;
        existing.RecurrenceRule = updated.RecurrenceRule ?? string.Empty;
        existing.UpdatedAt = DateTimeOffset.Now;
    }

    public void Delete(StudyData data, string eventId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var item = data.CalendarEvents.FirstOrDefault(x => string.Equals(x.Id, eventId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Calendar event '{eventId}' does not exist.", nameof(eventId));
        data.CalendarEvents.Remove(item);
    }

    public IReadOnlyList<CalendarEvent> GetRange(
        StudyData data,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        string? category = null,
        string? source = null) =>
        CalendarService.GetEventsInRange(data.CalendarEvents, start, endExclusive, category, source);

    /// <summary>
    /// Creates or updates a clearly labelled planning event for an uncompleted
    /// exam. Its stable external id makes repeated calls idempotent.
    /// </summary>
    public CalendarEvent? EnsureExamEvent(StudyData data, Exam exam, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(exam);
        if (string.IsNullOrWhiteSpace(exam.Id) || exam.Completed || exam.PlannedDate is null)
            return null;

        var at = now ?? DateTimeOffset.Now;
        var externalId = $"exam:{exam.Id}";
        var date = exam.PlannedDate.Value.Date;
        var localStart = date.AddHours(9);
        var start = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
        var courseName = exam.CourseName?.Trim() ?? string.Empty;
        var title = !string.IsNullOrWhiteSpace(exam.Name)
            ? exam.Name.Trim()
            : !string.IsNullOrWhiteSpace(courseName)
                ? $"Esame: {courseName}"
                : "Esame pianificato";
        var courseId = string.IsNullOrWhiteSpace(exam.CourseId) ? null : exam.CourseId.Trim();
        var session = string.IsNullOrWhiteSpace(exam.PlannedSession)
            ? "sessione non specificata"
            : exam.PlannedSession.Trim();
        var notes = $"Data proposta dal pianificatore ({session}): non è un appello ufficiale.";
        var timeZoneId = TimeZoneInfo.Local.Id;

        var existing = data.CalendarEvents.FirstOrDefault(x =>
            string.Equals(x.ExternalId, externalId, StringComparison.Ordinal) &&
            string.Equals(x.Source, CalendarEventSource.Planner, StringComparison.Ordinal));
        if (existing is not null)
        {
            var changed = false;
            if (!string.Equals(existing.Title, title, StringComparison.Ordinal))
            {
                existing.Title = title;
                changed = true;
            }
            if (!string.Equals(existing.Category, CalendarEventCategory.Exam, StringComparison.Ordinal))
            {
                existing.Category = CalendarEventCategory.Exam;
                changed = true;
            }
            if (existing.StartsAt != start)
            {
                existing.StartsAt = start;
                changed = true;
            }
            if (existing.EndsAt != start.AddHours(2))
            {
                existing.EndsAt = start.AddHours(2);
                changed = true;
            }
            if (existing.IsAllDay)
            {
                existing.IsAllDay = false;
                changed = true;
            }
            if (!string.Equals(existing.CourseId, courseId, StringComparison.Ordinal))
            {
                existing.CourseId = courseId;
                changed = true;
            }
            if (!string.Equals(existing.CourseName, courseName, StringComparison.Ordinal))
            {
                existing.CourseName = courseName;
                changed = true;
            }
            if (!string.Equals(existing.Source, CalendarEventSource.Planner, StringComparison.Ordinal))
            {
                existing.Source = CalendarEventSource.Planner;
                changed = true;
            }
            if (!string.Equals(existing.Verification, CalendarEventVerification.Indicative, StringComparison.Ordinal))
            {
                existing.Verification = CalendarEventVerification.Indicative;
                changed = true;
            }
            if (!string.Equals(existing.TimeZoneId, timeZoneId, StringComparison.Ordinal))
            {
                existing.TimeZoneId = timeZoneId;
                changed = true;
            }
            if (!string.Equals(existing.Notes, notes, StringComparison.Ordinal))
            {
                existing.Notes = notes;
                changed = true;
            }
            if (existing.IsCancelled)
            {
                existing.IsCancelled = false;
                changed = true;
            }
            if (changed) existing.UpdatedAt = at;
            CalendarService.Validate(existing);
            return existing;
        }

        var item = new CalendarEvent
        {
            Id = DeterministicId("exam-event", exam.Id),
            Title = title,
            Category = CalendarEventCategory.Exam,
            StartsAt = start,
            EndsAt = start.AddHours(2),
            CourseId = courseId,
            CourseName = courseName,
            Source = CalendarEventSource.Planner,
            Verification = CalendarEventVerification.Indicative,
            ExternalId = externalId,
            TimeZoneId = timeZoneId,
            Notes = notes,
            CreatedAt = at,
            UpdatedAt = at
        };
        CalendarService.Validate(item);
        data.CalendarEvents.Add(item);
        return item;
    }

    /// <summary>
    /// Reconciles planner-owned exam events with the current planned exams.
    /// Completed, unscheduled or removed exams are soft-cancelled; Moodle, ICS
    /// and manual events are never changed by this operation.
    /// </summary>
    public IReadOnlyList<CalendarEvent> LinkPlannedExams(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var linked = new List<CalendarEvent>();
        var activeExamIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var exam in data.Exams)
        {
            if (exam.Completed || exam.PlannedDate is null || string.IsNullOrWhiteSpace(exam.Id))
                continue;

            activeExamIds.Add(exam.Id);
            var item = EnsureExamEvent(data, exam, at);
            if (item is not null) linked.Add(item);
        }

        foreach (var item in data.CalendarEvents.Where(x =>
                     string.Equals(x.Source, CalendarEventSource.Planner, StringComparison.Ordinal) &&
                     x.ExternalId?.StartsWith("exam:", StringComparison.Ordinal) == true))
        {
            var examId = item.ExternalId!["exam:".Length..];
            if (activeExamIds.Contains(examId) || item.IsCancelled) continue;
            item.IsCancelled = true;
            item.UpdatedAt = at;
        }

        return linked;
    }

    private static string DeterministicId(string prefix, string value) => $"{prefix}:{value}";
}
