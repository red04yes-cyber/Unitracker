using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Models = UniToStudyHub.Models;

namespace UniToStudyHub.Infrastructure.Data;

public static class StudyDataEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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
        data.CoursePrerequisites ??= new ObservableCollection<CoursePrerequisiteData>();
        data.SyllabusTopics ??= new ObservableCollection<SyllabusTopicData>();
        data.Questions ??= new ObservableCollection<QuestionBankItemData>();

        EnsureNoNullItems(data.Courses, "course");
        EnsureNoNullItems(data.Grades, "grade");
        EnsureNoNullItems(data.Exams, "planned exam");
        EnsureNoNullItems(data.Announcements, "announcement");
        EnsureNoNullItems(data.CareerExams, "career exam");
        EnsureNoNullItems(data.CoursePrerequisites, "course prerequisite");
        EnsureNoNullItems(data.SyllabusTopics, "syllabus topic");
        EnsureNoNullItems(data.Questions, "question");

        EnsureUniqueIds(data.Courses, x => x.Id, "course");
        EnsureUniqueIds(data.Grades, x => x.Id, "grade");
        EnsureUniqueIds(data.Exams, x => x.Id, "planned exam");
        EnsureUniqueIds(data.Announcements, x => x.Id, "announcement");
        EnsureUniqueIds(data.CareerExams, x => x.Id, "career exam");
        EnsureUniqueIds(data.SyllabusTopics, x => x.Id, "syllabus topic");
        EnsureUniqueIds(data.Questions, x => x.Id, "question");

        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var course in data.Courses)
        {
            if (string.IsNullOrWhiteSpace(course.Name))
                throw new InvalidDataException("A course has no name.");
            if (course.Cfu < 0)
                throw new InvalidDataException($"Course '{course.Name}' has negative CFU.");
            if (course.EstimatedStudyHours < 0)
                throw new InvalidDataException($"Course '{course.Name}' has negative estimated study hours.");
            if (!Enum.IsDefined(course.Status))
                throw new InvalidDataException($"Course '{course.Name}' has an unknown status.");
            if (course.Status == CourseStatus.Passed) course.Completed = true;
            if (course.Status == CourseStatus.Failed) course.Completed = false;
            if (course.Completed && course.Status is CourseStatus.NotStarted or CourseStatus.InProgress or CourseStatus.Prepared or CourseStatus.ExamScheduled)
                course.Status = CourseStatus.Passed;
            if (course.Status == CourseStatus.Archived) course.Archived = true;
            course.Code ??= string.Empty;
            course.Teacher ??= string.Empty;
            course.AcademicYear ??= string.Empty;
            course.Session ??= string.Empty;
            course.SyllabusSummary ??= string.Empty;
            course.SyllabusUrl ??= string.Empty;
            course.Notes ??= string.Empty;
            course.Source ??= "manual";
            if (course.SyllabusUrl.Length > 2048)
                throw new InvalidDataException($"Course '{course.Name}' has an invalid syllabus URL.");
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

        ValidateProfile(data);
        MigrateLegacyGrades(data);
        ValidateCareer(data);
        ValidatePrerequisites(data);
        ValidateSyllabus(data);
        ValidateQuestions(data);

        if (string.IsNullOrWhiteSpace(data.MoodleBaseUrl)) data.MoodleBaseUrl = string.Empty;
        if (data.GraduationTarget == default)
            throw new InvalidDataException("GraduationTarget is missing or invalid.");

        // Rehydrate the domain aggregate to enforce state, grade and recording
        // invariants before data is persisted or imported.
        _ = CareerDataProjection.ToDomainExams(data.CareerExams);
    }

    private static void ValidateProfile(StudyData data)
    {
        if (data.DegreeTotalCfu is < 0 or > 1000)
            throw new InvalidDataException("DegreeTotalCfu is outside the supported range.");
        if (data.WeeklyStudyHours is < 0 or > 168)
            throw new InvalidDataException("WeeklyStudyHours is outside the supported range.");
        if (data.MaxDailyStudyMinutes is < 0 or > 1440)
            throw new InvalidDataException("MaxDailyStudyMinutes is outside the supported range.");
        if (data.EnrollmentYear is < 1900 or > 2200)
            throw new InvalidDataException("EnrollmentYear is outside the supported range.");
        if (data.CurrentAcademicYear is < 1 or > 20)
            throw new InvalidDataException("CurrentAcademicYear is outside the supported range.");
        if (data.DegreeTotalCfu > 0 && data.Courses.Sum(x => x.Cfu) > data.DegreeTotalCfu)
            throw new InvalidDataException("Course CFU exceed the configured degree total.");
        data.DegreeProgramName ??= string.Empty;
        data.DegreeProgramCode ??= string.Empty;
        data.StudyPreferences ??= string.Empty;
    }

    /// <summary>
    /// Older builds stored only <see cref="Grade"/> rows. They are projected
    /// into the career aggregate once, with deterministic ids, so installing a
    /// new build does not lose a user's existing marks. Rows already represented
    /// by a CareerExam are deliberately left untouched to avoid duplication.
    /// </summary>
    private static void MigrateLegacyGrades(StudyData data)
    {
        foreach (var group in data.Grades.GroupBy(x => x.CourseId, StringComparer.Ordinal))
        {
            if (data.CareerExams.Any(x => string.Equals(x.CourseId, group.Key, StringComparison.Ordinal)))
                continue;

            var course = data.Courses.FirstOrDefault(x => string.Equals(x.Id, group.Key, StringComparison.Ordinal));
            if (course is null) continue;
            var validGrades = group
                .Where(x => x.Value is >= 18 and <= 30 && x.Cfu > 0)
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();
            if (validGrades.Count == 0) continue;

            var exam = new CareerExamData
            {
                Id = DeterministicId("legacy-career", course.Id),
                CourseId = course.Id,
                Name = string.IsNullOrWhiteSpace(group.First().CourseName) ? course.Name : group.First().CourseName,
                Cfu = validGrades[0].Cfu,
                CountsTowardAverage = validGrades[0].CountsTowardAverage
            };
            foreach (var grade in validGrades)
            {
                var attemptedAt = grade.Date == default
                    ? DateTimeOffset.UnixEpoch
                    : new DateTimeOffset(DateTime.SpecifyKind(grade.Date, DateTimeKind.Utc));
                exam.Attempts.Add(new ExamAttemptData
                {
                    Id = DeterministicId("legacy-grade", grade.Id),
                    AttemptedAt = attemptedAt,
                    Status = nameof(AttemptStatus.Completed),
                    Outcome = nameof(ExamOutcome.Passed),
                    GradeDecision = grade.CountsTowardAverage ? nameof(GradeDecision.Accepted) : nameof(GradeDecision.Rejected),
                    NumericGrade = grade.Value,
                    IsHonors = grade.Honors,
                    RecordedAt = grade.CountsTowardAverage ? attemptedAt.AddMinutes(1) : null
                });
            }
            data.CareerExams.Add(exam);
            course.Completed = true;
            course.Status = CourseStatus.Passed;
        }
    }

    private static void ValidateCareer(StudyData data)
    {
        var attemptIds = new HashSet<string>(StringComparer.Ordinal);
        var teachingUnitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exam in data.CareerExams)
        {
            if (string.IsNullOrWhiteSpace(exam.CourseId) || !data.Courses.Any(x => x.Id == exam.CourseId))
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
    }

    private static void ValidatePrerequisites(StudyData data)
    {
        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        var edges = new List<(string CourseId, string PrerequisiteCourseId)>();
        var pairs = new HashSet<(string, string)>();
        foreach (var prerequisite in data.CoursePrerequisites)
        {
            if (!courseIds.Contains(prerequisite.CourseId) || !courseIds.Contains(prerequisite.PrerequisiteCourseId))
                throw new InvalidDataException("A prerequisite refers to a course that does not exist.");
            if (!pairs.Add((prerequisite.CourseId, prerequisite.PrerequisiteCourseId)))
                throw new InvalidDataException("A prerequisite edge occurs more than once.");
            edges.Add((prerequisite.CourseId, prerequisite.PrerequisiteCourseId));
        }
        _ = new PrerequisiteGraphService(edges);
    }

    private static void ValidateSyllabus(StudyData data)
    {
        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var topic in data.SyllabusTopics)
        {
            if (string.IsNullOrWhiteSpace(topic.Title))
                throw new InvalidDataException($"Syllabus topic '{topic.Id}' has no title.");
            if (!courseIds.Contains(topic.CourseId))
                throw new InvalidDataException($"Syllabus topic '{topic.Id}' refers to a course that does not exist.");
            if (!Enum.IsDefined(topic.Status))
                throw new InvalidDataException($"Syllabus topic '{topic.Id}' has an unknown status.");
            if (topic.EstimatedMinutes < 0)
                throw new InvalidDataException($"Syllabus topic '{topic.Id}' has negative estimated minutes.");
            topic.Description ??= string.Empty;
            topic.SourceUrl ??= string.Empty;
        }
    }

    private static void ValidateQuestions(StudyData data)
    {
        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        var topics = data.SyllabusTopics.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var question in data.Questions)
        {
            if (!courseIds.Contains(question.CourseId))
                throw new InvalidDataException($"Question '{question.Id}' refers to a course that does not exist.");
            if (!string.IsNullOrWhiteSpace(question.SyllabusTopicId) &&
                (!topics.TryGetValue(question.SyllabusTopicId, out var topic) || topic.CourseId != question.CourseId))
                throw new InvalidDataException($"Question '{question.Id}' refers to a syllabus topic from another course.");
            if (string.IsNullOrWhiteSpace(question.Text))
                throw new InvalidDataException($"Question '{question.Id}' has no text.");
            if (!Enum.IsDefined(question.Type))
                throw new InvalidDataException($"Question '{question.Id}' has an unknown type.");
            question.Options ??= new ObservableCollection<string>();
            question.Options = new ObservableCollection<string>(question.Options.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            question.Difficulty ??= "Media";
            question.Source ??= "Manuale";
            question.Explanation ??= string.Empty;
            if (question.Type == QuestionType.Closed)
            {
                if (question.Options.Count < 2 || question.Options.Distinct(StringComparer.Ordinal).Count() != question.Options.Count)
                    throw new InvalidDataException($"Closed question '{question.Id}' must have at least two distinct options.");
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || !question.Options.Contains(question.CorrectAnswer))
                    throw new InvalidDataException($"Closed question '{question.Id}' must designate one of its options as the correct answer.");
            }
        }
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

        var status = CareerDataProjection.ParseEnum<AttemptStatus>(attempt.Status, "attempt status", attempt.Id);
        var outcome = CareerDataProjection.ParseEnum<ExamOutcome>(attempt.Outcome, "exam outcome", attempt.Id);
        var decision = CareerDataProjection.ParseEnum<GradeDecision>(attempt.GradeDecision, "grade decision", attempt.Id);

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

    public static void AddToContext(StudyHubDbContext db, StudyData data)
    {
        db.ProfileSettings.Add(new ProfileSettingsEntity
        {
            Id = 1,
            GraduationTarget = data.GraduationTarget,
            MoodleBaseUrl = data.MoodleBaseUrl,
            DegreeProgramName = data.DegreeProgramName,
            DegreeProgramCode = data.DegreeProgramCode,
            EnrollmentYear = data.EnrollmentYear,
            CurrentAcademicYear = data.CurrentAcademicYear,
            DegreeTotalCfu = data.DegreeTotalCfu,
            StudyPreferences = data.StudyPreferences,
            WeeklyStudyHours = data.WeeklyStudyHours,
            MaxDailyStudyMinutes = data.MaxDailyStudyMinutes
        });
        db.Courses.AddRange(data.Courses.Select(x => new CourseEntity
        {
            Id = x.Id,
            MoodleCourseId = x.MoodleCourseId,
            Name = x.Name,
            Code = x.Code,
            Cfu = x.Cfu,
            Completed = x.Completed,
            Status = (int)x.Status,
            Archived = x.Archived,
            Teacher = x.Teacher,
            AcademicYear = x.AcademicYear,
            Session = x.Session,
            EstimatedStudyHours = x.EstimatedStudyHours,
            AppealDate = x.AppealDate,
            SyllabusSummary = x.SyllabusSummary,
            SyllabusUrl = x.SyllabusUrl,
            Notes = x.Notes,
            Source = x.Source,
            UpdatedAt = x.UpdatedAt
        }));
        db.Grades.AddRange(data.Grades.Select(x => new GradeEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            CourseName = x.CourseName,
            Value = x.Value,
            Honors = x.Honors,
            Cfu = x.Cfu,
            Date = x.Date,
            CountsTowardAverage = x.CountsTowardAverage
        }));
        db.PlannedExams.AddRange(data.Exams.Select(x => new PlannedExamEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            CourseName = x.CourseName,
            Name = x.Name,
            Cfu = x.Cfu,
            EstimatedStudyHours = x.EstimatedStudyHours,
            PlannedDate = x.PlannedDate,
            PlannedSession = x.PlannedSession,
            Completed = x.Completed
        }));
        db.Announcements.AddRange(data.Announcements.Select(x => new AnnouncementEntity
        {
            Id = x.Id,
            SourceKey = x.SourceKey,
            CourseName = x.CourseName,
            Title = x.Title,
            Summary = x.Summary,
            Url = x.Url,
            UpdatedAt = x.UpdatedAt,
            IsNew = x.IsNew
        }));
        db.CareerExams.AddRange(data.CareerExams.Select(x => new CareerExamEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            Name = x.Name,
            Cfu = x.Cfu,
            CountsTowardAverage = x.CountsTowardAverage,
            Attempts = x.Attempts.Select(a => new ExamAttemptEntity
            {
                Id = a.Id,
                ExamId = x.Id,
                AttemptedAt = a.AttemptedAt,
                Status = (int)CareerDataProjection.ParseEnum<AttemptStatus>(a.Status, "attempt status", a.Id),
                Outcome = (int)CareerDataProjection.ParseEnum<ExamOutcome>(a.Outcome, "exam outcome", a.Id),
                GradeDecision = (int)CareerDataProjection.ParseEnum<GradeDecision>(a.GradeDecision, "grade decision", a.Id),
                NumericGrade = a.NumericGrade,
                IsHonors = a.IsHonors,
                IsPassFail = a.IsPassFail,
                RecordedAt = a.RecordedAt
            }).ToList()
        }));
        db.CoursePrerequisites.AddRange(data.CoursePrerequisites.Select(x => new CoursePrerequisiteEntity
        {
            CourseId = x.CourseId,
            PrerequisiteCourseId = x.PrerequisiteCourseId,
            IsMandatory = x.IsMandatory
        }));
        db.SyllabusTopics.AddRange(data.SyllabusTopics.Select(x => new SyllabusTopicEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            Title = x.Title,
            Description = x.Description,
            Status = (int)x.Status,
            EstimatedMinutes = x.EstimatedMinutes,
            SourceUrl = x.SourceUrl,
            LastStudiedAt = x.LastStudiedAt,
            UpdatedAt = x.UpdatedAt
        }));
        db.QuestionBankItems.AddRange(data.Questions.Select(x => new QuestionBankItemEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            SyllabusTopicId = x.SyllabusTopicId,
            Text = x.Text,
            Type = (int)x.Type,
            Difficulty = x.Difficulty,
            OptionsJson = JsonSerializer.Serialize(x.Options),
            CorrectAnswer = x.CorrectAnswer,
            Explanation = x.Explanation,
            Source = x.Source,
            IsOfficial = x.IsOfficial,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }));
    }

    public static async Task<StudyData> ReadFromContextAsync(StudyHubDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.ProfileSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var result = new StudyData
        {
            GraduationTarget = settings?.GraduationTarget ?? new DateTime(2027, 10, 1),
            MoodleBaseUrl = settings?.MoodleBaseUrl ?? string.Empty,
            DegreeProgramName = settings?.DegreeProgramName ?? string.Empty,
            DegreeProgramCode = settings?.DegreeProgramCode ?? string.Empty,
            EnrollmentYear = settings?.EnrollmentYear,
            CurrentAcademicYear = settings?.CurrentAcademicYear,
            DegreeTotalCfu = settings?.DegreeTotalCfu ?? 0,
            StudyPreferences = settings?.StudyPreferences ?? string.Empty,
            WeeklyStudyHours = settings?.WeeklyStudyHours ?? 20,
            MaxDailyStudyMinutes = settings?.MaxDailyStudyMinutes ?? 240
        };
        foreach (var x in await db.Courses.AsNoTracking().ToListAsync(cancellationToken))
            result.Courses.Add(new Course
            {
                Id = x.Id,
                MoodleCourseId = x.MoodleCourseId,
                Name = x.Name,
                Code = x.Code,
                Cfu = x.Cfu,
                Completed = x.Completed,
                Status = (CourseStatus)x.Status,
                Archived = x.Archived,
                Teacher = x.Teacher,
                AcademicYear = x.AcademicYear,
                Session = x.Session,
                EstimatedStudyHours = x.EstimatedStudyHours,
                AppealDate = x.AppealDate,
                SyllabusSummary = x.SyllabusSummary,
                SyllabusUrl = x.SyllabusUrl,
                Notes = x.Notes,
                Source = x.Source,
                UpdatedAt = x.UpdatedAt
            });
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
                Id = x.Id,
                CourseId = x.CourseId,
                Name = x.Name,
                Cfu = x.Cfu,
                CountsTowardAverage = x.CountsTowardAverage,
                Attempts = new ObservableCollection<ExamAttemptData>(x.Attempts.Select(a => new ExamAttemptData
                {
                    Id = a.Id,
                    AttemptedAt = a.AttemptedAt,
                    Status = Enum.GetName(typeof(AttemptStatus), a.Status)!,
                    Outcome = Enum.GetName(typeof(ExamOutcome), a.Outcome)!,
                    GradeDecision = Enum.GetName(typeof(GradeDecision), a.GradeDecision)!,
                    NumericGrade = a.NumericGrade,
                    IsHonors = a.IsHonors,
                    IsPassFail = a.IsPassFail,
                    RecordedAt = a.RecordedAt
                }))
            });
        foreach (var x in await db.CoursePrerequisites.AsNoTracking().ToListAsync(cancellationToken))
            result.CoursePrerequisites.Add(new CoursePrerequisiteData
            {
                CourseId = x.CourseId,
                PrerequisiteCourseId = x.PrerequisiteCourseId,
                IsMandatory = x.IsMandatory
            });
        foreach (var x in await db.SyllabusTopics.AsNoTracking().ToListAsync(cancellationToken))
            result.SyllabusTopics.Add(new SyllabusTopicData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                Title = x.Title,
                Description = x.Description,
                Status = (SyllabusTopicStatus)x.Status,
                EstimatedMinutes = x.EstimatedMinutes,
                SourceUrl = x.SourceUrl,
                LastStudiedAt = x.LastStudiedAt,
                UpdatedAt = x.UpdatedAt
            });
        foreach (var x in await db.QuestionBankItems.AsNoTracking().ToListAsync(cancellationToken))
        {
            ObservableCollection<string> options;
            try
            {
                options = JsonSerializer.Deserialize<ObservableCollection<string>>(x.OptionsJson, JsonOptions)
                    ?? new ObservableCollection<string>();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Question '{x.Id}' has invalid option data.", ex);
            }
            result.Questions.Add(new QuestionBankItemData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                SyllabusTopicId = x.SyllabusTopicId,
                Text = x.Text,
                Type = (QuestionType)x.Type,
                Difficulty = x.Difficulty,
                Options = options,
                CorrectAnswer = x.CorrectAnswer,
                Explanation = x.Explanation,
                Source = x.Source,
                IsOfficial = x.IsOfficial,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        }

        NormalizeAndValidate(result);
        return result;
    }

    public static IReadOnlyList<Domain.Entities.CareerExam> ToDomainExams(IEnumerable<CareerExamData> source) =>
        CareerDataProjection.ToDomainExams(source);

    /// <summary>
    /// Projects legacy courses onto the domain. Course ids are preserved so
    /// that <see cref="Domain.Entities.CareerExam.TeachingUnitId"/> continues to
    /// reference the same teaching unit; courses without CFU (for example a
    /// freshly synced Moodle course) are not yet complete teaching units.
    /// </summary>
    public static IReadOnlyList<Domain.Entities.TeachingUnit> ToTeachingUnits(string degreeProgramId, IEnumerable<Course> courses)
    {
        ArgumentNullException.ThrowIfNull(courses);
        return courses
            .Where(x => x is not null && x.Cfu > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new Domain.Entities.TeachingUnit(
                degreeProgramId,
                x.Name,
                x.Code ?? string.Empty,
                new Domain.ValueObjects.Cfu(x.Cfu),
                id: x.Id))
            .ToList();
    }

    private static string DeterministicId(string prefix, string value) =>
        $"{prefix}:{value}";
}
