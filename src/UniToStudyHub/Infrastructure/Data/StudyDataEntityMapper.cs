using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using Models = UniToStudyHub.Models;

namespace UniToStudyHub.Infrastructure.Data;

public static class StudyDataEntityMapper
{
    public static void NormalizeAndValidate(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Courses ??= new ObservableCollection<Course>();
        data.Grades ??= new ObservableCollection<Grade>();
        data.Exams ??= new ObservableCollection<Models.Exam>();
        data.Announcements ??= new ObservableCollection<Announcement>();
        data.CareerExams ??= new ObservableCollection<CareerExamData>();
        MakeIdsUnique(data.Courses, x => x.Id, (x, id) => x.Id = id);
        MakeIdsUnique(data.Grades, x => x.Id, (x, id) => x.Id = id);
        MakeIdsUnique(data.Exams, x => x.Id, (x, id) => x.Id = id);
        MakeIdsUnique(data.Announcements, x => x.Id, (x, id) => x.Id = id);
        MakeIdsUnique(data.CareerExams, x => x.Id, (x, id) => x.Id = id);
        PopulateCareerExamsFromLegacyGrades(data);

        foreach (var c in data.Courses)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) throw new InvalidDataException("A course has no name.");
            if (c.Cfu < 0) throw new InvalidDataException($"Course '{c.Name}' has negative CFU.");
        }
        foreach (var g in data.Grades)
        {
            if (g.Value is < 18 or > 30) throw new InvalidDataException($"Grade '{g.Id}' is outside the 18–30 range.");
            if (g.Cfu <= 0) throw new InvalidDataException($"Grade '{g.Id}' must have positive CFU.");
            if (g.Honors && g.Value != 30) throw new InvalidDataException($"Grade '{g.Id}' marks honors for a value other than 30.");
        }
        foreach (var e in data.Exams)
        {
            if (string.IsNullOrWhiteSpace(e.Name)) throw new InvalidDataException("A planned exam has no name.");
            if (e.Cfu < 0 || e.EstimatedStudyHours < 0) throw new InvalidDataException($"Planned exam '{e.Name}' has a negative value.");
        }
        foreach (var exam in data.CareerExams)
        {
            if (string.IsNullOrWhiteSpace(exam.Name) || exam.Cfu <= 0) throw new InvalidDataException($"Career exam '{exam.Id}' has invalid name or CFU.");
            exam.Attempts ??= new ObservableCollection<ExamAttemptData>();
            MakeIdsUnique(exam.Attempts, x => x.Id, (x, id) => x.Id = id);
            foreach (var attempt in exam.Attempts)
            {
                if (!Enum.TryParse<AttemptStatus>(attempt.Status, true, out var status)) throw new InvalidDataException($"Unknown attempt status '{attempt.Status}'.");
                if (!Enum.TryParse<ExamOutcome>(attempt.Outcome, true, out var outcome)) throw new InvalidDataException($"Unknown exam outcome '{attempt.Outcome}'.");
                if (!Enum.TryParse<GradeDecision>(attempt.GradeDecision, true, out var decision)) throw new InvalidDataException($"Unknown grade decision '{attempt.GradeDecision}'.");
                if (!attempt.IsPassFail && attempt.NumericGrade is not null)
                    _ = GradeValue.Numeric(attempt.NumericGrade.Value, attempt.IsHonors);
                else if (attempt.IsPassFail)
                    _ = GradeValue.Restore(null, attempt.IsHonors, true);

                if (status == AttemptStatus.Completed)
                {
                    if (outcome == ExamOutcome.Passed)
                    {
                        if (attempt.NumericGrade is null || attempt.IsPassFail || decision is not (GradeDecision.Accepted or GradeDecision.Rejected))
                            throw new InvalidDataException($"Completed graded attempt '{attempt.Id}' has inconsistent outcome or grade decision.");
                        if (decision == GradeDecision.Accepted && attempt.RecordedAt is null)
                            throw new InvalidDataException($"Accepted attempt '{attempt.Id}' has no recording timestamp.");
                        if (decision == GradeDecision.Rejected && attempt.RecordedAt is not null)
                            throw new InvalidDataException($"Rejected attempt '{attempt.Id}' cannot have a recording timestamp.");
                    }
                    else if (outcome == ExamOutcome.EligibilityPassed)
                    {
                        if (!attempt.IsPassFail || decision != GradeDecision.NotApplicable || attempt.RecordedAt is null)
                            throw new InvalidDataException($"Eligibility attempt '{attempt.Id}' has inconsistent result data.");
                    }
                    else if (outcome == ExamOutcome.Failed)
                    {
                        if (attempt.NumericGrade is not null || attempt.IsPassFail || decision != GradeDecision.NotApplicable || attempt.RecordedAt is not null)
                            throw new InvalidDataException($"Failed attempt '{attempt.Id}' has inconsistent result data.");
                    }
                    else throw new InvalidDataException($"Completed attempt '{attempt.Id}' has a pending outcome.");
                }
                else if (attempt.RecordedAt is not null)
                    throw new InvalidDataException($"Non-completed attempt '{attempt.Id}' cannot have a recording timestamp.");
            }
        }
        if (string.IsNullOrWhiteSpace(data.MoodleBaseUrl)) data.MoodleBaseUrl = string.Empty;
    }


    private static void PopulateCareerExamsFromLegacyGrades(StudyData data)
    {
        var representedCourseIds = data.CareerExams.Select(x => x.CourseId).ToHashSet(StringComparer.Ordinal);
        var courseById = data.Courses.GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var group in data.Grades.GroupBy(
                     g => string.IsNullOrWhiteSpace(g.CourseId) ? $"legacy-orphan:{g.Id}" : g.CourseId,
                     StringComparer.Ordinal))
        {
            if (representedCourseIds.Contains(group.Key)) continue;
            var grades = group.OrderBy(g => g.Date).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
            var latest = grades[^1];
            courseById.TryGetValue(group.Key, out var course);
            var cfu = course?.Cfu > 0 ? course.Cfu : latest.Cfu;
            var exam = new CareerExamData
            {
                Id = Guid.NewGuid().ToString("N"),
                CourseId = group.Key,
                Name = string.IsNullOrWhiteSpace(course?.Name) ? latest.CourseName : course.Name,
                Cfu = cfu,
                CountsTowardAverage = latest.CountsTowardAverage
            };
            foreach (var grade in grades)
            {
                var attemptedAt = new DateTimeOffset(grade.Date);
                exam.Attempts.Add(new ExamAttemptData
                {
                    Id = grade.Id,
                    AttemptedAt = attemptedAt,
                    Status = nameof(AttemptStatus.Completed),
                    Outcome = nameof(ExamOutcome.Passed),
                    GradeDecision = nameof(GradeDecision.Accepted),
                    NumericGrade = grade.Value,
                    IsHonors = grade.Honors,
                    IsPassFail = false,
                    RecordedAt = attemptedAt
                });
            }
            data.CareerExams.Add(exam);
            representedCourseIds.Add(group.Key);
        }
    }

    private static void MakeIdsUnique<T>(IEnumerable<T> items, Func<T, string> getId, Action<T, string> setId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = getId(item);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                do { id = Guid.NewGuid().ToString("N"); } while (!seen.Add(id));
                setId(item, id);
            }
        }
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
        return result;
    }

    public static IReadOnlyList<CareerExam> ToDomainExams(IEnumerable<CareerExamData> source)
    {
        return source.Select(data =>
        {
            var exam = new CareerExam(data.CourseId, data.Name, data.Cfu, data.CountsTowardAverage, data.Id);
            foreach (var item in data.Attempts)
            {
                var attempt = new ExamAttempt(data.Id, item.AttemptedAt, item.Id);
                if (Enum.TryParse<AttemptStatus>(item.Status, true, out var status))
                {
                    if (status == AttemptStatus.Booked) attempt.Book();
                    else if (status == AttemptStatus.InProgress) attempt.Start();
                    else if (status == AttemptStatus.Withdrawn) attempt.Withdraw();
                }
                if (Enum.TryParse<AttemptStatus>(item.Status, true, out status) && status == AttemptStatus.Completed)
                {
                    var outcome = Enum.Parse<ExamOutcome>(item.Outcome, true);
                    var decision = Enum.Parse<GradeDecision>(item.GradeDecision, true);
                    GradeValue? grade = item.IsPassFail ? GradeValue.Eligibility() :
                        item.NumericGrade is int value ? GradeValue.Numeric(value, item.IsHonors) : null;
                    attempt.Complete(outcome, decision, grade, item.RecordedAt);
                }
                exam.AddAttempt(attempt);
            }
            return exam;
        }).ToList();
    }
}
