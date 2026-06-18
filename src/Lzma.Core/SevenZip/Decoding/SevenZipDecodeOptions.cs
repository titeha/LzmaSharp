namespace Lzma.Core.SevenZip;

/// <summary>
/// Настройки декодирования 7z.
/// </summary>
public sealed class SevenZipDecodeOptions
{
  /// <summary>
  /// Настройки декодирования по умолчанию.
  /// </summary>
  public static SevenZipDecodeOptions Default { get; } = new();

  /// <summary>
  /// Парольный материал для зашифрованных 7z-сценариев.
  /// </summary>
  /// <remarks>
  /// Объект настроек не владеет паролем и не вызывает <see cref="IDisposable.Dispose"/>.
  /// Ответственность за время жизни пароля остаётся у вызывающего кода.
  /// </remarks>
  public SevenZipPassword? Password { get; init; }

  /// <summary>
  /// Возвращает значение, показывающее, передан ли пароль.
  /// </summary>
  public bool HasPassword => Password is not null;

  /// <summary>
  /// Создаёт настройки декодирования с паролем.
  /// </summary>
  public static SevenZipDecodeOptions WithPassword(SevenZipPassword password)
  {
    ArgumentNullException.ThrowIfNull(password);

    return new SevenZipDecodeOptions
    {
      Password = password,
    };
  }
}
