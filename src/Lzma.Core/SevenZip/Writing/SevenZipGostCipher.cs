namespace Lzma.Core.SevenZip;

/// <summary>
/// Выбор шифра для экспериментального ГОСТ-шифрования при записи 7z-архива.
/// </summary>
public enum SevenZipGostCipher
{
  /// <summary>Кузнечик (ГОСТ Р 34.12-2015, блок 128 бит) в режиме CTR.</summary>
  Kuznyechik = 0,

  /// <summary>Магма (ГОСТ Р 34.12-2015, блок 64 бита) в режиме CTR.</summary>
  Magma = 1,
}
