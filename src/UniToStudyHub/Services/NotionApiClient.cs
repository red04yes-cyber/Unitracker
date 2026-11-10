using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UniToStudyHub.Services;

public sealed record NotionDataSourceInfo(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Properties);

public sealed record NotionCourseRecord(
    string PageId,
    string PageUrl,
    string Name,
    string? Code,
    int? Cfu,
    string? Teacher,
    string? CareerStatus,
    int? Grade,
    string? AcademicYear,
    string? ExamFormat,
    string? Priority,
    string? PreparationStatus,
    string? SessionRole,
    IReadOnlyList<string> TargetSessions,
    decimal? ProgressPercent,
    int? EstimatedStudyHours,
    DateTimeOffset? ExamDate,
    string? ExamSourceUrl,
    string? MoodleUrl,
    string? OfficialCourseUrl,
    string? Prerequisites,
    bool StudyNow,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastAppealCheckAt,
    DateTimeOffset? LastEditedAt)
{
    public bool IsReference => Name.StartsWith("REFERENCE", StringComparison.OrdinalIgnoreCase);
}

public sealed record NotionContentBlock(
    string Id,
    string Type,
    string Text,
    string? ExternalUrl,
    IReadOnlyList<NotionContentBlock> Children,
    bool IsTemporaryFile = false);

public sealed record NotionPageContent(string PageId, IReadOnlyList<NotionContentBlock> Blocks);

public sealed class NotionApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public NotionApiException(HttpStatusCode statusCode)
        : base($"Notion API request failed ({(int)statusCode} {statusCode}).")
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Read-only Notion REST client. Uses the data-source API introduced in
/// Notion-Version 2026-03-11. Tokens are attached per request and never logged.
/// Imported page content is returned as data, never interpreted as instructions.
/// </summary>
public sealed class NotionApiClient
{
    public const string ApiVersion = "2026-03-11";
    private const int PageSize = 100;
    private const int MaximumQueryPages = 20;
    private const int MaximumBlocksPerPage = 1000;
    public const int MaximumAttachmentBytes = 64 * 1024 * 1024;
    private const string NotionFileHost = "prod-files-secure.s3.us-west-2.amazonaws.com";
    private const int MaximumBlockDepth = 6;
    private readonly HttpClient _http;

