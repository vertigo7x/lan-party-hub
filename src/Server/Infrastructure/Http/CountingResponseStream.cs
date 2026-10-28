using System.Threading;

namespace LANPartyHub.Server.Infrastructure.Http;

/// <summary>
/// Stream of the response that counts the bytes that the host writes (AD-15).
///
/// <para>
/// The stream decorates the one of the response and counts every byte that goes out. The
/// count feeds the transfer rate of the host, which is one of the three metrics that the
/// endpoint of the host publishes locally.
/// </para>
/// <para>
/// The decoration never closes the decorated stream: the owner of the response stream is
/// the host, and the decorator only observes it.
/// </para>
/// </summary>
public sealed class CountingResponseStream : Stream
{
    private readonly Stream _inner;
    private long _bytesWritten;

    /// <summary>Creates the counting stream over the stream of the response.</summary>
    /// <param name="inner">Stream of the response that the decorator observes.</param>
    public CountingResponseStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <summary>Number of bytes that the host has written through this stream.</summary>
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    /// <inheritdoc />
    public override bool CanRead => _inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <inheritdoc />
    public override void SetLength(long value) => _inner.SetLength(value);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);

        Interlocked.Add(ref _bytesWritten, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);

        Interlocked.Add(ref _bytesWritten, buffer.Length);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        CountAsync(_inner.WriteAsync(buffer, offset, count, cancellationToken), count);

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        CountAsync(_inner.WriteAsync(buffer, cancellationToken), buffer.Length);

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        _inner.WriteByte(value);

        Interlocked.Increment(ref _bytesWritten);
    }

    /// <summary>
    /// Closes the decorator. The decorated stream stays open: its owner is the host and
    /// not this decorator.
    /// </summary>
    /// <param name="disposing">Whether the managed resources have to be released.</param>
    protected override void Dispose(bool disposing)
    {
        // The decorated stream belongs to the host, so it is not closed here.
        base.Dispose(disposing);
    }

    private async Task CountAsync(Task write, int count)
    {
        await write.ConfigureAwait(false);

        Interlocked.Add(ref _bytesWritten, count);
    }

    private async ValueTask CountAsync(ValueTask write, int count)
    {
        await write.ConfigureAwait(false);

        Interlocked.Add(ref _bytesWritten, count);
    }
}
