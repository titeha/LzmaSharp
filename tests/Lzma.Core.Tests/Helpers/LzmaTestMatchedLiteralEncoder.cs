using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;
/// <summary>
/// <para>
/// Мини-энкодер, который умеет собрать небольшой поток:
/// - несколько обычных литералов;
/// - один "обычный" match (isRep=0) в low-диапазоне длины;
/// - один литерал СРАЗУ ПОСЛЕ match, который обязан кодироваться в "matched literal" режиме.
/// </para>
/// <para>
/// Это нужно, чтобы тестами зафиксировать поведение декодера:
/// - после match состояние НЕ литеральное => литерал должен декодироваться с учётом matchByte;
/// - matchByte берётся из словаря по rep0 (rep0 после match = distance этого match);
/// - декодирование должно корректно работать потоково (когда вход режем на мелкие кусочки).
/// </para>
/// </summary>
internal static class LzmaTestMatchedLiteralEncoder
{
  /// <summary>
  /// <para>Кодирует поток, который при распаковке даёт "ABCABX":</para>
  /// <para>
  /// - литералы: 'A','B','C'
  /// - match: distance=3, len=2 (копирует "AB")
  /// - matched-literal: 'X' (matchByte при этом = 'C', потому что rep0=3)
  /// </para>
  /// </summary>
  public static byte[] Encode_ABC_Match3_Len2_Then_MatchedLiteral(LzmaProperties props, byte matchedLiteral)
  {
    // Параметры тестового потока.
    ReadOnlySpan<byte> literals = "ABC"u8;
    const int matchDistance = 3;
    const int matchLen = 2;
    byte finalLiteral = matchedLiteral;

    if (matchDistance <= 0)
      throw new ArgumentOutOfRangeException(nameof(matchDistance));

    if (matchLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(matchLen));

    // На данном шаге держим всё в low-диапазоне длины (как и другие тестовые энкодеры).
    if (matchLen > LzmaConstants.MatchMinLen + 7)
      throw new ArgumentOutOfRangeException(nameof(matchLen), "Тестовый энкодер поддерживает только low-диапазон длины (до MatchMinLen + 7).");

    var range = new LzmaTestRangeEncoder();
    range.EncodeInitBytes();

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // Модели для isMatch и isRep.
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);

