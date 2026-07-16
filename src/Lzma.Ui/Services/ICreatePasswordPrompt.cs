using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>
/// Запрос НОВОГО пароля при создании зашифрованного (AES) архива — с подтверждением ввода.
/// Отделён от <see cref="IPasswordPrompt"/> (тот — для чтения существующего архива).
/// </summary>
public interface ICreatePasswordPrompt
{
  /// <summary>
  /// Показывает диалог задания пароля (поле + подтверждение). Возвращает пароль, либо
  /// <see langword="null"/> при отмене.
  /// </summary>
  Task<string?> RequestNewPasswordAsync();
}
