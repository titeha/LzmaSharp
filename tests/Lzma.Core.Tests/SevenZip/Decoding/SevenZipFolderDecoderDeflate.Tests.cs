using System.IO.Compression;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipFolderDecoderDeflateTests
{
  [Fact]
  public void DecodeFolderToArray_Deflate_СвойстваИгнорируются_ВозвращаетИсходныеБайты()
  {
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    byte[] packed = EncodeDeflate(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x01, 0x08],
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
  public void DecodeFolderToArray_Deflate_ЛишниеРаспакованныеБайты_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    byte[] packed = EncodeDeflate(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x01, 0x08],
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
  public void DecodeFolderToArray_Deflate_НулевойХвостPackedStream_ВозвращаетOk()
  {
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 23 + 9);

    byte[] packedCore = EncodeDeflate(plain);
    byte[] packed = [.. packedCore, 0x00, 0x00, 0x00];

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x01, 0x08],
        properties: [],
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
  public void DecodeFolderToArray_Deflate_ОбрезанныйPackedStream_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 19 + 5);

    byte[] packed = EncodeDeflate(plain);
    Assert.True(packed.Length > 2);

    Array.Resize(ref packed, packed.Length - 2);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x01, 0x08],
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

  [Fact]
  public void DecodeFolderToArray_Deflate_ОжидаемыйРазмерБольшеРеального_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 11);

    byte[] packed = EncodeDeflate(plain);

    var coder = new SevenZipCoderInfo(
        methodId: [0x04, 0x01, 0x08],
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

  private static byte[] EncodeDeflate(ReadOnlySpan<byte> plain)
  {
    using var ms = new MemoryStream();
    using (var ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
    {
      ds.Write(plain);
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
