namespace Lzma.Core.Deflate;

/// <summary>
/// Результат декодирования DEFLATE-потока.
/// </summary>
public enum DeflateDecodeResult
{
  /// <summary>Поток успешно декодирован.</summary>
  Ok = 0,

  /// <summary>Поток повреждён или не соответствует формату DEFLATE.</summary>
  InvalidData = 1,
}

/// <summary>
/// <para>Управляемый декодер DEFLATE (RFC 1951), без unsafe.</para>
/// <para>
/// Логика следует эталонному разборщику puff.c (Mark Adler): канонические коды Хаффмана,
/// блоки stored / fixed / dynamic, back-reference копирование из уже распакованного окна.
/// Это первый шаг к собственной реализации Deflate взамен внешних путей.
/// </para>
/// </summary>
public static class DeflateDecoder
{
  private const int MaxBits = 15;          // максимальная длина кода Хаффмана
  private const int MaxLitLenCodes = 286;  // 0..285 (литералы + длины + конец блока)
  private const int MaxDistCodes = 30;     // 0..29
  private const int FixedLitLenCodes = 288;

  // База и доп. биты для длин совпадений (коды 257..285).
  private static readonly short[] LengthBase =
  [
      3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
      35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
  ];

  private static readonly short[] LengthExtra =
  [
      0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
      3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
  ];

  // База и доп. биты для дистанций (коды 0..29).
  private static readonly int[] DistBase =
  [
      1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
      257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
      8193, 12289, 16385, 24577
  ];

  private static readonly short[] DistExtra =
  [
      0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
      7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
  ];

  // Deflate64 (Enhanced Deflate): добавлены дистанционные коды 30 и 31 (по 14 доп. бит),
  // что расширяет максимальную дистанцию до 64 КБ.
  private static readonly int[] Dist64Base =
  [
      1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
      257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
      8193, 12289, 16385, 24577, 32769, 49153
  ];

  private static readonly short[] Dist64Extra =
  [
      0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
      7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14
  ];

  // Порядок чтения длин кодов для code-length алфавита (dynamic-блок).
  private static readonly int[] CodeLengthOrder =
  [
      16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
  ];

  /// <summary>
  /// Декодирует raw DEFLATE-поток в <paramref name="output"/>.
  /// </summary>
  /// <param name="input">Сжатые данные.</param>
  /// <param name="output">Буфер вывода ожидаемого распакованного размера.</param>
  /// <param name="bytesConsumed">Сколько байт входа прочитано.</param>
  /// <param name="bytesWritten">Сколько байт записано в вывод.</param>
  public static DeflateDecodeResult Decode(
      ReadOnlySpan<byte> input,
      Span<byte> output,
      out int bytesConsumed,
      out int bytesWritten)
      => Decode(input, output, deflate64: false, out bytesConsumed, out bytesWritten);

