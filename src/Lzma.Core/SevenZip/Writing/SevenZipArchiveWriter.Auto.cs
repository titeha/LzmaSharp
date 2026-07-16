namespace Lzma.Core.SevenZip;

// Автовыбор кодека (level 1 — дешёвая эвристика по содержимому). Точный выбор «сжать обоими
// и взять меньший» (try-both, в т.ч. параллельно) — отдельная задача этапа UI.
public static partial class SevenZipArchiveWriter
{
  // Порог доли «бинарных» байт: ниже него считаем данные текстовыми (→ PPMd).
  // Случайные/уже-сжатые данные дают ~11% управляющих байт (29 из 256 значений),
  // натуральный текст — почти 0%.
  private const double AutoBinaryByteThreshold = 0.05;

  // Порог энтропии Шеннона (бит/байт): выше него данные считаем практически несжимаемыми
  // (уже сжатые архивы/медиа, шифртекст, случайные) — их выгоднее ХРАНИТЬ (Copy), чем гонять
  // LZMA2 впустую (трата CPU + возможное небольшое раздувание). Текст ~4-5, exe ~6, jpeg/7z ~7.95.
  private const double AutoIncompressibleEntropyBitsPerByte = 7.7;

  // Для больших файлов эвристику считаем по префиксу такого размера (для энтропии/доли достаточно).
  private const int AutoSampleBytes = 1 << 20;

  /// <summary>
  /// Строит архив, выбрав кодек эвристикой по содержимому непустых файлов: преимущественно
  /// текстовые данные → PPMd (он плотнее на тексте), иначе → LZMA2.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildAutoEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int lzma2DictionarySize,
      out byte[] archive,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    SevenZipWriterCompressionMethod method = ChooseAutoMethod(entries);

    return method == SevenZipWriterCompressionMethod.Ppmd
        ? BuildPpmdEntriesArchive(entries, out archive, progress, token)
        : BuildLzma2EntriesArchive(entries, lzma2DictionarySize, out archive, progress, token);
  }

  /// <summary>
  /// Выбирает кодек по доле «бинарных» (управляющих) байт в содержимом непустых файлов.
  /// </summary>
  private static SevenZipWriterCompressionMethod ChooseAutoMethod(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    long total = 0;
    long binary = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      byte[] content = entry.Content;
      total += content.Length;

      for (int j = 0; j < content.Length; j++)
        if (IsBinaryByte(content[j]))
          binary++;
    }

    if (total == 0)
      return SevenZipWriterCompressionMethod.Lzma2;

    return binary < total * AutoBinaryByteThreshold
        ? SevenZipWriterCompressionMethod.Ppmd
        : SevenZipWriterCompressionMethod.Lzma2;
  }

  /// <summary>
  /// «Бинарный» байт: управляющий (&lt; 0x20), кроме табуляции/перевода строки/возврата каретки.
  /// </summary>
  private static bool IsBinaryByte(byte b) => b < 0x20 && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r');
}
