using System.Text.Json.Serialization;

namespace UniToStudyHub.Models;

/// <summary>Persisted study-productivity settings and records. All dates are local calendar dates or offset-aware instants.</summary>
public sealed class StudyProductivityData
{
    public int DailyStudyGoalMinutes { get; set; } = 60;
    public int WeeklyStudyGoalMinutes { get; set; } = 600;
    public bool DesktopWidgetEnabled { get; set; }
    public List<StudyReminderData> Reminders { get; set; } = new();
    public List<StudyDayLogData> StudyDays { get; set; } = new();
    public List<QuickStudyNoteData> QuickNotes { get; set; } = new();
    public List<LectureAttendanceData> Attendance { get; set; } = new();
    public List<DashboardWidgetPreferenceData> DashboardWidgets { get; set; } = new();
    public ExamModeData? ActiveExamMode { get; set; }
}

public sealed class StudyReminderData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string? CourseId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string Kind { get; set; } = "Deadline";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastNotifiedAt { get; set; }
}

public sealed class StudyDayLogData
{
    public DateOnly Date { get; set; }
    public int FocusMinutes { get; set; }
}

public sealed class QuickStudyNoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class LectureAttendanceData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public bool Attended { get; set; }
    public string? Notes { get; set; }
}

public sealed class DashboardWidgetPreferenceData : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
    private string _displayValue = string.Empty;
    [JsonIgnore]
    public string DisplayValue
    {
        get => _displayValue;
        set
        {
            if (string.Equals(_displayValue, value, StringComparison.Ordinal)) return;
            _displayValue = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayValue)));
        }
    }
    [JsonIgnore]
    public string DisplayName => Key switch
    {
        "next-activity" => "Prossima attività",
        "weekly-goal" => "Obiettivo settimanale",
        "streak" => "Streak di studio",
        "upcoming-exams" => "Esami in arrivo",
        "recent-notes" => "Note recenti",
        _ => Key
    };
}

public sealed class ExamModeData
{
    public string ExamId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

