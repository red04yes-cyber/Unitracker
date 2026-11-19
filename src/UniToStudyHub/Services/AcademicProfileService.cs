using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record AcademicProfileUpdate(
    string DegreeProgramName,
    string DegreeProgramCode,
    int? EnrollmentYear,
    int? CurrentAcademicYear,
    int DegreeTotalCfu,
    DateTime GraduationTarget,
    string StudyPreferences,
    int WeeklyStudyHours,
    int MaxDailyStudyMinutes);

/// <summary>
/// A non-mutating inventory of the records affected by deleting a course.
/// Calendar events are counted separately because they can be detached while
/// preserving the event itself. Active focus work must be completed first.
/// </summary>
public sealed record CourseRemovalImpact(
    int GradeRecords,
    int PlannedExams,
    int CareerExams,
    int CareerExamAttempts,
    int SyllabusTopics,
    int PrerequisiteLinks,
    int Questions,
    int StudySessions,
    int StudySessionSegments,
    int StudyTasks,
    int StudyActivities,
    int StudyMissions,
    int MissionSteps,
    int Flashcards,
    int FlashcardReviews,
    int ErrorJournalEntries,
    int Distractions,
    int PomodoroCycles,
    int PdfDocuments,
    int PdfPages,
    int CalendarEventsToDetach,
    int ActivityRevisions,
    bool HasActiveStudySession,
    bool HasActivePomodoro)
{
    public bool HasAcademicHistory =>
        GradeRecords > 0 || PlannedExams > 0 || CareerExams > 0 || CareerExamAttempts > 0 ||
        SyllabusTopics > 0 || PrerequisiteLinks > 0;

    public bool HasStudyHistory =>
        Questions > 0 || StudySessions > 0 || StudySessionSegments > 0 ||
        StudyTasks > 0 || StudyActivities > 0 || StudyMissions > 0 ||
        MissionSteps > 0 || Flashcards > 0 || FlashcardReviews > 0 ||
        ErrorJournalEntries > 0 || Distractions > 0 || PomodoroCycles > 0 ||
        PdfDocuments > 0 || PdfPages > 0 || ActivityRevisions > 0;

    public bool HasActiveWork => HasActiveStudySession || HasActivePomodoro;

    public bool CanRemoveWithoutConfirmation =>
        !HasAcademicHistory && !HasStudyHistory &&
        CalendarEventsToDetach == 0 && !HasActiveWork;
}

/// <summary>
/// Validates and applies university-profile changes. This service never saves:
/// the caller owns the transaction through <see cref="IStudyDataStore"/>.
/// </summary>
public sealed class AcademicProfileService
{
    public void Update(StudyData data, AcademicProfileUpdate update)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(update);

        var name = update.DegreeProgramName?.Trim() ?? string.Empty;
        var code = update.DegreeProgramCode?.Trim() ?? string.Empty;
        var preferences = update.StudyPreferences?.Trim() ?? string.Empty;
        if (update.EnrollmentYear is < 1900 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(update), "Enrollment year is outside the supported range.");
        if (update.CurrentAcademicYear is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(update), "Academic year must be between 1 and 20.");
        if (update.EnrollmentYear is int enrollment && update.CurrentAcademicYear is int current &&
            enrollment + current - 1 > DateTime.Today.Year + 1)
            throw new ArgumentOutOfRangeException(nameof(update), "Academic year is inconsistent with the enrollment year.");
        if (update.DegreeTotalCfu is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(update), "Degree CFU total must be between 0 and 1000.");
        if (update.WeeklyStudyHours is < 0 or > 168)
            throw new ArgumentOutOfRangeException(nameof(update), "Weekly study hours must be between 0 and 168.");
        if (update.MaxDailyStudyMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(update), "Daily study minutes must be between 0 and 1440.");
        if (update.GraduationTarget == default)
            throw new ArgumentException("Graduation target is required.", nameof(update));
        if (update.DegreeTotalCfu > 0 && data.Courses.Sum(x => x.Cfu) > update.DegreeTotalCfu)
            throw new ArgumentOutOfRangeException(nameof(update), "Course CFU exceed the configured degree total.");

