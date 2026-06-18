using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipDeltaFilterChainedCodersIntegrationTests
{
  [Fact]
  public void Read_ИDecode_ОдинФайл_ОдинFolder_ДваCoder_DeltaThenLzma2_Ok()
  {
    byte[] plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;
    const int deltaDistance = 4;

    byte[] deltaEncoded = DeltaEncode(plain, deltaDistance);

    // Packed stream = LZMA2(COPY) от delta-encoded данных.
    byte[] packed = Lzma2CopyEncoder.Encode(deltaEncoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_DeltaThenLzma2(
      packedStream: packed,
      unpackSize: plain.Length,
      fileName: fileName,
      deltaDistance: deltaDistance,
      lzma2PropsByte: lzma2PropsByte);

    // 1) Читаем архив и проверяем, что PackedStreamIndices вычислился как 1.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;
    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(1UL, folder.PackedStreamIndices[0]);

    // 2) Декодируем folder напрямую.
    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    Assert.Equal(
      SevenZipFolderDecodeResult.Ok,
      SevenZipFolderDecoder.DecodeFolderToArray(header.StreamsInfo, packedStreams, folderIndex: 0, out byte[] folderBytes));

    Assert.Equal(plain, folderBytes);

    // 3) End-to-end через ArchiveDecoder.
    SevenZipArchiveDecodeResult dr = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int decodeConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, dr);
    Assert.Equal(archive.Length, decodeConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] DeltaEncode(ReadOnlySpan<byte> src, int delta)
  {
    if ((uint)(delta - 1) > 255u)
      throw new ArgumentOutOfRangeException(nameof(delta));

    byte[] s = src.ToArray();
    byte[] dst = new byte[s.Length];

    for (int i = 0; i < s.Length; i++)
    {
      if (i < delta)
        dst[i] = s[i];
      else
        dst[i] = unchecked((byte)(s[i] - s[i - delta]));
    }

    return dst;
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_DeltaThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    int deltaDistance,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_DeltaThenLzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
      deltaDistance: deltaDistance,
      lzma2PropsByte: lzma2PropsByte);

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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_DeltaThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    int deltaDistance,
    byte lzma2PropsByte)
  {
    if ((uint)(deltaDistance - 1) > 255u)
      throw new ArgumentOutOfRangeException(nameof(deltaDistance));

    byte deltaPropByte = (byte)(deltaDistance - 1);

    List<byte> h = new(512)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, 0);            // PackPos
    WriteU64(h, 1);            // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);  // NumFolders
    h.Add(0x00);     // External = 0

    // Folder: NumCoders = 2
    WriteU64(h, 2);

    // coder0: Delta (filter) — idSize=1 + hasProps
    h.Add(0x21); // mainByte
    h.Add(0x03); // methodId: Delta
    WriteU64(h, 1);
    h.Add(deltaPropByte);

    // coder1: LZMA2 — idSize=1 + hasProps
    h.Add(0x21); // mainByte
    h.Add(0x21); // methodId: LZMA2
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    // Вход Delta связан с выходом LZMA2 => packed stream идёт во вход LZMA2 (InIndex=1).
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    // OutIndex0 (Delta final)
    WriteU64(h, (ulong)unpackSize);
    // OutIndex1 (LZMA2 intermediate)
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1); // NumFiles

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
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
