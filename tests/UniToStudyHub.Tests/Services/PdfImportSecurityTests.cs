using System.Collections.ObjectModel;
using System.Text;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class PdfImportSecurityTests
{
    private sealed class FakeExtractor(int pageCount, string text) : IPdfTextExtractor
    {
        public Task<PdfExtraction> ExtractAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PdfExtraction(pageCount,
                new[] { new PdfPageData { PageNumber = 1, Text = text } },
                string.IsNullOrWhiteSpace(text),
                Array.Empty<string>()));
    }

    private sealed class GeneratedOversizedStream(long length) : Stream
    {
        private long _remaining = length;

        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..read].Clear();
            _remaining -= read;
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remaining);
            Array.Clear(buffer, offset, read);
            _remaining -= read;
            BytesRead += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
    };

    private static async Task<string> TempFileAsync(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "unitracker-pdf-" + Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    [Fact]
    public async Task OversizedPdfIsRejectedBeforeAnyParse()
    {
        var path = Path.Combine(Path.GetTempPath(), "unitracker-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            stream.SetLength(PdfImportService.MaximumFileBytes + 1);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new PdfImportService(new FakeExtractor(1, "testo")).ImportAsync(path, Data()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FilesWithoutPdfSignatureAreRejected()
    {
        var path = await TempFileAsync(".pdf", Encoding.ASCII.GetBytes("This is not a PDF at all."));
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new PdfImportService(new FakeExtractor(1, "testo")).ImportAsync(path, Data()));
            Assert.Contains("firma PDF", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Data().PdfDocuments);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NonPdfExtensionIsRejected()
    {
        var path = await TempFileAsync(".txt", Encoding.ASCII.GetBytes("%PDF-1.4\nbody"));
        try
        {
            var data = Data();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new PdfImportService(new FakeExtractor(1, "testo")).ImportAsync(path, data));
            Assert.Empty(data.PdfDocuments);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PdfExtractorStopsReadingNonSeekableStreamsAtTheLimitPlusOneByte()
    {
        var stream = new GeneratedOversizedStream(PdfImportService.MaximumFileBytes + 100_000);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PdfPigTextExtractor().ExtractAsync(stream));

        Assert.Equal(PdfImportService.MaximumFileBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task AbsurdPageCountIsRejectedEvenWithAnotherExtractor()
    {
        var path = await TempFileAsync(".pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nbody"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new PdfImportService(new FakeExtractor(PdfImportService.MaximumPageCount + 1, "testo"))
                    .ImportAsync(path, Data()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PageLimitBoundaryIsEnforced()
    {
        PdfImportService.EnsurePageLimit(PdfImportService.MaximumPageCount);
        Assert.Throws<InvalidDataException>(() =>
            PdfImportService.EnsurePageLimit(PdfImportService.MaximumPageCount + 1));
    }

    [Fact]
    public void StoredFileNameIsALeafNameWithoutControlCharacters()
    {
        Assert.Equal("appunti.pdf", PdfImportService.SafeFileName("C:\\tmp\\..\\appunti.pdf"));
        Assert.Equal("appunti.pdf", PdfImportService.SafeFileName("/tmp/corso/../../appunti.pdf"));
        Assert.Equal("appunti.pdf", PdfImportService.SafeFileName("app\u0001unti.pdf"));
        Assert.Equal("documento.pdf", PdfImportService.SafeFileName("   "));
        Assert.True(PdfImportService.SafeFileName(new string('a', 400) + ".pdf").Length <= 200);
    }

    [Fact]
    public async Task ImportedDocumentKeepsUnverifiableTextButNoPath()
    {
        var path = await TempFileAsync(".pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nbody"));
        try
        {
            var data = Data();
            var result = await new PdfImportService(new FakeExtractor(1, "Derivata: 2x."))
                .ImportAsync(path, data, "analisi");
            Assert.EndsWith(".pdf", result.Document.FileName, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetTempPath(), result.Document.FileName, StringComparison.Ordinal);
            Assert.Equal("analisi", result.Document.CourseId);
            Assert.Single(data.PdfDocuments);
        }
        finally { File.Delete(path); }
    }
}
