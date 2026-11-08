using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>Google Gemini generateContent adapter. The API key is sent only in x-goog-api-key.</summary>
public sealed class GeminiProvider : IAiProvider, IDisposable
{
    private readonly AiSettings _settings;
    private readonly string _apiKey;
    private readonly HttpClient _client;

    public GeminiProvider(AiSettings settings, string apiKey, HttpClient? client = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("API key is required.", nameof(apiKey))
            : apiKey.Trim();
        if (!settings.ConsentGranted || !string.Equals(settings.Provider, "Gemini", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.Equals(endpoint.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Model))
            throw new ArgumentException("Gemini settings are invalid or consent has not been granted.", nameof(settings));

        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    public bool IsConfigured => true;
    public string Name => "Gemini";
    public const int MaximumPromptCharacters = 120_000;
    public const int MaximumResponseBytes = 4 * 1024 * 1024;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SystemPrompt.Length + request.UserPrompt.Length > MaximumPromptCharacters)
            return new AiResponse(AiStatus.Refused, string.Empty, Name, "Il contenuto selezionato supera il limite per una richiesta AI.");
        try
        {
            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            var model = Uri.EscapeDataString(_settings.Model);
            using var message = new HttpRequestMessage(HttpMethod.Post,
                new Uri($"{baseUrl}/models/{model}:generateContent"));
            message.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
            message.Content = new StringContent(JsonSerializer.Serialize(new
            {
                systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = request.UserPrompt } } } },
                generationConfig = new
                {
                    maxOutputTokens = Math.Clamp(request.MaxTokens, 1, 4000),
                    temperature = Math.Clamp(request.Temperature, 0, 2)
                }
            }), Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new AiResponse(AiStatus.Error, string.Empty, Name,
                    $"Gemini ha rifiutato la richiesta ({(int)response.StatusCode}).");
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                return new AiResponse(AiStatus.Error, string.Empty, Name, "La risposta Gemini supera il limite consentito.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            var total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaximumResponseBytes)
                    return new AiResponse(AiStatus.Error, string.Empty, Name, "La risposta Gemini supera il limite consentito.");
                buffer.Write(chunk, 0, read);
            }

            using var json = JsonDocument.Parse(buffer.ToArray());
            var parts = json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var answer = string.Join("\n", parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
            if (string.IsNullOrWhiteSpace(answer))
                return new AiResponse(AiStatus.Error, string.Empty, Name, "Gemini non ha restituito testo utilizzabile.");
            return new AiResponse(AiStatus.Configured, answer, Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or
                                   KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or UriFormatException)
        {
            AppDiagnostics.Warning("ai", "Richiesta Gemini non riuscita.", ex);
            return new AiResponse(AiStatus.Error, string.Empty, Name,
                "Gemini non è raggiungibile o ha restituito una risposta non valida. Verifica modello e connessione.");
        }
    }

    public void Dispose() => _client.Dispose();
}
