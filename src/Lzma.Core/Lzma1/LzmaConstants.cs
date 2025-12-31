namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Константы алгоритма LZMA (LZMA1).</para>
/// <para>
/// Здесь нет «магии» — это устоявшиеся значения из классической реализации LZMA (7-Zip SDK).
/// Мы держим их в одном месте, чтобы:
/// - проще читать код (видно, что означает число),
/// - проще писать тесты,
/// - проще менять/экспериментировать позже.
/// </para>
/// </summary>
public static class LzmaConstants
{
  // ============================================================
  // Range coder (арифметический декодер)
  // ============================================================

  /// <summary>
  /// Количество бит в «шкале вероятности».
  /// В LZMA вероятность хранится в 11-битном фиксированном виде.
  /// </summary>
  public const int NumBitModelTotalBits = 11;

  /// <summary>
  /// Полная шкала вероятности: 1 &lt;&lt; NumBitModelTotalBits (2048).
  /// </summary>
  public const int BitModelTotal = 1 << NumBitModelTotalBits;

  /// <summary>
  /// «Скорость адаптации» вероятностей (двиг на NumMoveBits).
  /// </summary>
  public const int NumMoveBits = 5;

  /// <summary>
  /// Граница нормализации range-кодера.
  /// Если Range &lt; TopValue — надо дочитать байт и расширить диапазон.
  /// </summary>
  public const uint RangeTopValue = 1u << 24; // 0x0100_0000

  // ============================================================
  // Базовые параметры LZMA
  // ============================================================

  /// <summary>Минимальная длина матча (match) в LZMA.</summary>
  public const int MatchMinLen = 2;

  /// <summary>Количество «репит»-дистанций (rep0..rep3).</summary>
  public const int NumRepDistances = 4;

  /// <summary>Количество состояний автомата LZMA.</summary>
  public const int NumStates = 12;

  /// <summary>
  /// Количество «len-to-pos» состояний.
  /// Используется при выборе модели расстояния по длине матча.
  /// </summary>
  public const int NumLenToPosStates = 4;

  // ============================================================
  // Модель расстояний (dist)
  // ============================================================

  /// <summary>Число бит в «posSlot» (0..63).</summary>
  public const int NumPosSlotBits = 6;

  /// <summary>Количество бит для align (младшие биты расстояния).</summary>
  public const int NumAlignBits = 4;

  /// <summary>Размер таблицы align: 1 &lt;&lt; NumAlignBits (16).</summary>
  public const int AlignTableSize = 1 << NumAlignBits;

  /// <summary>Маска для выделения align-части: AlignTableSize - 1 (15).</summary>
  public const int AlignMask = AlignTableSize - 1;

  /// <summary>Индекс, с которого начинается pos-model (dist) в LZMA.</summary>
  public const int StartPosModelIndex = 4;

  /// <summary>Индекс, на котором заканчивается pos-model (dist) в LZMA.</summary>
  public const int EndPosModelIndex = 14;

  /// <summary>Количество pos-model-ов: EndPosModelIndex - StartPosModelIndex.</summary>
  public const int NumPosModels = EndPosModelIndex - StartPosModelIndex;

  /// <summary>
  /// Количество «полных» дистанций, кодируемых напрямую моделью.
  /// </summary>
  public const int NumFullDistances = 1 << (EndPosModelIndex / 2);

  // ============================================================
  // Модель длин (len)
  // ============================================================

  public const int NumLowLenBits = 3;
  public const int NumMidLenBits = 3;
  public const int NumHighLenBits = 8;

  public const int NumLowLenSymbols = 1 << NumLowLenBits;
  public const int NumMidLenSymbols = 1 << NumMidLenBits;
  public const int NumHighLenSymbols = 1 << NumHighLenBits;

  /// <summary>
  /// Общее количество кодируемых значений длины.
  /// </summary>
  public const int NumLenSymbols = NumLowLenSymbols + NumMidLenSymbols + NumHighLenSymbols;

  /// <summary>
  /// Максимальная длина матча: MatchMinLen + NumLenSymbols - 1 (обычно 273).
  /// </summary>
  public const int MatchMaxLen = MatchMinLen + NumLenSymbols - 1;

  // ============================================================
  // Модель литералов
  // ============================================================

  /// <summary>
  /// Количество «литеральных состояний» (используется в state-machine).
  /// </summary>
  public const int NumLitStates = 7;
}