        data.DegreeProgramName = name;
        data.DegreeProgramCode = code;
        data.EnrollmentYear = update.EnrollmentYear;
        data.CurrentAcademicYear = update.CurrentAcademicYear;
        data.DegreeTotalCfu = update.DegreeTotalCfu;
        data.GraduationTarget = update.GraduationTarget;
        data.StudyPreferences = preferences;
        data.WeeklyStudyHours = update.WeeklyStudyHours;
        data.MaxDailyStudyMinutes = update.MaxDailyStudyMinutes;
    }

    public Course AddCourse(StudyData data, Course draft)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(draft);
        ValidateCourse(draft, data);
        if (data.Courses.Any(x => string.Equals(x.Id, draft.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Course id '{draft.Id}' already exists.", nameof(draft));
        if (!string.IsNullOrWhiteSpace(draft.Code) &&
            data.Courses.Any(x => string.Equals(x.Code, draft.Code, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Course code '{draft.Code}' already exists.", nameof(draft));

        ApplyCourse(draft);
        data.Courses.Add(draft);
        return draft;
    }

    public void UpdateCourse(StudyData data, Course updated)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(updated);
        var existing = data.Courses.FirstOrDefault(x => string.Equals(x.Id, updated.Id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{updated.Id}' does not exist.", nameof(updated));
        ValidateCourse(updated, data, existing.Id);
        if (!string.IsNullOrWhiteSpace(updated.Code) &&
            data.Courses.Any(x => !string.Equals(x.Id, existing.Id, StringComparison.Ordinal) &&
                                  string.Equals(x.Code, updated.Code, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Course code '{updated.Code}' already exists.", nameof(updated));

        ApplyCourse(updated);
        existing.Name = updated.Name;
        existing.Code = updated.Code;
        existing.Cfu = updated.Cfu;
        existing.Teacher = updated.Teacher;
        existing.AcademicYear = updated.AcademicYear;
        existing.Session = updated.Session;
        existing.EstimatedStudyHours = updated.EstimatedStudyHours;
        existing.AppealDate = updated.AppealDate;
        existing.SyllabusSummary = updated.SyllabusSummary;
        existing.SyllabusUrl = updated.SyllabusUrl;
        existing.Notes = updated.Notes;
        existing.Source = updated.Source;
        existing.Status = updated.Status;
        existing.Archived = updated.Archived;
        existing.Completed = updated.Completed;
        existing.UpdatedAt = updated.UpdatedAt ?? DateTime.UtcNow;
    }

    public void SetStatus(StudyData data, string courseId, CourseStatus status)
    {
        ArgumentNullException.ThrowIfNull(data);
        var course = FindCourse(data, courseId);
        if (status == CourseStatus.Archived)
        {
            Archive(data, courseId);
            return;
        }

        course.Status = status;
        course.Completed = status == CourseStatus.Passed;
        course.UpdatedAt = DateTime.UtcNow;
    }

    public void Archive(StudyData data, string courseId)
    {
        var course = FindCourse(data, courseId);
        // Keep the academic status intact. Archived is an independent flag, so
        // restoring a course never has to guess whether it was prepared,
        // scheduled, failed, passed or still not started.
        course.Archived = true;
        course.UpdatedAt = DateTime.UtcNow;
    }

    public void Restore(StudyData data, string courseId)
    {
        var course = FindCourse(data, courseId);
        course.Archived = false;
        // Compatibility for records archived by older builds, which overwrote
        // Status with Archived and therefore did not retain the previous value.
        if (course.Status == CourseStatus.Archived)
            course.Status = course.Completed ? CourseStatus.Passed : CourseStatus.InProgress;
        course.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Builds a stable, non-mutating preview of every record that would be
    /// removed or detached with the course.
    /// </summary>
    public CourseRemovalImpact GetCourseRemovalImpact(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return BuildCourseRemovalPlan(data, courseId).Impact;
    }

    /// <summary>
    /// Deletes a course only after separate confirmation for academic history,
    /// Study Lab history and calendar-event detachment. All constraints are
    /// checked before the first collection is changed.
    /// </summary>
    public void RemoveCourse(
        StudyData data,
        string courseId,
        bool removeCareerHistory = false,
        bool removeStudyHistory = false,
        bool detachCalendarEvents = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var plan = BuildCourseRemovalPlan(data, courseId);
        var impact = plan.Impact;

        if (impact.HasActiveStudySession)
            throw new InvalidOperationException("Finish or cancel the course focus session before deleting the course.");
        if (impact.HasActivePomodoro)
            throw new InvalidOperationException("Complete or reset the course Pomodoro timer before deleting the course.");
        if (impact.HasAcademicHistory && !removeCareerHistory)
            throw new InvalidOperationException(
                "The course has grades, exams, syllabus or prerequisite links. Review the removal impact and explicitly confirm academic-history deletion.");
        if (impact.HasStudyHistory && !removeStudyHistory)
            throw new InvalidOperationException(
                "The course has Study Lab records. Review the removal impact and explicitly confirm Study Lab history deletion.");
        if (impact.CalendarEventsToDetach > 0 && !detachCalendarEvents)
            throw new InvalidOperationException(
                "The course has calendar events. Review the removal impact and explicitly confirm that their course link may be detached.");

        // No mutation occurs until all three confirmation gates and active-work
        // checks have passed. Remove child records before their parents.
        foreach (var grade in plan.Grades) data.Grades.Remove(grade);
        foreach (var exam in plan.PlannedExams) data.Exams.Remove(exam);

        if (removeCareerHistory)
        {
            foreach (var careerExam in plan.CareerExams) data.CareerExams.Remove(careerExam);
            foreach (var topic in plan.SyllabusTopics) data.SyllabusTopics.Remove(topic);
            foreach (var prerequisite in plan.PrerequisiteLinks) data.CoursePrerequisites.Remove(prerequisite);
        }

        if (removeStudyHistory)
        {
            foreach (var revision in plan.ActivityRevisions) data.ActivityRevisions.Remove(revision);
            foreach (var review in plan.FlashcardReviews) data.FlashcardReviews.Remove(review);
            foreach (var activity in plan.StudyActivities) data.StudyActivities.Remove(activity);
            foreach (var distraction in plan.Distractions) data.Distractions.Remove(distraction);
            foreach (var cycle in plan.PomodoroCycles) data.PomodoroCycles.Remove(cycle);
            foreach (var question in plan.Questions) data.Questions.Remove(question);
            foreach (var card in plan.Flashcards) data.Flashcards.Remove(card);
            foreach (var entry in plan.ErrorJournalEntries) data.ErrorJournalEntries.Remove(entry);
            foreach (var document in plan.PdfDocuments) data.PdfDocuments.Remove(document);
            foreach (var mission in plan.StudyMissions) data.StudyMissions.Remove(mission);
            foreach (var task in plan.StudyTasks) data.StudyTasks.Remove(task);
            foreach (var session in plan.StudySessions) data.StudySessions.Remove(session);

            if (data.PomodoroTimer is { } timer && plan.SessionIds.Contains(timer.SessionId))
                data.PomodoroTimer = null;
        }

        if (detachCalendarEvents)
        {
            foreach (var calendarEvent in plan.CalendarEvents)
            {
                if (string.IsNullOrWhiteSpace(calendarEvent.CourseName))
                    calendarEvent.CourseName = plan.Course.Name;
                calendarEvent.CourseId = null;
                calendarEvent.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        data.Courses.Remove(plan.Course);
    }

    private static CourseRemovalPlan BuildCourseRemovalPlan(StudyData data, string courseId)
    {
        var course = FindCourse(data, courseId);
        var grades = data.Grades.Where(x => x.CourseId == courseId).ToArray();
        var plannedExams = data.Exams.Where(x => x.CourseId == courseId).ToArray();
        var careerExams = data.CareerExams.Where(x => x.CourseId == courseId).ToArray();
        var syllabusTopics = data.SyllabusTopics.Where(x => x.CourseId == courseId).ToArray();
        var topicIds = syllabusTopics.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var prerequisiteLinks = data.CoursePrerequisites
            .Where(x => x.CourseId == courseId || x.PrerequisiteCourseId == courseId).ToArray();

        var studySessions = data.StudySessions.Where(x => x.CourseId == courseId).ToArray();
        var sessionIds = studySessions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var studyTasks = data.StudyTasks.Where(x => x.CourseId == courseId).ToArray();
        var taskIds = studyTasks.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var studyMissions = data.StudyMissions
            .Where(x => x.CourseId == courseId || topicIds.Contains(x.SyllabusTopicId)).ToArray();
        var missionIds = studyMissions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var examIds = plannedExams.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        var questions = data.Questions
            .Where(x => x.CourseId == courseId || topicIds.Contains(x.SyllabusTopicId)).ToArray();
        var flashcards = data.Flashcards
            .Where(x => x.CourseId == courseId || topicIds.Contains(x.SyllabusTopicId)).ToArray();
        var flashcardIds = flashcards.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var flashcardReviews = data.FlashcardReviews
            .Where(x => flashcardIds.Contains(x.FlashcardId)).ToArray();
        var errorJournalEntries = data.ErrorJournalEntries
            .Where(x => x.CourseId == courseId || topicIds.Contains(x.SyllabusTopicId)).ToArray();
        var pdfDocuments = data.PdfDocuments.Where(x => x.CourseId == courseId).ToArray();
        var calendarEvents = data.CalendarEvents.Where(x => x.CourseId == courseId).ToArray();

        // Activities may omit CourseId and still be dependent through a topic,
        // task, mission, planned exam or focus session. Include the full closure.
        var studyActivities = data.StudyActivities
            .Where(x => x.CourseId == courseId ||
                        topicIds.Contains(x.SyllabusTopicId) ||
                        taskIds.Contains(x.TaskId) ||
                        missionIds.Contains(x.MissionId) ||
                        examIds.Contains(x.ExamId) ||
                        sessionIds.Contains(x.SessionId))
            .ToArray();
        var activityIds = studyActivities.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var activityRevisions = data.ActivityRevisions
            .Where(x => activityIds.Contains(x.ActivityId)).ToArray();
        var distractions = data.Distractions
            .Where(x => sessionIds.Contains(x.SessionId ?? string.Empty)).ToArray();
        var pomodoroCycles = data.PomodoroCycles
            .Where(x => sessionIds.Contains(x.SessionId)).ToArray();

        var hasActiveStudySession = studySessions.Any(x =>
            x.State is StudySessionState.Running or StudySessionState.Paused);
        var hasActivePomodoro = data.PomodoroTimer is { } timer &&
            (timer.Status is PomodoroTimerStatus.Running or PomodoroTimerStatus.Paused) &&
            sessionIds.Contains(timer.SessionId);

        var impact = new CourseRemovalImpact(
            GradeRecords: grades.Length,
            PlannedExams: plannedExams.Length,
            CareerExams: careerExams.Length,
            CareerExamAttempts: careerExams.Sum(x => x.Attempts?.Count ?? 0),
            SyllabusTopics: syllabusTopics.Length,
            PrerequisiteLinks: prerequisiteLinks.Length,
            Questions: questions.Length,
            StudySessions: studySessions.Length,
            StudySessionSegments: studySessions.Sum(x => x.Segments?.Count ?? 0),
            StudyTasks: studyTasks.Length,
            StudyActivities: studyActivities.Length,
            StudyMissions: studyMissions.Length,
            MissionSteps: studyMissions.Sum(x => x.Steps?.Count ?? 0),
            Flashcards: flashcards.Length,
            FlashcardReviews: flashcardReviews.Length,
            ErrorJournalEntries: errorJournalEntries.Length,
            Distractions: distractions.Length,
            PomodoroCycles: pomodoroCycles.Length,
            PdfDocuments: pdfDocuments.Length,
            PdfPages: pdfDocuments.Sum(x => x.Pages?.Count ?? 0),
            CalendarEventsToDetach: calendarEvents.Length,
            ActivityRevisions: activityRevisions.Length,
            HasActiveStudySession: hasActiveStudySession,
            HasActivePomodoro: hasActivePomodoro);

        return new CourseRemovalPlan(
            course, grades, plannedExams, careerExams, syllabusTopics,
            prerequisiteLinks, questions, studySessions, studyTasks,
            studyActivities, studyMissions, flashcards, flashcardReviews,
            errorJournalEntries, distractions, pomodoroCycles, pdfDocuments,
            calendarEvents, activityRevisions, sessionIds, impact);
    }

    private sealed record CourseRemovalPlan(
        Course Course,
        IReadOnlyList<Grade> Grades,
        IReadOnlyList<Exam> PlannedExams,
        IReadOnlyList<CareerExamData> CareerExams,
        IReadOnlyList<SyllabusTopicData> SyllabusTopics,
        IReadOnlyList<CoursePrerequisiteData> PrerequisiteLinks,
        IReadOnlyList<QuestionBankItemData> Questions,
        IReadOnlyList<StudySessionData> StudySessions,
        IReadOnlyList<StudyTaskData> StudyTasks,
        IReadOnlyList<StudyActivityData> StudyActivities,
        IReadOnlyList<StudyMissionData> StudyMissions,
        IReadOnlyList<FlashcardData> Flashcards,
        IReadOnlyList<FlashcardReviewData> FlashcardReviews,
        IReadOnlyList<ErrorJournalEntryData> ErrorJournalEntries,
        IReadOnlyList<DistractionData> Distractions,
        IReadOnlyList<PomodoroCycleData> PomodoroCycles,
        IReadOnlyList<PdfDocumentData> PdfDocuments,
        IReadOnlyList<CalendarEvent> CalendarEvents,
        IReadOnlyList<ActivityRevisionData> ActivityRevisions,
        HashSet<string> SessionIds,
        CourseRemovalImpact Impact);

    private static Course FindCourse(StudyData data, string courseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId);
        return data.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
    }

    private static void ValidateCourse(Course course, StudyData data, string? existingId = null)
    {
        if (string.IsNullOrWhiteSpace(course.Name))
            throw new ArgumentException("Course name is required.", nameof(course));
        if (course.Cfu < 0 || course.Cfu > 60)
            throw new ArgumentOutOfRangeException(nameof(course), "Course CFU must be between 0 and 60.");
        if (course.EstimatedStudyHours is < 0 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(course), "Estimated study hours are outside the supported range.");
        if (course.Cfu > 0 && data.DegreeTotalCfu > 0 &&
            data.Courses.Where(x => !string.Equals(x.Id, existingId, StringComparison.Ordinal)).Sum(x => x.Cfu) + course.Cfu > data.DegreeTotalCfu)
            throw new InvalidOperationException("Course CFU exceed the configured degree total.");
        if (!string.IsNullOrWhiteSpace(course.Code) &&
            (course.Code.Length > 40 || course.Code.Any(char.IsControl)))
            throw new ArgumentException("Course code is invalid.", nameof(course));
        if ((course.SyllabusUrl?.Length ?? 0) > 2048 || (course.Source?.Length ?? 0) > 40)
            throw new ArgumentException("Course metadata contains an invalid value.", nameof(course));
    }

    private static void ApplyCourse(Course course)
    {
        course.Name = course.Name.Trim();
        course.Code = course.Code?.Trim() ?? string.Empty;
        course.Teacher = course.Teacher?.Trim() ?? string.Empty;
        course.AcademicYear = course.AcademicYear?.Trim() ?? string.Empty;
        course.Session = course.Session?.Trim() ?? string.Empty;
        course.SyllabusSummary = course.SyllabusSummary?.Trim() ?? string.Empty;
        course.SyllabusUrl = course.SyllabusUrl?.Trim() ?? string.Empty;
        course.Notes = course.Notes?.Trim() ?? string.Empty;
        course.Source = string.IsNullOrWhiteSpace(course.Source) ? "manual" : course.Source.Trim();
        if (course.Completed && course.Status is CourseStatus.NotStarted or CourseStatus.InProgress or CourseStatus.Prepared or CourseStatus.ExamScheduled)
            course.Status = CourseStatus.Passed;
        if (course.Status == CourseStatus.Passed) course.Completed = true;
        if (course.Status == CourseStatus.Failed) course.Completed = false;
        if (course.Status == CourseStatus.Archived) course.Archived = true;
        course.UpdatedAt ??= DateTime.UtcNow;
    }
}
