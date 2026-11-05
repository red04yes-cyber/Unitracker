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
        data.CalendarEvents ??= new ObservableCollection<CalendarEvent>();
        data.StudySessions ??= new ObservableCollection<StudySessionData>();
        data.StudyTasks ??= new ObservableCollection<StudyTaskData>();
        data.StudyActivities ??= new ObservableCollection<StudyActivityData>();
        data.StudyMissions ??= new ObservableCollection<StudyMissionData>();
        data.PomodoroSettings ??= new PomodoroSettingsData();
        data.PomodoroCycles ??= new ObservableCollection<PomodoroCycleData>();
        data.ActivityRevisions ??= new ObservableCollection<ActivityRevisionData>();
        data.ErrorJournalEntries ??= new ObservableCollection<ErrorJournalEntryData>();
        data.Flashcards ??= new ObservableCollection<FlashcardData>();
        data.FlashcardReviews ??= new ObservableCollection<FlashcardReviewData>();
        data.Distractions ??= new ObservableCollection<DistractionData>();
        data.EnergyCheckIns ??= new ObservableCollection<EnergyCheckInData>();
        data.PdfDocuments ??= new ObservableCollection<PdfDocumentData>();

        EnsureNoNullItems(data.Courses, "course");
        EnsureNoNullItems(data.Grades, "grade");
        EnsureNoNullItems(data.Exams, "planned exam");
        EnsureNoNullItems(data.Announcements, "announcement");
        EnsureNoNullItems(data.CareerExams, "career exam");
        EnsureNoNullItems(data.CoursePrerequisites, "course prerequisite");
        EnsureNoNullItems(data.SyllabusTopics, "syllabus topic");
        EnsureNoNullItems(data.Questions, "question");
        EnsureNoNullItems(data.CalendarEvents, "calendar event");
        EnsureNoNullItems(data.StudySessions, "study session");
        EnsureNoNullItems(data.StudyTasks, "study task");
        EnsureNoNullItems(data.StudyActivities, "study activity");
        EnsureNoNullItems(data.StudyMissions, "study mission");
        EnsureNoNullItems(data.PomodoroCycles, "Pomodoro cycle");
        EnsureNoNullItems(data.ActivityRevisions, "activity revision");
        EnsureNoNullItems(data.ErrorJournalEntries, "error journal entry");
        EnsureNoNullItems(data.Flashcards, "flashcard");
        EnsureNoNullItems(data.FlashcardReviews, "flashcard review");
        EnsureNoNullItems(data.Distractions, "distraction");
        EnsureNoNullItems(data.EnergyCheckIns, "energy check-in");
        EnsureNoNullItems(data.PdfDocuments, "PDF document");

        EnsureUniqueIds(data.Courses, x => x.Id, "course");
        EnsureUniqueIds(data.Grades, x => x.Id, "grade");
        EnsureUniqueIds(data.Exams, x => x.Id, "planned exam");
        EnsureUniqueIds(data.Announcements, x => x.Id, "announcement");
        EnsureUniqueIds(data.CareerExams, x => x.Id, "career exam");
        EnsureUniqueIds(data.SyllabusTopics, x => x.Id, "syllabus topic");
        EnsureUniqueIds(data.Questions, x => x.Id, "question");
        EnsureUniqueIds(data.CalendarEvents, x => x.Id, "calendar event");
        EnsureUniqueIds(data.StudySessions, x => x.Id, "study session");
        EnsureUniqueIds(data.StudyTasks, x => x.Id, "study task");
        EnsureUniqueIds(data.StudyActivities, x => x.Id, "study activity");
        EnsureUniqueIds(data.StudyMissions, x => x.Id, "study mission");
        EnsureUniqueIds(data.PomodoroCycles, x => x.Id, "Pomodoro cycle");
        EnsureUniqueIds(data.ActivityRevisions, x => x.Id, "activity revision");
        EnsureUniqueIds(data.ErrorJournalEntries, x => x.Id, "error journal entry");
        EnsureUniqueIds(data.Flashcards, x => x.Id, "flashcard");
        EnsureUniqueIds(data.FlashcardReviews, x => x.Id, "flashcard review");
        EnsureUniqueIds(data.Distractions, x => x.Id, "distraction");
        EnsureUniqueIds(data.EnergyCheckIns, x => x.Id, "energy check-in");
        EnsureUniqueIds(data.PdfDocuments, x => x.Id, "PDF document");

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
        ValidateCalendarAndStudyLab(data);

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

    private static void ValidateCalendarAndStudyLab(StudyData data)
    {
        var courseIds = new HashSet<string>(data.Courses.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var item in data.CalendarEvents)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title))
                throw new InvalidDataException("A calendar event has no id or title.");
            if (item.EndsAt <= item.StartsAt)
                throw new InvalidDataException($"Calendar event '{item.Id}' ends before it starts.");
            if (item.StartsAt == default || item.EndsAt == default)
                throw new InvalidDataException($"Calendar event '{item.Id}' has invalid timestamps.");
            if (item.Title.Length > 400 || item.Location.Length > 400 || item.Notes.Length > 12000)
                throw new InvalidDataException($"Calendar event '{item.Id}' contains an overlong field.");
            if (!string.IsNullOrWhiteSpace(item.CourseId) && !courseIds.Contains(item.CourseId))
                throw new InvalidDataException($"Calendar event '{item.Id}' refers to a course that does not exist.");
            if (string.IsNullOrWhiteSpace(item.Category) || string.IsNullOrWhiteSpace(item.Source) ||
                string.IsNullOrWhiteSpace(item.Verification))
                throw new InvalidDataException($"Calendar event '{item.Id}' has no category, source or verification state.");
            item.CourseName ??= string.Empty;
            item.Location ??= string.Empty;
            item.Notes ??= string.Empty;
            item.Source ??= CalendarEventSource.Manual;
            item.Verification ??= CalendarEventVerification.Unverified;
            item.TimeZoneId = string.IsNullOrWhiteSpace(item.TimeZoneId) ? TimeZoneInfo.Local.Id : item.TimeZoneId;
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(item.TimeZoneId); }
            catch (TimeZoneNotFoundException) { throw new InvalidDataException($"Calendar event '{item.Id}' has an unknown time zone."); }
            catch (InvalidTimeZoneException) { throw new InvalidDataException($"Calendar event '{item.Id}' has an invalid time zone."); }
        }

        ValidatePomodoro(data);

        var segmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in data.StudySessions)
        {
            if (session.PlannedMinutes is < 1 or > 1440)
                throw new InvalidDataException($"Study session '{session.Id}' has an invalid planned duration.");
            if (session.AccumulatedFocusSeconds < 0 || session.AccumulatedPauseSeconds < 0)
                throw new InvalidDataException($"Study session '{session.Id}' has negative accumulated time.");
            if (!string.IsNullOrWhiteSpace(session.CourseId) && !courseIds.Contains(session.CourseId))
                throw new InvalidDataException($"Study session '{session.Id}' refers to a course that does not exist.");
            if (!Enum.IsDefined(session.State))
                throw new InvalidDataException($"Study session '{session.Id}' has an unknown state.");
            if (session.State == StudySessionState.Running && session.PausedAt is not null)
                throw new InvalidDataException($"Study session '{session.Id}' is running but marked paused.");
            if (session.State == StudySessionState.Paused && session.PausedAt is null)
                throw new InvalidDataException($"Study session '{session.Id}' is paused without a pause timestamp.");

            session.Segments ??= new ObservableCollection<StudySessionSegmentData>();
            DateTimeOffset? previousEnd = null;
            StudySessionSegmentData? open = null;
            foreach (var segment in session.Segments)
            {
                if (string.IsNullOrWhiteSpace(segment.Id) || !segmentIds.Add(segment.Id))
                    throw new InvalidDataException($"Study session '{session.Id}' has a missing or duplicate segment id.");
                if (!Enum.IsDefined(segment.Type) || segment.StartedAt == default)
                    throw new InvalidDataException($"Study session '{session.Id}' has an invalid segment.");
                if (segment.EndedAt is { } end && end <= segment.StartedAt)
                    throw new InvalidDataException($"Study session '{session.Id}' has a segment that ends before it starts.");
                if (previousEnd is { } closed && segment.StartedAt < closed)
                    throw new InvalidDataException($"Study session '{session.Id}' has overlapping segments.");
                if (open is not null)
                    throw new InvalidDataException($"Study session '{session.Id}' has more than one open segment.");
                previousEnd = segment.EndedAt;
                if (segment.EndedAt is null) open = segment;
            }

            if (session.State == StudySessionState.Running && open is not null && open.Type != StudySessionSegmentType.Focus)
                throw new InvalidDataException($"Running study session '{session.Id}' has an open pause segment.");
            if (session.State == StudySessionState.Paused && open is not null && open.Type != StudySessionSegmentType.Pause)
                throw new InvalidDataException($"Paused study session '{session.Id}' has an open focus segment.");
        }

        foreach (var task in data.StudyTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Title))
                throw new InvalidDataException($"Study task '{task.Id}' has no title.");
            if (task.EstimatedMinutes is < 0 or > 14400 || task.Priority is < 1 or > 5)
                throw new InvalidDataException($"Study task '{task.Id}' has an invalid estimate or priority.");
            if (!Enum.IsDefined(task.Status))
                throw new InvalidDataException($"Study task '{task.Id}' has an unknown status.");
            if (!string.IsNullOrWhiteSpace(task.CourseId) && !courseIds.Contains(task.CourseId))
                throw new InvalidDataException($"Study task '{task.Id}' refers to a course that does not exist.");
        }

        foreach (var mission in data.StudyMissions)
        {
            if (string.IsNullOrWhiteSpace(mission.Title))
                throw new InvalidDataException($"Study mission '{mission.Id}' has no title.");
            if (!Enum.IsDefined(mission.Period) || !Enum.IsDefined(mission.Status))
                throw new InvalidDataException($"Study mission '{mission.Id}' has an invalid period or status.");
            if (mission.Difficulty is < 1 or > 5)
                throw new InvalidDataException($"Study mission '{mission.Id}' has an invalid difficulty.");
            if (mission.StartsAt == default || mission.DueAt is { } due && due < mission.StartsAt)
                throw new InvalidDataException($"Study mission '{mission.Id}' has invalid dates.");
            if (!string.IsNullOrWhiteSpace(mission.CourseId) && !courseIds.Contains(mission.CourseId))
                throw new InvalidDataException($"Study mission '{mission.Id}' refers to a course that does not exist.");
            if (!string.IsNullOrWhiteSpace(mission.SyllabusTopicId) &&
                !data.SyllabusTopics.Any(x => string.Equals(x.Id, mission.SyllabusTopicId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Study mission '{mission.Id}' refers to a syllabus topic that does not exist.");
            mission.Description ??= string.Empty;
            mission.Reward ??= string.Empty;
            mission.Source ??= "manual";
            mission.Steps ??= new ObservableCollection<MissionStepData>();
            if (mission.Steps.Count > 200)
                throw new InvalidDataException($"Study mission '{mission.Id}' has too many steps.");
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < mission.Steps.Count; index++)
            {
                var step = mission.Steps[index];
                if (string.IsNullOrWhiteSpace(step.Id) || !stepIds.Add(step.Id))
                    throw new InvalidDataException($"Study mission '{mission.Id}' has a missing or duplicate step id.");
                if (string.IsNullOrWhiteSpace(step.Title))
                    throw new InvalidDataException($"Study mission '{mission.Id}' has a step without a title.");
                if (step.IsCompleted && step.CompletedAt is null)
                    throw new InvalidDataException($"Completed step '{step.Id}' has no completion timestamp.");
                step.Position = index;
            }
        }

        var missionIds = new HashSet<string>(data.StudyMissions.Select(x => x.Id), StringComparer.Ordinal);
        var linkedSessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in data.StudyActivities)
        {
            if (string.IsNullOrWhiteSpace(activity.Title))
                throw new InvalidDataException($"Study activity '{activity.Id}' has no title.");
            if (!Enum.IsDefined(activity.Type) || !Enum.IsDefined(activity.Origin) || !Enum.IsDefined(activity.Status))
                throw new InvalidDataException($"Study activity '{activity.Id}' has an invalid type, origin or status.");
            if (activity.StartedAt == default || activity.EndedAt is { } ended && ended <= activity.StartedAt)
                throw new InvalidDataException($"Study activity '{activity.Id}' has invalid timestamps.");
            if (activity.Status == StudyActivityStatus.Completed && activity.EndedAt is null)
                throw new InvalidDataException($"Completed study activity '{activity.Id}' has no end timestamp.");
            if (activity.EffectiveSeconds is < 0 or > 86400)
                throw new InvalidDataException($"Study activity '{activity.Id}' has invalid effective seconds.");
            if (activity.EndedAt is { } end &&
                activity.EffectiveSeconds > (int)Math.Floor((end - activity.StartedAt).TotalSeconds))
                throw new InvalidDataException($"Study activity '{activity.Id}' credits more time than its interval.");
            if (string.IsNullOrWhiteSpace(activity.TimeZoneId))
                throw new InvalidDataException($"Study activity '{activity.Id}' has no time zone.");
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(activity.TimeZoneId); }
            catch (TimeZoneNotFoundException) { throw new InvalidDataException($"Study activity '{activity.Id}' has an unknown time zone."); }
            catch (InvalidTimeZoneException) { throw new InvalidDataException($"Study activity '{activity.Id}' has an invalid time zone."); }
            activity.Note ??= string.Empty;
            activity.Result ??= string.Empty;
            activity.Tags ??= new ObservableCollection<string>();
            if (activity.Note.Length > 12000 || activity.Result.Length > 2000 ||
                activity.Tags.Count > 20 || activity.Tags.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 60))
                throw new InvalidDataException($"Study activity '{activity.Id}' has overlong text or invalid tags.");
            if (activity.FocusRating is < 1 or > 5 || activity.DifficultyRating is < 1 or > 5 ||
                activity.QualityRating is < 1 or > 5 || activity.EnergyRating is < 1 or > 5)
                throw new InvalidDataException($"Study activity '{activity.Id}' has a rating outside 1–5.");
            if (!string.IsNullOrWhiteSpace(activity.CourseId) && !courseIds.Contains(activity.CourseId))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to a course that does not exist.");
            if (!string.IsNullOrWhiteSpace(activity.SyllabusTopicId) &&
                !data.SyllabusTopics.Any(x => string.Equals(x.Id, activity.SyllabusTopicId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to a syllabus topic that does not exist.");
            if (!string.IsNullOrWhiteSpace(activity.TaskId) &&
                !data.StudyTasks.Any(x => string.Equals(x.Id, activity.TaskId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to a task that does not exist.");
            if (!string.IsNullOrWhiteSpace(activity.MissionId) && !missionIds.Contains(activity.MissionId))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to a mission that does not exist.");
            if (!string.IsNullOrWhiteSpace(activity.ExamId) &&
                !data.Exams.Any(x => string.Equals(x.Id, activity.ExamId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to an exam that does not exist.");
            if (!string.IsNullOrWhiteSpace(activity.SessionId))
            {
                if (!data.StudySessions.Any(x => string.Equals(x.Id, activity.SessionId, StringComparison.Ordinal)))
                    throw new InvalidDataException($"Study activity '{activity.Id}' refers to a session that does not exist.");
                if (!linkedSessionIds.Add(activity.SessionId))
                    throw new InvalidDataException($"More than one activity is linked to session '{activity.SessionId}'.");
            }
            if (!string.IsNullOrWhiteSpace(activity.CalendarEventId) &&
                !data.CalendarEvents.Any(x => string.Equals(x.Id, activity.CalendarEventId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Study activity '{activity.Id}' refers to a calendar event that does not exist.");
        }

        var activityIds = new HashSet<string>(data.StudyActivities.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var revision in data.ActivityRevisions)
        {
            if (!activityIds.Contains(revision.ActivityId))
                throw new InvalidDataException($"Activity revision '{revision.Id}' refers to a missing activity.");
            if (!Enum.IsDefined(revision.Action) || string.IsNullOrWhiteSpace(revision.Reason))
                throw new InvalidDataException($"Activity revision '{revision.Id}' has an invalid action or reason.");
            if (revision.BeforeJson.Length > 200000 || revision.AfterJson.Length > 200000)
                throw new InvalidDataException($"Activity revision '{revision.Id}' is too large.");
            if (revision.RevisedAt == default)
                throw new InvalidDataException($"Activity revision '{revision.Id}' has no timestamp.");
        }

        var topicIds = new HashSet<string>(data.SyllabusTopics.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var entry in data.ErrorJournalEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Question) || string.IsNullOrWhiteSpace(entry.CorrectAnswer))
                throw new InvalidDataException($"Error journal entry '{entry.Id}' is incomplete.");
            if (!string.IsNullOrWhiteSpace(entry.CourseId) && !courseIds.Contains(entry.CourseId))
                throw new InvalidDataException($"Error journal entry '{entry.Id}' refers to a course that does not exist.");
            if (!string.IsNullOrWhiteSpace(entry.SyllabusTopicId) && !topicIds.Contains(entry.SyllabusTopicId))
                throw new InvalidDataException($"Error journal entry '{entry.Id}' refers to a syllabus topic that does not exist.");
            if (!Enum.IsDefined(entry.Status) || entry.ReviewCount < 0)
                throw new InvalidDataException($"Error journal entry '{entry.Id}' has invalid review data.");
        }

        var cardIds = new HashSet<string>(data.Flashcards.Select(x => x.Id), StringComparer.Ordinal);
        foreach (var card in data.Flashcards)
        {
            if (string.IsNullOrWhiteSpace(card.Front) || string.IsNullOrWhiteSpace(card.Back))
                throw new InvalidDataException($"Flashcard '{card.Id}' is missing its front or back.");
            if (card.EaseFactor is < 1.3m or > 2.8m || card.IntervalDays < 0 || card.Repetitions < 0 || card.Lapses < 0)
                throw new InvalidDataException($"Flashcard '{card.Id}' has invalid scheduling data.");
            if (!string.IsNullOrWhiteSpace(card.CourseId) && !courseIds.Contains(card.CourseId))
                throw new InvalidDataException($"Flashcard '{card.Id}' refers to a course that does not exist.");
        }

        foreach (var review in data.FlashcardReviews)
        {
            if (!cardIds.Contains(review.FlashcardId))
                throw new InvalidDataException($"Flashcard review '{review.Id}' refers to a missing flashcard.");
            if (!Enum.IsDefined(review.Rating) || review.ReviewedAt == default)
                throw new InvalidDataException($"Flashcard review '{review.Id}' is invalid.");
        }

        foreach (var distraction in data.Distractions)
        {
            if (string.IsNullOrWhiteSpace(distraction.Text))
                throw new InvalidDataException($"Distraction '{distraction.Id}' has no text.");
            if (!string.IsNullOrWhiteSpace(distraction.SessionId) &&
                !data.StudySessions.Any(x => string.Equals(x.Id, distraction.SessionId, StringComparison.Ordinal)))
                throw new InvalidDataException($"Distraction '{distraction.Id}' refers to a missing session.");
        }

        foreach (var checkIn in data.EnergyCheckIns)
        {
            if (checkIn.EnergyLevel is < 1 or > 5 || checkIn.FocusLevel is < 1 or > 5)
                throw new InvalidDataException($"Energy check-in '{checkIn.Id}' must use 1–5 levels.");
            if (checkIn.RecordedAt == default)
                throw new InvalidDataException($"Energy check-in '{checkIn.Id}' has no timestamp.");
        }

        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in data.PdfDocuments)
        {
            if (string.IsNullOrWhiteSpace(document.FileName) || string.IsNullOrWhiteSpace(document.Sha256) ||
                document.Sha256.Length != 64 || document.PageCount < 1)
                throw new InvalidDataException("A PDF document has invalid metadata.");
            if (!hashes.Add(document.Sha256))
                throw new InvalidDataException($"The same PDF was imported more than once: {document.FileName}.");
            if (!string.IsNullOrWhiteSpace(document.CourseId) && !courseIds.Contains(document.CourseId))
                throw new InvalidDataException($"PDF '{document.FileName}' refers to a course that does not exist.");
            document.Pages ??= new ObservableCollection<PdfPageData>();
            if (document.Pages.Count != document.PageCount ||
                document.Pages.Select(x => x.PageNumber).Distinct().Count() != document.PageCount)
                throw new InvalidDataException($"PDF '{document.FileName}' has inconsistent page data.");
        }
    }

    private static void ValidatePomodoro(StudyData data)
    {
        var settings = data.PomodoroSettings;
        if (settings.FocusMinutes is < 1 or > 1440 ||
            settings.ShortBreakMinutes is < 1 or > 240 ||
            settings.LongBreakMinutes is < 1 or > 240 ||
            settings.SessionsBeforeLongBreak is < 1 or > 12)
            throw new InvalidDataException("Pomodoro settings are outside the supported range.");

        var sessions = data.StudySessions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var cycle in data.PomodoroCycles)
        {
            if (!sessions.ContainsKey(cycle.SessionId))
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' refers to a missing session.");
            if (cycle.CycleNumber < 1 || cycle.FocusStartedAt == default || cycle.FocusEndedAt < cycle.FocusStartedAt)
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' has invalid focus timestamps.");
            if (!Enum.IsDefined(cycle.BreakType) || cycle.BreakType == PomodoroPhase.Focus)
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' has an invalid break type.");
            var focusInterval = (int)Math.Floor((cycle.FocusEndedAt - cycle.FocusStartedAt).TotalSeconds);
            if (cycle.FocusSeconds is < 0 || cycle.FocusSeconds > focusInterval)
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' credits more focus time than its interval.");
            if (cycle.BreakStartedAt is { } breakStart && breakStart < cycle.FocusEndedAt)
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' starts its break before focus ends.");
            if (cycle.BreakEndedAt is { } breakEnd &&
                (cycle.BreakStartedAt is null || breakEnd < cycle.BreakStartedAt))
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' has an invalid break interval.");
            var breakInterval = cycle.BreakStartedAt is { } start && cycle.BreakEndedAt is { } end
                ? (int)Math.Floor((end - start).TotalSeconds)
                : 0;
            if (cycle.BreakSeconds is < 0 || cycle.BreakSeconds > breakInterval && cycle.BreakEndedAt is not null)
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' credits more break time than its interval.");
            if (cycle.IsCompleted && (cycle.BreakEndedAt is null || cycle.CompletedAt is null))
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' is marked completed without its break.");
            if (cycle.CompletedAt is { } completedAt && completedAt < (cycle.BreakEndedAt ?? cycle.FocusEndedAt))
                throw new InvalidDataException($"Pomodoro cycle '{cycle.Id}' was completed before its break ended.");
        }

        if (data.PomodoroTimer is not { } timer) return;
        if (string.IsNullOrWhiteSpace(timer.Id) || timer.Id != "current")
            throw new InvalidDataException("The active Pomodoro timer must use the stable id 'current'.");
        if (!sessions.TryGetValue(timer.SessionId, out var session))
            throw new InvalidDataException("The active Pomodoro timer refers to a missing session.");
        if (!Enum.IsDefined(timer.Phase) || !Enum.IsDefined(timer.Status))
            throw new InvalidDataException("The active Pomodoro timer has an invalid phase or status.");
        if (timer.PhaseWindowStartedAt == default || timer.PhaseStartedAt == default || timer.UpdatedAt == default)
            throw new InvalidDataException("The active Pomodoro timer has invalid timestamps.");
        if (timer.ActiveSeconds < 0 || timer.RemainingSeconds < 0)
            throw new InvalidDataException("The active Pomodoro timer has negative seconds.");
        if (timer.CycleNumber < 1 || timer.FocusesCompletedInSet < 0 ||
            timer.FocusesCompletedInSet > settings.SessionsBeforeLongBreak)
            throw new InvalidDataException("The active Pomodoro timer has invalid cycle counters.");
        if (timer.Status is PomodoroTimerStatus.Running or PomodoroTimerStatus.Paused && timer.RemainingSeconds == 0)
            throw new InvalidDataException("A running or paused Pomodoro phase must have remaining time.");
        if (timer.Status == PomodoroTimerStatus.Paused && timer.PhaseEndsAt is not null)
            throw new InvalidDataException("A paused Pomodoro phase cannot have an absolute end.");
        if (timer.Status == PomodoroTimerStatus.Running && timer.PhaseEndsAt is null)
            throw new InvalidDataException("A running Pomodoro phase needs an absolute end.");
        if (timer.Status == PomodoroTimerStatus.Completed && timer.PhaseEndsAt is null)
            throw new InvalidDataException("A completed Pomodoro phase needs an absolute end.");
        if (!string.IsNullOrWhiteSpace(timer.CurrentCycleId) &&
            !data.PomodoroCycles.Any(x => string.Equals(x.Id, timer.CurrentCycleId, StringComparison.Ordinal) &&
                                          string.Equals(x.SessionId, timer.SessionId, StringComparison.Ordinal)))
            throw new InvalidDataException("The active Pomodoro timer refers to a missing cycle.");

        var expectedState = timer.Status switch
        {
            PomodoroTimerStatus.Running when timer.Phase == PomodoroPhase.Focus => StudySessionState.Running,
            PomodoroTimerStatus.Running => StudySessionState.Paused,
            PomodoroTimerStatus.Paused => StudySessionState.Paused,
            _ => (StudySessionState?)null
        };
        if (expectedState is { } state && session.State != state)
            throw new InvalidDataException("The active Pomodoro timer and its focus session are out of sync.");
        if (timer.Status == PomodoroTimerStatus.Completed &&
            session.State is not (StudySessionState.Paused or StudySessionState.Completed))
            throw new InvalidDataException("A completed Pomodoro cycle must leave its focus session paused or completed.");
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
        db.CalendarEvents.AddRange(data.CalendarEvents.Select(x => new CalendarEventEntity
        {
            Id = x.Id,
            Title = x.Title,
            Category = x.Category,
            StartsAt = x.StartsAt,
            EndsAt = x.EndsAt,
            IsAllDay = x.IsAllDay,
            CourseId = x.CourseId,
            CourseName = x.CourseName,
            Location = x.Location,
            Notes = x.Notes,
            Source = x.Source,
            Verification = x.Verification,
            TimeZoneId = x.TimeZoneId,
            ExternalId = x.ExternalId,
            ExternalSequence = x.ExternalSequence,
            IsCancelled = x.IsCancelled,
            RecurrenceRule = x.RecurrenceRule,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }));
        db.PomodoroSettings.Add(new PomodoroSettingsEntity
        {
            Id = 1,
            FocusMinutes = data.PomodoroSettings.FocusMinutes,
            ShortBreakMinutes = data.PomodoroSettings.ShortBreakMinutes,
            LongBreakMinutes = data.PomodoroSettings.LongBreakMinutes,
            SessionsBeforeLongBreak = data.PomodoroSettings.SessionsBeforeLongBreak
        });
        db.StudySessions.AddRange(data.StudySessions.Select(x => new StudySessionEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            Topic = x.Topic,
            PlannedMinutes = x.PlannedMinutes,
            StartedAt = x.StartedAt,
            LastResumedAt = x.LastResumedAt,
            PausedAt = x.PausedAt,
            EndedAt = x.EndedAt,
            AccumulatedFocusSeconds = x.AccumulatedFocusSeconds,
            AccumulatedPauseSeconds = x.AccumulatedPauseSeconds,
            State = (int)x.State,
            Outcome = x.Outcome,
            Notes = x.Notes,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            Segments = x.Segments.Select(segment => new StudySessionSegmentEntity
            {
                Id = segment.Id,
                SessionId = x.Id,
                Type = (int)segment.Type,
                StartedAt = segment.StartedAt,
                EndedAt = segment.EndedAt
            }).ToList()
        }));
        if (data.PomodoroTimer is { } timer)
            db.PomodoroTimers.Add(new PomodoroTimerEntity
            {
                Id = timer.Id,
                SessionId = timer.SessionId,
                Phase = (int)timer.Phase,
                Status = (int)timer.Status,
                PhaseWindowStartedAt = timer.PhaseWindowStartedAt,
                PhaseStartedAt = timer.PhaseStartedAt,
                PhaseEndsAt = timer.PhaseEndsAt,
                RemainingSeconds = timer.RemainingSeconds,
                ActiveSeconds = timer.ActiveSeconds,
                FocusesCompletedInSet = timer.FocusesCompletedInSet,
                CycleNumber = timer.CycleNumber,
                CurrentCycleId = timer.CurrentCycleId,
                UpdatedAt = timer.UpdatedAt
            });
        db.PomodoroCycles.AddRange(data.PomodoroCycles.Select(x => new PomodoroCycleEntity
        {
            Id = x.Id,
            SessionId = x.SessionId,
            CycleNumber = x.CycleNumber,
            FocusStartedAt = x.FocusStartedAt,
            FocusEndedAt = x.FocusEndedAt,
            FocusSeconds = x.FocusSeconds,
            BreakType = (int)x.BreakType,
            BreakStartedAt = x.BreakStartedAt,
            BreakEndedAt = x.BreakEndedAt,
            BreakSeconds = x.BreakSeconds,
            WasSkipped = x.WasSkipped,
            IsCompleted = x.IsCompleted,
            CompletedAt = x.CompletedAt
        }));
        db.StudyTasks.AddRange(data.StudyTasks.Select(x => new StudyTaskEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            Title = x.Title,
            Description = x.Description,
            DueAt = x.DueAt,
            EstimatedMinutes = x.EstimatedMinutes,
            Priority = x.Priority,
            Status = (int)x.Status,
            CompletedAt = x.CompletedAt,
            Source = x.Source,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }));
        db.StudyMissions.AddRange(data.StudyMissions.Select(x => new StudyMissionEntity
        {
            Id = x.Id,
            Title = x.Title,
            Description = x.Description,
            CourseId = x.CourseId,
            SyllabusTopicId = x.SyllabusTopicId,
            Period = (int)x.Period,
            StartsAt = x.StartsAt,
            DueAt = x.DueAt,
            Difficulty = x.Difficulty,
            Status = (int)x.Status,
            Reward = x.Reward,
            Source = x.Source,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            CompletedAt = x.CompletedAt,
            Steps = x.Steps.Select(step => new MissionStepEntity
            {
                Id = step.Id,
                MissionId = x.Id,
                Title = step.Title,
                IsCompleted = step.IsCompleted,
                Position = step.Position,
                CompletedAt = step.CompletedAt
            }).ToList()
        }));
        db.StudyActivities.AddRange(data.StudyActivities.Select(x => new StudyActivityEntity
        {
            Id = x.Id,
            Type = (int)x.Type,
            Title = x.Title,
            CourseId = x.CourseId,
            SyllabusTopicId = x.SyllabusTopicId,
            TaskId = x.TaskId,
            MissionId = x.MissionId,
            ExamId = x.ExamId,
            SessionId = x.SessionId,
            CalendarEventId = x.CalendarEventId,
            StartedAt = x.StartedAt,
            EndedAt = x.EndedAt,
            TimeZoneId = x.TimeZoneId,
            EffectiveSeconds = x.EffectiveSeconds,
            Origin = (int)x.Origin,
            Status = (int)x.Status,
            Result = x.Result,
            FocusRating = x.FocusRating,
            DifficultyRating = x.DifficultyRating,
            QualityRating = x.QualityRating,
            EnergyRating = x.EnergyRating,
            Note = x.Note,
            TagsJson = JsonSerializer.Serialize(x.Tags),
            IsPrivate = x.IsPrivate,
            Revision = x.Revision,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            DeletedAt = x.DeletedAt
        }));
        db.ActivityRevisions.AddRange(data.ActivityRevisions.Select(x => new ActivityRevisionEntity
        {
            Id = x.Id,
            ActivityId = x.ActivityId,
            Action = (int)x.Action,
            Reason = x.Reason,
            BeforeJson = x.BeforeJson,
            AfterJson = x.AfterJson,
            RevisedAt = x.RevisedAt
        }));
        db.ErrorJournalEntries.AddRange(data.ErrorJournalEntries.Select(x => new ErrorJournalEntryEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            SyllabusTopicId = x.SyllabusTopicId,
            Topic = x.Topic,
            Question = x.Question,
            IncorrectAnswer = x.IncorrectAnswer,
            CorrectAnswer = x.CorrectAnswer,
            ErrorType = x.ErrorType,
            RootCause = x.RootCause,
            NextReviewAt = x.NextReviewAt,
            ReviewCount = x.ReviewCount,
            Status = (int)x.Status,
            Source = x.Source,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }));
        db.Flashcards.AddRange(data.Flashcards.Select(x => new FlashcardEntity
        {
            Id = x.Id,
            CourseId = x.CourseId,
            SyllabusTopicId = x.SyllabusTopicId,
            Front = x.Front,
            Back = x.Back,
            Source = x.Source,
            EaseFactor = x.EaseFactor,
            IntervalDays = x.IntervalDays,
            Repetitions = x.Repetitions,
            Lapses = x.Lapses,
            DueAt = x.DueAt,
            LastReviewedAt = x.LastReviewedAt,
            IsSuspended = x.IsSuspended,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }));
        db.FlashcardReviews.AddRange(data.FlashcardReviews.Select(x => new FlashcardReviewEntity
        {
            Id = x.Id,
            FlashcardId = x.FlashcardId,
            ReviewedAt = x.ReviewedAt,
            Rating = (int)x.Rating,
            PreviousIntervalDays = x.PreviousIntervalDays,
            NewIntervalDays = x.NewIntervalDays,
            EaseFactorAfter = x.EaseFactorAfter
        }));
        db.Distractions.AddRange(data.Distractions.Select(x => new DistractionEntity
        {
            Id = x.Id,
            SessionId = x.SessionId,
            Text = x.Text,
            CreatedAt = x.CreatedAt,
            ResolvedAt = x.ResolvedAt
        }));
        db.EnergyCheckIns.AddRange(data.EnergyCheckIns.Select(x => new EnergyCheckInEntity
        {
            Id = x.Id,
            RecordedAt = x.RecordedAt,
            EnergyLevel = x.EnergyLevel,
            FocusLevel = x.FocusLevel,
            Note = x.Note
        }));
        db.PdfDocuments.AddRange(data.PdfDocuments.Select(x => new PdfDocumentEntity
        {
            Id = x.Id,
            FileName = x.FileName,
            Sha256 = x.Sha256,
            PageCount = x.PageCount,
            ImportedAt = x.ImportedAt,
            CourseId = x.CourseId,
            PagesJson = JsonSerializer.Serialize(x.Pages)
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
        foreach (var x in await db.CalendarEvents.AsNoTracking().ToListAsync(cancellationToken))
            result.CalendarEvents.Add(new CalendarEvent
            {
                Id = x.Id,
                Title = x.Title,
                Category = x.Category,
                StartsAt = x.StartsAt,
                EndsAt = x.EndsAt,
                IsAllDay = x.IsAllDay,
                CourseId = x.CourseId,
                CourseName = x.CourseName,
                Location = x.Location,
                Notes = x.Notes,
                Source = x.Source,
                Verification = x.Verification,
                TimeZoneId = x.TimeZoneId,
                ExternalId = x.ExternalId,
                ExternalSequence = x.ExternalSequence,
                IsCancelled = x.IsCancelled,
                RecurrenceRule = x.RecurrenceRule,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        var pomodoroSettings = await db.PomodoroSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        result.PomodoroSettings = pomodoroSettings is null
            ? new PomodoroSettingsData()
            : new PomodoroSettingsData
            {
                FocusMinutes = pomodoroSettings.FocusMinutes,
                ShortBreakMinutes = pomodoroSettings.ShortBreakMinutes,
                LongBreakMinutes = pomodoroSettings.LongBreakMinutes,
                SessionsBeforeLongBreak = pomodoroSettings.SessionsBeforeLongBreak
            };
        var pomodoroTimer = await db.PomodoroTimers.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (pomodoroTimer is not null)
            result.PomodoroTimer = new PomodoroTimerData
            {
                Id = pomodoroTimer.Id,
                SessionId = pomodoroTimer.SessionId,
                Phase = (PomodoroPhase)pomodoroTimer.Phase,
                Status = (PomodoroTimerStatus)pomodoroTimer.Status,
                PhaseWindowStartedAt = pomodoroTimer.PhaseWindowStartedAt,
                PhaseStartedAt = pomodoroTimer.PhaseStartedAt,
                PhaseEndsAt = pomodoroTimer.PhaseEndsAt,
                RemainingSeconds = pomodoroTimer.RemainingSeconds,
                ActiveSeconds = pomodoroTimer.ActiveSeconds,
                FocusesCompletedInSet = pomodoroTimer.FocusesCompletedInSet,
                CycleNumber = pomodoroTimer.CycleNumber,
                CurrentCycleId = pomodoroTimer.CurrentCycleId,
                UpdatedAt = pomodoroTimer.UpdatedAt
            };
        foreach (var x in await db.PomodoroCycles.AsNoTracking().ToListAsync(cancellationToken))
            result.PomodoroCycles.Add(new PomodoroCycleData
            {
                Id = x.Id,
                SessionId = x.SessionId,
                CycleNumber = x.CycleNumber,
                FocusStartedAt = x.FocusStartedAt,
                FocusEndedAt = x.FocusEndedAt,
                FocusSeconds = x.FocusSeconds,
                BreakType = (PomodoroPhase)x.BreakType,
                BreakStartedAt = x.BreakStartedAt,
                BreakEndedAt = x.BreakEndedAt,
                BreakSeconds = x.BreakSeconds,
                WasSkipped = x.WasSkipped,
                IsCompleted = x.IsCompleted,
                CompletedAt = x.CompletedAt
            });
        foreach (var x in await db.StudySessions.Include(x => x.Segments).AsNoTracking().ToListAsync(cancellationToken))
            result.StudySessions.Add(new StudySessionData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                Topic = x.Topic,
                PlannedMinutes = x.PlannedMinutes,
                StartedAt = x.StartedAt,
                LastResumedAt = x.LastResumedAt,
                PausedAt = x.PausedAt,
                EndedAt = x.EndedAt,
                AccumulatedFocusSeconds = x.AccumulatedFocusSeconds,
                AccumulatedPauseSeconds = x.AccumulatedPauseSeconds,
                State = (StudySessionState)x.State,
                Outcome = x.Outcome,
                Notes = x.Notes,
                Segments = new ObservableCollection<StudySessionSegmentData>(x.Segments
                    .OrderBy(segment => segment.StartedAt)
                    .Select(segment => new StudySessionSegmentData
                    {
                        Id = segment.Id,
                        Type = (StudySessionSegmentType)segment.Type,
                        StartedAt = segment.StartedAt,
                        EndedAt = segment.EndedAt
                    })),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        foreach (var x in await db.StudyTasks.AsNoTracking().ToListAsync(cancellationToken))
            result.StudyTasks.Add(new StudyTaskData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                Title = x.Title,
                Description = x.Description,
                DueAt = x.DueAt,
                EstimatedMinutes = x.EstimatedMinutes,
                Priority = x.Priority,
                Status = (StudyTaskStatus)x.Status,
                CompletedAt = x.CompletedAt,
                Source = x.Source,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        foreach (var x in await db.StudyMissions.Include(x => x.Steps).AsNoTracking().ToListAsync(cancellationToken))
            result.StudyMissions.Add(new StudyMissionData
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                CourseId = x.CourseId,
                SyllabusTopicId = x.SyllabusTopicId,
                Period = (StudyMissionPeriod)x.Period,
                StartsAt = x.StartsAt,
                DueAt = x.DueAt,
                Difficulty = x.Difficulty,
                Status = (StudyMissionStatus)x.Status,
                Reward = x.Reward,
                Source = x.Source,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                CompletedAt = x.CompletedAt,
                Steps = new ObservableCollection<MissionStepData>(x.Steps
                    .OrderBy(step => step.Position)
                    .Select(step => new MissionStepData
                    {
                        Id = step.Id,
                        Title = step.Title,
                        IsCompleted = step.IsCompleted,
                        Position = step.Position,
                        CompletedAt = step.CompletedAt
                    }))
            });
        foreach (var x in await db.StudyActivities.AsNoTracking().ToListAsync(cancellationToken))
        {
            ObservableCollection<string> tags;
            try
            {
                tags = JsonSerializer.Deserialize<ObservableCollection<string>>(x.TagsJson, JsonOptions)
                    ?? new ObservableCollection<string>();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Study activity '{x.Id}' has invalid tag data.", ex);
            }
            result.StudyActivities.Add(new StudyActivityData
            {
                Id = x.Id,
                Type = (StudyActivityType)x.Type,
                Title = x.Title,
                CourseId = x.CourseId,
                SyllabusTopicId = x.SyllabusTopicId,
                TaskId = x.TaskId,
                MissionId = x.MissionId,
                ExamId = x.ExamId,
                SessionId = x.SessionId,
                CalendarEventId = x.CalendarEventId,
                StartedAt = x.StartedAt,
                EndedAt = x.EndedAt,
                TimeZoneId = x.TimeZoneId,
                EffectiveSeconds = x.EffectiveSeconds,
                Origin = (StudyActivityOrigin)x.Origin,
                Status = (StudyActivityStatus)x.Status,
                Result = x.Result,
                FocusRating = x.FocusRating,
                DifficultyRating = x.DifficultyRating,
                QualityRating = x.QualityRating,
                EnergyRating = x.EnergyRating,
                Note = x.Note,
                Tags = tags,
                IsPrivate = x.IsPrivate,
                Revision = x.Revision,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                DeletedAt = x.DeletedAt
            });
        }
        foreach (var x in await db.ActivityRevisions.AsNoTracking().ToListAsync(cancellationToken))
            result.ActivityRevisions.Add(new ActivityRevisionData
            {
                Id = x.Id,
                ActivityId = x.ActivityId,
                Action = (ActivityRevisionAction)x.Action,
                Reason = x.Reason,
                BeforeJson = x.BeforeJson,
                AfterJson = x.AfterJson,
                RevisedAt = x.RevisedAt
            });
        foreach (var x in await db.ErrorJournalEntries.AsNoTracking().ToListAsync(cancellationToken))
            result.ErrorJournalEntries.Add(new ErrorJournalEntryData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                SyllabusTopicId = x.SyllabusTopicId,
                Topic = x.Topic,
                Question = x.Question,
                IncorrectAnswer = x.IncorrectAnswer,
                CorrectAnswer = x.CorrectAnswer,
                ErrorType = x.ErrorType,
                RootCause = x.RootCause,
                NextReviewAt = x.NextReviewAt,
                ReviewCount = x.ReviewCount,
                Status = (ErrorReviewStatus)x.Status,
                Source = x.Source,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        foreach (var x in await db.Flashcards.AsNoTracking().ToListAsync(cancellationToken))
            result.Flashcards.Add(new FlashcardData
            {
                Id = x.Id,
                CourseId = x.CourseId,
                SyllabusTopicId = x.SyllabusTopicId,
                Front = x.Front,
                Back = x.Back,
                Source = x.Source,
                EaseFactor = x.EaseFactor,
                IntervalDays = x.IntervalDays,
                Repetitions = x.Repetitions,
                Lapses = x.Lapses,
                DueAt = x.DueAt,
                LastReviewedAt = x.LastReviewedAt,
                IsSuspended = x.IsSuspended,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        foreach (var x in await db.FlashcardReviews.AsNoTracking().ToListAsync(cancellationToken))
            result.FlashcardReviews.Add(new FlashcardReviewData
            {
                Id = x.Id,
                FlashcardId = x.FlashcardId,
                ReviewedAt = x.ReviewedAt,
                Rating = (RecallRating)x.Rating,
                PreviousIntervalDays = x.PreviousIntervalDays,
                NewIntervalDays = x.NewIntervalDays,
                EaseFactorAfter = x.EaseFactorAfter
            });
        foreach (var x in await db.Distractions.AsNoTracking().ToListAsync(cancellationToken))
            result.Distractions.Add(new DistractionData
            {
                Id = x.Id,
                SessionId = x.SessionId,
                Text = x.Text,
                CreatedAt = x.CreatedAt,
                ResolvedAt = x.ResolvedAt
            });
        foreach (var x in await db.EnergyCheckIns.AsNoTracking().ToListAsync(cancellationToken))
            result.EnergyCheckIns.Add(new EnergyCheckInData
            {
                Id = x.Id,
                RecordedAt = x.RecordedAt,
                EnergyLevel = x.EnergyLevel,
                FocusLevel = x.FocusLevel,
                Note = x.Note
            });
        foreach (var x in await db.PdfDocuments.AsNoTracking().ToListAsync(cancellationToken))
        {
            ObservableCollection<PdfPageData> pages;
            try
            {
                pages = JsonSerializer.Deserialize<ObservableCollection<PdfPageData>>(x.PagesJson, JsonOptions)
                    ?? new ObservableCollection<PdfPageData>();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"PDF '{x.FileName}' has invalid page data.", ex);
            }
            result.PdfDocuments.Add(new PdfDocumentData
            {
                Id = x.Id,
                FileName = x.FileName,
                Sha256 = x.Sha256,
                PageCount = x.PageCount,
                ImportedAt = x.ImportedAt,
                CourseId = x.CourseId,
                Pages = pages
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
