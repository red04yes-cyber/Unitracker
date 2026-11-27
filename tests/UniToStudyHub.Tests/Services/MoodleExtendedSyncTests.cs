using System.Net;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class MoodleExtendedSyncTests
{
    private const string Token = "abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task ActivityMetadataUsesOnlyFunctionsGrantedToTheToken()
    {
        var requests = new List<string>();
        using var client = new MoodleClient("https://moodle.example.edu", Token, new StubHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add(body);
            return Json("[{\"id\":7,\"name\":\"Quiz finale\"}]");
        }));

        var result = await client.GetCourseActivityDetailsAsync(
            42,
            new[] { "mod_quiz_get_quizzes_by_courses" });

        Assert.Contains("mod_quiz_get_quizzes_by_courses", result.Keys);
        Assert.Equal(7, result["mod_quiz_get_quizzes_by_courses"][0].GetProperty("id").GetInt32());
        Assert.Single(requests);
        Assert.Contains("courseids%5B0%5D=42", requests[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomPluginActivityApisAreDiscoveredFromTheTokenFunctionList()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(_ => Json("""[{"id":18,"name":"Attività plugin"}]""")));

        var result = await client.GetCourseActivityDetailsAsync(
            42, new[] { "mod_customactivity_get_activities_by_courses" });

        Assert.Contains("mod_customactivity_get_activities_by_courses", result.Keys);
    }

    [Fact]
    public async Task ActivityCompletionIsImportedWhenTheFunctionIsGranted()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(_ => Json("{\"statuses\":[{\"cmid\":99,\"modname\":\"assign\",\"state\":1,\"timecompleted\":1700000000}]}")));

        var result = await client.GetActivityCompletionAsync(
            42, 123, new[] { "core_completion_get_activities_completion_status" });

        var status = Assert.Single(result);
        Assert.Equal(99, status.cmid);
        Assert.Equal(1, status.state);
    }

    [Fact]
    public async Task AssignmentSubmissionDetailsAreReadWhenTokenAllowsIt()
    {
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(_ => Json("""{"lastattempt":{"submission":{"status":"submitted"}}}""")));
        var sections = new[]
        {
            new UniToStudyHub.Models.MoodleCourseSection
            {
                modules = new List<UniToStudyHub.Models.MoodleModule>
                {
                    new() { id = 11, instance = 22, modname = "assign" }
                }
            }
        };

        var details = await client.GetModuleUserActivityDataAsync(
            sections, 123, new[] { "mod_assign_get_submission_status" });

        Assert.Contains(details.Keys, key => key.Contains("assign:11", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityApiResponsesExposeNestedFileAttachments()
    {
        using var document = JsonDocument.Parse(
            """{"assignments":[{"introfiles":[{"filename":"brief.pdf","fileurl":"https://moodle.example.edu/pluginfile.php/1/brief.pdf","filesize":1234,"mimetype":"application/pdf"}]}]}""");

        var files = MoodleClient.ExtractActivityFiles(new[] { document.RootElement });

        var file = Assert.Single(files);
        Assert.Equal("brief.pdf", file.filename);
        Assert.Equal(1234, file.filesize);
        Assert.Equal("file", file.type);
    }

    [Fact]
    public async Task FileDownloadRejectsOtherOriginsBeforeSendingARequest()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync("https://attacker.example/steal.pdf",
                    Path.Combine(folder, "steal.pdf"), allowedDirectory: folder));
            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FileDownloadStreamsContentThroughTheWebServiceEndpoint()
    {
        var payload = Encoding.UTF8.GetBytes("%PDF-test-content");
        Uri? requestedUri = null;
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") }
                    }
                };
            }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "lecture.pdf");
        try
        {
            await client.DownloadFileAsync(
                "https://moodle.example.edu/pluginfile.php/1/mod_resource/content/lecture.pdf?token=stale&wstoken=old&forcedownload=1",
                destination, allowedDirectory: folder);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.NotNull(requestedUri);
            Assert.True(requestedUri!.AbsolutePath.EndsWith(
                "/webservice/pluginfile.php/1/mod_resource/content/lecture.pdf",
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains("token=" + Token, requestedUri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("wstoken=", requestedUri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("forcedownload=1", requestedUri.Query, StringComparison.Ordinal);
            Assert.Equal(1, requestedUri.Query.Split('&').Count(part =>
                part.StartsWith("?token=", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("token=", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FileDownloadKeepsAnAlreadyRewrittenWebServiceEndpoint()
    {
        Uri? requestedUri = null;
        using var client = new MoodleClient("https://moodle.example.edu/moodle", Token,
            new StubHandler(request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-test"))
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") }
                    }
                };
            }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await client.DownloadFileAsync(
                "https://moodle.example.edu/moodle/webservice/pluginfile.php/1/lecture.pdf",
                Path.Combine(folder, "lecture.pdf"), allowedDirectory: folder);

            Assert.NotNull(requestedUri);
            Assert.True(requestedUri!.AbsolutePath.EndsWith(
                "/moodle/webservice/pluginfile.php/1/lecture.pdf",
                StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("/webservice/webservice/", requestedUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FileDownloadRejectsSameOriginNonFileEndpointBeforeSendingARequest()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu/moodle", Token,
            new StubHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync("https://moodle.example.edu/moodle/login/index.php",
                    Path.Combine(folder, "login.html"), allowedDirectory: folder));
            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FileDownloadRejectsNestedSameOriginPluginfileEndpoint()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu/moodle", Token,
            new StubHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync("https://moodle.example.edu/moodle/other/pluginfile.php/1/file.pdf",
                    Path.Combine(folder, "file.pdf"), allowedDirectory: folder));
            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FileDownloadRejectsSameOriginPathOutsideConfiguredMoodleBase()
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu/moodle", Token,
            new StubHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync("https://moodle.example.edu/other/pluginfile.php/1/lecture.pdf",
                    Path.Combine(folder, "lecture.pdf"), allowedDirectory: folder));
            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("https://user:pass@moodle.example.edu/moodle/pluginfile.php/1/lecture.pdf")]
    [InlineData("https://moodle.example.edu/moodle/pluginfile.php/1/lecture.pdf#secret")]
    public async Task FileDownloadRejectsCredentialsAndFragmentsBeforeSendingARequest(string url)
    {
        var requests = 0;
        using var client = new MoodleClient("https://moodle.example.edu/moodle", Token,
            new StubHandler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadFileAsync(url, Path.Combine(folder, "lecture.pdf"), allowedDirectory: folder));
            Assert.Equal(0, requests);
            Assert.Empty(Directory.EnumerateFiles(folder));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
