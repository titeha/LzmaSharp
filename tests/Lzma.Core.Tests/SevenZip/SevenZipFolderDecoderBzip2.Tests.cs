using ICSharpCode.SharpZipLib.BZip2;

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
