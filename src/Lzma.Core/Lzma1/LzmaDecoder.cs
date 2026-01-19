namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Потоковый LZMA-декодер (инкрементальный, без небезопасной магии и указателей).</para>
/// <para>
/// На текущем шаге реализовано:
/// - литералы (isMatch == 0);
/// - обычный match (isMatch == 1, isRep == 0);
/// - rep0 (isMatch == 1, isRep == 1, isRepG0 == 0):
///   - короткий rep0 (isRep0Long == 0) — длина 1;
///   - длинный rep0 (isRep0Long == 1) — длина из repLen.
/// - rep1 (isMatch == 1, isRep == 1, isRepG0 == 1, isRepG1 == 0).
/// </para>
/// <para>
/// Пока НЕ реализовано (вернём <see cref="LzmaDecodeResult.NotImplemented"/>):
/// - rep2/rep3 (ветка isRepG0 == 1, isRepG1 == 1).
/// </para>
/// <para>
/// Контракт:
/// - метод <see cref="Decode"/> можно вызывать много раз;
/// - декодер хранит состояние между вызовами;
/// - если входных данных не хватает, возвращает <see cref="LzmaDecodeResult.NeedsMoreInput"/>;
/// - выходной буфер заполняется максимально (пока не закончился dst или не упёрлись в NeedsMoreInput).
/// </para>
/// </summary>
public sealed class LzmaDecoder
{
  private enum Step
  {
    /// <summary>Ожидаем 5 байт инициализации range decoder.</summary>
    RangeInit,

    /// <summary>Декодируем isMatch.</summary>
    IsMatch,

    /// <summary>Декодируем isRep (только если isMatch == 1).</summary>
    IsRep,

    /// <summary>Декодируем isRepG0 (только если isRep == 1).</summary>
    IsRepG0,

    /// <summary>Декодируем isRepG1 (только если isRepG0 == 1).</summary>
    IsRepG1,

    /// <summary>Декодируем isRep0Long (только если isRepG0 == 0).</summary>
    IsRep0Long,

    /// <summary>Декодируем длину для rep0 (repLen).</summary>
    DecodeRepLen,

    /// <summary>Декодируем длину обычного match (len).</summary>
    DecodeMatchLen,

    /// <summary>Декодируем расстояние обычного match.</summary>
    DecodeMatchDistance,

    /// <summary>Копируем match в словарь и output (может продолжаться в следующем вызове).</summary>
    MatchCopy,

    /// <summary>Декодируем литерал (8 бит в битовом дереве).</summary>
    Literal,
  }

  private readonly LzmaProperties _properties;
  // Важно: LzmaRangeDecoder — struct с изменяемым состоянием (Code/Range и т.п.).
  // Если сделать поле readonly, компилятор начнёт делать защитные копии при вызове методов,
  // и состояние range decoder'а перестанет сохраняться между вызовами (а иногда даже внутри одного Decode).
  private LzmaRangeDecoder _range = new();
  private readonly LzmaDictionary _dictionary;
  private readonly LzmaLiteralDecoder _literal;
  private readonly LzmaLenDecoder _lenDecoder;
  private readonly LzmaLenDecoder _repLenDecoder;
  private readonly LzmaDistanceDecoder _distanceDecoder;

  // isMatch[state][posState]
  private readonly ushort[] _isMatch;

  // isRep[state]
  private readonly ushort[] _isRep;

  // isRepG0[state]
  private readonly ushort[] _isRepG0;

  // isRepG1[state]
  private readonly ushort[] _isRepG1;

  // isRep0Long[state][posState]
  private readonly ushort[] _isRep0Long;

  private readonly int _numPosStates;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _previousByte;
  private Step _step;

  // Литерал: промежуточное состояние декодирования (битовое дерево 8 бит).
  private int _literalSubCoderOffset;
  private int _literalSymbol;

  // Match/Rep: промежуточное состояние.
  private int _matchPosState;
  private int _decodedMatchLen;
  private int _pendingMatchLength;
  private int _pendingMatchDistance;

  // Rep distances.
  private int _rep0;
  private int _rep1;
  private int _rep2;
  private int _rep3;

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

    _properties = properties;
    _dictionary = new LzmaDictionary(dictionarySize);
    _literal = new LzmaLiteralDecoder(properties.Lc, properties.Lp);

    _numPosStates = 1 << properties.Pb;
    _posStateMask = _numPosStates - 1;

    _isMatch = new ushort[LzmaConstants.NumStates * _numPosStates];
    _isRep = new ushort[LzmaConstants.NumStates];
    _isRepG0 = new ushort[LzmaConstants.NumStates];
    _isRepG1 = new ushort[LzmaConstants.NumStates];
    _isRep0Long = new ushort[LzmaConstants.NumStates * _numPosStates];

