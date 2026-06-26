namespace Lzma.Core.SevenZip;

/// <summary>
/// Параметры 7zAES-шифрования (AES-256 + SHA-256 KDF) при записи 7z-архива.
/// </summary>
/// <remarks>
/// Каждый непустой файл шифруется отдельным folder-ом. Ключ общий для архива (зависит от соли
/// и пароля), а IV у каждого потока свой (база + индекс потока) — для CBC это исключает
/// одинаковые первые блоки шифртекста у файлов с одинаковым началом. Если <see cref="Salt"/>
/// или <see cref="InitializationVector"/> не заданы (<see langword="null"/>), writer генерирует
/// их криптослучайно (рекомендуемый режим). Явные значения — для детерминированных тестов.
/// </remarks>
public sealed record SevenZipAesEncryptionOptions
{
  /// <summary>Парольный материал архива.</summary>
  public required SevenZipPassword Password { get; init; }

  /// <summary>
  /// Показатель числа циклов SHA-256 KDF (0..<see cref="SevenZipAesCoder.SupportedNumCyclesPowerMax"/>),
  /// либо <see cref="SevenZipAesCoder.DirectKeyNumCyclesPower"/> для тестового direct-key.
  /// По умолчанию 19 (как обычно у 7-Zip).
  /// </summary>
  public byte NumCyclesPower { get; init; } = DefaultNumCyclesPower;

  /// <summary>
  /// Соль KDF. Если <see langword="null"/>, writer сгенерирует криптослучайную соль
  /// размером <see cref="DefaultSaltSize"/>.
  /// </summary>
  public byte[]? Salt { get; init; }

  /// <summary>
  /// Базовый 16-байтовый IV. Если <see langword="null"/>, writer сгенерирует криптослучайный.
  /// </summary>
  public byte[]? InitializationVector { get; init; }

  /// <summary>
  /// Сжимать содержимое LZMA2 перед шифрованием (folder из двух coder-ов: LZMA2 → AES).
  /// По умолчанию <see langword="false"/> — только шифрование.
  /// </summary>
  public bool CompressWithLzma2 { get; init; }

  /// <summary>Размер соли по умолчанию.</summary>
  public const int DefaultSaltSize = 16;

  /// <summary>Размер IV по умолчанию (полный блок AES).</summary>
  public const int DefaultInitializationVectorSize = 16;

  /// <summary>Показатель числа циклов KDF по умолчанию.</summary>
  public const byte DefaultNumCyclesPower = 19;
}
