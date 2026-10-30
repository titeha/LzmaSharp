namespace Lzma.Core.SevenZip;

/// <summary>
/// Политика памяти для in-memory маршрута декодирования folder'а 7z (RES-002).
/// Предел выводится из длины переданного packed-слайса: сумма заявленных распакованных размеров
/// потоков одного folder'а не может превышать max(Floor, packedLength * Amplification), но и не
/// больше Cap. Floor нужен потому, что легитимные архивы бывают исключительно сжимаемыми
/// (в fixture проекта: 70 МиБ из ~450 байт, то есть усиление ~240 000×).
/// Cap ограничивает заявленный распакованный объём ОДНОГО folder'а, а не пик памяти операции:
/// размер словаря LZMA и memSize PPMd этой политикой не ограничиваются (отдельный шаг C5),
/// а результат in-memory декодирования архива накапливается по folder'ам.
/// В основном маршруте packedLength — длина packed-региона архива, поэтому ветвь усиления
/// действует только при packedLength &lt; Cap / Amplification (1 048 576 байт), а при большей
/// длине предел равен Cap. Значения по умолчанию; поднимаются публичными опциями (шаг C10).
/// </summary>
internal static class SevenZipMemoryPolicy
{
  /// <summary>Нижняя планка предела: 256 МиБ (прецедент SolidWaveMemoryBudget в writer'е).</summary>
  internal const long FloorBytes = 256L << 20;

  /// <summary>Верхняя планка предела: 1 ГиБ на распакованный объём folder'а.</summary>
  internal const long CapBytes = 1L << 30;

  /// <summary>Допустимое усиление относительно длины packed-данных.</summary>
  internal const long Amplification = 1024L;

  /// <summary>Предел in-memory распаковки для packed-данных указанной длины (в байтах).</summary>
  internal static long GetMaxUnpackBytes(long packedLength)
  {
    if (packedLength < 0)
      packedLength = 0;

    // Насыщение вместо переполнения: при packedLength >= Cap/Amplification предел равен Cap.
    long byAmplification = packedLength >= CapBytes / Amplification
      ? CapBytes
      : packedLength * Amplification;

    long limit = byAmplification > FloorBytes ? byAmplification : FloorBytes;
    return limit > CapBytes ? CapBytes : limit;
  }
}
