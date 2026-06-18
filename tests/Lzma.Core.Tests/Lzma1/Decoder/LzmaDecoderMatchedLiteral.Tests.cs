using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;
public sealed class LzmaDecoderMatchedLiteralTests
{
  [Fact]
  public void Decode_OneShot_Handles_Literal_After_Match_Using_MatchedLiteral_Mode()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte[] compressed = LzmaTestMatchedLiteralEncoder.Encode_ABC_Match3_Len2_Then_MatchedLiteral(
      props,
      matchedLiteral: (byte)'X');

    byte[] expected = Encoding.ASCII.GetBytes("ABCABX");

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    var output = new byte[expected.Length];
    var res = dec.Decode(compressed, out int consumed, output, out int written, out var progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(expected.Length, written);
    Assert.True(consumed > 0);
    Assert.Equal(expected, output);

    Assert.Equal(expected.Length, progress.BytesWritten);
    Assert.True(progress.BytesRead > 0);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks_Even_After_Match()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte[] compressed = LzmaTestMatchedLiteralEncoder.Encode_ABC_Match3_Len2_Then_MatchedLiteral(
      props,
      matchedLiteral: (byte)'X');

    byte[] expected = Encoding.ASCII.GetBytes("ABCABX");

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    int inputOffset = 0;
    int outputOffset = 0;

    var output = new byte[expected.Length];

    // Мини-чънки: так мы гарантированно гоняем декодер через NeedsMoreInput
    // в самых разных местах, включая середину "matched literal".
    while (outputOffset < output.Length)
    {
      if (inputOffset > compressed.Length)
        throw new InvalidOperationException("Внутренняя ошибка теста: inputOffset ушёл за пределы массива.");

      int inLen = Math.Min(1, compressed.Length - inputOffset);
      ReadOnlySpan<byte> inChunk = inLen == 0 ? ReadOnlySpan<byte>.Empty : compressed.AsSpan(inputOffset, inLen);

      int outLen = Math.Min(1, output.Length - outputOffset);
      Span<byte> outChunk = output.AsSpan(outputOffset, outLen);

      var res = dec.Decode(inChunk, out int consumed, outChunk, out int written, out _);

      inputOffset += consumed;
      outputOffset += written;

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      if (res == LzmaDecodeResult.NeedsMoreInput)
      {
        if (inputOffset >= compressed.Length)
          throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");
        continue;
      }

      Assert.Equal(LzmaDecodeResult.Ok, res);
    }

    Assert.Equal(expected, output);
  }
}
