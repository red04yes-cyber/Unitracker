using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UniToStudyHub.Security;

/// <summary>
/// Removes credentials and unnecessary personal data before a message reaches
/// a log file, a diagnostics report or the UI. It is intentionally conservative:
/// over-redacting a diagnostic line is acceptable, leaking a token is not.
/// </summary>
public static class SecurityRedaction
{
    public const string Placeholder = "[REDACTED]";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex SecretAssignment = new(
        @"(?<key>\b(?:wstoken|token|api[_-]?key|apikey|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret|password|passwd|pwd|authorization)\b\s*[:=]\s*)(?<value>[^""'&\s,;]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex BearerToken = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex PrivateKeyBlock = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex LongHex = new(
        @"\b[0-9a-fA-F]{32,128}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex LongBase64 = new(
        @"\b[A-Za-z0-9+/]{40,}={0,2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EmailAddress = new(
        @"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex WindowsUserPath = new(
        @"(?<root>[A-Za-z]:\\Users\\)(?<user>[^\\\s""']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex UnixUserPath = new(
        @"(?<root>/home/|/Users/)(?<user>[^/\s""']+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        try
        {
            var redacted = PrivateKeyBlock.Replace(text, "[REDACTED KEY]");
            // Bearer first: the assignment rule stops at the first space, which
            // would otherwise leave the raw token after "Authorization: Bearer".
            redacted = BearerToken.Replace(redacted, "Bearer " + Placeholder);
            redacted = SecretAssignment.Replace(redacted, match => match.Groups["key"].Value + Placeholder);
            redacted = LongHex.Replace(redacted, Placeholder);
            redacted = LongBase64.Replace(redacted, Placeholder);
            redacted = EmailAddress.Replace(redacted, "[email]");
            redacted = WindowsUserPath.Replace(redacted, match => match.Groups["root"].Value + "[user]");
            redacted = UnixUserPath.Replace(redacted, match => match.Groups["root"].Value + "[user]");
            return redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            // Never fall back to the raw text when redaction cannot complete.
            return "[unredactable message omitted]";
        }
    }

    /// <summary>
    /// Single-line, control-character-free and redacted text for a log entry.
    /// Prevents log injection through imported titles or Moodle responses.
    /// </summary>
    public static string ForLog(string? text, int maximumLength = 4000)
    {
        var redacted = Redact(text);
        var builder = new StringBuilder(Math.Min(redacted.Length, maximumLength));
        foreach (var character in redacted)
        {
            if (builder.Length >= maximumLength) break;
            builder.Append(char.IsControl(character) ? ' ' : character);
        }
        return builder.ToString().Trim();
    }

    public static string ExceptionForLog(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var builder = new StringBuilder();
        builder.Append(exception.GetType().Name).Append(": ").Append(ForLog(exception.Message, 1000));
        return builder.ToString();
    }

    /// <summary>
    /// A short message that is safe to show to the user: no stack trace, no
    /// file-system path, no credential and no response body. Messages authored
    /// by this application for validation failures are kept because they are
    /// actionable; anything else becomes a generic sentence.
    /// </summary>
    public static string ForUser(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            TaskCanceledException => "Il servizio esterno non ha risposto in tempo.",
            OperationCanceledException => "Operazione annullata.",
            UnauthorizedAccessException => "Permesso negato dal sistema operativo.",
            FileNotFoundException => "File non trovato.",
            DirectoryNotFoundException => "Cartella non trovata.",
            PathTooLongException => "Il percorso del file è troppo lungo.",
            InvalidDataException invalid => SafeDetail(invalid.Message,
                "Il file non è valido o non è supportato."),
            ArgumentException argument => SafeDetail(argument.Message, "I dati inseriti non sono validi."),
            IOException => "Impossibile leggere o scrivere il file richiesto.",
            HttpRequestException => "Servizio esterno non raggiungibile.",
            CryptographicException => "I dati protetti non sono leggibili da questo utente Windows.",
            _ => "Operazione non riuscita. I dettagli tecnici restano nel registro locale."
        };
    }

    private static string SafeDetail(string? message, string fallback)
    {
        var cleaned = ForLog(message, 240);
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
