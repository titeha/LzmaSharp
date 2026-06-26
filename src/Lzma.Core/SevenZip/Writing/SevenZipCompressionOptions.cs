namespace Lzma.Core.SevenZip;

/// <summary>
/// Параметры сжатия при записи 7z-архива: метод и его настройки. Расширяемо — сюда будут
/// добавляться параметры по мере надобности (уровень, размер словаря и т.п.).
/// </summary>
public sealed record SevenZipCompressionOptions
{
  /// <summary>Размер словаря LZMA2 по умолчанию (64 KiB).</summary>
  public const int DefaultLzma2DictionarySize = 1 << 16;

  /// <summary>Метод сжатия непустых файлов.</summary>
  public SevenZipWriterCompressionMethod Method { get; init; } = SevenZipWriterCompressionMethod.Lzma2;

  /// <summary>
  /// Размер словаря LZMA2 в байтах (для методов <see cref="SevenZipWriterCompressionMethod.Lzma2"/>
  /// и <see cref="SevenZipWriterCompressionMethod.Auto"/>, когда тот выбирает LZMA2). Значение
  /// округляется вверх до ближайшего канонического размера LZMA2; для прочих методов игнорируется.
  /// </summary>
  public int Lzma2DictionarySize { get; init; } = DefaultLzma2DictionarySize;

  /// <summary>Создаёт опции с заданным методом и прочими настройками по умолчанию.</summary>
  public static SevenZipCompressionOptions ForMethod(SevenZipWriterCompressionMethod method)
      => new() { Method = method };
}