    _lenDecoder = new LzmaLenDecoder();
    _repLenDecoder = new LzmaLenDecoder();
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
    LzmaProbability.Reset(_isRepG0);
    LzmaProbability.Reset(_isRepG1);
    LzmaProbability.Reset(_isRep0Long);
    _lenDecoder.Reset(_numPosStates);
    _repLenDecoder.Reset(_numPosStates);
    _distanceDecoder.Reset();
    _literal.Reset();

    // Состояние конечного автомата LZMA.
    _state.Reset();

    // Предыдущий байт (для контекста литералов).
    _previousByte = 0;

    // Rep distances по умолчанию — 1 (это стандартное начальное значение в LZMA).
    _rep0 = _rep1 = _rep2 = _rep3 = 1;

    // Мы ещё не прочитали 5 байт инициализации range coder.
    _step = Step.RangeInit;

    // Литерал сейчас не в процессе.
    _literalSubCoderOffset = 0;
    _literalSymbol = 0;

    // Match/Rep сейчас не в процессе.
    _matchPosState = 0;
    _decodedMatchLen = 0;
    _pendingMatchLength = 0;
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

    // Значения по умолчанию — на случай раннего выхода.
    LzmaDecodeResult result = LzmaDecodeResult.Ok;
    bool shouldTerminate = false;

