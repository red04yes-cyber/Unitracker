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
        using var client = new MoodleClient("https://moodle.example.edu", Token, new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
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
    public async Task FileDownloadStreamsContentToTheApprovedCourseFolder()
    {
        var payload = Encoding.UTF8.GetBytes("%PDF-test-content");
        using var client = new MoodleClient("https://moodle.example.edu", Token,
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") }
                }
            }));
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerMoodleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "lecture.pdf");
        try
        {
            await client.DownloadFileAsync("https://moodle.example.edu/pluginfile.php/1/mod_resource/content/lecture.pdf",
                destination, allowedDirectory: folder);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
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
