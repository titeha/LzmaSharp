namespace Lzma.Core.Lzma1;

/// <summary>
/// Тип операции для «ручного» (скриптового) LZMA-кодирования.
/// </summary>
internal enum LzmaEncodeOpKind
{
  Literal,
  Match,
}

/// <summary>
/// Операция для «ручного» (скриптового) LZMA-кодирования.
/// </summary>
/// <remarks>
/// На ранних шагах мы собираем энкодер постепенно и пока не делаем матч-файндер.
/// Поэтому тесты (и отладочный код) могут задавать последовательность операций явно:
/// literal или обычный match (isRep == 0).
/// </remarks>
internal readonly struct LzmaEncodeOp
{
  public LzmaEncodeOpKind Kind { get; }

  /// <summary>
  /// Значение литерала (для <see cref="LzmaEncodeOpKind.Literal"/>).
  /// </summary>
  public byte Literal { get; }

  /// <summary>
  /// Дистанция матча (для <see cref="LzmaEncodeOpKind.Match"/>). Дистанция в LZMA 1-based.
  /// </summary>
  public int Distance { get; }

  /// <summary>
  /// Длина матча (для <see cref="LzmaEncodeOpKind.Match"/>).
  /// </summary>
  public int Length { get; }

  private LzmaEncodeOp(LzmaEncodeOpKind kind, byte literal, int distance, int length)
  {
    Kind = kind;
    Literal = literal;
    Distance = distance;
    Length = length;
  }

  public static LzmaEncodeOp Lit(byte value) => new(LzmaEncodeOpKind.Literal, value, distance: 0, length: 0);

  public static LzmaEncodeOp Match(int distance, int length) => new(LzmaEncodeOpKind.Match, literal: 0, distance, length);
}
