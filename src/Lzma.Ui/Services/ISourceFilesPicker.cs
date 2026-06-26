using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>Выбранный исходный файл для добавления в архив: имя записи и содержимое.</summary>
public sealed record PickedFile(string Name, byte[] Bytes);

/// <summary>
/// Абстракция выбора исходных файлов для упаковки. Отделяет ViewModel от платформенного
/// диалога множественного выбора, чтобы логику создания архива можно было тестировать без UI.
/// </summary>
public interface ISourceFilesPicker
{
  /// <summary>
  /// Просит выбрать один или несколько файлов. Возвращает <see langword="null"/>, если выбор
  /// отменён (пустой список трактуется так же — добавлять нечего).
  /// </summary>
  Task<IReadOnlyList<PickedFile>?> PickFilesAsync();
}
