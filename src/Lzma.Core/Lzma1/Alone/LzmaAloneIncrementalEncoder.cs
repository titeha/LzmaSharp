using System.Buffers.Binary;

namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Инкрементальный энкодер формата LZMA-Alone.</para>
/// <para>
/// Текущая реализация умеет кодировать только «literal-only» поток (то есть без match/rep).
/// Этого достаточно для тестов и для простых сценариев, а оптимизация/расширение функционала
/// будет отдельными шагами.
/// </para>
/// <para>
/// Важно: заголовок LZMA-Alone содержит размер распакованных данных. Поэтому размер
/// <see cref="UncompressedSize"/> должен быть известен заранее.
/// </para>
/// </summary>
public sealed class LzmaAloneIncrementalEncoder
{

  private readonly byte[] _headerBytes = new byte[LzmaAloneHeader.HeaderSize];
  private int _headerOffset;

  // Состояние LZMA1 literal-only энкодера.
  // Range encoder хранит внутреннее состояние, и мы передаём его по ref в под-энкодеры.
  // Поэтому поле не должно быть readonly.
  private LzmaRangeEncoder _range = new();
  private readonly LzmaLiteralEncoder _literalEncoder;
  private readonly LzmaDictionary _dictionary;
  private readonly ushort[] _isMatch;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _previousByte;

  private bool _finalRequested;
  private bool _rangeFlushed;
  private bool _finished;

  private long _totalBytesRead;
  private long _totalBytesWritten;

  public LzmaProperties Properties { get; }

  public int DictionarySize { get; }

  public long UncompressedSize { get; }

  public long TotalBytesRead => _totalBytesRead;

  public long TotalBytesWritten => _totalBytesWritten;

