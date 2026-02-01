using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipEncodedUInt64Tests
{
  // Вместо IEnumerable<object[]> используем TheoryData<...>.
  // Это не влияет на выполнение тестов, но даёт больше типобезопасности
  // и убирает предупреждение анализаторов xUnit.
  public static TheoryData<ulong, byte[]> Examples => new()
  {
    // 1 байт
    { 0UL, new byte[] { 0x00 } },
    { 1UL, new byte[] { 0x01 } },
    { 127UL, new byte[] { 0x7F } },

    // 2 байта (N=1)
    { 128UL, new byte[] { 0x80, 0x80 } },
    { 255UL, new byte[] { 0x80, 0xFF } },
    { 256UL, new byte[] { 0x81, 0x00 } },
    { 16383UL, new byte[] { 0xBF, 0xFF } },

    // 3 байта (N=2)
    { 16384UL, new byte[] { 0xC0, 0x00, 0x40 } },

    // 9 байт (0xFF + 8 байт значения)
    { ulong.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF } },
  };

  [Theory]
  [MemberData(nameof(Examples))]
  public void TryRead_Декодирует_ИзвестныеПримеры(ulong expected, byte[] encoded)
  {
    var res = SevenZipEncodedUInt64.TryRead(encoded, out ulong value, out int bytesRead);

    Assert.Equal(SevenZipEncodedUInt64.ReadResult.Ok, res);
    Assert.Equal(encoded.Length, bytesRead);
    Assert.Equal(expected, value);
  }

  [Theory]
  [InlineData(0UL)]
  [InlineData(1UL)]
  [InlineData(127UL)]
  [InlineData(128UL)]
  [InlineData(255UL)]
  [InlineData(256UL)]
  [InlineData(16383UL)]
  [InlineData(16384UL)]
  [InlineData(2097151UL)] // 2^21 - 1
  [InlineData(2097152UL)] // 2^21
  [InlineData(ulong.MaxValue)]
  public void TryWriteThenTryRead_Раундтрип(ulong value)
  {
    Span<byte> buf = stackalloc byte[9];

    var w = SevenZipEncodedUInt64.TryWrite(value, buf, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, w);
    Assert.InRange(written, 1, 9);

    var r = SevenZipEncodedUInt64.TryRead(buf[..written], out ulong decoded, out int read);
    Assert.Equal(SevenZipEncodedUInt64.ReadResult.Ok, r);
    Assert.Equal(written, read);
    Assert.Equal(value, decoded);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиНеХватаетБайт()
  {
    // Первый байт 0x80 означает N=1 (нужно ещё 1 байт), но мы его не дали.
    byte[] truncated = [0x80];

    var r = SevenZipEncodedUInt64.TryRead(truncated, out _, out int read);

    Assert.Equal(SevenZipEncodedUInt64.ReadResult.NeedMoreInput, r);
    Assert.Equal(0, read);
  }

  [Fact]
  public void TryWrite_NeedMoreOutput_ЕслиБуферСлишкомМал()
  {
    // 128 кодируется как 2 байта.
    Span<byte> tooSmall = stackalloc byte[1];

    var w = SevenZipEncodedUInt64.TryWrite(128UL, tooSmall, out int written);

    Assert.Equal(SevenZipEncodedUInt64.WriteResult.NeedMoreOutput, w);
    Assert.Equal(0, written);
  }
}
