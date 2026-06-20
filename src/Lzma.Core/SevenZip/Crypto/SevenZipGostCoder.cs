namespace Lzma.Core.SevenZip;

/// <summary>
/// Экспериментальные закрытые идентификаторы методов для GOST-ветки LzmaSharp.
/// </summary>
/// <remarks>
/// Это не стандартные идентификаторы методов 7-Zip.
/// Они используются только как внутреннее расширение формата для LzmaSharp.
/// </remarks>
public static class SevenZipGostCoder
{
  /// <summary>
  /// Текущая версия формата properties для экспериментальных GOST coder-ов.
  /// </summary>
  public const byte CurrentPropertiesVersion = 1;

  /// <summary>
  /// Специальное значение свойства numCyclesPower, при котором ключ строится
  /// напрямую из соли и парольного материала без обычной функции формирования ключа.
  /// </summary>
  /// <remarks>
  /// Для GOST-ветки LzmaSharp это экспериментальный тестовый режим.
  /// Полноценная функция формирования ключа через Стрибог будет добавлена отдельно.
  /// </remarks>
  public const byte DirectKeyNumCyclesPower = 0x3F;

  /// <summary>
  /// Максимальное поддержанное значение numCyclesPower для парольного KDF.
  /// </summary>
  /// <remarks>
  /// Парольный KDF использует one-shot Стрибог над конкатенацией всех раундов,
  /// поэтому объём буфера = 2^numCyclesPower × (соль+пароль+8). Ограничение
  /// держит этот буфер в разумных пределах; при появлении потокового Стрибога
  /// предел можно будет поднять.
  /// </remarks>
  public const byte SupportedNumCyclesPowerMax = 20;

  /// <summary>
  /// Проверяет, поддержано ли значение numCyclesPower (парольный KDF или direct-key).
  /// </summary>
  public static bool IsSupportedNumCyclesPower(byte numCyclesPower)
  {
    return numCyclesPower <= SupportedNumCyclesPowerMax
        || numCyclesPower == DirectKeyNumCyclesPower;
  }

  /// <summary>
  /// Максимальный размер соли в свойствах экспериментального GOST-кодера.
  /// </summary>
  public const int MaxSaltSize = 32;

  /// <summary>
  /// Максимальный размер вектора инициализации в свойствах экспериментального GOST-кодера.
  /// </summary>
  public const int MaxInitializationVectorSize = 16;

  // Закрытые экспериментальные идентификаторы методов по схеме 3F ... MM MM.
  // Префикс фиксируем внутри проекта и не меняем без крайней необходимости.

