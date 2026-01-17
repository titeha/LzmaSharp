namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Минимальный потоковый LZMA-декодер.</para>
/// <para>
/// На данном шаге реализованы:
/// - литералы (isMatch == 0);
/// - обычные матчи (isMatch == 1, isRep == 0).
/// Rep-матчи пока не реализованы и возвращают <see cref="LzmaDecodeResult.NotImplemented"/>.
/// Мы постепенно собираем полноценный декодер маленькими шагами.
/// </para>
/// <para>
/// Контракт:
/// - метод <see cref="Decode"/> может вызываться много раз;
/// - декодер хранит состояние между вызовами;
/// - если входных данных не хватает, возвращает <see cref="LzmaDecodeResult.NeedsMoreInput"/>;
/// - если встретили rep-match, возвращает <see cref="LzmaDecodeResult.NotImplemented"/>;
/// - выходной буфер заполняется максимально (пока не закончился dst или не упёрлись в NeedsMoreInput).
/// </para>
/// </summary>
public sealed class LzmaDecoder
{
  private enum Step
  {
    IsMatch,
    IsRep,
    MatchLen,
    MatchDist,
    CopyMatch,
    Literal,
  }

  private readonly LzmaProperties _properties;
  private LzmaRangeDecoder _range;
  private readonly LzmaDictionary _dictionary;
  private readonly LzmaLiteralDecoder _literal;

  // isRep[state]
  private readonly ushort[] _isRep;
  private readonly LzmaLenDecoder _lenDecoder;
  private readonly LzmaDistanceDecoder _distanceDecoder;
  private uint _pendingMatchLen;
  private uint _pendingMatchDistance;

  // isMatch[state][posState]
  private readonly ushort[] _isMatch;
  private readonly int _numPosStates;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _previousByte;

  private Step _step;

  // Промежуточное состояние декодирования литерала (битовое дерево 8 бит).
  private int _literalSubCoderOffset;
  private int _literalSymbol;

  private bool _isTerminal;
  private LzmaDecodeResult _terminalResult;

  private long _totalInputBytes;

  private bool _rangeInitialized;

  /// <summary>
  /// Создаёт декодер для потока с указанными параметрами.
  /// </summary>
  /// <param name="properties">LZMA properties (lc/lp/pb).</param>
  /// <param name="dictionarySize">Размер словаря в байтах.</param>
  public LzmaDecoder(LzmaProperties properties, int dictionarySize)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    _range = new LzmaRangeDecoder();
    _properties = properties;
    _dictionary = new LzmaDictionary(dictionarySize);
    _literal = new LzmaLiteralDecoder(properties.Lc, properties.Lp);

    _numPosStates = 1 << properties.Pb;
    _posStateMask = _numPosStates - 1;

    _isMatch = new ushort[LzmaConstants.NumStates * _numPosStates];

    _isRep = new ushort[LzmaConstants.NumStates];
    _lenDecoder = new LzmaLenDecoder();
    _distanceDecoder = new LzmaDistanceDecoder();

