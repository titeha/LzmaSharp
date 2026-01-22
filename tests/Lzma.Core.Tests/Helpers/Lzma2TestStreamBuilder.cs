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

  public static byte[] SingleLzmaChunkWithNewPropsNoResetDictionaryThenEnd(
    LzmaProperties props,
    byte[] lzmaPayload,
    int unpackSize)
  {
    // control 0xC0..0xDF: reset state + new props, без reset dictionary
    byte propByte = props.ToByteOrThrow();

    ArgumentNullException.ThrowIfNull(lzmaPayload);

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

    // reset state + new props, без reset dictionary
    // high5bits(unpackMinus1) кладём в low5bits control.
    byte control = (byte)(0xC0 | ((unpackMinus1 >> 16) & 0x1F));

    // Заголовок LZMA-чанка: 1 (control) + 2 (unpack) + 2 (pack) + 1 (props)
    var data = new byte[6 + lzmaPayload.Length + 1];

    int i = 0;

    byte u1 = (byte)(unpackMinus1 >> 8);
    byte u0 = (byte)(unpackMinus1 >> 0);
    byte p1 = (byte)(packMinus1 >> 8);
    byte p0 = (byte)(packMinus1 >> 0);

    data[i++] = control;
    data[i++] = u1;
    data[i++] = u0;
    data[i++] = p1;
    data[i++] = p0;
    data[i++] = propByte;

    Buffer.BlockCopy(lzmaPayload, 0, data, i, lzmaPayload.Length);
    i += lzmaPayload.Length;

    // End marker
    data[i++] = 0x00;

    return data;
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

  public static byte[] TwoLzmaChunks_SecondNoPropsNoResetStateThenEnd(
    LzmaProperties props,
    byte[] payload1,
    uint unpackSize1,
    byte[] payload2,
    uint unpackSize2)
  {
    // 1) Первый LZMA-чанк с properties (reset state + reset dic).
    // 2) Второй LZMA-чанк без properties и без resetState (control 0x80..0x9F).
    //    Он использует те же properties, словарь и вероятностные модели, что и предыдущий чанк.
    byte propByte = props.ToByteOrThrow();
    byte[] first = SingleLzmaChunkWithProps(payload1, checked((int)unpackSize1), propByte, endMarker: false);
    byte[] second = SingleLzmaChunkNoPropsNoResetState(payload2, unpackSize2, endMarker: true);

    byte[] data = new byte[first.Length + second.Length];
    first.CopyTo(data, 0);
    second.CopyTo(data, first.Length);
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


  private static byte[] SingleLzmaChunkNoPropsNoResetState(
    byte[] payload,
    uint unpackSize,
    bool endMarker)
  {
    // control 0x80..0x9F: LZMA, без properties, без resetState, без resetDictionary.
    byte control = (byte)(0x80 | (((unpackSize - 1) >> 16) & 0x1F));

    ushort unpackSizeMinus1Lo = (ushort)((unpackSize - 1) & 0xFFFF);
    ushort packSizeMinus1 = (ushort)(payload.Length - 1);

    // Заголовок LZMA2 LZMA-чанка без properties: 1 (control) + 2 (unpackSize-1) + 2 (packSize-1) = 5 байт.
    const int headerSize = 5;
    int endSize = endMarker ? 1 : 0;

    byte[] data = new byte[headerSize + payload.Length + endSize];

    data[0] = control;
    data[1] = (byte)(unpackSizeMinus1Lo >> 8);
    data[2] = (byte)(unpackSizeMinus1Lo & 0xFF);
    data[3] = (byte)(packSizeMinus1 >> 8);
    data[4] = (byte)(packSizeMinus1 & 0xFF);

    payload.CopyTo(data, headerSize);

    if (endMarker)
      data[^1] = 0x00;

    return data;
  }
}
