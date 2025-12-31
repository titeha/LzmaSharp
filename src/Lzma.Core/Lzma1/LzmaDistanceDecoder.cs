namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодирование «дистанции» (расстояния) для матчей LZMA.</para>
/// <para>
/// В LZMA расстояние кодируется через:
///  - posSlot (6-битное значение, зависит от lenToPosState)
///  - далее либо «обратное» дерево (ReverseBitTree) для младших битов (posSlot &lt; 14),
///  - либо прямые биты + align (posSlot &gt;= 14).
/// </para>
/// <para>
/// Важно: возвращаемое значение distance — это «настоящее» расстояние (как в Copy):
///  distance = pos + 1, то есть оно начинается с 1.
///  Значение 0 возможно только при переполнении uint — это признак повреждённого потока
///  (проверим позже на уровне полного декодера).
/// </para>
/// </summary>
public sealed class LzmaDistanceDecoder
{
  private readonly LzmaBitTreeDecoder[] _posSlotDecoders;
  private readonly LzmaBitTreeDecoder _posAlignDecoder;

  // Массив вероятностей для дерева posDecoders.
  // Размер взят из оригинального алгоритма: NumFullDistances - EndPosModelIndex.
  private readonly ushort[] _posDecoders;

  public LzmaDistanceDecoder()
  {
    _posSlotDecoders = new LzmaBitTreeDecoder[LzmaConstants.NumLenToPosStates];
    for (int i = 0; i < _posSlotDecoders.Length; i++)
      _posSlotDecoders[i] = new LzmaBitTreeDecoder(LzmaConstants.NumPosSlotBits);

    _posAlignDecoder = new LzmaBitTreeDecoder(LzmaConstants.NumAlignBits);

    const int posDecodersSize = LzmaConstants.NumFullDistances - LzmaConstants.EndPosModelIndex;
    if (posDecodersSize <= 0)
      throw new InvalidOperationException("Некорректные константы LZMA: размер posDecoders должен быть > 0.");

    _posDecoders = new ushort[posDecodersSize];
    Reset();
  }

  /// <summary>
  /// Сброс вероятностных моделей в исходное состояние.
  /// </summary>
  public void Reset()
  {
    foreach (var d in _posSlotDecoders)
      d.Reset();

    _posAlignDecoder.Reset();

    // Инициализация вероятностей «1/2».
    Array.Fill(_posDecoders, LzmaConstants.ProbabilityInitValue);
  }

  /// <summary>
  /// Декодировать distance (расстояние) для матча.
  ///
  /// lenToPosState вычисляется из длины матча и лежит в диапазоне [0..3].
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeDistance(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      int lenToPosState,
      out uint distance)
  {
    if ((uint)lenToPosState >= LzmaConstants.NumLenToPosStates)
      throw new ArgumentOutOfRangeException(nameof(lenToPosState));

    // 1) posSlot
    // В нашем LzmaBitTreeDecoder метод называется TryDecodeSymbol().
    var res = _posSlotDecoders[lenToPosState].TryDecodeSymbol(ref range, input, ref inputOffset, out uint posSlot);
    if (res != LzmaRangeDecodeResult.Ok)
    {
      distance = 0;
      return res;
    }

    // 2) Остальные биты расстояния
    return TryDecodeDistanceFromPosSlot(
        ref range,
        input,
        ref inputOffset,
        posSlot,
        out distance);
  }

