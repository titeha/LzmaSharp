using Lzma.Core.Lzma1;

namespace Lzma.Core.Lzma2;

/// <summary>
/// Вспомогательный энкодер для тестов/прототипов: кодирует данные в LZMA2 как один LZMA-чанк (с properties).
/// </summary>
/// <remarks>
/// <para>На этом шаге энкодер делает один LZMA-чанк вида control >= 0xE0 (reset dic + reset state + props) + end-marker.</para>
/// <para>Payload внутри чанка — это "сырой" LZMA-поток, который выдаёт <see cref="LzmaEncoder"/>.</para>
/// <para>Это не полноценный production-энкодер LZMA2, а удобный генератор валидных потоков для тестов.</para>
/// </remarks>
public static class Lzma2LzmaEncoder
{
  /// <summary>
  /// Кодирует <paramref name="data"/> в LZMA2 как один LZMA-чанк (reset dic/state + props) + end-marker.
  /// Внутри чанка кодирование LZMA выполняется в режиме "только литералы".
  /// </summary>
  public static byte[] EncodeLiteralOnly(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    out byte lzmaPropertiesByte)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    // "Сырой" LZMA-поток.
    var enc = new LzmaEncoder(lzmaProperties, dictionarySize);
    byte[] payload = enc.EncodeLiteralOnly(data);

    return WrapSingleLzmaChunkWithProps(payload, unpackSize: data.Length, lzmaPropertiesByte);
  }

  /// <summary>
  /// Кодирует <paramref name="data"/> в LZMA2 как несколько LZMA-чанков (LZMA compressed chunks)
  /// и дописывает end-marker (0x00).
  /// </summary>
  /// <remarks>
  /// На этом шаге мы делаем простой (но валидный) вариант:
  /// <list type="bullet">
  /// <item><description>первый чанк: reset dictionary + reset state + properties;</description></item>
  /// <item><description>последующие чанки: reset state, но <b>без</b> properties (props берутся из первого чанка).</description></item>
  /// </list>
  /// Это уменьшает накладные расходы (не повторяем 1 байт properties для каждого чанка).
  /// </remarks>
  public static byte[] EncodeLiteralOnlyChunked(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProps,
    int dictionarySize,
    int maxUnpackChunkSize,
    out byte lzmaPropertiesByte)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    // Properties byte мы пишем только в первом LZMA-чанке.
    // Для последующих чанков (с reset state) декодер берёт props из предыдущего чанка.
    lzmaPropertiesByte = lzmaProps.ToByteOrThrow();

    using var ms = new MemoryStream();

    int offset = 0;
    for (int chunkIndex = 0; offset < data.Length; chunkIndex++)
    {
      int chunkSize = Math.Min(maxUnpackChunkSize, data.Length - offset);
      var chunk = data.Slice(offset, chunkSize);

      // Пакуем текущий кусок в «обычный» LZMA1 поток.
      // На этом шаге мы кодируем только литералы и каждый чанк делает reset state,
      // поэтому чанки независимы по вероятностным моделям/предыдущему байту.
      var lzma = new LzmaEncoder(lzmaProps, dictionarySize);
      byte[] lzmaPayload = lzma.EncodeLiteralOnly(chunk);

      uint unpackSizeMinus1 = (uint)chunkSize - 1;
      uint packSizeMinus1 = (uint)lzmaPayload.Length - 1;

      bool isFirst = chunkIndex == 0;

      // control:
      // - 0xE0..0xFF: reset dictionary + reset state + properties
      // - 0xA0..0xBF: reset state, properties НЕ пишем
      byte controlBase = isFirst ? (byte)0xE0 : (byte)0xA0;
      byte control = (byte)(controlBase | ((unpackSizeMinus1 >> 16) & 0x1F));

      if (isFirst)
      {
        Span<byte> header =
        [
          control,
          (byte)(unpackSizeMinus1 >> 8),
          (byte)(unpackSizeMinus1),
          (byte)(packSizeMinus1 >> 8),
          (byte)(packSizeMinus1),
          lzmaPropertiesByte,
        ];

        ms.Write(header);
      }
      else
      {
        Span<byte> header =
        [
          control,
          (byte)(unpackSizeMinus1 >> 8),
          (byte)(unpackSizeMinus1),
          (byte)(packSizeMinus1 >> 8),
          (byte)(packSizeMinus1),
        ];

        ms.Write(header);
      }

      ms.Write(lzmaPayload);

      offset += chunkSize;
    }

    // End marker.
    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  /// <summary>
  /// Кодирует скрипт (литералы + обычные match) в LZMA2 как один LZMA-чанк (reset dic/state + props) + end-marker.
  /// </summary>
  /// <remarks>
  /// Метод internal, потому что тип <see cref="LzmaEncodeOp"/> у нас internal.
  /// Тестовый проект имеет доступ через InternalsVisibleTo.
  /// </remarks>
  internal static byte[] EncodeScript(
    ReadOnlySpan<LzmaEncodeOp> script,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    out byte lzmaPropertiesByte)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    int unpackSize = EstimateUnpackSize(script);

    var enc = new LzmaEncoder(lzmaProperties, dictionarySize);
    byte[] payload = enc.EncodeScript(script);

    return WrapSingleLzmaChunkWithProps(payload, unpackSize, lzmaPropertiesByte);
  }

  private static int EstimateUnpackSize(ReadOnlySpan<LzmaEncodeOp> script)
  {
    long total = 0;
    for (int i = 0; i < script.Length; i++)
    {
      var op = script[i];
      total += op.Kind == LzmaEncodeOpKind.Literal ? 1 : op.Length;
      if (total > int.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(script), "Слишком большой ожидаемый распакованный размер для тестового энкодера.");
    }
    return (int)total;
  }

  private static byte[] WrapSingleLzmaChunkWithProps(byte[] lzmaPayload, int unpackSize, byte lzmaPropertiesByte)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(unpackSize);
    if (unpackSize == 0)
      return [0x00]; // пустой поток — просто end-marker

    if (lzmaPayload is null || lzmaPayload.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload), "LZMA payload не должен быть пустым при unpackSize > 0.");

    uint usm1 = (uint)unpackSize - 1;
    uint psm1 = (uint)lzmaPayload.Length - 1;

    // control = 0xE0..0xFF: reset dic + reset state + props, плюс 5 старших бит unpackSize-1.
    byte control = (byte)(0xE0 | ((usm1 >> 16) & 0x1F));

    byte b1 = (byte)((usm1 >> 8) & 0xFF);
    byte b2 = (byte)(usm1 & 0xFF);
    byte b3 = (byte)((psm1 >> 8) & 0xFF);
    byte b4 = (byte)(psm1 & 0xFF);

    // Заголовок LZMA-чанка с props: 6 байт.
    byte[] result = new byte[6 + lzmaPayload.Length + 1];
    int o = 0;
    result[o++] = control;
    result[o++] = b1;
    result[o++] = b2;
    result[o++] = b3;
    result[o++] = b4;
    result[o++] = lzmaPropertiesByte;

    Buffer.BlockCopy(lzmaPayload, 0, result, o, lzmaPayload.Length);
    o += lzmaPayload.Length;

    result[o] = 0x00; // end-marker
    return result;
  }
}
