using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UniToStudyHub.Security;

/// <summary>
/// Protects secret bytes for the current OS user. Implementations must never
/// return the plaintext in an exception message and must not be reversible by
/// another account on the same machine.
/// </summary>
public interface ISecretProtector
{
    string Name { get; }
    bool IsAvailable { get; }
    byte[] Protect(byte[] plaintext, byte[] context);
    byte[] Unprotect(byte[] ciphertext, byte[] context);
}

/// <summary>
/// Windows DPAPI (current user, no prompt). The <c>context</c> argument is used
/// as optional entropy so a blob written for one secret name cannot be moved to
/// another name and still decrypt.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;
    public string Name => "dpapi";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] plaintext, byte[] context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Transform(protect: true, plaintext, context ?? Array.Empty<byte>());
    }

    public byte[] Unprotect(byte[] ciphertext, byte[] context)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return Transform(protect: false, ciphertext, context ?? Array.Empty<byte>());
    }

    private static byte[] Transform(bool protect, byte[] inputBytes, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is only available on Windows.");
        if (inputBytes.Length == 0) throw new ArgumentException("The secret is empty.", nameof(inputBytes));

        var input = new DataBlob { Size = inputBytes.Length, Data = Marshal.AllocHGlobal(inputBytes.Length) };
        var entropyBytesPointer = IntPtr.Zero;
        var entropyStructPointer = IntPtr.Zero;
        var output = new DataBlob();
        try
        {
            Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);
            if (entropy.Length > 0)
            {
                entropyBytesPointer = Marshal.AllocHGlobal(entropy.Length);
                Marshal.Copy(entropy, 0, entropyBytesPointer, entropy.Length);
                entropyStructPointer = Marshal.AllocHGlobal(Marshal.SizeOf<DataBlob>());
                Marshal.StructureToPtr(new DataBlob { Size = entropy.Length, Data = entropyBytesPointer },
                    entropyStructPointer, false);
            }

            var ok = protect
                ? CryptProtectData(ref input, "UniToStudyHub secret", entropyStructPointer, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, entropyStructPointer, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                // Never include the payload or the entropy.
                var failure = new CryptographicException(
                    protect
                        ? "The secret could not be protected for the current Windows user."
                        : "The protected secret does not belong to the current Windows user or is damaged.");
                failure.HResult = error;
                throw failure;
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                ZeroMemory(output.Data, output.Size);
                LocalFree(output.Data);
            }
            if (input.Data != IntPtr.Zero)
            {
                ZeroMemory(input.Data, input.Size);
                Marshal.FreeHGlobal(input.Data);
            }
            if (entropyBytesPointer != IntPtr.Zero)
            {
                ZeroMemory(entropyBytesPointer, entropy.Length);
                Marshal.FreeHGlobal(entropyBytesPointer);
            }
            if (entropyStructPointer != IntPtr.Zero) Marshal.FreeHGlobal(entropyStructPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static void ZeroMemory(IntPtr destination, int length)
    {
        if (destination == IntPtr.Zero || length <= 0) return;
        // RtlSecureZeroMemory is a compiler intrinsic, not a kernel32 export.
        // Marshal.Copy performs a native write and cannot be optimized away by
        // the managed JIT; the buffer contains zeros only.
        Marshal.Copy(new byte[length], 0, destination, length);
    }
}

public enum SecretReadStatus
{
    Missing,
    Available,
    Corrupted,
    Unreadable
}

public sealed record SecretReadResult(SecretReadStatus Status, string? Value)
{
    public bool IsAvailable => Status == SecretReadStatus.Available;
}

/// <summary>
/// File-backed secret store. Every blob is protected with
/// <see cref="ISecretProtector"/>, written atomically into a folder restricted
/// to the current user, never logged, and removed with a scrub on disconnect.
///
/// The app-level <c>context</c> is a scope tag, not an encryption key: on
/// Windows the actual key material is owned by DPAPI. Swapping or copying a
/// blob between machines or Windows accounts therefore fails closed.
/// </summary>
public sealed class ProtectedSecretStore
{
    public const string EnvelopeMagic = "UTSK";
    public const byte EnvelopeVersion = 1;
    public const int MaximumSecretBytes = 8192;
    public const int MaximumEnvelopeBytes = MaximumSecretBytes + 4096;
    private const string Extension = ".secret";
    private static readonly char[] InvalidNameCharacters = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    private readonly ISecretProtector _protector;
    private readonly byte[] _context;
    private readonly string _directory;

    public ProtectedSecretStore(string directory, ISecretProtector protector, byte[]? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _directory = Path.GetFullPath(directory);
        _context = context is { Length: > 0 } ? (byte[])context.Clone() : Array.Empty<byte>();
    }

    public string StorageDirectory => _directory;
    public string ProtectorName => _protector.Name;
    public bool IsAvailable => _protector.IsAvailable;

    public bool Exists(string name)
    {
        var path = PathFor(name);
        return File.Exists(path);
    }

    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();
        try
        {
            return System.IO.Directory.EnumerateFiles(_directory, "*" + Extension, SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(IsValidName)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reads a secret. Missing, damaged or foreign blobs are reported as a
    /// status instead of throwing: a corrupted vault must show "reconnect"
    /// rather than a raw cryptographic error, and must never fall back to a
    /// stale token.
    /// </summary>
    public SecretReadResult Read(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return new SecretReadResult(SecretReadStatus.Missing, null);
        byte[] envelope;
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumEnvelopeBytes)
                return new SecretReadResult(SecretReadStatus.Corrupted, null);
            envelope = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SecretReadResult(SecretReadStatus.Unreadable, null);
        }

        var payload = Array.Empty<byte>();
        var plaintext = Array.Empty<byte>();
        try
        {
            if (!TryUnwrap(envelope, out payload)) return new SecretReadResult(SecretReadStatus.Corrupted, null);
            plaintext = _protector.Unprotect(payload, EntropyFor(name));
            var value = Encoding.UTF8.GetString(plaintext);
            return new SecretReadResult(SecretReadStatus.Available, value);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException
                                       or IOException or ArgumentException)
        {
            return new SecretReadResult(SecretReadStatus.Corrupted, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    public bool TryRead(string name, out string? value)
    {
        var result = Read(name);
        value = result.Value;
        return result.IsAvailable;
    }

    public void Write(string name, string value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("The secret is empty.", nameof(value));
        var plaintext = Encoding.UTF8.GetBytes(value);
        if (plaintext.Length > MaximumSecretBytes)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new ArgumentException("The secret is too large to store.", nameof(value));
        }

        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = _protector.Protect(plaintext, EntropyFor(name));
            var envelope = Wrap(protectedBytes);
            LocalFileSecurity.WriteAllBytesAtomically(PathFor(name), envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public Task WriteAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        var plaintext = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (plaintext.Length == 0 || plaintext.Length > MaximumSecretBytes)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return Task.FromException(new ArgumentException("The secret is empty or too large.", nameof(value)));
        }
        try
        {
            var protectedBytes = _protector.Protect(plaintext, EntropyFor(name));
            var envelope = Wrap(protectedBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
            return LocalFileSecurity.WriteAllBytesAtomicallyAsync(PathFor(name), envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Deletes one secret and scrubs its bytes first.</summary>
    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return false;
        LocalFileSecurity.ScrubAndDelete(path);
        return !File.Exists(path);
    }

    public int DeleteAll()
    {
        var deleted = 0;
        foreach (var name in List())
            if (Delete(name)) deleted++;
        return deleted;
    }

    public string PathFor(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("The secret name is not valid.", nameof(name));
        return Path.Combine(_directory, name + Extension);
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 64 &&
        name[0] is >= 'a' and <= 'z' or >= '0' and <= '9' &&
        name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_') &&
        !name.Contains("..", StringComparison.Ordinal) &&
        name.IndexOfAny(InvalidNameCharacters) < 0;

    private byte[] EntropyFor(string name)
    {
        // HMAC(context, name) is a deterministic scope tag, not a key: the
        // confidentiality comes from DPAPI. Binding it to the name prevents
        // copying one secret blob over another.
        var nameBytes = Encoding.UTF8.GetBytes(name);
        if (_context.Length == 0) return nameBytes;
        using var hmac = new HMACSHA256(_context);
        return hmac.ComputeHash(nameBytes);
    }

    private static byte[] Wrap(byte[] protectedBytes)
    {
        var envelope = new byte[EnvelopeMagic.Length + 1 + protectedBytes.Length];
        Encoding.ASCII.GetBytes(EnvelopeMagic, 0, EnvelopeMagic.Length, envelope, 0);
        envelope[EnvelopeMagic.Length] = EnvelopeVersion;
        protectedBytes.CopyTo(envelope, EnvelopeMagic.Length + 1);
        return envelope;
    }

    private static bool TryUnwrap(byte[] envelope, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (envelope.Length <= EnvelopeMagic.Length + 1) return false;
        if (!envelope.AsSpan(0, EnvelopeMagic.Length)
                .SequenceEqual(Encoding.ASCII.GetBytes(EnvelopeMagic))) return false;
        if (envelope[EnvelopeMagic.Length] != EnvelopeVersion) return false;
        payload = envelope[(EnvelopeMagic.Length + 1)..];
        return true;
    }
}