  /// <summary>
  /// Чистая «арифметика» LZMA: как из posSlot и дополнительных битов получить distance.
  ///
  /// reverseBits — результат ReverseBitTree (LSB-first), используется только для posSlot &lt; EndPosModelIndex.
  /// directBits  — результат прямых бит (MSB-first как число), используется только для posSlot &gt;= EndPosModelIndex.
  /// alignBits   — результат ReverseBitTree (LSB-first) для align, используется только для posSlot &gt;= EndPosModelIndex.
  /// </summary>
  public static uint ComputeDistanceFromPosSlot(uint posSlot, uint reverseBits, uint directBits, uint alignBits)
  {
    // posSlot 0..3 кодирует расстояние напрямую
    if (posSlot < LzmaConstants.StartPosModelIndex)
      return posSlot + 1;

    int numDirectBits = (int)(posSlot >> 1) - 1;

    // Базовая часть позиции (pos)
    uint pos = (2 | (posSlot & 1)) << numDirectBits;

    if (posSlot < LzmaConstants.EndPosModelIndex)
    {
      // В этой ветке дополнительные биты берутся из posDecoders в «обратном» порядке.
      pos += reverseBits;
    }
    else
    {
      // В этой ветке: прямые биты (numDirectBits - NumAlignBits) и align (4 бита, reverse).
      pos += directBits << LzmaConstants.NumAlignBits;
      pos += alignBits;
    }

    // В LZMA расстояния хранятся как pos+1.
    // Если pos == uint.MaxValue, то distance переполнится и станет 0 — это признак повреждённого потока.
    return unchecked(pos + 1);
  }

  private LzmaRangeDecodeResult TryDecodeDistanceFromPosSlot(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      uint posSlot,
      out uint distance)
  {
    // Быстрый путь: posSlot 0..3
    if (posSlot < LzmaConstants.StartPosModelIndex)
    {
      distance = posSlot + 1;
      return LzmaRangeDecodeResult.Ok;
    }

    int numDirectBits = (int)(posSlot >> 1) - 1;
    uint pos = (2 | (posSlot & 1)) << numDirectBits;

    uint reverseBits = 0;
    uint directBits = 0;
    uint alignBits = 0;

    if (posSlot < LzmaConstants.EndPosModelIndex)
    {
      // posDecoders: «обратное» дерево с динамическим смещением.
      int startIndex = (int)(pos - posSlot - 1);

      var res = TryReverseDecodePosDecoders(
          ref range,
          input,
          ref inputOffset,
          startIndex,
          numDirectBits,
          out reverseBits);

      if (res != LzmaRangeDecodeResult.Ok)
      {
        distance = 0;
        return res;
      }
    }
    else
    {
      int numDirectBits2 = numDirectBits - LzmaConstants.NumAlignBits;

      // Сигнатура: (int numBits, ReadOnlySpan<byte> input, ref int offset, out uint value)
      var res = range.TryDecodeDirectBits(numDirectBits2, input, ref inputOffset, out directBits);
      if (res != LzmaRangeDecodeResult.Ok)
      {
        distance = 0;
        return res;
      }

      res = _posAlignDecoder.TryReverseDecode(ref range, input, ref inputOffset, out alignBits);
      if (res != LzmaRangeDecodeResult.Ok)
      {
        distance = 0;
        return res;
      }
    }

    distance = ComputeDistanceFromPosSlot(posSlot, reverseBits, directBits, alignBits);
    return LzmaRangeDecodeResult.Ok;
  }

  /// <summary>
  /// ReverseBitTreeDecode по массиву вероятностей, как в оригинальном LZMA.
  ///
  /// Важно: startIndex — это «смещение базового указателя» (аналог probs + startIndex в C),
  /// а внутри дерева индексируется как probs[startIndex + m], где m начинается с 1.
  /// </summary>
  private LzmaRangeDecodeResult TryReverseDecodePosDecoders(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      int startIndex,
      int numBits,
      out uint symbol)
  {
    if (startIndex < -1 || startIndex >= _posDecoders.Length)
      throw new ArgumentOutOfRangeException(nameof(startIndex));

    if (numBits < 0 || numBits > 31)
      throw new ArgumentOutOfRangeException(nameof(numBits));

    uint m = 1;
    uint result = 0;

    for (int i = 0; i < numBits; i++)
    {
      int probIndex = startIndex + (int)m;
      if ((uint)probIndex >= (uint)_posDecoders.Length)
        throw new InvalidOperationException("Внутренняя ошибка: выход за границы posDecoders.");

      var res = range.TryDecodeBit(ref _posDecoders[probIndex], input, ref inputOffset, out uint bit);
      if (res != LzmaRangeDecodeResult.Ok)
      {
        symbol = 0;
        return res;
      }

      m = (m << 1) | bit;
      result |= bit << i;
    }

    symbol = result;
    return LzmaRangeDecodeResult.Ok;
  }
}
