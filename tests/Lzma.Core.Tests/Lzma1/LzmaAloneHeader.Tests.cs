using System.Buffers.Binary;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneHeaderTests
{
  [Fact]
  public void TryRead_NeedMoreInput_КогдаМеньше13Байт()
  {
    for (int len = 0; len < LzmaAloneHeader.HeaderSize; len++)
    {
      var buf = new byte[len];
      var res = LzmaAloneHeader.TryRead(buf, out _, out int consumed);

      Assert.Equal(LzmaAloneHeader.ReadResult.NeedMoreInput, res);
      Assert.Equal(0, consumed);
    }
  }

  [Fact]
  public void TryRead_InvalidData_КогдаНеверныйPropertiesByte()
  {
    var buf = new byte[LzmaAloneHeader.HeaderSize];
    buf[0] = 0xFF; // заведомо неверный properties

    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(5, 8), 0);

    var res = LzmaAloneHeader.TryRead(buf, out _, out int consumed);

    Assert.Equal(LzmaAloneHeader.ReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_InvalidData_КогдаРазмерСловаряНоль()
  {
    var props = new LzmaProperties(3,0,2);
    byte propsByte = props.ToByteOrThrow();

    var buf = new byte[LzmaAloneHeader.HeaderSize];
    buf[0] = propsByte;

    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), 0); // invalid
    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(5, 8), 123);

    var res = LzmaAloneHeader.TryRead(buf, out _, out int consumed);

    Assert.Equal(LzmaAloneHeader.ReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ЧитаетЗаголовок_КогдаРазмерРаспаковкиИзвестен()
  {
    var props = new LzmaProperties(3, 0, 2);
    byte propsByte = props.ToByteOrThrow();

    const int dictionarySize = 1 << 20;
    const ulong unpackSize = 123456789;

    var buf = new byte[LzmaAloneHeader.HeaderSize];
    buf[0] = propsByte;
    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), dictionarySize);
    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(5, 8), unpackSize);

    var res = LzmaAloneHeader.TryRead(buf, out var header, out int consumed);

    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, res);
    Assert.Equal(LzmaAloneHeader.HeaderSize, consumed);

    Assert.Equal(props, header.Properties);
    Assert.Equal(dictionarySize, header.DictionarySize);
    Assert.Equal(unpackSize, header.UncompressedSize);
  }

  [Fact]
  public void TryRead_ЧитаетЗаголовок_КогдаРазмерРаспаковкиНеизвестен()
  {
    var props = new LzmaProperties(3, 0, 2);
    byte propsByte = props.ToByteOrThrow();

    const int dictionarySize = 1 << 20;

    var buf = new byte[LzmaAloneHeader.HeaderSize];
    buf[0] = propsByte;
    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1, 4), (uint)dictionarySize);
    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(5, 8), ulong.MaxValue);

    var res = LzmaAloneHeader.TryRead(buf, out var header, out int consumed);

    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, res);
    Assert.Equal(LzmaAloneHeader.HeaderSize, consumed);

    Assert.Equal(props, header.Properties);
    Assert.Equal(dictionarySize, header.DictionarySize);
    Assert.Null(header.UncompressedSize);
  }

  [Fact]
  public void TryWrite_Пишет13Байт_ИЧитаетсяОбратно()
  {
    var props = new LzmaProperties(3, 0, 2);
    var original = new LzmaAloneHeader(props, dictionarySize: 1 << 22, uncompressedSize: 777);

    Span<byte> buf = stackalloc byte[LzmaAloneHeader.HeaderSize];
    Assert.True(original.TryWrite(buf, out int written));
    Assert.Equal(LzmaAloneHeader.HeaderSize, written);

    var res = LzmaAloneHeader.TryRead(buf, out var parsed, out int consumed);
    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, res);
    Assert.Equal(LzmaAloneHeader.HeaderSize, consumed);

    Assert.Equal(original.Properties, parsed.Properties);
    Assert.Equal(original.DictionarySize, parsed.DictionarySize);
    Assert.Equal(original.UncompressedSize, parsed.UncompressedSize);
  }

  [Fact]
  public void TryWrite_False_КогдаБуферМаленький()
  {
    var props = new LzmaProperties(3, 0, 2);
    var header = new LzmaAloneHeader(props, dictionarySize: 1 << 20, uncompressedSize: 1);

    Span<byte> buf = stackalloc byte[LzmaAloneHeader.HeaderSize - 1];
    Assert.False(header.TryWrite(buf, out int written));
    Assert.Equal(0, written);
  }
}
