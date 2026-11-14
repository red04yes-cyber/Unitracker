using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;

namespace UniToStudyHub.Models;

// Backward-compatible DTO for %LOCALAPPDATA%/UniToStudyHub/data.json.
public sealed class StudyData
{
    public ObservableCollection<Course> Courses { get; set; } = new();
    public ObservableCollection<Grade> Grades { get; set; } = new();
    public ObservableCollection<Exam> Exams { get; set; } = new();
    public ObservableCollection<Announcement> Announcements { get; set; } = new();
    public ObservableCollection<CareerExamData> CareerExams { get; set; } = new();
    public ObservableCollection<CoursePrerequisiteData> CoursePrerequisites { get; set; } = new();
    public ObservableCollection<SyllabusTopicData> SyllabusTopics { get; set; } = new();
    public ObservableCollection<QuestionBankItemData> Questions { get; set; } = new();
    public ObservableCollection<StudySessionData> StudySessions { get; set; } = new();
    public ObservableCollection<StudyTaskData> StudyTasks { get; set; } = new();
    public ObservableCollection<StudyActivityData> StudyActivities { get; set; } = new();
    public ObservableCollection<StudyMissionData> StudyMissions { get; set; } = new();
    public PomodoroSettingsData PomodoroSettings { get; set; } = new();
    public PomodoroTimerData? PomodoroTimer { get; set; }
    public ObservableCollection<PomodoroCycleData> PomodoroCycles { get; set; } = new();
    public ObservableCollection<ActivityRevisionData> ActivityRevisions { get; set; } = new();
    public ObservableCollection<ErrorJournalEntryData> ErrorJournalEntries { get; set; } = new();
    public ObservableCollection<FlashcardData> Flashcards { get; set; } = new();
    public ObservableCollection<FlashcardReviewData> FlashcardReviews { get; set; } = new();
    public ObservableCollection<DistractionData> Distractions { get; set; } = new();
    public ObservableCollection<EnergyCheckInData> EnergyCheckIns { get; set; } = new();
    public ObservableCollection<PdfDocumentData> PdfDocuments { get; set; } = new();
    public ObservableCollection<NotificationDismissalData> NotificationDismissals { get; set; } = new();

    public DateTime GraduationTarget { get; set; } = new(2027, 10, 1);
    public string MoodleBaseUrl { get; set; } = string.Empty;
    public ObservableCollection<CalendarEvent> CalendarEvents { get; set; } = new();

    // University profile. Empty values mean "not configured" and must not be
    // rendered as if they were official university data.
    public string DegreeProgramName { get; set; } = string.Empty;
    public string DegreeProgramCode { get; set; } = string.Empty;
    public int? EnrollmentYear { get; set; }
    public int? CurrentAcademicYear { get; set; }
    public int DegreeTotalCfu { get; set; }
    public string StudyPreferences { get; set; } = string.Empty;
    public int WeeklyStudyHours { get; set; } = 20;
    public int MaxDailyStudyMinutes { get; set; } = 240;
}

public sealed class NotificationDismissalData
{
    // SHA-256 of the notification source identity and occurrence instant.
    // Notification text and personal content are deliberately not persisted.
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset DismissedAt { get; set; }
}

public sealed class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int? MoodleCourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Cfu { get; set; }

    /// <summary>Legacy presence flag. It means the exam was concluded successfully.</summary>
    public bool Completed { get; set; }

    public CourseStatus Status { get; set; } = CourseStatus.NotStarted;
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

public sealed class Grade
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int Value { get; set; }
    public bool Honors { get; set; }
    public int Cfu { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public bool CountsTowardAverage { get; set; } = true;
}

public sealed class Exam
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public int EstimatedStudyHours { get; set; }
    public DateTime? PlannedDate { get; set; }
    public string PlannedSession { get; set; } = "Da pianificare";
    public bool Completed { get; set; }
}

public sealed class Announcement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceKey { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsNew { get; set; } = true;
}

public sealed class CareerExamData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public bool CountsTowardAverage { get; set; } = true;
    public ObservableCollection<ExamAttemptData> Attempts { get; set; } = new();
}

