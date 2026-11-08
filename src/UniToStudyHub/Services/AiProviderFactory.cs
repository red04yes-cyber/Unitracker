using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Selects either the OpenAI cloud endpoint or a local Ollama OpenAI-compatible
/// endpoint. Configuration is read at startup; secrets are resolved from DPAPI
/// or the process environment and never copied into settings.
/// </summary>
public sealed record AiProviderRuntime(IAiProvider Provider, AiSettings? Settings);

public sealed class AiProviderFactory(
    AiSettingsService settingsService,
    AiCredentialStore credentials,
    AiProviderPreferencesStore? preferences = null)
{
    public const string ProviderEnvironmentVariable = "STUDYHUB_AI_PROVIDER";
    public const string ConsentEnvironmentVariable = "STUDYHUB_AI_CONSENT";

    public AiProviderRuntime CreateFromEnvironment(
        Func<string, string?>? environment = null)
    {
        var get = environment ?? Environment.GetEnvironmentVariable;
        var saved = preferences?.Load();
        var providerName = (saved?.Provider ?? get(ProviderEnvironmentVariable))?.Trim().ToLowerInvariant();
        var consent = saved?.ConsentGranted ??
            string.Equals(get(ConsentEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase);
        if (!consent || providerName is not ("openai" or "ollama"))
            return new AiProviderRuntime(new UnavailableAiProvider(), null);

        var isLocal = providerName == "ollama";
        var baseUrl = get("STUDYHUB_AI_BASE_URL") ??
                      (isLocal ? "http://127.0.0.1:11434/v1" : "https://api.openai.com/v1");
        var model = saved?.Model ?? get("STUDYHUB_AI_MODEL") ??
                    (isLocal ? string.Empty : "gpt-4.1-mini");
        if (isLocal && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var localEndpoint) ||
                        !localEndpoint.IsLoopback))
            return new AiProviderRuntime(new UnavailableAiProvider(), null);
        var secretName = isLocal ? null : "openai";
        var keyEnvironmentVariable = isLocal ? "STUDYHUB_OLLAMA_LOCAL_TOKEN" : "STUDYHUB_OPENAI_API_KEY";
        var settings = new AiSettings(
            Provider: isLocal ? "Ollama (locale)" : "OpenAI",
            BaseUrl: baseUrl,
            Model: model,
            ApiKeyEnvironmentVariable: keyEnvironmentVariable,
            ConsentGranted: consent,
            MaxRequestsPerDay: 50,
            ApiKeySecretName: secretName,
            AllowLoopbackHttpEndpoint: isLocal);

        // Ollama's local OpenAI-compatible endpoint does not require a secret.
        // A fixed non-secret bearer value is used because the shared adapter
        // emits Authorization: Bearer; it is never persisted or logged.
        string? GetEnvironment(string name) =>
            isLocal && name == keyEnvironmentVariable ? "ollama-local" : get(name);
        string? GetStoredSecret(string name) => credentials.LoadKey(name);

        if (string.IsNullOrWhiteSpace(model) ||
            !settingsService.IsReady(settings, GetEnvironment, GetStoredSecret))
            return new AiProviderRuntime(new UnavailableAiProvider(), null);

        var key = settingsService.ResolveApiKey(settings, GetEnvironment, GetStoredSecret);
        if (string.IsNullOrWhiteSpace(key))
            return new AiProviderRuntime(new UnavailableAiProvider(), null);

        try
        {
            return new AiProviderRuntime(new OpenAiCompatibleProvider(settings, key), settings);
        }
        catch (ArgumentException)
        {
            return new AiProviderRuntime(new UnavailableAiProvider(), null);
        }
    }
}
