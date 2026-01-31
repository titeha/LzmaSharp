using System.Diagnostics;

namespace Lzma.Core.Lzma1;

/// <summary>
/// Энкодер диапазона (range coder) для LZMA.
///
/// Это «низкоуровневый» компонент, который кодирует биты в байтовый поток.
/// </summary>
internal sealed class LzmaRangeEncoder
{
  private const uint _topValue = 1u << 24;

  // Замечание по реализации:
  // Мы храним выход в List<byte>. Для инкрементального режима (стриминг) добавили
  // простую «дренажную» механику (DrainTo), чтобы можно было постепенно вычитывать
  // готовые байты, не копируя каждый раз весь массив.
  private readonly List<byte> _output = new();
  private int _readPos;

  private uint _range;
  private ulong _low;

  private byte _cache;
  private uint _cacheSize;

  public LzmaRangeEncoder() => Reset();

  /// <summary>
  /// Сбрасывает состояние энкодера и очищает накопленный вывод.
  /// </summary>
  public void Reset()
  {
    _output.Clear();
    _readPos = 0;

    _range = uint.MaxValue;
    _low = 0;

    _cache = 0;
    _cacheSize = 1;
  }

  /// <summary>
  /// Сколько байт готово к выдаче наружу (ещё не было «сдренировано»).
  /// </summary>
  internal int PendingBytes => _output.Count - _readPos;

  /// <summary>
  /// Пытается скопировать часть накопленного вывода в <paramref name="destination"/>.
  /// Возвращает, сколько байт записали.
  /// </summary>
  internal int DrainTo(Span<byte> destination)
  {
    if (destination.Length == 0)
      return 0;

    int available = PendingBytes;
    if (available <= 0)
      return 0;

    int toCopy = Math.Min(available, destination.Length);

    // List<byte> не даёт Span напрямую без unsafe/CollectionsMarshal.
    // Здесь важнее простота; оптимизацию сделаем позже.
    for (int i = 0; i < toCopy; i++)
      destination[i] = _output[_readPos + i];

    _readPos += toCopy;

    // Если вычитали всё – очищаем полностью (быстро).
    if (_readPos == _output.Count)
    {
      _output.Clear();
      _readPos = 0;
      return toCopy;
    }

    // Периодическая компактация, чтобы список не рос бесконечно.
    // Порог подобран «на глаз»; оптимизируем позже.
    if (_readPos > 4096 && _readPos > (_output.Count / 2))
    {
      _output.RemoveRange(0, _readPos);
      _readPos = 0;
    }

    return toCopy;
  }

  /// <summary>
  /// Возвращает (копию) всех байт, которые ещё не были вычитаны через <see cref="DrainTo"/>.
  /// </summary>
  public byte[] ToArray()
  {
    int available = PendingBytes;
    if (available <= 0)
      return Array.Empty<byte>();

    var arr = new byte[available];
    for (int i = 0; i < available; i++)
      arr[i] = _output[_readPos + i];

    return arr;
  }

  /// <summary>
  /// Заканчивает поток (дописать оставшиеся байты).
  /// </summary>
  public void Flush()
  {
    for (int i = 0; i < 5; i++)
      ShiftLow();
  }

  public void EncodeBit(ref ushort prob, uint symbol)
  {
    uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * prob;

    if (symbol == 0)
    {
      _range = bound;
      prob += (ushort)((LzmaConstants.BitModelTotal - prob) >> LzmaConstants.NumMoveBits);
    }
    else
    {
      _low += bound;
      _range -= bound;
      prob -= (ushort)(prob >> LzmaConstants.NumMoveBits);
    }

    if (_range < _topValue)
    {
      _range <<= 8;
      ShiftLow();
    }
  }

  public void EncodeDirectBits(uint value, int numTotalBits)
  {
    for (int i = numTotalBits - 1; i >= 0; i--)
    {
      _range >>= 1;
      if (((value >> i) & 1) != 0)
        _low += _range;

      if (_range < _topValue)
      {
        _range <<= 8;
        ShiftLow();
      }
    }
  }

  private void ShiftLow()
  {
    uint lowHi = (uint)(_low >> 32);
    if (lowHi != 0 || _low < 0xFF000000UL)
    {
      byte temp = _cache;
      do
      {
        _output.Add((byte)(temp + lowHi));
        temp = 0xFF;
      }
      while (--_cacheSize != 0);

      _cache = (byte)(_low >> 24);
    }

    _cacheSize++;
    _low = (_low & 0x00FFFFFFUL) << 8;
  }

#if DEBUG
  // Небольшая страховка для отладки: убедимся, что диапазон не «ломается».
  private void AssertInvariant()
  {
    Debug.Assert(_range != 0);
  }
#endif
}
