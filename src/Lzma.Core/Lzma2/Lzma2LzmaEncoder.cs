using Lzma.Core.Lzma1;

namespace Lzma.Core.Lzma2;

/// <summary>
/// Минимальный LZMA2-энкодер, который пишет один сжатый (LZMA) чанк.
/// </summary>
/// <remarks>
/// <para>
/// На этом шаге мы делаем самый простой вариант:
/// - один LZMA-чанк;
/// - resetDictionary = true;
/// - resetState = true;
/// - includeProps = true;
/// - payload получаем через <see cref="LzmaEncoder"/>, кодируя вход как последовательность литералов.
/// </para>
/// <para>
/// Это не «настоящий» компрессор (пока не ищем совпадения), но поток валидный и успешно
/// распаковывается нашим <see cref="Lzma2IncrementalDecoder"/>.
/// </para>
/// </remarks>
public static class Lzma2LzmaEncoder
{
  // Ограничения LZMA2-формата для LZMA-чанка:
  // unpackSizeMinus1 хранится в 21 бите (5 бит в control + 16 бит дальше).
  private const int _maxChunkUnpackSize = 1 << 21; // 2_097_152

  // packSizeMinus1 хранится в 16 битах.
  private const int _maxChunkPackSize = 1 << 16; // 65_536

  /// <summary>
  /// Кодирует вход как один LZMA-чанк внутри LZMA2-потока.
  /// </summary>
  /// <param name="input">Несжатые данные.</param>
  /// <param name="lzmaProps">Параметры LZMA (lc/lp/pb), которые будут записаны в заголовок чанка.</param>
  /// <param name="dictionarySize">Размер словаря LZMA/LZMA2.</param>
  /// <param name="lzma2PropertiesByte">Выходной properties byte для LZMA2 (кодирует размер словаря).</param>
  public static byte[] EncodeLiteralOnly(
    ReadOnlySpan<byte> input,
    LzmaProperties lzmaProps,
    int dictionarySize,
    out byte lzma2PropertiesByte)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    if (!Lzma2Properties.TryEncode(dictionarySize, out lzma2PropertiesByte))
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Некорректный размер словаря для LZMA2.");

    // Пустой вход: просто END (0x00). Такой поток распакуется в пустоту.
    if (input.Length == 0)
      return [0x00];

    if (input.Length > _maxChunkUnpackSize)
      throw new ArgumentOutOfRangeException(nameof(input), $"На данном шаге поддерживаем только один чанк: unpackSize <= {_maxChunkUnpackSize}.");

    if (!lzmaProps.TryToByte(out byte lzmaPropsByte))
      throw new ArgumentException("Некорректные параметры LZMA (lc/lp/pb).", nameof(lzmaProps));

    // 1) Строим «скрипт» из одних литералов.
    var script = new LzmaEncodeOp[input.Length];
    for (int i = 0; i < input.Length; i++)
      script[i] = LzmaEncodeOp.Lit(input[i]);

    // 2) Кодируем LZMA-полезную нагрузку.
    var lzma = new LzmaEncoder(lzmaProps, dictionarySize);
    byte[] lzmaPayload = lzma.EncodeScript(script);

    int unpackSize = input.Length;
    int packSize = lzmaPayload.Length;

    if (packSize <= 0)
      throw new InvalidOperationException("Внутренняя ошибка: LZMA payload оказался пустым.");

    if (packSize > _maxChunkPackSize)
      throw new ArgumentOutOfRangeException(nameof(input), $"На данном шаге поддерживаем только один чанк: packSize <= {_maxChunkPackSize}.");

    // 3) Формируем LZMA2 поток: [LZMA chunk] + [END].
    int unpackSizeMinus1 = unpackSize - 1;
    int packSizeMinus1 = packSize - 1;

    // control (LZMA chunk + reset dic + reset state + has props)
    // 0x80 (LZMA) | 0x40 (dic reset) | 0x20 (state reset) | 0x10 (props) = 0xF0,
    // но верхние 5 бит unpackSizeMinus1 кладём в младшие 5 бит control (как в спецификации).
    byte control = (byte)(0xE0 | ((unpackSizeMinus1 >> 16) & 0x1F));

    var output = new List<byte>(
      capacity: 1 /*control*/ +
               2 /*unpackSize*/ +
               2 /*packSize*/ +
               1 /*lzmaProps*/ +
               packSize +
               1 /*end*/)
    {
      control,

      // unpackSizeMinus1: низшие 16 бит.
      (byte)((unpackSizeMinus1 >> 8) & 0xFF),
      (byte)(unpackSizeMinus1 & 0xFF),

      // packSizeMinus1: 16 бит.
      (byte)((packSizeMinus1 >> 8) & 0xFF),
      (byte)(packSizeMinus1 & 0xFF),

      // LZMA properties byte (lc/lp/pb)
      lzmaPropsByte
    };

    // Payload
    output.AddRange(lzmaPayload);

    // END marker
    output.Add(0x00);

    return [.. output];
  }
}
