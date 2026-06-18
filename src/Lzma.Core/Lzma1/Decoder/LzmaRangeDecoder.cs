namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодер range coder (арифметический кодер), используемый в LZMA/LZMA2.</para>
/// <para>
/// Что это такое:
/// - LZMA кодирует решения (0/1) с помощью range coder'а. Он поддерживает
///   два 32-битных регистра: Range и Code.
/// - «Вероятности» (prob) — это 11-битные значения (0..2048), которые
///   адаптивно обновляются после каждого декодированного бита.
/// </para>
/// <para>
/// Почему здесь есть режим NeedMoreInput:
/// - В потоковом декодере мы не можем гарантировать, что очередной байт входа
///   уже доступен. Range coder иногда требует «нормализацию» (подтянуть один
///   байт из входа), когда Range становится слишком маленьким.
/// - Этот класс умеет вернуть NeedMoreInput, если байта пока нет.
/// </para>
/// <para>
/// Важно:
/// - Инициализация всегда читает ровно 5 байт (как в оригинальном SDK).
/// - Переполнения uint при сдвигах — это нормальная часть алгоритма.
/// </para>
/// </summary>
public struct LzmaRangeDecoder
{
  /// <summary>
  /// Порог для нормализации range coder'а.
  /// В классическом LZMA это 1 << 24.
  /// </summary>
  public const uint TopValue = LzmaConstants.RangeTopValue;

  // Текущее «окно» и «код» range coder'а.
  private uint _range;
  private uint _code;

  // Для Init() нужно прочитать 5 байт.
  private int _initBytesRemaining;

  /// <summary>
  /// Текущее значение Range (для отладки/тестов).
  /// </summary>
  public readonly uint Range => _range;

  /// <summary>
  /// Текущее значение Code (для отладки/тестов).
  /// </summary>
  public readonly uint Code => _code;

  /// <summary>
  /// Сколько байт ещё нужно прочитать для завершения инициализации (0..5).
  /// </summary>
  public readonly int InitBytesRemaining => _initBytesRemaining;

  /// <summary>
  /// True, если инициализация (5 байт) полностью завершена.
  /// </summary>
  public readonly bool IsInitialized => _initBytesRemaining == 0;

  /// <summary>
  /// Создаёт range decoder в состоянии «после Reset()».
  /// </summary>
  public LzmaRangeDecoder()
  {
    _range = 0xFFFF_FFFFu;
    _code = 0u;
    _initBytesRemaining = 5;
  }

  /// <summary>
  /// Сбрасывает состояние range decoder'а.
  /// </summary>
  public void Reset()
  {
    _range = 0xFFFF_FFFFu;
    _code = 0u;
    _initBytesRemaining = 5;
  }

  /// <summary>
  /// <para>Пытается дочитать 5 байт инициализации (Init()) из <paramref name="input"/>.</para>
  /// <para>
  /// Примечание:
  /// - Метод «частично-потребляющий»: если вход обрывается, он вернёт NeedMoreInput,
  ///   но уже прочитанные байты останутся учтёнными во внутреннем состоянии.
  /// </para>
  /// </summary>
  public LzmaRangeInitResult TryInitialize(ReadOnlySpan<byte> input, ref int offset)
  {
    if (_initBytesRemaining == 0)
      return LzmaRangeInitResult.Ok;

    // Читаем доступные байты до тех пор, пока не наберём 5.
    while (_initBytesRemaining > 0 && offset < input.Length)
    {
      _code = (_code << 8) | input[offset++];
      _initBytesRemaining--;
    }

    return _initBytesRemaining == 0
        ? LzmaRangeInitResult.Ok
        : LzmaRangeInitResult.NeedMoreInput;
  }

