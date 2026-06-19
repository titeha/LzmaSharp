namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>
/// Минимальный LZMA-энкодер.
/// На этом шаге мы умеем:
/// - кодировать литералы;
/// - кодировать ОДИН обычный match (isMatch=1, isRep=0) из скрипта.
/// </para>
/// <para>
/// Важно: энкодер «скриптовый», он не ищет совпадения сам.
/// Тесты задают последовательность операций (literal/match).
/// </para>
/// </summary>
public sealed class LzmaEncoder
{
  private readonly LzmaProperties _properties;
  private readonly LzmaDictionary _dictionary;
  private readonly LzmaLiteralEncoder _literal;
  private readonly LzmaDistanceEncoder _distanceEncoder = new();

  // Не readonly: мы передаём range по ref в некоторые энкодеры.
  private LzmaRangeEncoder _range = new();

  private readonly LzmaLenEncoder _lenEncoder;
  private readonly LzmaLenEncoder _repLenEncoder;

  // isMatch[state][posState]
  private readonly ushort[] _isMatch;

  // isRep[state]
  private readonly ushort[] _isRep;

  // isRepG0/G1/G2[state] — выбор индекса rep-дистанции (rep0..rep3).
  private readonly ushort[] _isRepG0;
  private readonly ushort[] _isRepG1;
  private readonly ushort[] _isRepG2;

  // isRep0Long[state][posState] — короткий (len 1) или длинный rep0.
  private readonly ushort[] _isRep0Long;

  private readonly int _numPosStates;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _prevByte;

  // rep0..rep3 (храним distance-1, как в LZMA SDK)
  private readonly int[] _reps = new int[4];

  public LzmaEncoder(LzmaProperties properties, int dictionarySize = 1 << 20)
  {
    _properties = properties;
    _dictionary = new LzmaDictionary(dictionarySize);
    _literal = new LzmaLiteralEncoder(properties.Lc, properties.Lp);

    _numPosStates = 1 << properties.Pb;
    _posStateMask = _numPosStates - 1;

    _isMatch = new ushort[LzmaConstants.NumStates * _numPosStates];
    _isRep = new ushort[LzmaConstants.NumStates];
    _isRepG0 = new ushort[LzmaConstants.NumStates];
    _isRepG1 = new ushort[LzmaConstants.NumStates];
    _isRepG2 = new ushort[LzmaConstants.NumStates];
    _isRep0Long = new ushort[LzmaConstants.NumStates * _numPosStates];

    _lenEncoder = new LzmaLenEncoder(_numPosStates);
    _repLenEncoder = new LzmaLenEncoder(_numPosStates);

    Reset();
  }

  public void Reset()
  {
    _range.Reset();
    _dictionary.Reset(clearBuffer: true);

    LzmaProbability.Reset(_isMatch);
    LzmaProbability.Reset(_isRep);
    LzmaProbability.Reset(_isRepG0);
    LzmaProbability.Reset(_isRepG1);
    LzmaProbability.Reset(_isRepG2);
    LzmaProbability.Reset(_isRep0Long);

    _literal.Reset();
    _lenEncoder.Reset();
    _repLenEncoder.Reset();
    _distanceEncoder.Reset();

    _state.Reset();
    _prevByte = 0;

    _reps[0] = 0;
    _reps[1] = 0;
    _reps[2] = 0;
    _reps[3] = 0;
  }

  /// <summary>
  /// Кодирует вход как поток ТОЛЬКО из литералов.
  /// </summary>
  public byte[] EncodeLiteralOnly(ReadOnlySpan<byte> input)
  {
    var ops = new LzmaEncodeOp[input.Length];
    for (int i = 0; i < input.Length; i++)
      ops[i] = LzmaEncodeOp.Lit(input[i]);

    return EncodeScript(ops);
  }

  /// <summary>
  /// Скриптовое кодирование: последовательность операций literal/match.
  /// Используется тестами.
  /// </summary>
  internal byte[] EncodeScript(ReadOnlySpan<LzmaEncodeOp> script)
  {
    Reset();

    for (int i = 0; i < script.Length; i++)
    {
      var op = script[i];
      switch (op.Kind)
      {
        case LzmaEncodeOpKind.Literal:
          EncodeLiteral(op.Literal);
          break;
        case LzmaEncodeOpKind.Match:
          EncodeMatch(op.Distance, op.Length);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(script), "Неизвестный тип операции.");
      }
    }

