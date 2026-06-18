using System.Numerics;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDistanceDecoderTests
{
  [Fact]
  public void ComputeDistance_PosSlotBelow4_IsPosSlotPlus1()
  {
    Assert.Equal(1u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 0, reverseBits: 123, directBits: 456, alignBits: 789));
    Assert.Equal(2u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 1, reverseBits: 0, directBits: 0, alignBits: 0));
    Assert.Equal(3u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 2, reverseBits: 0, directBits: 0, alignBits: 0));
    Assert.Equal(4u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 3, reverseBits: 0, directBits: 0, alignBits: 0));
  }

  [Fact]
  public void ComputeDistance_PosSlot4_UsesReverseBits()
  {
    // posSlot=4 => numDirectBits=1 => base pos=4 => distance = (pos + reverseBits) + 1
    Assert.Equal(5u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 4, reverseBits: 0, directBits: 123, alignBits: 456));
    Assert.Equal(6u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 4, reverseBits: 1, directBits: 123, alignBits: 456));
  }

  [Fact]
  public void ComputeDistance_PosSlot14_UsesDirectAndAlignBits()
  {
    // posSlot=14 => numDirectBits=6, base pos=128
    // directBits (2 бита) идут в старшие младшие разряды (<< 4), alignBits (4 бита) - в самый низ.
    Assert.Equal(129u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 14, reverseBits: 999, directBits: 0, alignBits: 0));
    Assert.Equal(192u, LzmaDistanceDecoder.ComputeDistanceFromPosSlot(posSlot: 14, reverseBits: 999, directBits: 3, alignBits: 15));
  }

  [Fact]
  public void DecodeDistance_AllZeroBits_Returns1()
  {
    var decoder = new LzmaDistanceDecoder();
    decoder.Reset();

    var range = new LzmaRangeDecoder();

    // Нулевые init-байты => code=0 => TryDecodeBit почти всегда даёт 0.
    var init = new byte[5];
    int initOffset = 0;
    var initRes = range.TryInitialize(init, ref initOffset);
    Assert.Equal(LzmaRangeInitResult.Ok, initRes);
    Assert.Equal(5, initOffset);

    int offset = 0;
    var res = decoder.TryDecodeDistance(
        ref range,
        [],
        ref offset,
        lenToPosState: 0,
        out uint distance);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(1u, distance);
    Assert.Equal(0, offset);
  }

  [Fact]
  public void DecodeDistance_InvalidLenToPosState_Throws()
  {
    var decoder = new LzmaDistanceDecoder();
    var range = new LzmaRangeDecoder();
    int initOffset = 0;
    range.TryInitialize(new byte[5], ref initOffset);

    int offset = 0;

    void Act() => decoder.TryDecodeDistance(
        ref range,
        [],
        ref offset,
        lenToPosState: 123,
        out _);

    Assert.Throws<ArgumentOutOfRangeException>(Act);
  }
}
