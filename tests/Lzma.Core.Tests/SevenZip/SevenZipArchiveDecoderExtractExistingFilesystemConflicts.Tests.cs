using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests
{
  [Fact]
  public void ExtractToDirectory_КореньНазначенияУжеСуществуетКакФайл_InvalidData()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    byte[] archive = BuildArchiveSingleFileCopy(plain, "file.bin");

    string sandboxRoot = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests),
      Guid.NewGuid().ToString("N"));

    string destinationAsFile = Path.Combine(sandboxRoot, "dest-as-file");

    try
    {
      Directory.CreateDirectory(sandboxRoot);
      File.WriteAllBytes(destinationAsFile, [1, 2, 3]);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        destinationAsFile,
        overwrite: false,
        out int consumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, consumed);
      Assert.True(File.Exists(destinationAsFile));
      Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destinationAsFile));
    }
    finally
    {
      TryDeleteTree(sandboxRoot);
    }
  }

  [Fact]
  public void ExtractToDirectory_ОдинИзРодительскихСегментовКорняУжеФайл_InvalidData()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    byte[] archive = BuildArchiveSingleFileCopy(plain, "file.bin");

    string sandboxRoot = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests),
      Guid.NewGuid().ToString("N"));

    string fileSegment = Path.Combine(sandboxRoot, "file-segment");
    string destination = Path.Combine(fileSegment, "child");

    try
    {
      Directory.CreateDirectory(sandboxRoot);
      File.WriteAllBytes(fileSegment, [9, 8, 7]);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        destination,
        overwrite: false,
        out int consumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, consumed);
      Assert.True(File.Exists(fileSegment));
      Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(fileSegment));
      Assert.False(Directory.Exists(destination));
    }
    finally
    {
      TryDeleteTree(sandboxRoot);
    }
  }

  [Fact]
  public void ExtractToDirectory_ДляФайлаОдинИзРодительскихСегментовУжеФайл_InvalidData_ИНичегоНеИзвлекает()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    byte[] archive = BuildArchiveSingleFileCopy(plain, "dir/file.bin");

    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
      archive,
      out SevenZipDecodedEntry[] entries,
      out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);
    Assert.Single(entries);
    Assert.Equal("dir/file.bin", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(plain, entries[0].Bytes);

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests),
      Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(Path.Combine(root, "dir"), [5, 4, 3, 2, 1]);

      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
      Assert.Equal(archive.Length, consumed2);

      Assert.True(File.Exists(Path.Combine(root, "dir")));
      Assert.Equal(new byte[] { 5, 4, 3, 2, 1 }, File.ReadAllBytes(Path.Combine(root, "dir")));
      Assert.False(File.Exists(Path.Combine(root, "dir", "file.bin")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_ДляКаталогаПоТомуЖеПутиУжеФайл_InvalidData_ИНичегоНеИзвлекает()
  {
    byte[] archive = BuildArchiveSingleDirectory("dir");

    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
      archive,
      out SevenZipDecodedEntry[] entries,
      out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);
    Assert.Single(entries);
    Assert.Equal("dir", entries[0].Name);
    Assert.True(entries[0].IsDirectory);
    Assert.Empty(entries[0].Bytes);

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests),
      Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(Path.Combine(root, "dir"), [0x10, 0x20]);

      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
      Assert.Equal(archive.Length, consumed2);

      Assert.True(File.Exists(Path.Combine(root, "dir")));
      Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(Path.Combine(root, "dir")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_ДляФайлаПоТомуЖеПутиУжеКаталог_InvalidData_ИНеПерезаписываетКаталог()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    byte[] archive = BuildArchiveSingleFileCopy(plain, "target.bin");

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingFilesystemConflictsTests),
      Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(Path.Combine(root, "target.bin"));

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: true,
        out int consumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, consumed);
      Assert.True(Directory.Exists(Path.Combine(root, "target.bin")));
      Assert.False(File.Exists(Path.Combine(root, "target.bin")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] BuildArchiveSingleFileCopy(byte[] plain, string fileName)
  {
    byte[] nextHeader = BuildNextHeaderSingleFileCopy(
      packSize: plain.Length,
      unpackSize: plain.Length,
      fileName: fileName);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);
    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)plain.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + plain.Length + nextHeader.Length];
    sig.Write(archive);
    Buffer.BlockCopy(plain, 0, archive, SevenZipSignatureHeader.Size, plain.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + plain.Length, nextHeader.Length);
    return archive;
  }

  private static byte[] BuildArchiveSingleDirectory(string directoryName)
  {
    byte[] nextHeader = BuildNextHeaderSingleDirectory(directoryName);
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: 0,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeader.Length];
    sig.Write(archive);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size, nextHeader.Length);
    return archive;
  }

  private static byte[] BuildNextHeaderSingleFileCopy(int packSize, int unpackSize, string fileName)
  {
    List<byte> h =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.PackInfo,
    ];

    WriteU64(h, 0);
    WriteU64(h, 1);
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);
    h.Add(0x00);
    WriteU64(h, 1);
    h.Add(0x01);
    h.Add(0x00);
    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);
    h.Add(SevenZipNid.End);
    h.Add(SevenZipNid.End);

    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1);
    WriteNameProperty(h, fileName);
    h.Add(SevenZipNid.End);
    h.Add(SevenZipNid.End);

    return [.. h];
  }

  private static byte[] BuildNextHeaderSingleDirectory(string directoryName)
  {
    List<byte> h =
    [
      SevenZipNid.Header,
      SevenZipNid.FilesInfo,
    ];

    WriteU64(h, 1);
    WriteNameProperty(h, directoryName);
    WriteSingleEmptyStreamProperty(h);
    h.Add(SevenZipNid.End);
    h.Add(SevenZipNid.End);

    return [.. h];
  }

  private static void WriteNameProperty(List<byte> h, string fileName)
  {
    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00);
    h.AddRange(nameBytes);
  }

  private static void WriteSingleEmptyStreamProperty(List<byte> h)
  {
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x80);
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    byte[] bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (!Directory.Exists(root))
        return;

      foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        File.SetAttributes(filePath, FileAttributes.Normal);

      string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
      Array.Sort(dirs, static (a, b) => b.Length.CompareTo(a.Length));
      foreach (string dirPath in dirs)
        File.SetAttributes(dirPath, FileAttributes.Directory);

      File.SetAttributes(root, FileAttributes.Directory);
    }
    catch
    {
    }

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
