namespace Lzma.Core.Lzma2;

/// <summary>
/// Результат шага кодирования LZMA2.
/// </summary>
public enum Lzma2EncodeResult
{
  /// <summary>
  /// Кодер продвинулся (потребил вход и/или записал выход).
  /// </summary>
  Ok,

  /// <summary>
  /// Для продолжения нужен дополнительный ввод.
  /// </summary>
  NeedMoreInput,

  /// <summary>
  /// Для продолжения нужен дополнительный буфер вывода.
  /// </summary>
  NeedMoreOutput,

  /// <summary>
  /// Кодер завершил поток (записал end-marker).
  /// </summary>
  Finished,
}
