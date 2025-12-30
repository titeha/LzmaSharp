// Copyright (c) ...
//
// Этот файл — часть учебного/постепенного переписывания LZMA/LZMA2.
// Шаг 5: добавляем маленький, но важный кирпичик — разбор/сборку LZMA property byte.
//
// Зачем это нужно:
//   * LZMA2 (в "LZMA"-чанках) может нести 1 байт properties.
//   * Эти properties задают параметры модели: lc, lp, pb.
//   * В следующих шагах мы начнём писать LZMA-декодер, и нам нужно будет
//     строго и предсказуемо проверять/хранить эти значения.
//
// Формат property byte (используется как в LZMA1, так и в LZMA2):
//   props = (pb * 5 + lp) * 9 + lc
// где:
//   0 <= lc <= 8
//   0 <= lp <= 4
//   0 <= pb <= 4
// Максимально допустимое значение props: 224 (0xE0).

namespace Lzma.Core.Lzma1;

/// <summary>
/// Параметры LZMA-модели (lc/lp/pb), закодированные в одном байте properties.
/// </summary>
/// <remarks>
/// <para>
/// Эти параметры влияют на то, как декодируются литералы (lc/lp) и как работает
/// модель контекста (pb). Их корректность критична: неверные значения приводят
/// к некорректной распаковке или аварийному завершению.
/// </para>
/// <para>
/// Мы храним значения как <see cref="byte"/>, потому что диапазоны малы.
/// </para>
/// </remarks>
public readonly record struct LzmaProperties(byte Lc, byte Lp, byte Pb)
{
  /// <summary>Максимально допустимое значение lc.</summary>
  public const int MaxLc = 8;

  /// <summary>Максимально допустимое значение lp.</summary>
  public const int MaxLp = 4;

  /// <summary>Максимально допустимое значение pb.</summary>
  public const int MaxPb = 4;

  /// <summary>Максимально допустимое значение property byte.</summary>
  public const int MaxPropertyByte = 224; // 0xE0

  /// <summary>
  /// Пытается разобрать <paramref name="propertyByte"/> (LZMA property byte) в (lc/lp/pb).
  /// </summary>
  /// <param name="propertyByte">Байт свойств (0..224).</param>
  /// <param name="properties">Результат, если разбор успешен.</param>
  public static bool TryParse(byte propertyByte, out LzmaProperties properties)
  {
    properties = default;

    // В спецификации жёстко ограничено.
    if (propertyByte > MaxPropertyByte)
      return false;

    int v = propertyByte;

    int lc = v % 9;
    v /= 9;

    int lp = v % 5;
    int pb = v / 5;

    if (lc is < 0 or > MaxLc)
      return false;
    if (lp is < 0 or > MaxLp)
      return false;
    if (pb is < 0 or > MaxPb)
      return false;

    properties = new LzmaProperties((byte)lc, (byte)lp, (byte)pb);
    return true;
  }

  /// <summary>
  /// Пытается создать <see cref="LzmaProperties"/> из чисел, проверяя диапазоны.
  /// </summary>
  public static bool TryCreate(int lc, int lp, int pb, out LzmaProperties properties)
  {
    properties = default;

    if (lc is < 0 or > MaxLc)
      return false;
    if (lp is < 0 or > MaxLp)
      return false;
    if (pb is < 0 or > MaxPb)
      return false;

    properties = new LzmaProperties((byte)lc, (byte)lp, (byte)pb);
    return true;
  }

  /// <summary>
  /// Пытается закодировать текущие значения (lc/lp/pb) в один байт properties.
  /// </summary>
  public bool TryToByte(out byte propertyByte)
  {
    propertyByte = 0;

    // На всякий случай защищаемся, даже если структура была создана некорректно.
    if (Lc > MaxLc || Lp > MaxLp || Pb > MaxPb)
      return false;

    int v = (Pb * 5 + Lp) * 9 + Lc;
    if (v is < 0 or > MaxPropertyByte)
      return false;

    propertyByte = (byte)v;
    return true;
  }

  /// <summary>
  /// Возвращает property byte или бросает исключение, если значения вне диапазона.
  /// </summary>
  /// <remarks>
  /// Этот метод удобен в тестах/прототипах. В основном коде лучше использовать
  /// <see cref="TryToByte"/> и обрабатывать ошибку без исключений.
  /// </remarks>
  public byte ToByteOrThrow()
  {
    if (!TryToByte(out byte b))
      throw new ArgumentOutOfRangeException(nameof(LzmaProperties), "Значения lc/lp/pb вне допустимого диапазона.");
    return b;
  }
}
