using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderEmptyStreamsTests
{
  [Fact]
  public void DecodeAllFilesToArray_FirstFileEmptyStream_SecondHasData_Ok()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);

    Assert.Equal("empty", files[0].Name);
    Assert.Empty(files[0].Bytes);

    Assert.Equal("file.bin", files[1].Name);
    Assert.Equal(data, files[1].Bytes);
  }

  [Fact]
  public void DecodeAllFilesToArray_FirstFileEmptyStream_WithFilesInfoCrcZero_Ok()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    uint emptyCrc = Crc32.Compute([]);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      emptyFileCrc: emptyCrc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);
    Assert.Equal("empty", files[0].Name);
    Assert.Empty(files[0].Bytes);
    Assert.Equal("file.bin", files[1].Name);
    Assert.Equal(data, files[1].Bytes);
  }

  [Fact]
  public void DecodeAllFilesToArray_FirstFileEmptyStream_WithFilesInfoCrcMismatch_InvalidData()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      emptyFileCrc: 1u);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
  }

  [Fact]
  public void DecodeAllFilesToArray_SecondFileHasFilesInfoCrc_Ok()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    uint crc = Crc32.Compute(data);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      secondFileCrc: crc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);
    Assert.Empty(files[0].Bytes);
    Assert.Equal(data, files[1].Bytes);
  }

  [Fact]
  public void DecodeAllFilesToArray_SecondFileHasFilesInfoCrcMismatch_InvalidData()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    uint crcWrong = Crc32.Compute(data) ^ 1u;

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      secondFileCrc: crcWrong);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
  }

  [Fact]
  public void DecodeToEntries_FirstEntryIsDirectory_WhenEmptyStreamAndNoEmptyFileProperty()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "dir",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      firstEmptyIsFile: false);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, entries.Length);

    Assert.Equal("dir", entries[0].Name);
    Assert.True(entries[0].IsDirectory);
    Assert.Empty(entries[0].Bytes);

    Assert.Equal("file.bin", entries[1].Name);
    Assert.False(entries[1].IsDirectory);
    Assert.Equal(data, entries[1].Bytes);
  }

  [Fact]
  public void DecodeToEntries_FirstEntryIsEmptyFile_WhenEmptyFileBitSet()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
      emptyName: "empty.txt",
      fileName: "file.bin",
      fileBytes: data,
      dictionarySize: 1 << 20,
      firstEmptyIsFile: true);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, entries.Length);

    Assert.Equal("empty.txt", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Empty(entries[0].Bytes);
  }

  private static byte[] Build7z_TwoFiles_FirstEmpty_SecondLzma2Copy(
    string emptyName,
    string fileName,
    ReadOnlySpan<byte> fileBytes,
    int dictionarySize,
    uint? emptyFileCrc = null,
    uint? secondFileCrc = null,
    bool firstEmptyIsFile = false)
  {
    byte[] packedStream = Lzma2CopyEncoder.Encode(fileBytes, dictionarySize, out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeader_TwoFiles_FirstEmpty(
      emptyName: emptyName,
      fileName: fileName,
      packSize: (ulong)packedStream.Length,
      unpackSize: (ulong)fileBytes.Length,
      lzma2PropertiesByte: lzma2PropsByte,
      emptyFileCrc: emptyFileCrc,
      secondFileCrc: secondFileCrc,
      firstEmptyIsFile);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStream.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    byte[] archive = new byte[sigBytes.Length + packedStream.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedStream.CopyTo(archive.AsSpan(sigBytes.Length));
    nextHeader.CopyTo(archive.AsSpan(sigBytes.Length + packedStream.Length));

    return archive;
  }

  private static byte[] BuildHeader_TwoFiles_FirstEmpty(
    string emptyName,
    string fileName,
    ulong packSize,
    ulong unpackSize,
    byte lzma2PropertiesByte,
    uint? emptyFileCrc = null,
    uint? secondFileCrc = null,
    bool firstEmptyIsFile = false)
  {
    List<byte> h = new(256)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);

    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0);       // External = 0

    // NumCoders
    WriteU64(h, 1);

    // Coder: LZMA2 (0x21) + properties size 1
    h.Add(0x21); // main byte: idSize=1 + hasProps
    h.Add(0x21); // method id
    WriteU64(h, 1);
    h.Add(lzma2PropertiesByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 2 files
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2);

    // kEmptyStream: [true, false] => 0x80
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x80);

    // kEmptyFile: для EmptyStreams (у нас он один) BIT IsEmptyFile.
    // Если true => 0x80, если false => 0x00.
    if (firstEmptyIsFile)
    {
      h.Add(SevenZipNid.EmptyFile);
      WriteU64(h, 1);     // payload = 1 байт, потому что NumEmptyStreams=1
      h.Add(0x80);        // единственный empty-stream является пустым файлом
    }

    // kCRC (FilesInfo): CRC по файлам. Пишем один блок kCRC, если задан хоть один CRC.
    if (emptyFileCrc.HasValue || secondFileCrc.HasValue)
    {
      h.Add(SevenZipNid.Crc);

      byte bits = 0;
      int definedCount = 0;

      // file0 => 0x80, file1 => 0x40
      if (emptyFileCrc.HasValue)
      {
        bits |= 0x80;
        definedCount++;
      }

      if (secondFileCrc.HasValue)
      {
        bits |= 0x40;
        definedCount++;
      }

      WriteU64(h, (ulong)(1 + 1 + 4 * definedCount)); // allAreDefined + bits + CRCs
      h.Add(0x00); // AllAreDefined=0
      h.Add(bits); // Defined bits

      // CRCs идут по порядку индексов файлов (0, затем 1)
      if (emptyFileCrc.HasValue)
        WriteU32LE(h, emptyFileCrc.Value);

      if (secondFileCrc.HasValue)
        WriteU32LE(h, secondFileCrc.Value);
    }

    // kName
    h.Add(SevenZipNid.Name);
    byte[] namesBytes = Encoding.Unicode.GetBytes(emptyName + "\0" + fileName + "\0");
    WriteU64(h, (ulong)(1 + namesBytes.Length));
    h.Add(0); // External = 0
    h.AddRange(namesBytes);

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

  private static void WriteU32LE(List<byte> dst, uint value)
  {
    dst.Add((byte)value);
    dst.Add((byte)(value >> 8));
    dst.Add((byte)(value >> 16));
    dst.Add((byte)(value >> 24));
  }
}
