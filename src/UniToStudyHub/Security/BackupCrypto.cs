using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace UniToStudyHub.Security;

public enum BackupKeyProtection : byte
{
    None = 0,
    Dpapi = 1,
    Password = 2
}

/// <summary>Wraps the random content key of a backup for one user or device.</summary>
public interface IBackupKeyProtector
{
    string Name { get; }
    bool IsAvailable { get; }
    byte[] Wrap(byte[] key, byte[] context);
    byte[] Unwrap(byte[] wrapped, byte[] context);
}

/// <summary>Backup keys wrapped with Windows DPAPI for the current account.</summary>
public sealed class DpapiBackupKeyProtector : IBackupKeyProtector
{
    private readonly DpapiSecretProtector _dpapi = new();
    public string Name => "dpapi-windows-user";
    public bool IsAvailable => _dpapi.IsAvailable;
    public byte[] Wrap(byte[] key, byte[] context) => _dpapi.Protect(key, context);
    public byte[] Unwrap(byte[] wrapped, byte[] context) => _dpapi.Unprotect(wrapped, context);
}

public sealed record BackupContainerInfo(
    BackupKeyProtection KeyProtection,
    int Iterations,
    int PayloadLength,
    long ContainerLength);

/// <summary>
/// Self-contained encrypted backup container ("UTBK2").
///
/// Layout: magic(5) | key mode(1) | reserved(1) | iterations(4, BE) |
/// salt(16) | wrapped-key length(4, BE) | wrapped key(N) | nonce(12) |
/// payload length(4, BE) | ciphertext | GCM tag(16).
///
/// The whole header is authenticated as associated data, so altering the key
/// mode, the iteration count, the salt or the payload length makes decryption
/// fail instead of silently weakening it. The key is random per backup and is
/// either wrapped by Windows DPAPI for the current user or derived from a
/// user-chosen passphrase with PBKDF2-HMAC-SHA256. No key is ever hard-coded
/// and no key material is written next to the plaintext.
/// </summary>
public static class BackupCrypto
{
    public const string Magic = "UTBK2";
    public const int SaltBytes = 16;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;
    public const int KeyBytes = 32;
    public const int MinimumIterations = 100_000;
    public const int DefaultIterations = 600_000;
    public const int MaximumIterations = 4_000_000;
    public const int MaximumWrappedKeyBytes = 4096;
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 1024;
    public const long MaximumPayloadBytes = 8L * 1024 * 1024 * 1024;
    private const int FixedHeaderBytes = 5 + 1 + 1 + 4 + SaltBytes + 4 + NonceBytes + 4;

    public static bool LooksLikeContainer(ReadOnlySpan<byte> data) =>
        data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(System.Text.Encoding.ASCII.GetBytes(Magic));

    public static bool LooksLikeContainer(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[Magic.Length];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (stream.CanSeek) stream.Seek(-read, SeekOrigin.Current);
        return read == header.Length && header.SequenceEqual(System.Text.Encoding.ASCII.GetBytes(Magic));
    }

    public static byte[] Encrypt(byte[] plaintext, string? password, IBackupKeyProtector? protector,
        int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.Length == 0) throw new ArgumentException("The backup payload is empty.", nameof(plaintext));
        if (plaintext.LongLength > MaximumPayloadBytes)
            throw new ArgumentException("The backup payload is too large.", nameof(plaintext));

        var usePassword = !string.IsNullOrEmpty(password);
        if (usePassword) ValidatePassword(password!);
        else if (protector is null || !protector.IsAvailable)
            throw new InvalidOperationException(
                "Le copie cifrate richiedono Windows DPAPI oppure una password esplicita.");

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var key = Array.Empty<byte>();
        var wrappedKey = Array.Empty<byte>();
        var mode = usePassword ? BackupKeyProtection.Password : BackupKeyProtection.Dpapi;
        var effectiveIterations = usePassword ? Math.Clamp(iterations, MinimumIterations, MaximumIterations) : 0;
        try
        {
            key = usePassword
                ? Rfc2898DeriveBytes.Pbkdf2(password!, salt, effectiveIterations, HashAlgorithmName.SHA256, KeyBytes)
                : RandomNumberGenerator.GetBytes(KeyBytes);
            if (!usePassword) wrappedKey = protector!.Wrap(key, salt);

            var header = BuildHeader(mode, effectiveIterations, salt, wrappedKey, nonce, plaintext.Length);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagBytes];
            using (var aes = new AesGcm(key, TagBytes))
                aes.Encrypt(nonce, plaintext, ciphertext, tag, header);

