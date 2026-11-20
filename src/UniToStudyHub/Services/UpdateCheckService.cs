using System.Net;
using System.Text.Json;

namespace UniToStudyHub.Services;

public sealed record AvailableAppUpdate(Version Version, string TagName, Uri ReleaseUri, Uri ReleaseDownloadBase);

/// <summary>
/// Checks the public GitHub Releases endpoint. It sends no user data or credentials
/// and treats unavailable or malformed release metadata as "no update".
/// </summary>
public sealed class UpdateCheckService(HttpClient httpClient)
{
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/red04yes-cyber/Unitracker/releases/latest";
    private const int MaximumResponseBytes = 256 * 1024;

    public async Task<AvailableAppUpdate?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.UserAgent.ParseAdd("UniToStudyHub-UpdateChecker/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            return null;

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            return null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await responseStream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (buffer.Length + read > MaximumResponseBytes)
                return null;

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryGetString(root, "tag_name", out var tag) ||
            tag.Length is < 2 or > 64 ||
            tag[0] != 'v' ||
            !Version.TryParse(tag.AsSpan(1), out var publishedVersion))
        {
            return null;
        }

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease") || publishedVersion <= currentVersion)
            return null;

        // Construct the destination from a fixed trusted origin instead of
        // opening an arbitrary html_url supplied by remote JSON.
        var releaseUri = new Uri(
            $"https://github.com/red04yes-cyber/Unitracker/releases/tag/{Uri.EscapeDataString(tag)}",
            UriKind.Absolute);

        var downloadBase = new Uri($"https://github.com/red04yes-cyber/Unitracker/releases/download/{Uri.EscapeDataString(tag)}/", UriKind.Absolute);
        return new AvailableAppUpdate(publishedVersion, tag, releaseUri, downloadBase);
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(property, out var item) &&
               item.ValueKind == JsonValueKind.String &&
               (value = item.GetString() ?? string.Empty).Length > 0;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.True;
}
