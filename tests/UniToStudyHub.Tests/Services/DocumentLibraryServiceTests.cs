using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class DocumentLibraryServiceTests
{
    [Fact]
    public async Task ExistingDocumentIsIndexedWithChecksumAndRemoteMetadata()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var file = CreateFile("lezione-01.pdf", "https://moodle.example/pluginfile.php/1/lezione-01.pdf");
        var section = CreateSection(file);
        var payload = Encoding.UTF8.GetBytes("materiale didattico");
        var remoteTime = DateTimeOffset.FromUnixTimeSeconds(1_795_000_000);
        file.filesize = payload.Length;
        file.timemodified = checked((int)remoteTime.ToUnixTimeSeconds());
        var path = await WriteExpectedFileAsync(folder.Path, course, section, payload, remoteTime);

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DocumentLibraryStatus.Available, entry.Status);
        Assert.Equal(path, entry.LocalPath);
        Assert.Equal<long?>((long)payload.Length, entry.LocalSizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), entry.Sha256);
        Assert.Equal<DateTimeOffset?>(remoteTime, entry.RemoteModifiedUtc);
        Assert.Equal((long)payload.Length, result.BytesHashed);
        Assert.True(result.LocationScanComplete);
    }

    [Fact]
    public async Task MissingRemoteFileIsReportedWithoutCreatingDirectories()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var section = CreateSection(CreateFile("appunti.pdf", "https://moodle.example/files/appunti"));

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DocumentLibraryStatus.Missing, entry.Status);
        Assert.Null(entry.LocalPath);
        Assert.Null(entry.Sha256);
        Assert.False(Directory.Exists(DocumentSyncService.GetCourseDirectory(course, folder.Path)));
    }

    [Fact]
    public async Task RemoteSizeMismatchRequiresAnUpdate()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var file = CreateFile("dispensa.pdf", "https://moodle.example/files/dispensa");
        file.filesize = 20;
        var section = CreateSection(file);
        await WriteExpectedFileAsync(folder.Path, course, section, Encoding.UTF8.GetBytes("old"));

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        Assert.Equal(DocumentLibraryStatus.UpdateRequired, Assert.Single(result.Entries).Status);
    }

    [Fact]
    public async Task UniqueFileMovedInsideCourseFolderIsLocated()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var file = CreateFile("spostato.pdf", "https://moodle.example/files/spostato");
        file.filesize = 4;
        var section = CreateSection(file);
        var root = DocumentSyncService.GetCourseDirectory(course, folder.Path);
        var movedPath = Path.Combine(root, "Recuperati", "spostato.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        await File.WriteAllTextAsync(movedPath, "test");

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DocumentLibraryStatus.Moved, entry.Status);
        Assert.True(entry.IsMoved);
        Assert.Equal(movedPath, entry.LocalPath);
        Assert.True(entry.Sha256 is not null);
    }

    [Fact]
    public async Task MultipleSameNameCandidatesAreNotChosenArbitrarily()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var file = CreateFile("duplicato.pdf", "https://moodle.example/files/duplicato");
        var section = CreateSection(file);
        var root = DocumentSyncService.GetCourseDirectory(course, folder.Path);
        foreach (var directory in new[] { "A", "B" })
        {
            var path = Path.Combine(root, directory, file.filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, directory);
        }

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DocumentLibraryStatus.Missing, entry.Status);
        Assert.True(entry.HasAmbiguousLocation);
        Assert.Null(entry.LocalPath);
    }

    [Fact]
    public async Task SearchCategoryExtensionStatusAndDateFiltersCanBeCombined()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var day = DateTimeOffset.FromUnixTimeSeconds(1_795_000_000);
        var pdf = CreateFile("slide-introduzione.pdf", "https://moodle.example/files/slide-pdf");
        var slides = CreateFile("esercizio.pptx", "https://moodle.example/files/esercizio-pptx");
        foreach (var file in new[] { pdf, slides })
        {
            file.filesize = 4;
            file.timemodified = checked((int)day.ToUnixTimeSeconds());
        }
        var section = CreateSection(pdf, slides);
        await WriteExpectedFileAsync(folder.Path, course, section, Encoding.UTF8.GetBytes("data"), day, pdf);
        await WriteExpectedFileAsync(folder.Path, course, section, Encoding.UTF8.GetBytes("data"), day, slides);

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);
        var filtered = DocumentLibraryService.Filter(result.Entries, new DocumentLibraryFilter(
            SearchText: "slide",
            CourseId: course.Id,
            Category: "PDF",
            Extension: "pdf",
            Status: DocumentLibraryStatus.Available,
            ModifiedFromUtc: day,
            ModifiedThroughUtc: day));

        var entry = Assert.Single(filtered);
        Assert.Equal("slide-introduzione.pdf", entry.FileName);
    }

    [Fact]
    public async Task TraversalSegmentsAreRemovedAndExpectedPathStaysInsideCourseFolder()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var file = CreateFile("safe.pdf", "https://moodle.example/files/safe");
        file.filepath = "../../../../outside";
        var section = CreateSection(file);

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        var entry = Assert.Single(result.Entries);
        var root = DocumentSyncService.GetCourseDirectory(course, folder.Path);
        Assert.True(UniToStudyHub.Security.LocalFileSecurity.IsInside(root, entry.ExpectedPath));
        Assert.Equal(DocumentLibraryStatus.Missing, entry.Status);
    }

    [Fact]
    public async Task ExecutableAndOversizedRemoteFilesAreMarkedUnsupported()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var executable = CreateFile("lezione.exe", "https://moodle.example/files/exe");
        var oversized = CreateFile("archivio.zip", "https://moodle.example/files/large");
        oversized.filesize = MoodleClient.MaximumDownloadBytes + 1;
        var section = CreateSection(executable, oversized);

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        Assert.All(result.Entries, entry => Assert.Equal(DocumentLibraryStatus.Unsupported, entry.Status));
        Assert.All(result.Entries, entry => Assert.Null(entry.Sha256));
    }

    [Fact]
    public async Task DuplicateSourceUrlsAreDeduplicatedButDistinctCaseSensitiveUrlsRemainSeparate()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var first = CreateFile("uno.pdf", "https://moodle.example/pluginfile.php/1/File.pdf");
        var duplicate = CreateFile("nome-diverso.pdf", "https://moodle.example/pluginfile.php/1/File.pdf");
        var caseDistinct = CreateFile("due.pdf", "https://moodle.example/pluginfile.php/1/file.pdf");
        var section = CreateSection(first, duplicate, caseDistinct);

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, entry => entry.FileName == "uno.pdf");
        Assert.Contains(result.Entries, entry => entry.FileName == "due.pdf");
    }

    [Fact]
    public void ActivityFileExtractionPreservesCaseSensitiveUrlPaths()
    {
        using var document = JsonDocument.Parse("""
            {"files":[
                {"filename":"upper.pdf","fileurl":"https://moodle.example/pluginfile.php/1/File.pdf"},
                {"filename":"lower.pdf","fileurl":"https://moodle.example/pluginfile.php/1/file.pdf"}
            ]}
            """);

        var files = MoodleClient.ExtractActivityFiles(new[] { document.RootElement });

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.filename == "upper.pdf");
        Assert.Contains(files, file => file.filename == "lower.pdf");
    }

    [Fact]
    public async Task CatalogueIsBoundedAndReportsTruncation()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var files = Enumerable.Range(0, DocumentSyncService.MaximumFilesPerCourse + 1)
            .Select(index => CreateFile($"file-{index}.pdf", $"https://moodle.example/files/{index}"))
            .ToList();
        var section = CreateSection(files.ToArray());

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, documentsBasePath: folder.Path);

        Assert.Equal(DocumentSyncService.MaximumFilesPerCourse, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.Contains(result.Warnings, warning => warning.Contains("limitati", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogueEnumerationIsBoundedEvenWhenSectionsContainNoFiles()
    {
        var sections = Enumerable.Range(0, DocumentSyncService.MaximumSectionsPerCourse + 1)
            .Select(_ => new MoodleCourseSection())
            .ToArray();

        var result = DocumentSyncService.EnumerateSources(sections);

        Assert.Empty(result.Files);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task InvalidAndNullDescriptorsAreIgnoredAndReportedWithoutThrowing()
    {
        using var folder = new TemporaryFolder();
        var course = CreateCourse();
        var valid = CreateFile("valid.pdf", "https://moodle.example/files/valid");
        var tooDeep = CreateFile("deep.pdf", "https://moodle.example/files/deep");
        tooDeep.filepath = string.Join("/", Enumerable.Repeat("nested", DocumentSyncService.MaximumFilePathSegments + 1));
        var section = new MoodleCourseSection
        {
            name = "Lezioni",
            modules = new List<MoodleModule>
            {
                new() { name = "File", contents = new List<MoodleContentFile> { null!, tooDeep, valid } },
                null!
            }
        };
        var invalidType = CreateFile("ignored.pdf", "https://moodle.example/files/ignored");
        invalidType.type = null!;
        var extra = new[] { invalidType };

        var result = await new DocumentLibraryService().IndexCourseAsync(
            course, new[] { section }, extra, folder.Path);

        Assert.Single(result.Entries);
        Assert.Contains(result.Warnings, warning => warning.Contains("ignorati", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreCancelledIndexDoesNotEnumerateTheFilesystem()
    {
        using var folder = new TemporaryFolder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DocumentLibraryService().IndexCourseAsync(
                CreateCourse(), Array.Empty<MoodleCourseSection>(), documentsBasePath: folder.Path,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void InvertedModifiedDateFilterIsRejected()
    {
        var from = DateTimeOffset.FromUnixTimeSeconds(1_795_000_000);
        var through = from.AddDays(-1);

        Assert.Throws<ArgumentException>(() => DocumentLibraryService.Filter(
            Array.Empty<DocumentLibraryEntry>(),
            new DocumentLibraryFilter(ModifiedFromUtc: from, ModifiedThroughUtc: through)));
    }

    private static Course CreateCourse() => new()
    {
        Id = "course-analisi",
        Name = "Analisi 1",
        MoodleCourseId = 42
    };

    private static MoodleContentFile CreateFile(string name, string url) => new()
    {
        type = "file",
        filename = name,
        fileurl = url,
        filepath = "/",
        mimetype = "application/pdf"
    };

    private static MoodleCourseSection CreateSection(params MoodleContentFile[] files) => new()
    {
        name = "Lezioni",
        modules = new List<MoodleModule>
        {
            new() { name = "Materiale", modname = "resource", contents = files.ToList() }
        }
    };

    private static async Task<string> WriteExpectedFileAsync(
        string documentsBase,
        Course course,
        MoodleCourseSection section,
        byte[] content,
        DateTimeOffset? modifiedAt = null,
        MoodleContentFile? target = null)
    {
        target ??= section.modules.Single().contents.First();
        var source = DocumentSyncService.EnumerateSources(new[] { section }).Files
            .Single(item => ReferenceEquals(item.File, target));
        var path = Path.Combine(
            DocumentSyncService.GetCourseDirectory(course, documentsBase),
            DocumentSyncService.GetRelativePath(source),
            DocumentSyncService.SafeName(target.filename));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content);
        if (modifiedAt.HasValue)
            File.SetLastWriteTimeUtc(path, modifiedAt.Value.UtcDateTime);
        return path;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "UniToStudyHubDocumentLibrary", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
