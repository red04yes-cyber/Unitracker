using System.IO;
using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Security;

namespace UniToStudyHub.Infrastructure.Data;

/// <summary>
/// Generates one random database key and stores only its DPAPI-protected
/// envelope for the current Windows account. A damaged vault fails closed:
/// replacing its key would make an existing database permanently unreadable.
/// </summary>
public sealed class DatabaseKeyVault
{
    public const string SecretName = "database-key";
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("UniToStudyHub.SQLite.DatabaseKey.v1");
    private readonly ProtectedSecretStore _store;

    public DatabaseKeyVault(string dataDirectory, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _store = new ProtectedSecretStore(
            Path.Combine(Path.GetFullPath(dataDirectory), "secrets"),
            protector ?? new DpapiSecretProtector(),
            Context);
    }

    public string GetOrCreateKey()
    {
        var stored = _store.Read(SecretName);
        if (stored.IsAvailable)
        {
            try
            {
                var key = Convert.FromBase64String(stored.Value!);
                if (key.Length == 32) return Convert.ToHexString(key);
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("La chiave del database protetta non ha una dimensione valida.");
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("La chiave del database protetta è danneggiata.", ex);
            }
        }

        if (stored.Status != SecretReadStatus.Missing)
            throw new CryptographicException(
                "La chiave del database non è leggibile con questo account Windows. I dati non sono stati modificati.");

        if (!_store.IsAvailable)
            throw new PlatformNotSupportedException(
                "La cifratura del database richiede Windows DPAPI e un account Windows.");

        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            _store.Write(SecretName, Convert.ToBase64String(bytes));
            return Convert.ToHexString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
