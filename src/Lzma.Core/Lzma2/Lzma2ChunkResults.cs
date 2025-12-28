namespace Lzma.Core.Lzma2;

/// <summary>
/// Результат чтения LZMA2-чанка (заголовок + полезная нагрузка) из входного буфера.
/// </summary>
public enum Lzma2ReadChunkResult
{
  /// <summary>Чанк успешно считан.</summary>
  Ok,

  /// <summary>
  /// Для чтения чанка не хватило данных (вход обрезан).
  /// Важный момент: байты не «потреблены», т.е. bytesConsumed == 0.
  /// </summary>
  NeedMoreInput,

  /// <summary>Входные данные повреждены/некорректны.</summary>
  InvalidData,
}
