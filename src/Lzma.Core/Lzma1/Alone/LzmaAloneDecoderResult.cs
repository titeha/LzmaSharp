namespace Lzma.Core.Lzma1;

/// <summary>
/// Результат декодирования потока в формате LZMA-Alone (.lzma).
/// </summary>
public enum LzmaAloneDecodeResult
{
  /// <summary>
  /// Декодирование завершено: получен весь ожидаемый распакованный вывод.
  /// </summary>
  Finished,

  /// <summary>
  /// Нужны дополнительные входные данные.
  /// </summary>
  NeedMoreInput,

  /// <summary>
  /// Нужен больший/новый выходной буфер (выход закончился раньше, чем декодирование).
  /// </summary>
  NeedMoreOutput,

  /// <summary>
  /// Некорректные/повреждённые данные.
  /// </summary>
  InvalidData,

  /// <summary>
  /// Данные/режим пока не поддерживаются (например, неизвестный размер распаковки в заголовке).
  /// </summary>
  NotSupported,
}
