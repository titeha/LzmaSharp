namespace Lzma.Ui.Services;

/// <summary>Выбранный пользователем архив: имя и содержимое.</summary>
public sealed record PickedArchive(string Name, byte[] Bytes);

/// <summary>
/// Цель объединённого открытия: имя, размер и ЛИБО локальный путь (для авто-выбора in-memory/поток по
/// размеру), ЛИБО уже прочитанные в память байты (нелокальный источник без пути).
/// </summary>
public sealed record PickedOpenTarget(string Name, long Length, string? LocalPath, byte[]? Bytes);

/// <summary>
/// Абстракция выбора файла архива. Отделяет ViewModel от платформенного файлового диалога,
/// чтобы логику открытия можно было тестировать без UF/IO.
/// </summary>
public interface IArchivePicker
{
  /// <summary>
  /// Просит пользователя выбрать архив. Возвращает <see langword="null"/>, если выбор отменён.
  /// </summary>
  Task<PickedArchive?> PickAsync();

  /// <summary>
  /// Просит выбрать файл архива и возвращает его ПУТЬ (без чтения в память) — для потокового
  /// извлечения архивов больше 2 ГиБ. <see langword="null"/> — выбор отменён или пути нет.
  /// По умолчанию не поддерживается.
  /// </summary>
  Task<string?> PickArchivePathAsync() => Task.FromResult<string?>(null);

  /// <summary>
  /// Поддерживает ли пикер объединённый выбор для открытия (<see cref="PickForOpenAsync"/>). Если нет
  /// (например, фейки в тестах) — используется байтовый <see cref="PickAsync"/>.
  /// </summary>
  bool SupportsUnifiedOpen => false;

  /// <summary>
  /// Единый выбор для открытия: одна кнопка «Открыть…» решает по размеру, читать ли архив в память
  /// или открывать потоково. <see langword="null"/> — выбор отменён. По умолчанию не поддерживается.
  /// </summary>
  Task<PickedOpenTarget?> PickForOpenAsync() => Task.FromResult<PickedOpenTarget?>(null);
}
