using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDictionaryTests
{
  [Fact]
  public void PutByte_ПишетВВыходИОбновляетСловарь()
  {
    var dic = new LzmaDictionary(size: 4);

    Span<byte> output = stackalloc byte[3];
    int pos = 0;

    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(0x10, output, ref pos));
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(0x20, output, ref pos));
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(0x30, output, ref pos));

    Assert.Equal(3, pos);
    Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, output.ToArray());

    Assert.Equal(3, dic.TotalWritten);
    Assert.True(dic.TryGetByteBack(1, out var last));
    Assert.Equal(0x30, last);

    Assert.True(dic.TryGetByteBack(3, out var first));
    Assert.Equal(0x10, first);
  }

  [Fact]
  public void CopyMatch_БезПерекрытия_КопируетОжидаемыеБайты()
  {
    var dic = new LzmaDictionary(size: 8);

    var output = new byte[8];
    int pos = 0;

    // "abcd"
    foreach (var b in new byte[] { (byte)'a', (byte)'b', (byte)'c', (byte)'d' })
      Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(b, output, ref pos));

    // distance=4, length=4 => ещё раз "abcd"
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryCopyMatch(distance: 4, length: 4, output, ref pos));

    Assert.Equal(8, pos);
    Assert.Equal("abcdabcd", Encoding.ASCII.GetString(output));
  }

  [Fact]
  public void CopyMatch_СПерекрытием_РаботаетКакОжидается()
  {
    var dic = new LzmaDictionary(size: 8);

    var output = new byte[4];
    int pos = 0;

    // "a"
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte((byte)'a', output, ref pos));

    // distance=1, length=3 => "aaa" (перекрытие: копируем то, что сами только что записали)
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryCopyMatch(distance: 1, length: 3, output, ref pos));

    Assert.Equal(4, pos);
    Assert.Equal("aaaa", Encoding.ASCII.GetString(output));
  }

  [Fact]
  public void CopyMatch_НевернаяДистанция_ВозвращаетInvalidDistance()
  {
    var dic = new LzmaDictionary(size: 4);

    var output = new byte[1];
    int pos = 0;

    // Нельзя ссылаться на distance=1, если мы ещё не записали ни одного байта.
    Assert.Equal(LzmaDictionaryResult.InvalidDistance,
        dic.TryCopyMatch(distance: 1, length: 1, output, ref pos));

    // distance=0 невалиден.
    Assert.Equal(LzmaDictionaryResult.InvalidDistance,
        dic.TryCopyMatch(distance: 0, length: 1, output, ref pos));

    // distance больше размера словаря.
    Assert.Equal(LzmaDictionaryResult.InvalidDistance,
        dic.TryCopyMatch(distance: 5, length: 1, output, ref pos));
  }

  [Fact]
  public void OutputTooSmall_ВозвращаетсяЕслиВыходНеВмещает()
  {
    var dic = new LzmaDictionary(size: 4);

    var output = new byte[2];
    int pos = 0;

    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(1, output, ref pos));
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(2, output, ref pos));

    // Третий байт уже не помещается.
    Assert.Equal(LzmaDictionaryResult.OutputTooSmall, dic.TryPutByte(3, output, ref pos));

    // А если попросить матч на 1 байт — тоже не поместится.
    // Для этого нужно, чтобы distance было валидным: запишем хотя бы один байт в новый словарь.
    dic.Reset();
    pos = 0;
    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte((byte)'x', output, ref pos));
    // pos == 1, осталось 1 место, но просим длину 2.
    Assert.Equal(LzmaDictionaryResult.OutputTooSmall,
        dic.TryCopyMatch(distance: 1, length: 2, output, ref pos));
  }

  [Fact]
  public void Reset_СбрасываетСчётчики()
  {
    var dic = new LzmaDictionary(size: 4);

    var output = new byte[1];
    int pos = 0;

    Assert.Equal(LzmaDictionaryResult.Ok, dic.TryPutByte(1, output, ref pos));
    Assert.Equal(1, dic.TotalWritten);

    dic.Reset(clearBuffer: true);

    Assert.Equal(0, dic.TotalWritten);
    Assert.Equal(0, dic.Position);

    // После Reset() читать "назад" нельзя.
    Assert.False(dic.TryGetByteBack(1, out _));
  }
}