public sealed class ExamAttemptData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset AttemptedAt { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Scheduled";
    public string Outcome { get; set; } = "Pending";
    public string GradeDecision { get; set; } = "Pending";
    public int? NumericGrade { get; set; }
    public bool IsHonors { get; set; }
    public bool IsPassFail { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}

public sealed class CoursePrerequisiteData
{
    public string CourseId { get; set; } = string.Empty;
    public string PrerequisiteCourseId { get; set; } = string.Empty;
    public bool IsMandatory { get; set; } = true;
}

public sealed class SyllabusTopicData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SyllabusTopicStatus Status { get; set; }
    public int EstimatedMinutes { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime? LastStudiedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class QuestionBankItemData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public QuestionType Type { get; set; } = QuestionType.Open;
    public string Difficulty { get; set; } = "Media";
    public ObservableCollection<string> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Source { get; set; } = "Manuale";
    public bool IsOfficial { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Moodle response DTOs retained for the existing integration.
public sealed class MoodleSiteInfo { public int userid { get; set; } public string fullname { get; set; } = string.Empty; public string username { get; set; } = string.Empty; public string sitename { get; set; } = string.Empty; public string siteurl { get; set; } = string.Empty; public List<MoodleWebServiceFunction> functions { get; set; } = new(); }
public sealed class MoodleWebServiceFunction { public string name { get; set; } = string.Empty; public string version { get; set; } = string.Empty; }
public sealed class MoodleCourse { public int id { get; set; } public string fullname { get; set; } = string.Empty; public string shortname { get; set; } = string.Empty; public string idnumber { get; set; } = string.Empty; public string summary { get; set; } = string.Empty; public int visible { get; set; } }
public sealed class MoodleCourseSection { public int id { get; set; } public int section { get; set; } public string name { get; set; } = string.Empty; public string summary { get; set; } = string.Empty; public int visible { get; set; } public bool uservisible { get; set; } = true; public List<MoodleModule> modules { get; set; } = new(); }
public sealed class MoodleModule { public int id { get; set; } public int instance { get; set; } public string name { get; set; } = string.Empty; public string modname { get; set; } = string.Empty; public string modplural { get; set; } = string.Empty; public string description { get; set; } = string.Empty; public string url { get; set; } = string.Empty; public int visible { get; set; } public bool uservisible { get; set; } = true; public int completion { get; set; } public string availability { get; set; } = string.Empty; public List<MoodleContentFile> contents { get; set; } = new(); }
public sealed class MoodleContentFile { public string type { get; set; } = string.Empty; public string filename { get; set; } = string.Empty; public string fileurl { get; set; } = string.Empty; public string filepath { get; set; } = string.Empty; public long filesize { get; set; } public string mimetype { get; set; } = string.Empty; public int timemodified { get; set; } public bool isexternalfile { get; set; } }
public sealed class MoodleActivityCompletionResult { public List<MoodleActivityCompletionStatus> statuses { get; set; } = new(); }
public sealed class MoodleActivityCompletionStatus { public int cmid { get; set; } public string modname { get; set; } = string.Empty; public int instance { get; set; } public int state { get; set; } public int timecompleted { get; set; } public int tracking { get; set; } public int valueused { get; set; } }
public sealed record MoodleCourseActivitySnapshot(IReadOnlyDictionary<string, System.Text.Json.JsonElement> ActivityDetails, IReadOnlyList<MoodleActivityCompletionStatus> CompletionStatuses, IReadOnlyList<MoodleContentFile> Files);
public sealed class MoodleForumDiscussion { public int id { get; set; } public int forumid { get; set; } public string name { get; set; } = string.Empty; public int timemodified { get; set; } public int created { get; set; } public MoodleForumPost? firstpost { get; set; } }
public sealed class MoodleForumPost { public int id { get; set; } public string subject { get; set; } = string.Empty; public string message { get; set; } = string.Empty; }
public sealed class MoodleCalendarResult { public List<MoodleCalendarEvent> events { get; set; } = new(); }
public sealed class MoodleCalendarEvent { public int id { get; set; } public string name { get; set; } = string.Empty; public string description { get; set; } = string.Empty; public int courseid { get; set; } public string eventtype { get; set; } = string.Empty; public int timestart { get; set; } public int timeduration { get; set; } public int visible { get; set; } public int timemodified { get; set; } }
public sealed class MoodleForumDiscussionsResult { public List<MoodleForumDiscussion> discussions { get; set; } = new(); }
