using System.Collections.ObjectModel;
using System.IO;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using Models = UniToStudyHub.Models;

namespace UniToStudyHub.Infrastructure.Data;

public static class StudyDataEntityMapper
{
    /// <summary>
    /// Validates legacy data without repairing identifiers. Duplicate or
    /// inconsistent identifiers are reported to the caller: rewriting them
    /// would break the relationships between courses, grades and exams.
    /// </summary>
    public static void NormalizeAndValidate(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Courses ??= new ObservableCollection<Course>();
        data.Grades ??= new ObservableCollection<Grade>();
        data.Exams ??= new ObservableCollection<Models.Exam>();
        data.Announcements ??= new ObservableCollection<Announcement>();
        data.CareerExams ??= new ObservableCollection<CareerExamData>();

        EnsureNoNullItems(data.Courses, "course");
        EnsureNoNullItems(data.Grades, "grade");
        EnsureNoNullItems(data.Exams, "planned exam");
        EnsureNoNullItems(data.Announcements, "announcement");
        EnsureNoNullItems(data.CareerExams, "career exam");

        EnsureUniqueIds(data.Courses, x => x.Id, "course");
        EnsureUniqueIds(data.Grades, x => x.Id, "grade");
        EnsureUniqueIds(data.Exams, x => x.Id, "planned exam");
        EnsureUniqueIds(data.Announcements, x => x.Id, "announcement");
        EnsureUniqueIds(data.CareerExams, x => x.Id, "career exam");

        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var course in data.Courses)
        {
            if (string.IsNullOrWhiteSpace(course.Name))
                throw new InvalidDataException("A course has no name.");
            if (course.Cfu < 0)
                throw new InvalidDataException($"Course '{course.Name}' has negative CFU.");
            course.Code ??= string.Empty;
        }

        foreach (var grade in data.Grades)
        {
            if (string.IsNullOrWhiteSpace(grade.CourseId) || !courseIds.Contains(grade.CourseId))
                throw new InvalidDataException($"Grade '{grade.Id}' refers to course '{grade.CourseId}', which does not exist.");
            grade.CourseName ??= string.Empty;
            if (grade.Value is < 18 or > 30)
                throw new InvalidDataException($"Grade '{grade.Id}' is outside the 18–30 range.");
            if (grade.Cfu <= 0)
                throw new InvalidDataException($"Grade '{grade.Id}' must have positive CFU.");
            if (grade.Honors && grade.Value != 30)
                throw new InvalidDataException($"Grade '{grade.Id}' marks honors for a value other than 30.");
        }

        foreach (var exam in data.Exams)
        {
            if (string.IsNullOrWhiteSpace(exam.Name))
                throw new InvalidDataException("A planned exam has no name.");
            if (string.IsNullOrWhiteSpace(exam.CourseId) || !courseIds.Contains(exam.CourseId))
                throw new InvalidDataException($"Planned exam '{exam.Name}' refers to a course that does not exist.");
            exam.CourseName ??= string.Empty;
            exam.PlannedSession ??= string.Empty;
            if (exam.Cfu < 0 || exam.EstimatedStudyHours < 0)
                throw new InvalidDataException($"Planned exam '{exam.Name}' has a negative value.");
        }

        foreach (var announcement in data.Announcements)
        {
            announcement.SourceKey ??= string.Empty;
            announcement.CourseName ??= string.Empty;
            announcement.Title ??= string.Empty;
            announcement.Summary ??= string.Empty;
            announcement.Url ??= string.Empty;
        }

        if (string.IsNullOrWhiteSpace(data.MoodleBaseUrl)) data.MoodleBaseUrl = string.Empty;
        if (data.GraduationTarget == default)
            throw new InvalidDataException("GraduationTarget is missing or invalid.");

