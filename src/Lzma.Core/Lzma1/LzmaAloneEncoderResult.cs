namespace Lzma.Core.Lzma1;

/// <summary>
/// Результат шага инкрементального энкодинга формата LZMA-Alone.
/// </summary>
public enum LzmaAloneEncodeResult
{
  /// <summary>
  /// Прогресс есть, можно вызывать дальше.
  /// </summary>
  Ok = 0,

  /// <summary>
  /// Нужен следующий кусок входных данных.
  /// </summary>
  NeedMoreInput = 1,

  /// <summary>
  /// Нужен больший буфер для вывода.
  /// </summary>
  NeedMoreOutput = 2,

  /// <summary>
  /// Поток полностью закодирован и все данные выведены.
  /// </summary>
  Finished = 3,

  /// <summary>
  /// Некорректные данные/состояние (например, заявленный размер не совпадает с фактическим).
  /// </summary>
  InvalidData = 4,

  /// <summary>
  /// Фича не поддерживается текущей реализацией.
  /// </summary>
  NotSupported = 5,
}
