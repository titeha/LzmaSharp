namespace Lzma.Core.Ppmd;

/// <summary>
/// Результат кодирования PPMd (вариант H / PPMd7, как в 7z).
/// </summary>
public enum Ppmd7EncodeResult
{
  /// <summary>Данные успешно закодированы.</summary>
  Ok = 0,

  /// <summary>Параметры не поддержаны (например, недопустимый order/memSize).</summary>
  NotSupported = 2,
}

/// <summary>
/// <para>Управляемый энкодер PPMd var.H (PPMd7) с 7z range coder, без unsafe.</para>
/// <para>
/// Тонкая обёртка над общей моделью <see cref="Ppmd7Model"/> (верный порт эталона
/// LZMA SDK Ppmd7.c / Ppmd7Enc.c, PPMd var.H Дмитрия Шкарина). Производит «сырой»
/// PPMd7-поток (после properties), который читается <see cref="Ppmd7Decoder"/> и
/// настоящим 7-Zip.
/// </para>
/// </summary>
public static class Ppmd7Encoder
{
  private const int MinOrder = 2;
  private const int MaxOrder = 64;
  private const uint MinMemSize = 1 << 11;
  private const uint MaxMemSize = 0xFFFFFFFF - 12 * 3;

  /// <summary>
  /// Кодирует <paramref name="data"/> в «сырой» PPMd7-поток.
  /// </summary>
  /// <param name="data">Исходные байты.</param>
  /// <param name="order">PPMd order (2..64).</param>
  /// <param name="memSize">Размер памяти модели в байтах.</param>
  /// <param name="output">Сжатый поток (после properties).</param>
  public static Ppmd7EncodeResult Encode(ReadOnlySpan<byte> data, int order, uint memSize, out byte[] output)
  {
    output = [];

    if (order < MinOrder || order > MaxOrder)
      return Ppmd7EncodeResult.NotSupported;

    if (memSize < MinMemSize || memSize > MaxMemSize)
      return Ppmd7EncodeResult.NotSupported;

    var model = new Ppmd7Model(memSize, ReadOnlySpan<byte>.Empty);
    model.Init(order);

    var buffer = new List<byte>(data.Length / 2 + 16);
    model.RangeEncInit(buffer);

    foreach (byte b in data)
      model.EncodeSymbol(b);

    model.RangeEncFlush();

    output = model.GetEncodedOutput();
    return Ppmd7EncodeResult.Ok;
  }
}
