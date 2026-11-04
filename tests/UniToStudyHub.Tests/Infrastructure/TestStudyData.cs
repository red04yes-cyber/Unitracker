using System.Collections.ObjectModel;
using UniToStudyHub.Models;

namespace UniToStudyHub.Tests.Infrastructure;

internal static class TestStudyData
{
    public static StudyData CreateValid() => new()
    {
        GraduationTarget = new DateTime(2027, 10, 1),
        MoodleBaseUrl = "https://moodle.example.edu",
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = "Analisi", Code = "MAT1", Cfu = 6, Completed = true },
            new() { Id = "course-2", Name = "Fisica", Code = "FIS1", Cfu = 9 }
        },
        Grades = new ObservableCollection<Grade>
        {
            new()
            {
                Id = "grade-1", CourseId = "course-1", CourseName = "Analisi",
                Value = 28, Cfu = 6, Date = new DateTime(2026, 1, 10)
            }
        },
        Exams = new ObservableCollection<Exam>
        {
            new()
            {
                Id = "planned-1", CourseId = "course-2", CourseName = "Fisica",
                Name = "Fisica", Cfu = 9, EstimatedStudyHours = 90,
                PlannedDate = new DateTime(2026, 6, 20), PlannedSession = "Estiva 2026"
            }
        },
        Announcements = new ObservableCollection<Announcement>
        {
            new()
            {
                Id = "announcement-1", SourceKey = "moodle:1", CourseName = "Analisi",
                Title = "Materiale nuovo", Summary = "Dispense",
                Url = "https://moodle.example.edu/mod/resource/view.php?id=1",
                UpdatedAt = new DateTime(2026, 1, 10), IsNew = true
            }
        },
        CareerExams = new ObservableCollection<CareerExamData>
        {
            new()
            {
                Id = "career-1", CourseId = "course-1", Name = "Analisi", Cfu = 6,
                CountsTowardAverage = true,
                Attempts = new ObservableCollection<ExamAttemptData>
                {
                    new()
                    {
                        Id = "attempt-1",
                        AttemptedAt = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero),
                        Status = "Completed",
                        Outcome = "Passed",
                        GradeDecision = "Accepted",
                        NumericGrade = 28,
                        IsHonors = false,
                        IsPassFail = false,
                        RecordedAt = new DateTimeOffset(2026, 1, 11, 10, 0, 0, TimeSpan.Zero)
                    }
                }
            }
        }
    };
}
