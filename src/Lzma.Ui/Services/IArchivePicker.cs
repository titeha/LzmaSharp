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
}
