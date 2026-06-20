namespace Lzma.Core.SevenZip;

/// <summary>
/// Параметры экспериментального ГОСТ-шифрования при записи 7z-архива.
/// </summary>
/// <remarks>
/// Вектор инициализации должен быть уникальным для каждого зашифрованного потока:
/// режим CTR при повторном использовании пары (ключ, IV) раскрывает гамму. Для архива
/// из нескольких файлов writer берёт <see cref="InitializationVector"/> как базу и для
/// каждого потока использует базу + индекс потока (поэтому база должна быть случайной и
/// иметь запас до переполнения разрядности). Цепочка <see cref="CompressWithLzma2"/>
/// поддерживает пока только один непустой файл.
/// </remarks>
public sealed record SevenZipGostEncryptionOptions
{
  /// <summary>Выбранный шифр.</summary>
  public required SevenZipGostCipher Cipher { get; init; }

  /// <summary>Парольный материал архива.</summary>
  public required SevenZipPassword Password { get; init; }

  /// <summary>
  /// Показатель числа циклов формирования ключа: либо парольный KDF через Стрибог
  /// (0..<see cref="SevenZipGostCoder.SupportedNumCyclesPowerMax"/>), либо
  /// <see cref="SevenZipGostCoder.DirectKeyNumCyclesPower"/> для тестового direct-key.
  /// </summary>
  public required byte NumCyclesPower { get; init; }

  /// <summary>Соль для формирования ключа.</summary>
  public required byte[] Salt { get; init; }

  /// <summary>
  /// Вектор инициализации: 8 байт для Кузнечика, 4 байта для Магмы.
  /// </summary>
  public required byte[] InitializationVector { get; init; }

  /// <summary>
  /// Сжимать содержимое LZMA2 перед шифрованием (folder из двух coder-ов:
  /// LZMA2 → ГОСТ). По умолчанию <see langword="false"/> — только шифрование.
  /// </summary>
  public bool CompressWithLzma2 { get; init; }
}
