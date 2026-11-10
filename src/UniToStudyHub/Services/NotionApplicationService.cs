using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record NotionConnectionPreview(
    NotionDataSourceInfo DataSource,
    IReadOnlyList<NotionCourseRecord> Courses,
    DateTimeOffset RetrievedAt);

public sealed record NotionCourseImportResult(
    int CreatedCourses,
    int UpdatedCourses,
    int AddedGrades,
    int SkippedReferences,
    int SkippedInvalidRecords,
    IReadOnlyList<string> Warnings);

/// <summary>
/// User-driven Notion connection and import orchestration. Connecting only
/// validates access and builds a preview; local data changes happen only when
/// the caller explicitly invokes MergeCourses after user confirmation.
/// </summary>
public sealed class NotionApplicationService
{
    private readonly INotionTokenStore _tokens;
    private readonly NotionApiClient _api;

    public NotionApplicationService(INotionTokenStore tokens, NotionApiClient api)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsConnected => _tokens.HasToken;

    public async Task<NotionConnectionPreview> ConnectAndPreviewAsync(
        string token,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedToken = NotionTokenVault.Normalize(token);
        var dataSource = await _api.GetDataSourceAsync(normalizedToken, dataSourceId, cancellationToken)
            .ConfigureAwait(false);
        if (!dataSource.Properties.TryGetValue("Esame", out var titleType) ||
            !string.Equals(titleType, "title", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Il database selezionato non contiene la proprietà titolo 'Esame' attesa dal registro corsi.");

        var courses = await _api.QueryCourseRecordsAsync(normalizedToken, dataSourceId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _tokens.Save(normalizedToken);

        AppDiagnostics.Info("notion", $"Connessione verificata: {courses.Count} record letti dal data source.");
        return new NotionConnectionPreview(dataSource, courses, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<NotionCourseRecord>> PreviewCoursesAsync(
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        return await _api.QueryCourseRecordsAsync(token, dataSourceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NotionPageContent> GetCoursePageContentAsync(
        string pageId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        return await _api.GetPageContentAsync(token, pageId, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tokens.Delete());
    }

    /// <summary>
    /// Merges previewed Notion records into the existing StudyData. Existing
    /// local values are preserved; Notion only fills missing metadata. A later
    /// sync never overwrites non-empty local values. No exam date is
    /// copied into the calendar because the legacy Course model cannot retain
    /// official-source and verification provenance.
    /// </summary>
    public static NotionCourseImportResult MergeCourses(
        StudyData data,
        IEnumerable<NotionCourseRecord> records)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(records);

        var created = 0;
        var updated = 0;
        var addedGrades = 0;
        var skippedReferences = 0;
        var skippedInvalid = 0;
        var warnings = new List<string>();
        var seenPageIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.PageId) ||
                string.IsNullOrWhiteSpace(record.Name) || record.Cfu is < 0 or > 1000)
            {
                skippedInvalid++;
                continue;
            }

            if (!seenPageIds.Add(record.PageId))
            {
                warnings.Add($"Record Notion duplicato ignorato: {record.PageId}.");
                continue;
            }

            if (record.IsReference)
            {
                skippedReferences++;
                continue;
            }

            var sourceKey = SourceKey(record.PageId);
            var course = data.Courses.FirstOrDefault(x =>
                string.Equals(x.Source, sourceKey, StringComparison.Ordinal));
            var createdNew = false;
            var linkedExisting = false;

            if (course is null && !string.IsNullOrWhiteSpace(record.Code))
            {
                var matches = data.Courses.Where(x =>
                    string.Equals(x.Code, record.Code, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (matches.Count == 1) course = matches[0];
            }

            if (course is null)
            {
                var matches = data.Courses.Where(x =>
                    string.Equals(x.Name, record.Name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (matches.Count == 1) course = matches[0];
            }

            if (course is null)
            {
                course = new Course
                {
                    Id = DeterministicCourseId(record.PageId),
                    Name = record.Name.Trim(),
                    Source = sourceKey
                };
                data.Courses.Add(course);
                created++;
                createdNew = true;
            }
            else if (!string.Equals(course.Source, sourceKey, StringComparison.Ordinal))
            {
                // A unique code/name match links the existing local course to
                // Notion while preserving all non-empty user-entered values.
                course.Source = sourceKey;
                linkedExisting = true;
            }

            var changed = createdNew || linkedExisting;
            changed |= MergeText(course.Name, record.Name, value => course.Name = value);
            changed |= MergeText(course.Code, record.Code, value => course.Code = value);
            changed |= MergeNumber(course.Cfu, record.Cfu, value => course.Cfu = value);
            changed |= MergeText(course.Teacher, record.Teacher, value => course.Teacher = value);
            changed |= MergeText(course.AcademicYear, record.AcademicYear, value => course.AcademicYear = value);
            changed |= MergeText(course.Session,
                record.TargetSessions.Count == 0 ? null : string.Join(", ", record.TargetSessions),
                value => course.Session = value);
            changed |= MergeNumber(course.EstimatedStudyHours, record.EstimatedStudyHours,
                value => course.EstimatedStudyHours = value);
            changed |= MergeText(course.SyllabusUrl, record.OfficialCourseUrl, value => course.SyllabusUrl = value);

            var importedStatus = MapStatus(record.CareerStatus);
            if (importedStatus == CourseStatus.Passed)
            {
                if (course.Status != CourseStatus.Passed || !course.Completed)
                {
                    course.Status = CourseStatus.Passed;
                    course.Completed = true;
                    changed = true;
                }
            }
            else if (!course.Archived && importedStatus is not null && course.Status != CourseStatus.Passed &&
                     StatusRank(importedStatus.Value) > StatusRank(course.Status))
            {
                course.Status = importedStatus.Value;
                changed = true;
            }

            if (record.Grade is { } grade)
            {
                if (grade is < 18 or > 30 || record.Cfu is null or <= 0)
                {
                    warnings.Add($"Voto o CFU non validi per '{record.Name}': il voto non è stato importato.");
                }
                else if (!HasGrade(data, course.Id, grade))
                {
                    data.Grades.Add(new Grade
                    {
                        Id = DeterministicGradeId(record.PageId, grade),
                        CourseId = course.Id,
                        CourseName = course.Name,
                        Value = grade,
                        Cfu = record.Cfu.Value,
                        // The Notion register does not expose an exam date for
                        // these marks. UnixEpoch is the existing unknown-date
                        // sentinel understood by the career migration.
                        Date = DateTime.UnixEpoch,
                        CountsTowardAverage = true
                    });
                    addedGrades++;
                    changed = true;
                }
            }

            if (record.ExamDate is not null)
                warnings.Add($"Data appello di '{record.Name}' lasciata nell'anteprima: il modello locale non conserva ancora fonte e verifica.");

            if (changed)
            {
                course.UpdatedAt = record.LastEditedAt?.UtcDateTime ?? DateTime.UtcNow;
                if (!createdNew) updated++;
            }
        }

        return new NotionCourseImportResult(created, updated, addedGrades, skippedReferences, skippedInvalid,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private string RequireToken()
    {
        var token = _tokens.Load();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Notion non è collegato. Inserisci e verifica il token dell'integrazione prima di sincronizzare.");
        return token;
    }

    private static bool HasGrade(StudyData data, string courseId, int grade) =>
        data.Grades.Any(x => x.CourseId == courseId && x.Value == grade) ||
        data.CareerExams.Any(x => x.CourseId == courseId &&
            x.Attempts.Any(attempt => attempt.NumericGrade == grade &&
                                      string.Equals(attempt.Outcome, nameof(ExamOutcome.Passed),
                                          StringComparison.Ordinal)));

    private static CourseStatus? MapStatus(string? status) => status switch
    {
        "Superato" => CourseStatus.Passed,
        "Frequenza attribuita" or "In corso" => CourseStatus.InProgress,
        "Da iniziare" or "Da verificare" or "Opzionale" => CourseStatus.NotStarted,
        _ => null
    };

    private static string SourceKey(string pageId) =>
        "notion:" + pageId.Replace("-", string.Empty, StringComparison.Ordinal);

    private static string DeterministicCourseId(string pageId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("notion-course:" + pageId));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static string DeterministicGradeId(string pageId, int grade)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"notion-grade:{pageId}:{grade}"));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static int StatusRank(CourseStatus status) => status switch
    {
        CourseStatus.NotStarted => 0,
        CourseStatus.InProgress => 1,
        CourseStatus.Prepared => 2,
        CourseStatus.ExamScheduled or CourseStatus.Failed => 3,
        CourseStatus.Passed => 4,
        _ => 0
    };

    private static bool MergeText(string target, string? incoming, Action<string> assign)
    {
        if (string.IsNullOrWhiteSpace(incoming) || !string.IsNullOrWhiteSpace(target)) return false;
        var normalized = incoming.Trim();
        if (string.Equals(target, normalized, StringComparison.Ordinal)) return false;
        assign(normalized);
        return true;
    }

    private static bool MergeNumber(int target, int? incoming, Action<int> assign)
    {
        if (incoming is null || incoming < 0 || target != 0) return false;
        if (target == incoming.Value) return false;
        assign(incoming.Value);
        return true;
    }
}
