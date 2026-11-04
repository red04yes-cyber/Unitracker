using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

// Backward-compatible DTO for %LOCALAPPDATA%/UniToStudyHub/data.json.
public sealed class StudyData
{
    public ObservableCollection<Course> Courses { get; set; } = new();
    public ObservableCollection<Grade> Grades { get; set; } = new();
    public ObservableCollection<Exam> Exams { get; set; } = new();
    public ObservableCollection<Announcement> Announcements { get; set; } = new();
    public DateTime GraduationTarget { get; set; } = new(2027, 10, 1);
    public string MoodleBaseUrl { get; set; } = string.Empty;
    public ObservableCollection<CareerExamData> CareerExams { get; set; } = new();
}

public sealed class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int? MoodleCourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Cfu { get; set; }
    public bool Completed { get; set; }
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

// Moodle response DTOs retained for the existing integration.
public sealed class MoodleSiteInfo { public int userid { get; set; } public string fullname { get; set; } = string.Empty; public string username { get; set; } = string.Empty; public string sitename { get; set; } = string.Empty; public string siteurl { get; set; } = string.Empty; }
public sealed class MoodleCourse { public int id { get; set; } public string fullname { get; set; } = string.Empty; public string shortname { get; set; } = string.Empty; public string idnumber { get; set; } = string.Empty; public string summary { get; set; } = string.Empty; public int visible { get; set; } }
public sealed class MoodleCourseSection { public int id { get; set; } public string name { get; set; } = string.Empty; public List<MoodleModule> modules { get; set; } = new(); }
public sealed class MoodleModule { public int id { get; set; } public int instance { get; set; } public string name { get; set; } = string.Empty; public string modname { get; set; } = string.Empty; public List<MoodleContentFile> contents { get; set; } = new(); }
public sealed class MoodleContentFile { public string type { get; set; } = string.Empty; public string filename { get; set; } = string.Empty; public string fileurl { get; set; } = string.Empty; public string filepath { get; set; } = string.Empty; public long filesize { get; set; } public string mimetype { get; set; } = string.Empty; }
public sealed class MoodleForumDiscussion { public int id { get; set; } public int forumid { get; set; } public string name { get; set; } = string.Empty; public int timemodified { get; set; } public int created { get; set; } public MoodleForumPost? firstpost { get; set; } }
public sealed class MoodleForumPost { public int id { get; set; } public string subject { get; set; } = string.Empty; public string message { get; set; } = string.Empty; }
public sealed class MoodleCalendarResult { public List<MoodleCalendarEvent> events { get; set; } = new(); }
public sealed class MoodleCalendarEvent { public int id { get; set; } public string name { get; set; } = string.Empty; public string description { get; set; } = string.Empty; public int courseid { get; set; } public string eventtype { get; set; } = string.Empty; public int timestart { get; set; } public int timeduration { get; set; } public int visible { get; set; } public int timemodified { get; set; } }
public sealed class MoodleForumDiscussionsResult { public List<MoodleForumDiscussion> discussions { get; set; } = new(); }
