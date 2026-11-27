using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Security;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Security;

public sealed class BackupSecurityTests
{
    private const string Password = "correct horse battery staple";
    private const string Marker = "SQLite format 3";

    private static StudyData Data(string courseName) => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = courseName, Cfu = 6 }
        }
    };

    private static async Task<(SqliteTestDatabase Database, string Path, string Backups, BackupService Service)>
        CreateAsync()
    {
        var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var backups = Path.Combine(Path.GetDirectoryName(path)!, "backups");
        var service = new BackupService(database.Factory, path, backups, new TestBackupKeyProtector());
        return (database, path, backups, service);
    }

    [Fact]
    public async Task EncryptedBackupRoundTripsWithAPassword()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));

        var backup = await fixture.Service.CreateBackupAsync(Password);

        Assert.True(backup.IsEncrypted);
        Assert.Equal(BackupService.CurrentFormatVersion, backup.FormatVersion);
        Assert.EndsWith(BackupService.ContainerExtension, backup.FilePath);
        var container = await File.ReadAllBytesAsync(backup.FilePath);
        Assert.True(container.AsSpan(0, 5).SequenceEqual("UTBK2"u8));
        Assert.DoesNotContain(Marker, Encoding.ASCII.GetString(container), StringComparison.Ordinal);

        Assert.True(await fixture.Service.VerifyBackupAsync(backup.FilePath, Password));
        var manifest = await fixture.Service.ReadManifestAsync(backup.FilePath);
        Assert.NotNull(manifest);
        Assert.Equal(BackupService.CurrentFormatVersion, manifest!.FormatVersion);
        Assert.Equal("aes-256-gcm", manifest.Encryption);
        Assert.Equal(backup.Sha256, manifest.Sha256);
        Assert.Equal(backup.ContainerSha256, manifest.CiphertextSha256);

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);

        await fixture.Service.RestoreAsync(backup.FilePath, Password);
        Assert.Equal("Analisi", (await store.LoadAsync()).Courses[0].Name);
    }

    [Fact]
    public async Task WrongPasswordIsRejectedAndNeverReplacesTheDatabase()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        var backup = await fixture.Service.CreateBackupAsync(Password);

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);

        Assert.False(await fixture.Service.VerifyBackupAsync(backup.FilePath, "another wrong passphrase"));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.RestoreAsync(backup.FilePath, "another wrong passphrase"));

        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(backup.FilePath, exception.Message, StringComparison.Ordinal);
        Assert.Equal("Modificata", (await store.LoadAsync()).Courses[0].Name);
    }

    [Fact]
    public async Task TamperedContainerIsRejected()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        await new SqliteStudyDataStore(database.Factory).SaveAsync(Data("Analisi"));
        var backup = await fixture.Service.CreateBackupAsync(Password);

        var container = await File.ReadAllBytesAsync(backup.FilePath);
        container[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(backup.FilePath, container);

        Assert.False(await fixture.Service.VerifyBackupAsync(backup.FilePath, Password));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.RestoreAsync(backup.FilePath, Password));
    }

    [Fact]
    public async Task TruncatedContainerIsRejected()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        await new SqliteStudyDataStore(database.Factory).SaveAsync(Data("Analisi"));
        var backup = await fixture.Service.CreateBackupAsync(Password);

        var container = await File.ReadAllBytesAsync(backup.FilePath);
        await File.WriteAllBytesAsync(backup.FilePath, container[..(container.Length / 2)]);

        Assert.False(await fixture.Service.VerifyBackupAsync(backup.FilePath, Password));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.RestoreAsync(backup.FilePath, Password));
    }

    [Fact]
    public async Task BackupFromANewerFormatIsRejectedBeforeTouchingTheDatabase()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        var backup = await fixture.Service.CreateBackupAsync(Password);
        var manifest = await fixture.Service.ReadManifestAsync(backup.FilePath);
        Assert.NotNull(manifest);

        await File.WriteAllTextAsync(BackupService.ManifestPath(backup.FilePath),
            JsonSerializer.Serialize(manifest! with { FormatVersion = 99 }));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.RestoreAsync(backup.FilePath, Password));
        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Analisi", (await store.LoadAsync()).Courses[0].Name);
    }

    [Fact]
    public async Task LegacyPlaintextBackupRemainsVerifiableAndRestorable()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        Directory.CreateDirectory(fixture.Backups);
        var legacy = Path.Combine(fixture.Backups, "unitracker-20260101-000000-000-legacy.db");
        // Produce the legacy file by decrypting a verified snapshot, so the test
        // never copies a database that still has an active write-ahead log.
        var encrypted = await fixture.Service.CreateBackupAsync(Password);
        var payload = BackupCrypto.Decrypt(await File.ReadAllBytesAsync(encrypted.FilePath), Password, null);
        try
        {
            await File.WriteAllBytesAsync(legacy, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        Assert.True(await fixture.Service.VerifyBackupAsync(legacy));

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);

        await fixture.Service.RestoreAsync(legacy);
        Assert.Equal("Analisi", (await store.LoadAsync()).Courses[0].Name);
    }

    [Fact]
    public async Task InterruptedWritesAreNotListedOrRestorable()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        Directory.CreateDirectory(fixture.Backups);
        var partial = Path.Combine(fixture.Backups, "unitracker-20260101-000000-000-dead.utbk.part");
        await File.WriteAllTextAsync(partial, "partial backup");

        Assert.DoesNotContain(fixture.Service.ListBackups(), backup => backup.FilePath == partial);
        Assert.False(await fixture.Service.VerifyBackupAsync(partial));

        File.SetLastWriteTimeUtc(partial, DateTime.UtcNow.AddHours(-2));
        var removed = LocalFileSecurity.CleanupStaleTemporaryFiles(fixture.Backups, TimeSpan.FromHours(1), "*.part");
        Assert.Equal(1, removed);
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task BackupPayloadNeverContainsStoredCredentials()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        await new SqliteStudyDataStore(database.Factory).SaveAsync(Data("Analisi"));
        var dataDirectory = Path.GetDirectoryName(fixture.Path)!;
        var token = "abcdef0123456789abcdef0123456789";
        new MoodleTokenVault(dataDirectory, new TestSecretProtector()).Save(token);

        var backup = await fixture.Service.CreateBackupAsync(Password);
        var container = await File.ReadAllBytesAsync(backup.FilePath);
        var payload = BackupCrypto.Decrypt(container, Password, null);
        try
        {
            var text = Encoding.UTF8.GetString(payload);
            Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    [Fact]
    public async Task BoundedBackupReadersRejectOversizedFilesAndRemovePartialCopies()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.bin");
        var destination = Path.Combine(folder, "copy.bin");
        await File.WriteAllBytesAsync(source, Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BackupService.ReadFileBoundedAsync(source, 16, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BackupService.CopyFileBoundedAsync(source, destination, 16, CancellationToken.None));

        Assert.False(File.Exists(destination));
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task BoundedBackupReadersPreserveFilesWithinTheLimit()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.bin");
        var destination = Path.Combine(folder, "copy.bin");
        var expected = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        await File.WriteAllBytesAsync(source, expected);

        var read = await BackupService.ReadFileBoundedAsync(source, expected.Length, CancellationToken.None);
        await BackupService.CopyFileBoundedAsync(source, destination, expected.Length, CancellationToken.None);

        Assert.Equal(expected, read);
        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        CryptographicOperations.ZeroMemory(read);
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task OversizedManifestIsRejectedBeforeVerificationOrRestore()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        var backup = await fixture.Service.CreateBackupAsync(Password);
        await File.WriteAllBytesAsync(BackupService.ManifestPath(backup.FilePath),
            new byte[BackupService.MaximumManifestBytes + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.ReadManifestAsync(backup.FilePath));
        Assert.False(await fixture.Service.VerifyBackupAsync(backup.FilePath, Password));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.RestoreAsync(backup.FilePath, Password));
        Assert.Equal("Analisi", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task LegacyRestoreRejectsManifestChecksumMismatchBeforeReplacingDatabase()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        var encrypted = await fixture.Service.CreateBackupAsync(Password);
        var container = await File.ReadAllBytesAsync(encrypted.FilePath);
        var payload = BackupCrypto.Decrypt(container, Password, null);
        CryptographicOperations.ZeroMemory(container);

        var legacy = Path.Combine(fixture.Backups, "unitracker-20260102-000000-000-checksum.db");
        try
        {
            await File.WriteAllBytesAsync(legacy, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        var manifest = new BackupManifest(BackupService.LegacyFormatVersion, DateTimeOffset.UtcNow,
            new string('0', 64), new FileInfo(legacy).Length, "none", "none", null,
            new FileInfo(legacy).Length);
        await File.WriteAllTextAsync(BackupService.ManifestPath(legacy), JsonSerializer.Serialize(manifest));

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);

        Assert.False(await fixture.Service.VerifyBackupAsync(legacy));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.RestoreAsync(legacy));
        Assert.Equal("Modificata", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task LegacyRestoreRejectsManifestLengthMismatchEvenWhenChecksumMatches()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data("Analisi"));
        var encrypted = await fixture.Service.CreateBackupAsync(Password);
        var container = await File.ReadAllBytesAsync(encrypted.FilePath);
        var payload = BackupCrypto.Decrypt(container, Password, null);
        CryptographicOperations.ZeroMemory(container);

        var legacy = Path.Combine(fixture.Backups, "unitracker-20260103-000000-000-length.db");
        string payloadHash;
        try
        {
            await File.WriteAllBytesAsync(legacy, payload);
            payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        var actualLength = new FileInfo(legacy).Length;
        var manifest = new BackupManifest(BackupService.LegacyFormatVersion, DateTimeOffset.UtcNow,
            payloadHash, actualLength, "none", "none", null, actualLength + 1);
        await File.WriteAllTextAsync(BackupService.ManifestPath(legacy), JsonSerializer.Serialize(manifest));

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);

        Assert.False(await fixture.Service.VerifyBackupAsync(legacy));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.RestoreAsync(legacy));
        Assert.Equal("Modificata", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task UnsupportedBackupExtensionIsRejected()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var unsupported = Path.Combine(fixture.Backups, "unitracker-not-a-backup.bin");
        Directory.CreateDirectory(fixture.Backups);
        await File.WriteAllTextAsync(unsupported, "not a backup");

        Assert.False(await fixture.Service.VerifyBackupAsync(unsupported));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.RestoreAsync(unsupported));
    }

    [Fact]
    public async Task UnencryptedBackupCreationNeedsDpapiOrAPassword()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        var unavailable = new BackupService(database.Factory, fixture.Path, fixture.Backups);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.CreateBackupAsync());
    }

    [Fact]
    public async Task RetentionKeepsTheNewestCopies()
    {
        var fixture = await CreateAsync();
        await using var database = fixture.Database;
        await new SqliteStudyDataStore(database.Factory).SaveAsync(Data("Analisi"));
        await fixture.Service.CreateBackupAsync(Password);
        await fixture.Service.CreateBackupAsync(Password);
        await fixture.Service.CreateBackupAsync(Password);

        var deleted = await fixture.Service.PruneAsync(2, TimeSpan.FromDays(90));

        Assert.Equal(1, deleted);
        Assert.Equal(2, fixture.Service.ListBackups().Count);
    }
}
