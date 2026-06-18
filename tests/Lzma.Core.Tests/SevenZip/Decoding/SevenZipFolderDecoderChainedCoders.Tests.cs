using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderChainedCodersTests
{
  [Fact]
  public void DecodeFolderToArray_TwoCoders_CopyThenLzma2_Returns_OriginalBytes()
  {
    byte[] plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const int dictionarySize = 1 << 20;

    byte[] packed = Lzma2CopyEncoder.Encode(plain, dictionarySize, out byte lzma2PropsByte);

    var packInfo = new SevenZipPackInfo(
      packPos: 0,
      packSizes: [(ulong)packed.Length]);

    var copyCoder = new SevenZipCoderInfo(
      methodId: [0x00],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var lzma2Coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [lzma2PropsByte],
      numInStreams: 1,
      numOutStreams: 1);

    // Порядок coders как у фильтров в реальном 7z: [filter][compression]
    // BindPair связывает вход filter'а с выходом compression.
    var folder = new SevenZipFolder(
      Coders: [copyCoder, lzma2Coder],
      BindPairs: [new SevenZipBindPair(InIndex: 0, OutIndex: 1)],
      PackedStreamIndices: [1], // packed input у LZMA2 (coder #1)
      NumInStreams: 2,
      NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[(ulong)plain.Length, (ulong)plain.Length]]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: streamsInfo,
      packedStreams: packed,
      folderIndex: 0,
      output: out byte[] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r);
    Assert.Equal(plain, decoded);
  }
}
