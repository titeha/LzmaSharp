using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDecodeAllFilesToArrayTests
{
  [Fact]
  public void DecodeAllFilesToArray_Real7z_MultiFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/solid_a_empty_b_lzma2_d1m_mhc_off.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(3, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (SevenZipDecodedFile f in files)
      byName.Add(f.Name.Replace('\\', '/'), f);

    Assert.Equal(MakeFilled(4096, 0x41), byName["a.bin"].Bytes);
    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(MakeFilled(6000, 0x42), byName["b.bin"].Bytes);
  }

  [Fact]
  public void DecodeAllFilesToArray_Real7z_EncryptedHeader_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Empty(files);
  }

  private static byte[] MakeFilled(int length, byte value)
  {
    byte[] bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
