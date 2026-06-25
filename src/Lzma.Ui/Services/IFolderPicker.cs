namespace Lzma.Ui.Services;

/// <summary>
/// Абстракция выбора папки назначения. Отделяет ViewModel от платформенного диалога,
/// чтобы логику извлечения можно было тестировать без UI.
/// </summary>
public interface IFolderPicker
{
  /// <summary>
  /// Просит выбрать папку назначения. Возвращает путь либо <see langword="null"/>, если отменено.
  /// </summary>
  Task<string?> PickFolderAsync();
}
