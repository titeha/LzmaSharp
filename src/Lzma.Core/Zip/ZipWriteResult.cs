namespace Lzma.Core.Zip;

/// <summary>
/// Результат записи ZIP-архива.
/// </summary>
public enum ZipWriteResult
{
  /// <summary>Архив успешно построен.</summary>
  Ok = 0,

  /// <summary>Некорректные входные данные.</summary>
  InvalidData = 1,
}
