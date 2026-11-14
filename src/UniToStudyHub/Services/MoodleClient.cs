using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Minimal Moodle Web Services client for an instance the user is authorised to
/// use.
///
/// REST requests are POSTed (the token travels in the form body). The Moodle
/// file-download endpoint is the protocol exception: its single GET carries
/// <c>token=</c> in the query. Downloads are restricted to the same origin,
/// the default HTTP handler does not follow redirects, responses are size-bounded and files are
/// written atomically. No method logs the token or the tokenised URL.
/// The base URL is validated against <see cref="OutboundEndpointPolicy"/>.
/// </summary>
public sealed class MoodleClient : IDisposable
{
    public const long MaximumDownloadBytes = 250L * 1024 * 1024;
    public const int MaximumApiResponseBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
    private const int CopyBufferBytes = 81920;

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _token;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public MoodleClient(
        string baseUrl,
        string token,
        HttpMessageHandler? handler = null,
        bool allowLoopbackHttp = false)
    {
        var endpoint = OutboundEndpointPolicy.ValidateBaseUrl(baseUrl, allowLoopbackHttp, nameof(baseUrl));
        _token = MoodleTokenVault.Normalize(token);
        _baseUri = endpoint.BaseUri;
        var effectiveHandler = handler ?? new HttpClientHandler { AllowAutoRedirect = false };
        _http = new HttpClient(effectiveHandler, disposeHandler: true) { Timeout = RequestTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("UniToStudyHub/0.2");
    }

    /// <summary>Normalised, credential-free base endpoint (safe to display/log).</summary>
    public Uri BaseUri => _baseUri;

    public Task<MoodleSiteInfo> GetSiteInfoAsync(CancellationToken ct = default) =>
        CallAsync<MoodleSiteInfo>("core_webservice_get_site_info", new Dictionary<string, string>(), ct);

    public Task<List<MoodleCourse>> GetEnrolledCoursesAsync(int id, CancellationToken ct = default) =>
        CallAsync<List<MoodleCourse>>("core_enrol_get_users_courses",
            new Dictionary<string, string> { { "userid", id.ToString() } }, ct);

    public Task<List<MoodleCourseSection>> GetCourseContentsAsync(int id, CancellationToken ct = default) =>
        CallAsync<List<MoodleCourseSection>>("core_course_get_contents",
            new Dictionary<string, string> { { "courseid", id.ToString() } }, ct);

    /// <summary>
    /// Reads metadata for every built-in Moodle activity family whose web
    /// service is enabled for this token. Unsupported/disabled functions are
    /// skipped rather than making the entire course sync fail.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>> GetCourseActivityDetailsAsync(
        int courseId,
        IEnumerable<string> availableFunctions,
        CancellationToken ct = default)
    {
        var available = new HashSet<string>(availableFunctions ?? Array.Empty<string>(), StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var candidates = CourseActivityFunctions
            .Concat(available.Where(name => name.StartsWith("mod_", StringComparison.Ordinal) &&
                                            name.EndsWith("_by_courses", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(100);
        foreach (var function in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!available.Contains(function)) continue;
            try
            {
                var payload = await CallAsync<JsonElement>(function,
                    new Dictionary<string, string> { { "courseids[0]", courseId.ToString() } }, ct)
                    .ConfigureAwait(false);
                if (payload.ValueKind != JsonValueKind.Null &&
                    !(payload.ValueKind == JsonValueKind.Array && payload.GetArrayLength() == 0) &&
                    !(payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().Any()))
                    result[function] = payload;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException)
            {
                AppDiagnostics.Warning("moodle", $"Dettagli attività non disponibili: {function}.", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// Reads user-specific attempts/submissions for common interactive activity
    /// modules. Each call is guarded by the function list granted to this token;
    /// the Moodle server remains the authority for access to the user's data.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>> GetModuleUserActivityDataAsync(
        IEnumerable<MoodleCourseSection> sections,
        int userId,
        IEnumerable<string> availableFunctions,
        CancellationToken ct = default)
    {
        var available = new HashSet<string>(availableFunctions ?? Array.Empty<string>(), StringComparer.Ordinal);
        var results = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var module in (sections ?? Array.Empty<MoodleCourseSection>())
                     .Where(section => section.modules is not null)
                     .SelectMany(section => section.modules)
                     .Where(module => module.instance > 0)
                     .Take(100))
        {
            ct.ThrowIfCancellationRequested();
            string? function = null;
            var parameters = new Dictionary<string, string>();
            switch (module.modname.ToLowerInvariant())
            {
                case "assign":
                    function = "mod_assign_get_submission_status";
                    parameters["assignid"] = module.instance.ToString();
                    parameters["userid"] = userId.ToString();
                    break;
                case "quiz":
                    function = "mod_quiz_get_user_attempts";
                    parameters["quizid"] = module.instance.ToString();
                    parameters["userid"] = userId.ToString();
                    parameters["status"] = "all";
                    parameters["includepreviews"] = "0";
                    break;
                case "lesson":
                    function = "mod_lesson_get_user_attempts";
                    parameters["lessonid"] = module.instance.ToString();
                    parameters["userid"] = userId.ToString();
                    break;
                case "workshop":
                    function = "mod_workshop_get_user_assessments";
                    parameters["workshopid"] = module.instance.ToString();
                    parameters["userid"] = userId.ToString();
                    break;
                case "scorm":
                    function = "mod_scorm_get_scorm_scoes_track";
                    parameters["scormid"] = module.instance.ToString();
                    parameters["userid"] = userId.ToString();
                    parameters["attempt"] = "1";
                    break;
                case "wiki":
                    function = "mod_wiki_get_subwiki_pages";
                    parameters["wikiid"] = module.instance.ToString();
                    parameters["groupid"] = "0";
                    parameters["userid"] = userId.ToString();
                    break;
                case "data":
                    function = "mod_data_get_entries";
                    parameters["databaseid"] = module.instance.ToString();
                    parameters["groupid"] = "0";
                    parameters["returncontents"] = "1";
                    parameters["page"] = "0";
                    parameters["perpage"] = "100";
                    break;
            }

            if (function is null || !available.Contains(function)) continue;
            try
            {
                var payload = await CallAsync<JsonElement>(function, parameters, ct).ConfigureAwait(false);
                if (payload.ValueKind != JsonValueKind.Null &&
                    !(payload.ValueKind == JsonValueKind.Array && payload.GetArrayLength() == 0) &&
                    !(payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().Any()))
                    results[$"{module.modname}:{module.id}:{function}"] = payload;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException)
            {
                AppDiagnostics.Warning("moodle", $"Dati personali attività non disponibili: {module.modname} #{module.id}.", ex);
            }
        }
        return results;
    }

    /// <summary>Extracts all file descriptors embedded in enabled activity API responses.</summary>
    public static IReadOnlyList<MoodleContentFile> ExtractActivityFiles(
        IEnumerable<JsonElement> activityResponses)
    {
        var files = new Dictionary<string, MoodleContentFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var response in activityResponses ?? Array.Empty<JsonElement>())
            CollectActivityFiles(response, files);
        return files.Values.ToList();
    }

    private static void CollectActivityFiles(JsonElement element, IDictionary<string, MoodleContentFile> files)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("fileurl", out var url) &&
                url.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(url.GetString()) &&
                element.TryGetProperty("filename", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(name.GetString()))
            {
                var file = new MoodleContentFile
                {
                    type = "file",
                    fileurl = url.GetString()!,
                    filename = name.GetString()!,
                    filepath = element.TryGetProperty("filepath", out var path) ? path.GetString() ?? string.Empty : string.Empty,
                    mimetype = element.TryGetProperty("mimetype", out var mime) ? mime.GetString() ?? string.Empty : string.Empty,
                    filesize = element.TryGetProperty("filesize", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                    timemodified = element.TryGetProperty("timemodified", out var modified) && modified.TryGetInt32(out var timestamp) ? timestamp : 0
                };
                files.TryAdd(file.fileurl, file);
            }

            foreach (var property in element.EnumerateObject())
                CollectActivityFiles(property.Value, files);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CollectActivityFiles(child, files);
        }
    }

    public async Task<IReadOnlyList<MoodleActivityCompletionStatus>> GetActivityCompletionAsync(
        int courseId,
        int userId,
        IEnumerable<string> availableFunctions,
        CancellationToken ct = default)
    {
        if (!availableFunctions.Contains("core_completion_get_activities_completion_status", StringComparer.Ordinal))
            return Array.Empty<MoodleActivityCompletionStatus>();
        try
        {
            var result = await CallAsync<MoodleActivityCompletionResult>(
                "core_completion_get_activities_completion_status",
                new Dictionary<string, string>
                {
                    { "courseid", courseId.ToString() },
                    { "userid", userId.ToString() }
                }, ct).ConfigureAwait(false);
            return result.statuses ?? new List<MoodleActivityCompletionStatus>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException)
        {
            AppDiagnostics.Warning("moodle", $"Completamento attività non disponibile per il corso {courseId}.", ex);
            return Array.Empty<MoodleActivityCompletionStatus>();
        }
    }

    private static readonly string[] CourseActivityFunctions =
    {
        "mod_assign_get_assignments", "mod_quiz_get_quizzes_by_courses",
        "mod_choice_get_choices_by_courses", "mod_feedback_get_feedbacks_by_courses",
        "mod_lesson_get_lessons_by_courses", "mod_workshop_get_workshops_by_courses",
        "mod_glossary_get_glossaries_by_courses", "mod_data_get_databases_by_courses",
        "mod_wiki_get_wikis_by_courses", "mod_scorm_get_scorms_by_courses",
        "mod_h5pactivity_get_h5pactivities_by_courses", "mod_bigbluebuttonbn_get_bigbluebuttonbns_by_courses",
        "mod_lti_get_ltis_by_courses", "mod_resource_get_resources_by_courses",
        "mod_page_get_pages_by_courses", "mod_folder_get_folders_by_courses",
        "mod_url_get_urls_by_courses", "mod_book_get_books_by_courses",
        "mod_chat_get_chats_by_courses", "mod_survey_get_surveys_by_courses",
        "mod_imscp_get_imscps_by_courses", "mod_forum_get_forums_by_courses"
    };

    public Task<MoodleCalendarResult> GetCalendarEventsAsync(
        IEnumerable<int> ids,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>
        {
            { "options[userevents]", "1" },
            { "options[siteevents]", "1" },
            { "options[timestart]", new DateTimeOffset(from).ToUnixTimeSeconds().ToString() },
            { "options[timeend]", new DateTimeOffset(to).ToUnixTimeSeconds().ToString() },
            { "options[ignorehidden]", "1" }
        };
        var index = 0;
        foreach (var id in ids.Distinct()) parameters[$"events[courseids][{index++}]"] = id.ToString();
        return CallAsync<MoodleCalendarResult>("core_calendar_get_calendar_events", parameters, ct);
    }

    public Task<List<MoodleForumDiscussion>> GetForumDiscussionsAsync(int id, CancellationToken ct = default) =>
        GetForumDiscussionsPageAsync(id, 0, 25, ct);

    public async Task<List<MoodleForumDiscussion>> GetForumDiscussionsPageAsync(
        int id,
        int page,
        int perPage = 25,
        CancellationToken ct = default)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (perPage is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(perPage));
        var result = await CallAsync<MoodleForumDiscussionsResult>("mod_forum_get_forum_discussions",
            new Dictionary<string, string>
            {
                { "forumid", id.ToString() },
                { "sortby", "timemodified" },
                { "sortdirection", "DESC" },
                { "page", page.ToString() },
                { "perpage", perPage.ToString() },
                { "groupid", "-1" }
            }, ct);
        return result.discussions ?? new List<MoodleForumDiscussion>();
    }

    /// <summary>
    /// Downloads a course file into <paramref name="path"/>. When
    /// <paramref name="allowedDirectory"/> is supplied the destination must
    /// resolve inside it. The file is streamed to a sibling <c>.part</c> file
    /// and only moved into place after a complete, size-checked transfer.
    /// </summary>
    public async Task DownloadFileAsync(
        string url,
        string path,
        CancellationToken ct = default,
        string? allowedDirectory = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var source) || !SameOrigin(source, _baseUri))
            throw new InvalidOperationException("Download bloccato: URL esterno all'istanza Moodle.");
        if (source.Scheme != _baseUri.Scheme)
            throw new InvalidOperationException("Download bloccato: schema non coerente con l'istanza Moodle.");

        var destination = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(allowedDirectory) &&
            !LocalFileSecurity.IsInside(allowedDirectory, destination))
            throw new InvalidOperationException("Download bloccato: destinazione fuori dalla cartella consentita.");

