namespace Lzma.Core.SevenZip;

/// <summary>
/// Экспериментальные private method id для GOST-веток LzmaSharp.
/// </summary>
/// <remarks>
/// Это не стандартные method id 7-Zip.
/// Они используются только как внутреннее расширение формата для LzmaSharp.
/// </remarks>
public static class SevenZipGostCoder
{
  /// <summary>
  /// Текущая версия формата properties для экспериментальных GOST coder-ов.
  /// </summary>
  public const byte CurrentPropertiesVersion = 1;

  /// <summary>
  /// Специальное значение properties, при котором ключ строится напрямую
  /// из salt и password без обычного KDF.
  /// </summary>
  /// <remarks>
  /// Для GOST-ветки LzmaSharp это экспериментальный test-friendly режим.
  /// Production KDF будет добавлен отдельным шагом через Стрибог.
  /// </remarks>
  public const byte DirectKeyNumCyclesPower = 0x3F;

  /// <summary>
  /// Максимальный размер salt в properties экспериментального GOST coder-а.
  /// </summary>
  public const int MaxSaltSize = 32;

  /// <summary>
  /// Максимальный размер IV в properties экспериментального GOST coder-а.
  /// </summary>
  public const int MaxInitializationVectorSize = 16;

  // Private experimental IDs по схеме 3F ... MM MM.
  // Префикс фиксируем внутри проекта и не меняем без крайней необходимости.

  /// <summary>
  /// Experimental method id для шифрования Кузнечик.
  /// </summary>
  public static ReadOnlySpan<byte> KuznyechikMethodId
      => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x01];

  /// <summary>
  /// Experimental method id для шифрования Магма.
  /// </summary>
  public static ReadOnlySpan<byte> MagmaMethodId
      => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x02];

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
  /// Пытается разобрать properties экспериментального GOST coder-а.
  /// </summary>
  /// <remarks>
  /// Формат version 1:
  ///   byte 0: version
  ///   byte 1: flags (пока должен быть 0)
  ///   byte 2: numCyclesPower
  ///   byte 3: saltSize
  ///   byte 4: ivSize
  ///   затем: salt
  ///   затем: IV
  /// </remarks>
  public static bool TryParseProperties(
      ReadOnlySpan<byte> properties,
      out SevenZipGostProperties? parsed)
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
}

/// <summary>
/// Разобранные properties экспериментального GOST coder-а.
/// </summary>
/// <remarks>
/// Создаёт объект разобранных properties экспериментального GOST coder-а.
/// </remarks>
public sealed class SevenZipGostProperties(
    byte version,
    byte flags,
    byte numCyclesPower,
    byte[] salt,
    byte[] initializationVector)
{

  /// <summary>
  /// Версия формата properties.
  /// </summary>
  public byte Version { get; } = version;

  /// <summary>
  /// Flags поля properties.
  /// </summary>
  public byte Flags { get; } = flags;

  /// <summary>
  /// Показатель числа циклов derivation.
  /// </summary>
  public byte NumCyclesPower { get; } = numCyclesPower;

  /// <summary>
  /// Salt.
  /// </summary>
  public byte[] Salt { get; } = salt;

  /// <summary>
  /// Initialization Vector.
  /// </summary>
  public byte[] InitializationVector { get; } = initializationVector;
}
