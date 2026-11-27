using System.IO.Compression;
using System.Text;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyExportFileWriterTests
{
    [Fact]
    public async Task CsvBundleIsWrittenAsFlatZipWithAllEntries()
    {
        using var directory = new TempDirectory();
        var destination = Path.Combine(directory.Path, "study-export.zip");
        var bundle = new StudyExportBundle(
            StudyExportFormat.Csv,
            "1.1",
            DateTimeOffset.UtcNow,
            new[]
            {
                new StudyExportFile("tasks.csv", "text/csv", Encoding.UTF8.GetBytes("id,title\nt1,Task")),
                new StudyExportFile("grades.csv", "text/csv", Encoding.UTF8.GetBytes("id,value\ng1,28"))
            });

        await StudyExportFileWriter.WriteAsync(destination, bundle);

        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal(new[] { "grades.csv", "tasks.csv" }, archive.Entries.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal));
        using var reader = new StreamReader(archive.GetEntry("tasks.csv")!.Open());
        Assert.Equal("id,title\nt1,Task", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(StudyExportFormat.Json, ".json")]
    [InlineData(StudyExportFormat.Xlsx, ".xlsx")]
    public async Task SingleFileFormatsReplaceDestinationOnlyAfterSuccessfulWrite(StudyExportFormat format, string extension)
    {
        using var directory = new TempDirectory();
        var destination = Path.Combine(directory.Path, "study-export" + extension);
        await File.WriteAllTextAsync(destination, "previous");
        var payload = Encoding.UTF8.GetBytes("new-content");
        var bundle = new StudyExportBundle(
            format,
            "1.1",
            DateTimeOffset.UtcNow,
            new[] { new StudyExportFile("export" + extension, "application/octet-stream", payload) });

        await StudyExportFileWriter.WriteAsync(destination, bundle);

        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("../outside.csv")]
    [InlineData("nested/outside.csv")]
    [InlineData("nested\\outside.csv")]
    [InlineData("bad:name.csv")]
    [InlineData("..")]
    public async Task CsvBundleRejectsEntryPathTraversal(string entryName)
    {
        using var directory = new TempDirectory();
        var destination = Path.Combine(directory.Path, "export.zip");
        var bundle = new StudyExportBundle(
            StudyExportFormat.Csv,
            "1.1",
            DateTimeOffset.UtcNow,
            new[] { new StudyExportFile(entryName, "text/csv", Array.Empty<byte>()) });

        await Assert.ThrowsAsync<InvalidDataException>(() => StudyExportFileWriter.WriteAsync(destination, bundle));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CsvBundleRejectsDuplicateNames()
    {
        using var directory = new TempDirectory();
        var bundle = new StudyExportBundle(
            StudyExportFormat.Csv,
            "1.1",
            DateTimeOffset.UtcNow,
            new[]
            {
                new StudyExportFile("tasks.csv", "text/csv", Array.Empty<byte>()),
                new StudyExportFile("TASKS.CSV", "text/csv", Array.Empty<byte>())
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StudyExportFileWriter.WriteAsync(Path.Combine(directory.Path, "export.zip"), bundle));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task NonCsvBundleRequiresExactlyOneFile()
    {
        using var directory = new TempDirectory();
        var bundle = new StudyExportBundle(
            StudyExportFormat.Json,
            "1.1",
            DateTimeOffset.UtcNow,
            new[]
            {
                new StudyExportFile("one.json", "application/json", Array.Empty<byte>()),
                new StudyExportFile("two.json", "application/json", Array.Empty<byte>())
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StudyExportFileWriter.WriteAsync(Path.Combine(directory.Path, "export.json"), bundle));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task EmptyBundleAndWrongExtensionAreRejectedBeforeWriting()
    {
        using var directory = new TempDirectory();
        var empty = new StudyExportBundle(StudyExportFormat.Json, "1.1", DateTimeOffset.UtcNow, Array.Empty<StudyExportFile>());
        var valid = new StudyExportBundle(
            StudyExportFormat.Json,
            "1.1",
            DateTimeOffset.UtcNow,
            new[] { new StudyExportFile("export.json", "application/json", Encoding.UTF8.GetBytes("{}")) });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StudyExportFileWriter.WriteAsync(Path.Combine(directory.Path, "empty.json"), empty));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            StudyExportFileWriter.WriteAsync(Path.Combine(directory.Path, "wrong.zip"), valid));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CancellationBeforeWritingLeavesExistingFileUntouched()
    {
        using var directory = new TempDirectory();
        var destination = Path.Combine(directory.Path, "export.json");
        await File.WriteAllTextAsync(destination, "original");
        var bundle = new StudyExportBundle(
            StudyExportFormat.Json,
            "1.1",
            DateTimeOffset.UtcNow,
            new[] { new StudyExportFile("export.json", "application/json", Encoding.UTF8.GetBytes("{}")) });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StudyExportFileWriter.WriteAsync(destination, bundle, cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "study-export-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
