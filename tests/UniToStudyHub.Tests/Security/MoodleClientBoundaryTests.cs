using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Security;

public sealed class MoodleClientBoundaryTests
{
    private const string Token = "abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task ApiTokenIsSentInThePostBodyAndNeverInTheRequestUri()
    {
        HttpRequestMessage? captured = null;
        string? form = null;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(async (request, ct) =>
            {
                captured = request;
                form = await request.Content!.ReadAsStringAsync(ct);
                return Json("{}");
            }));

        await client.GetSiteInfoAsync();

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Empty(captured.RequestUri!.Query);
        Assert.DoesNotContain(Token, captured.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Contains("wstoken=" + Token, form, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiResponseLimitIsEnforcedWhileStreamingWithoutBufferingTheWholeBody()
    {
        var totalBytes = MoodleClient.MaximumApiResponseBytes + 4L * 1024 * 1024;
        var stream = new CountingGeneratedStream(totalBytes);
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonStream(stream)
            }));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetSiteInfoAsync());

        Assert.True(stream.BytesRead < totalBytes);
        Assert.InRange(stream.BytesRead,
            MoodleClient.MaximumApiResponseBytes + 1L,
            MoodleClient.MaximumApiResponseBytes + 81920L);
    }

    [Fact]
    public async Task ApiRejectsMalformedUtf8InsteadOfSilentlyReplacingBytes()
    {
        var payload = Encoding.UTF8.GetBytes("{\"sitename\":\"")
            .Concat(new byte[] { 0xff })
            .Concat(Encoding.UTF8.GetBytes("\"}"))
            .ToArray();
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonBytes(payload)
            }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetSiteInfoAsync());

        Assert.Contains("UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiResponseReadPropagatesCancellation()
    {
        var stream = new BlockingReadStream();
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonStream(stream, "application/json")
            }));
        using var cancellation = new CancellationTokenSource();

        var operation = client.GetSiteInfoAsync(cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task MoodleExceptionDetailsAreRedactedBeforeBeingExposed()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => Json(
                "{\"exception\":\"invalidtoken\",\"message\":\"wstoken=" + Token + "\"}")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetSiteInfoAsync());

        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Contains("core_webservice_get_site_info", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadRejectsSameHostOnDifferentPortBeforeSendingARequest()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ =>
            {
                requests++;
                return PdfResponse();
            }));
        var folder = CreateTempDirectory();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync(
                    "https://moodle.example.edu:444/pluginfile.php/1/file.pdf",
                    Path.Combine(folder, "file.pdf"),
                    allowedDirectory: folder));

            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    [Fact]
    public async Task DownloadRejectsRedirectWithoutIssuingASecondRequest()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://attacker.example/collect") }
                };
            }));
        var folder = CreateTempDirectory();
        var destination = Path.Combine(folder, "lecture.pdf");
        await File.WriteAllTextAsync(destination, "existing file");
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.DownloadFileAsync(
                    "https://moodle.example.edu/pluginfile.php/1/file.pdf",
                    destination,
                    allowedDirectory: folder));

            Assert.Equal(1, requests);
            Assert.Equal("existing file", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.EnumerateFiles(folder));
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    [Fact]
    public async Task HtmlLoginResponseDoesNotReplaceAnExistingDownload()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html")
            }));
        var folder = CreateTempDirectory();
        var destination = Path.Combine(folder, "lecture.pdf");
        await File.WriteAllTextAsync(destination, "trusted local copy");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadFileAsync(
                    "https://moodle.example.edu/pluginfile.php/1/file.pdf",
                    destination,
                    allowedDirectory: folder));

            Assert.Equal("trusted local copy", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.EnumerateFiles(folder));
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    [Fact]
    public async Task DeclaredOversizedDownloadIsRejectedBeforeReplacingTheDestination()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ =>
            {
                var content = new ByteArrayContent(Array.Empty<byte>());
                content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Headers.ContentLength = MoodleClient.MaximumDownloadBytes + 1;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }));
        var folder = CreateTempDirectory();
        var destination = Path.Combine(folder, "lecture.pdf");
        await File.WriteAllTextAsync(destination, "trusted local copy");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadFileAsync(
                    "https://moodle.example.edu/pluginfile.php/1/file.pdf",
                    destination,
                    allowedDirectory: folder));

            Assert.Equal("trusted local copy", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.EnumerateFiles(folder));
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    [Fact]
    public async Task DownloadReplacesDuplicateTokenQueryParametersWithOneCurrentToken()
    {
        Uri? requestedUri = null;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(request =>
            {
                requestedUri = request.RequestUri;
                return PdfResponse();
            }));
        var folder = CreateTempDirectory();
        try
        {
            await client.DownloadFileAsync(
                "https://moodle.example.edu/pluginfile.php/1/file.pdf?token=old&keep=yes&ToKeN=older",
                Path.Combine(folder, "lecture.pdf"),
                allowedDirectory: folder);

            Assert.NotNull(requestedUri);
            var query = requestedUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var tokenParameters = query
                .Where(part => Uri.UnescapeDataString(part.Split('=', 2)[0])
                    .Equals("token", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var tokenParameter = Assert.Single(tokenParameters);
            Assert.Equal(Token, Uri.UnescapeDataString(tokenParameter.Split('=', 2)[1]));
            Assert.Contains("keep=yes", query, StringComparer.Ordinal);
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    [Fact]
    public void ActivityFileExtractionDeduplicatesRepeatedUrls()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "first": { "filename": "lecture.pdf", "fileurl": "https://moodle.example.edu/file/1" },
              "duplicate": { "filename": "renamed.pdf", "fileurl": "https://moodle.example.edu/file/1" }
            }
            """);

        var files = MoodleClient.ExtractActivityFiles(new[] { document.RootElement });

        var file = Assert.Single(files);
        Assert.Equal("lecture.pdf", file.filename);
    }

    [Fact]
    public async Task CancellingDownloadPreservesExistingFileAndRemovesPartialFile()
    {
        var stream = new BlockingReadStream();
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonStream(stream, "application/pdf")
            }));
        var folder = CreateTempDirectory();
        var destination = Path.Combine(folder, "lecture.pdf");
        await File.WriteAllTextAsync(destination, "trusted local copy");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var operation = client.DownloadFileAsync(
                "https://moodle.example.edu/pluginfile.php/1/file.pdf",
                destination,
                cancellation.Token,
                folder);
            await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

            Assert.Equal("trusted local copy", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.EnumerateFiles(folder));
        }
        finally
        {
            DeleteDirectory(folder);
        }
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private static HttpContent JsonBytes(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static HttpContent JsonStream(Stream stream, string mediaType = "application/json")
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return content;
    }

    private static HttpResponseMessage PdfResponse()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-test"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleBoundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this((request, _) => Task.FromResult(send(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class CountingGeneratedStream(long length) : Stream
    {
        private long _remaining = length;

        public long BytesRead { get; private set; }
        public long TotalLength { get; } = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remaining);
            Array.Fill(buffer, (byte)' ', offset, read);
            _remaining -= read;
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..read].Fill((byte)' ');
            _remaining -= read;
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted => _readStarted;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
