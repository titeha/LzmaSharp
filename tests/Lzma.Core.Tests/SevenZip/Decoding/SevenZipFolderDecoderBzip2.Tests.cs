using System.Text;

using ICSharpCode.SharpZipLib.BZip2;

using Lzma.Core.BZip2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBzip2Tests
{
  [Fact]
  public void DecodeFolderToArray_Bzip2_СвойстваИгнорируются_ВозвращаетИсходныеБайты()
  {
    byte[] plain = new byte[513];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 29 + 11);

    byte[] packed = EncodeBzip2(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [0xDE, 0xAD, 0xBE, 0xEF],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  [Fact]
  public void DecodeFolderToArray_Bzip2_ЛишниеРаспакованныеБайты_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[513];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 5);

    byte[] packed = EncodeBzip2(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length - 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Bzip2_ОжидаемыйРазмерБольшеРеального_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[513];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 9);

    byte[] packed = EncodeBzip2(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length + 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Bzip2_ОбрезанныйPackedStream_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[513];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 7 + 3);

    byte[] packedFull = EncodeBzip2(plain);
    Assert.True(packedFull.Length > 4);

    byte[] packed = packedFull[..^4];

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  // SEC-012b: the actual BZip2 output in the 7z in-memory route is bounded by the declared size.
  // RED before the fix: the result code is the same InvalidData, but the allocation delta is
  // ~21 MiB (above the threshold). After the fix the delta is ~4.3 MiB (format tables only), so
  // the test passes. The 8 MiB threshold is derived from the two measured numbers:
  // 4 550 808 * 1.3 ~= 5 916 050 (lower bound) and 22 244 424 (upper bound, before the fix);
  // 8 MiB = 8 388 608 lies strictly between them.
  [Fact]
  public void SEC012B_RED_Bzip2_ActualGreaterThanDeclared_InvalidDataWithBoundedAllocation()
  {
    const int plainSize = 4 * 1024 * 1024; // 4 MiB of zeros - the ceiling set by the contract (SEC-012b 5.5)
    byte[] plain = new byte[plainSize];

    byte[] packed = EncodeBzip2(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    GC.Collect();
    GC.WaitForPendingFinalizers();

    long before = GC.GetAllocatedBytesForCurrentThread();

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: 16, // declared size is far below the actual 4 MiB
        output: out byte[] output);

    long after = GC.GetAllocatedBytesForCurrentThread();
    GC.Collect();
    GC.WaitForPendingFinalizers();

    long deltaAllocated = after - before;
    const long threshold = 8 * 1024 * 1024; // 8 MiB, justified by the two measured numbers

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
    Assert.True(deltaAllocated < threshold, $"RED/GREEN delta={deltaAllocated} threshold={threshold}");
  }

  // SEC-012b: regression guard, not RED. The test also passed before the fix, because the length
  // check rejected the mismatch. Without a limit a declared size of 0 does not bound the output.
  [Fact]
  public void SEC012B_Bzip2_DeclaredZeroWithRealData_InvalidData()
  {
    byte[] plain = Encoding.UTF8.GetBytes("several hundred bytes of real bzip2 payload, not empty");

    byte[] packed = EncodeBzip2(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x02, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: 0, // declared size 0 while the stream carries real data
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  // SEC-012b: direct check of the internal DecodeBounded on a real stream from BZip2Encoder.
  [Fact]
  public void SEC012B_Bounded_ActualGreaterThanLimit_InvalidDataWithEmptyOutput()
  {
    byte[] plain = new byte[100_000];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i % 251);

    byte[] packed = BZip2Encoder.Encode(plain);

    BZip2DecodeResult result = BZip2Decoder.DecodeBounded(packed, 16, out byte[] output);

    Assert.Equal(BZip2DecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void SEC012B_Bounded_LimitExactlyActual_OkWithSameBytes()
  {
    byte[] plain = new byte[100_000];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i % 251);

    byte[] packed = BZip2Encoder.Encode(plain);

    BZip2DecodeResult full = BZip2Decoder.DecodeBounded(packed, long.MaxValue, out byte[] fullOutput);
    Assert.Equal(BZip2DecodeResult.Ok, full);
    long limit = fullOutput.Length;

    BZip2DecodeResult result = BZip2Decoder.DecodeBounded(packed, limit, out byte[] output);

    Assert.Equal(BZip2DecodeResult.Ok, result);
    Assert.Equal((int)limit, output.Length); // the boundary must not cut off the legitimate last byte
    Assert.Equal(fullOutput, output);
  }

  [Fact]
  public void SEC012B_Bounded_ZeroLimitOnEmptyStream_OkWithEmptyOutput()
  {
    byte[] packed = BZip2Encoder.Encode(System.ReadOnlySpan<byte>.Empty);

    BZip2DecodeResult result = BZip2Decoder.DecodeBounded(packed, 0, out byte[] output);

    Assert.Equal(BZip2DecodeResult.Ok, result);
    Assert.Empty(output);
  }

  private static byte[] EncodeBzip2(byte[] plain)
  {
    using var ms = new MemoryStream();

    using (var bs = new BZip2OutputStream(ms))
    {
      bs.IsStreamOwner = false;
      bs.Write(plain, 0, plain.Length);
    }

    return ms.ToArray();
  }

  private static SevenZipFolderDecodeResult DecodeSingleCoderFolderToArray(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> packed,
      int expectedUnpackSize,
      out byte[] output)
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [(ulong)packed.Length]);

    var folder = new SevenZipFolder(
        Coders: [coder],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [[(ulong)expectedUnpackSize]]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    return SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packed,
        folderIndex: 0,
        output: out output);
  }
}
