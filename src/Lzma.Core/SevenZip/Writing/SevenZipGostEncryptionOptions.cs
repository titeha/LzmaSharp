namespace Lzma.Core.SevenZip;

/// <summary>
/// Параметры экспериментального ГОСТ-шифрования при записи 7z-архива.
/// </summary>
/// <remarks>
/// Вектор инициализации должен быть уникальным для каждого зашифрованного потока:
/// режим CTR при повторном использовании пары (ключ, IV) раскрывает гамму. Текущий
/// writer шифрует не более одного непустого файла на архив, поэтому достаточно одного
/// IV; при расширении на несколько потоков потребуется свой IV на каждый поток.
/// </remarks>
public sealed class SevenZipGostEncryptionOptions
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
}
