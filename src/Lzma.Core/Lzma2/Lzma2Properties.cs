namespace Lzma.Core.Lzma2;

/// <summary>
/// Свойства кодека LZMA2 (один байт), которые задают размер словаря.
/// </summary>
/// <remarks>
/// <para>
/// В 7z (и не только) «properties» для LZMA2 — это один байт в диапазоне 0..40.
/// Он кодирует размер словаря по формуле из LZMA SDK:
/// </para>
/// <code>
/// dicSize = (2 | (prop & 1)) &lt;&lt; (prop / 2 + 11)
/// </code>
/// <para>
/// Для prop == 40 размер словаря задаётся как 0xFFFF_FFFF (4 GiB - 1).
/// В .NET это удобно хранить как <see cref="uint"/>.
/// </para>
/// </remarks>
public readonly struct Lzma2Properties
{
  /// <summary>
  /// Максимально допустимое значение байта свойств.
  /// </summary>
  public const byte MaxDictionaryProp = 40;

  /// <summary>
  /// Синоним для <see cref="MaxDictionaryProp"/> (для совместимости со старыми именами).
  /// </summary>
  public const byte MaxValue = MaxDictionaryProp;

  /// <summary>
  /// Исходное значение байта свойств (0..40).
  /// </summary>
  public byte DictionaryProp { get; }

  /// <summary>
  /// Синоним для <see cref="DictionaryProp"/> (для совместимости со старыми именами).
  /// </summary>
  public byte RawValue => DictionaryProp;

  /// <summary>
  /// Размер словаря, декодированный из <see cref="DictionaryProp"/>.
  /// </summary>
  public uint DictionarySize { get; }

  private Lzma2Properties(byte dictionaryProp, uint dictionarySize)
  {
    DictionaryProp = dictionaryProp;
    DictionarySize = dictionarySize;
  }

  /// <summary>
  /// Пытается получить размер словаря как <see cref="int"/>.
  /// </summary>
  /// <remarks>
  /// Возвращает <c>false</c>, если значение не помещается в Int32 (например, 2 GiB и выше)
  /// или если <see cref="DictionaryProp"/> равен 40 (0xFFFF_FFFF).
  /// </remarks>
  public bool TryGetDictionarySizeInt32(out int dictionarySize)
  {
    if (DictionarySize == 0xFFFF_FFFFu || DictionarySize > int.MaxValue)
    {
      dictionarySize = 0;
      return false;
    }

    dictionarySize = unchecked((int)DictionarySize);
    return true;
  }

  /// <summary>
  /// Парсит байт свойств LZMA2.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="dictionaryProp"/> не в диапазоне 0..40.</exception>
  public static Lzma2Properties Parse(byte dictionaryProp)
  {
    if (!TryParse(dictionaryProp, out var properties))
      throw new ArgumentOutOfRangeException(nameof(dictionaryProp), dictionaryProp, "LZMA2 properties должны быть в диапазоне 0..40.");

    return properties;
  }

  /// <summary>
  /// Пытается распарсить байт свойств LZMA2.
  /// </summary>
  public static bool TryParse(byte dictionaryProp, out Lzma2Properties properties)
  {
    if (dictionaryProp > MaxDictionaryProp)
    {
      properties = default;
      return false;
    }

    // Спец-значение из SDK
    if (dictionaryProp == 40)
    {
      properties = new Lzma2Properties(dictionaryProp: 40, dictionarySize: 0xFFFF_FFFFu);
      return true;
    }

    properties = new Lzma2Properties(dictionaryProp, ComputeDictionarySize(dictionaryProp));
    return true;
  }

  /// <summary>
  /// Создаёт <see cref="Lzma2Properties"/> по желаемому размеру словаря.
  /// </summary>
  /// <remarks>
  /// Возвращается минимальный <see cref="DictionaryProp"/>, для которого вычисленный
  /// <see cref="DictionarySize"/> будет <b>не меньше</b> <paramref name="dictionarySize"/>.
  /// </remarks>
  /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="dictionarySize"/> меньше 4 KiB.</exception>
  public static Lzma2Properties FromDictionarySize(uint dictionarySize)
  {
    if (!TryCreateFromDictionarySize(dictionarySize, out var properties))
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), dictionarySize, "Размер словаря LZMA2 должен быть не меньше 4096 байт.");

    return properties;
  }

  /// <summary>
  /// Пытается создать <see cref="Lzma2Properties"/> по желаемому размеру словаря.
  /// </summary>
  /// <remarks>
  /// Возвращается минимальный <see cref="DictionaryProp"/>, для которого вычисленный
  /// <see cref="DictionarySize"/> будет <b>не меньше</b> <paramref name="dictionarySize"/>.
  /// </remarks>
  public static bool TryCreateFromDictionarySize(uint dictionarySize, out Lzma2Properties properties)
  {
    // Минимальный размер словаря, который можно закодировать (prop=0).
    if (dictionarySize < 4096u)
    {
      properties = default;
      return false;
    }

    // Спец-значение.
    if (dictionarySize == 0xFFFF_FFFFu)
    {
      properties = new Lzma2Properties(dictionaryProp: 40, dictionarySize: 0xFFFF_FFFFu);
      return true;
    }

    // Подбираем минимальный prop, который покрывает требуемый размер.
    for (byte prop = 0; prop < 40; prop++)
    {
      uint size = ComputeDictionarySize(prop);
      if (size >= dictionarySize)
      {
        properties = new Lzma2Properties(dictionaryProp: prop, dictionarySize: size);
        return true;
      }
    }

    // Если размер больше того, что можно представить prop=39 (3 GiB), остаётся только prop=40.
    properties = new Lzma2Properties(dictionaryProp: 40, dictionarySize: 0xFFFF_FFFFu);
    return true;
  }

  /// <summary>
  /// Пытается закодировать размер словаря в «properties byte» (DictionaryProp) для LZMA2.
  /// </summary>
  /// <param name="dictionarySize">Размер словаря в байтах.</param>
  /// <param name="propertiesByte">Байт свойств LZMA2 (DictionaryProp).</param>
  /// <returns><see langword="true"/>, если удалось закодировать; иначе <see langword="false"/>.</returns>
  public static bool TryEncode(int dictionarySize, out byte propertiesByte)
  {
    if (dictionarySize <= 0)
    {
      propertiesByte = 0;
      return false;
    }

    return TryEncode((uint)dictionarySize, out propertiesByte);
  }

  /// <summary>
  /// Пытается закодировать размер словаря в «properties byte» (DictionaryProp) для LZMA2.
  /// </summary>
  /// <param name="dictionarySize">Размер словаря в байтах.</param>
  /// <param name="propertiesByte">Байт свойств LZMA2 (DictionaryProp).</param>
  /// <returns><see langword="true"/>, если удалось закодировать; иначе <see langword="false"/>.</returns>
  public static bool TryEncode(uint dictionarySize, out byte propertiesByte)
  {
    if (!TryCreateFromDictionarySize(dictionarySize, out var props))
    {
      propertiesByte = 0;
      return false;
    }

    propertiesByte = props.DictionaryProp;
    return true;
  }

  private static uint ComputeDictionarySize(byte dictionaryProp)
  {
    // Формула из SDK. Для пропов 0..39 значения помещаются в UInt32.
    int shift = dictionaryProp / 2 + 11;
    uint mul = (uint)(2 | (dictionaryProp & 1));
    return mul << shift;
  }
}
