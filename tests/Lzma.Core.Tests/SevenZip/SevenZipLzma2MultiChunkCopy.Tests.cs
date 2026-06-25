using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

// Регрессия: байты несжатых (copy) чанков LZMA2 должны попадать в словарь, иначе матчи
// последующих LZMA-чанков, ссылающиеся на них, уезжают → рассинхрон и InvalidData.
//
// Фикстура lzma2_copychunks_lzmafirst.7z собрана настоящим 7-Zip из файла
// [сжимаемое 200 КБ][R 256 КБ][R 256 КБ] (R — случайные данные): R хранится copy-чанками,
// второй R кодируется матчами в copy-регион. Корректная распаковка обязана восстановить
// второй R из первого — то есть две его копии должны совпасть.
public sealed class SevenZipLzma2MultiChunkCopyTests
{
  private const int CompressiblePrefix = 204800;
  private const int RSize = 262144;
  private const int TotalSize = CompressiblePrefix + RSize + RSize;

  [Fact]
  public void DecodeSingleFile_Lzma2СCopyЧанками_ВосстанавливаетМатчиВCopyРегион()
  {
    byte[] archive = ReadTestData("lzma2_copychunks_lzmafirst.7z");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        fileBytes: out byte[] fileBytes,
        fileName: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(TotalSize, fileBytes.Length);

    // Две копии R (по 256 КБ) должны совпасть: вторая восстановлена матчами в copy-чанки первой.
    Assert.True(
        fileBytes.AsSpan(CompressiblePrefix, RSize)
            .SequenceEqual(fileBytes.AsSpan(CompressiblePrefix + RSize, RSize)),
        "Копии R не совпали — байты copy-чанков не дошли до словаря LZMA.");
  }

  private static byte[] ReadTestData(string fileName, [CallerFilePath] string caller = "")
  {
    string dir = Path.GetDirectoryName(caller)!;
    string path = Path.GetFullPath(Path.Combine(dir, "TestData/Real/", fileName));
    return File.ReadAllBytes(path);
  }
}
