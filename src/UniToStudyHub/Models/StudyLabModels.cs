using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

public enum StudySessionState
{
    Running,
    Paused,
    Completed,
    Cancelled
}

public enum StudyTaskStatus
{
    Todo,
    InProgress,
    Completed,
    Cancelled
}

public enum ErrorReviewStatus
{
    Open,
    Reviewing,
    Resolved
}

public enum RecallRating
{
    Again,
    Hard,
    Good,
    Easy
}

/// <summary>
/// A focus session is persisted as timestamps and accumulated active seconds,
/// never as a countdown owned by a UI timer. This allows the remaining time to
/// be reconstructed after an app or machine restart.
/// </summary>
public sealed class StudySessionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public int PlannedMinutes { get; set; } = 25;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastResumedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int AccumulatedFocusSeconds { get; set; }
    public int AccumulatedPauseSeconds { get; set; }
    public StudySessionState State { get; set; } = StudySessionState.Running;
    public string Outcome { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class StudyTaskData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? DueAt { get; set; }
    public int EstimatedMinutes { get; set; }
    public int Priority { get; set; } = 3;
    public StudyTaskStatus Status { get; set; } = StudyTaskStatus.Todo;
    public DateTimeOffset? CompletedAt { get; set; }
    public string Source { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ErrorJournalEntryData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string IncorrectAnswer { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    public string ErrorType { get; set; } = "Concetto";
    public string RootCause { get; set; } = string.Empty;
    public DateTimeOffset? NextReviewAt { get; set; }
    public int ReviewCount { get; set; }
    public ErrorReviewStatus Status { get; set; } = ErrorReviewStatus.Open;
    public string Source { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class FlashcardData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public decimal EaseFactor { get; set; } = 2.5m;
    public int IntervalDays { get; set; }
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public bool IsSuspended { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class FlashcardReviewData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FlashcardId { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.Now;
    public RecallRating Rating { get; set; }
    public int PreviousIntervalDays { get; set; }
    public int NewIntervalDays { get; set; }
    public decimal EaseFactorAfter { get; set; }
}

public sealed class DistractionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SessionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class EnergyCheckInData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;
    public int EnergyLevel { get; set; } = 3;
    public int FocusLevel { get; set; } = 3;
    public string Note { get; set; } = string.Empty;
}

public sealed class PdfPageData
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Text extracted from a user-supplied PDF. The document is data, never an
/// instruction source. Pages are kept individually so a citation can be
/// verified against a real page before it is shown.
/// </summary>
public sealed class PdfDocumentData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
    public string CourseId { get; set; } = string.Empty;
    public ObservableCollection<PdfPageData> Pages { get; set; } = new();
    public bool HasExtractableText => Pages.Any(x => !string.IsNullOrWhiteSpace(x.Text));
}

public sealed class PdfImportResult
{
    public PdfDocumentData Document { get; init; } = new();
    public bool WasDuplicate { get; init; }
    public bool RequiresOcr { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
