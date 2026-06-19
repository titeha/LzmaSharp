namespace Lzma.Core.Zip;

/// <summary>
/// Результат чтения ZIP-архива.
/// </summary>
public enum ZipReadResult
{
  /// <summary>Архив успешно прочитан.</summary>
  Ok = 0,

  /// <summary>Архив повреждён или не соответствует формату ZIP.</summary>
  InvalidData = 1,

  /// <summary>Сценарий распознан, но пока не поддержан (ZIP64, шифрование, неизвестный метод).</summary>
  NotSupported = 2,
}