    public NotionApiClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _http.BaseAddress ??= new Uri("https://api.notion.com/v1/");
        _http.Timeout = _http.Timeout == Timeout.InfiniteTimeSpan
            ? TimeSpan.FromSeconds(30)
            : _http.Timeout;
    }

    public async Task<NotionDataSourceInfo> GetDataSourceAsync(
        string token,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(dataSourceId, nameof(dataSourceId));
        using var document = await SendJsonAsync(token, HttpMethod.Get, $"data_sources/{id}", null,
            cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var name = root.TryGetProperty("title", out var titleElement)
            ? ReadRichText(titleElement)
            : string.Empty;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("properties", out var propertyObject) &&
            propertyObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertyObject.EnumerateObject())
            {
                var type = property.Value.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                properties[property.Name] = type;
            }
        }

        return new NotionDataSourceInfo(
            root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? id : id,
            string.IsNullOrWhiteSpace(name) ? "Notion data source" : name,
            properties);
    }

    public async Task<IReadOnlyList<NotionCourseRecord>> QueryCourseRecordsAsync(
        string token,
        string dataSourceId,
        CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(dataSourceId, nameof(dataSourceId));
        var records = new List<NotionCourseRecord>();
        string? cursor = null;

        for (var page = 0; page < MaximumQueryPages; page++)
        {
            var payload = new Dictionary<string, object?> { ["page_size"] = PageSize };
            if (!string.IsNullOrWhiteSpace(cursor)) payload["start_cursor"] = cursor;

            using var document = await SendJsonAsync(token, HttpMethod.Post,
                $"data_sources/{id}/query", payload, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("La risposta Notion non contiene l'elenco dei record.");

            foreach (var pageElement in results.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                records.Add(ParseCourseRecord(pageElement));
            }

            var hasMore = root.TryGetProperty("has_more", out var moreElement) &&
                          moreElement.ValueKind == JsonValueKind.True;
            if (!hasMore) return records;
            cursor = root.TryGetProperty("next_cursor", out var cursorElement)
                ? cursorElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(cursor))
                throw new InvalidDataException("Notion ha indicato altri record senza fornire un cursore.");
        }

        throw new InvalidDataException(
            $"La query Notion supera il limite di sicurezza di {MaximumQueryPages * PageSize} record.");
    }

    public async Task<NotionPageContent> GetPageContentAsync(
        string token,
        string pageId,
        CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(pageId, nameof(pageId));
        var count = 0;
        var blocks = await ReadBlockChildrenAsync(token, id, 0, () => ++count, cancellationToken)
            .ConfigureAwait(false);
        return new NotionPageContent(id, blocks);
    }

    /// <summary>
    /// Downloads a Notion-hosted attachment from its short-lived signed URL.
    /// The URL is used immediately and must never be persisted or logged.
    /// No Notion bearer token is sent to the file host.
    /// </summary>
    public async Task<byte[]> DownloadFileAsync(
        string fileUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, NotionFileHost, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.Query.Contains("X-Amz-Signature=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il collegamento al file Notion non è valido o non è supportato.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new NotionApiException(response.StatusCode);

        if (!string.Equals(response.RequestMessage?.RequestUri?.Host, NotionFileHost,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il download Notion ha restituito un host non autorizzato.");

        if (response.Content.Headers.ContentLength is > MaximumAttachmentBytes)
            throw new InvalidDataException("L'allegato Notion supera il limite di 64 MB.");

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            mediaType is not ("application/json" or "text/plain" or "application/octet-stream"))
            throw new InvalidDataException("Il file Notion non è un allegato JSON/testo supportato.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaximumAttachmentBytes)
                throw new InvalidDataException("L'allegato Notion supera il limite di 64 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private async Task<IReadOnlyList<NotionContentBlock>> ReadBlockChildrenAsync(
        string token,
        string blockId,
        int depth,
        Func<int> incrementCount,
        CancellationToken cancellationToken)
    {
        if (depth > MaximumBlockDepth)
            throw new InvalidDataException("La pagina Notion supera la profondità massima supportata.");

        var blocks = new List<NotionContentBlock>();
        string? cursor = null;
        for (var page = 0; page < MaximumQueryPages; page++)
        {
            var query = $"blocks/{NormalizeId(blockId, nameof(blockId))}/children?page_size={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
                query += "&start_cursor=" + Uri.EscapeDataString(cursor);

            using var document = await SendJsonAsync(token, HttpMethod.Get, query, null, cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("La risposta Notion non contiene i blocchi della pagina.");

            foreach (var element in results.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (incrementCount() > MaximumBlocksPerPage)
                    throw new InvalidDataException(
                        $"La pagina Notion supera il limite di sicurezza di {MaximumBlocksPerPage} blocchi.");

                var id = element.TryGetProperty("id", out var blockIdElement)
                    ? blockIdElement.GetString() ?? string.Empty
                    : string.Empty;
                var type = element.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString() ?? "unknown"
                    : "unknown";
                var text = ReadBlockText(element, type);
                var externalUrl = ReadExternalUrl(element, type);
                IReadOnlyList<NotionContentBlock> children = Array.Empty<NotionContentBlock>();
                if (element.TryGetProperty("has_children", out var hasChildren) &&
                    hasChildren.ValueKind == JsonValueKind.True && !string.IsNullOrWhiteSpace(id))
                {
                    children = await ReadBlockChildrenAsync(token, id, depth + 1, incrementCount, cancellationToken)
                        .ConfigureAwait(false);
                }

                blocks.Add(new NotionContentBlock(id, type, text, externalUrl, children,
                    IsTemporaryFileBlock(element, type)));
            }

            var hasMore = root.TryGetProperty("has_more", out var moreElement) &&
                          moreElement.ValueKind == JsonValueKind.True;
            if (!hasMore) return blocks;
            cursor = root.TryGetProperty("next_cursor", out var cursorElement)
                ? cursorElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(cursor))
                throw new InvalidDataException("Notion ha indicato altri blocchi senza fornire un cursore.");
        }

        throw new InvalidDataException("La pagina Notion supera il limite di paginazione supportato.");
    }

    private async Task<JsonDocument> SendJsonAsync(
        string token,
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken)
    {
        var normalizedToken = NotionTokenVault.Normalize(token);
        for (var attempt = 0; attempt <= 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativeUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedToken);
            request.Headers.Add("Notion-Version", ApiVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8,
                    "application/json");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var retryable = response.StatusCode == (HttpStatusCode)429 ||
                            (int)response.StatusCode is >= 500 and <= 599;
            if (retryable && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta;
                if (delay is null && response.Headers.RetryAfter?.Date is { } retryDate)
                    delay = retryDate - DateTimeOffset.UtcNow;
                delay ??= TimeSpan.FromMilliseconds(350 * (attempt + 1));
                if (delay is { } negativeDelay && negativeDelay < TimeSpan.Zero) delay = TimeSpan.Zero;
                if (delay is { } longDelay && longDelay > TimeSpan.FromSeconds(5))
                    delay = TimeSpan.FromSeconds(5);
                await Task.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new NotionApiException(response.StatusCode);

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(json) > 5 * 1024 * 1024)
                throw new InvalidDataException("La risposta Notion supera il limite di 5 MiB.");
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new InvalidDataException("Notion ha restituito una risposta JSON non valida.");
            }
        }

        throw new NotionApiException(HttpStatusCode.ServiceUnavailable);
    }

    private static NotionCourseRecord ParseCourseRecord(JsonElement page)
    {
        var pageId = page.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;
        var pageUrl = page.TryGetProperty("url", out var urlElement)
            ? urlElement.GetString() ?? string.Empty
            : string.Empty;
        var properties = page.TryGetProperty("properties", out var propertyElement)
            ? propertyElement
            : default;

        var name = GetText(properties, "Esame");
        if (string.IsNullOrWhiteSpace(name))
            name = GetText(properties, "Name") ?? GetText(properties, "Titolo") ?? string.Empty;

        return new NotionCourseRecord(
            pageId,
            pageUrl,
            name.Trim(),
            GetText(properties, "Codice"),
            GetInt(properties, "CFU"),
            GetText(properties, "Docente"),
            GetSelect(properties, "Stato carriera"),
            GetInt(properties, "Voto"),
            GetSelect(properties, "Anno"),
            GetSelect(properties, "Modalità esame"),
            GetSelect(properties, "Priorità"),
            GetSelect(properties, "Preparazione sessione"),
            GetSelect(properties, "Ruolo nella sessione"),
            GetMultiSelect(properties, "Sessione target"),
            GetDecimal(properties, "Avanzamento %"),
            GetInt(properties, "Ore stimate"),
            GetDate(properties, "Data appello"),
            GetUrl(properties, "Fonte appello"),
            GetUrl(properties, "Moodle"),
            GetUrl(properties, "Scheda UniTo"),
            GetText(properties, "Propedeuticità"),
            GetCheckbox(properties, "Studio adesso"),
            GetDate(properties, "Ultima verifica"),
            GetDate(properties, "Ultimo controllo appelli"),
            page.TryGetProperty("last_edited_time", out var edited) &&
            DateTimeOffset.TryParse(edited.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var editedAt) ? editedAt : null);
    }

    private static string? GetText(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out var property)) return null;
        if (property.TryGetProperty("title", out var title)) return ReadRichText(title);
        if (property.TryGetProperty("rich_text", out var richText)) return ReadRichText(richText);
        if (property.TryGetProperty("url", out var url)) return url.GetString();
        if (property.TryGetProperty("select", out var select) &&
            select.TryGetProperty("name", out var selected)) return selected.GetString();
        return null;
    }

    private static string? GetSelect(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object &&
        properties.TryGetProperty(name, out var property) &&
        property.TryGetProperty("select", out var select) &&
        select.ValueKind == JsonValueKind.Object &&
        select.TryGetProperty("name", out var selected)
            ? selected.GetString()
            : null;

    private static IReadOnlyList<string> GetMultiSelect(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out var property) ||
            !property.TryGetProperty("multi_select", out var values) ||
            values.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return values.EnumerateArray()
            .Where(item => item.TryGetProperty("name", out _))
            .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static int? GetInt(JsonElement properties, string name)
    {
        var value = GetDecimal(properties, name);
        return value is >= int.MinValue and <= int.MaxValue ? decimal.ToInt32(value.Value) : null;
    }

    private static decimal? GetDecimal(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out var property) ||
            !property.TryGetProperty("number", out var number) ||
            number.ValueKind != JsonValueKind.Number ||
            !number.TryGetDecimal(out var result)) return null;
        return result;
    }

    private static bool GetCheckbox(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object &&
        properties.TryGetProperty(name, out var property) &&
        property.TryGetProperty("checkbox", out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? GetDate(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out var property) ||
            !property.TryGetProperty("date", out var date) ||
            date.ValueKind != JsonValueKind.Object ||
            !date.TryGetProperty("start", out var start) ||
            start.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(start.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    private static string? GetUrl(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object &&
        properties.TryGetProperty(name, out var property) &&
        property.TryGetProperty("url", out var url) &&
        url.ValueKind == JsonValueKind.String
            ? url.GetString()
            : null;

    private static string ReadRichText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Concat(element.EnumerateArray().Select(item =>
            item.TryGetProperty("plain_text", out var text) ? text.GetString() ?? string.Empty :
            item.TryGetProperty("text", out var textObject) &&
            textObject.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty :
            string.Empty));
    }

    private static string ReadBlockText(JsonElement block, string type)
    {
        if (block.TryGetProperty("name", out var blockName) && blockName.ValueKind == JsonValueKind.String)
            return blockName.GetString() ?? string.Empty;
        if (!block.TryGetProperty(type, out var payload) || payload.ValueKind != JsonValueKind.Object)
            return string.Empty;
        if (payload.TryGetProperty("name", out var payloadName) && payloadName.ValueKind == JsonValueKind.String)
            return payloadName.GetString() ?? string.Empty;
        if (payload.TryGetProperty("file", out var fileObject) &&
            fileObject.ValueKind == JsonValueKind.Object &&
            fileObject.TryGetProperty("name", out var fileName) && fileName.ValueKind == JsonValueKind.String)
            return fileName.GetString() ?? string.Empty;
        if (payload.TryGetProperty("rich_text", out var richText)) return ReadRichText(richText);
        if (payload.TryGetProperty("title", out var title))
        {
            if (title.ValueKind == JsonValueKind.Array) return ReadRichText(title);
            return title.GetString() ?? string.Empty;
        }
        if (payload.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            return string.Join(" | ", cells.EnumerateArray().Select(ReadRichText));
        if (payload.TryGetProperty("url", out var url)) return url.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string? ReadExternalUrl(JsonElement block, string type)
    {
        if (!block.TryGetProperty(type, out var payload) || payload.ValueKind != JsonValueKind.Object)
            return null;
        if (payload.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            return url.GetString();
        if (payload.TryGetProperty("file", out var fileObject) && fileObject.ValueKind == JsonValueKind.Object)
        {
            if (fileObject.TryGetProperty("url", out var fileUrl) && fileUrl.ValueKind == JsonValueKind.String)
                return fileUrl.GetString();
            if (fileObject.TryGetProperty("file", out var nestedFile) &&
                nestedFile.ValueKind == JsonValueKind.Object &&
                nestedFile.TryGetProperty("url", out var nestedUrl) && nestedUrl.ValueKind == JsonValueKind.String)
                return nestedUrl.GetString();
        }
        if (payload.TryGetProperty("external", out var external) &&
            external.ValueKind == JsonValueKind.Object &&
            external.TryGetProperty("url", out var externalUrl))
            return externalUrl.GetString();
        return null;
    }

    private static bool IsTemporaryFileBlock(JsonElement block, string type)
    {
        if (!block.TryGetProperty(type, out var payload) || payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("file", out var fileObject) || fileObject.ValueKind != JsonValueKind.Object)
            return false;
        if (fileObject.TryGetProperty("url", out var fileUrl) && fileUrl.ValueKind == JsonValueKind.String)
            return true;
        return fileObject.TryGetProperty("file", out var nestedFile) &&
               nestedFile.ValueKind == JsonValueKind.Object &&
               nestedFile.TryGetProperty("url", out var nestedUrl) &&
               nestedUrl.ValueKind == JsonValueKind.String;
    }

    private static string NormalizeId(string value, string parameterName)
    {
        if (!Guid.TryParse(value, out var id))
            throw new ArgumentException("Inserisci un ID Notion valido.", parameterName);
        return id.ToString("D");
    }
}
