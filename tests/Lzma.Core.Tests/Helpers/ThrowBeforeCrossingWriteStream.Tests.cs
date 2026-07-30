using System;
using System.IO;
using Xunit;

namespace Lzma.Core.Tests.Helpers;

public sealed class ThrowBeforeCrossingWriteStreamTests
{
    [Fact]
    public void Write_BelowBoundary_WritesAllBytes()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        byte[] data = { 1, 2, 3 };

        stream.Write(data, 0, data.Length);

        Assert.Equal(3L, stream.BytesWrittenToInner);
        Assert.False(stream.HasInjectedFailure);
        Assert.Equal(data, inner.ToArray());
    }

    [Fact]
    public void Write_ExactlyToBoundary_WritesAllBytes()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        byte[] data = { 1, 2, 3, 4, 5 };

        stream.Write(data, 0, data.Length);

        Assert.Equal(5L, stream.BytesWrittenToInner);
        Assert.False(stream.HasInjectedFailure);
        Assert.Equal(data, inner.ToArray());
    }

    [Fact]
    public void Write_CrossingBoundary_ThrowsWithoutChangingInnerOrCounter()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        byte[] initial = { 1, 2, 3 };
        stream.Write(initial, 0, initial.Length);

        byte[] innerBeforeFailure = inner.ToArray();
        long positionBeforeFailure = inner.Position;

        Assert.Throws<IOException>(
            () => stream.Write(new byte[] { 4, 5, 6, 7 }, 0, 4));

        Assert.True(stream.HasInjectedFailure);
        Assert.Equal(3L, stream.BytesWrittenToInner);
        Assert.Equal(positionBeforeFailure, inner.Position);
        Assert.Equal(innerBeforeFailure, inner.ToArray());
    }

    [Fact]
    public void Write_AfterInjectedFailure_RemainsFailedAndChangesNothing()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        Assert.Throws<IOException>(
            () => stream.Write(new byte[6], 0, 6));

        Assert.Throws<IOException>(
            () => stream.Write(new byte[] { 1 }, 0, 1));

        Assert.True(stream.HasInjectedFailure);
        Assert.Equal(0L, stream.BytesWrittenToInner);
        Assert.Equal(0L, inner.Length);
        Assert.Equal(0L, inner.Position);
    }

    [Fact]
    public void Write_ZeroByteLimit_RejectsFirstNonEmptyWrite()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 0,
            leaveOpen: true);

        Assert.Throws<IOException>(
            () => stream.Write(new byte[] { 1 }, 0, 1));

        Assert.True(stream.HasInjectedFailure);
        Assert.Equal(0L, stream.BytesWrittenToInner);
        Assert.Equal(0L, inner.Length);
    }

    [Fact]
    public void Write_ZeroLength_SucceedsAtBoundaryAndAfterFailure()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 0,
            leaveOpen: true);

        byte[] empty = Array.Empty<byte>();

        stream.Write(empty, 0, 0);

        Assert.False(stream.HasInjectedFailure);
        Assert.Equal(0L, stream.BytesWrittenToInner);

        Assert.Throws<IOException>(
            () => stream.Write(new byte[] { 1 }, 0, 1));

        stream.Write(empty, 0, 0);

        Assert.True(stream.HasInjectedFailure);
        Assert.Equal(0L, stream.BytesWrittenToInner);
        Assert.Equal(0L, inner.Length);
    }

    [Fact]
    public void WriteSpan_ExactlyToBoundary_WritesAllBytes()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        byte[] data = { 1, 2, 3, 4, 5 };

        stream.Write(data.AsSpan());

        Assert.Equal(5L, stream.BytesWrittenToInner);
        Assert.False(stream.HasInjectedFailure);
        Assert.Equal(data, inner.ToArray());
    }

    [Fact]
    public void WriteSpan_CrossingBoundary_ThrowsWithoutChangingInnerOrCounter()
    {
        using var inner = new MemoryStream();
        using var stream = new ThrowBeforeCrossingWriteStream(
            inner,
            byteLimit: 5,
            leaveOpen: true);

        byte[] initial = { 1, 2, 3 };
        stream.Write(initial.AsSpan());

        byte[] crossing = { 4, 5, 6, 7 };
        byte[] innerBeforeFailure = inner.ToArray();
        long positionBeforeFailure = inner.Position;

        Assert.Throws<IOException>(
            () => stream.Write(crossing.AsSpan()));

        Assert.True(stream.HasInjectedFailure);
        Assert.Equal(3L, stream.BytesWrittenToInner);
        Assert.Equal(positionBeforeFailure, inner.Position);
        Assert.Equal(innerBeforeFailure, inner.ToArray());
    }
}
