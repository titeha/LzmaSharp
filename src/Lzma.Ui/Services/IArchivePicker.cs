namespace Lzma.Ui.Services;

/// <summary>Выбранный пользователем архив: имя и содержимое.</summary>
public sealed record PickedArchive(string Name, byte[] Bytes);

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
}
