using System.Text;
using UniToStudyHub.Security;
using Xunit;

namespace UniToStudyHub.Tests.Security;

public sealed class SecretAndFileSecurityTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "unitracker-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void SecretRoundTripsIsUpdatableAndDeletable()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProtectedSecretStore(directory, new TestSecretProtector());
            var first = "abcdef0123456789abcdef0123456789";
            var second = "0123456789abcdef0123456789abcdef";

            store.Write("moodle-token", first);
            Assert.True(store.Exists("moodle-token"));
            Assert.True(store.TryRead("moodle-token", out var loaded));
            Assert.Equal(first, loaded);

            store.Write("moodle-token", second);
            Assert.Equal(second, store.Read("moodle-token").Value);

            Assert.True(store.Delete("moodle-token"));
            Assert.False(store.Exists("moodle-token"));
            Assert.Equal(SecretReadStatus.Missing, store.Read("moodle-token").Status);
            Assert.Null(store.Read("moodle-token").Value);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SecretFileNeverContainsThePlaintext()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProtectedSecretStore(directory, new TestSecretProtector());
            var token = "abcdef0123456789abcdef0123456789";
            store.Write("moodle-token", token);

            var bytes = File.ReadAllBytes(store.PathFor("moodle-token"));
            Assert.StartsWith("UTSK", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.DoesNotContain(token, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexString(Encoding.UTF8.GetBytes(token)),
                Convert.ToHexString(bytes), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CorruptedSecretIsReportedAndNotReturned()
    {
        var directory = NewDirectory();
        try
        {
            var store = new ProtectedSecretStore(directory, new TestSecretProtector());
            store.Write("moodle-token", "abcdef0123456789abcdef0123456789");
            var path = store.PathFor("moodle-token");

            var valid = File.ReadAllBytes(path);
            valid[^1] ^= 0xFF;
            File.WriteAllBytes(path, valid);
            var corrupted = store.Read("moodle-token");
            Assert.Equal(SecretReadStatus.Corrupted, corrupted.Status);
            Assert.Null(corrupted.Value);

            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            Assert.Equal(SecretReadStatus.Corrupted, store.Read("moodle-token").Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void AnotherProtectorCannotReadTheBlob()
    {
        var directory = NewDirectory();
        try
        {
            new ProtectedSecretStore(directory, new TestSecretProtector())
                .Write("moodle-token", "abcdef0123456789abcdef0123456789");
            var foreign = new ProtectedSecretStore(directory, new TestSecretProtector()).Read("moodle-token");
            Assert.Equal(SecretReadStatus.Corrupted, foreign.Status);
            Assert.Null(foreign.Value);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("Uppercase")]
    [InlineData("with space")]
    public void SecretNamesRejectTraversalAndSeparators(string name)
    {
        var store = new ProtectedSecretStore(NewDirectory(), new TestSecretProtector());
        Assert.False(ProtectedSecretStore.IsValidName(name));
        Assert.Throws<ArgumentException>(() => store.PathFor(name));
    }

    [Fact]
    public void AtomicWriteLeavesNoTemporaryFile()
    {
        var directory = NewDirectory();
        try
        {
            var target = Path.Combine(directory, "unitracker.db");
            LocalFileSecurity.WriteAllBytesAtomically(target, Encoding.UTF8.GetBytes("first"));
            LocalFileSecurity.WriteAllBytesAtomically(target, Encoding.UTF8.GetBytes("second"));

            Assert.Equal("second", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void StaleTemporaryFilesAreCleanedOnlyWhenOld()
    {
        var directory = NewDirectory();
        try
        {
            var stale = Path.Combine(directory, "db.restore-old.tmp");
            var recent = Path.Combine(directory, "db.restore-new.tmp");
            File.WriteAllText(stale, "stale");
            File.WriteAllText(recent, "recent");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            var removed = LocalFileSecurity.CleanupStaleTemporaryFiles(directory, TimeSpan.FromHours(1), "*.tmp");

            Assert.Equal(1, removed);
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(recent));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void IsInsideRejectsTraversal()
    {
        var root = NewDirectory();
        try
        {
            Assert.True(LocalFileSecurity.IsInside(root, Path.Combine(root, "file.pdf")));
            Assert.False(LocalFileSecurity.IsInside(root, Path.Combine(root, "..", "escape.pdf")));
            Assert.False(LocalFileSecurity.IsInside(root, root + "-sibling/file.pdf"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DirectoriesAndFilesArePrivateOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = NewDirectory();
        try
        {
            var store = new ProtectedSecretStore(Path.Combine(directory, "secrets"), new TestSecretProtector());
            store.Write("moodle-token", "abcdef0123456789abcdef0123456789");
            Assert.Equal(LocalFileSecurity.PrivateDirectoryMode,
                File.GetUnixFileMode(store.StorageDirectory));
            Assert.Equal(LocalFileSecurity.PrivateFileMode,
                File.GetUnixFileMode(store.PathFor("moodle-token")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ScrubAndDeleteRemovesTheFile()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "token.bin");
            File.WriteAllText(path, "SECRET-CONTENT-THAT-MUST-NOT-SURVIVE");
            LocalFileSecurity.ScrubAndDelete(path);
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
