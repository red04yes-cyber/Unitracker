namespace UniToStudyHub.Infrastructure.Data.Entities;

public sealed class ProfileSettingsEntity
{
    public int Id { get; set; } = 1;
    public DateTime GraduationTarget { get; set; } = new(2027, 10, 1);
    public string MoodleBaseUrl { get; set; } = string.Empty;
    public string ProductivityStateJson { get; set; } = "{}";
}
public sealed class CourseEntity
{
    public string Id { get; set; } = string.Empty;
    public int? MoodleCourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public bool Completed { get; set; }
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
public sealed class LegacyImportEntity
{
    public string Key { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
}
