using UniToStudyHub.Security;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AiProviderFactoryTests
{
    [Fact]
    public void SavedPreferencesSelectOpenAiWithoutPersistingTheSecretInPreferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preferences = new AiProviderPreferencesStore(root);
        preferences.Save(new AiProviderPreferences("openai", "test-model", true));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        credentials.SaveKey("openai", "test-key-not-real");
        var factory = new AiProviderFactory(new AiSettingsService(), credentials, preferences);

        var runtime = factory.CreateFromEnvironment(_ => null);

        Assert.True(runtime.Provider.IsConfigured);
        Assert.Equal("OpenAI", runtime.Provider.Name);
        Assert.Equal("test-model", runtime.Settings?.Model);
        var storedPreferences = File.ReadAllText(Path.Combine(root, "ai-provider-preferences.json"));
        Assert.DoesNotContain("test-key-not-real", storedPreferences, StringComparison.Ordinal);
        (runtime.Provider as IDisposable)?.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void SavedPreferencesWithRevokedConsentDisableProviderEvenWhenEnvironmentConsents()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preferences = new AiProviderPreferencesStore(root);
        preferences.Save(new AiProviderPreferences("openai", "test-model", false));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        credentials.SaveKey("openai", "test-key-not-real");
        var factory = new AiProviderFactory(new AiSettingsService(), credentials, preferences);
        var environment = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "openai",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true"
        };

        var runtime = factory.CreateFromEnvironment(name => environment.GetValueOrDefault(name));

        Assert.False(runtime.Provider.IsConfigured);
        Assert.Null(runtime.Settings);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void GeminiProviderLoadsItsDedicatedDpapiKeyAndDefaultEndpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preferences = new AiProviderPreferencesStore(root);
        preferences.Save(new AiProviderPreferences("gemini", "gemini-2.5-flash", true));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        credentials.SaveKey("gemini", "gemini-test-key");
        var factory = new AiProviderFactory(new AiSettingsService(), credentials, preferences);

        var runtime = factory.CreateFromEnvironment(_ => null);

        Assert.True(runtime.Provider.IsConfigured);
        Assert.IsType<GeminiProvider>(runtime.Provider);
        Assert.Equal("Gemini", runtime.Provider.Name);
        Assert.Equal("gemini-2.5-flash", runtime.Settings?.Model);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta", runtime.Settings?.BaseUrl);
        var saved = File.ReadAllText(Path.Combine(root, "ai-provider-preferences.json"));
        Assert.DoesNotContain("gemini-test-key", saved, StringComparison.Ordinal);
        (runtime.Provider as IDisposable)?.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void GeminiRequiresConsentAndItsOwnCredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        credentials.SaveKey("openai", "openai-test-key");
        var values = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "gemini",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true",
            ["STUDYHUB_AI_MODEL"] = "gemini-2.5-flash"
        };
        var runtime = new AiProviderFactory(new AiSettingsService(), credentials)
            .CreateFromEnvironment(name => values.GetValueOrDefault(name));
        Assert.False(runtime.Provider.IsConfigured);
        Assert.Null(runtime.Settings);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

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

    [Theory]
    [InlineData("http://example.com/v1")]
    [InlineData("https://example.com/v1")]
    public void OllamaCannotBeRedirectedToAnExternalEndpoint(string endpoint)
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-ai-" + Guid.NewGuid().ToString("N"));
        var credentials = new AiCredentialStore(root, new TestSecretProtector());
        var values = new Dictionary<string, string?>
        {
            [AiProviderFactory.ProviderEnvironmentVariable] = "ollama",
            [AiProviderFactory.ConsentEnvironmentVariable] = "true",
            ["STUDYHUB_AI_MODEL"] = "llama3.2",
            ["STUDYHUB_AI_BASE_URL"] = endpoint
        };
        var factory = new AiProviderFactory(new AiSettingsService(), credentials);

        var runtime = factory.CreateFromEnvironment(name => values.GetValueOrDefault(name));

        Assert.False(runtime.Provider.IsConfigured);
        Assert.Null(runtime.Settings);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
