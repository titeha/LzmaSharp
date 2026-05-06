namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат построения 7z-архива.
/// </summary>
public enum SevenZipArchiveWriteResult
{
  Ok,
  InvalidData,
  NotSupported,

  /// <summary>
  /// Внутренняя ошибка или неожиданное состояние writer-а.
  /// </summary>
  InternalError,
}