  /// <summary>
  /// Декодирует raw DEFLATE или Deflate64-поток в <paramref name="output"/>.
  /// </summary>
  /// <param name="deflate64">
  /// <see langword="true"/> — режим Deflate64 (Enhanced Deflate): код длины 285 имеет 16 доп.
  /// бит (база 3), доступны дистанционные коды 30/31 и окно до 64 КБ.
  /// </param>
  public static DeflateDecodeResult Decode(
      ReadOnlySpan<byte> input,
      Span<byte> output,
      bool deflate64,
      out int bytesConsumed,
      out int bytesWritten)
  {
    var state = new Inflater(input, output, deflate64);

    try
    {
      state.Inflate();
    }
    catch (InvalidDeflateException)
    {
      bytesConsumed = 0;
      bytesWritten = 0;
      return DeflateDecodeResult.InvalidData;
    }

    bytesConsumed = state.InputPosition;
    bytesWritten = state.OutputPosition;
    return DeflateDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует raw DEFLATE/Deflate64-поток, ПОТОКОВО записывая выход в <paramref name="output"/>.
  /// </summary>
  /// <remarks>
  /// Вход целиком в памяти (≤2 ГиБ), но распакованные данные могут быть заметно больше: back-reference
  /// разрешается по кольцевому окну истории (см. <see cref="WindowInflater"/>), а не по полному
  /// выходному буферу. Это снимает лимит 2 ГиБ на РАСПАКОВАННЫЙ размер одного члена (типичный случай —
  /// текст/логи: сжатый член &lt; 2 ГиБ, распакованный — много больше).
  /// </remarks>
  /// <param name="input">Сжатые данные (весь член целиком).</param>
  /// <param name="output">Поток для потоковой записи распакованных данных.</param>
  /// <param name="deflate64">Режим Deflate64 (окно до 64 КБ, коды дистанций 30/31).</param>
  /// <param name="bytesWritten">Сколько байт записано в вывод.</param>
  public static DeflateDecodeResult Decode(
      ReadOnlySpan<byte> input,
      Stream output,
      bool deflate64,
      out long bytesWritten)
  {
    var state = new WindowInflater(input, output, deflate64);

    try
    {
      state.Inflate();
      state.Flush();
    }
    catch (InvalidDeflateException)
    {
      bytesWritten = state.BytesWritten;
      return DeflateDecodeResult.InvalidData;
    }

    bytesWritten = state.BytesWritten;
    return DeflateDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует raw DEFLATE/Deflate64-поток, читая ВХОД из <paramref name="input"/> порциями и записывая
  /// ВЫХОД в <paramref name="output"/> — ни сжатые, ни распакованные данные целиком в памяти не держатся.
  /// </summary>
  /// <remarks>
  /// Снимает лимит 2 ГиБ на РАЗМЕР одного DEFLATE-члена в обе стороны (для ZIP-членов &gt; 2 ГиБ).
  /// Бит-ридер тянет байты из потока по мере надобности (не более <paramref name="inputLength"/> байт),
  /// back-reference — по кольцевому окну истории.
  /// </remarks>
  /// <param name="input">Поток сжатых данных.</param>
  /// <param name="inputLength">Сколько байт члена читать из потока (границы DEFLATE-члена).</param>
  /// <param name="output">Поток для потоковой записи распакованных данных.</param>
  /// <param name="deflate64">Режим Deflate64 (окно до 64 КБ, коды дистанций 30/31).</param>
  /// <param name="bytesWritten">Сколько байт записано в вывод.</param>
  public static DeflateDecodeResult Decode(
      Stream input,
      long inputLength,
      Stream output,
      bool deflate64,
      out long bytesWritten)
  {
    var state = new StreamInflater(input, inputLength, output, deflate64);

    try
    {
      state.Inflate();
      state.Flush();
    }
    catch (InvalidDeflateException)
    {
      bytesWritten = state.BytesWritten;
      return DeflateDecodeResult.InvalidData;
    }

    bytesWritten = state.BytesWritten;
    return DeflateDecodeResult.Ok;
  }

  /// <summary>
  /// Внутренний сигнал о повреждённом потоке (заменяет longjmp из puff.c).
  /// </summary>
  private sealed class InvalidDeflateException : Exception;

  /// <summary>
  /// Канонические таблицы Хаффмана: count[len] — количество кодов длины len,
  /// symbol[] — символы, упорядоченные по (длина кода, значение символа).
  /// </summary>
  private sealed class HuffmanTable
  {
    public readonly short[] Count = new short[MaxBits + 1];
    public readonly short[] Symbol;

    public HuffmanTable(int symbolCount) => Symbol = new short[symbolCount];
  }

  private ref struct Inflater
  {
    private readonly ReadOnlySpan<byte> _input;
    private readonly Span<byte> _output;
    private readonly bool _deflate64;
    private readonly int[] _distBase;
    private readonly short[] _distExtra;

    private int _inPos;
    private int _outPos;

    private int _bitBuffer;
    private int _bitCount;

    public Inflater(ReadOnlySpan<byte> input, Span<byte> output, bool deflate64)
    {
      _input = input;
      _output = output;
      _deflate64 = deflate64;
      _distBase = deflate64 ? Dist64Base : DistBase;
      _distExtra = deflate64 ? Dist64Extra : DistExtra;
      _inPos = 0;
      _outPos = 0;
      _bitBuffer = 0;
      _bitCount = 0;
    }

    public readonly int InputPosition => _inPos;
    public readonly int OutputPosition => _outPos;

    public void Inflate()
    {
      bool last;

      do
      {
        last = ReadBits(1) == 1;
        int type = ReadBits(2);

        switch (type)
        {
          case 0:
            DecodeStoredBlock();
            break;
          case 1:
            DecodeBlock(BuildFixedLitLenTable(), BuildFixedDistTable());
            break;
          case 2:
            DecodeDynamicBlock();
            break;
          default:
            throw new InvalidDeflateException();
        }
      }
      while (!last);
    }

    /// <summary>
    /// Читает <paramref name="need"/> бит в порядке «младший бит — первый» (как в DEFLATE).
    /// </summary>
    private int ReadBits(int need)
    {
      long value = _bitBuffer;

      while (_bitCount < need)
      {
        if (_inPos >= _input.Length)
          throw new InvalidDeflateException();

        value |= (long)_input[_inPos++] << _bitCount;
        _bitCount += 8;
      }

      _bitBuffer = (int)(value >> need);
      _bitCount -= need;

      return (int)(value & ((1L << need) - 1));
    }

    private void DecodeStoredBlock()
    {
      // Stored-блок выровнен по байту: остаток битового буфера отбрасываем.
      _bitBuffer = 0;
      _bitCount = 0;

      if (_inPos + 4 > _input.Length)
        throw new InvalidDeflateException();

      int len = _input[_inPos] | (_input[_inPos + 1] << 8);
      int nlen = _input[_inPos + 2] | (_input[_inPos + 3] << 8);
      _inPos += 4;

      // LEN и ~LEN должны быть комплементарны.
      if ((len ^ 0xFFFF) != nlen)
        throw new InvalidDeflateException();

      if (_inPos + len > _input.Length)
        throw new InvalidDeflateException();

      if (_outPos + len > _output.Length)
        throw new InvalidDeflateException();

      _input.Slice(_inPos, len).CopyTo(_output.Slice(_outPos, len));
      _inPos += len;
      _outPos += len;
    }

    private void DecodeDynamicBlock()
    {
      int hlit = ReadBits(5) + 257;
      int hdist = ReadBits(5) + 1;
      int hclen = ReadBits(4) + 4;

      int maxDistCodes = _deflate64 ? 32 : MaxDistCodes;
      if (hlit > MaxLitLenCodes || hdist > maxDistCodes)
        throw new InvalidDeflateException();

      // Длины кодов для code-length алфавита (19 символов).
      short[] codeLengthLengths = new short[19];
      for (int i = 0; i < hclen; i++)
        codeLengthLengths[CodeLengthOrder[i]] = (short)ReadBits(3);

      HuffmanTable codeLengthTable = BuildTable(codeLengthLengths, 19);

      // Распаковываем длины кодов для lit/len + dist алфавитов.
      short[] lengths = new short[hlit + hdist];
      int index = 0;

      while (index < lengths.Length)
      {
        int symbol = Decode(codeLengthTable);

        if (symbol < 16)
        {
          lengths[index++] = (short)symbol;
          continue;
        }

        int repeatValue = 0;
        int repeatCount;

        switch (symbol)
        {
          case 16:
            // Повтор предыдущей длины 3..6 раз.
            if (index == 0)
              throw new InvalidDeflateException();

            repeatValue = lengths[index - 1];
            repeatCount = 3 + ReadBits(2);
            break;
          case 17:
            // Повтор нуля 3..10 раз.
            repeatCount = 3 + ReadBits(3);
            break;
          case 18:
            // Повтор нуля 11..138 раз.
            repeatCount = 11 + ReadBits(7);
            break;
          default:
            throw new InvalidDeflateException();
        }

        if (index + repeatCount > lengths.Length)
          throw new InvalidDeflateException();

        for (int i = 0; i < repeatCount; i++)
          lengths[index++] = (short)repeatValue;
      }

      // Конец блока (символ 256) обязан иметь ненулевую длину кода.
      if (lengths[256] == 0)
        throw new InvalidDeflateException();

      short[] litLenLengths = lengths[..hlit];
      short[] distLengths = lengths[hlit..];

      HuffmanTable litLenTable = BuildTable(litLenLengths, hlit);
      HuffmanTable distTable = BuildTable(distLengths, hdist);

      DecodeBlock(litLenTable, distTable);
    }

    /// <summary>
    /// Декодирует тело блока (последовательность литералов и back-reference) до символа 256.
    /// </summary>
    private void DecodeBlock(HuffmanTable litLenTable, HuffmanTable distTable)
    {
      while (true)
      {
        int symbol = Decode(litLenTable);

        if (symbol == 256)
          return;

        if (symbol < 256)
        {
          if (_outPos >= _output.Length)
            throw new InvalidDeflateException();

          _output[_outPos++] = (byte)symbol;
          continue;
        }

        // Совпадение: длина + дистанция.
        symbol -= 257;
        if (symbol >= LengthBase.Length)
          throw new InvalidDeflateException();

        // Deflate64: код длины 285 (symbol == 28) переопределён на базу 3 + 16 доп. бит.
        int length = _deflate64 && symbol == 28
            ? 3 + ReadBits(16)
            : LengthBase[symbol] + ReadBits(LengthExtra[symbol]);

        int distSymbol = Decode(distTable);
        if (distSymbol >= _distBase.Length)
          throw new InvalidDeflateException();

        int distance = _distBase[distSymbol] + ReadBits(_distExtra[distSymbol]);

        if (distance > _outPos)
          throw new InvalidDeflateException();

        if (_outPos + length > _output.Length)
          throw new InvalidDeflateException();

        int source = _outPos - distance;
        for (int i = 0; i < length; i++)
          _output[_outPos++] = _output[source++];
      }
    }

    /// <summary>
    /// Декодирует один символ по канонической таблице Хаффмана (биты читаются по одному,
    /// первый прочитанный — старший бит кода).
    /// </summary>
    private int Decode(HuffmanTable table)
    {
      int code = 0;
      int first = 0;
      int index = 0;

      for (int len = 1; len <= MaxBits; len++)
      {
        code |= ReadBits(1);

        int count = table.Count[len];
        if (code - first < count)
          return table.Symbol[index + (code - first)];

        index += count;
        first += count;
        first <<= 1;
        code <<= 1;
      }

      throw new InvalidDeflateException();
    }
  }

  /// <summary>
  /// Потоковый инфлейтер: разбор идентичен <see cref="Inflater"/>, но выход не в один буфер, а
  /// в <see cref="Stream"/> через кольцевое окно истории. Back-reference берётся из окна, поэтому
  /// распакованный размер не ограничен памятью (окно &gt; максимальной дистанции DEFLATE/Deflate64).
  /// </summary>
  private ref struct WindowInflater
  {
    // Окно должно быть СТРОГО больше максимальной дистанции (Deflate — 32 КБ, Deflate64 — 64 КБ),
    // иначе позиция «pos - distance» столкнётся с текущей. 128 КБ (степень двойки) с запасом.
    private const int WindowSize = 1 << 17;
    private const int WindowMask = WindowSize - 1;
    private const int OutBufferSize = 1 << 16;

    private readonly ReadOnlySpan<byte> _input;
    private readonly Stream _output;
    private readonly bool _deflate64;
    private readonly int[] _distBase;
    private readonly short[] _distExtra;
    private readonly byte[] _window;
    private readonly byte[] _outBuffer;

    private int _inPos;
    private int _bitBuffer;
    private int _bitCount;
    private int _windowPos;
    private int _outBufPos;
    private long _written;

    public WindowInflater(ReadOnlySpan<byte> input, Stream output, bool deflate64)
    {
      _input = input;
      _output = output;
      _deflate64 = deflate64;
      _distBase = deflate64 ? Dist64Base : DistBase;
      _distExtra = deflate64 ? Dist64Extra : DistExtra;
      _window = new byte[WindowSize];
      _outBuffer = new byte[OutBufferSize];
      _inPos = 0;
      _bitBuffer = 0;
      _bitCount = 0;
      _windowPos = 0;
      _outBufPos = 0;
      _written = 0;
    }

    public readonly long BytesWritten => _written;

    public void Inflate()
    {
      bool last;

      do
      {
        last = ReadBits(1) == 1;
        int type = ReadBits(2);

        switch (type)
        {
          case 0:
            DecodeStoredBlock();
            break;
          case 1:
            DecodeBlock(BuildFixedLitLenTable(), BuildFixedDistTable());
            break;
          case 2:
            DecodeDynamicBlock();
            break;
          default:
            throw new InvalidDeflateException();
        }
      }
      while (!last);
    }

    // Дописывает остаток выходного буфера в поток (вызывать после успешного Inflate).
    public void Flush()
    {
      if (_outBufPos > 0)
      {
        _output.Write(_outBuffer, 0, _outBufPos);
        _outBufPos = 0;
      }
    }

    // Пишет один байт в окно истории и в выходной буфер (сбрасывая его в поток по заполнении).
    private void Emit(byte value)
    {
      _window[_windowPos] = value;
      _windowPos = (_windowPos + 1) & WindowMask;

      _outBuffer[_outBufPos++] = value;
      if (_outBufPos == OutBufferSize)
      {
        _output.Write(_outBuffer, 0, OutBufferSize);
        _outBufPos = 0;
      }

      _written++;
    }

    private int ReadBits(int need)
    {
      long value = _bitBuffer;

      while (_bitCount < need)
      {
        if (_inPos >= _input.Length)
          throw new InvalidDeflateException();

        value |= (long)_input[_inPos++] << _bitCount;
        _bitCount += 8;
      }

      _bitBuffer = (int)(value >> need);
      _bitCount -= need;

      return (int)(value & ((1L << need) - 1));
    }

    private void DecodeStoredBlock()
    {
      _bitBuffer = 0;
      _bitCount = 0;

      if (_inPos + 4 > _input.Length)
        throw new InvalidDeflateException();

      int len = _input[_inPos] | (_input[_inPos + 1] << 8);
      int nlen = _input[_inPos + 2] | (_input[_inPos + 3] << 8);
      _inPos += 4;

      if ((len ^ 0xFFFF) != nlen)
        throw new InvalidDeflateException();

      if (_inPos + len > _input.Length)
        throw new InvalidDeflateException();

      for (int i = 0; i < len; i++)
        Emit(_input[_inPos++]);
    }

    private void DecodeDynamicBlock()
    {
      int hlit = ReadBits(5) + 257;
      int hdist = ReadBits(5) + 1;
      int hclen = ReadBits(4) + 4;

      int maxDistCodes = _deflate64 ? 32 : MaxDistCodes;
      if (hlit > MaxLitLenCodes || hdist > maxDistCodes)
        throw new InvalidDeflateException();

      short[] codeLengthLengths = new short[19];
      for (int i = 0; i < hclen; i++)
        codeLengthLengths[CodeLengthOrder[i]] = (short)ReadBits(3);

      HuffmanTable codeLengthTable = BuildTable(codeLengthLengths, 19);

      short[] lengths = new short[hlit + hdist];
      int index = 0;

      while (index < lengths.Length)
      {
        int symbol = Decode(codeLengthTable);

        if (symbol < 16)
        {
          lengths[index++] = (short)symbol;
          continue;
        }

        int repeatValue = 0;
        int repeatCount;

        switch (symbol)
        {
          case 16:
            if (index == 0)
              throw new InvalidDeflateException();

            repeatValue = lengths[index - 1];
            repeatCount = 3 + ReadBits(2);
            break;
          case 17:
            repeatCount = 3 + ReadBits(3);
            break;
          case 18:
            repeatCount = 11 + ReadBits(7);
            break;
          default:
            throw new InvalidDeflateException();
        }

        if (index + repeatCount > lengths.Length)
          throw new InvalidDeflateException();

        for (int i = 0; i < repeatCount; i++)
          lengths[index++] = (short)repeatValue;
      }

      if (lengths[256] == 0)
        throw new InvalidDeflateException();

      short[] litLenLengths = lengths[..hlit];
      short[] distLengths = lengths[hlit..];

      HuffmanTable litLenTable = BuildTable(litLenLengths, hlit);
      HuffmanTable distTable = BuildTable(distLengths, hdist);

      DecodeBlock(litLenTable, distTable);
    }

    private void DecodeBlock(HuffmanTable litLenTable, HuffmanTable distTable)
    {
      while (true)
      {
        int symbol = Decode(litLenTable);

        if (symbol == 256)
          return;

        if (symbol < 256)
        {
          Emit((byte)symbol);
          continue;
        }

        symbol -= 257;
        if (symbol >= LengthBase.Length)
          throw new InvalidDeflateException();

        int length = _deflate64 && symbol == 28
            ? 3 + ReadBits(16)
            : LengthBase[symbol] + ReadBits(LengthExtra[symbol]);

        int distSymbol = Decode(distTable);
        if (distSymbol >= _distBase.Length)
          throw new InvalidDeflateException();

        int distance = _distBase[distSymbol] + ReadBits(_distExtra[distSymbol]);

        if (distance > _written)
          throw new InvalidDeflateException();

        // Копирование из окна: _windowPos сдвигается внутри Emit, поэтому пересчитываем источник
        // каждый байт — так корректно работают перекрывающиеся совпадения (distance < length).
        for (int i = 0; i < length; i++)
          Emit(_window[(_windowPos - distance) & WindowMask]);
      }
    }

    private int Decode(HuffmanTable table)
    {
      int code = 0;
      int first = 0;
      int index = 0;

      for (int len = 1; len <= MaxBits; len++)
      {
        code |= ReadBits(1);

        int count = table.Count[len];
        if (code - first < count)
          return table.Symbol[index + (code - first)];

        index += count;
        first += count;
        first <<= 1;
        code <<= 1;
      }

      throw new InvalidDeflateException();
    }
  }

  /// <summary>
  /// Потоковый инфлейтер с ВХОДОМ из <see cref="Stream"/> (в дополнение к потоковому выходу через
  /// кольцевое окно, как в <see cref="WindowInflater"/>). Бит-ридер тянет байты из потока по мере
  /// надобности (не более заданной длины члена), поэтому ни вход, ни выход не ограничены 2 ГиБ.
  /// </summary>
  private sealed class StreamInflater
  {
    private const int WindowSize = 1 << 17;   // > макс. дистанции (Deflate 32 КБ, Deflate64 64 КБ)
    private const int WindowMask = WindowSize - 1;
    private const int OutBufferSize = 1 << 16;
    private const int InBufferSize = 1 << 16;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _deflate64;
    private readonly int[] _distBase;
    private readonly short[] _distExtra;
    private readonly byte[] _window;
    private readonly byte[] _outBuffer;
    private readonly byte[] _inBuffer;

    private long _inputRemaining;   // сколько байт члена ещё можно прочитать из потока
    private int _inPos;
    private int _inLen;

    private int _bitBuffer;
    private int _bitCount;
    private int _windowPos;
    private int _outBufPos;
    private long _written;

    public StreamInflater(Stream input, long inputLength, Stream output, bool deflate64)
    {
      _input = input;
      _output = output;
      _deflate64 = deflate64;
      _distBase = deflate64 ? Dist64Base : DistBase;
      _distExtra = deflate64 ? Dist64Extra : DistExtra;
      _window = new byte[WindowSize];
      _outBuffer = new byte[OutBufferSize];
      _inBuffer = new byte[InBufferSize];
      _inputRemaining = inputLength < 0 ? 0 : inputLength;
    }

    public long BytesWritten => _written;

    public void Inflate()
    {
      bool last;

      do
      {
        last = ReadBits(1) == 1;
        int type = ReadBits(2);

        switch (type)
        {
          case 0:
            DecodeStoredBlock();
            break;
          case 1:
            DecodeBlock(BuildFixedLitLenTable(), BuildFixedDistTable());
            break;
          case 2:
            DecodeDynamicBlock();
            break;
          default:
            throw new InvalidDeflateException();
        }
      }
      while (!last);
    }

    public void Flush()
    {
      if (_outBufPos > 0)
      {
        _output.Write(_outBuffer, 0, _outBufPos);
        _outBufPos = 0;
      }
    }

    // Читает один байт члена из входного потока (с дозаправкой буфера), не выходя за границу члена.
    private int ReadByte()
    {
      if (_inPos >= _inLen)
      {
        if (_inputRemaining <= 0)
          throw new InvalidDeflateException(); // DEFLATE-потоку нужно больше входа, чем заявлено

        int want = (int)Math.Min(_inBuffer.Length, _inputRemaining);
        _inLen = 0;
        while (_inLen < want)
        {
          int read = _input.Read(_inBuffer, _inLen, want - _inLen);
          if (read <= 0)
            break;
          _inLen += read;
        }

        if (_inLen == 0)
          throw new InvalidDeflateException(); // неожиданный конец потока

        _inPos = 0;
        _inputRemaining -= _inLen;
      }

      return _inBuffer[_inPos++];
    }

    private void Emit(byte value)
    {
      _window[_windowPos] = value;
      _windowPos = (_windowPos + 1) & WindowMask;

      _outBuffer[_outBufPos++] = value;
      if (_outBufPos == OutBufferSize)
      {
        _output.Write(_outBuffer, 0, OutBufferSize);
        _outBufPos = 0;
      }

      _written++;
    }

    private int ReadBits(int need)
    {
      long value = _bitBuffer;

      while (_bitCount < need)
      {
        value |= (long)ReadByte() << _bitCount;
        _bitCount += 8;
      }

      _bitBuffer = (int)(value >> need);
      _bitCount -= need;

      return (int)(value & ((1L << need) - 1));
    }

    private void DecodeStoredBlock()
    {
      _bitBuffer = 0;
      _bitCount = 0;

      int len = ReadByte() | (ReadByte() << 8);
      int nlen = ReadByte() | (ReadByte() << 8);

      if ((len ^ 0xFFFF) != nlen)
        throw new InvalidDeflateException();

      for (int i = 0; i < len; i++)
        Emit((byte)ReadByte());
    }

    private void DecodeDynamicBlock()
    {
      int hlit = ReadBits(5) + 257;
      int hdist = ReadBits(5) + 1;
      int hclen = ReadBits(4) + 4;

      int maxDistCodes = _deflate64 ? 32 : MaxDistCodes;
      if (hlit > MaxLitLenCodes || hdist > maxDistCodes)
        throw new InvalidDeflateException();

      short[] codeLengthLengths = new short[19];
      for (int i = 0; i < hclen; i++)
        codeLengthLengths[CodeLengthOrder[i]] = (short)ReadBits(3);

      HuffmanTable codeLengthTable = BuildTable(codeLengthLengths, 19);

      short[] lengths = new short[hlit + hdist];
      int index = 0;

      while (index < lengths.Length)
      {
        int symbol = Decode(codeLengthTable);

        if (symbol < 16)
        {
          lengths[index++] = (short)symbol;
          continue;
        }

        int repeatValue = 0;
        int repeatCount;

        switch (symbol)
        {
          case 16:
            if (index == 0)
              throw new InvalidDeflateException();

            repeatValue = lengths[index - 1];
            repeatCount = 3 + ReadBits(2);
            break;
          case 17:
            repeatCount = 3 + ReadBits(3);
            break;
          case 18:
            repeatCount = 11 + ReadBits(7);
            break;
          default:
            throw new InvalidDeflateException();
        }

        if (index + repeatCount > lengths.Length)
          throw new InvalidDeflateException();

        for (int i = 0; i < repeatCount; i++)
          lengths[index++] = (short)repeatValue;
      }

      if (lengths[256] == 0)
        throw new InvalidDeflateException();

      short[] litLenLengths = lengths[..hlit];
      short[] distLengths = lengths[hlit..];

      HuffmanTable litLenTable = BuildTable(litLenLengths, hlit);
      HuffmanTable distTable = BuildTable(distLengths, hdist);

      DecodeBlock(litLenTable, distTable);
    }

    private void DecodeBlock(HuffmanTable litLenTable, HuffmanTable distTable)
    {
      while (true)
      {
        int symbol = Decode(litLenTable);

        if (symbol == 256)
          return;

        if (symbol < 256)
        {
          Emit((byte)symbol);
          continue;
        }

        symbol -= 257;
        if (symbol >= LengthBase.Length)
          throw new InvalidDeflateException();

        int length = _deflate64 && symbol == 28
            ? 3 + ReadBits(16)
            : LengthBase[symbol] + ReadBits(LengthExtra[symbol]);

        int distSymbol = Decode(distTable);
        if (distSymbol >= _distBase.Length)
          throw new InvalidDeflateException();

        int distance = _distBase[distSymbol] + ReadBits(_distExtra[distSymbol]);

        if (distance > _written)
          throw new InvalidDeflateException();

        for (int i = 0; i < length; i++)
          Emit(_window[(_windowPos - distance) & WindowMask]);
      }
    }

    private int Decode(HuffmanTable table)
    {
      int code = 0;
      int first = 0;
      int index = 0;

      for (int len = 1; len <= MaxBits; len++)
      {
        code |= ReadBits(1);

        int count = table.Count[len];
        if (code - first < count)
          return table.Symbol[index + (code - first)];

        index += count;
        first += count;
        first <<= 1;
        code <<= 1;
      }

      throw new InvalidDeflateException();
    }
  }

  /// <summary>
  /// Строит каноническую таблицу Хаффмана из массива длин кодов.
  /// </summary>
  private static HuffmanTable BuildTable(short[] lengths, int count)
  {
    var table = new HuffmanTable(count);

    for (int symbol = 0; symbol < count; symbol++)
      table.Count[lengths[symbol]]++;

    // Длина 0 означает «символ не используется». Все нули — пустая таблица (допустимо для dist).
    if (table.Count[0] == count)
      return table;

    // Проверяем, что набор длин образует корректный (не переполненный) код.
    int left = 1;
    for (int len = 1; len <= MaxBits; len++)
    {
      left <<= 1;
      left -= table.Count[len];
      if (left < 0)
        throw new InvalidDeflateException();
    }

    Span<short> offsets = stackalloc short[MaxBits + 1];
    offsets[1] = 0;
    for (int len = 1; len < MaxBits; len++)
      offsets[len + 1] = (short)(offsets[len] + table.Count[len]);

    for (int symbol = 0; symbol < count; symbol++)
      if (lengths[symbol] != 0)
        table.Symbol[offsets[lengths[symbol]]++] = (short)symbol;

    return table;
  }

  private static HuffmanTable BuildFixedLitLenTable()
  {
    short[] lengths = new short[FixedLitLenCodes];

    for (int i = 0; i < 144; i++)
      lengths[i] = 8;
    for (int i = 144; i < 256; i++)
      lengths[i] = 9;
    for (int i = 256; i < 280; i++)
      lengths[i] = 7;
    for (int i = 280; i < FixedLitLenCodes; i++)
      lengths[i] = 8;

    return BuildTable(lengths, FixedLitLenCodes);
  }

  private static HuffmanTable BuildFixedDistTable()
  {
    // 32 кода по 5 бит. Коды 30/31 валидны только в Deflate64; в обычном Deflate
    // обращение к ним отсекается проверкой длины таблицы дистанций (_distBase).
    short[] lengths = new short[32];
    for (int i = 0; i < lengths.Length; i++)
      lengths[i] = 5;

    return BuildTable(lengths, lengths.Length);
  }
}