    // Если терминальное состояние — выходим сразу.
    if (_isTerminal)
    {
      bytesConsumed = 0;
      bytesWritten = 0;
      progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);
      return _terminalResult;
    }

    // Если мы на старте потока — обязаны прочитать 5 init-байт range coder.
    // Важно: мы делаем это ДО основного цикла, чтобы далее TryDecodeBit никогда
    // не вызывался на неинициализированном range decoder'е.
    if (_step == Step.RangeInit && !_rangeInitialized)
    {
      var init = _range.TryInitialize(src, ref srcPos);
      if (init == LzmaRangeInitResult.NeedMoreInput)
      {
        result = LzmaDecodeResult.NeedsMoreInput;
        goto Done;
      }

      if (init == LzmaRangeInitResult.InvalidData)
      {
        result = LzmaDecodeResult.InvalidData;
        shouldTerminate = true;
        goto Done;
      }

      _rangeInitialized = true;
    }

    while (dstPos < dst.Length)
    {
      if (_step == Step.RangeInit)
      {
        // Range coder уже инициализирован (или мы вернули NeedsMoreInput выше).
        // Здесь лишь инициализируем модели и состояние.
        LzmaProbability.Reset(_isMatch);
        LzmaProbability.Reset(_isRep);
        LzmaProbability.Reset(_isRepG0);
        LzmaProbability.Reset(_isRepG1);
        LzmaProbability.Reset(_isRep0Long);
        _lenDecoder.Reset(_numPosStates);
        _repLenDecoder.Reset(_numPosStates);
        _distanceDecoder.Reset();
        _literal.Reset();
        _state.Reset();
        _previousByte = 0;
        _rep0 = _rep1 = _rep2 = _rep3 = 1;

        _step = Step.IsMatch;
        continue;
      }

      if (_step == Step.IsMatch)
      {
        long pos = _dictionary.TotalWritten;
        int posState = (int)pos & _posStateMask;
        int idx = (_state.Value * _numPosStates) + posState;
        ref ushort prob = ref _isMatch[idx];

        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isMatch);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isMatch == 0)
        {
          _literalSubCoderOffset = _literal.GetSubCoderOffset(pos, _previousByte);
          _literalSymbol = 1;
          _step = Step.Literal;
        }
        else
        {
          _matchPosState = posState;
          _step = Step.IsRep;
        }

        continue;
      }

      if (_step == Step.IsRep)
      {
        ref ushort prob = ref _isRep[_state.Value];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRep);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _step = isRep == 0 ? Step.DecodeMatchLen : Step.IsRepG0;
        continue;
      }

      if (_step == Step.IsRepG0)
      {
        // Ветка rep. Пока реализуем ТОЛЬКО rep0 (isRepG0 == 0).
        ref ushort prob = ref _isRepG0[_state.Value];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRepG0);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isRepG0 != 0)
        {
          // isRepG0 == 1 означает rep1/rep2/rep3.
          // На этом шаге реализуем ТОЛЬКО rep1.
          _step = Step.IsRepG1;
          continue;
        }

        _step = Step.IsRep0Long;
        continue;
      }

      if (_step == Step.IsRepG1)
      {
        ref ushort prob = ref _isRepG1[_state.Value];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRepG1);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        // isRepG1 == 0 -> rep1
        // isRepG1 == 1 -> rep2/rep3 (пока не поддержано)
        if (isRepG1 != 0)
        {
          result = LzmaDecodeResult.NotImplemented;
          shouldTerminate = true;
          break;
        }

        // rep1: используем _rep1 как distance и поднимаем его в rep0.
        // История rep-ов: [rep0, rep1, rep2, rep3] -> [rep1, rep0, rep2, rep3]
        int dist = _rep1;
        _rep1 = _rep0;
        _rep0 = dist;

        _step = Step.DecodeRepLen;
        continue;
      }

      if (_step == Step.IsRep0Long)
      {
        // isRep0Long зависит от state и posState.
        int idx = (_state.Value * _numPosStates) + _matchPosState;
        ref ushort prob = ref _isRep0Long[idx];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRep0Long);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isRep0Long == 0)
        {
          // Короткий rep0: длина 1.
          _pendingMatchLength = 1;
          _pendingMatchDistance = _rep0;
          _state.UpdateShortRep();
          _step = Step.MatchCopy;
        }
        else
        {
          _step = Step.DecodeRepLen;
        }

        continue;
      }

      if (_step == Step.DecodeRepLen)
      {
        var lenRes = _repLenDecoder.TryDecode(ref _range, src, ref srcPos, _matchPosState, out uint repLen);
        if (lenRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (lenRes != LzmaRangeDecodeResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        _pendingMatchLength = checked((int)repLen);
        _pendingMatchDistance = _rep0;
        _state.UpdateRep();
        _step = Step.MatchCopy;
        continue;
      }

      if (_step == Step.DecodeMatchLen)
      {
        var lenRes = _lenDecoder.TryDecode(ref _range, src, ref srcPos, _matchPosState, out uint matchLen);
        if (lenRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (lenRes != LzmaRangeDecodeResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        _decodedMatchLen = checked((int)matchLen);
        _step = Step.DecodeMatchDistance;
        continue;
      }

      if (_step == Step.DecodeMatchDistance)
      {
        // В LZMA distance декодируется с учётом lenToPosState.
        // lenToPosState = min(matchLen - MatchMinLen, NumLenToPosStates - 1)
        // (в оригинальном 7-Zip это: lenToPosState = (len < 6) ? (len - 2) : 3)
        int lenToPosState = _decodedMatchLen - LzmaConstants.MatchMinLen;
        if (lenToPosState < 0)
          lenToPosState = 0;
        if (lenToPosState >= LzmaConstants.NumLenToPosStates)
          lenToPosState = LzmaConstants.NumLenToPosStates - 1;

        var distRes = _distanceDecoder.TryDecodeDistance(ref _range, src, ref srcPos, lenToPosState, out uint distance);
        if (distRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (distRes != LzmaRangeDecodeResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        // Обновляем rep history.
        _rep3 = _rep2;
        _rep2 = _rep1;
        _rep1 = _rep0;
        _rep0 = (int)distance;

        _pendingMatchLength = _decodedMatchLen;
        _pendingMatchDistance = _rep0;

        _state.UpdateMatch();
        _step = Step.MatchCopy;
        continue;
      }

      if (_step == Step.MatchCopy)
      {
        int before = dstPos;

        // Важно: LzmaDictionary.TryCopyMatch(...) не умеет сам уменьшать length.
        // Поэтому мы сами считаем, сколько байт реально скопировали в dst,
        // и уменьшаем _pendingMatchLength.
        var copyRes = _dictionary.TryCopyMatch(_pendingMatchDistance, _pendingMatchLength, dst, ref dstPos);

        int copied = dstPos - before;
        _pendingMatchLength -= copied;
        if (_pendingMatchLength < 0)
        {
          // Такого быть не должно: это означает рассинхрон длины и реального копирования.
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        if (copyRes == LzmaDictionaryResult.InvalidDistance)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        if (copied > 0)
          _previousByte = dst[dstPos - 1];

        if (_pendingMatchLength == 0)
        {
          _step = Step.IsMatch;
          continue;
        }

        // Выход закончился — вернём Ok, а копирование продолжим в следующем вызове.
        if (copyRes == LzmaDictionaryResult.OutputTooSmall)
        {
          result = LzmaDecodeResult.Ok;
          break;
        }

        // Если словарь сказал "Ok", но match не докопирован — это ошибка/битый поток.
        result = LzmaDecodeResult.InvalidData;
        shouldTerminate = true;
        break;
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
        continue;
      }

      // Неверное состояние.
      result = LzmaDecodeResult.InvalidData;
      shouldTerminate = true;
      break;
    }

  Done:
    if (shouldTerminate)
    {
      _isTerminal = true;
      _terminalResult = result;
    }

    bytesConsumed = srcPos;
    bytesWritten = dstPos;

    // Важно: учитываем потреблённый вход даже при NeedsMoreInput.
    // Это гарантирует корректный прогресс при потоковой подаче.
    _totalInputBytes += bytesConsumed;

    progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);
    return result;
  }
}
