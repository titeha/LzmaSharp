using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterLzma2DictionarySizeTests
{
  // Детерминированный «несжимаемый» блок (xorshift) — чтобы повтор ловился только словарём.
  private static byte[] PseudoRandomBlock(int length, uint seed)
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

  // Вход с повтором блока на дистанции > 64 КБ: блок, наполнитель, тот же блок.
  private static byte[] BuildLongRangeRepeat()
  {
    byte[] block = PseudoRandomBlock(32 * 1024, seed: 0x1234_5678);
    byte[] filler = new byte[100 * 1024]; // нули — сжимаются почти в ничто

    byte[] data = new byte[block.Length + filler.Length + block.Length];
    block.CopyTo(data, 0);
    filler.CopyTo(data, block.Length);
    block.CopyTo(data, block.Length + filler.Length);
    return data;
  }

  [Fact]
  public void Options_ДефолтныйСловарь_БайтВБайтКакEnumПерегрузка()
  {
    byte[] content = BuildLongRangeRepeat();
    var entry = new SevenZipArchiveWriterEntry("data.bin", content);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [entry], SevenZipWriterCompressionMethod.Lzma2, out byte[] viaEnum));

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [entry], new SevenZipCompressionOptions { Method = SevenZipWriterCompressionMethod.Lzma2 }, out byte[] viaOptions));

    Assert.Equal(viaEnum, viaOptions);
  }

  [Fact]
  public void БольшойСловарь_RoundTrip()
  {
    byte[] content = BuildLongRangeRepeat();

    var options = new SevenZipCompressionOptions
    {
      Method = SevenZipWriterCompressionMethod.Lzma2,
      Lzma2DictionarySize = 1 << 20, // 1 МиБ
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("data.bin", content)], options, out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeSingleFileToArray(archive, out byte[] decoded, out _));
    Assert.Equal(content, decoded);
  }

  [Fact]
  public void БольшийСловарь_ЖмётЛучше_НаДальнихПовторах()
  {
    byte[] content = BuildLongRangeRepeat(); // повтор на дистанции ~132 КБ

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("data.bin", content)],
        new SevenZipCompressionOptions { Method = SevenZipWriterCompressionMethod.Lzma2, Lzma2DictionarySize = 1 << 16 }, // 64 КБ — повтор не достаёт
        out byte[] small));

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("data.bin", content)],
        new SevenZipCompressionOptions { Method = SevenZipWriterCompressionMethod.Lzma2, Lzma2DictionarySize = 1 << 18 }, // 256 КБ — повтор виден
        out byte[] big));

    // С 64 КБ второй блок (~32 КБ) кодируется заново; с 256 КБ — становится ссылкой.
    // Разница должна быть весомой (заведомо больше 16 КБ).
    Assert.True(small.Length - big.Length > 16 * 1024,
        $"ожидался заметный выигрыш: 64КБ={small.Length}, 256КБ={big.Length}");

    // Оба архива корректно распаковываются.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeSingleFileToArray(small, out byte[] a, out _));
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeSingleFileToArray(big, out byte[] b, out _));
    Assert.Equal(content, a);
    Assert.Equal(content, b);
  }

  [Fact]
  public void СлишкомМаленькийСловарь_InvalidData()
  {
    var options = new SevenZipCompressionOptions
    {
      Method = SevenZipWriterCompressionMethod.Lzma2,
      Lzma2DictionarySize = 1024, // < 4 КБ — некодируемо
    };

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("a.txt", "содержимое"u8.ToArray())], options, out _));
  }

  [Fact]
  public void СловарьБольше2ГиБ_NotSupported()
  {
    var options = new SevenZipCompressionOptions
    {
      Method = SevenZipWriterCompressionMethod.Lzma2,
      Lzma2DictionarySize = int.MaxValue, // канонический размер выйдет за Int32 → не поддержано
    };

    Assert.Equal(SevenZipArchiveWriteResult.NotSupported, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("a.txt", "содержимое"u8.ToArray())], options, out _));
  }
}
