using System.Buffers;
using System.IO;

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
      out int bytesConsumed,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    var decoder = new Lzma2IncrementalDecoder(progress: progress, dictionarySize: dictionarySize);
    return DecodeToArray(decoder, input, out output, out bytesConsumed, token);
  }

  /// <summary>
  /// Декодирует поток LZMA2 в массив байт.
  /// </summary>
  public static Lzma2DecodeResult DecodeToArray(
      ReadOnlySpan<byte> input,
      Lzma2Properties properties,
      out byte[] output,
      out int bytesConsumed,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    if (!properties.TryGetDictionarySizeInt32(out int dictionarySize))
    {
      output = Array.Empty<byte>();
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    var decoder = new Lzma2IncrementalDecoder(progress: progress, dictionarySize: dictionarySize);
    return DecodeToArray(decoder, input, out output, out bytesConsumed, token);
  }

  /// <summary>
  /// Декодирует поток LZMA2 в массив байт.
  /// </summary>
  public static Lzma2DecodeResult DecodeToArray(
      ReadOnlySpan<byte> input,
      byte dictionaryProp,
      out byte[] output,
      out int bytesConsumed,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    if (!Lzma2Properties.TryParse(dictionaryProp, out var properties))
    {
      output = [];
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    return DecodeToArray(input, properties, out output, out bytesConsumed, progress, token);
  }

  /// <summary>
  /// Декодирует поток LZMA2 напрямую в <see cref="Stream"/>, не накапливая весь выход в памяти
  /// (для больших файлов). <paramref name="bytesWritten"/> — сколько байт записано (long).
  /// </summary>
  public static Lzma2DecodeResult DecodeToStream(
      ReadOnlySpan<byte> input,
      byte dictionaryProp,
      Stream output,
      out long bytesWritten,
      out int bytesConsumed,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    if (!Lzma2Properties.TryParse(dictionaryProp, out var properties))
    {
      bytesWritten = 0;
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    return DecodeToStream(input, properties, output, out bytesWritten, out bytesConsumed, progress, token);
  }

  /// <summary>
  /// Декодирует поток LZMA2 напрямую в <see cref="Stream"/>, не накапливая весь выход в памяти.
  /// </summary>
  public static Lzma2DecodeResult DecodeToStream(
      ReadOnlySpan<byte> input,
      Lzma2Properties properties,
      Stream output,
      out long bytesWritten,
      out int bytesConsumed,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(output);

    if (!properties.TryGetDictionarySizeInt32(out int dictionarySize))
    {
      bytesWritten = 0;
      bytesConsumed = 0;
      return Lzma2DecodeResult.NotSupported;
    }

    var decoder = new Lzma2IncrementalDecoder(progress: progress, dictionarySize: dictionarySize);
    return DecodeToStream(decoder, input, output, out bytesWritten, out bytesConsumed, token);
  }

  private static Lzma2DecodeResult DecodeToStream(
      Lzma2IncrementalDecoder decoder,
      ReadOnlySpan<byte> input,
      Stream output,
      out long bytesWritten,
      out int bytesConsumed,
      System.Threading.CancellationToken token = default)
  {
    int inputOffset = 0;
    long written = 0;
    byte[] buffer = ArrayPool<byte>.Shared.Rent(_defaultOutputChunkSize);

    try
    {
      while (true)
      {
        // Кооперативная отмена на границе выходного чанка (~64 КБ), как в DecodeToArray.
        token.ThrowIfCancellationRequested();

        Lzma2DecodeResult result = decoder.Decode(
            input.Slice(inputOffset),
            buffer,
            out int consumed,
            out int writtenNow);

        inputOffset += consumed;

        if (writtenNow > 0)
        {
          output.Write(buffer, 0, writtenNow);
          written += writtenNow;
        }

        if (result == Lzma2DecodeResult.NeedMoreOutput)
        {
          if (consumed == 0 && writtenNow == 0)
            throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

          continue;
        }

        bytesConsumed = inputOffset;
        bytesWritten = written;
        return result;
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  private static Lzma2DecodeResult DecodeToArray(
      Lzma2IncrementalDecoder decoder,
      ReadOnlySpan<byte> input,
      out byte[] output,
      out int bytesConsumed,
      System.Threading.CancellationToken token = default)
  {
    int inputOffset = 0;
    var writer = new ArrayBufferWriter<byte>();

    while (true)
    {
      // Кооперативная отмена на границе выходного чанка (~64 КБ) — так отменяется
      // и распаковка одного большого файла посреди, а не только между folder-ами.
      token.ThrowIfCancellationRequested();

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