    // В конце обязательно делаем flush range coder'а.
    _range.Flush();
    return _range.ToArray();
  }

  /// <summary>
  /// Сколько байт упакованного вывода уже накоплено в текущем чанке (для контроля
  /// границы LZMA2-чанка по packed-размеру).
  /// </summary>
  internal int PendingChunkBytes => _range.PendingBytes;

  /// <summary>
  /// Кодирует одну операцию (литерал/match) в текущий поток БЕЗ сброса состояния.
  /// Используется при покусочном кодировании с несущим словарём.
  /// </summary>
  internal void EncodeOp(LzmaEncodeOp op)
  {
    switch (op.Kind)
    {
      case LzmaEncodeOpKind.Literal:
        EncodeLiteral(op.Literal);
        break;
      case LzmaEncodeOpKind.Match:
        EncodeMatch(op.Distance, op.Length);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(op), "Неизвестный тип операции.");
    }
  }

  /// <summary>
  /// Завершает текущий LZMA2-чанк: делает flush range coder'а и возвращает его байты.
  /// Состояние модели (вероятности, reps, словарь, позиция) сохраняется.
  /// </summary>
  internal byte[] FinishChunk()
  {
    _range.Flush();
    return _range.ToArray();
  }

  /// <summary>
  /// Начинает следующий LZMA2-чанк, сохраняя словарь и состояние модели (семантика
  /// control 0x80): переинициализируется только range coder (как и у декодера, который
  /// заводит новый range-поток на границе каждого LZMA-чанка).
  /// </summary>
  internal void BeginNextChunkKeepState()
  {
    _range.Reset();
  }

  private void EncodeLiteral(byte b)
  {
    long pos = _dictionary.TotalWritten;
    int posState = (int)pos & _posStateMask;
    int state = _state.Value;

    // isMatch = 0
    ref ushort isMatchProb = ref _isMatch[state * _numPosStates + posState];
    _range.EncodeBit(ref isMatchProb, 0);

    if (_state.IsLiteralState)
    {
      // Обычный литерал.
      _literal.EncodeNormal(ref _range, pos, _prevByte, b);
    }
    else
    {
      // "Matched literal" (после match/rep): нужен matchByte по rep0.
      byte matchByte = _dictionary.PeekBackByte(_reps[0] + 1);
      _literal.EncodeMatched(ref _range, pos, _prevByte, matchByte, b);
    }

    _dictionary.PutByte(b);
    _prevByte = b;
    _state.UpdateLiteral();
  }

  private void EncodeMatch(int distance, int len)
  {
    if (distance <= 0)
      throw new ArgumentOutOfRangeException(nameof(distance), "Distance должен быть >= 1.");

    if (len < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(len), $"Длина match должна быть >= {LzmaConstants.MatchMinLen}.");

    long pos = _dictionary.TotalWritten;
    int posState = (int)pos & _posStateMask;
    int state = _state.Value;

    // isMatch = 1
    ref ushort isMatchProb = ref _isMatch[(state * _numPosStates) + posState];
    _range.EncodeBit(ref isMatchProb, 1);

    // Если дистанция совпадает с одной из последних rep-дистанций — кодируем дешёвый
    // rep-match (без полного кодирования дистанции). reps хранят distance-1.
    int repIndex = FindRepIndex(distance - 1);

    ref ushort isRepProb = ref _isRep[state];

    if (repIndex < 0)
    {
      _range.EncodeBit(ref isRepProb, 0); // обычный match

      _lenEncoder.Encode(ref _range, posState, len);

      int lenToPosState = len - LzmaConstants.MatchMinLen;
      if (lenToPosState > LzmaConstants.NumLenToPosStates - 1)
        lenToPosState = LzmaConstants.NumLenToPosStates - 1;

      _distanceEncoder.EncodeDistance(_range, lenToPosState, (uint)distance);

      // Обновляем reps: rep0 хранит distance-1.
      _reps[3] = _reps[2];
      _reps[2] = _reps[1];
      _reps[1] = _reps[0];
      _reps[0] = distance - 1;

      _state.UpdateMatch();
    }
    else
    {
      _range.EncodeBit(ref isRepProb, 1); // rep-match
      EncodeRepMatch(repIndex, len, posState, state);
      _state.UpdateRep();
    }

    // Обновляем словарь так, как будто декодер реально выполнил match.
    _dictionary.CopyMatch(distance, len);

    // Последний записанный байт (distance = 1).
    _prevByte = _dictionary.PeekBackByte(1);
  }

  /// <summary>
  /// Возвращает индекс rep-дистанции (0..3), равной <paramref name="distMinus1"/> (distance-1),
  /// или -1, если совпадения нет. Меньший индекс предпочтительнее (он дешевле кодируется).
  /// </summary>
  private int FindRepIndex(int distMinus1)
  {
    for (int i = 0; i < 4; i++)
      if (_reps[i] == distMinus1)
        return i;

    return -1;
  }

  /// <summary>
  /// Кодирует rep-match: биты выбора индекса (isRepG0/G1/G2, isRep0Long) и длину repLen,
  /// затем обновляет историю rep-дистанций — строго зеркально декодеру.
  /// </summary>
  private void EncodeRepMatch(int repIndex, int len, int posState, int state)
  {
    if (repIndex == 0)
    {
      _range.EncodeBit(ref _isRepG0[state], 0);
      // Мы не выдаём матчи длиной < MatchMinLen, поэтому это всегда длинный rep0 (isRep0Long = 1).
      _range.EncodeBit(ref _isRep0Long[(state * _numPosStates) + posState], 1);
      // rep0 не меняется.
    }
    else
    {
      _range.EncodeBit(ref _isRepG0[state], 1);

      if (repIndex == 1)
      {
        _range.EncodeBit(ref _isRepG1[state], 0);
        int d = _reps[1];
        _reps[1] = _reps[0];
        _reps[0] = d;
      }
      else
      {
        _range.EncodeBit(ref _isRepG1[state], 1);

        if (repIndex == 2)
        {
          _range.EncodeBit(ref _isRepG2[state], 0);
          int d = _reps[2];
          _reps[2] = _reps[1];
          _reps[1] = _reps[0];
          _reps[0] = d;
        }
        else // repIndex == 3
        {
          _range.EncodeBit(ref _isRepG2[state], 1);
          int d = _reps[3];
          _reps[3] = _reps[2];
          _reps[2] = _reps[1];
          _reps[1] = _reps[0];
          _reps[0] = d;
        }
      }
    }

    _repLenEncoder.Encode(ref _range, posState, len);
  }
}
