namespace Lzma.Core.Lzma1;

/// <summary>
/// Range encoder (арифметический кодер), парный к <see cref="LzmaRangeDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// LZMA (и LZMA2 внутри сжатых чанков) кодирует биты через range coder.
/// На данном шаге мы добавляем минимальную рабочую реализацию энкодера,
/// чтобы дальше можно было собирать полноценный LZMA-энкодер.
/// </para>
/// <para>
/// Важно: эта реализация сознательно «простая и понятная».
/// Оптимизации будем делать позже, когда функциональность будет закрыта тестами.
/// </para>
/// </remarks>
internal sealed class LzmaRangeEncoder
{
  // В LZMA «нормализация» происходит, когда range становится меньше 1<<24.
  // Это значение уже вынесено в константы, используем их.

  private readonly List<byte> _output = new();

  private ulong _low;
  private uint _range;

  // «Кэш» для корректной обработки переносов при записи старшего байта low.
  private byte _cache;
  private uint _cacheSize;

  public LzmaRangeEncoder()
  {
    Reset();
  }

  /// <summary>
  /// Сбрасывает состояние энкодера и очищает накопленный выход.
  /// </summary>
  public void Reset()
  {
    _output.Clear();

    _low = 0;
    _range = uint.MaxValue; // 0xFFFF_FFFF

    _cache = 0;
    _cacheSize = 1;
  }

  /// <summary>
  /// Возвращает все закодированные байты (копией).
  /// </summary>
  public byte[] ToArray() => [.. _output];

  /// <summary>
  /// Кодирует один бит с вероятностной моделью LZMA (bit model).
  /// </summary>
  /// <param name="prob">
  /// Вероятность (0..2048). Значение обновляется внутри метода так же,
  /// как это делает декодер.
  /// </param>
  /// <param name="bit">Бит (0 или 1).</param>
  public void EncodeBit(ref ushort prob, uint bit)
  {
    if (bit > 1)
      throw new ArgumentOutOfRangeException(nameof(bit), "Бит должен быть 0 или 1.");

    uint p = prob;

    // bound = (range >> 11) * p
    uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * p;

    if (bit == 0)
    {
      _range = bound;
      p += (LzmaConstants.BitModelTotal - p) >> LzmaConstants.NumMoveBits;
    }
    else
    {
      _low += bound;
      _range -= bound;
      p -= p >> LzmaConstants.NumMoveBits;
    }

    prob = (ushort)p;

    // Нормализация.
    while (_range < LzmaConstants.RangeTopValue)
    {
      _range <<= 8;
      ShiftLow();
    }
  }

  /// <summary>
  /// Кодирует <paramref name="numBits"/> «прямых» бит без вероятностной модели.
  /// </summary>
  /// <remarks>
  /// В LZMA это используется при кодировании больших расстояний (distance/pos),
  /// когда часть бит пишется напрямую (без bit model).
  /// Парный метод на стороне декодера — <see cref="LzmaRangeDecoder.TryDecodeDirectBits"/>.
  /// </remarks>
  public void EncodeDirectBits(uint value, int numBits)
  {
    if (numBits < 0 || numBits > 32)
      throw new ArgumentOutOfRangeException(nameof(numBits), "numBits должен быть в диапазоне 0..32.");

    // Пишем биты от старшего к младшему.
    for (int i = numBits - 1; i >= 0; i--)
    {
      _range >>= 1;

      uint bit = (value >> i) & 1u;
      if (bit != 0)
        _low += _range;

      if (_range < LzmaConstants.RangeTopValue)
      {
        _range <<= 8;
        ShiftLow();
      }
    }
  }

  /// <summary>
  /// «Финализирует» поток range coder'а: дописывает хвостовые байты.
  /// </summary>
  /// <remarks>
  /// В классическом LZMA после окончания кодирования делают 5 раз ShiftLow().
  /// Это гарантирует, что декодер сможет прочитать начальные 5 байт Code.
  /// </remarks>
  public void Flush()
  {
    for (int i = 0; i < 5; i++)
      ShiftLow();
  }

  private void ShiftLow()
  {
    uint lowHi = (uint)(_low >> 32);

    if (lowHi != 0 || (uint)_low < 0xFF00_0000)
    {
      byte temp = _cache;

      // Выгружаем «кэш»; если был перенос (lowHi != 0),
      // он протаскивается через цепочку 0xFF.
      do
      {
        _output.Add((byte)(temp + lowHi));
        temp = 0xFF;
      }
      while (--_cacheSize != 0);

      _cache = (byte)(((uint)_low) >> 24);
    }

    _cacheSize++;
    _low = ((uint)_low) << 8;
  }
}