  /// <summary>
  /// Экспериментальный идентификатор метода для шифрования Кузнечиком.
  /// </summary>
  public static ReadOnlySpan<byte> KuznyechikMethodId => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x01];

  /// <summary>
  /// Экспериментальный идентификатор метода для шифрования Магмой.
  /// </summary>
  public static ReadOnlySpan<byte> MagmaMethodId => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x02];

  /// <summary>
  /// Проверяет, относится ли method id к экспериментальным GOST coder-ам LzmaSharp.
  /// </summary>
  public static bool IsGostMethodId(ReadOnlySpan<byte> methodId) => IsKuznyechikMethodId(methodId) || IsMagmaMethodId(methodId);

  /// <summary>
  /// Проверяет, является ли method id coder-ом Кузнечик.
  /// </summary>
  public static bool IsKuznyechikMethodId(ReadOnlySpan<byte> methodId) => methodId.SequenceEqual(KuznyechikMethodId);

  /// <summary>
  /// Проверяет, является ли method id coder-ом Магма.
  /// </summary>
  public static bool IsMagmaMethodId(ReadOnlySpan<byte> methodId) => methodId.SequenceEqual(MagmaMethodId);

  /// <summary>
  /// Пытается разобрать свойства экспериментального GOST-кодера.
  /// </summary>
  /// <remarks>
  /// Формат версии 1:
  /// <list type="bullet">
  /// <item>
  /// <description>байт 0: версия формата;</description>
  /// </item>
  /// <item>
  /// <description>байт 1: флаги, пока должны быть равны 0;</description>
  /// </item>
  /// <item>
  /// <description>байт 2: показатель числа циклов формирования ключа;</description>
  /// </item>
  /// <item>
  /// <description>байт 3: размер соли;</description>
  /// </item>
  /// <item>
  /// <description>байт 4: размер вектора инициализации;</description>
  /// </item>
  /// <item>
  /// <description>далее: соль;</description>
  /// </item>
  /// <item>
  /// <description>далее: вектор инициализации.</description>
  /// </item>
  /// </list>
  /// </remarks>
  public static bool TryParseProperties(ReadOnlySpan<byte> properties, out SevenZipGostProperties? parsed)
  {
    parsed = null;

    if (properties.Length < 5)
      return false;

    byte version = properties[0];
    byte flags = properties[1];
    byte numCyclesPower = properties[2];
    int saltSize = properties[3];
    int ivSize = properties[4];

    if (version != CurrentPropertiesVersion)
      return false;

    if (flags != 0)
      return false;

    if (saltSize > MaxSaltSize || ivSize > MaxInitializationVectorSize)
      return false;

    int expectedSize = 5 + saltSize + ivSize;
    if (properties.Length != expectedSize)
      return false;

    byte[] salt = properties.Slice(5, saltSize).ToArray();
    byte[] iv = properties.Slice(5 + saltSize, ivSize).ToArray();

    parsed = new SevenZipGostProperties(
        version: version,
        flags: flags,
        numCyclesPower: numCyclesPower,
        salt: salt,
        initializationVector: iv);

    return true;
  }

  /// <summary>
  /// Пытается сериализовать свойства экспериментального GOST-кодера в байты
  /// формата версии 1 (см. <see cref="TryParseProperties"/>).
  /// </summary>
  /// <param name="properties">Свойства для сериализации.</param>
  /// <param name="serialized">Сериализованные байты при успехе; иначе пустой массив.</param>
  /// <returns>
  /// <see langword="true"/>, если свойства корректны и сериализованы;
  /// иначе <see langword="false"/>.
  /// </returns>
  public static bool TrySerializeProperties(
      SevenZipGostProperties properties,
      out byte[] serialized)
  {
    ArgumentNullException.ThrowIfNull(properties);

    serialized = [];

    if (properties.Version != CurrentPropertiesVersion)
      return false;

    if (properties.Flags != 0)
      return false;

    if (properties.Salt.Length > MaxSaltSize
        || properties.InitializationVector.Length > MaxInitializationVectorSize)
      return false;

    var result = new byte[5 + properties.Salt.Length + properties.InitializationVector.Length];

    result[0] = properties.Version;
    result[1] = properties.Flags;
    result[2] = properties.NumCyclesPower;
    result[3] = (byte)properties.Salt.Length;
    result[4] = (byte)properties.InitializationVector.Length;

    properties.Salt.CopyTo(result.AsSpan(5));
    properties.InitializationVector.CopyTo(result.AsSpan(5 + properties.Salt.Length));

    serialized = result;
    return true;
  }
}

/// <summary>
/// Разобранные свойства экспериментального GOST-кодера.
/// </summary>
/// <param name="version">Версия формата свойств.</param>
/// <param name="flags">Флаги свойств.</param>
/// <param name="numCyclesPower">Показатель числа циклов формирования ключа.</param>
/// <param name="salt">Соль.</param>
/// <param name="initializationVector">Вектор инициализации.</param>
public sealed class SevenZipGostProperties(
    byte version,
    byte flags,
    byte numCyclesPower,
    byte[] salt,
    byte[] initializationVector)
{

  /// <summary>
  /// Версия формата свойств.
  /// </summary>
  public byte Version { get; } = version;

  /// <summary>
  /// Флаги свойств.
  /// </summary>
  public byte Flags { get; } = flags;

  /// <summary>
  /// Показатель числа циклов формирования ключа.
  /// </summary>
  public byte NumCyclesPower { get; } = numCyclesPower;

  /// <summary>
  /// Соль.
  /// </summary>
  public byte[] Salt { get; } = salt;

  /// <summary>
  /// Вектор инициализации.
  /// </summary>
  public byte[] InitializationVector { get; } = initializationVector;
}
