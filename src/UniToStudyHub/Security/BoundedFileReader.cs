using System.IO;

namespace UniToStudyHub.Security;

/// <summary>
/// Reads an untrusted local stream into memory without exceeding a caller-owned
/// byte budget. The length pre-check is only an optimization: the read loop
/// enforces the limit even for non-seekable or growing streams.
/// </summary>
internal static class BoundedFileReader
{
    public static async Task<byte[]> ReadAllBytesAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        if (maximumBytes is <= 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        cancellationToken.ThrowIfCancellationRequested();

        var initialCapacity = 0;
        if (source.CanSeek)
        {
            try
            {
                var remaining = source.Length - source.Position;
                if (remaining > maximumBytes)
                    throw TooLarge(maximumBytes);
                if (remaining > 0)
                    initialCapacity = (int)remaining;
            }
            catch (NotSupportedException)
            {
                // Some seekable streams do not expose a reliable length.
            }
        }

        using var output = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length > maximumBytes - read)
                throw TooLarge(maximumBytes);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static InvalidDataException TooLarge(long maximumBytes) =>
        new($"The input exceeds the maximum allowed size of {maximumBytes} bytes.");
}
