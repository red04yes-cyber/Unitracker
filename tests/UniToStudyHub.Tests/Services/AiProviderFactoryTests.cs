using UniToStudyHub.Security;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AiProviderFactoryTests
{
    [Fact]
    public void MissingConsentKeepsProviderUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var factory = new AiProviderFactory(new AiSettingsService(),
            new AiCredentialStore(root, new TestSecretProtector()));
        var runtime = factory.CreateFromEnvironment(_ => null);

        Assert.False(runtime.Provider.IsConfigured);
        Assert.Null(runtime.Settings);
    }

    [Fact]
    public void OpenAiProviderRequiresConsentAndResolvesDpapiCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        credentials.SaveKey("openai", "test-key-not-real");
        var values = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "openai",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true"
        };
        var factory = new AiProviderFactory(new AiSettingsService(), credentials);

        var runtime = factory.CreateFromEnvironment(name => values.GetValueOrDefault(name));

        Assert.True(runtime.Provider.IsConfigured);
        Assert.Equal("OpenAI", runtime.Provider.Name);
        Assert.True(runtime.Settings?.ConsentGranted);
        (runtime.Provider as IDisposable)?.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void OllamaUsesLoopbackOnlyAndDoesNotRequireAnApiKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        var values = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "ollama",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true",
            ["STUDYHUB_AI_MODEL"] = "llama3.2"
        };
        var factory = new AiProviderFactory(new AiSettingsService(), credentials);

        var runtime = factory.CreateFromEnvironment(name => values.GetValueOrDefault(name));

        Assert.True(runtime.Provider.IsConfigured);
        Assert.Equal("Ollama (locale)", runtime.Provider.Name);
        Assert.Equal("http://127.0.0.1:11434/v1", runtime.Settings?.BaseUrl);
        Assert.True(runtime.Settings?.AllowLoopbackHttpEndpoint);
        (runtime.Provider as IDisposable)?.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void OllamaCannotBeRedirectedToAnExternalHttpEndpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        var values = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "ollama",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true",
            ["STUDYHUB_AI_MODEL"] = "llama3.2",
            ["STUDYHUB_AI_BASE_URL"] = "http://example.com/v1"
        };
        var factory = new AiProviderFactory(new AiSettingsService(), credentials);

        var runtime = factory.CreateFromEnvironment(name => values.GetValueOrDefault(name));

        Assert.False(runtime.Provider.IsConfigured);
        Assert.Null(runtime.Settings);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
