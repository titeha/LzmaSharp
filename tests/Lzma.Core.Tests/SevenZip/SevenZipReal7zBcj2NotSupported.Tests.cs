using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2NotSupportedTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_Bcj2_ReturnsNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_x86_lzma2_d1m_mhc.7z");

    // 1) Парсинг header должен работать и показать BCJ2 coder.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipFolder folder = header.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Contains(folder.Coders, c => IsBcj2(c.MethodId));

    var bcj2 = folder.Coders.First(c => IsBcj2(c.MethodId));
    Assert.True(bcj2.NumInStreams != 1 || bcj2.NumOutStreams != 1);

    // 2) Декодирование пока не поддерживаем => NotSupported (это фиксируем контрактом).
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
  }

  private static bool IsBcj2(byte[] methodId)
  {
    // BCJ2 обычно идёт как 4 байта 03 03 01 1B. :contentReference[oaicite:3]{index=3}
    // На всякий случай допускаем и короткий 1B.
    return
      methodId.Length == 1 && methodId[0] == 0x1B ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x1B;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
