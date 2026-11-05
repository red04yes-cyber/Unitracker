using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class BackupServiceTests
{
    private static StudyData Data(string name = "Analisi") => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = name, Cfu = 6 }
        }
    };

    [Fact]
    public async Task BackupCanBeVerifiedAndRestoredWithoutLosingData()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data());
        var backups = Path.Combine(Path.GetDirectoryName(path)!, "backups");
        var service = new BackupService(database.Factory, path, backups, new TestBackupKeyProtector());

        var backup = await service.CreateBackupAsync();

        Assert.True(backup.IntegrityVerified);
        Assert.True(File.Exists(backup.FilePath));
        Assert.True(await service.VerifyBackupAsync(backup.FilePath));
        Assert.Single(service.ListBackups());
        var manifest = await service.ReadManifestAsync(backup.FilePath);
        Assert.NotNull(manifest);
        Assert.Equal(BackupService.CurrentFormatVersion, manifest!.FormatVersion);
        Assert.Equal(backup.Sha256, manifest.Sha256);

        var changed = await store.LoadAsync();
        changed.Courses[0].Name = "Modificata";
        await store.SaveAsync(changed);
        Assert.Equal("Modificata", (await store.LoadAsync()).Courses[0].Name);

        await service.RestoreAsync(backup.FilePath);

        var restored = await store.LoadAsync();
        Assert.Equal("Analisi", restored.Courses[0].Name);
        Assert.True(await service.VerifyBackupAsync(path));
    }

    [Fact]
    public async Task CorruptOrMissingBackupIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var folder = Path.GetDirectoryName(path)!;
        var backups = Path.Combine(folder, "backups");
        Directory.CreateDirectory(backups);
        var corrupt = Path.Combine(backups, "corrupt.db");
        await File.WriteAllTextAsync(corrupt, "not a sqlite database");
        var service = new BackupService(database.Factory, path, backups, new TestBackupKeyProtector());

        Assert.False(await service.VerifyBackupAsync(corrupt));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(corrupt));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.RestoreAsync(Path.Combine(backups, "missing.db")));
    }
}
