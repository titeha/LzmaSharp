namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Состояние LZMA (0..11), которое определяет «контекст» при декодировании.</para>
/// <para>
/// Это маленький конечный автомат, описанный в оригинальном LZMA SDK.
/// Он обновляется после каждого декодированного события:
/// - Literal (байт-литерал)
/// - Match (обычное совпадение)
/// - Rep (повтор/репетиция из rep0..rep3)
/// - ShortRep (короткий повтор длиной 1)
/// </para>
/// <para>
/// Почему оно нужно:
/// - влияет на выбор вероятностных моделей (IsMatch / IsRep / Literal context и т.д.)
/// - влияет на режим декодирования литерала (обычный vs «matched literal»).
/// </para>
/// </summary>
public struct LzmaState
{
  /// <summary>
  /// Текущее значение состояния (0..11).
  /// </summary>
  public byte Value { get; private set; }

  /// <summary>
  /// Создаёт состояние с проверкой диапазона.
  /// Обычно в «боевом» декодере состояние начинается с 0 (Reset) и дальше
  /// меняется только методами Update*(), поэтому внешний код редко создаёт
  /// произвольные значения.
  /// </summary>
  public LzmaState(byte value)
  {
    if (value >= LzmaConstants.NumStates)
      throw new ArgumentOutOfRangeException(nameof(value), value, $"Допустимый диапазон: 0..{LzmaConstants.NumStates - 1}.");

    Value = value;
  }

  /// <summary>
  /// «Символьное» состояние (char state) — состояние, в котором последний токен был литералом.
  /// В LZMA это состояния 0..6 (включительно).
  /// </summary>
  public readonly bool IsLiteralState => Value < LzmaConstants.NumLitStates;

  /// <summary>
  /// Сбрасывает состояние в начальное (0).
  /// </summary>
  public void Reset() => Value = 0;

  /// <summary>
  /// Обновляет состояние после декодирования литерала.
  /// Формула полностью соответствует LZMA SDK.
  /// </summary>
  public void UpdateLiteral()
  {
    // Если мы и так в «ранних» состояниях, то остаёмся в 0.
    if (Value < 4)
    {
      Value = 0;
      return;
    }

    // Для 4..9 -> -3, для 10..11 -> -6.
    Value = Value < 10 ? (byte)(Value - 3) : (byte)(Value - 6);
  }

  /// <summary>
  /// Обновляет состояние после обычного match.
  /// </summary>
  public void UpdateMatch()
  {
    // 0..6 -> 7, 7..11 -> 10
    Value = Value < 7 ? (byte)7 : (byte)10;
  }

  /// <summary>
  /// Обновляет состояние после rep (репетиции).
  /// </summary>
  public void UpdateRep()
  {
    // 0..6 -> 8, 7..11 -> 11
    Value = Value < 7 ? (byte)8 : (byte)11;
  }

  /// <summary>
  /// Обновляет состояние после short rep (rep длиной 1).
  /// </summary>
  public void UpdateShortRep()
  {
    // 0..6 -> 9, 7..11 -> 11
    Value = Value < 7 ? (byte)9 : (byte)11;
  }
}
