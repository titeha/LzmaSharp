using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveInspectorTests
{
  [Fact]
  public void TryDescribeMethods_Bcj2Lzma2Архив_ПеречисляетBcj2ИLzma2()
  {
    byte[] archive = ReadTestData("bcj2_x86_lzma2_d1m_mhc_off.7z");

    bool ok = SevenZipArchiveInspector.TryDescribeMethods(archive, password: null, out string description);

    Assert.True(ok);
    Assert.Contains("BCJ2", description);
    Assert.Contains("LZMA2", description);
  }

  [Fact]
  public void TryDescribeMethods_AesАрхив_ПеречисляетAes()
  {
    byte[] archive = ReadTestData("aes_lzma2_singlefile_pwd_mhe_off.7z");

    bool ok = SevenZipArchiveInspector.TryDescribeMethods(
        archive, password: "LzmaSharp-AES-Stage15", out string description);

    Assert.True(ok);
    Assert.Contains("AES-256", description);
  }

  [Fact]
  public void TryDescribeMethods_CopyАрхив_ПеречисляетCopy()
  {
    byte[] archive = ReadTestData("hello_copy_mhc_off.7z");

    bool ok = SevenZipArchiveInspector.TryDescribeMethods(archive, password: null, out string description);

    Assert.True(ok);
    Assert.Contains("Copy", description);
  }

  [Fact]
  public void TryDescribeMethods_МноготомныйТом_СообщаетЧтоЗаголовокНеРазобран()
  {
    // Первый том многотомного архива — заголовок не разбирается из одного куска.
    byte[] volume = ReadTestData("hello_copy_split_v10k_mhc_off.7z.001");

    bool ok = SevenZipArchiveInspector.TryDescribeMethods(volume, password: null, out string description);

    Assert.False(ok);
    Assert.Contains("заголовок не разобран", description);
  }

  private static byte[] ReadTestData(string fileName, [CallerFilePath] string caller = "")
  {
    string dir = Path.GetDirectoryName(caller)!;
    string path = Path.GetFullPath(Path.Combine(dir, "TestData/Real/", fileName));
    return File.ReadAllBytes(path);
  }
}
