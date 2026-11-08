using System.Security.Cryptography;
using UniToStudyHub.Security;
using UniToStudyHub.Services;

namespace UniToStudyHub.Tests.Security;

/// <summary>
/// Test-only protector. Each instance owns a random in-memory key, so two
/// instances cannot read each other's blobs, mirroring "another Windows user".
/// No key is hard-coded and nothing is persisted.
/// </summary>
internal sealed class TestSecretProtector : ISecretProtector
{
    private readonly byte[] _masterKey = RandomNumberGenerator.GetBytes(32);

    public string Name => "test-aes-gcm";
    public bool IsAvailable => true;

    public byte[] Protect(byte[] plaintext, byte[] context)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Derive(context), 16))
            aes.Encrypt(nonce, plaintext, ciphertext, tag, context);
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        ciphertext.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    public byte[] Unprotect(byte[] ciphertext, byte[] context)
    {
        if (ciphertext.Length < 28) throw new CryptographicException("Damaged test blob.");
        var nonce = ciphertext.AsSpan(0, 12).ToArray();
        var tag = ciphertext.AsSpan(12, 16).ToArray();
        var payload = ciphertext.AsSpan(28).ToArray();
        var plaintext = new byte[payload.Length];
        using var aes = new AesGcm(Derive(context), 16);
        aes.Decrypt(nonce, payload, tag, plaintext, context);
        return plaintext;
    }

    private byte[] Derive(byte[]? context)
    {
        using var hmac = new HMACSHA256(_masterKey);
        return hmac.ComputeHash(context is { Length: > 0 } ? context : Array.Empty<byte>());
    }
}

/// <summary>Test-only backup key wrapping with a random per-instance key.</summary>
internal sealed class TestBackupKeyProtector : IBackupKeyProtector
{
    private readonly TestSecretProtector _inner = new();

    public string Name => "test-wrapped-key";
    public bool IsAvailable => true;
    public byte[] Wrap(byte[] key, byte[] context) => _inner.Protect(key, context);
    public byte[] Unwrap(byte[] wrapped, byte[] context) => _inner.Unprotect(wrapped, context);
}

internal sealed class InMemoryMoodleTokenStore : IMoodleTokenStore
{
    private string? _token;
    public bool HasToken => _token is not null;
    public string? Load() => _token;
    public void Save(string token) => _token = token;
    public bool Delete()
    {
        var existed = _token is not null;
        _token = null;
        return existed;
    }
}

internal sealed class CountingAiProvider : IAiProvider
{
    private readonly Func<AiRequest, string> _response;
    public CountingAiProvider(Func<AiRequest, string> response) => _response = response;
    public int Calls { get; private set; }
    public bool IsConfigured => true;
    public string Name => "counting-mock";

    public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new AiResponse(AiStatus.Configured, _response(request), Name));
    }
}
