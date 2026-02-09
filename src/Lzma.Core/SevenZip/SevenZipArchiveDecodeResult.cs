namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат декодирования 7z-архива.
/// </summary>
public enum SevenZipArchiveDecodeResult
{
  Ok,
  NeedMoreData,
  InvalidData,
  NotSupported,

  /// <summary>
  /// Внутренняя ошибка (неожиданное состояние).
  /// </summary>
  InternalError,
}