        var tokenised = WithToken(source);
        var directory = Path.GetDirectoryName(destination)!;
        LocalFileSecurity.EnsurePrivateDirectory(directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await _http.GetAsync(tokenised, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Moodle ha risposto con lo stato {(int)response.StatusCode}.");
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Moodle ha restituito una pagina HTML invece del file: verifica permessi e validità del token.");
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidDataException("Il file supera il limite di 250 MB.");

            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             CopyBufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[CopyBufferBytes];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaximumDownloadBytes)
                        throw new InvalidDataException("Il file supera il limite di 250 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
            LocalFileSecurity.TryRestrictFile(destination);
        }
        finally
        {
            LocalFileSecurity.TryDelete(temporary);
        }
    }

    private async Task<T> CallAsync<T>(string function, IDictionary<string, string> parameters, CancellationToken ct)
    {
        var form = new Dictionary<string, string>(parameters)
        {
            { "wstoken", _token },
            { "wsfunction", function },
            { "moodlewsrestformat", "json" }
        };

        using var response = await _http
            .PostAsync(new Uri(_baseUri, "webservice/rest/server.php"), new FormUrlEncodedContent(form), ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Moodle ha risposto con lo stato {(int)response.StatusCode}.");

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrEmpty(mediaType) && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Moodle ha restituito un contenuto non JSON.");

        var body = await ReadBoundedStringAsync(response.Content, MaximumApiResponseBytes, ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("exception", out _))
            {
                var message = document.RootElement.TryGetProperty("message", out var detail)
                    ? SecurityRedaction.ForLog(detail.GetString(), 200)
                    : "errore Web Services";
                throw new InvalidOperationException(
                    $"{function}: il servizio Moodle ha rifiutato la richiesta ({message}). Verifica permessi e token.");
            }

            return document.RootElement.Deserialize<T>(_json)
                   ?? throw new InvalidDataException("Risposta Moodle non valida.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Risposta Moodle non valida.", ex);
        }
    }

    private static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("La risposta del servizio supera il limite consentito.");
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[CopyBufferBytes];
        int read;
        var total = 0;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("La risposta del servizio supera il limite consentito.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private Uri WithToken(Uri source)
    {
        var builder = new UriBuilder(source);
        var parts = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !Uri.UnescapeDataString(part.Split('=', 2)[0])
                .Equals("token", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add("token=" + Uri.EscapeDataString(_token));
        builder.Query = string.Join("&", parts);
        return builder.Uri;
    }

    private static bool SameOrigin(Uri candidate, Uri baseUri) =>
        candidate.Scheme == baseUri.Scheme &&
        string.Equals(candidate.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == baseUri.Port;

    public void Dispose() => _http.Dispose();
}
