using System.IO;
using UniToStudyHub.Security;
using Xunit;

namespace UniToStudyHub.Tests.Security;

public sealed class BoundedFileReaderTests
{
    [Fact]
    public async Task ReadAllBytesAsyncAcceptsPayloadAtExactLimit()
    {
        await using var source = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await BoundedFileReader.ReadAllBytesAsync(source, 3);

        Assert.Equal(new byte[] { 1, 2, 3 }, result);
    }

    [Fact]
    public async Task ReadAllBytesAsyncRejectsFirstByteBeyondLimit()
    {
        await using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedFileReader.ReadAllBytesAsync(source, 3));
    }

    [Fact]
    public async Task ReadAllBytesAsyncRejectsSeekableStreamThatGrowsAfterLengthCheck()
    {
        await using var source = new GrowingSeekableReadStream(new byte[] { 1, 2, 3, 4 }, reportedLength: 3);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedFileReader.ReadAllBytesAsync(source, 3));
    }

    [Fact]
    public async Task ReadAllBytesAsyncSupportsNonSeekableStreams()
    {
        await using var source = new NonSeekableReadStream(new byte[] { 4, 5, 6 });

        var result = await BoundedFileReader.ReadAllBytesAsync(source, 3);

        Assert.Equal(new byte[] { 4, 5, 6 }, result);
    }

    [Fact]
    public async Task ReadAllBytesAsyncHonorsCancellation()
    {
        await using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BoundedFileReader.ReadAllBytesAsync(source, 3, cancellation.Token));
    }

    [Fact]
    public async Task ReadAllBytesAsyncRejectsNonPositiveLimits()
    {
        await using var source = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BoundedFileReader.ReadAllBytesAsync(source, 0));
    }

    private sealed class GrowingSeekableReadStream(byte[] content, int reportedLength) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => reportedLength;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > content.Length) throw new ArgumentOutOfRangeException(nameof(value));
                _position = (int)value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, content.Length - _position);
            content.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset, read));
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Math.Min(buffer.Length, content.Length - _position);
            content.AsMemory(_position, read).CopyTo(buffer);
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => content.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            Position = next;
            return _position;
        }

        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, content.Length - _position);
            content.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset, read));
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Math.Min(buffer.Length, content.Length - _position);
            content.AsMemory(_position, read).CopyTo(buffer);
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
