namespace UniToStudyHub.Infrastructure.Data.Entities;

public sealed class ProfileSettingsEntity
{
    public int Id { get; set; } = 1;
    public DateTime GraduationTarget { get; set; } = new(2027, 10, 1);
    public string MoodleBaseUrl { get; set; } = string.Empty;
    public string DegreeProgramName { get; set; } = string.Empty;
    public string DegreeProgramCode { get; set; } = string.Empty;
    public int? EnrollmentYear { get; set; }
    public int? CurrentAcademicYear { get; set; }
    public int DegreeTotalCfu { get; set; }
    public string StudyPreferences { get; set; } = string.Empty;
    public int WeeklyStudyHours { get; set; } = 20;
    public int MaxDailyStudyMinutes { get; set; } = 240;
}

public sealed class CourseEntity
{
    public string Id { get; set; } = string.Empty;
    public int? MoodleCourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public bool Completed { get; set; }
    public int Status { get; set; }
    public bool Archived { get; set; }
    public string Teacher { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public string Session { get; set; } = string.Empty;
    public int EstimatedStudyHours { get; set; }
    public DateTime? AppealDate { get; set; }
    public string SyllabusSummary { get; set; } = string.Empty;
    public string SyllabusUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public DateTime? UpdatedAt { get; set; }
}

public sealed class GradeEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int Value { get; set; }
    public bool Honors { get; set; }
    public int Cfu { get; set; }
    public DateTime Date { get; set; }
    public bool CountsTowardAverage { get; set; }
}

public sealed class PlannedExamEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public int EstimatedStudyHours { get; set; }
    public DateTime? PlannedDate { get; set; }
    public string PlannedSession { get; set; } = string.Empty;
    public bool Completed { get; set; }
}

public sealed class AnnouncementEntity
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsNew { get; set; }
}

public sealed class CareerExamEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public bool CountsTowardAverage { get; set; }
    public List<ExamAttemptEntity> Attempts { get; set; } = new();
}

public sealed class ExamAttemptEntity
{
    public string Id { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;
    public DateTimeOffset AttemptedAt { get; set; }
    public int Status { get; set; }
    public int Outcome { get; set; }
    public int GradeDecision { get; set; }
    public int? NumericGrade { get; set; }
    public bool IsHonors { get; set; }
    public bool IsPassFail { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public sealed class CoursePrerequisiteEntity
{
    public string CourseId { get; set; } = string.Empty;
    public string PrerequisiteCourseId { get; set; } = string.Empty;
    public bool IsMandatory { get; set; } = true;
}

public sealed class SyllabusTopicEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; }
    public int EstimatedMinutes { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime? LastStudiedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class QuestionBankItemEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "[]";
    public string CorrectAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CalendarEventEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public string? CourseId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Verification { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public long ExternalSequence { get; set; }
    public bool IsCancelled { get; set; }
    public string RecurrenceRule { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StudySessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public int PlannedMinutes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastResumedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int AccumulatedFocusSeconds { get; set; }
    public int AccumulatedPauseSeconds { get; set; }
    public int State { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StudyTaskEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? DueAt { get; set; }
    public int EstimatedMinutes { get; set; }
    public int Priority { get; set; }
    public int Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ErrorJournalEntryEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string IncorrectAnswer { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public DateTimeOffset? NextReviewAt { get; set; }
    public int ReviewCount { get; set; }
    public int Status { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FlashcardEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public decimal EaseFactor { get; set; }
    public int IntervalDays { get; set; }
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public bool IsSuspended { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FlashcardReviewEntity
{
    public string Id { get; set; } = string.Empty;
    public string FlashcardId { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; }
    public int Rating { get; set; }
    public int PreviousIntervalDays { get; set; }
    public int NewIntervalDays { get; set; }
    public decimal EaseFactorAfter { get; set; }
}

public sealed class DistractionEntity
{
    public string Id { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class EnergyCheckInEntity
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
    public int EnergyLevel { get; set; }
    public int FocusLevel { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class PdfDocumentEntity
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string CourseId { get; set; } = string.Empty;
    public string PagesJson { get; set; } = "[]";
}

public sealed class LegacyImportEntity
{
    public string Key { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}
