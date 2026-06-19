namespace Lzma.Core.Ppmd;

/// <summary>
/// Результат декодирования PPMd (вариант H / PPMd7, как в 7z).
/// </summary>
public enum Ppmd7DecodeResult
{
  /// <summary>Поток успешно декодирован.</summary>
  Ok = 0,

  /// <summary>Поток повреждён или не соответствует формату.</summary>
  InvalidData = 1,

  /// <summary>Параметры не поддержаны (например, недопустимый order/memSize).</summary>
  NotSupported = 2,
}

/// <summary>
/// <para>Управляемый декодер PPMd var.H (PPMd7) с 7z range coder, без unsafe.</para>
/// <para>
/// Тонкая обёртка над общей моделью <see cref="Ppmd7Model"/> (верный порт эталона
/// LZMA SDK Ppmd7.c / Ppmd7Dec.c, PPMd var.H Дмитрия Шкарина). Контекстная модель,
/// suballocator и range-декодер воспроизведены 1:1; указатели заменены на UInt32-смещения.
/// </para>
/// </summary>
public static class Ppmd7Decoder
{
  private const int MinOrder = 2;
  private const int MaxOrder = 64;
  private const uint MinMemSize = 1 << 11;
  private const uint MaxMemSize = 0xFFFFFFFF - 12 * 3;

  /// <summary>
  /// Декодирует PPMd7-поток в <paramref name="output"/> ровно на длину буфера.
  /// </summary>
  /// <param name="input">Сжатые данные (после properties).</param>
  /// <param name="order">PPMd order (2..64), из properties.</param>
  /// <param name="memSize">Размер памяти модели в байтах, из properties.</param>
  public static Ppmd7DecodeResult Decode(ReadOnlySpan<byte> input, int order, uint memSize, Span<byte> output)
  {
    if (order < MinOrder || order > MaxOrder)
      return Ppmd7DecodeResult.NotSupported;

    if (memSize < MinMemSize || memSize > MaxMemSize)
      return Ppmd7DecodeResult.NotSupported;

    var model = new Ppmd7Model(memSize, input);

    model.Init(order);

    if (!model.RangeDecInit())
      return Ppmd7DecodeResult.InvalidData;

    for (int i = 0; i < output.Length; i++)
    {
      int sym = model.DecodeSymbol();
      if (sym < 0)
        return Ppmd7DecodeResult.InvalidData;

      output[i] = (byte)sym;
    }

    return Ppmd7DecodeResult.Ok;
  }
}
