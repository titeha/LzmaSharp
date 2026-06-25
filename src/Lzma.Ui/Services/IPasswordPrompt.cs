namespace Lzma.Ui.Services;

/// <summary>
/// Абстракция запроса пароля у пользователя. Отделяет ViewModel от платформенного
/// диалога, чтобы логику открытия зашифрованных архивов можно было тестировать без UI.
/// </summary>
public interface IPasswordPrompt
{
  /// <summary>
  /// Просит ввести пароль для архива. Возвращает <see langword="null"/>, если отменено.
  /// </summary>
  /// <param name="archiveName">Имя архива (для текста диалога).</param>
  /// <param name="previousAttemptFailed">
  /// <see langword="true"/>, если предыдущая попытка была с неверным паролем
  /// (диалог покажет подсказку).
  /// </param>
  Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed);
}
