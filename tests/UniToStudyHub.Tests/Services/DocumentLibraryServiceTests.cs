using System.Security.Cryptography;
using System.Text;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class DocumentLibraryServiceTests
{
    [Fact]
    public async Task ScanIndexesCourseCategoryMetadataAndSha256WithoutChangingTheFile()
    {
        var root = CreateRoot();
        var relativeDirectory = Path.Combine("Analisi I", "Settimana 1");
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lezione.pdf");
        const string payload = "materiale didattico locale";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            var result = await new DocumentLibraryService().ScanAsync(root);

            var entry = Assert.Single(result.Entries);
            Assert.Equal("Analisi I", entry.CourseName);
            Assert.Equal("PDF", entry.Category);
            Assert.Equal("lezione.pdf", entry.FileName);
            Assert.Equal(".pdf", entry.Extension);
            Assert.Equal(bytes.LongLength, entry.SizeBytes);
            Assert.Equal(Path.Combine(relativeDirectory, "lezione.pdf"), entry.RelativePath);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), entry.Sha256);
            Assert.Equal(DocumentFileAvailability.Available, entry.Availability);
            Assert.Equal(DocumentChecksumStatus.Computed, entry.ChecksumStatus);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.False(result.IsTruncated);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FilterCombinesCourseCategoryExtensionQueryAndInclusiveDateRange()
    {
        var root = CreateRoot();
        var first = Path.Combine(root, "Analisi I", "Slide", "lezione.pdf");
        var second = Path.Combine(root, "Fisica", "Materiali", "esercizi.csv");
        var third = Path.Combine(root, "Analisi II", "Materiali", "dispensa.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        Directory.CreateDirectory(Path.GetDirectoryName(third)!);
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        await File.WriteAllTextAsync(third, "three");
        var targetTime = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first, targetTime);
        File.SetLastWriteTimeUtc(second, targetTime.AddDays(-2));
        File.SetLastWriteTimeUtc(third, targetTime.AddDays(1));

        try
        {
            var scan = await new DocumentLibraryService().ScanAsync(root);
            var filtered = DocumentLibraryService.Filter(scan.Entries, new DocumentLibraryFilter(
                Query: "analisi",
                CourseName: "Analisi I",
                Category: "pdf",
                Extension: "PDF",
                Availability: DocumentFileAvailability.Available,
                ModifiedFromUtc: new DateTimeOffset(targetTime, TimeSpan.Zero),
                ModifiedToUtc: new DateTimeOffset(targetTime, TimeSpan.Zero)));

            var entry = Assert.Single(filtered);
            Assert.Equal("lezione.pdf", entry.FileName);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FilterStopsEnumeratingAtTheConfiguredInputLimit()
    {
        var yielded = 0;
        var template = new DocumentLibraryEntry(
            "Corso", "PDF", "file.pdf", ".pdf", 1, DateTimeOffset.UnixEpoch,
            Path.Combine("Corso", "file.pdf"), null,
            DocumentFileAvailability.Available, DocumentChecksumStatus.Computed);

        IEnumerable<DocumentLibraryEntry> Generate()
        {
            for (var index = 0; index < DocumentLibraryService.MaximumFilterItems + 100; index++)
            {
                yielded++;
                yield return template;
            }
        }

        var result = DocumentLibraryService.Filter(Generate(), new DocumentLibraryFilter(Query: "inesistente"));

        Assert.Empty(result);
        Assert.Equal(DocumentLibraryService.MaximumFilterItems, yielded);
    }

    [Fact]
    public async Task ScanExcludesTemporaryAndPartialDownloadFiles()
    {
        var root = CreateRoot();
        var course = Path.Combine(root, "Diritto", "Materiali");
        Directory.CreateDirectory(course);
        await File.WriteAllTextAsync(Path.Combine(course, "dispensa.pdf"), "pdf");
        await File.WriteAllTextAsync(Path.Combine(course, "download.tmp"), "partial");
        await File.WriteAllTextAsync(Path.Combine(course, "download.part"), "partial");

        try
        {
            var result = await new DocumentLibraryService().ScanAsync(root);

            Assert.Equal(1, result.Entries.Count);
            Assert.Equal("dispensa.pdf", result.Entries[0].FileName);
            Assert.Equal(3, result.FilesVisited);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RefreshAvailabilityMarksDeletedFilesAsMissing()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "Statistica", "Materiali");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appunti.pdf");
        await File.WriteAllTextAsync(path, "appunti");

        try
        {
            var scan = await new DocumentLibraryService().ScanAsync(root);
            var entry = Assert.Single(scan.Entries);
            File.Delete(path);

            var refreshed = DocumentLibraryService.RefreshAvailability(root, entry);

            Assert.Equal(DocumentFileAvailability.Missing, refreshed.Availability);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task TryGetSafePathReturnsContainedFilesAndRejectsTraversal()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "Programmazione", "Slide");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lezione.pdf");
        await File.WriteAllTextAsync(path, "lezione");

        try
        {
            var scan = await new DocumentLibraryService().ScanAsync(root);
            var entry = Assert.Single(scan.Entries);

            Assert.True(DocumentLibraryService.TryGetSafePath(root, entry, out var resolved));
            Assert.Equal(Path.GetFullPath(path), resolved);

            var forged = entry with { RelativePath = Path.Combine("..", "outside.txt") };
            Assert.False(DocumentLibraryService.TryGetSafePath(root, forged, out var rejected));
            Assert.Equal(string.Empty, rejected);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ScanReturnsAnEmptySnapshotWhenTheArchiveDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnitrackerDocumentLibrary", Guid.NewGuid().ToString("N"));

        var result = await new DocumentLibraryService().ScanAsync(root);

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.DirectoriesVisited);
        Assert.Equal(0, result.FilesVisited);
        Assert.False(result.IsTruncated);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ScanHonorsCancellationBeforeStartingFilesystemWork()
    {
        var root = CreateRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new DocumentLibraryService().ScanAsync(root, cancellation.Token));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ScanStopsAtTheMaximumDirectoryDepth()
    {
        var root = CreateRoot();
        var current = root;
        for (var depth = 0; depth <= DocumentLibraryService.MaximumDepth; depth++)
        {
            current = Path.Combine(current, $"level-{depth}");
            Directory.CreateDirectory(current);
        }

        try
        {
            var result = await new DocumentLibraryService().ScanAsync(root);

            Assert.True(result.IsTruncated);
            Assert.True(result.DirectoriesVisited <= DocumentLibraryService.MaximumDepth + 1);
            Assert.Contains(result.Warnings, warning => warning.Contains("profondità", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnitrackerDocumentLibrary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup must not mask the assertion failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup must not mask the assertion failure.
        }
    }
}