    Reset(clearDictionary: true);
  }

  /// <summary>
  /// Сколько байт всего записано в выход (с начала потока).
  /// </summary>
  public long TotalWritten => _dictionary.TotalWritten;

  /// <summary>
  /// Сколько байт всего принято во вход (с начала потока).
  /// </summary>
  public long TotalRead => _totalInputBytes;

  /// <summary>
  /// Сбрасывает состояние декодера для декодирования нового потока.
  /// </summary>
  public void Reset(bool clearDictionary)
  {
    _rangeInitialized = false;
    _range.Reset();

    _dictionary.Reset(clearBuffer: clearDictionary);

    // Вероятностные модели.
    LzmaProbability.Reset(_isMatch);
    LzmaProbability.Reset(_isRep);
    _literal.Reset();
    _lenDecoder.Reset(_numPosStates);
    _distanceDecoder.Reset();

    // Состояние конечного автомата LZMA.
    _state.Reset();

    // Предыдущий байт (для контекста литералов).
    _previousByte = 0;

    // Мы ещё не прочитали 5 байт инициализации range coder.
    _step = Step.IsMatch;

    // Литерал сейчас не в процессе.
    _literalSubCoderOffset = 0;
    _literalSymbol = 0;

    _pendingMatchLen = 0;
    _pendingMatchDistance = 0;

    _isTerminal = false;
    _terminalResult = LzmaDecodeResult.Ok;

    _totalInputBytes = 0;
  }

  /// <summary>
  /// Декодирует данные из <paramref name="src"/> в <paramref name="dst"/>.
  /// </summary>
  /// <remarks>
  /// Мы не знаем "unpackSize" на этом шаге. Поэтому декодируем "сколько получится":
  /// пока есть место в <paramref name="dst"/> и пока хватает входных данных.
  /// 
  /// Когда выходной буфер заполнился, возвращаем <see cref="LzmaDecodeResult.Ok"/>.
  /// Чтобы продолжить — вызови Decode ещё раз с новым куском выходного буфера.
  /// </remarks>
  public LzmaDecodeResult Decode(
    ReadOnlySpan<byte> src,
    out int bytesConsumed,
    Span<byte> dst,
    out int bytesWritten,
    out LzmaProgress progress)
  {
    int srcPos = 0;
    int dstPos = 0;

    // Если терминальное состояние — выходим сразу
    if (_isTerminal)
    {
      bytesConsumed = 0;
      bytesWritten = 0;
      progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);
      return _terminalResult;
    }

    // Если range decoder ещё не инициализирован, пытаемся дочитать 5 init-байт.
    // Это обязательно для LZMA: пока не прочитали 5 байт, декодировать биты нельзя.
    if (!_rangeInitialized)
    {
      var initResult = _range.TryInitialize(src, ref srcPos);
      if (initResult == LzmaRangeInitResult.NeedMoreInput)
      {
        bytesConsumed = srcPos;   // сколько успели "съесть" init-байт
        bytesWritten = dstPos;
        progress = new LzmaProgress(_totalInputBytes + bytesConsumed, _dictionary.TotalWritten);
        return LzmaDecodeResult.NeedsMoreInput;
      }

      if (initResult == LzmaRangeInitResult.InvalidData)
      {
        bytesConsumed = srcPos;
        bytesWritten = dstPos;
        progress = new LzmaProgress(_totalInputBytes + bytesConsumed, _dictionary.TotalWritten);
        return LzmaDecodeResult.InvalidData;
      }

      LzmaProbability.Reset(_isMatch);
      LzmaProbability.Reset(_isRep);
      _literal.Reset();
      _lenDecoder.Reset(_numPosStates);
      _distanceDecoder.Reset();
      _state.Reset();
      _previousByte = 0;

      _rangeInitialized = true;
      _step = Step.IsMatch; // на всякий случай
    }

    // Локальные переменные для управления выходом
    LzmaDecodeResult result = LzmaDecodeResult.Ok;
    bool shouldTerminate = false;

    while (dstPos < dst.Length)
    {
      if (_step == Step.IsMatch)
      {
        long pos = _dictionary.TotalWritten;
        int posState = (int)pos & _posStateMask;
        int idx = _state.Value * _numPosStates + posState;
        ref ushort prob = ref _isMatch[idx];

        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isMatch);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isMatch != 0)
        {
          // Переходим к разбору типа матча (rep / обычный).
          _step = Step.IsRep;
          continue;
        }

        _literalSubCoderOffset = _literal.GetSubCoderOffset(pos, _previousByte);
        _literalSymbol = 1;
        _step = Step.Literal;
        continue;
      }

      if (_step == Step.IsRep)
      {
        // На этом шаге реализуем только обычные матчи (isRep = 0).
        // Rep-матчи добавим отдельным следующим шагом.
        ref ushort prob = ref _isRep[_state.Value];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRep);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isRep != 0)
        {
          result = LzmaDecodeResult.NotImplemented;
          shouldTerminate = true;
          break;
        }

        _step = Step.MatchLen;
        continue;
      }

      if (_step == Step.MatchLen)
      {
        long pos = _dictionary.TotalWritten;
        int posState = (int)pos & _posStateMask;

        var lenRes = _lenDecoder.TryDecode(ref _range, src, ref srcPos, posState, out uint len);
        if (lenRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _pendingMatchLen = len;
        _step = Step.MatchDist;
        continue;
      }

      if (_step == Step.MatchDist)
      {
        // lenToPosState зависит от длины матча.
        // Для простоты используем минимальную формулу: min(len-2, 3).
        // Это совпадает с логикой LZMA SDK.
        uint len = _pendingMatchLen;
        int lenToPosState = (int)(len < 6 ? (len - 2) : 3);

        var distRes = _distanceDecoder.TryDecodeDistance(ref _range, src, ref srcPos, lenToPosState, out uint dist);
        if (distRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _pendingMatchDistance = dist;
        _step = Step.CopyMatch;
        continue;
      }

      if (_step == Step.CopyMatch)
      {
        // distance в наших примитивах уже в формате 1..N.
        // LZMA dictionary копирует именно так.

        int dist = checked((int)_pendingMatchDistance);
        int len = checked((int)_pendingMatchLen);

        var copyRes = _dictionary.TryCopyMatch(dist, len, dst, ref dstPos);
        if (copyRes == LzmaDictionaryResult.OutputTooSmall)
        {
          // Нет места в выходе — просто выходим, не теряя состояние.
          result = LzmaDecodeResult.Ok;
          break;
        }
        if (copyRes != LzmaDictionaryResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        // Обновляем state и prevByte
        _state.UpdateMatch();
        // Последний записанный байт после CopyMatch — это последний байт выходного буфера.
        // (CopyMatch уже записал данные и в словарь, и в dst).
        if (dstPos > 0)
          _previousByte = dst[dstPos - 1];

        _pendingMatchLen = 0;
        _pendingMatchDistance = 0;
        _step = Step.IsMatch;
        continue;
      }

      if (_step == Step.Literal)
      {
        ref ushort prob = ref _literal.Probs[_literalSubCoderOffset + _literalSymbol];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint bit);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _literalSymbol = (_literalSymbol << 1) | (int)bit;
        if (_literalSymbol < 0x100)
          continue;

        byte b = (byte)_literalSymbol;
        var dictRes = _dictionary.TryPutByte(b, dst, ref dstPos);
        if (dictRes != LzmaDictionaryResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        _previousByte = b;
        _state.UpdateLiteral();
        _step = Step.IsMatch;
        _literalSymbol = 0;
        continue;
      }

      // Неверное состояние
      result = LzmaDecodeResult.InvalidData;
      shouldTerminate = true;
      break;
    }

    // Применяем терминальное состояние, если нужно
    if (shouldTerminate)
    {
      _isTerminal = true;
      _terminalResult = result;
    }

    // ЕДИНСТВЕННОЕ место присвоения out-параметров
    bytesConsumed = srcPos;
    bytesWritten = dstPos;
    _totalInputBytes += bytesConsumed;
    progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);

    return result;
  }
}
