namespace Lzma.Core.Zip;

/// <summary>
/// Результат распаковки ZIP-архива на диск.
/// </summary>
public enum ZipExtractResult
{
  /// <summary>Все элементы успешно записаны.</summary>
  Ok = 0,

  /// <summary>Небезопасный/конфликтующий путь, дубли или существующий файл без разрешения на перезапись.</summary>
  InvalidData = 1,

  /// <summary>Ошибка ввода-вывода при записи на диск.</summary>
  IOError = 2,

  /// <summary>Зашифрованный архив: неверный пароль либо пароль не задан.</summary>
  WrongPassword = 3,
}
