namespace Lzma.Core.SevenZip;

/// <summary>
/// Параметры экспериментального ГОСТ-шифрования при записи 7z-архива.
/// </summary>
/// <remarks>
/// Вектор инициализации должен быть уникальным для каждого зашифрованного потока:
/// режим CTR при повторном использовании пары (ключ, IV) раскрывает гамму. Для архива
/// из нескольких файлов writer берёт <see cref="InitializationVector"/> как базу и для
/// каждого потока использует базу + индекс потока. Если <see cref="Salt"/> или
/// <see cref="InitializationVector"/> не заданы (<see langword="null"/>), writer
/// генерирует их криптослучайно — это рекомендуемый режим для реального использования,
/// он исключает случайный повтор IV. Явно заданные значения используются как есть
/// (нужно для детерминированных сценариев и тестов). Цепочка
/// <see cref="CompressWithLzma2"/> поддерживает пока только один непустой файл.
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

  /// <summary>
  /// Соль для формирования ключа. Если <see langword="null"/>, writer сгенерирует
  /// криптослучайную соль размером <see cref="DefaultSaltSize"/>.
  /// </summary>
  public byte[]? Salt { get; init; }

  /// <summary>
  /// Базовый вектор инициализации: 8 байт для Кузнечика, 4 байта для Магмы. Если
  /// <see langword="null"/>, writer сгенерирует криптослучайный IV нужного размера.
  /// </summary>
  public byte[]? InitializationVector { get; init; }

  /// <summary>
  /// Сжимать содержимое LZMA2 перед шифрованием (folder из двух coder-ов:
  /// LZMA2 → ГОСТ). По умолчанию <see langword="false"/> — только шифрование.
  /// </summary>
  public bool CompressWithLzma2 { get; init; }

  /// <summary>Размер криптослучайной соли по умолчанию, когда <see cref="Salt"/> не задана.</summary>
  public const int DefaultSaltSize = 16;
}
