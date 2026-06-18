using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipChainedCodersIntegrationTests
{
  [Fact]
  public void Read_ИDecode_ОдинФайл_ОдинFolder_ДваCoder_CopyThenLzma2_PackedStreamIndexDerived_Ok()
  {
    byte[] plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_CopyThenLzma2(
      plainFileBytes: plain,
      fileName: fileName,
      dictionarySize: dictionarySize);

    // 1) Проверяем парсинг Header -> UnpackInfo (PackedStreamIndices должен быть вычислен как 1).
    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult rr = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, rr);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    Assert.NotNull(header.StreamsInfo);
    Assert.NotNull(header.StreamsInfo.UnpackInfo);

    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;
    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);

    // Для цепочки Copy <- LZMA2 bindPair = InIndex(0) <- OutIndex(1)
    // единственный "не связанный" InIndex = 1 => PackedStreamIndices[0] == 1.
    Assert.Equal(1UL, folder.PackedStreamIndices[0]);

    // 2) Декодируем folder напрямую (проверяем цепочку coders).
    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult fr = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: header.StreamsInfo,
      packedStreams: packedStreams,
      folderIndex: 0,
      output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, fr);
    Assert.Equal(plain, folderBytes);

    // 3) Декодируем через ArchiveDecoder (проверяем end-to-end разбиение на файлы).
    SevenZipArchiveDecodeResult dr = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int decodeBytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, dr);
    Assert.Equal(archive.Length, decodeBytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_CopyThenLzma2(
    ReadOnlySpan<byte> plainFileBytes,
    string fileName,
    int dictionarySize)
  {
    // Packed stream: LZMA2 (COPY-режим внутри LZMA2 потока).
    byte[] packed = Lzma2CopyEncoder.Encode(plainFileBytes, dictionarySize, out byte lzma2PropsByte);

    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_CopyThenLzma2(
      packSize: packed.Length,
      unpackSize: plainFileBytes.Length,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packed.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packed.Length + nextHeader.Length];
    sig.Write(archive);

    packed.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packed.Length));

    return archive;
  }

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_CopyThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
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

    // coder0: Copy (filter) — idSize=1, без props
    h.Add(0x01); // mainByte
    h.Add(0x00); // methodId

    // coder1: LZMA2 — idSize=1 + hasProps
    h.Add(0x21); // mainByte
    h.Add(0x21); // methodId
    WriteU64(h, 1); // props size
    h.Add(lzma2PropsByte);

    // BindPairs: TotalOutStreams - 1 = 1
    // InIndex(0) <- OutIndex(1): вход Copy связан с выходом LZMA2.
    WriteU64(h, 0);
    WriteU64(h, 1);

    // NumPackedStreams = TotalInStreams - NumBindPairs = 2 - 1 = 1
    // => PackedStreamIndices НЕ хранятся, индекс должен вычисляться reader'ом.

    h.Add(SevenZipNid.CodersUnpackSize);
    // out0 (Copy) final
    WriteU64(h, (ulong)unpackSize);
    // out1 (LZMA2) intermediate
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
