using System.IO;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового маршрутизатора substream-ов: раскладка непрерывного потока по файлам-сегментам
/// в заданном порядке, инкрементальный CRC32, обнаружение несовпадения CRC и переполнения.
/// </summary>
public sealed class SubstreamRoutingWriterTests
{
  private static SubstreamRoutingWriter.Segment Seg(Stream target, byte[] expectedContent, bool withCrc = true)
      => new(target, expectedContent.LongLength, withCrc, Crc32.Compute(expectedContent));

  [Fact]
  public void РаскладываетПоСегментам_ИПроверяетCrc()
  {
    byte[] a = [1, 2, 3];
    byte[] b = [10, 20, 30, 40, 50];
    byte[] c = [99];

    using var sa = new MemoryStream();
    using var sb = new MemoryStream();
    using var sc = new MemoryStream();

    var writer = new SubstreamRoutingWriter([Seg(sa, a), Seg(sb, b), Seg(sc, c)]);

    // Пишем весь конкатенированный поток кусками разной длины (проверяем разбиение через границы).
    byte[] all = [.. a, .. b, .. c];
    writer.Write(all, 0, 4);
    writer.Write(all, 4, 2);
    writer.Write(all, 6, all.Length - 6);

    Assert.True(writer.IsComplete);
    Assert.False(writer.CrcMismatch);
    Assert.False(writer.SizeOverflow);
    Assert.Equal(a, sa.ToArray());
    Assert.Equal(b, sb.ToArray());
    Assert.Equal(c, sc.ToArray());
  }

  [Fact]
  public void НесовпадениеCrc_Помечается()
  {
    byte[] a = [1, 2, 3];

    using var sa = new MemoryStream();
    // Сегмент ожидает CRC пустого массива, а придут реальные байты → несовпадение.
    var seg = new SubstreamRoutingWriter.Segment(sa, a.LongLength, hasCrc: true, expectedCrc: Crc32.Compute([]));
    var writer = new SubstreamRoutingWriter([seg]);

    writer.Write(a, 0, a.Length);

    Assert.True(writer.IsComplete);
    Assert.True(writer.CrcMismatch);
    Assert.Equal(a, sa.ToArray()); // данные всё равно записаны (валидацию делает вызывающий)
  }

  [Fact]
  public void ЛишниеБайты_ПомечаютПереполнение()
  {
    byte[] a = [1, 2, 3];

    using var sa = new MemoryStream();
    var writer = new SubstreamRoutingWriter([Seg(sa, a)]);

    byte[] tooMuch = [1, 2, 3, 4, 5];
    writer.Write(tooMuch, 0, tooMuch.Length);

    Assert.True(writer.SizeOverflow);
    Assert.Equal(a, sa.ToArray()); // записан ровно первый сегмент, лишнее отброшено
  }

  [Fact]
  public void НедописанныйПоток_НеComplete()
  {
    byte[] a = [1, 2, 3, 4, 5];

    using var sa = new MemoryStream();
    var writer = new SubstreamRoutingWriter([Seg(sa, a)]);

    writer.Write(a, 0, 2); // записали только часть

    Assert.False(writer.IsComplete);
    Assert.False(writer.SizeOverflow);
  }

  [Fact]
  public void СегментБезCrc_НеПроверяется()
  {
    byte[] a = [7, 7, 7];

    using var sa = new MemoryStream();
    var seg = new SubstreamRoutingWriter.Segment(sa, a.LongLength, hasCrc: false, expectedCrc: 0xDEADBEEF);
    var writer = new SubstreamRoutingWriter([seg]);

    writer.Write(a, 0, a.Length);

    Assert.True(writer.IsComplete);
    Assert.False(writer.CrcMismatch); // CRC не задан — не сверяем
  }
}
