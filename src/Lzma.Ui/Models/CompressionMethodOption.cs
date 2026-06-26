using Lzma.Core.SevenZip;

namespace Lzma.Ui.Models;

/// <summary>
/// Пункт выбора метода сжатия для UI: сам метод и его человекочитаемое имя.
/// </summary>
public sealed record CompressionMethodOption(SevenZipWriterCompressionMethod Method, string DisplayName)
{
  /// <summary>Возвращает пункт с дружелюбным именем для заданного метода.</summary>
  public static CompressionMethodOption ForMethod(SevenZipWriterCompressionMethod method) => method switch
  {
    SevenZipWriterCompressionMethod.Lzma2 => new(method, "LZMA2 — универсальный"),
    SevenZipWriterCompressionMethod.Ppmd => new(method, "PPMd — плотнее на тексте"),
    SevenZipWriterCompressionMethod.Auto => new(method, "Авто — выбор по содержимому"),
    SevenZipWriterCompressionMethod.Copy => new(method, "Без сжатия (Copy)"),
    _ => new(method, method.ToString()),
  };
}
