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
    public string ParentTopicId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Status { get; set; }
    public int EstimatedMinutes { get; set; }
    public int Priority { get; set; } = 3;
    public int Difficulty { get; set; } = 3;
    public int? PreparationLevel { get; set; }
    public int Position { get; set; }
    public bool IsArchived { get; set; }
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
    public string PlanId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public int PlannerStatus { get; set; }
    public int PriorityScore { get; set; }
    public int ActualMinutes { get; set; }
    public int RescheduleCount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StudySessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string CalendarEventId { get; set; } = string.Empty;
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
    public bool NotesArePrivate { get; set; } = true;
    public List<StudySessionSegmentEntity> Segments { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StudySessionSegmentEntity
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int Type { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
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

public sealed class StudyActivityEntity
{
    public string Id { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string MissionId { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CalendarEventId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public int EffectiveSeconds { get; set; }
    public int Origin { get; set; }
    public int Status { get; set; }
    public string Result { get; set; } = string.Empty;
    public int? FocusRating { get; set; }
    public int? DifficultyRating { get; set; }
    public int? QualityRating { get; set; }
    public int? EnergyRating { get; set; }
    public string Note { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public bool IsPrivate { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class ActivityRevisionEntity
{
    public string Id { get; set; } = string.Empty;
    public string ActivityId { get; set; } = string.Empty;
    public int Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public DateTimeOffset RevisedAt { get; set; }
}

public sealed class StudyMissionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public int Period { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int Difficulty { get; set; }
    public int Status { get; set; }
    public string Reward { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<MissionStepEntity> Steps { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class MissionStepEntity
{
    public string Id { get; set; } = string.Empty;
    public string MissionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
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

public sealed class PomodoroSettingsEntity
{
    public int Id { get; set; } = 1;
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;
}

public sealed class PomodoroTimerEntity
{
    public string Id { get; set; } = "current";
    public string SessionId { get; set; } = string.Empty;
    public int Phase { get; set; }
    public int Status { get; set; }
    public DateTimeOffset PhaseWindowStartedAt { get; set; }
    public DateTimeOffset PhaseStartedAt { get; set; }
    public DateTimeOffset? PhaseEndsAt { get; set; }
    public int RemainingSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int FocusesCompletedInSet { get; set; }
    public int CycleNumber { get; set; }
    public string CurrentCycleId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PomodoroCycleEntity
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int CycleNumber { get; set; }
    public DateTimeOffset FocusStartedAt { get; set; }
    public DateTimeOffset FocusEndedAt { get; set; }
    public int FocusSeconds { get; set; }
    public int BreakType { get; set; }
    public DateTimeOffset? BreakStartedAt { get; set; }
    public DateTimeOffset? BreakEndedAt { get; set; }
    public int BreakSeconds { get; set; }
    public bool WasSkipped { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class LegacyImportEntity
{
    public string Key { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}

public sealed class PlannerSettingsEntity
{
    public int Id { get; set; } = 1;
    public int PreferredSessionMinutes { get; set; } = 50;
    public int MinimumSessionMinutes { get; set; } = 25;
    public int BreakBetweenSessionsMinutes { get; set; } = 10;
    public int MaxSessionsPerDay { get; set; } = 3;
    public int ReviewReservePercent { get; set; } = 15;
    public int ReviewReserveDays { get; set; } = 2;
    public bool IncludeWeekends { get; set; } = true;
    public bool AutoRescheduleMissed { get; set; } = true;
    public string AvailabilityJson { get; set; } = "[]";
}

public sealed class StudyPlanEntity
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public int RequiredMinutes { get; set; }
    public int ReviewReserveMinutes { get; set; }
    public int ScheduledMinutes { get; set; }
    public int Status { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string WarningsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PlannerRescheduleEntity
{
    public string Id { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string CalendarEventId { get; set; } = string.Empty;
    public DateTimeOffset PreviousStartsAt { get; set; }
    public DateTimeOffset PreviousEndsAt { get; set; }
    public DateTimeOffset NewStartsAt { get; set; }
    public DateTimeOffset NewEndsAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset MovedAt { get; set; }
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
}
