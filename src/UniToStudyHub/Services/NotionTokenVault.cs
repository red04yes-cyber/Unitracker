using System.Text;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public interface INotionTokenStore
{
    bool HasToken { get; }
    string? Load();
    void Save(string token);
    bool Delete();
}

/// <summary>
/// Stores the Notion integration token outside StudyData, backups and logs.
/// The protected secret is scoped independently from Moodle and AI credentials.
/// </summary>
public sealed class NotionTokenVault : INotionTokenStore
{
    public const string SecretName = "notion-api-token";
    private static readonly byte[] EntropyContext = Encoding.UTF8.GetBytes("UniToStudyHub.NotionToken.v1");
    private readonly ProtectedSecretStore _store;

    public NotionTokenVault(string dataDirectory, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        _store = new ProtectedSecretStore(Path.Combine(root, "secrets"),
            protector ?? new DpapiSecretProtector(), EntropyContext);
    }

    public static NotionTokenVault Default => new(StudyHubDatabase.GetDataDirectory());

    public bool IsProtectionAvailable => _store.IsAvailable;
    public bool HasToken => _store.Exists(SecretName);

    public string? Load()
    {
        var result = _store.Read(SecretName);
        return result.IsAvailable ? result.Value : null;
    }

    public void Save(string token)
    {
        var normalized = Normalize(token);
        _store.Write(SecretName, normalized);
        AppDiagnostics.Info("notion", "Token dell'integrazione Notion archiviato con protezione DPAPI.");
    }

    public bool Delete()
    {
        var deleted = _store.Delete(SecretName);
        if (deleted) AppDiagnostics.Info("notion", "Credenziali Notion locali rimosse.");
        return deleted;
    }

    public static string Normalize(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Inserisci il token dell'integrazione Notion.", nameof(token));

        var normalized = token.Trim();
        if (normalized.Length is < 8 or > ProtectedSecretStore.MaximumSecretBytes ||
            normalized.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("Il token Notion non ha un formato valido.", nameof(token));

        return normalized;
    }
}
