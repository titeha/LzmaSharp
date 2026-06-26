using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>
/// Абстракция выбора пути для сохранения создаваемого архива. Отделяет ViewModel от
/// платформенного диалога «Сохранить как…», чтобы логику создания можно было тестировать без UI.
/// </summary>
public interface ISaveFilePicker
{
  /// <summary>
  /// Просит выбрать путь сохранения архива. Возвращает путь либо <see langword="null"/>,
  /// если выбор отменён.
  /// </summary>
  /// <param name="suggestedFileName">Имя файла по умолчанию (например, <c>archive.7z</c>).</param>
  Task<string?> PickSavePathAsync(string suggestedFileName);
}
