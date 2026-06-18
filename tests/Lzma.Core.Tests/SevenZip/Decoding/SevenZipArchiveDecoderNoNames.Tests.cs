using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderNoNamesTests
{
  [Fact]
  public void DecodeAllFilesToArray_ЕслиНетKName_ИспользуетFallbackИмя()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
      plainFileBytes: plain,
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Single(files);

    Assert.Equal("file_0", files[0].Name);
    Assert.Equal(plain, files[0].Bytes);
  }

  [Fact]
  public void DecodeToArray_ЕслиНетKName_ИспользуетFallbackИмя_ИВозвращаетBytesConsumed()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
        plainFileBytes: plain,
        dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("file_0", files[0].Name);
    Assert.Equal(plain, files[0].Bytes);
  }

  [Fact]
  public void DecodeToEntries_ЕслиНетKName_ИспользуетFallbackИмя()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
        plainFileBytes: plain,
        dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(entries);
    Assert.Equal("file_0", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(plain, entries[0].Bytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_ЕслиНетKName_ИспользуетFallbackИмя()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
        plainFileBytes: plain,
        dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal("file_0", fileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void ExtractToDirectory_ЕслиНетKName_ИспользуетFallbackИмя()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
        plainFileBytes: plain,
        dictionarySize: 1 << 20);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderNoNamesTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      string path = Path.Combine(root, "file_0");
      Assert.True(File.Exists(path));
      Assert.Equal(plain, File.ReadAllBytes(path));
    }
    finally
    {
      try
      {
        if (Directory.Exists(root))
          Directory.Delete(root, recursive: true);
      }
      catch
      {
      }
    }
  }

  [Fact]
  public void DecodeToArray_ЕслиНетKName_ДляНесколькихФайлов_ИспользуетFallbackИмена()
  {
    byte[] file0 = MakePattern(64, mul: 13, add: 1);
    byte[] file1 = MakePattern(96, mul: 17, add: 3);

    byte[] archive = Build7zArchive_TwoFiles_TwoCopyFolders_NoNames(
        file0Bytes: file0,
        file1Bytes: file1);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, files.Length);

    Assert.Equal("file_0", files[0].Name);
    Assert.Equal(file0, files[0].Bytes);

    Assert.Equal("file_1", files[1].Name);
    Assert.Equal(file1, files[1].Bytes);
  }

  [Fact]
  public void DecodeToEntries_ЕслиНетKName_ДляНесколькихФайлов_ИспользуетFallbackИмена()
  {
    byte[] file0 = MakePattern(64, mul: 13, add: 1);
    byte[] file1 = MakePattern(96, mul: 17, add: 3);

    byte[] archive = Build7zArchive_TwoFiles_TwoCopyFolders_NoNames(
        file0Bytes: file0,
        file1Bytes: file1);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, entries.Length);

    Assert.Equal("file_0", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(file0, entries[0].Bytes);

    Assert.Equal("file_1", entries[1].Name);
    Assert.False(entries[1].IsDirectory);
    Assert.Equal(file1, entries[1].Bytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_ЕслиНетKName_НоФайловНесколько_ВозвращаетNotSupported()
  {
    byte[] file0 = MakePattern(64, mul: 13, add: 1);
    byte[] file1 = MakePattern(96, mul: 17, add: 3);

    byte[] archive = Build7zArchive_TwoFiles_TwoCopyFolders_NoNames(
        file0Bytes: file0,
        file1Bytes: file1);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void ExtractToDirectory_ЕслиНетKName_ДляНесколькихФайлов_ИспользуетFallbackИмена()
  {
    byte[] file0 = MakePattern(64, mul: 13, add: 1);
    byte[] file1 = MakePattern(96, mul: 17, add: 3);

    byte[] archive = Build7zArchive_TwoFiles_TwoCopyFolders_NoNames(
        file0Bytes: file0,
        file1Bytes: file1);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderNoNamesTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.Equal(file0, File.ReadAllBytes(Path.Combine(root, "file_0")));
      Assert.Equal(file1, File.ReadAllBytes(Path.Combine(root, "file_1")));
    }
    finally
    {
      try
      {
        if (Directory.Exists(root))
          Directory.Delete(root, recursive: true);
      }
      catch
      {
      }
    }
  }

  private static byte[] Build7zArchive_TwoFiles_TwoCopyFolders_NoNames(
      ReadOnlySpan<byte> file0Bytes,
      ReadOnlySpan<byte> file1Bytes)
  {
    // ----- NextHeader ("Header") -----
    List<byte> headerPayload =
    [
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,

        // PackInfo
        SevenZipNid.PackInfo,
    ];

    WriteU64(headerPayload, 0); // PackPos
    WriteU64(headerPayload, 2); // NumPackStreams

    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)file0Bytes.Length);
    WriteU64(headerPayload, (ulong)file1Bytes.Length);
    headerPayload.Add(SevenZipNid.End);

    // UnpackInfo
    headerPayload.Add(SevenZipNid.UnpackInfo);
    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 2); // NumFolders
    headerPayload.Add(0);       // External = 0

    // Folder #0: Copy
    WriteU64(headerPayload, 1); // NumCoders
    headerPayload.Add(0x01);    // mainByte: idSize=1
    headerPayload.Add(0x00);    // MethodID: Copy

    // Folder #1: Copy
    WriteU64(headerPayload, 1); // NumCoders
    headerPayload.Add(0x01);    // mainByte: idSize=1
    headerPayload.Add(0x00);    // MethodID: Copy

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)file0Bytes.Length);
    WriteU64(headerPayload, (ulong)file1Bytes.Length);

    headerPayload.Add(SevenZipNid.End); // End UnpackInfo
    headerPayload.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 2 файла, но без kName.
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 2); // NumFiles
    headerPayload.Add(SevenZipNid.End); // End FilesInfo properties
    headerPayload.Add(SevenZipNid.End); // End Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)(file0Bytes.Length + file1Bytes.Length),
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + file0Bytes.Length + file1Bytes.Length + nextHeader.Length];
    sig.Write(archive);

    int pos = SevenZipSignatureHeader.Size;
    file0Bytes.CopyTo(archive.AsSpan(pos));
    pos += file0Bytes.Length;

    file1Bytes.CopyTo(archive.AsSpan(pos));
    pos += file1Bytes.Length;

    nextHeader.CopyTo(archive.AsSpan(pos));
    return archive;
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    byte[] bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
  }

  private static byte[] Build7zArchive_SingleFile_Lzma2Copy_NoNames(ReadOnlySpan<byte> plainFileBytes, int dictionarySize)
  {
    byte[] packedStream = Lzma2CopyEncoder.Encode(plainFileBytes, dictionarySize, out byte lzma2PropertiesByte);

    // ----- NextHeader ("Header") -----
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.PackInfo,
    ];

    WriteU64(headerPayload, 0); // PackPos
    WriteU64(headerPayload, 1); // NumPackStreams
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedStream.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);

    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0);       // External = 0

    WriteU64(headerPayload, 1); // NumCoders

    headerPayload.Add(0b0010_0001); // MainByte: idSize=1, hasProps=1
    headerPayload.Add(0x21);        // MethodID: LZMA2
    headerPayload.Add(1);           // props size
    headerPayload.Add(lzma2PropertiesByte);

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytes.Length);
    headerPayload.Add(SevenZipNid.End); // End UnpackInfo

    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 1 файл, но без kName
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1);       // NumFiles
    headerPayload.Add(SevenZipNid.End); // End FilesInfo properties

    headerPayload.Add(SevenZipNid.End); // End Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
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

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