            var container = new byte[header.Length + ciphertext.Length + TagBytes];
            header.CopyTo(container, 0);
            ciphertext.CopyTo(container, header.Length);
            tag.CopyTo(container, header.Length + ciphertext.Length);
            return container;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(wrappedKey);
        }
    }

    public static byte[] Decrypt(byte[] container, string? password, IBackupKeyProtector? protector)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!TryReadInfo(container, out var info, out _) || info is null)
            throw new InvalidDataException(
                "La copia di sicurezza non ha un formato supportato o è danneggiata.");

        var salt = container.AsSpan(11, SaltBytes).ToArray();
        var wrappedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(27, 4));
        var wrappedKey = container.AsSpan(31, wrappedLength).ToArray();
        var nonce = container.AsSpan(31 + wrappedLength, NonceBytes).ToArray();
        var payloadOffset = 31 + wrappedLength + NonceBytes + 4;
        var ciphertext = container.AsSpan(payloadOffset, info.PayloadLength).ToArray();
        var tag = container.AsSpan(payloadOffset + info.PayloadLength, TagBytes).ToArray();
        var associatedData = container.AsSpan(0, payloadOffset).ToArray();

        var key = Array.Empty<byte>();
        var plaintext = new byte[info.PayloadLength];
        try
        {
            key = info.KeyProtection switch
            {
                BackupKeyProtection.Password => DeriveKey(password, salt, info.Iterations),
                BackupKeyProtection.Dpapi => (protector is null || !protector.IsAvailable
                        ? throw new InvalidDataException(
                            "La copia è protetta da Windows DPAPI e non può essere aperta con l'account corrente.")
                        : protector.Unwrap(wrappedKey, salt)),
                _ => throw new InvalidDataException("La copia usa una modalità di cifratura non supportata.")
            };
            if (key.Length != KeyBytes)
                throw new InvalidDataException("La chiave della copia di sicurezza non è valida.");

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException(
                "La copia di sicurezza non può essere aperta: password o chiave errata, oppure file danneggiato.",
                ex);
        }
        catch (FormatException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("La copia di sicurezza è danneggiata.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(wrappedKey);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public static bool TryReadInfo(ReadOnlySpan<byte> container, out BackupContainerInfo? info, out string? error)
    {
        info = null;
        error = null;
        if (container.Length < FixedHeaderBytes)
        {
            error = "Il file è troppo piccolo per essere una copia di sicurezza.";
            return false;
        }
        if (!container[..Magic.Length].SequenceEqual(System.Text.Encoding.ASCII.GetBytes(Magic)))
        {
            error = "Il file non è una copia di sicurezza UniTracker.";
            return false;
        }
        var modeByte = container[5];
        if (modeByte != (byte)BackupKeyProtection.Dpapi && modeByte != (byte)BackupKeyProtection.Password)
        {
            error = "Modalità di cifratura sconosciuta.";
            return false;
        }
        if (container[6] != 0)
        {
            error = "Il campo riservato dell'intestazione non è valido.";
            return false;
        }
        var mode = (BackupKeyProtection)modeByte;
        var iterations = BinaryPrimitives.ReadInt32BigEndian(container.Slice(7, 4));
        if (mode == BackupKeyProtection.Password &&
            iterations is < MinimumIterations or > MaximumIterations)
        {
            error = "Il numero di iterazioni della copia non è accettabile.";
            return false;
        }
        if (mode == BackupKeyProtection.Dpapi && iterations != 0)
        {
            error = "L'intestazione della copia non è valida.";
            return false;
        }

        var wrappedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(container.Slice(27, 4));
        if (wrappedLength is < 0 or > MaximumWrappedKeyBytes)
        {
            error = "La chiave protetta della copia non è valida.";
            return false;
        }
        if (mode == BackupKeyProtection.Password && wrappedLength != 0)
        {
            error = "L'intestazione della copia non è valida.";
            return false;
        }
        if (mode == BackupKeyProtection.Dpapi && wrappedLength == 0)
        {
            error = "La chiave protetta della copia è assente.";
            return false;
        }

        var payloadOffset = 31 + wrappedLength + NonceBytes + 4;
        if (container.Length < payloadOffset + TagBytes + 1)
        {
            error = "La copia di sicurezza è troncata.";
            return false;
        }
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(container.Slice(payloadOffset - 4, 4));
        if (payloadLength <= 0)
        {
            error = "La dimensione del contenuto non è valida.";
            return false;
        }
        if (container.Length != (long)payloadOffset + payloadLength + TagBytes)
        {
            error = "La copia di sicurezza è troncata o contiene dati aggiuntivi.";
            return false;
        }

        info = new BackupContainerInfo(mode, iterations, payloadLength, container.Length);
        return true;
    }

    private static byte[] BuildHeader(
        BackupKeyProtection mode,
        int iterations,
        byte[] salt,
        byte[] wrappedKey,
        byte[] nonce,
        int payloadLength)
    {
        var header = new byte[31 + wrappedKey.Length + NonceBytes + 4];
        System.Text.Encoding.ASCII.GetBytes(Magic, 0, Magic.Length, header, 0);
        header[5] = (byte)mode;
        header[6] = 0;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(7, 4), iterations);
        salt.CopyTo(header, 11);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(27, 4), (uint)wrappedKey.Length);
        wrappedKey.CopyTo(header, 31);
        nonce.CopyTo(header, 31 + wrappedKey.Length);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(31 + wrappedKey.Length + NonceBytes, 4), payloadLength);
        return header;
    }

    private static byte[] DeriveKey(string? password, byte[] salt, int iterations)
    {
        if (string.IsNullOrEmpty(password))
            throw new InvalidDataException("La copia richiede una password.");
        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            throw new InvalidDataException("La password della copia non ha una lunghezza valida.");
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < MinimumPasswordLength)
            throw new ArgumentException(
                $"La password deve avere almeno {MinimumPasswordLength} caratteri.", nameof(password));
        if (password.Length > MaximumPasswordLength)
            throw new ArgumentException("La password è troppo lunga.", nameof(password));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("La password non può essere composta solo da spazi.", nameof(password));
    }
}
