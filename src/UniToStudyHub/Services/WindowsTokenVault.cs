using System.IO;
using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>Storage contract for the Moodle web-service token.</summary>
public interface IMoodleTokenStore
{
    bool HasToken { get; }
    string? Load();
    void Save(string token);
    bool Delete();
}

/// <summary>
/// DPAPI-backed Moodle token vault.
///
/// The token lives in <c>%LOCALAPPDATA%\UniToStudyHub\secrets\moodle-token.secret</c>,
/// encrypted with Windows DPAPI for the current account, scoped with optional
/// entropy and written atomically into a folder restricted to the user. The
/// previous <c>token.bin</c> is migrated once and scrubbed; a corrupted or
/// foreign blob is reported as "not connected" instead of falling back to a
/// stale token. The token never enters StudyData, backups, URLs or logs.
/// </summary>
public sealed class MoodleTokenVault : IMoodleTokenStore
{
    public const string SecretName = "moodle-token";
    public const string LegacyFileName = "token.bin";
    private static readonly byte[] EntropyContext = Encoding.UTF8.GetBytes("UniToStudyHub.MoodleToken.v1");
    private readonly ISecretProtector _protector;
    private readonly ProtectedSecretStore _store;
    private readonly string _legacyPath;

    public MoodleTokenVault(string dataDirectory, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _protector = protector ?? new DpapiSecretProtector();
        var root = Path.GetFullPath(dataDirectory);
        _store = new ProtectedSecretStore(Path.Combine(root, "secrets"), _protector, EntropyContext);
        _legacyPath = Path.Combine(root, LegacyFileName);
    }

    public static MoodleTokenVault Default => new(StudyHubDatabase.GetDataDirectory());

    public bool IsProtectionAvailable => _store.IsAvailable;
    public string ProtectorName => _store.ProtectorName;
    public bool HasToken => _store.Exists(SecretName) || File.Exists(_legacyPath);

    public string? Load()
    {
        var stored = _store.Read(SecretName);
        if (stored.IsAvailable) return stored.Value;
        // A corrupted or foreign blob is a hard stop: silently minting a new
        // session from the legacy file could resurrect a revoked token.
        if (stored.Status != SecretReadStatus.Missing) return null;
        return MigrateLegacyToken();
    }

    public void Save(string token)
    {
        var normalized = Normalize(token);
        _store.Write(SecretName, normalized);
        LocalFileSecurity.ScrubAndDelete(_legacyPath);
        AppDiagnostics.Info("moodle", "Token Moodle archiviato con protezione DPAPI.");
    }

    public bool Delete()
    {
        var deleted = _store.Delete(SecretName);
        var legacyExisted = File.Exists(_legacyPath);
        LocalFileSecurity.ScrubAndDelete(_legacyPath);
        if (deleted || legacyExisted) AppDiagnostics.Info("moodle", "Credenziali Moodle locali rimosse.");
        return deleted || legacyExisted;
    }

    public static string Normalize(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Inserisci il token Moodle.", nameof(token));
        var normalized = token.Trim();
        if (normalized.Length is < 8 or > 512 || normalized.Any(character => character is < (char)0x21 or > (char)0x7E))
            throw new ArgumentException("Il token Moodle non ha un formato valido.", nameof(token));
        return normalized;
    }

    private string? MigrateLegacyToken()
    {
        if (!File.Exists(_legacyPath)) return null;
        byte[]? protectedBytes = null;
        byte[]? plaintext = null;
        try
        {
            var info = new FileInfo(_legacyPath);
            if (info.Length <= 0 || info.Length > ProtectedSecretStore.MaximumEnvelopeBytes) return null;
            protectedBytes = File.ReadAllBytes(_legacyPath);
            plaintext = _protector.Unprotect(protectedBytes, Array.Empty<byte>());
            var token = Normalize(Encoding.UTF8.GetString(plaintext));
            _store.Write(SecretName, token);
            LocalFileSecurity.ScrubAndDelete(_legacyPath);
            AppDiagnostics.Info("moodle", "Token Moodle migrato nel nuovo archivio protetto.");
            return token;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
                                       or ArgumentException or PlatformNotSupportedException)
        {
            AppDiagnostics.Warning("moodle", "Il token Moodle precedente non è leggibile; richiedi un nuovo token.");
            return null;
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

/// <summary>
/// Compatibility facade for the original API. New code should depend on
/// <see cref="MoodleTokenVault"/> (or <see cref="IMoodleTokenStore"/>).
/// </summary>
public static class WindowsTokenVault
{
    public static bool HasToken => MoodleTokenVault.Default.HasToken;
    public static string? Load() => MoodleTokenVault.Default.Load();
    public static void Save(string token) => MoodleTokenVault.Default.Save(token);
    public static bool Delete() => MoodleTokenVault.Default.Delete();
}