        var attemptIds = new HashSet<string>(StringComparer.Ordinal);
        var teachingUnitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exam in data.CareerExams)
        {
            if (string.IsNullOrWhiteSpace(exam.CourseId) || !courseIds.Contains(exam.CourseId))
                throw new InvalidDataException($"Career exam '{exam.Id}' refers to course '{exam.CourseId}', which does not exist.");
            if (!teachingUnitIds.Add(exam.CourseId))
                throw new InvalidDataException(
                    $"Course '{exam.CourseId}' has more than one career exam; its CFU would be counted twice.");
            if (string.IsNullOrWhiteSpace(exam.Name) || exam.Cfu <= 0)
                throw new InvalidDataException($"Career exam '{exam.Id}' has an invalid name or CFU.");

            exam.Attempts ??= new ObservableCollection<ExamAttemptData>();
            EnsureNoNullItems(exam.Attempts, "attempt");
            foreach (var attempt in exam.Attempts)
            {
                if (string.IsNullOrWhiteSpace(attempt.Id))
                    throw new InvalidDataException("An imported attempt has an empty identifier.");
                if (!attemptIds.Add(attempt.Id))
                    throw new InvalidDataException(
                        $"Duplicate attempt identifier '{attempt.Id}' found. The source data was not modified.");

                ValidateAttempt(attempt, exam.Id);
            }
        }

        // Rehydrate the domain aggregate to enforce state, grade and recording
        // invariants before data is persisted or imported.
        _ = ToDomainExams(data.CareerExams);
    }

    private static void EnsureNoNullItems<T>(IEnumerable<T> items, string label) where T : class
    {
        if (items.Any(x => x is null))
            throw new InvalidDataException($"The imported data contains a null {label}.");
    }

    private static void EnsureUniqueIds<T>(IEnumerable<T> items, Func<T, string> getId, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = getId(item);
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException($"An imported {label} has an empty identifier.");
            if (!seen.Add(id))
                throw new InvalidDataException(
                    $"Duplicate identifier '{id}' found among {label} records. The source data was not modified.");
        }
    }

    private static void ValidateAttempt(ExamAttemptData attempt, string examId)
    {
        if (attempt.AttemptedAt == default)
            throw new InvalidDataException($"Attempt '{attempt.Id}' of exam '{examId}' has no valid attempt date.");

        var status = ParseEnum<AttemptStatus>(attempt.Status, "attempt status", attempt.Id);
        var outcome = ParseEnum<ExamOutcome>(attempt.Outcome, "exam outcome", attempt.Id);
        var decision = ParseEnum<GradeDecision>(attempt.GradeDecision, "grade decision", attempt.Id);

        if (attempt.RecordedAt is not null && attempt.RecordedAt.Value < attempt.AttemptedAt)
            throw new InvalidDataException($"Attempt '{attempt.Id}' was recorded before it took place.");

        if (status != AttemptStatus.Completed)
        {
            if (outcome != ExamOutcome.Pending ||
                decision != GradeDecision.Pending ||
                attempt.NumericGrade is not null ||
                attempt.IsHonors ||
                attempt.IsPassFail ||
                attempt.RecordedAt is not null)
                throw new InvalidDataException(
                    $"Attempt '{attempt.Id}' is {status} but carries a final result. The source data is inconsistent.");
            return;
        }

        if (outcome == ExamOutcome.Pending)
            throw new InvalidDataException($"Completed attempt '{attempt.Id}' has no final outcome.");

        if (attempt.IsPassFail)
        {
            if (attempt.NumericGrade is not null || attempt.IsHonors)
                throw new InvalidDataException($"Eligibility attempt '{attempt.Id}' has a numeric grade or honors.");
            _ = GradeValue.Restore(null, false, true);
        }
        else if (attempt.NumericGrade is int value)
        {
            _ = GradeValue.Numeric(value, attempt.IsHonors);
        }
        else if (outcome != ExamOutcome.Failed)
        {
            throw new InvalidDataException($"Passed attempt '{attempt.Id}' has no numeric grade.");
        }
    }

    private static TEnum ParseEnum<TEnum>(string value, string label, string attemptId) where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            throw new InvalidDataException($"Attempt '{attemptId}' has an unknown {label} '{value}'.");
        return parsed;
    }

    public static void AddToContext(StudyHubDbContext db, StudyData data)
    {
        db.ProfileSettings.Add(new ProfileSettingsEntity
        {
            Id = 1,
            GraduationTarget = data.GraduationTarget,
            MoodleBaseUrl = data.MoodleBaseUrl
        });
        db.Courses.AddRange(data.Courses.Select(x => new CourseEntity
        {
            Id = x.Id, MoodleCourseId = x.MoodleCourseId, Name = x.Name, Code = x.Code,
            Cfu = x.Cfu, Completed = x.Completed
        }));
        db.Grades.AddRange(data.Grades.Select(x => new GradeEntity
        {
            Id = x.Id, CourseId = x.CourseId, CourseName = x.CourseName, Value = x.Value,
            Honors = x.Honors, Cfu = x.Cfu, Date = x.Date, CountsTowardAverage = x.CountsTowardAverage
        }));
        db.PlannedExams.AddRange(data.Exams.Select(x => new PlannedExamEntity
        {
            Id = x.Id, CourseId = x.CourseId, CourseName = x.CourseName, Name = x.Name,
            Cfu = x.Cfu, EstimatedStudyHours = x.EstimatedStudyHours, PlannedDate = x.PlannedDate,
            PlannedSession = x.PlannedSession, Completed = x.Completed
        }));
        db.Announcements.AddRange(data.Announcements.Select(x => new AnnouncementEntity
        {
            Id = x.Id, SourceKey = x.SourceKey, CourseName = x.CourseName, Title = x.Title,
            Summary = x.Summary, Url = x.Url, UpdatedAt = x.UpdatedAt, IsNew = x.IsNew
        }));
        db.CareerExams.AddRange(data.CareerExams.Select(x => new CareerExamEntity
        {
            Id = x.Id, CourseId = x.CourseId, Name = x.Name, Cfu = x.Cfu,
            CountsTowardAverage = x.CountsTowardAverage,
            Attempts = x.Attempts.Select(a => new ExamAttemptEntity
            {
                Id = a.Id, ExamId = x.Id, AttemptedAt = a.AttemptedAt,
                Status = (int)Enum.Parse<AttemptStatus>(a.Status, true),
                Outcome = (int)Enum.Parse<ExamOutcome>(a.Outcome, true),
                GradeDecision = (int)Enum.Parse<GradeDecision>(a.GradeDecision, true),
                NumericGrade = a.NumericGrade, IsHonors = a.IsHonors, IsPassFail = a.IsPassFail,
                RecordedAt = a.RecordedAt
            }).ToList()
        }));
    }

    public static async Task<StudyData> ReadFromContextAsync(StudyHubDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.ProfileSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var result = new StudyData
        {
            GraduationTarget = settings?.GraduationTarget ?? new DateTime(2027, 10, 1),
            MoodleBaseUrl = settings?.MoodleBaseUrl ?? string.Empty
        };
        foreach (var x in await db.Courses.AsNoTracking().ToListAsync(cancellationToken))
            result.Courses.Add(new Course { Id = x.Id, MoodleCourseId = x.MoodleCourseId, Name = x.Name, Code = x.Code, Cfu = x.Cfu, Completed = x.Completed });
        foreach (var x in await db.Grades.AsNoTracking().ToListAsync(cancellationToken))
            result.Grades.Add(new Grade { Id = x.Id, CourseId = x.CourseId, CourseName = x.CourseName, Value = x.Value, Honors = x.Honors, Cfu = x.Cfu, Date = x.Date, CountsTowardAverage = x.CountsTowardAverage });
        foreach (var x in await db.PlannedExams.AsNoTracking().ToListAsync(cancellationToken))
            result.Exams.Add(new Models.Exam { Id = x.Id, CourseId = x.CourseId, CourseName = x.CourseName, Name = x.Name, Cfu = x.Cfu, EstimatedStudyHours = x.EstimatedStudyHours, PlannedDate = x.PlannedDate, PlannedSession = x.PlannedSession, Completed = x.Completed });
        foreach (var x in await db.Announcements.AsNoTracking().ToListAsync(cancellationToken))
            result.Announcements.Add(new Announcement { Id = x.Id, SourceKey = x.SourceKey, CourseName = x.CourseName, Title = x.Title, Summary = x.Summary, Url = x.Url, UpdatedAt = x.UpdatedAt, IsNew = x.IsNew });
        var careerExams = await db.CareerExams.Include(x => x.Attempts).AsNoTracking().ToListAsync(cancellationToken);
        foreach (var x in careerExams)
            result.CareerExams.Add(new CareerExamData
            {
                Id = x.Id, CourseId = x.CourseId, Name = x.Name, Cfu = x.Cfu, CountsTowardAverage = x.CountsTowardAverage,
                Attempts = new ObservableCollection<ExamAttemptData>(x.Attempts.Select(a => new ExamAttemptData
                {
                    Id = a.Id, AttemptedAt = a.AttemptedAt, Status = Enum.GetName(typeof(AttemptStatus), a.Status)!,
                    Outcome = Enum.GetName(typeof(ExamOutcome), a.Outcome)!, GradeDecision = Enum.GetName(typeof(GradeDecision), a.GradeDecision)!,
                    NumericGrade = a.NumericGrade, IsHonors = a.IsHonors, IsPassFail = a.IsPassFail, RecordedAt = a.RecordedAt
                }))
            });

        NormalizeAndValidate(result);
        return result;
    }

    public static IReadOnlyList<CareerExam> ToDomainExams(IEnumerable<CareerExamData> source)
    {
        return source.Select(data =>
        {
            var exam = new CareerExam(data.CourseId, data.Name, data.Cfu, data.CountsTowardAverage, data.Id);
            foreach (var item in data.Attempts)
            {
                var status = ParseEnum<AttemptStatus>(item.Status, "attempt status", item.Id);
                var attempt = new ExamAttempt(data.Id, item.AttemptedAt, item.Id);
                switch (status)
                {
                    case AttemptStatus.Scheduled:
                        break;
                    case AttemptStatus.Booked:
                        attempt.Book();
                        break;
                    case AttemptStatus.InProgress:
                        attempt.Start();
                        break;
                    case AttemptStatus.Withdrawn:
                        attempt.Withdraw();
                        break;
                    case AttemptStatus.Completed:
                        var outcome = ParseEnum<ExamOutcome>(item.Outcome, "exam outcome", item.Id);
                        var decision = ParseEnum<GradeDecision>(item.GradeDecision, "grade decision", item.Id);
                        GradeValue? grade = item.IsPassFail
                            ? GradeValue.Eligibility()
                            : item.NumericGrade is int value ? GradeValue.Numeric(value, item.IsHonors) : null;
                        attempt.Complete(outcome, decision, grade, item.RecordedAt);
                        break;
                    default:
                        throw new InvalidDataException($"Attempt '{item.Id}' has an unknown status '{item.Status}'.");
                }
                exam.AddAttempt(attempt);
            }
            return exam;
        }).ToList();
    }

    /// <summary>
    /// Projects legacy courses onto the domain. Course ids are preserved so
    /// that <see cref="CareerExam.TeachingUnitId"/> continues to reference the
    /// same teaching unit; courses without CFU (for example a freshly synced
    /// Moodle course) are not yet complete teaching units and are skipped.
    /// </summary>
    public static IReadOnlyList<TeachingUnit> ToTeachingUnits(string degreeProgramId, IEnumerable<Course> courses)
    {
        ArgumentNullException.ThrowIfNull(courses);
        return courses
            .Where(x => x is not null && x.Cfu > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new TeachingUnit(
                degreeProgramId,
                x.Name,
                x.Code ?? string.Empty,
                new Cfu(x.Cfu),
                id: x.Id))
            .ToList();
    }
}
