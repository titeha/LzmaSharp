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
  /// Кодирует <paramref name="data"/> в LZMA2 как <b>несколько</b> независимых LZMA-чанков
  /// (каждый: reset dic/state + props), а затем дописывает end-marker (0x00).
  /// </summary>
  /// <remarks>
  /// <para>
  /// Это намеренно простой режим для тестов: каждый чанк кодируется отдельно и не использует словарь/состояние
  /// предыдущих чанков.
  /// </para>
  /// <para>
  /// Такой поток валиден, но хуже сжимает — зато позволяет малыми шагами проверять инфраструктуру LZMA2.
  /// </para>
  /// </remarks>
  public static byte[] EncodeLiteralOnlyChunked(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize,
    out byte lzmaPropertiesByte)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    if (maxUnpackChunkSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(maxUnpackChunkSize), "Размер чанка должен быть > 0.");

    // В LZMA2 (uncompressed_size - 1) хранится в 21 бите.
    const int Lzma2MaxUnpackSize = 1 << 21; // 2 MiB
    if (maxUnpackChunkSize > Lzma2MaxUnpackSize)
      throw new ArgumentOutOfRangeException(nameof(maxUnpackChunkSize), $"maxUnpackChunkSize не должен превышать {Lzma2MaxUnpackSize}.");

    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    if (data.Length == 0)
      return [0x00];

    using var ms = new MemoryStream();

    int offset = 0;
    while (offset < data.Length)
    {
      int chunkSize = Math.Min(maxUnpackChunkSize, data.Length - offset);
      ReadOnlySpan<byte> chunk = data.Slice(offset, chunkSize);

      // "Сырой" LZMA-поток для конкретного чанка.
      var enc = new LzmaEncoder(lzmaProperties, dictionarySize);
      byte[] lzmaPayload = enc.EncodeLiteralOnly(chunk);

      // Пишем заголовок LZMA-чанка с props.
      uint usm1 = (uint)chunkSize - 1;
      uint psm1 = (uint)lzmaPayload.Length - 1;

      // control = 0xE0..0xFF: reset dic + reset state + props, плюс 5 старших бит unpackSize-1.
      byte control = (byte)(0xE0 | ((usm1 >> 16) & 0x1F));

      byte b1 = (byte)((usm1 >> 8) & 0xFF);
      byte b2 = (byte)(usm1 & 0xFF);
      byte b3 = (byte)((psm1 >> 8) & 0xFF);
      byte b4 = (byte)(psm1 & 0xFF);

      Span<byte> header = [control, b1, b2, b3, b4, lzmaPropertiesByte];

      ms.Write(header);
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
