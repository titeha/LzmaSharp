using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipChainedCodersThreeCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_ThreeCoders_Swap2_Delta_Lzma2_Copy_Ok()
  {
    byte[] plain = new byte[97];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;
    const int deltaDistance = 4;

    // Декодер будет делать: LZMA2 -> DeltaDecode -> Swap2.
    // Поэтому данные для LZMA2 (X) должны быть такие, чтобы:
    // DeltaDecode(X) = Swap2(plain).
    byte[] y = Swap2Transform(plain);
    byte[] x = DeltaEncode(y, deltaDistance);

    byte[] packedStream = Lzma2CopyEncoder.Encode(x, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_ThreeCoders_Swap2_Delta_Lzma2(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte,
      deltaDistance: deltaDistance);

    // Проверяем парсинг + PackedStreamIndices (должен быть = 2 для порядка [Swap2, Delta, LZMA2]).
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    Assert.NotNull(header.StreamsInfo);
    Assert.NotNull(header.StreamsInfo.UnpackInfo);

    SevenZipFolder folder = header.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(3, folder.Coders.Length);
    Assert.Equal(2, folder.BindPairs.Length);
    Assert.Single(folder.PackedStreamIndices);

    // BindPairs: InIndex(0) <- OutIndex(1), InIndex(1) <- OutIndex(2)
    // Значит unbound InIndex == 2 => PackedStreamIndices[0] == 2
    Assert.Equal(2UL, folder.PackedStreamIndices[0]);

    // End-to-end decode.
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] Build7z_SingleFile_SingleFolder_ThreeCoders_Swap2_Delta_Lzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte,
    int deltaDistance)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_ThreeCoders_Swap2_Delta_Lzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte,
      deltaDistance: deltaDistance);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStream.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStream.Length + nextHeader.Length];
    sig.Write(archive);

    packedStream.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packedStream.Length));

    return archive;
  }

  private static byte[] BuildNextHeader_SingleFile_ThreeCoders_Swap2_Delta_Lzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte,
    int deltaDistance)
  {
    Assert.InRange(deltaDistance, 1, 256);
    byte deltaProp = (byte)(deltaDistance - 1);

    List<byte> h = new(512)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External = 0

    // Folder: 3 coders, порядок "как в реальных архивах": фильтры впереди, компрессия последней.
    WriteU64(h, 3);

    // coder0: Swap2 (idSize=3, без props)
    h.Add(0x03); // mainByte: idSize=3
    h.Add(0x02);
    h.Add(0x03);
    h.Add(0x02);

    // coder1: Delta (idSize=1 + hasProps)
    h.Add(0x21);
    h.Add(0x03);
    WriteU64(h, 1);
    h.Add(deltaProp);

    // coder2: LZMA2 (idSize=1 + hasProps)
    h.Add(0x21);
    h.Add(0x21);
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: TotalOutStreams - 1 = 2
    // Swap2.in(0) <- Delta.out(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    // Delta.in(1) <- LZMA2.out(2)
    WriteU64(h, 1);
    WriteU64(h, 2);

    // NumPackedStreams = 3 - 2 = 1 => PackedStreamIndices НЕ записываются, должны вычисляться.

    h.Add(SevenZipNid.CodersUnpackSize);
    // out0 (Swap2 final)
    WriteU64(h, (ulong)unpackSize);
    // out1 (Delta intermediate)
    WriteU64(h, (ulong)unpackSize);
    // out2 (LZMA2 intermediate)
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1);

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static byte[] Swap2Transform(byte[] src)
  {
    byte[] dst = (byte[])src.Clone();
    for (int i = 0; i + 2 <= dst.Length; i += 2)
      (dst[i + 1], dst[i]) = (dst[i], dst[i + 1]);
    return dst;
  }

  private static byte[] DeltaEncode(byte[] src, int delta)
  {
    byte[] dst = new byte[src.Length];

    for (int i = 0; i < src.Length; i++)
    {
      if (i < delta)
        dst[i] = src[i];
      else
        dst[i] = unchecked((byte)(src[i] - src[i - delta]));
    }

    return dst;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
