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
  // Выход храним в растущем byte[] (а не List<byte>): запись в горячем ShiftLow — это
  // индексная установка, а ToArray/DrainTo — быстрое копирование Span. Для инкрементального
  // режима (стриминг) есть «дренажная» механика (DrainTo) с переиспользованием буфера.
  private byte[] _output = new byte[256];
  private int _count;
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
    _count = 0;
    _readPos = 0;

    _range = uint.MaxValue;
    _low = 0;

    _cache = 0;
    _cacheSize = 1;
  }

  /// <summary>
  /// Сколько байт готово к выдаче наружу (ещё не было «сдренировано»).
  /// </summary>
  internal int PendingBytes => _count - _readPos;

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

    _output.AsSpan(_readPos, toCopy).CopyTo(destination);
    _readPos += toCopy;

    // Если вычитали всё – сбрасываем позиции (буфер переиспользуется).
    if (_readPos == _count)
    {
      _count = 0;
      _readPos = 0;
      return toCopy;
    }

    // Периодическая компактация, чтобы непрочитанный «хвост» не уезжал всё дальше.
    // Порог подобран «на глаз».
    if (_readPos > 4096 && _readPos > (_count / 2))
    {
      Array.Copy(_output, _readPos, _output, 0, _count - _readPos);
      _count -= _readPos;
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
      return [];

    return _output.AsSpan(_readPos, available).ToArray();
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
        WriteByte((byte)(temp + lowHi));
        temp = 0xFF;
      }
      while (--_cacheSize != 0);

      _cache = (byte)(_low >> 24);
    }

    _cacheSize++;
    _low = (_low & 0x00FFFFFFUL) << 8;
  }

  private void WriteByte(byte b)
  {
    if (_count == _output.Length)
      Array.Resize(ref _output, _output.Length * 2);

    _output[_count++] = b;
  }

#if DEBUG
  // Небольшая страховка для отладки: убедимся, что диапазон не «ломается».
  private void AssertInvariant()
  {
    Debug.Assert(_range != 0);
  }
#endif
}
