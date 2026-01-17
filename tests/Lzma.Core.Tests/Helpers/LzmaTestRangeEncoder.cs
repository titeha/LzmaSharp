using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>Минимальный RangeEncoder (только для тестов).</para>
/// <para>
/// Мы пишем его, чтобы генерировать корректный поток байт для нашего RangeDecoder.
/// Это НЕ production-код и НЕ оптимизация — это "эталонная" небольшая реализация,
/// достаточная для unit-тестов.
/// </para>
/// </summary>
internal sealed class LzmaTestRangeEncoder
{
  private const uint TopValue = 1u << 24; // 0x01000000

  private uint _range = 0xFFFFFFFF;
  private ulong _low;
  private uint _cacheSize = 1;
  private byte _cache;

  private readonly List<byte> _out = new();

  public void EncodeBit(ref ushort prob, uint bit)
  {
    uint prob32 = prob;
    uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * prob32;

    if (bit == 0)
    {
      _range = bound;
      prob = (ushort)(prob + ((LzmaConstants.BitModelTotal - prob) >> LzmaConstants.NumMoveBits));
    }
    else
    {
      _low += bound;
      _range -= bound;
      prob = (ushort)(prob - (prob >> LzmaConstants.NumMoveBits));
    }

    if (_range < TopValue)
    {
      _range <<= 8;
      ShiftLow();
    }
  }

  public byte[] Finish()
  {
    // По SDK — 5 раз делаем ShiftLow, чтобы выгрузить хвост.
    for (int i = 0; i < 5; i++)
    {
      ShiftLow();
    }

    return [.. _out];
  }

  public byte[] ToArrayAndReset()
  {
    byte[] data = Finish();
    Reset();
    return data;
  }

  public void Reset()
  {
    _range = 0xFFFFFFFF;
    _low = 0;
    _cacheSize = 1;
    _cache = 0;
    _out.Clear();
  }

  private void ShiftLow()
  {
    uint lowHi = (uint)(_low >> 32);

    if (lowHi != 0 || (uint)_low < 0xFF000000)
    {
      byte temp = _cache;
      do
      {
        _out.Add((byte)(temp + lowHi));
        temp = 0xFF;
      }
      while (--_cacheSize != 0);

      _cache = (byte)(((uint)_low) >> 24);
    }

    _cacheSize++;
    _low = (uint)_low << 8;
  }
}
