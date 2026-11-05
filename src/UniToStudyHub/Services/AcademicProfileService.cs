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
    /// Deletes a course only together with its dependent records. Career history
    /// is deliberately not silently discarded: callers must first archive or
    /// explicitly remove it through a dedicated confirmation flow.
    /// </summary>
    public void RemoveCourse(StudyData data, string courseId, bool removeCareerHistory = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var course = FindCourse(data, courseId);
        // Validate every deletion constraint before mutating any collection.
        // Callers may catch this exception and continue editing the same
        // snapshot; a rejected deletion must never leave grades/exams removed.
        if (!removeCareerHistory)
        {
            if (data.CareerExams.Any(x => x.CourseId == courseId))
                throw new InvalidOperationException("The course has exam history and cannot be deleted without explicit confirmation.");
            if (data.SyllabusTopics.Any(x => x.CourseId == courseId))
                throw new InvalidOperationException("The course has syllabus data and cannot be deleted without explicit confirmation.");
        }

        var gradeIds = data.Grades.Where(x => x.CourseId == courseId).Select(x => x.Id).ToList();
        foreach (var grade in data.Grades.Where(x => gradeIds.Contains(x.Id)).ToList()) data.Grades.Remove(grade);
        foreach (var exam in data.Exams.Where(x => x.CourseId == courseId).ToList()) data.Exams.Remove(exam);
        if (removeCareerHistory)
        {
            foreach (var careerExam in data.CareerExams.Where(x => x.CourseId == courseId).ToList())
                data.CareerExams.Remove(careerExam);
            foreach (var topic in data.SyllabusTopics.Where(x => x.CourseId == courseId).ToList())
                data.SyllabusTopics.Remove(topic);
        }

        foreach (var prerequisite in data.CoursePrerequisites
                     .Where(x => x.CourseId == courseId || x.PrerequisiteCourseId == courseId).ToList())
            data.CoursePrerequisites.Remove(prerequisite);
        foreach (var question in data.Questions.Where(x => x.CourseId == courseId).ToList())
            data.Questions.Remove(question);
        data.Courses.Remove(course);
    }

    private static Course FindCourse(StudyData data, string courseId) =>
        data.Courses.FirstOrDefault(x => string.Equals(x.Id, courseId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));

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
