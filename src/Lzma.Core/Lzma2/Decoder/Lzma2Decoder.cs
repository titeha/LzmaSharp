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

  /// <summary>
  /// Декодирует LZMA2 из <paramref name="packedInput"/> (ровно <paramref name="packLength"/> байт)
  /// напрямую в <paramref name="output"/>, читая вход ПО ЦЕЛЫМ ЧАНКАМ и не держа ни вход, ни выход
  /// в памяти целиком. Для потокового извлечения архивов больше 2 ГиБ.
  /// </summary>
  /// <remarks>
  /// Инкрементальный декодер не умеет возобновляться при частичной подаче packed-входа (внутри
  /// чанка), поэтому мы КАДРИРУЕМ поток: читаем заголовок чанка (<see cref="Lzma2ChunkHeader"/>),
  /// узнаём полный размер, дочитываем весь чанк и подаём его декодеру ЦЕЛИКОМ (возобновление —
  /// только по выходу, что декодер поддерживает).
  /// </remarks>
  public static Lzma2DecodeResult DecodeStreamToStream(
      Stream packedInput,
      long packLength,
      Lzma2Properties properties,
      Stream output,
      out long bytesWritten,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(packedInput);
    ArgumentNullException.ThrowIfNull(output);
    ArgumentOutOfRangeException.ThrowIfNegative(packLength);

    bytesWritten = 0;

    if (!properties.TryGetDictionarySizeInt32(out int dictionarySize))
      return Lzma2DecodeResult.NotSupported;

    var decoder = new Lzma2IncrementalDecoder(progress: progress, dictionarySize: dictionarySize);

    // Максимальный чанк LZMA2: заголовок(6) + payload(16-битный размер ≤ 65536) = 65542.
    byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(1 << 17);
    byte[] outBuffer = ArrayPool<byte>.Shared.Rent(_defaultOutputChunkSize);
    long remaining = packLength;
    long written = 0;

    try
    {
      while (true)
      {
        token.ThrowIfCancellationRequested();

        // 1) control-байт.
        if (!ReadExact(packedInput, chunkBuffer, 0, 1, ref remaining))
        {
          bytesWritten = written;
          return Lzma2DecodeResult.InvalidData; // нет end-marker / поток кончился
        }

        int headerSize = ChunkHeaderSizeFromControl(chunkBuffer[0]);
        if (headerSize < 0)
        {
          bytesWritten = written;
          return Lzma2DecodeResult.InvalidData;
        }

        // 2) остаток заголовка.
        if (headerSize > 1 && !ReadExact(packedInput, chunkBuffer, 1, headerSize - 1, ref remaining))
        {
          bytesWritten = written;
          return Lzma2DecodeResult.InvalidData;
        }

        if (Lzma2ChunkHeader.TryRead(chunkBuffer.AsSpan(0, headerSize), out Lzma2ChunkHeader header, out _)
            != Lzma2ReadHeaderResult.Ok)
        {
          bytesWritten = written;
          return Lzma2DecodeResult.InvalidData;
        }

        int total = header.TotalSize;

        // 3) payload (весь чанк должен поместиться в буфер).
        int payload = header.PayloadSize;
        if (payload > 0 && !ReadExact(packedInput, chunkBuffer, headerSize, payload, ref remaining))
        {
          bytesWritten = written;
          return Lzma2DecodeResult.InvalidData;
        }

        // 4) Подаём ЦЕЛЫЙ чанк декодеру (возобновление — только по выходу).
        int fed = 0;
        while (fed < total)
        {
          Lzma2DecodeResult result = decoder.Decode(
              chunkBuffer.AsSpan(fed, total - fed), outBuffer, out int consumed, out int producedNow);

          fed += consumed;

          if (producedNow > 0)
          {
            output.Write(outBuffer, 0, producedNow);
            written += producedNow;
          }

          if (result == Lzma2DecodeResult.Finished)
          {
            bytesWritten = written;
            return Lzma2DecodeResult.Finished;
          }

          if (result == Lzma2DecodeResult.NeedMoreOutput)
            continue;

          if (result == Lzma2DecodeResult.NeedMoreInput)
            break; // чанк потреблён целиком — читаем следующий

          bytesWritten = written;
          return result; // InvalidData / NotSupported
        }
      }
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(chunkBuffer);
      ArrayPool<byte>.Shared.Return(outBuffer);
    }
  }

  // Размер заголовка чанка LZMA2 по control-байту (без чтения остального): End=1, Copy=3, LZMA=5|6
  // (6 если есть properties, control >= 0xC0). -1 — некорректный control.
  private static int ChunkHeaderSizeFromControl(byte control)
  {
    if (control == 0x00)
      return 1;
    if (control is 0x01 or 0x02)
      return 3;
    if (control >= 0x80)
      return control >= 0xC0 ? 6 : 5;

    return -1;
  }

  // Читает ровно count байт в buffer[offset..], уменьшая remaining; false при обрыве/выходе за packLength.
  private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count, ref long remaining)
  {
    if (count > remaining)
      return false;

    int got = 0;
    while (got < count)
    {
      int n = stream.Read(buffer, offset + got, count - got);
      if (n <= 0)
        return false;
      got += n;
    }

    remaining -= count;
    return true;
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
