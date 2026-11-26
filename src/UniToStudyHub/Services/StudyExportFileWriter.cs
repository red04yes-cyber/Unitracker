using System.IO.Compression;

namespace UniToStudyHub.Services;

/// <summary>
/// Writes an export bundle to a user-selected destination. Files are staged
/// beside the destination and moved into place only after the write succeeds.
/// CSV collections are packaged in a ZIP with flat, validated entry names.
/// </summary>
public static class StudyExportFileWriter
{
    public static async Task WriteAsync(
        string destinationPath,
        StudyExportBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Files is null || bundle.Files.Count == 0)
            throw new InvalidDataException("L'esportazione non contiene file.");

        var extension = bundle.Format switch
        {
            StudyExportFormat.Csv => ".zip",
            StudyExportFormat.Xlsx => ".xlsx",
            StudyExportFormat.Json => ".json",
            _ => throw new ArgumentOutOfRangeException(nameof(bundle), "Formato non supportato.")
        };
        var fullPath = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Il file di destinazione deve avere estensione {extension}.", nameof(destinationPath));

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("La cartella di destinazione non esiste.");

        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous))
            {
                if (bundle.Format == StudyExportFormat.Csv)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        foreach (var file in bundle.Files)
                        {
                            ValidateCsvEntryName(file.FileName);
                            if (!names.Add(file.FileName))
                                throw new InvalidDataException("L'esportazione contiene nomi di file CSV duplicati.");

                            var entry = archive.CreateEntry(file.FileName, CompressionLevel.Optimal);
                            await using var entryStream = entry.Open();
                            await entryStream.WriteAsync(file.Content.AsMemory(), cancellationToken);
                        }
                    }
                }
                else
                {
                    if (bundle.Files.Count != 1)
                        throw new InvalidDataException("Il formato selezionato deve contenere un solo file.");
                    await stream.WriteAsync(bundle.Files[0].Content.AsMemory(), cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateCsvEntryName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName.IndexOfAny("<>:\"|?*".ToCharArray()) >= 0 ||
            fileName.EndsWith(' ') ||
            fileName.EndsWith('.') ||
            !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Il bundle CSV contiene un nome file non sicuro.");
        }
    }
}
