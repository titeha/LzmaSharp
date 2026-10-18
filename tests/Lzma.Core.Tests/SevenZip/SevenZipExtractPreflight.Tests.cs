using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты pre-flight проверок (свободное место и длина пути) для 7z-маршрута SEC-001 (шаг B5b).
/// <para>
/// Честная граница: тесты пространства проверяют контракт internal helper-а суммы
/// (<see cref="SevenZipArchiveDecoder.ComputeRequiredSpace"/>): насыщение,
/// фильтр каталогов, пропуск ≤ 0. End-to-end-триггер <c>InsufficientSpace</c> для 7z
/// отсутствует (нет шва инъекции размеров в публичный API, а заполнение диска запрещено);
/// аналогично «честной границе» ZIP-тестов (<c>ZipExtractPreFlightSpaceTests</c>),
/// проверка «строго до первой мутации» обеспечена местом вставки в <c>ExtractHeaderToDirectory</c>
/// и подтверждена ревью.
/// </para>
/// </summary>
public sealed class SevenZipExtractPreflightTests
{
  /// <summary>Создаёт уникальную временную папку (не создавая её на диске).</summary>
  private static string NewTempDir()
    => Path.Combine(Path.GetTempPath(), "lzs-7z-preflight-" + Guid.NewGuid().ToString("N"));

  private static byte[] Payload(string text) => Encoding.UTF8.GetBytes(text);

