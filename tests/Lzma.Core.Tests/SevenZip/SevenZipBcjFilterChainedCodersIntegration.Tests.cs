using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjFilterChainedCodersIntegrationTests
{
  [Fact]
  public void Read_ИDecode_ОдинФайл_ОдинFolder_ДваCoder_BcjX86ThenLzma2_Ok()
  {
    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;

    // "Оригинальные" байты (как в исполняемом коде): E8 + rel32.
    // CALL из позиции 0 на цель 0x20 => rel = 0x20 - (0 + 5) = 0x1B.
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = 0x90; // NOP

    plain[0] = 0xE8;
    BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(1, 4), 0x20 - 5);

    // То, что лежит в потоке после BCJ-ENC (абсолютный адрес цели):
    // abs = rel + (pos + 5) = 0x1B + 5 = 0x20.
    byte[] bcjEncoded = (byte[])plain.Clone();
    BinaryPrimitives.WriteInt32LittleEndian(bcjEncoded.AsSpan(1, 4), 0x20);

    // Убедимся, что фильтр реально что-то меняет.
    Assert.NotEqual(plain[1], bcjEncoded[1]);

    // Packed stream: LZMA2(COPY) от bcjEncoded.
    byte[] packed = Lzma2CopyEncoder.Encode(bcjEncoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_BcjX86ThenLzma2(
      packedStream: packed,
      unpackSize: plain.Length,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte);

    // 1) Парсим и проверяем PackedStreamIndices (должен вычислиться как 1).
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;

    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(1UL, folder.PackedStreamIndices[0]);

    // BCJ coder0 methodId = {03 03 01 03}
    Assert.Equal(4, folder.Coders[0].MethodId.Length);
    Assert.Equal(0x03, folder.Coders[0].MethodId[0]);
    Assert.Equal(0x03, folder.Coders[0].MethodId[1]);
    Assert.Equal(0x01, folder.Coders[0].MethodId[2]);
    Assert.Equal(0x03, folder.Coders[0].MethodId[3]);

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

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_BcjX86ThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_BcjX86ThenLzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_BcjX86ThenLzma2(
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

    // coder0: BCJ x86 (filter) — methodId = {03 03 01 03}, props обычно нет.
    h.Add(0x04);     // mainByte: idSize=4, без props
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x01);
    h.Add(0x03);

    // coder1: LZMA2 — idSize=1 + hasProps
    h.Add(0x21);     // mainByte
    h.Add(0x21);     // methodId: LZMA2
    WriteU64(h, 1);  // props size
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    // Вход BCJ связан с выходом LZMA2 => packed stream идёт во вход LZMA2 (InIndex=1).
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    // OutIndex0 (BCJ final)
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
