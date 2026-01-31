using System.Buffers;

namespace Lzma.Core.Lzma2;

/// <summary>
/// Высокоуровневые методы для декодирования целого потока LZMA2 в массив байт.
/// </summary>
/// <remarks>
/// <para>
/// LZMA2 поток имеет маркер конца (0x00), поэтому заранее знать размер распакованных данных не обязательно.
/// </para>
/// <para>
/// Реализация здесь намеренно простая: вызываем <see cref="Lzma2IncrementalDecoder"/> в цикле,
/// наращивая буфер результата.
/// </para>
/// </remarks>
public static class Lzma2Decoder
{
  private const int _defaultOutputChunkSize = 64 * 1024;

  /// <summary>
  /// Декодирует поток LZMA2 в массив байт.
  /// </summary>
  public static Lzma2DecodeResult DecodeToArray(
      ReadOnlySpan<byte> input,
      int dictionarySize,
      out byte[] output,
      out int bytesConsumed)
  {
    var decoder = new Lzma2IncrementalDecoder(dictionarySize: dictionarySize);
    return DecodeToArray(decoder, input, out output, out bytesConsumed);
  }

  /// <summary>
  /// Декодирует поток LZMA2 в массив байт.
  /// </summary>
  public static Lzma2DecodeResult DecodeToArray(
      ReadOnlySpan<byte> input,
      Lzma2Properties properties,
      out byte[] output,
      out int bytesConsumed)
  {
    if (!properties.TryGetDictionarySizeInt32(out int dictionarySize))
    {
      output = Array.Empty<byte>();
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    var decoder = new Lzma2IncrementalDecoder(dictionarySize: dictionarySize);
    return DecodeToArray(decoder, input, out output, out bytesConsumed);
  }

  /// <summary>
  /// Декодирует поток LZMA2 в массив байт.
  /// </summary>
  public static Lzma2DecodeResult DecodeToArray(
      ReadOnlySpan<byte> input,
      byte dictionaryProp,
      out byte[] output,
      out int bytesConsumed)
  {
    if (!Lzma2Properties.TryParse(dictionaryProp, out var properties))
    {
      output = [];
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    return DecodeToArray(input, properties, out output, out bytesConsumed);
  }

  private static Lzma2DecodeResult DecodeToArray(
      Lzma2IncrementalDecoder decoder,
      ReadOnlySpan<byte> input,
      out byte[] output,
      out int bytesConsumed)
  {
    int inputOffset = 0;
    var writer = new ArrayBufferWriter<byte>();

    while (true)
    {
      Span<byte> outSpan = writer.GetSpan(_defaultOutputChunkSize);

      Lzma2DecodeResult result = decoder.Decode(
          input.Slice(inputOffset),
          outSpan,
          out int consumed,
          out int written);

      inputOffset += consumed;
      writer.Advance(written);

      if (result == Lzma2DecodeResult.NeedMoreOutput)
      {
        if (consumed == 0 && written == 0)
          throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

        continue;
      }

      bytesConsumed = inputOffset;
      output = writer.WrittenSpan.ToArray();
      return result;
    }
  }
}
