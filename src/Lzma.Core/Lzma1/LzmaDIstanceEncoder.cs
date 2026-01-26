using System.Numerics;

namespace Lzma.Core.Lzma1;

/// <summary>
/// Кодировщик «расстояния» (distance) для LZMA.
/// </summary>
/// <remarks>
/// <para>
/// В LZMA расстояние кодируется в два этапа:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     Сначала кодируем <c>posSlot</c> (6 бит) через bit tree,
///     причём модель зависит от <c>lenToPosState</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     Затем кодируем «хвост» расстояния: либо reverse bit tree,
///     либо (для очень больших расстояний) часть бит идёт «прямыми»
///     битами + align (reverse bit tree из 4 бит).
///     </description>
///   </item>
/// </list>
/// <para>
/// Этот класс — парный к <see cref="LzmaDistanceDecoder"/>.
/// Мы реализуем ровно те же модели и тот же порядок бит.
/// </para>
/// </remarks>
internal sealed class LzmaDistanceEncoder
{
  private readonly LzmaBitTreeEncoder[] _posSlotEncoders;
  private readonly LzmaBitTreeEncoder[] _posEncoders;
  private readonly LzmaBitTreeEncoder _alignEncoder;

  public LzmaDistanceEncoder()
  {
    _posSlotEncoders = new LzmaBitTreeEncoder[LzmaConstants.NumLenToPosStates];
    for (int i = 0; i < _posSlotEncoders.Length; i++)
      _posSlotEncoders[i] = new LzmaBitTreeEncoder(LzmaConstants.NumPosSlotBits);

    // Модели для posSlot 4..13 (StartPosModelIndex..EndPosModelIndex-1).
    _posEncoders = new LzmaBitTreeEncoder[LzmaConstants.NumPosModels];
    for (int i = 0; i < _posEncoders.Length; i++)
    {
      int posSlot = LzmaConstants.StartPosModelIndex + i;
      int numDirectBits = (posSlot >> 1) - 1;
      _posEncoders[i] = new LzmaBitTreeEncoder(numDirectBits);
    }

    // Align-модель (4 бита) для больших расстояний.
    _alignEncoder = new LzmaBitTreeEncoder(LzmaConstants.NumAlignBits);
  }

  /// <summary>
  /// Сбрасывает вероятностные модели в начальное состояние.
  /// </summary>
  public void Reset()
  {
    for (int i = 0; i < _posSlotEncoders.Length; i++)
      _posSlotEncoders[i].Reset();

    for (int i = 0; i < _posEncoders.Length; i++)
      _posEncoders[i].Reset();

    _alignEncoder.Reset();
  }

  /// <summary>
  /// Кодирует <paramref name="distance"/> (1..∞) при заданном <paramref name="lenToPosState"/>.
  /// </summary>
  public void EncodeDistance(LzmaRangeEncoder range, int lenToPosState, uint distance)
  {
    if ((uint)lenToPosState >= LzmaConstants.NumLenToPosStates)
      throw new ArgumentOutOfRangeException(nameof(lenToPosState));

    if (distance == 0)
      throw new ArgumentOutOfRangeException(nameof(distance), "distance в LZMA не может быть 0 (минимум 1).");

    // Внутренне LZMA кодирует pos = distance - 1.
    uint pos = distance - 1;

    int posSlot = GetPosSlot(pos);

    // 1) posSlot.
    _posSlotEncoders[lenToPosState].EncodeSymbol(range, (uint)posSlot);

    // 2) «Хвост» distance.
    if (posSlot < LzmaConstants.StartPosModelIndex)
      return;

    int numDirectBits = (posSlot >> 1) - 1;
    uint basePos = (uint)((2 | (posSlot & 1)) << numDirectBits);
    uint dist = pos - basePos;

    if (posSlot < LzmaConstants.EndPosModelIndex)
    {
      // Для posSlot 4..13 хвост кодируется reverse bit tree.
      _posEncoders[posSlot - LzmaConstants.StartPosModelIndex].EncodeReverse(range, dist);
      return;
    }

    // Для больших posSlot часть бит пишем напрямую, плюс 4 align-бита.
    int directBits = numDirectBits - LzmaConstants.NumAlignBits;
    range.EncodeDirectBits(dist >> LzmaConstants.NumAlignBits, directBits);
    _alignEncoder.EncodeReverse(range, dist & ((1u << LzmaConstants.NumAlignBits) - 1u));
  }

  private static int GetPosSlot(uint pos)
  {
    // Для pos 0..3 posSlot == pos.
    if (pos < LzmaConstants.StartPosModelIndex)
      return (int)pos;

    // Пример:
    // pos=8..11 -> numDirectBits=2, posSlotBase=(2+1)*2=6, второй бит (pos>>2)&1 определяет чёт/нечёт.
    int hi = BitOperations.Log2(pos);
    int numDirectBits = hi - 1;

    int posSlotBase = (numDirectBits + 1) << 1;
    return posSlotBase + (int)((pos >> numDirectBits) & 1u);
  }
}
