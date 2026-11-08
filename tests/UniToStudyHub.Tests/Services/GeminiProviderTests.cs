using System.Net;
using System.Text;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task SendsKeyInHeaderAndParsesGenerateContentResponse()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[{"text":"Risposta Gemini"}]}}]}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler);
        var settings = new AiSettings("Gemini",
            "https://generativelanguage.googleapis.com/v1beta",
            "gemini-2.5-flash", "STUDYHUB_GEMINI_API_KEY", true);
        using var provider = new GeminiProvider(settings, "gemini-test-secret", client);

        var result = await provider.CompleteAsync(new AiRequest("system", "user"));

        Assert.Equal(AiStatus.Configured, result.Status);
        Assert.Equal("Risposta Gemini", result.Content);
        Assert.Equal("Gemini", result.Provider);
        Assert.NotNull(captured);
        Assert.Equal("gemini-test-secret", captured!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("gemini-test-secret", captured.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonGoogleOrNonHttpsEndpoint()
    {
        var settings = new AiSettings("Gemini", "https://example.com/v1beta",
            "gemini-2.5-flash", "STUDYHUB_GEMINI_API_KEY", true);
        Assert.Throws<ArgumentException>(() => new GeminiProvider(settings, "test-key"));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
