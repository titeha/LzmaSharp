using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// Небольшая «строилка» тестовых LZMA2-потоков.
/// </summary>
/// <remarks>
/// В реальной жизни LZMA2 обычно живёт внутри контейнера (например, .xz).
/// Мы же в тестах кормим декодеру «сырое» тело LZMA2.
/// </remarks>
internal static class Lzma2TestStreamBuilder
{
  /// <summary>
  /// Собирает минимальный LZMA2-поток:
  /// <list type="bullet">
  /// <item><description>один LZMA-чанк (control 0xE0..0xFF) + properties byte + payload</description></item>
  /// <item><description>опционально — end marker (0x00)</description></item>
  /// </list>
  /// </summary>
  /// <remarks>
  /// Это «ручная сборка» потока для тестов. Мы намеренно не используем внешний энкодер:
  /// так тесты остаются полностью детерминированными и не зависят от стороннего кода.
  /// </remarks>
  public static byte[] SingleLzmaChunkWithProps(byte[] lzmaPayload, int unpackSize, byte propertiesByte, bool endMarker = true)
  {
    ArgumentNullException.ThrowIfNull(lzmaPayload);

    if (unpackSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(unpackSize), "unpackSize должен быть > 0.");

    if (unpackSize > (1 << 21))
      throw new ArgumentOutOfRangeException(nameof(unpackSize), "Для одного LZMA2-чанка unpackSize не должен превышать 2 MiB.");

    if (lzmaPayload.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload), "lzmaPayload не должен быть пустым.");

    if (lzmaPayload.Length > 0x10000)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload), "packSize не должен превышать 65536 байт.");

    // Лёгкая валидация propertiesByte — чтобы тесты не создавали заведомо некорректные потоки.
    if (!LzmaProperties.TryParse(propertiesByte, out _))
      throw new ArgumentOutOfRangeException(nameof(propertiesByte), "propertiesByte не является валидным LZMA properties.");

    int unpackMinus1 = unpackSize - 1;
    int packMinus1 = lzmaPayload.Length - 1;

    // control: 111xxxxx
    // - 0xE0..0xFF => reset dic + reset state + props
    // - младшие 5 бит — верхние 5 бит (unpackSize-1).
    byte control = (byte)(0xE0 | ((unpackMinus1 >> 16) & 0x1F));

    byte u1 = (byte)((unpackMinus1 >> 8) & 0xFF);
    byte u0 = (byte)(unpackMinus1 & 0xFF);

    byte p1 = (byte)((packMinus1 >> 8) & 0xFF);
    byte p0 = (byte)(packMinus1 & 0xFF);

    int tail = endMarker ? 1 : 0;

    // Заголовок LZMA-чанка: 1 (control) + 2 (unpack) + 2 (pack) + 1 (props)
    var data = new byte[6 + lzmaPayload.Length + tail];

    int i = 0;
    data[i++] = control;
    data[i++] = u1;
    data[i++] = u0;
    data[i++] = p1;
    data[i++] = p0;
    data[i++] = propertiesByte;

    Buffer.BlockCopy(lzmaPayload, 0, data, i, lzmaPayload.Length);
    i += lzmaPayload.Length;

    if (endMarker)
      data[i++] = 0x00;

    return data;
  }

  /// <summary>
  /// Собирает поток: один LZMA-чанк (с props + reset dic/state) + end marker.
  /// </summary>
  public static byte[] SingleLzmaChunkThenEnd(LzmaProperties props, byte[] lzmaPayload, int unpackSize)
  {
    // Просто удобная обёртка над «сырой» сборкой по props-byte.
    byte propByte = props.ToByteOrThrow();
    return SingleLzmaChunkWithProps(lzmaPayload, unpackSize, propByte, endMarker: true);
  }

  /// <summary>
  /// Собирает поток:
  /// <list type="bullet">
  /// <item><description>LZMA-чанк с props (control 0xE0..0xFF, reset dic/state) + payload</description></item>
  /// <item><description>LZMA-чанк без props, но с reset state (control 0xA0..0xBF) + payload</description></item>
  /// <item><description>end marker (0x00)</description></item>
  /// </list>
  /// </summary>
  public static byte[] TwoLzmaChunks_SecondNoPropsResetStateThenEnd(
    LzmaProperties props,
    byte[] firstLzmaPayload,
    int firstUnpackSize,
    byte[] secondLzmaPayload,
    int secondUnpackSize)
  {
    byte propByte = props.ToByteOrThrow();

    // Первый чанк: с props.
    var first = SingleLzmaChunkWithProps(firstLzmaPayload, firstUnpackSize, propByte, endMarker: false);

    // Второй чанк: без props, но с reset state.
    var second = SingleLzmaChunkNoPropsResetState(secondLzmaPayload, secondUnpackSize, endMarker: true);

    var data = new byte[first.Length + second.Length];
    Buffer.BlockCopy(first, 0, data, 0, first.Length);
    Buffer.BlockCopy(second, 0, data, first.Length, second.Length);

    return data;
  }

  private static byte[] SingleLzmaChunkNoPropsResetState(byte[] lzmaPayload, int unpackSize, bool endMarker)
  {
    if (lzmaPayload is null)
      throw new ArgumentNullException(nameof(lzmaPayload));

    if (unpackSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(unpackSize), "unpackSize должен быть > 0.");

    if (unpackSize > (1 << 21))
      throw new ArgumentOutOfRangeException(nameof(unpackSize), "Для одного LZMA2-чанка unpackSize не должен превышать 2 MiB.");

    if (lzmaPayload.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload), "lzmaPayload не должен быть пустым.");

    if (lzmaPayload.Length > 0x10000)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload), "packSize не должен превышать 65536 байт.");

    int unpackMinus1 = unpackSize - 1;
    int packMinus1 = lzmaPayload.Length - 1;

    // control: 101xxxxx
    // - 0xA0..0xBF => reset state, no props, no reset dic
    // - младшие 5 бит — верхние 5 бит (unpackSize-1).
    byte control = (byte)(0xA0 | ((unpackMinus1 >> 16) & 0x1F));

    byte u1 = (byte)((unpackMinus1 >> 8) & 0xFF);
    byte u0 = (byte)(unpackMinus1 & 0xFF);

    byte p1 = (byte)((packMinus1 >> 8) & 0xFF);
    byte p0 = (byte)(packMinus1 & 0xFF);

    int tail = endMarker ? 1 : 0;

    // Заголовок LZMA-чанка без props: 1 (control) + 2 (unpack) + 2 (pack)
    var data = new byte[5 + lzmaPayload.Length + tail];

    int i = 0;
    data[i++] = control;
    data[i++] = u1;
    data[i++] = u0;
    data[i++] = p1;
    data[i++] = p0;

    Buffer.BlockCopy(lzmaPayload, 0, data, i, lzmaPayload.Length);
    i += lzmaPayload.Length;

    if (endMarker)
      data[i++] = 0x00;

    return data;
  }
}
