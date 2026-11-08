namespace Wintodo.Core.IO;

/// <summary>A forward-only read boundary for untrusted bodies, including synchronous decoders.</summary>
public sealed class BoundedReadStream : Stream
{
    private readonly Stream _source;
    private readonly long _maximumBytes;
    private readonly long? _declaredLength;
    private readonly CancellationToken _lifetimeToken;
    private long _readBytes;

    public BoundedReadStream(Stream source, long maximumBytes, CancellationToken lifetimeToken,
        long? declaredLength = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        if (declaredLength > maximumBytes || declaredLength < 0)
            throw new InvalidDataException("The response exceeds the byte limit.");
        _source = source;
        _maximumBytes = maximumBytes;
        _declaredLength = declaredLength;
        _lifetimeToken = lifetimeToken;
    }

    public override bool CanRead => _source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _readBytes; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // The BCL streaming JSON/base64 reader is synchronous. Use the underlying
        // cancellable asynchronous read so its network IO obeys the same deadline.
        return ReadAsync(buffer.AsMemory(offset, count), _lifetimeToken).AsTask().GetAwaiter().GetResult();
    }

    public override int Read(Span<byte> buffer)
    {
        byte[] temporary = new byte[Math.Min(buffer.Length, 64 * 1024)];
        int read = Read(temporary, 0, temporary.Length);
        temporary.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _lifetimeToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty) return 0;
        // Read at most one byte over budget to distinguish exact-limit EOF. No
        // over-budget bytes are returned to a parser or written to a destination.
        long remaining = _maximumBytes - _readBytes;
        int count = remaining >= buffer.Length ? buffer.Length : (int)remaining + 1;
        using CancellationTokenSource? linked = cancellationToken != _lifetimeToken && cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken, cancellationToken) : null;
        int read = await _source.ReadAsync(buffer[..count], linked?.Token ?? _lifetimeToken).ConfigureAwait(false);
        _lifetimeToken.ThrowIfCancellationRequested();
        _readBytes += read;
        if (_readBytes > _maximumBytes)
            throw new InvalidDataException("The response exceeds the byte limit.");
        if (read == 0 && _declaredLength is long expected && expected != _readBytes)
            throw new InvalidDataException("The response length does not match its declared length.");
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _source.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