  public LzmaAloneIncrementalEncoder(LzmaProperties properties, int dictionarySize, long uncompressedSize)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dictionarySize);

    ArgumentOutOfRangeException.ThrowIfNegative(uncompressedSize);

    Properties = properties;
    DictionarySize = dictionarySize;
    UncompressedSize = uncompressedSize;

    _literalEncoder = new LzmaLiteralEncoder(properties.Lc, properties.Lp);
    _dictionary = new LzmaDictionary(dictionarySize);

    // isMatch[state, posState]
    _isMatch = new ushort[LzmaConstants.NumStates << LzmaConstants.PosStateBitsMax];
    _posStateMask = (1 << properties.Pb) - 1;

    // Готовим заголовок один раз, потом просто дозаписываем его в выходной буфер.
    _headerBytes[0] = properties.ToByteOrThrow();
    BinaryPrimitives.WriteInt32LittleEndian(_headerBytes.AsSpan(1, 4), dictionarySize);
    BinaryPrimitives.WriteUInt64LittleEndian(_headerBytes.AsSpan(5, 8), (ulong)uncompressedSize);

    Reset();
  }

  public void Reset()
  {
    _headerOffset = 0;

    _finalRequested = false;
    _rangeFlushed = false;
    _finished = false;

    _totalBytesRead = 0;
    _totalBytesWritten = 0;

    _range.Reset();
    _literalEncoder.Reset();
    _dictionary.Reset();

    _state.Reset();
    _previousByte = 0;

    LzmaProbability.Reset(_isMatch);
  }

  /// <summary>
  /// Кодирует очередную порцию входных данных.
  /// </summary>
  /// <param name="input">Входные (некодированные) данные.</param>
  /// <param name="isFinal">True, если после этого вызова больше входных данных не будет.</param>
  /// <param name="output">Буфер для закодированных данных.</param>
  /// <param name="bytesConsumed">Сколько байт входа было потреблено.</param>
  /// <param name="bytesWritten">Сколько байт было записано в выход.</param>
  public LzmaAloneEncodeResult Encode(
    ReadOnlySpan<byte> input,
    bool isFinal,
    Span<byte> output,
    out int bytesConsumed,
    out int bytesWritten)
  {
    bytesConsumed = 0;
    bytesWritten = 0;

    if (_finished)
      return LzmaAloneEncodeResult.Finished;

    if (isFinal)
      _finalRequested = true;

    int outputBefore = output.Length;

    // 1) Заголовок (может быть частично).
    if (_headerOffset < _headerBytes.Length)
    {
      int wroteHeader = WriteHeader(output);
      bytesWritten += wroteHeader;
      _totalBytesWritten += wroteHeader;
      output = output[wroteHeader..];

      // Пока заголовок не дописан — вход не потребляем.
      if (_headerOffset < _headerBytes.Length)
        return FinishStep(outputBefore, bytesConsumed, bytesWritten);
    }

    // 2) Сначала стараемся «слить» уже готовые байты range encoder-а.
    int drained = _range.DrainTo(output);
    bytesWritten += drained;
    _totalBytesWritten += drained;
    output = output[drained..];

    // 3) Кодируем literal-ы.
    while (input.Length > 0)
    {
      // Если caller подал больше входа, чем обещал в заголовке — это ошибка.
      if (_totalBytesRead >= UncompressedSize)
        return LzmaAloneEncodeResult.InvalidData;

      // Если выход закончился, а внутри range encoder-а уже есть непросланный буфер,
      // лучше сначала запросить у caller-а больше output.
      if (output.Length == 0 && _range.PendingBytes > 0)
        break;

      EncodeLiteral(input[0]);
      input = input[1..];

      bytesConsumed++;
      _totalBytesRead++;

      if (output.Length == 0) // Выход закончился — продолжим на следующем вызове.
        break;

      drained = _range.DrainTo(output);
      bytesWritten += drained;
      _totalBytesWritten += drained;
      output = output[drained..];

      if (output.Length == 0)
        break;
    }

    // 4) Если это финал и все входные байты уже приняты — можно сделать Flush.
    if (!_rangeFlushed && _finalRequested)
    {
      if (_totalBytesRead == UncompressedSize)
      {
        _range.Flush();
        _rangeFlushed = true;

        drained = _range.DrainTo(output);
        bytesWritten += drained;
        _totalBytesWritten += drained;
        output = output.Slice(drained);
      }
      else if (isFinal && input.Length == 0)
      {
        // Caller сказал «финал», но недокормил вход относительно заявленного размера.
        return LzmaAloneEncodeResult.InvalidData;
      }
    }

    // 5) Завершение: все байты flushed и слиты наружу.
    if (_rangeFlushed && _range.PendingBytes == 0)
    {
      _finished = true;
      return LzmaAloneEncodeResult.Finished;
    }

    return FinishStep(outputBefore, bytesConsumed, bytesWritten);
  }

  private LzmaAloneEncodeResult FinishStep(int outputBefore, int bytesConsumed, int bytesWritten)
  {
    bool madeProgress = bytesConsumed != 0 || bytesWritten != 0;
    if (madeProgress)
      return LzmaAloneEncodeResult.Ok;

    // Нет прогресса: либо не хватило output, либо нужен input.
    if (outputBefore == 0)
      return LzmaAloneEncodeResult.NeedMoreOutput;

    // Если финал уже запрошен, но прогресса нет — как правило, это означает нехватку output.
    if (_finalRequested)
      return LzmaAloneEncodeResult.NeedMoreOutput;

    return LzmaAloneEncodeResult.NeedMoreInput;
  }

  private int WriteHeader(Span<byte> output)
  {
    int remaining = _headerBytes.Length - _headerOffset;
    if (remaining <= 0 || output.Length == 0)
      return 0;

    int toCopy = Math.Min(remaining, output.Length);
    _headerBytes.AsSpan(_headerOffset, toCopy).CopyTo(output);
    _headerOffset += toCopy;
    return toCopy;
  }

  private void EncodeLiteral(byte literal)
  {
    int posState = (int)(_dictionary.TotalWritten & _posStateMask);
    int state = _state.Value;

    int isMatchIndex = (posState << LzmaConstants.NumStatesBits) + state;

    _range.EncodeBit(ref _isMatch[isMatchIndex], 0);
    _literalEncoder.EncodeNormal(ref _range, (int)_dictionary.TotalWritten, _previousByte, literal);

    _dictionary.PutByte(literal);
    _previousByte = literal;
    _state.UpdateLiteral();
  }
}
