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

    return method switch
    {
      SevenZipWriterCompressionMethod.Ppmd => BuildPpmdEntriesArchive(entries, out archive, progress, token),
      SevenZipWriterCompressionMethod.Bcj2 => BuildBcj2EntriesArchive(entries, out archive),
      _ => BuildLzma2EntriesArchive(entries, lzma2DictionarySize, out archive, progress, token),
    };
  }

  /// <summary>
  /// Выбирает кодек по доле «бинарных» (управляющих) байт в содержимом непустых файлов.
  /// </summary>
  private static SevenZipWriterCompressionMethod ChooseAutoMethod(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    // Если ВСЕ непустые файлы — x86-исполняемые (PE), выгоден BCJ2 (адреса ветвлений → абсолютные).
    // Консервативно: при любом не-исполняемом файле откатываемся к текстовой эвристике (BCJ2 здесь —
    // на весь архив, поэтому применяем только когда это чистый набор исполняемых). Пофайловый выбор
    // BCJ2 в смешанном наборе — задача потокового пути (шаг 2).
    bool anyNonEmpty = false;
    bool allExecutable = true;
    for (int i = 0; i < entries.Count && allExecutable; i++)
    {
      if (!IsNonEmptyFile(entries[i]))
        continue;

      anyNonEmpty = true;
      if (!LooksLikeX86Executable(entries[i].Content))
        allExecutable = false;
    }

    if (anyNonEmpty && allExecutable)
      return SevenZipWriterCompressionMethod.Bcj2;

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
  /// Похоже ли содержимое на x86/x64 PE-исполняемый файл (`.exe`/`.dll`): сигнатура <c>MZ</c>,
  /// корректный указатель на <c>PE\0\0</c> и machine = i386 (0x014C) или amd64 (0x8664). Для таких
  /// файлов выгоден фильтр BCJ2 (как в 7-Zip). ELF/Mach-O пока не детектируем.
  /// </summary>
  private static bool LooksLikeX86Executable(byte[] content)
  {
    if (content.Length < 0x40)
      return false;

    if (content[0] != (byte)'M' || content[1] != (byte)'Z')
      return false;

    long peOffset = content[0x3C] | ((long)content[0x3D] << 8) | ((long)content[0x3E] << 16) | ((long)content[0x3F] << 24);
    if (peOffset < 0 || peOffset + 6 > content.Length)
      return false;

    int p = (int)peOffset;
    if (content[p] != (byte)'P' || content[p + 1] != (byte)'E' || content[p + 2] != 0 || content[p + 3] != 0)
      return false;

    ushort machine = (ushort)(content[p + 4] | (content[p + 5] << 8));
    return machine == 0x014C || machine == 0x8664;
  }

  /// <summary>
  /// «Бинарный» байт: управляющий (&lt; 0x20), кроме табуляции/перевода строки/возврата каретки.
  /// </summary>
  private static bool IsBinaryByte(byte b) => b < 0x20 && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r');
}
