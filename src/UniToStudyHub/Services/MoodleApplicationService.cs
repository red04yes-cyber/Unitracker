using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Application-level Moodle flow. Moodle stays optional: with no stored token
/// every operation fails before a network request is created, so a stale token
/// can never be reused after <see cref="DisconnectAsync"/>.
/// </summary>
public sealed class MoodleApplicationService
{
    private readonly IMoodleTokenStore _tokens;

    public MoodleApplicationService() : this(MoodleTokenVault.Default)
    {
    }

    public MoodleApplicationService(IMoodleTokenStore tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public bool IsConnected => _tokens.HasToken;

    public async Task<string> VerifyAndStoreTokenAsync(
        string baseUrl,
        string token,
        CancellationToken cancellationToken = default)
    {
        using var client = new MoodleClient(baseUrl, token);
        var site = await client.GetSiteInfoAsync(cancellationToken).ConfigureAwait(false);
        _tokens.Save(token);
        AppDiagnostics.Info("moodle", "Connessione Moodle verificata per " +
                                       OutboundEndpointPolicy.Describe(client.BaseUri));
        return site.fullname;
    }

    public async Task<IReadOnlyList<MoodleCourse>> GetEnrolledCoursesAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        using var client = new MoodleClient(baseUrl, token);
        var site = await client.GetSiteInfoAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetEnrolledCoursesAsync(site.userid, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MoodleCalendarResult> GetCalendarEventsAsync(
        string baseUrl,
        IEnumerable<int> courseIds,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        using var client = new MoodleClient(baseUrl, token);
        return await client.GetCalendarEventsAsync(courseIds, from, to, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MoodleCourseSection>> GetCourseContentsAsync(
        string baseUrl,
        int courseId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        using var client = new MoodleClient(baseUrl, token);
        return await client.GetCourseContentsAsync(courseId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MoodleForumDiscussion>> GetForumDiscussionsAsync(
        string baseUrl,
        int forumId,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        using var client = new MoodleClient(baseUrl, token);
        const int pageSize = 25;
        const int maximumPages = 20;
        var discussions = new List<MoodleForumDiscussion>();
        for (var page = 0; page < maximumPages; page++)
        {
            var batch = await client.GetForumDiscussionsPageAsync(forumId, page, pageSize, cancellationToken)
                .ConfigureAwait(false);
            discussions.AddRange(batch);
            if (batch.Count < pageSize) break;
        }
        return discussions;
    }

    /// <summary>
    /// Removes the stored token (and its legacy file). The academic data the
    /// user already imported is intentionally preserved: deleting courses and
    /// grades is a separate, explicit data-deletion action.
    /// </summary>
    public Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tokens.Delete());
    }

    /// <summary>
    /// Drops the local references that only made sense while connected. It is
    /// pure (no persistence) so the caller can save the change atomically and
    /// keep grading history untouched.
    /// </summary>
    public static bool ClearLocalMoodleReferences(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var changed = false;
        if (!string.IsNullOrEmpty(data.MoodleBaseUrl))
        {
            data.MoodleBaseUrl = string.Empty;
            changed = true;
        }
        foreach (var course in data.Courses)
        {
            if (course.MoodleCourseId is null) continue;
            course.MoodleCourseId = null;
            changed = true;
        }
        return changed;
    }

    private string RequireToken()
    {
        var token = _tokens.Load();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Moodle non è collegato. Verifica e salva un token prima di sincronizzare.");
        return token;
    }
}