  private static byte[] BuildLzma2(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] bytes));
    return bytes;
  }

  // ────────────────────────────────────────────────────────────────────────────
  // (a) PathTooLong end-to-end: 7z-архив с членом длиной 300 символов.
  // ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// 7z-архив с элементом, имя которого 300 символов (300 + 21 суффикс стадии &gt; 255 лимит
  /// компонента имени NTFS/POSIX). Извлечение в несуществующий dest возвращает
  /// <see cref="SevenZipArchiveDecodeResult.PathTooLong"/>, данных на диске нет.
  /// <para>
  /// Отличие от ZIP-маршрута: в 7z root-каталог создаётся ДО pre-flight проверки
  /// (строка ~932 в <c>ExtractHeaderToDirectory</c>), поэтому после отката каталог
  /// отсутствует — journal.Rollback удаляет отслеженные каталоги.
  /// </para>
  /// </summary>
  [Fact]
  public void ExtractToDirectory_СлишкомДлинноеИмяЭлемента_ВозвращаетPathTooLong_ИНеПишетДанные()
  {
    string longName = new string('x', 300) + ".txt";
    byte[] archive = BuildLzma2((longName, Payload("data")));

    string dest = NewTempDir();
    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: false,
          bytesConsumed: out _);

      Assert.Equal(SevenZipArchiveDecodeResult.PathTooLong, result);
      Assert.False(Directory.Exists(dest),
          "каталог назначения должен быть удалён откатом (root создаётся до pre-flight, " +
          "но journal.Rollback отслеживает созданные каталоги)");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Частичное извлечение: член со слишком длинным именем НЕ выбран через <c>shouldExtract</c> —
  /// операция не должна отклоняться из-за него (невыбранные записи не стадируются и их каталоги
  /// не создаются, поэтому pre-flight длины их не проверяет). Выбранный короткий член извлекается.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_Частичное_ДлинноеИмяНеВыбрано_ОперацияНеОтклоняется()
  {
    string longName = new string('y', 300) + ".txt";
    byte[] archive = BuildLzma2((longName, Payload("skip")), ("ok.txt", Payload("kept")));

    string dest = NewTempDir();
    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: false,
          bytesConsumed: out _,
          shouldExtract: name => name == "ok.txt");

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal("kept", File.ReadAllText(Path.Combine(dest, "ok.txt")));
      Assert.False(File.Exists(Path.Combine(dest, longName)),
          "невыбранный член не должен быть извлечён");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Порог суффикса стадии (MAJOR-1 ревью B5b): компонент имени 240 символов допустим сам по
  /// себе (240 ≤ 255), но вместе с суффиксом стадии 21 символ даёт 261 &gt; 255. Тест изолирует
  /// именно механизм суффикса: если в проверке заменить <c>extra</c> на 0, извлечение прошло бы
  /// с <c>Ok</c> и тест упал бы.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_КомпонентИмени240_ССуффиксомСтадии_ВозвращаетPathTooLong()
  {
    string name240 = new string('x', 236) + ".txt";
    Assert.Equal(240, name240.Length);
    byte[] archive = BuildLzma2((name240, Payload("data")));

    string dest = NewTempDir();
    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: false,
          bytesConsumed: out _);

      Assert.Equal(SevenZipArchiveDecodeResult.PathTooLong, result);
      Assert.False(Directory.Exists(dest),
          "каталог назначения должен быть удалён откатом");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  // ────────────────────────────────────────────────────────────────────────────
  // (b) Guard enum: значения в конце, прежние не сдвинулись.
  // ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Защита правила «только в конец enum»: существующие значения не изменились,
  /// новые <see cref="SevenZipArchiveDecodeResult.InsufficientSpace"/> == 5
  /// и <see cref="SevenZipArchiveDecodeResult.PathTooLong"/> == 6.
  /// </summary>
  [Fact]
  public void SevenZipArchiveDecodeResult_НовыеЗначения_АддитивныИВКонце()
  {
    // Неявные значения: порядок определения задаёт числовые значения.
    Assert.Equal(0, (int)SevenZipArchiveDecodeResult.Ok);
    Assert.Equal(1, (int)SevenZipArchiveDecodeResult.NeedMoreData);
    Assert.Equal(2, (int)SevenZipArchiveDecodeResult.InvalidData);
    Assert.Equal(3, (int)SevenZipArchiveDecodeResult.NotSupported);
    Assert.Equal(4, (int)SevenZipArchiveDecodeResult.InternalError);

    // Новые — строго в конце.
    Assert.Equal(5, (int)SevenZipArchiveDecodeResult.InsufficientSpace);
    Assert.Equal(6, (int)SevenZipArchiveDecodeResult.PathTooLong);
  }

  // ────────────────────────────────────────────────────────────────────────────
  // (c) Пространство: unit-тесты internal helper-а суммы.
  // End-to-end-триггер InsufficientSpace для 7z отсутствует (нет шва инъекции размеров).
  // ────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Контракт <see cref="SevenZipArchiveDecoder.ComputeRequiredSpace"/>:
  /// все элементы — DataFile, все выбраны, размеры положительные.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_ВсеВыбраны_СуммаРазмеров()
  {
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 100, false, 0),
      new("b.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 200, false, 0),
    };
    bool[] selected = [true, true];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(300, required);
  }

  /// <summary>
  /// Каталог (Directory) не участвует в сумме, даже если Size &gt; 0.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_КаталогиПропускаются()
  {
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("dir", SevenZipArchiveDecoder.ExtractEntryKind.Directory, 0, 0, false, 0),
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 100, false, 0),
    };
    bool[] selected = [true, true];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(100, required);
  }

  /// <summary>
  /// Невыбранные (shouldExtract = false) не участвуют в сумме.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_НевыбранныеПропускаются()
  {
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 100, false, 0),
      new("b.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 200, false, 0),
    };
    bool[] selected = [true, false];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(100, required);
  }

  /// <summary>
  /// Size ≤ 0 (недоверенный заголовок: отрицательные/нулевые) пропускается.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_РазмерМеньшеРавенНулю_Пропускается()
  {
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, -1, false, 0),
      new("b.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 0, false, 0),
      new("c.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 50, false, 0),
    };
    bool[] selected = [true, true, true];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(50, required);
  }

  /// <summary>
  /// Насыщение суммы: два элемента по <c>long.MaxValue / 2 + 1</c> дают сумму больше
  /// <see cref="long.MaxValue"/>. Без насыщения сумма переполнилась бы в отрицательную,
  /// и проверка места молча перестала бы срабатывать.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_ПереполнениеСуммы_НасыщаетсяДоMaxValue()
  {
    long half = long.MaxValue / 2 + 1;
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, half, false, 0),
      new("b.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, half, false, 0),
    };
    bool[] selected = [true, true];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(long.MaxValue, required);
  }

  /// <summary>
  /// Пустой план → 0 (нечего извлекать, место не требуется).
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_ПустойПлан_Ноль()
  {
    long required = SevenZipArchiveDecoder.ComputeRequiredSpace([], []);
    Assert.Equal(0, required);
  }

  /// <summary>
  /// EmptyFile (Kind == EmptyFile, Size == 0) не участвует: размер 0 пропускается.
  /// </summary>
  [Fact]
  public void ComputeRequiredSpace_EmptyFile_Пропускается()
  {
    var plan = new SevenZipArchiveDecoder.ExtractPlanEntry[]
    {
      new("empty.txt", SevenZipArchiveDecoder.ExtractEntryKind.EmptyFile, 0, 0, false, 0),
      new("a.bin", SevenZipArchiveDecoder.ExtractEntryKind.DataFile, 0, 42, false, 0),
    };
    bool[] selected = [true, true];

    long required = SevenZipArchiveDecoder.ComputeRequiredSpace(plan, selected);
    Assert.Equal(42, required);
  }
}