  /// <summary>
  /// <para>Нормализует состояние range coder'а (если нужно).</para>
  /// <para>
  /// В оригинальном алгоритме нормализация выполняется так:
  /// if (Range < TopValue) { Range <<= 8; Code = (Code << 8) | ReadByte(); }
  ///
  /// Мы делаем то же самое, но в потоковом режиме можем сказать NeedMoreInput,
  /// если очередного байта пока нет.
  /// </para>
  /// </summary>
  private LzmaRangeDecodeResult EnsureNormalized(ReadOnlySpan<byte> input, ref int offset)
  {
    if (_range >= TopValue)
      return LzmaRangeDecodeResult.Ok;

    if (offset >= input.Length)
      return LzmaRangeDecodeResult.NeedMoreInput;

    _range <<= 8;
    _code = (_code << 8) | input[offset++];
    return LzmaRangeDecodeResult.Ok;
  }

  /// <summary>
  /// <para>Декодирует 1 бит с использованием адаптивной вероятности <paramref name="prob"/>.</para>
  /// <para>
  /// Контракт потокового режима:
  /// - Если вернули NeedMoreInput, декодирование НЕ выполнено (prob/Range/Code не менялись),
  ///   потому что нам не хватило байта для нормализации до декодирования.
  /// </para>
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeBit(
      ref ushort prob,
      ReadOnlySpan<byte> input,
      ref int offset,
      out uint bit)
  {
    bit = 0;

    if (_initBytesRemaining != 0)
      throw new InvalidOperationException("Range decoder не инициализирован: сначала вызови TryInitialize().");

    // Перед любым декодированием мы обязаны иметь нормализованный Range.
    var norm = EnsureNormalized(input, ref offset);
    if (norm != LzmaRangeDecodeResult.Ok)
      return norm;

    // Стандартная формула LZMA:
    // bound = (range >> kNumBitModelTotalBits) * prob;
    uint bound = (_range >> LzmaConstants.NumBitModelTotalBits) * prob;

    if (_code < bound)
    {
      _range = bound;

      // prob += (kBitModelTotal - prob) >> kNumMoveBits;
      prob = (ushort)(prob + ((LzmaConstants.BitModelTotal - prob) >> LzmaConstants.NumMoveBits));
      bit = 0;
    }
    else
    {
      _range -= bound;
      _code -= bound;

      // prob -= prob >> kNumMoveBits;
      prob = (ushort)(prob - (prob >> LzmaConstants.NumMoveBits));
      bit = 1;
    }

    return LzmaRangeDecodeResult.Ok;
  }

  /// <summary>
  /// <para>Декодирует указанное количество «прямых» бит (без вероятностей).</para>
  /// <para>Это используется в LZMA для чтения некоторых чисел без вероятностной модели.</para>
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeDirectBits(
      int numBits,
      ReadOnlySpan<byte> input,
      ref int offset,
      out uint value)
  {
    if ((uint)numBits > 32)
      throw new ArgumentOutOfRangeException(nameof(numBits), "numBits должен быть в диапазоне 0..32.");

    if (_initBytesRemaining != 0)
      throw new InvalidOperationException("Range decoder не инициализирован: сначала вызови TryInitialize().");

    value = 0;

    for (int i = 0; i < numBits; i++)
    {
      // Перед каждой операцией обеспечиваем нормализацию.
      var norm = EnsureNormalized(input, ref offset);
      if (norm != LzmaRangeDecodeResult.Ok)
        return norm;

      _range >>= 1;
      uint t = (_code - _range) >> 31;
      _code -= _range & (t - 1);
      value = (value << 1) | (1 - t);
    }

    return LzmaRangeDecodeResult.Ok;
  }
}

/// <summary>
/// Результат попытки инициализации range decoder'а (чтение 5 байт Code).
/// </summary>
public enum LzmaRangeInitResult
{
  Ok,
  NeedMoreInput,
  InvalidData
}

/// <summary>
/// Результат попытки декодирования (бит/прямые биты).
/// </summary>
public enum LzmaRangeDecodeResult
{
  Ok,
  NeedMoreInput,
}
