using System.IO;
using System.Text;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// DPAPI-backed storage for AI provider keys.
///
/// <see cref="AiSettings"/> only ever holds the <em>name</em> of a key
/// (<c>ApiKeySecretName</c>) or the name of an environment variable. The value
/// lives either in the protected store or in the user's environment, is read at
/// call time, and is never written to StudyData, exports, logs or messages.
/// </summary>
public sealed class AiCredentialStore
{
    public const string SecretNamePrefix = "ai-key.";
    private static readonly byte[] EntropyContext = Encoding.UTF8.GetBytes("UniToStudyHub.AiKey.v1");
    private readonly ProtectedSecretStore _store;

    public AiCredentialStore(string dataDirectory, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        _store = new ProtectedSecretStore(Path.Combine(root, "secrets"), protector ?? new DpapiSecretProtector(),
            EntropyContext);
    }

    public static AiCredentialStore Default => new(StudyHubDatabase.GetDataDirectory());

    public bool IsProtectionAvailable => _store.IsAvailable;

    public IReadOnlyList<string> List() =>
        _store.List()
            .Where(name => name.StartsWith(SecretNamePrefix, StringComparison.Ordinal))
            .Select(name => name[SecretNamePrefix.Length..])
            .ToList();

    public void SaveKey(string name, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("The API key is empty.", nameof(apiKey));
        _store.Write(SecretName(name), apiKey.Trim());
    }

    public bool TryLoadKey(string name, out string? apiKey) =>
        _store.TryRead(SecretName(name), out apiKey);

    public string? LoadKey(string name)
    {
        TryLoadKey(name, out var key);
        return key;
    }

    public bool DeleteKey(string name) => _store.Delete(SecretName(name));

    /// <summary>Deletes AI keys only; the Moodle token in the same folder survives.</summary>
    public int DeleteAll()
    {
        var deleted = 0;
        foreach (var name in List())
            if (DeleteKey(name)) deleted++;
        return deleted;
    }

    public static bool IsValidName(string? name) =>
        ProtectedSecretStore.IsValidName(name) && !name!.StartsWith(SecretNamePrefix, StringComparison.Ordinal);

    private static string SecretName(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("The AI key name is not valid.", nameof(name));
        return SecretNamePrefix + name;
    }
}
