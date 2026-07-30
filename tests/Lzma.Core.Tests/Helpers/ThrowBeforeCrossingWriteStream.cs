using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lzma.Core.Tests.Helpers;

internal sealed class ThrowBeforeCrossingWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;

    public ThrowBeforeCrossingWriteStream(Stream inner, long byteLimit, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (byteLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLimit), "byteLimit must be >= 0.");

        _inner = inner;
        ByteLimit = byteLimit;
        _leaveOpen = leaveOpen;
    }

    public long ByteLimit { get; }
    public long BytesWrittenToInner { get; private set; }
    public bool HasInjectedFailure { get; private set; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if ((uint)offset > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > (uint)(buffer.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0)
        {
            _inner.Write(buffer, offset, count);
            return;
        }

        if (HasInjectedFailure)
            throw new IOException("Injected output-stream write failure.");

        if ((long)count > ByteLimit - BytesWrittenToInner)
        {
            HasInjectedFailure = true;
            throw new IOException("Injected output-stream write failure.");
        }

        _inner.Write(buffer, offset, count);
        BytesWrittenToInner += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            _inner.Write(buffer);
            return;
        }

        if (HasInjectedFailure)
            throw new IOException("Injected output-stream write failure.");

        if ((long)buffer.Length > ByteLimit - BytesWrittenToInner)
        {
            HasInjectedFailure = true;
            throw new IOException("Injected output-stream write failure.");
        }

        _inner.Write(buffer);
        BytesWrittenToInner += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        if (HasInjectedFailure)
            throw new IOException("Injected output-stream write failure.");

        if (1L > ByteLimit - BytesWrittenToInner)
        {
            HasInjectedFailure = true;
            throw new IOException("Injected output-stream write failure.");
        }

        _inner.WriteByte(value);
        BytesWrittenToInner++;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if ((uint)offset > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if ((uint)count > (uint)(buffer.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(count));

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        if (count == 0)
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);

        if (HasInjectedFailure)
            return Task.FromException(
                new IOException("Injected output-stream write failure."));

        if ((long)count > ByteLimit - BytesWrittenToInner)
        {
            HasInjectedFailure = true;

            return Task.FromException(
                new IOException("Injected output-stream write failure."));
        }

        return WriteArrayAsyncCore(
            buffer,
            offset,
            count,
            cancellationToken);
    }

    private async Task WriteArrayAsyncCore(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await _inner
            .WriteAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);

        BytesWrittenToInner += count;
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (buffer.IsEmpty)
            return _inner.WriteAsync(buffer, cancellationToken);

        if (HasInjectedFailure)
            return ValueTask.FromException(
                new IOException("Injected output-stream write failure."));

        if (buffer.Length > ByteLimit - BytesWrittenToInner)
        {
            HasInjectedFailure = true;

            return ValueTask.FromException(
                new IOException("Injected output-stream write failure."));
        }

        return WriteMemoryAsyncCore(buffer, cancellationToken);
    }

    private async ValueTask WriteMemoryAsyncCore(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        await _inner
            .WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);

        BytesWrittenToInner += buffer.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
