using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

/// <summary>
/// A calendar item stored locally. Source and verification state remain explicit
/// so imported/indicative dates are never presented as official by accident.
/// </summary>
public sealed class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = CalendarEventCategory.Personal;
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.Now.AddHours(1);
    public bool IsAllDay { get; set; }
    public string? CourseId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Source { get; set; } = CalendarEventSource.Manual;
    public string Verification { get; set; } = CalendarEventVerification.Unverified;
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public static class CalendarEventCategory
{
    public const string Lecture = "Lecture";
    public const string Exam = "Exam";
    public const string Deadline = "Deadline";
    public const string Study = "Study";
    public const string Personal = "Personal";
}

public static class CalendarEventSource
{
    public const string Manual = "Manual";
    public const string Moodle = "Moodle";
    public const string Ics = "ICS";
    public const string Planner = "Planner";
}

public static class CalendarEventVerification
{
    public const string Official = "Official";
    public const string Indicative = "Indicative";
    public const string Unverified = "Unverified";
}
