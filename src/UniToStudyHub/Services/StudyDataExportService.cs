using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public enum StudyExportFormat
{
    Csv,
    Xlsx,
    Json
}

public sealed record StudyExportRequest(
    DateOnly From,
    DateOnly To,
    string TimeZoneId = "",
    IReadOnlyCollection<string>? CourseIds = null,
    IReadOnlyCollection<StudyActivityType>? ActivityTypes = null,
    bool IncludePomodoroCycles = true,
    bool IncludePrivateNotes = false,
    bool IncludeNotes = true,
    bool IncludeRatings = true,
    bool IncludeTags = true,
    bool IncludeResult = true,
    bool IncludeDeleted = false,
    bool IncludeStudyLabRecords = false);

public sealed record StudyExportFile(string FileName, string ContentType, byte[] Content);

public sealed record StudyExportBundle(
    StudyExportFormat Format,
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<StudyExportFile> Files);

/// <summary>
/// Builds deterministic exports from the persisted StudyData. Exports are data
/// selections, not a second database: the caller writes the returned bytes.
/// Private notes are excluded by default; configured secrets and syllabus URLs are never selected.
/// </summary>
public sealed class StudyDataExportService
{
    public const string SchemaVersion = "1.1";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JsonContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StudyExportBundle Export(
        StudyData data,
        StudyExportRequest request,
        StudyExportFormat format,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var generatedAt = now ?? DateTimeOffset.Now;
        var selection = Select(data, request, generatedAt);
        return format switch
        {
            StudyExportFormat.Csv => new StudyExportBundle(format, SchemaVersion, generatedAt, BuildCsv(selection, request)),
            StudyExportFormat.Xlsx => new StudyExportBundle(format, SchemaVersion, generatedAt,
                new[] { BuildXlsx(selection, request, generatedAt) }),
            StudyExportFormat.Json => new StudyExportBundle(format, SchemaVersion, generatedAt,
                new[] { BuildJson(selection, request, generatedAt) }),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    /// <summary>
    /// Explicit full-study-data export. The ordinary Export method remains the
    /// compact progress report used by the existing WPF report button.
    /// </summary>
    public StudyExportBundle ExportStudyData(
        StudyData data,
        StudyExportRequest request,
        StudyExportFormat format,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Export(data, request with { IncludeStudyLabRecords = true }, format, now);
    }

    private static ExportSelection Select(StudyData data, StudyExportRequest request, DateTimeOffset now)
    {
        if (request.To < request.From)
            throw new ArgumentOutOfRangeException(nameof(request), "The end date must not precede the start date.");
        if (request.To.DayNumber - request.From.DayNumber > 3660)
            throw new ArgumentOutOfRangeException(nameof(request), "The export range is limited to 3661 days.");

        var zone = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? TimeZoneInfo.Local
            : ResolveTimeZone(request.TimeZoneId);
        var rangeStart = StartOfDay(request.From, zone);
        var rangeEnd = StartOfDay(request.To.AddDays(1), zone);
        var courses = request.CourseIds is { Count: > 0 }
            ? request.CourseIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal)
            : null;
        var types = request.ActivityTypes?.Count > 0 ? request.ActivityTypes.ToHashSet() : null;

        var activities = data.StudyActivities
            .Where(x => request.IncludeDeleted || !x.IsDeleted)
            .Where(x => x.StartedAt < rangeEnd && (x.EndedAt ?? now) > rangeStart)
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => types is null || types.Contains(x.Type))
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var sessions = data.StudySessions
            .Where(x => x.StartedAt < rangeEnd && (x.EndedAt ?? now) > rangeStart)
            .Where(x => CourseMatches(x.CourseId, courses))
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var sessionIds = sessions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var activity in activities)
        {
            if (string.IsNullOrWhiteSpace(activity.SessionId) || sessionIds.Contains(activity.SessionId))
                continue;

            var linked = data.StudySessions.FirstOrDefault(x => x.Id == activity.SessionId);
            if (linked is null)
                continue;

            // A linked record must still satisfy the export's date and course
            // filters. Otherwise a malformed cross-course or out-of-range link
            // could disclose a session's notes and segments in a scoped export.
            var overlapsRange = linked.StartedAt < rangeEnd && (linked.EndedAt ?? now) > rangeStart;
            if (!overlapsRange || !CourseMatches(linked.CourseId, courses))
                continue;

            sessionIds.Add(linked.Id);
            sessions.Add(linked);
        }
        sessions = sessions.OrderBy(x => x.StartedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();

        var cycles = request.IncludePomodoroCycles
            ? data.PomodoroCycles
                .Where(x => x.FocusStartedAt < rangeEnd && (x.CompletedAt ?? x.FocusEndedAt) > rangeStart)
                .Where(x => courses is null ||
                            data.StudySessions.FirstOrDefault(session => session.Id == x.SessionId) is { } session &&
                            CourseMatches(session.CourseId, courses))
                .OrderBy(x => x.FocusStartedAt)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList()
            : new List<PomodoroCycleData>();

        if (!request.IncludeStudyLabRecords)
        {
            var progressData = new StudyData
            {
                Courses = data.Courses,
                StudyActivities = new ObservableCollection<StudyActivityData>(activities),
                StudySessions = new ObservableCollection<StudySessionData>(sessions)
            };
            var progressSummary = new ActivityLedgerService().Summarize(
                progressData, request.From, request.To, now, zone.Id);
            return new ExportSelection(activities, sessions, cycles, progressSummary, zone, request, now);
        }

        // A record belongs to the export window when one of its meaningful
        // lifecycle dates (created, due, completed, reviewed or updated) falls
        // inside the selected local-calendar range.
        var tasks = data.StudyTasks
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InRange(x.CreatedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone) ||
                        InRange(x.DueAt, request.From, request.To, zone) ||
                        InRange(x.CompletedAt, request.From, request.To, zone))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var missions = data.StudyMissions
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InRange(x.StartsAt, request.From, request.To, zone) ||
                        InRange(x.CreatedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone) ||
                        InRange(x.DueAt, request.From, request.To, zone) ||
                        InRange(x.CompletedAt, request.From, request.To, zone))
            .OrderBy(x => x.StartsAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var flashcards = data.Flashcards
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InRange(x.CreatedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone) ||
                        InRange(x.DueAt, request.From, request.To, zone) ||
                        InRange(x.LastReviewedAt, request.From, request.To, zone))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var flashcardsById = flashcards
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var flashcardReviews = data.FlashcardReviews
            .Where(x => InRange(x.ReviewedAt, request.From, request.To, zone))
            .Where(x => flashcardsById.TryGetValue(x.FlashcardId, out var card) &&
                        CourseMatches(card.CourseId, courses))
            .OrderBy(x => x.ReviewedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var errors = data.ErrorJournalEntries
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InRange(x.CreatedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone) ||
                        InRange(x.NextReviewAt, request.From, request.To, zone))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        // Energy check-ins are global and have no course relationship. They are
        // deliberately omitted from a course-scoped export rather than implying
        // that a personal check-in belongs to the selected course.
        var energyCheckIns = courses is null
            ? data.EnergyCheckIns
                .Where(x => InRange(x.RecordedAt, request.From, request.To, zone))
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList()
            : new List<EnergyCheckInData>();

        var syllabusTopics = data.SyllabusTopics
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => (!x.LastStudiedAt.HasValue && !x.UpdatedAt.HasValue) ||
                        InRange(x.LastStudiedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone))
            .OrderBy(x => x.CourseId, StringComparer.Ordinal)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var questions = data.Questions
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InRange(x.CreatedAt, request.From, request.To, zone) ||
                        InRange(x.UpdatedAt, request.From, request.To, zone))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var grades = data.Grades
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InDateRange(x.Date, request.From, request.To))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var careerAttempts = data.CareerExams
            .Where(x => CourseMatches(x.CourseId, courses))
            .SelectMany(exam => exam.Attempts
                .Where(attempt => InRange(attempt.AttemptedAt, request.From, request.To, zone) ||
                                  InRange(attempt.RecordedAt, request.From, request.To, zone))
                .Select(attempt => new CareerAttemptRow(
                    exam.Id, exam.CourseId, exam.Name, exam.Cfu, exam.CountsTowardAverage, attempt)))
            .OrderBy(x => x.Attempt.AttemptedAt)
            .ThenBy(x => x.Attempt.Id, StringComparer.Ordinal)
            .ToList();

        var exams = data.Exams
            .Where(x => CourseMatches(x.CourseId, courses))
            .Where(x => InDateRange(x.PlannedDate, request.From, request.To))
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        // Course rows are reference data for the selected records, so they are
        // retained for the chosen courses even if the course itself was not
        // edited during the reporting window.
        var selectedCourses = data.Courses
            .Where(x => CourseMatches(x.Id, courses))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var scopedData = new StudyData
        {
            Courses = new ObservableCollection<Course>(selectedCourses),
            StudyActivities = new ObservableCollection<StudyActivityData>(activities),
            StudySessions = new ObservableCollection<StudySessionData>(sessions)
        };
        var summary = new ActivityLedgerService().Summarize(scopedData, request.From, request.To, now, zone.Id);
        return new ExportSelection(activities, sessions, cycles, summary, zone, request, now)
        {
            Tasks = tasks,
            Missions = missions,
            Flashcards = flashcards,
            FlashcardReviews = flashcardReviews,
            Errors = errors,
            EnergyCheckIns = energyCheckIns,
            SyllabusTopics = syllabusTopics,
            Questions = questions,
            Grades = grades,
            CareerAttempts = careerAttempts,
            Exams = exams,
            Courses = selectedCourses
        };
    }

    private static bool CourseMatches(string? courseId, HashSet<string>? courses) =>
        courses is null ||
        (!string.IsNullOrWhiteSpace(courseId) && courses.Contains(courseId));

    private static bool InRange(DateTimeOffset value, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);
        return day >= from && day <= to;
    }

    private static bool InRange(DateTimeOffset? value, DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        value is { } instant && InRange(instant, from, to, zone);

    private static bool InRange(DateTime value, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var local = value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTime(new DateTimeOffset(value, TimeSpan.Zero), zone).DateTime,
            DateTimeKind.Local => TimeZoneInfo.ConvertTime(new DateTimeOffset(value), zone).DateTime,
            _ => value
        };
        var day = DateOnly.FromDateTime(local);
        return day >= from && day <= to;
    }

    private static bool InRange(DateTime? value, DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        value is { } date && InRange(date, from, to, zone);

    private static bool InDateRange(DateTime? value, DateOnly from, DateOnly to) =>
        value is { } date && InDateRange(date, from, to);

    private static bool InDateRange(DateTime value, DateOnly from, DateOnly to)
    {
        var day = DateOnly.FromDateTime(value);
        return day >= from && day <= to;
    }

    private static IReadOnlyList<StudyExportFile> BuildCsv(ExportSelection selection, StudyExportRequest request)
    {
        var files = new List<StudyExportFile>
        {
            new("activities.csv", CsvContentType, CsvBytes(ActivityTable(selection, request))),
            new("sessions.csv", CsvContentType, CsvBytes(SessionTable(selection, request))),
            new("segments.csv", CsvContentType, CsvBytes(SegmentTable(selection))),
            new("summary.csv", CsvContentType, CsvBytes(SummaryTable(selection)))
        };
        if (request.IncludeStudyLabRecords)
        {
            files.AddRange(new[]
            {
                new StudyExportFile("tasks.csv", CsvContentType, CsvBytes(TaskTable(selection, request))),
                new StudyExportFile("missions.csv", CsvContentType, CsvBytes(MissionTable(selection, request))),
                new StudyExportFile("mission-steps.csv", CsvContentType, CsvBytes(MissionStepTable(selection, request))),
                new StudyExportFile("flashcards.csv", CsvContentType, CsvBytes(FlashcardTable(selection, request))),
                new StudyExportFile("flashcard-reviews.csv", CsvContentType, CsvBytes(FlashcardReviewTable(selection))),
                new StudyExportFile("errors.csv", CsvContentType, CsvBytes(ErrorTable(selection, request))),
                new StudyExportFile("energy-checkins.csv", CsvContentType, CsvBytes(EnergyCheckInTable(selection, request))),
                new StudyExportFile("syllabus.csv", CsvContentType, CsvBytes(SyllabusTable(selection, request))),
                new StudyExportFile("questions.csv", CsvContentType, CsvBytes(QuestionTable(selection, request))),
                new StudyExportFile("grades.csv", CsvContentType, CsvBytes(GradeTable(selection, request))),
                new StudyExportFile("career-attempts.csv", CsvContentType, CsvBytes(CareerAttemptTable(selection, request))),
                new StudyExportFile("exams.csv", CsvContentType, CsvBytes(ExamTable(selection))),
                new StudyExportFile("courses.csv", CsvContentType, CsvBytes(CourseTable(selection, request)))
            });
        }
        if (request.IncludePomodoroCycles)
            files.Add(new("pomodoro-cycles.csv", CsvContentType, CsvBytes(PomodoroTable(selection))));
        return files;
    }

    private static StudyExportFile BuildJson(ExportSelection selection, StudyExportRequest request, DateTimeOffset generatedAt)
    {
        var document = new
        {
            schemaVersion = SchemaVersion,
            generatedAt,
            filters = FilterDocument(request),
            summary = new
            {
                recordedSeconds = selection.Summary.RecordedSeconds,
                overlapSeconds = selection.Summary.OverlapSeconds,
                activityCount = selection.Summary.ActivityCount,
                byType = selection.Summary.ByType.ToDictionary(x => x.Key.ToString(), x => x.Value),
                byCourse = selection.Summary.ByCourse,
                byTopic = selection.Summary.ByTopic
            },
            activities = selection.Activities.Select(x => new
            {
                x.Id,
                type = x.Type.ToString(),
                x.Title,
                x.CourseId,
                x.SyllabusTopicId,
                x.TaskId,
                x.MissionId,
                x.SessionId,
                x.StartedAt,
                x.EndedAt,
                x.TimeZoneId,
                x.EffectiveSeconds,
                origin = x.Origin.ToString(),
                status = x.Status.ToString(),
                result = request.IncludeResult ? x.Result : null,
                ratings = request.IncludeRatings
                    ? new { x.FocusRating, x.DifficultyRating, x.QualityRating, x.EnergyRating }
                    : null,
                note = NoteForExport(x, request),
                tags = request.IncludeTags ? x.Tags : null,
                x.IsPrivate,
                x.Revision,
                x.DeletedAt
            }),
            sessions = selection.Sessions.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.Topic,
                x.PlannedMinutes,
                x.StartedAt,
                x.EndedAt,
                state = x.State.ToString(),
                x.AccumulatedFocusSeconds,
                x.AccumulatedPauseSeconds,
                outcome = request.IncludeResult ? x.Outcome : null,
                notes = SessionNotesForExport(x, request),
                notesArePrivate = x.NotesArePrivate,
                segments = selection.Segments
                    .Where(s => s.SessionId == x.Id)
                    .Select(s => new
                    {
                        s.Segment.Id,
                        type = s.Segment.Type.ToString(),
                        s.Segment.StartedAt,
                        s.Segment.EndedAt,
                        durationSeconds = s.DurationSeconds
                    })
            }),
            segments = selection.Segments.Select(x => new
            {
                x.SessionId,
                x.Segment.Id,
                type = x.Segment.Type.ToString(),
                x.Segment.StartedAt,
                x.Segment.EndedAt,
                x.DurationSeconds
            }),
            pomodoroCycles = request.IncludePomodoroCycles
                ? selection.Cycles.Select(x => new
                {
                    x.Id,
                    x.SessionId,
                    x.CycleNumber,
                    x.FocusStartedAt,
                    x.FocusEndedAt,
                    x.FocusSeconds,
                    breakType = x.BreakType.ToString(),
                    x.BreakStartedAt,
                    x.BreakEndedAt,
                    x.BreakSeconds,
                    x.WasSkipped,
                    x.IsCompleted,
                    x.CompletedAt
                })
                : null,
            tasks = request.IncludeStudyLabRecords ? selection.Tasks.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.Title,
                description = request.IncludeNotes ? x.Description : null,
                x.DueAt,
                x.EstimatedMinutes,
                x.Priority,
                status = x.Status.ToString(),
                x.CompletedAt,
                x.Source,
                x.CreatedAt,
                x.UpdatedAt
            }) : null,
            missions = request.IncludeStudyLabRecords ? selection.Missions.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.SyllabusTopicId,
                x.Title,
                description = request.IncludeNotes ? x.Description : null,
                period = x.Period.ToString(),
                x.StartsAt,
                x.DueAt,
                x.Difficulty,
                status = x.Status.ToString(),
                reward = request.IncludeNotes ? x.Reward : null,
                x.CompletedAt,
                completedStepCount = x.CompletedStepCount,
                totalStepCount = x.Steps.Count,
                steps = x.Steps.Select(step => new
                {
                    step.Id,
                    title = request.IncludeNotes ? step.Title : null,
                    step.IsCompleted,
                    step.Position,
                    step.CompletedAt
                }),
                x.CreatedAt,
                x.UpdatedAt
            }) : null,
            flashcards = request.IncludeStudyLabRecords ? selection.Flashcards.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.SyllabusTopicId,
                front = request.IncludeNotes ? x.Front : null,
                back = request.IncludeNotes ? x.Back : null,
                x.Source,
                x.EaseFactor,
                x.IntervalDays,
                x.Repetitions,
                x.Lapses,
                x.DueAt,
                x.LastReviewedAt,
                x.IsSuspended,
                x.CreatedAt,
                x.UpdatedAt
            }) : null,
            flashcardReviews = request.IncludeStudyLabRecords ? selection.FlashcardReviews.Select(x => new
            {
                x.Id,
                x.FlashcardId,
                x.ReviewedAt,
                rating = x.Rating.ToString(),
                x.PreviousIntervalDays,
                x.NewIntervalDays,
                x.EaseFactorAfter
            }) : null,
            errors = request.IncludeStudyLabRecords ? selection.Errors.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.SyllabusTopicId,
                x.Topic,
                question = request.IncludeNotes ? x.Question : null,
                incorrectAnswer = request.IncludeNotes && request.IncludeResult ? x.IncorrectAnswer : null,
                correctAnswer = request.IncludeNotes && request.IncludeResult ? x.CorrectAnswer : null,
                x.ErrorType,
                rootCause = request.IncludeNotes ? x.RootCause : null,
                x.NextReviewAt,
                x.ReviewCount,
                status = x.Status.ToString(),
                x.Source,
                x.CreatedAt,
                x.UpdatedAt
            }) : null,
            energyCheckIns = request.IncludeStudyLabRecords ? selection.EnergyCheckIns.Select(x => new
            {
                x.Id,
                x.RecordedAt,
                x.EnergyLevel,
                x.FocusLevel,
                note = request.IncludeNotes && request.IncludePrivateNotes ? x.Note : null
            }) : null,
            syllabusTopics = request.IncludeStudyLabRecords ? selection.SyllabusTopics.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.Title,
                description = request.IncludeNotes ? x.Description : null,
                status = x.Status.ToString(),
                x.EstimatedMinutes,
                x.LastStudiedAt,
                x.UpdatedAt
            }) : null,
            questions = request.IncludeStudyLabRecords ? selection.Questions.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.SyllabusTopicId,
                text = request.IncludeNotes ? x.Text : null,
                type = x.Type.ToString(),
                x.Difficulty,
                options = request.IncludeNotes ? x.Options : null,
                correctAnswer = request.IncludeNotes && request.IncludeResult ? x.CorrectAnswer : null,
                explanation = request.IncludeNotes ? x.Explanation : null,
                x.Source,
                x.IsOfficial,
                x.CreatedAt,
                x.UpdatedAt
            }) : null,
            grades = request.IncludeStudyLabRecords ? selection.Grades.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.CourseName,
                value = request.IncludeResult ? x.Value : (int?)null,
                honors = request.IncludeResult ? x.Honors : (bool?)null,
                x.Cfu,
                date = DateOnly.FromDateTime(x.Date),
                x.CountsTowardAverage
            }) : null,
            careerAttempts = request.IncludeStudyLabRecords ? selection.CareerAttempts.Select(x => new
            {
                x.ExamId,
                x.CourseId,
                x.ExamName,
                x.Cfu,
                x.CountsTowardAverage,
                x.Attempt.Id,
                x.Attempt.AttemptedAt,
                status = x.Attempt.Status,
                outcome = request.IncludeResult ? x.Attempt.Outcome : null,
                gradeDecision = request.IncludeResult ? x.Attempt.GradeDecision : null,
                numericGrade = request.IncludeResult ? x.Attempt.NumericGrade : null,
                isHonors = request.IncludeResult ? x.Attempt.IsHonors : (bool?)null,
                x.Attempt.IsPassFail,
                x.Attempt.RecordedAt
            }) : null,
            exams = request.IncludeStudyLabRecords ? selection.Exams.Select(x => new
            {
                x.Id,
                x.CourseId,
                x.CourseName,
                x.Name,
                x.Cfu,
                x.EstimatedStudyHours,
                x.PlannedDate,
                x.PlannedSession,
                x.Completed
            }) : null,
            courses = request.IncludeStudyLabRecords ? selection.Courses.Select(x => new
            {
                x.Id,
                x.MoodleCourseId,
                x.Name,
                x.Code,
                x.Cfu,
                x.Completed,
                status = x.Status.ToString(),
                x.Archived,
                x.Teacher,
                x.AcademicYear,
                x.Session,
                x.EstimatedStudyHours,
                x.AppealDate,
                syllabusSummary = request.IncludeNotes ? x.SyllabusSummary : null,
                notes = request.IncludeNotes ? x.Notes : null,
                x.Source,
                x.UpdatedAt
            }) : null
        };
        return new StudyExportFile("unitracker-export.json", JsonContentType,
            JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
    }

    private static StudyExportFile BuildXlsx(ExportSelection selection, StudyExportRequest request, DateTimeOffset generatedAt)
    {
        var sheets = new List<XlsxSheet>
        {
            SheetFromTable("Attività", ActivityTable(selection, request)),
            SheetFromTable("Sessioni", SessionTable(selection, request)),
            SheetFromTable("Segmenti", SegmentTable(selection)),
            new("Riepilogo", new[] { "Metrica", "Valore" }, new[]
            {
                new object?[] { "Schema", SchemaVersion },
                new object?[] { "Generato il", generatedAt },
                new object?[] { "Generato il (ISO)", generatedAt.ToString("O", CultureInfo.InvariantCulture) },
                new object?[] { "Dal", selection.Request.From },
                new object?[] { "Al", selection.Request.To },
                new object?[] { "Secondi unici", selection.Summary.RecordedSeconds },
                new object?[] { "Secondi sovrapposti", selection.Summary.OverlapSeconds },
                new object?[] { "Attività", selection.Summary.ActivityCount },
                new object?[] { "Minuti per tipo", string.Join("; ", selection.Summary.ByType.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) },
                new object?[] { "Minuti per materia", string.Join("; ", selection.Summary.ByCourse.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) },
                new object?[] { "Minuti per argomento", string.Join("; ", selection.Summary.ByTopic.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value / 60}")) }
            }, new[] { 0, 0 })
        };
        if (request.IncludeStudyLabRecords)
        {
            sheets.Add(SheetFromTable("Task", TaskTable(selection, request)));
            sheets.Add(SheetFromTable("Missioni", MissionTable(selection, request)));
            sheets.Add(SheetFromTable("Passi missioni", MissionStepTable(selection, request)));
            sheets.Add(SheetFromTable("Flashcard", FlashcardTable(selection, request)));
            sheets.Add(SheetFromTable("Ripassi", FlashcardReviewTable(selection)));
            sheets.Add(SheetFromTable("Errori", ErrorTable(selection, request)));
            sheets.Add(SheetFromTable("Check-in", EnergyCheckInTable(selection, request)));
            sheets.Add(SheetFromTable("Syllabus", SyllabusTable(selection, request)));
            sheets.Add(SheetFromTable("Domande", QuestionTable(selection, request)));
            sheets.Add(SheetFromTable("Voti", GradeTable(selection, request)));
            sheets.Add(SheetFromTable("Tentativi", CareerAttemptTable(selection, request)));
            sheets.Add(SheetFromTable("Appelli planner", ExamTable(selection)));
            sheets.Add(SheetFromTable("Materie", CourseTable(selection, request)));
        }
        if (request.IncludePomodoroCycles)
            sheets.Add(SheetFromTable("Pomodoro", PomodoroTable(selection)));
        return new StudyExportFile("unitracker-export.xlsx", XlsxContentType, XlsxBytes(sheets));
    }

    private static XlsxSheet SheetFromTable(string name, ExportTable table)
    {
        if (table.InstantColumns.Count == 0)
            return new XlsxSheet(name, table.Headers, table.Rows, table.Styles);

        var headers = table.Headers
            .Concat(table.InstantColumns.Select(index => table.Headers[index] + " ISO"))
            .ToArray();
        var styles = table.Styles
            .Concat(table.InstantColumns.Select(_ => 0))
            .ToArray();
        var rows = table.Rows.Select(row => row
            .Concat(table.InstantColumns.Select(index => row[index] is DateTimeOffset instant
                ? (object?)instant.ToString("O", CultureInfo.InvariantCulture)
                : string.Empty))
            .ToArray()).ToList();
        return new XlsxSheet(name, headers, rows, styles);
    }

    private static ExportTable ActivityTable(ExportSelection selection, StudyExportRequest request)
    {
        var columns = new List<ExportColumn<StudyActivityData>>
        {
            new("ID", x => x.Id),
            new("Tipo", x => x.Type.ToString()),
            new("Titolo", x => x.Title),
            new("Materia", x => x.CourseId),
            new("Argomento", x => x.SyllabusTopicId),
            new("Task", x => x.TaskId),
            new("Missione", x => x.MissionId),
            new("Sessione", x => x.SessionId),
            new("Inizio", x => x.StartedAt, 1, IsInstant: true),
            new("Fine", x => x.EndedAt, 1, IsInstant: true),
            new("Secondi effettivi", x => x.EffectiveSeconds, 2),
            new("Origine", x => x.Origin.ToString()),
            new("Stato", x => x.Status.ToString())
        };
        if (request.IncludeResult) columns.Add(new("Risultato", x => x.Result));
        if (request.IncludeRatings)
        {
            columns.Add(new("Focus 1-5", x => x.FocusRating));
            columns.Add(new("Difficoltà 1-5", x => x.DifficultyRating));
            columns.Add(new("Qualità 1-5", x => x.QualityRating));
            columns.Add(new("Energia 1-5", x => x.EnergyRating));
        }
        if (request.IncludeNotes) columns.Add(new("Nota", x => NoteForExport(x, request)));
        if (request.IncludeTags) columns.Add(new("Tag", x => string.Join(", ", x.Tags)));
        columns.Add(new("Privata", x => x.IsPrivate));
        columns.Add(new("Revisione", x => x.Revision));
        columns.Add(new("Eliminata il", x => x.DeletedAt, 1, IsInstant: true));
        return Table(columns, selection.Activities);
    }

    private static ExportTable SessionTable(ExportSelection selection, StudyExportRequest request)
    {
        var columns = new List<ExportColumn<StudySessionData>>
        {
            new("ID", x => x.Id),
            new("Materia", x => x.CourseId),
            new("Argomento", x => x.Topic),
            new("Minuti pianificati", x => x.PlannedMinutes),
            new("Inizio", x => x.StartedAt, 1, IsInstant: true),
            new("Fine", x => x.EndedAt, 1, IsInstant: true),
            new("Stato", x => x.State.ToString()),
            new("Secondi focus", x => x.AccumulatedFocusSeconds, 2),
            new("Secondi pausa", x => x.AccumulatedPauseSeconds, 2)
        };
        if (request.IncludeResult) columns.Add(new("Esito", x => x.Outcome));
        if (request.IncludeNotes) columns.Add(new("Note", x => SessionNotesForExport(x, request)));
        columns.Add(new("Note private", x => x.NotesArePrivate));
        return Table(columns, selection.Sessions);
    }

    private static ExportTable SegmentTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<SegmentRow>("Sessione", x => x.SessionId),
            new ExportColumn<SegmentRow>("Segmento", x => x.Segment.Id),
            new ExportColumn<SegmentRow>("Tipo", x => x.Segment.Type.ToString()),
            new ExportColumn<SegmentRow>("Inizio", x => x.Segment.StartedAt, 1, IsInstant: true),
            new ExportColumn<SegmentRow>("Fine", x => x.Segment.EndedAt, 1, IsInstant: true),
            new ExportColumn<SegmentRow>("Secondi", x => x.DurationSeconds, 2)
        }, selection.Segments);

    private static ExportTable PomodoroTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<PomodoroCycleData>("ID", x => x.Id),
            new ExportColumn<PomodoroCycleData>("Sessione", x => x.SessionId),
            new ExportColumn<PomodoroCycleData>("Ciclo", x => x.CycleNumber),
            new ExportColumn<PomodoroCycleData>("Focus inizio", x => x.FocusStartedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Focus fine", x => x.FocusEndedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Secondi focus", x => x.FocusSeconds, 2),
            new ExportColumn<PomodoroCycleData>("Pausa", x => x.BreakType.ToString()),
            new ExportColumn<PomodoroCycleData>("Pausa inizio", x => x.BreakStartedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Pausa fine", x => x.BreakEndedAt, 1, IsInstant: true),
            new ExportColumn<PomodoroCycleData>("Secondi pausa", x => x.BreakSeconds, 2),
            new ExportColumn<PomodoroCycleData>("Saltato", x => x.WasSkipped),
            new ExportColumn<PomodoroCycleData>("Completato", x => x.IsCompleted),
            new ExportColumn<PomodoroCycleData>("Completato il", x => x.CompletedAt, 1, IsInstant: true)
        }, selection.Cycles);

    private static ExportTable TaskTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<StudyTaskData>("ID", x => x.Id),
            new ExportColumn<StudyTaskData>("Materia", x => x.CourseId),
            new ExportColumn<StudyTaskData>("Titolo", x => x.Title),
            new ExportColumn<StudyTaskData>("Descrizione", x => request.IncludeNotes ? x.Description : null),
            new ExportColumn<StudyTaskData>("Scadenza", x => x.DueAt, 1, IsInstant: true),
            new ExportColumn<StudyTaskData>("Minuti stimati", x => x.EstimatedMinutes),
            new ExportColumn<StudyTaskData>("Priorità", x => x.Priority),
            new ExportColumn<StudyTaskData>("Stato", x => x.Status.ToString()),
            new ExportColumn<StudyTaskData>("Completato il", x => x.CompletedAt, 1, IsInstant: true),
            new ExportColumn<StudyTaskData>("Origine", x => x.Source),
            new ExportColumn<StudyTaskData>("Creato il", x => x.CreatedAt, 1, IsInstant: true),
            new ExportColumn<StudyTaskData>("Aggiornato il", x => x.UpdatedAt, 1, IsInstant: true)
        }, selection.Tasks);

    private static ExportTable MissionTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<StudyMissionData>("ID", x => x.Id),
            new ExportColumn<StudyMissionData>("Materia", x => x.CourseId),
            new ExportColumn<StudyMissionData>("Argomento", x => x.SyllabusTopicId),
            new ExportColumn<StudyMissionData>("Titolo", x => x.Title),
            new ExportColumn<StudyMissionData>("Descrizione", x => request.IncludeNotes ? x.Description : null),
            new ExportColumn<StudyMissionData>("Periodo", x => x.Period.ToString()),
            new ExportColumn<StudyMissionData>("Inizio", x => x.StartsAt, 1, IsInstant: true),
            new ExportColumn<StudyMissionData>("Scadenza", x => x.DueAt, 1, IsInstant: true),
            new ExportColumn<StudyMissionData>("Difficoltà", x => x.Difficulty),
            new ExportColumn<StudyMissionData>("Stato", x => x.Status.ToString()),
            new ExportColumn<StudyMissionData>("Ricompensa", x => request.IncludeNotes ? x.Reward : null),
            new ExportColumn<StudyMissionData>("Completato il", x => x.CompletedAt, 1, IsInstant: true),
            new ExportColumn<StudyMissionData>("Passi completati", x => x.CompletedStepCount),
            new ExportColumn<StudyMissionData>("Passi totali", x => x.Steps.Count),
            new ExportColumn<StudyMissionData>("Origine", x => x.Source),
            new ExportColumn<StudyMissionData>("Creato il", x => x.CreatedAt, 1, IsInstant: true),
            new ExportColumn<StudyMissionData>("Aggiornato il", x => x.UpdatedAt, 1, IsInstant: true)
        }, selection.Missions);

    private static ExportTable MissionStepTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<MissionStepRow>("Missione", x => x.MissionId),
            new ExportColumn<MissionStepRow>("Materia", x => x.CourseId),
            new ExportColumn<MissionStepRow>("ID passo", x => x.Step.Id),
            new ExportColumn<MissionStepRow>("Titolo", x => request.IncludeNotes ? x.Step.Title : null),
            new ExportColumn<MissionStepRow>("Completato", x => x.Step.IsCompleted),
            new ExportColumn<MissionStepRow>("Posizione", x => x.Step.Position),
            new ExportColumn<MissionStepRow>("Completato il", x => x.Step.CompletedAt, 1, IsInstant: true)
        }, selection.Missions.SelectMany(mission =>
            mission.Steps.Select(step => new MissionStepRow(mission.Id, mission.CourseId, step))));

    private static ExportTable FlashcardTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<FlashcardData>("ID", x => x.Id),
            new ExportColumn<FlashcardData>("Materia", x => x.CourseId),
            new ExportColumn<FlashcardData>("Argomento", x => x.SyllabusTopicId),
            new ExportColumn<FlashcardData>("Fronte", x => request.IncludeNotes ? x.Front : null),
            new ExportColumn<FlashcardData>("Retro", x => request.IncludeNotes ? x.Back : null),
            new ExportColumn<FlashcardData>("Origine", x => x.Source),
            new ExportColumn<FlashcardData>("Ease factor", x => x.EaseFactor),
            new ExportColumn<FlashcardData>("Intervallo giorni", x => x.IntervalDays),
            new ExportColumn<FlashcardData>("Ripetizioni", x => x.Repetitions),
            new ExportColumn<FlashcardData>("Errori", x => x.Lapses),
            new ExportColumn<FlashcardData>("Prossima revisione", x => x.DueAt, 1, IsInstant: true),
            new ExportColumn<FlashcardData>("Ultima revisione", x => x.LastReviewedAt, 1, IsInstant: true),
            new ExportColumn<FlashcardData>("Sospesa", x => x.IsSuspended),
            new ExportColumn<FlashcardData>("Creata il", x => x.CreatedAt, 1, IsInstant: true),
            new ExportColumn<FlashcardData>("Aggiornata il", x => x.UpdatedAt, 1, IsInstant: true)
        }, selection.Flashcards);

    private static ExportTable FlashcardReviewTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<FlashcardReviewData>("ID", x => x.Id),
            new ExportColumn<FlashcardReviewData>("Flashcard", x => x.FlashcardId),
            new ExportColumn<FlashcardReviewData>("Data", x => x.ReviewedAt, 1, IsInstant: true),
            new ExportColumn<FlashcardReviewData>("Valutazione", x => x.Rating.ToString()),
            new ExportColumn<FlashcardReviewData>("Intervallo precedente", x => x.PreviousIntervalDays),
            new ExportColumn<FlashcardReviewData>("Nuovo intervallo", x => x.NewIntervalDays),
            new ExportColumn<FlashcardReviewData>("Ease factor", x => x.EaseFactorAfter)
        }, selection.FlashcardReviews);

    private static ExportTable ErrorTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<ErrorJournalEntryData>("ID", x => x.Id),
            new ExportColumn<ErrorJournalEntryData>("Materia", x => x.CourseId),
            new ExportColumn<ErrorJournalEntryData>("Argomento", x => x.Topic),
            new ExportColumn<ErrorJournalEntryData>("Domanda", x => request.IncludeNotes ? x.Question : null),
            new ExportColumn<ErrorJournalEntryData>("Risposta errata", x => request.IncludeNotes && request.IncludeResult ? x.IncorrectAnswer : null),
            new ExportColumn<ErrorJournalEntryData>("Risposta corretta", x => request.IncludeNotes && request.IncludeResult ? x.CorrectAnswer : null),
            new ExportColumn<ErrorJournalEntryData>("Tipo errore", x => x.ErrorType),
            new ExportColumn<ErrorJournalEntryData>("Causa", x => request.IncludeNotes ? x.RootCause : null),
            new ExportColumn<ErrorJournalEntryData>("Prossimo ripasso", x => x.NextReviewAt, 1, IsInstant: true),
            new ExportColumn<ErrorJournalEntryData>("Numero revisioni", x => x.ReviewCount),
            new ExportColumn<ErrorJournalEntryData>("Stato", x => x.Status.ToString()),
            new ExportColumn<ErrorJournalEntryData>("Origine", x => x.Source),
            new ExportColumn<ErrorJournalEntryData>("Creato il", x => x.CreatedAt, 1, IsInstant: true),
            new ExportColumn<ErrorJournalEntryData>("Aggiornato il", x => x.UpdatedAt, 1, IsInstant: true)
        }, selection.Errors);

    private static ExportTable EnergyCheckInTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<EnergyCheckInData>("ID", x => x.Id),
            new ExportColumn<EnergyCheckInData>("Data", x => x.RecordedAt, 1, IsInstant: true),
            new ExportColumn<EnergyCheckInData>("Energia 1-5", x => x.EnergyLevel),
            new ExportColumn<EnergyCheckInData>("Focus 1-5", x => x.FocusLevel),
            new ExportColumn<EnergyCheckInData>("Nota privata", x => request.IncludeNotes && request.IncludePrivateNotes ? x.Note : null)
        }, selection.EnergyCheckIns);

    private static ExportTable SyllabusTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<SyllabusTopicData>("ID", x => x.Id),
            new ExportColumn<SyllabusTopicData>("Materia", x => x.CourseId),
            new ExportColumn<SyllabusTopicData>("Argomento", x => x.Title),
            new ExportColumn<SyllabusTopicData>("Descrizione", x => request.IncludeNotes ? x.Description : null),
            new ExportColumn<SyllabusTopicData>("Stato", x => x.Status.ToString()),
            new ExportColumn<SyllabusTopicData>("Minuti stimati", x => x.EstimatedMinutes),
            new ExportColumn<SyllabusTopicData>("Ultimo studio", x => FormatDateTime(x.LastStudiedAt)),
            new ExportColumn<SyllabusTopicData>("Aggiornato il", x => FormatDateTime(x.UpdatedAt))
        }, selection.SyllabusTopics);

    private static ExportTable QuestionTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<QuestionBankItemData>("ID", x => x.Id),
            new ExportColumn<QuestionBankItemData>("Materia", x => x.CourseId),
            new ExportColumn<QuestionBankItemData>("Argomento", x => x.SyllabusTopicId),
            new ExportColumn<QuestionBankItemData>("Testo", x => request.IncludeNotes ? x.Text : null),
            new ExportColumn<QuestionBankItemData>("Tipo", x => x.Type.ToString()),
            new ExportColumn<QuestionBankItemData>("Difficoltà", x => x.Difficulty),
            new ExportColumn<QuestionBankItemData>("Opzioni", x => request.IncludeNotes ? string.Join(" | ", x.Options) : null),
            new ExportColumn<QuestionBankItemData>("Risposta corretta", x => request.IncludeNotes && request.IncludeResult ? x.CorrectAnswer : null),
            new ExportColumn<QuestionBankItemData>("Spiegazione", x => request.IncludeNotes ? x.Explanation : null),
            new ExportColumn<QuestionBankItemData>("Fonte", x => x.Source),
            new ExportColumn<QuestionBankItemData>("Ufficiale", x => x.IsOfficial),
            new ExportColumn<QuestionBankItemData>("Creato il", x => FormatDateTime(x.CreatedAt)),
            new ExportColumn<QuestionBankItemData>("Aggiornato il", x => FormatDateTime(x.UpdatedAt))
        }, selection.Questions);

    private static ExportTable GradeTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<Grade>("ID", x => x.Id),
            new ExportColumn<Grade>("Materia", x => x.CourseId),
            new ExportColumn<Grade>("Nome materia", x => x.CourseName),
            new ExportColumn<Grade>("Voto", x => request.IncludeResult ? x.Value : null),
            new ExportColumn<Grade>("Lode", x => request.IncludeResult ? x.Honors : null),
            new ExportColumn<Grade>("CFU", x => x.Cfu),
            new ExportColumn<Grade>("Data", x => DateOnly.FromDateTime(x.Date)),
            new ExportColumn<Grade>("Conta nella media", x => x.CountsTowardAverage)
        }, selection.Grades);

    private static ExportTable CareerAttemptTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<CareerAttemptRow>("Esame", x => x.ExamId),
            new ExportColumn<CareerAttemptRow>("Materia", x => x.CourseId),
            new ExportColumn<CareerAttemptRow>("Nome esame", x => x.ExamName),
            new ExportColumn<CareerAttemptRow>("CFU", x => x.Cfu),
            new ExportColumn<CareerAttemptRow>("Conta nella media", x => x.CountsTowardAverage),
            new ExportColumn<CareerAttemptRow>("Tentativo", x => x.Attempt.Id),
            new ExportColumn<CareerAttemptRow>("Data tentativo", x => x.Attempt.AttemptedAt, 1, IsInstant: true),
            new ExportColumn<CareerAttemptRow>("Stato", x => x.Attempt.Status),
            new ExportColumn<CareerAttemptRow>("Esito", x => request.IncludeResult ? x.Attempt.Outcome : null),
            new ExportColumn<CareerAttemptRow>("Decisione voto", x => request.IncludeResult ? x.Attempt.GradeDecision : null),
            new ExportColumn<CareerAttemptRow>("Voto", x => request.IncludeResult ? x.Attempt.NumericGrade : null),
            new ExportColumn<CareerAttemptRow>("Lode", x => request.IncludeResult ? x.Attempt.IsHonors : null),
            new ExportColumn<CareerAttemptRow>("Idoneità", x => x.Attempt.IsPassFail),
            new ExportColumn<CareerAttemptRow>("Registrato il", x => x.Attempt.RecordedAt, 1, IsInstant: true)
        }, selection.CareerAttempts);

    private static ExportTable ExamTable(ExportSelection selection) =>
        Table(new[]
        {
            new ExportColumn<Exam>("ID", x => x.Id),
            new ExportColumn<Exam>("Materia", x => x.CourseId),
            new ExportColumn<Exam>("Nome materia", x => x.CourseName),
            new ExportColumn<Exam>("Esame", x => x.Name),
            new ExportColumn<Exam>("CFU", x => x.Cfu),
            new ExportColumn<Exam>("Ore stimate", x => x.EstimatedStudyHours),
            new ExportColumn<Exam>("Data pianificata", x => x.PlannedDate is { } date ? DateOnly.FromDateTime(date) : null),
            new ExportColumn<Exam>("Sessione", x => x.PlannedSession),
            new ExportColumn<Exam>("Completato", x => x.Completed)
        }, selection.Exams);

    private static ExportTable CourseTable(ExportSelection selection, StudyExportRequest request) =>
        Table(new[]
        {
            new ExportColumn<Course>("ID", x => x.Id),
            new ExportColumn<Course>("Moodle ID", x => x.MoodleCourseId),
            new ExportColumn<Course>("Nome", x => x.Name),
            new ExportColumn<Course>("Codice", x => x.Code),
            new ExportColumn<Course>("CFU", x => x.Cfu),
            new ExportColumn<Course>("Completato", x => x.Completed),
            new ExportColumn<Course>("Stato", x => x.Status.ToString()),
            new ExportColumn<Course>("Archiviato", x => x.Archived),
            new ExportColumn<Course>("Docente", x => x.Teacher),
            new ExportColumn<Course>("Anno accademico", x => x.AcademicYear),
            new ExportColumn<Course>("Sessione", x => x.Session),
            new ExportColumn<Course>("Ore stimate", x => x.EstimatedStudyHours),
            new ExportColumn<Course>("Data appello", x => x.AppealDate is { } date ? DateOnly.FromDateTime(date) : null),
            new ExportColumn<Course>("Syllabus", x => request.IncludeNotes ? x.SyllabusSummary : null),
            new ExportColumn<Course>("Note", x => request.IncludeNotes ? x.Notes : null),
            new ExportColumn<Course>("Fonte", x => x.Source),
            new ExportColumn<Course>("Aggiornato il", x => FormatDateTime(x.UpdatedAt))
        }, selection.Courses);

    private static string? FormatDateTime(DateTime? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    private static ExportTable SummaryTable(ExportSelection selection)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Secondi unici", selection.Summary.RecordedSeconds },
            new object?[] { "Secondi sovrapposti", selection.Summary.OverlapSeconds },
            new object?[] { "Attività", selection.Summary.ActivityCount }
        };
        foreach (var item in selection.Summary.ByType.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Tipo: {item.Key}", item.Value });
        foreach (var item in selection.Summary.ByCourse.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Materia: {item.Key}", item.Value });
        foreach (var item in selection.Summary.ByTopic.OrderBy(x => x.Key))
            rows.Add(new object?[] { $"Argomento: {item.Key}", item.Value });
        return new ExportTable(new[] { "Metrica", "Secondi" }, new[] { 0, 2 }, rows, Array.Empty<int>());
    }

    private static ExportTable Table<T>(IEnumerable<ExportColumn<T>> columns, IEnumerable<T> rows)
    {
        var list = columns.ToList();
        return new ExportTable(
            list.Select(x => x.Header).ToArray(),
            list.Select(x => x.Style).ToArray(),
            rows.Select(row => list.Select(column => column.Value(row)).ToArray()).ToList(),
            list.Select((column, index) => (column, index))
                .Where(x => x.column.IsInstant)
                .Select(x => x.index)
                .ToArray());
    }

    private static byte[] CsvBytes(ExportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', table.Headers.Select(EscapeCsv)));
        foreach (var row in table.Rows)
            builder.AppendLine(string.Join(',', row.Select(EscapeCsv)));
        // Encoding.GetBytes does not emit the preamble. Prefix it explicitly so Excel
        // recognizes UTF-8 CSV files and preserves accents and special characters.
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static string EscapeCsv(object? value)
    {
        if (value is null) return string.Empty;
        if (value is DateTimeOffset date)
            return QuoteCsv(date.ToString("O", CultureInfo.InvariantCulture));
        if (value is DateOnly dateOnly)
            return QuoteCsv(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (value is bool boolean)
            return boolean ? "true" : "false";
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        return QuoteCsv(NeedsFormulaGuard(value.ToString() ?? string.Empty)
            ? "'" + value
            : value.ToString() ?? string.Empty);
    }

    private static string QuoteCsv(string text) =>
        "\"" + text.Replace("\"", "\"\"") + "\"";

    private static bool NeedsFormulaGuard(string text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character)) continue;
            return character is '=' or '+' or '-' or '@';
        }
        return false;
    }

    private static byte[] XlsxBytes(IReadOnlyList<XlsxSheet> sheets)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypes(sheets.Count));
            AddEntry(archive, "_rels/.rels", RootRelationships());
            AddEntry(archive, "xl/workbook.xml", Workbook(sheets));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
            AddEntry(archive, "xl/styles.xml", Styles());
            for (var index = 0; index < sheets.Count; index++)
                AddEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheets[index]));
        }
        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Worksheet(XlsxSheet sheet)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(builder, 1, sheet.Headers.Select(x => new ExportCell(x, 0)));
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var row = sheet.Rows[index];
            AppendRow(builder, index + 2,
                row.Select((value, column) => new ExportCell(value, sheet.Styles[column])));
        }
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, int rowNumber, IEnumerable<ExportCell> cells)
    {
        builder.Append("<row r=\"").Append(rowNumber).Append("\">");
        var column = 0;
        foreach (var cell in cells)
        {
            builder.Append("<c r=\"").Append(ColumnName(column++)).Append(rowNumber).Append('"');
            if (cell.Style > 0) builder.Append(" s=\"").Append(cell.Style).Append('"');
            var value = cell.Value;
            if (value is null)
            {
                builder.Append("/>");
                continue;
            }
            if (value is string text)
            {
                builder.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(Xml(text)).Append("</t></is></c>");
                continue;
            }
            if (value is DateTimeOffset date)
            {
                var serial = (date.DateTime - new DateTime(1899, 12, 30)).TotalDays;
                builder.Append("><v>").Append(serial.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                continue;
            }
            if (value is DateOnly dateOnly)
            {
                var serial = (dateOnly.ToDateTime(TimeOnly.MinValue) - new DateTime(1899, 12, 30)).TotalDays;
                builder.Append("><v>").Append(serial.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                continue;
            }
            if (value is bool boolean)
            {
                builder.Append(" t=\"b\"><v>").Append(boolean ? '1' : '0').Append("</v></c>");
                continue;
            }
            builder.Append("><v>").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("</v></c>");
        }
        builder.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static string ContentTypes(int sheetCount)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var index = 1; index <= sheetCount; index++)
            builder.Append("<Override PartName=\"/xl/worksheets/sheet").Append(index)
                .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return builder.Append("</Types>").ToString();
    }

    private static string RootRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";

    private static string Workbook(IReadOnlyList<XlsxSheet> sheets)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (var index = 0; index < sheets.Count; index++)
            builder.Append("<sheet name=\"").Append(Xml(sheets[index].Name)).Append("\" sheetId=\"")
                .Append(index + 1).Append("\" r:id=\"rId").Append(index + 1).Append("\"/>");
        return builder.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRelationships(int sheetCount)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var index = 1; index <= sheetCount; index++)
            builder.Append("<Relationship Id=\"rId").Append(index)
                .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                .Append(index).Append(".xml\"/>");
        builder.Append("<Relationship Id=\"rId").Append(sheetCount + 1)
            .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return builder.Append("</Relationships>").ToString();
    }

    private static string Styles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"[h]:mm:ss\"/></numFmts><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>";

    private static string Xml(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string? NoteForExport(StudyActivityData activity, StudyExportRequest request) =>
        request.IncludeNotes && (request.IncludePrivateNotes || !activity.IsPrivate)
            ? activity.Note
            : null;

    private static string? SessionNotesForExport(StudySessionData session, StudyExportRequest request) =>
        request.IncludeNotes && (request.IncludePrivateNotes || !session.NotesArePrivate)
            ? session.Notes
            : null;

    private static object FilterDocument(StudyExportRequest request) => new
    {
        request.From,
        request.To,
        request.TimeZoneId,
        request.CourseIds,
        activityTypes = request.ActivityTypes?.Select(x => x.ToString()).ToArray(),
        request.IncludePomodoroCycles,
        request.IncludePrivateNotes,
        request.IncludeNotes,
        request.IncludeRatings,
        request.IncludeTags,
        request.IncludeResult,
        request.IncludeDeleted,
        request.IncludeStudyLabRecords,
        includedStudyLabCollections = request.IncludeStudyLabRecords
            ? new[]
            {
                "tasks", "missions", "missionSteps", "flashcards", "flashcardReviews",
                "errors", "energyCheckIns", "syllabusTopics", "questions", "grades",
                "careerAttempts", "exams", "courses"
            }
            : Array.Empty<string>()
    };

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException ex) { throw new ArgumentException($"Unknown time zone '{id}'.", nameof(id), ex); }
        catch (InvalidTimeZoneException ex) { throw new ArgumentException($"Invalid time zone '{id}'.", nameof(id), ex); }
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private sealed record ExportColumn<T>(string Header, Func<T, object?> Value, int Style = 0, bool IsInstant = false);
    private sealed record ExportTable(
        string[] Headers,
        int[] Styles,
        IReadOnlyList<object?[]> Rows,
        IReadOnlyList<int> InstantColumns);
    private sealed record XlsxSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows, int[] Styles);
    private sealed record ExportCell(object? Value, int Style);
    private sealed record ExportSelection(
        IReadOnlyList<StudyActivityData> Activities,
        IReadOnlyList<StudySessionData> Sessions,
        IReadOnlyList<PomodoroCycleData> Cycles,
        ActivitySummary Summary,
        TimeZoneInfo Zone,
        StudyExportRequest Request,
        DateTimeOffset Now)
    {
        public IReadOnlyList<SegmentRow> Segments { get; } = Sessions
            .SelectMany(session => session.Segments.Select(segment => new SegmentRow(session.Id, segment, Now)))
            .OrderBy(x => x.Segment.StartedAt)
            .ToList();

        public IReadOnlyList<StudyTaskData> Tasks { get; init; } = Array.Empty<StudyTaskData>();
        public IReadOnlyList<StudyMissionData> Missions { get; init; } = Array.Empty<StudyMissionData>();
        public IReadOnlyList<FlashcardData> Flashcards { get; init; } = Array.Empty<FlashcardData>();
        public IReadOnlyList<FlashcardReviewData> FlashcardReviews { get; init; } = Array.Empty<FlashcardReviewData>();
        public IReadOnlyList<ErrorJournalEntryData> Errors { get; init; } = Array.Empty<ErrorJournalEntryData>();
        public IReadOnlyList<EnergyCheckInData> EnergyCheckIns { get; init; } = Array.Empty<EnergyCheckInData>();
        public IReadOnlyList<SyllabusTopicData> SyllabusTopics { get; init; } = Array.Empty<SyllabusTopicData>();
        public IReadOnlyList<QuestionBankItemData> Questions { get; init; } = Array.Empty<QuestionBankItemData>();
        public IReadOnlyList<Grade> Grades { get; init; } = Array.Empty<Grade>();
        public IReadOnlyList<CareerAttemptRow> CareerAttempts { get; init; } = Array.Empty<CareerAttemptRow>();
        public IReadOnlyList<Exam> Exams { get; init; } = Array.Empty<Exam>();
        public IReadOnlyList<Course> Courses { get; init; } = Array.Empty<Course>();
    }

    private sealed record CareerAttemptRow(
        string ExamId,
        string CourseId,
        string ExamName,
        int Cfu,
        bool CountsTowardAverage,
        ExamAttemptData Attempt);

    private sealed record MissionStepRow(string MissionId, string CourseId, MissionStepData Step);

    private sealed record SegmentRow(string SessionId, StudySessionSegmentData Segment, DateTimeOffset Now)
    {
        public int DurationSeconds => Segment.EndedAt is { } end
            ? Math.Max(0, (int)Math.Floor((end - Segment.StartedAt).TotalSeconds))
            : Math.Max(0, (int)Math.Floor((Now - Segment.StartedAt).TotalSeconds));
    }
}
