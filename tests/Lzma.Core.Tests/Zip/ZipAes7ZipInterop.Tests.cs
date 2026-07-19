using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Интероп с 7-Zip по шифрованию ZIP. Фикстура — реальный ZIP, созданный 7-Zip 23.01
/// (7z a -tzip -pSecret123 -mem=AES256): один файл a.txt = "привет секрет из 7zip".
/// 7-Zip пишет AE-2 (version=2, CRC=0 в заголовке) + extra NTFS 0x000A перед 0x9901.
/// </summary>
public sealed class ZipAes7ZipInteropTests
{
  // Base64 архива, созданного оригинальным 7-Zip (AES-256, пароль Secret123).
  private const string SevenZipAesBase64 =
      "UEsDBDMAAQBjAKxT81wAAAAAQQAAACUAAAAFAAsAYS50eHQBmQcAAgBBRQMAANK74ZTmZ3BqLN+U5F807Wv" +
      "IC7JEClFFoOeaTWsg64fYMI/iyWSuQTdusMpHR83xDrEEKQU0Lc/6uZgon1xErYUfUEsBAj8AMwABAGMArFPzXAAAAABBAAAAJQAAAAUALwAA" +
      "AAAAAAAgAAAAAAAAAGEudHh0CgAgAAAAAAABABgAdCfByi4X3QEAAAAAAAAAAAAAAAAAAAAAAZkHAAIAQUUDAABQSwUGAAAAAAEAAQBiAAAAbwAAAAAA";

  [Fact]
  public void Извлечь_7ZipAES256_РасшифровываетсяВерно()
  {
    byte[] archive = System.Convert.FromBase64String(SevenZipAesBase64.Replace("\n", "").Replace("\r", ""));

    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));
    ZipStreamEntry e = Assert.Single(entries);
    Assert.True(e.IsEncrypted);
    Assert.Equal(WinZipAes.Strength.Aes256, e.AesStrength);

    string dest = Path.Combine(Path.GetTempPath(), "lzs-7zaes-" + System.Guid.NewGuid().ToString("N"));
    try
    {
      var r = ZipStreamExtractor.ExtractToDirectory(
          ms, entries, dest, overwrite: false, currentFile: null, token: default, progress: null,
          password: Encoding.UTF8.GetBytes("Secret123"));
      Assert.Equal(ZipExtractResult.Ok, r);
      Assert.Equal("привет секрет из 7zip", File.ReadAllText(Path.Combine(dest, "a.txt")).Trim());
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }

  [Fact]
  public void Извлечь_7ZipAES256_НеверныйПароль()
  {
    byte[] archive = System.Convert.FromBase64String(SevenZipAesBase64.Replace("\n", "").Replace("\r", ""));
    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    string dest = Path.Combine(Path.GetTempPath(), "lzs-7zaes-bad-" + System.Guid.NewGuid().ToString("N"));
    try
    {
      var r = ZipStreamExtractor.ExtractToDirectory(ms, entries, dest, false, null, default, null, Encoding.UTF8.GetBytes("nope"));
      Assert.Equal(ZipExtractResult.WrongPassword, r);
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }
}
