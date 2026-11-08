using UniToStudyHub.Services;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AiStudyGuidanceTests
{
    [Fact]
    public async Task AskAsyncMarksLocalStudyContextAsUntrusted()
    {
        var provider = new MockAiProvider(request => request.UserPrompt);
        var service = new AiStudyService(provider);

        var response = await service.AskAsync("Crea un piano", "Corso: Matematica");

        Assert.Equal(AiStatus.Configured, response.Status);
        Assert.Contains("[UNTRUSTED_STUDY_CONTEXT]", response.Content);
        Assert.Contains("Corso: Matematica", response.Content);
    }

    [Fact]
    public async Task AskAsyncRefusesBeforeCallingProviderWithoutConsent()
    {
        var provider = new CountingAiProvider(_ => "non deve essere chiamato");
        var settings = new AiSettings(
            Provider: "OpenAI",
            BaseUrl: "https://api.openai.com/v1",
            Model: "test-model",
            ApiKeyEnvironmentVariable: "TEST_API_KEY",
            ConsentGranted: false);
        var service = new AiStudyService(provider, settings);

        var response = await service.AskAsync("Crea un piano", "Contesto locale");

        Assert.Equal(AiStatus.Refused, response.Status);
        Assert.Equal(0, provider.Calls);
    }
}
