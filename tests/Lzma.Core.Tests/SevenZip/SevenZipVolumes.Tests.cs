using System.Linq;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipVolumesTests
{
  // Детерминированный «несжимаемый» блок (xorshift) — чтобы архив был заведомо крупным.
  private static byte[] PseudoRandom(int length, uint seed)
  {
    byte[] data = new byte[length];
    uint x = seed;

    for (int i = 0; i < length; i++)
    {
      x ^= x << 13;
      x ^= x >> 17;
      x ^= x << 5;
      data[i] = (byte)x;
    }

    return data;
  }

  [Fact]
  public void SplitJoin_РеальныйАрхив_РаспаковываетсяПослеСклейки()
  {
    byte[] content = PseudoRandom(10 * 1024, seed: 0xC0FFEE); // несжимаемо → архив ~10 КБ

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("data.txt", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive));

    byte[][] volumes = SevenZipVolumes.Split(archive, volumeSize: 1000);

    Assert.True(volumes.Length > 1); // архив реально разбит на несколько томов
    Assert.All(volumes.Take(volumes.Length - 1), v => Assert.Equal(1000, v.Length));
    Assert.True(volumes[^1].Length <= 1000);

    byte[] joined = SevenZipVolumes.Join(volumes);
    Assert.Equal(archive, joined);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeSingleFileToArray(joined, out byte[] decoded, out _));
    Assert.Equal(content, decoded);
  }

  [Fact]
  public void Split_РовноеКратное_БезОстатка()
  {
    byte[] data = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();

    byte[][] volumes = SevenZipVolumes.Split(data, volumeSize: 10);

    Assert.Equal(3, volumes.Length);
    Assert.All(volumes, v => Assert.Equal(10, v.Length));
    Assert.Equal(data, SevenZipVolumes.Join(volumes));
  }

  [Fact]
  public void Split_СОстатком_ПоследнийТомКороче()
  {
    byte[] data = Enumerable.Range(0, 25).Select(i => (byte)i).ToArray();

    byte[][] volumes = SevenZipVolumes.Split(data, volumeSize: 10);

    Assert.Equal(3, volumes.Length);
    Assert.Equal(10, volumes[0].Length);
    Assert.Equal(10, volumes[1].Length);
    Assert.Equal(5, volumes[2].Length);
    Assert.Equal(data, SevenZipVolumes.Join(volumes));
  }

  [Fact]
  public void Split_РазмерБольшеАрхива_ОдинТом()
  {
    byte[] data = [1, 2, 3];

    byte[][] volumes = SevenZipVolumes.Split(data, volumeSize: 1000);

    Assert.Single(volumes);
    Assert.Equal(data, volumes[0]);
  }

  [Fact]
  public void Split_ПустойВход_НетТомов()
  {
    Assert.Empty(SevenZipVolumes.Split([], volumeSize: 100));
  }

  [Fact]
  public void Split_НеположительныйРазмер_Бросает()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => SevenZipVolumes.Split([1, 2, 3], volumeSize: 0));
  }

  [Theory]
  [InlineData(0, 5, "archive.7z.001")]
  [InlineData(4, 5, "archive.7z.005")]
  [InlineData(0, 1500, "archive.7z.0001")]
  [InlineData(1499, 1500, "archive.7z.1500")]
  public void VolumeFileName_ПравильноеИмяИШирина(int index, int count, string expected)
  {
    Assert.Equal(expected, SevenZipVolumes.VolumeFileName("archive.7z", index, count));
  }

  [Fact]
  public void TryParseVolumeName_КорректныйТом_ВозвращаетБазуИИндекс()
  {
    Assert.True(SevenZipVolumes.TryParseVolumeName("archive.7z.001", out string baseName, out int index));
    Assert.Equal("archive.7z", baseName);
    Assert.Equal(0, index);
  }

  [Theory]
  [InlineData("archive.7z")]   // не том — суффикс не цифры
  [InlineData("archive.7z.01")] // меньше 3 цифр
  [InlineData("archive.7z.")]   // пустой суффикс
  [InlineData("noextension")]   // нет точки
  public void TryParseVolumeName_НеТом_Отвергает(string fileName)
  {
    Assert.False(SevenZipVolumes.TryParseVolumeName(fileName, out _, out _));
  }

  [Fact]
  public void VolumeFileName_ЗатемParse_КругаемИндекс()
  {
    string name = SevenZipVolumes.VolumeFileName("data.7z", index: 41, volumeCount: 100);
    Assert.Equal("data.7z.042", name);

    Assert.True(SevenZipVolumes.TryParseVolumeName(name, out string baseName, out int index));
    Assert.Equal("data.7z", baseName);
    Assert.Equal(41, index);
  }
}
