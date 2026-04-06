using System.Text;
using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderOverwriteTests
{
  [Fact]
  public void ExtractToDirectory_СуществующийФайл_БезOverwrite_InvalidData_ИСтароеСодержимоеСохраняется()
  {
    byte[] archive = Build7z_OneEmptyRegularFile_WithName("file.bin");

    string root = Path.Combine(Path.GetTempPath(), "LzmaSharpTests", Guid.NewGuid().ToString("N"));
    string filePath = Path.Combine(root, "file.bin");
    byte[] oldBytes = [1, 2, 3, 4, 5];

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(filePath, oldBytes);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.True(bytesConsumed > 0);
      Assert.True(File.Exists(filePath));
      Assert.Equal(oldBytes, File.ReadAllBytes(filePath));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void ExtractToDirectory_СуществующийФайл_СOverwrite_Ok_ИФайлПерезаписывается()
  {
    byte[] archive = Build7z_OneEmptyRegularFile_WithName("file.bin");

    string root = Path.Combine(Path.GetTempPath(), "LzmaSharpTests", Guid.NewGuid().ToString("N"));
    string filePath = Path.Combine(root, "file.bin");

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(filePath, [10, 20, 30, 40]);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: true,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.True(bytesConsumed > 0);
      Assert.True(File.Exists(filePath));
      Assert.Empty(File.ReadAllBytes(filePath));
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void ExtractToDirectory_ПутьНазначенияУказываетНаСуществующийФайл_InvalidData()
  {
    byte[] archive = Build7z_OneEmptyRegularFile_WithName("file.bin");

    string baseRoot = Path.Combine(Path.GetTempPath(), "LzmaSharpTests", Guid.NewGuid().ToString("N"));
    string destinationPath = Path.Combine(baseRoot, "target");
    byte[] oldBytes = [7, 8, 9];

    try
    {
      Directory.CreateDirectory(baseRoot);
      File.WriteAllBytes(destinationPath, oldBytes);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        destinationPath,
        overwrite: true,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.True(bytesConsumed > 0);
      Assert.True(File.Exists(destinationPath));
      Assert.Equal(oldBytes, File.ReadAllBytes(destinationPath));
    }
    finally
    {
      if (Directory.Exists(baseRoot))
      {
        Directory.Delete(baseRoot, recursive: true);
      }
    }
  }

  private static byte[] Build7z_OneEmptyRegularFile_WithName(string name)
  {
    List<byte> header =
    [
      SevenZipNid.Header,
      SevenZipNid.FilesInfo,
    ];

    WriteU64(header, 1); // NumFiles

    // kEmptyStream: [true] => 0x80
    header.Add(SevenZipNid.EmptyStream);
    WriteU64(header, 1);
    header.Add(0x80);

    // kEmptyFile: для единственного EmptyStream ставим бит => это пустой файл, а не директория.
    header.Add(SevenZipNid.EmptyFile);
    WriteU64(header, 1);
    header.Add(0x80);

    // kName
    header.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(name + "\0");
    WriteU64(header, (ulong)(1 + nameBytes.Length));
    header.Add(0x00); // External = 0
    header.AddRange(nameBytes);

    header.Add(SevenZipNid.End); // End FilesInfo
    header.Add(SevenZipNid.End); // End Header

    byte[] nextHeader = [.. header];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: 0,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeader.Length];
    sig.Write(archive);
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    return archive;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    for (int i = 0; i < written; i++)
    {
      dst.Add(tmp[i]);
    }
  }
}