    // Модели для len (упрощённо: choice + low[posState]).
    ushort[] lenChoice = new ushort[2];
    ushort[] lenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];
    LzmaProbability.Reset(lenChoice);
    LzmaProbability.Reset(lenLow);

    // Модели для posSlot (упрощённо: только posSlot, т.к. distance < 4).
    // На один lenToPosState приходится отдельное дерево из (1<<NumPosSlotBits) вероятностей.
    ushort[] posSlot = new ushort[LzmaConstants.NumLenToPosStates * (1 << LzmaConstants.NumPosSlotBits)];
    LzmaProbability.Reset(posSlot);

    // Модели литералов.
    var literal = new LzmaLiteralDecoder(props.Lc, props.Lp);
    literal.Reset();

    var state = new LzmaState();
    state.Reset();

    // Мини-словарь для вычисления matchByte (и для проверки самих значений на выходе энкодера).
    var outBytes = new List<byte>(capacity: 64);

    byte prev = 0;

    int rep0 = 1;
    int rep1 = 1;
    int rep2 = 1;
    int rep3 = 1;

    // 1) Пишем литералы "ABC".
    for (int i = 0; i < literals.Length; i++)
    {
      long pos = outBytes.Count;
      EncodeIsMatch(range, isMatch, state, numPosStates, posStateMask, pos, bit: 0);
      EncodeLiteralNormal(range, literal, pos, prev, literals[i]);

      state.UpdateLiteral();
      prev = literals[i];
      outBytes.Add(literals[i]);
    }

    // 2) Match (isMatch=1, isRep=0).
    {
      long pos = outBytes.Count;
      int posState = (int)pos & posStateMask;

      EncodeIsMatch(range, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
      EncodeIsRep(range, isRep, state, bit: 0);

      EncodeMatchLenLowOnly(range, lenChoice, lenLow, posState, numPosStates, matchLen);
      EncodeDistance_LowOnly(range, posSlot, matchLen, matchDistance);

      // Обновляем rep-историю как в декодере: rep0 получает distance match.
      rep3 = rep2;
      rep2 = rep1;
      rep1 = rep0;
      rep0 = matchDistance;

      state.UpdateMatch();

      // Моделируем копирование в словарь.
      for (int i = 0; i < matchLen; i++)
      {
        int srcIndex = outBytes.Count - matchDistance;
        byte b = outBytes[srcIndex];
        outBytes.Add(b);
        prev = b;
      }
    }

    // 3) Литерал сразу после match. Он должен кодироваться в matched literal режиме.
    {
      long pos = outBytes.Count;
      EncodeIsMatch(range, isMatch, state, numPosStates, posStateMask, pos, bit: 0);

      // matchByte берётся по rep0.
      byte matchByte = outBytes[outBytes.Count - rep0];

      EncodeLiteralMatched(range, literal, pos, prev, matchByte, finalLiteral);

      state.UpdateLiteral();
      prev = finalLiteral;
      outBytes.Add(finalLiteral);
    }

    range.Flush();
    return range.ToArray();
  }

  private static void EncodeIsMatch(
    LzmaTestRangeEncoder range,
    ushort[] isMatch,
    LzmaState state,
    int numPosStates,
    int posStateMask,
    long pos,
    uint bit)
  {
    int posState = (int)pos & posStateMask;
    int idx = state.Value * numPosStates + posState;
    range.EncodeBit(ref isMatch[idx], bit);
  }

  private static void EncodeIsRep(LzmaTestRangeEncoder range, ushort[] isRep, LzmaState state, uint bit)
  {
    range.EncodeBit(ref isRep[state.Value], bit);
  }

  private static void EncodeMatchLenLowOnly(
    LzmaTestRangeEncoder range,
    ushort[] choice,
    ushort[] low,
    int posState,
    int numPosStates,
    int matchLen)
  {
    // На данном шаге используем только low-ветку.
    // choice[0] == 0 => low.
    range.EncodeBit(ref choice[0], 0);

    int symbolValue = matchLen - LzmaConstants.MatchMinLen; // 0..7

    int probsOffset = posState * (1 << LzmaConstants.LenNumLowBits);

    // bit-tree на LenNumLowBits.
    int m = 1;
    for (int i = LzmaConstants.LenNumLowBits - 1; i >= 0; i--)
    {
      uint bit = (uint)((symbolValue >> i) & 1);
      range.EncodeBit(ref low[probsOffset + m], bit);
      m = (m << 1) | (int)bit;
    }
  }

  private static void EncodeDistance_LowOnly(
    LzmaTestRangeEncoder range,
    ushort[] posSlot,
    int matchLen,
    int distance)
  {
    // Поддерживаем только маленькие distance (< 4), чтобы не лезть в direct/align bits.
    if (distance < 1 || distance > 4)
      throw new ArgumentOutOfRangeException(nameof(distance), "Тестовый энкодер поддерживает только distance 1..4.");

    int lenToPosState = matchLen - LzmaConstants.MatchMinLen;
    if (lenToPosState >= LzmaConstants.NumLenToPosStates)
      lenToPosState = LzmaConstants.NumLenToPosStates - 1;

    int posSlotValue = distance - 1; // для distance < 4: distance = posSlot + 1

    int baseOffset = lenToPosState * (1 << LzmaConstants.NumPosSlotBits);
    EncodeBitTree(range, posSlot, baseOffset, LzmaConstants.NumPosSlotBits, posSlotValue);
  }

  private static void EncodeBitTree(
    LzmaTestRangeEncoder range,
    ushort[] probs,
    int probsOffset,
    int numBits,
    int symbolValue)
  {
    int m = 1;

    for (int i = numBits - 1; i >= 0; i--)
    {
      uint bit = (uint)((symbolValue >> i) & 1);
      range.EncodeBit(ref probs[probsOffset + m], bit);
      m = (m << 1) | (int)bit;
    }
  }

  private static void EncodeLiteralNormal(
    LzmaTestRangeEncoder range,
    LzmaLiteralDecoder literal,
    long pos,
    byte prevByte,
    byte value)
  {
    int baseOffset = literal.GetSubCoderOffset(pos, prevByte);

    int symbol = 1;
    for (int i = 7; i >= 0; i--)
    {
      uint bit = (uint)((value >> i) & 1);
      range.EncodeBit(ref literal.Probs[baseOffset + symbol], bit);
      symbol = (symbol << 1) | (int)bit;
    }
  }

  private static void EncodeLiteralMatched(
    LzmaTestRangeEncoder range,
    LzmaLiteralDecoder literal,
    long pos,
    byte prevByte,
    byte matchByte,
    byte value)
  {
    int baseOffset = literal.GetSubCoderOffset(pos, prevByte);

    int symbol = 1;
    uint mb = matchByte;
    bool matchMode = true;

    for (int i = 7; i >= 0; i--)
    {
      uint bit = (uint)((value >> i) & 1);

      if (matchMode)
      {
        uint matchBit = (mb >> 7) & 1;
        mb = (mb << 1) & 0xFF;

        int probIndex = baseOffset + (((1 + (int)matchBit) << 8) + symbol);
        range.EncodeBit(ref literal.Probs[probIndex], bit);

        symbol = (symbol << 1) | (int)bit;

        if (bit != matchBit)
          matchMode = false;
      }
      else
      {
        range.EncodeBit(ref literal.Probs[baseOffset + symbol], bit);
        symbol = (symbol << 1) | (int)bit;
      }
    }
  }
}
