namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Минимальный потоковый LZMA-декодер.</para>
/// <para>
/// Мы собираем декодер маленькими шагами, поэтому в нём постепенно появляются новые ветки.
/// На текущем шаге реализовано:
/// <list type="bullet">
///   <item><description>инициализация range decoder'а (чтение 5 init-байт);</description></item>
///   <item><description>литералы (<c>isMatch == 0</c>);</description></item>
///   <item><description>обычные match'и (<c>isMatch == 1</c> и <c>isRep == 0</c>): декод длины + дистанции и копирование из словаря;</description></item>
///   <item><description>rep0 (<c>isMatch == 1</c>, <c>isRep == 1</c>, <c>isRepG0 == 0</c>):
///     <list type="bullet">
///       <item><description>short rep0 (<c>isRep0Long == 0</c>) — копируем 1 байт назад;</description></item>
///       <item><description>long rep0 (<c>isRep0Long == 1</c>) — длина через repLen-модель, дистанция = rep0.</description></item>
///     </list>
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Пока НЕ реализовано (вернём <see cref="LzmaDecodeResult.NotImplemented"/>):
/// <list type="bullet">
///   <item><description>rep1/rep2/rep3 (когда <c>isRepG0 == 1</c>);</description></item>
///   <item><description>прочие ветки (end marker, align и т.п. появятся позже).</description></item>
/// </list>
/// </para>
/// <para>
/// Контракт:
/// <list type="bullet">
///   <item><description><see cref="Decode"/> можно вызывать много раз; декодер хранит состояние между вызовами;</description></item>
///   <item><description>если входных данных не хватает, возвращает <see cref="LzmaDecodeResult.NeedsMoreInput"/>;</description></item>
///   <item><description>выходной буфер заполняется максимально (пока не закончился dst или не упёрлись в NeedsMoreInput / ошибку);</description></item>
///   <item><description>поддерживается потоковая выдача match'ей: если длина match'а больше остатка dst — копируем сколько помещается и продолжим на следующем вызове.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class LzmaDecoder
{
  private enum Step
  {
    RangeInit,

    IsMatch,
    IsRep,

    // Обычный match (isRep == 0)
    MatchLen,
    MatchDist,
    CopyMatch,

    // Rep-ветка (isRep == 1)
    IsRepG0,
    IsRep0Long,
    RepLen,
    CopyRep,

    // Литерал
    Literal,
  }

  private readonly LzmaProperties _properties;
  private LzmaRangeDecoder _range = new();
  private readonly LzmaDictionary _dictionary;
  private readonly LzmaLiteralDecoder _literal;

  private readonly LzmaLenDecoder _lenDecoder = new();
  private readonly LzmaLenDecoder _repLenDecoder = new();
  private readonly LzmaDistanceDecoder _distanceDecoder = new();

  // isMatch[state][posState]
  private readonly ushort[] _isMatch;

  // isRep[state]
  private readonly ushort[] _isRep;

  // isRepG0[state]
  private readonly ushort[] _isRepG0;

  // isRep0Long[state][posState]
  private readonly ushort[] _isRep0Long;

  private readonly int _numPosStates;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _previousByte;

  private Step _step;

  // Промежуточное состояние декодирования литерала (битовое дерево 8 бит).
  private int _literalSubCoderOffset;
  private int _literalSymbol;

  // Промежуточное состояние match/rep
  private int _matchLength;     // ВАЖНО: это "остаток" длины, который ещё надо скопировать.
  private int _matchDistance;

  private bool _isTerminal;
  private LzmaDecodeResult _terminalResult;

  private long _totalInputBytes;

  // Rep history. На текущем шаге используем только rep0.
  private int _rep0;

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
    _isRep0Long = new ushort[LzmaConstants.NumStates * _numPosStates];

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
    _range.Reset();

    _dictionary.Reset(clearBuffer: clearDictionary);

    // Вероятностные модели.
    LzmaProbability.Reset(_isMatch);
    LzmaProbability.Reset(_isRep);
    LzmaProbability.Reset(_isRepG0);
    LzmaProbability.Reset(_isRep0Long);

    _literal.Reset();
    _lenDecoder.Reset(_numPosStates);
    _repLenDecoder.Reset(_numPosStates);
    _distanceDecoder.Reset();

    // Состояние конечного автомата LZMA.
    _state.Reset();

    // Предыдущий байт (для контекста литералов).
    _previousByte = 0;

    // Мы ещё не прочитали 5 байт инициализации range coder.
    _step = Step.RangeInit;

    // Литерал сейчас не в процессе.
    _literalSubCoderOffset = 0;
    _literalSymbol = 0;

    // Match сейчас не в процессе.
    _matchLength = 0;
    _matchDistance = 0;

    _isTerminal = false;
    _terminalResult = LzmaDecodeResult.Ok;

    _totalInputBytes = 0;

    // Как в LZMA SDK: rep0 по умолчанию 1.
    _rep0 = 1;
  }

  /// <summary>
  /// Декодирует данные из <paramref name="src"/> в <paramref name="dst"/>.
  /// </summary>
  /// <remarks>
  /// Мы не знаем "unpackSize" на этом шаге. Поэтому декодируем "сколько получится":
  /// пока есть место в <paramref name="dst"/> и пока хватает входных данных.
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

    // Если терминальное состояние — выходим сразу.
    if (_isTerminal)
    {
      bytesConsumed = 0;
      bytesWritten = 0;
      progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);
      return _terminalResult;
    }

    LzmaDecodeResult result = LzmaDecodeResult.Ok;
    bool shouldTerminate = false;

    while (dstPos < dst.Length)
    {
      if (_step == Step.RangeInit)
      {
        var initRes = _range.TryInitialize(src, ref srcPos);
        if (initRes == LzmaRangeInitResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (initRes == LzmaRangeInitResult.InvalidData)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        // Инициализация нового потока.
        LzmaProbability.Reset(_isMatch);
        LzmaProbability.Reset(_isRep);
        LzmaProbability.Reset(_isRepG0);
        LzmaProbability.Reset(_isRep0Long);

        _literal.Reset();
        _lenDecoder.Reset(_numPosStates);
        _repLenDecoder.Reset(_numPosStates);
        _distanceDecoder.Reset();

        _state.Reset();
        _previousByte = 0;

        _rep0 = 1;

        _matchLength = 0;
        _matchDistance = 0;

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
          continue;
        }

        _step = Step.IsRep;
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

        if (isRep == 0)
        {
          _step = Step.MatchLen;
          continue;
        }

        // rep-ветка
        _step = Step.IsRepG0;
        continue;
      }

      if (_step == Step.MatchLen)
      {
        int posState = (int)_dictionary.TotalWritten & _posStateMask;
        var lenRes = _lenDecoder.TryDecode(ref _range, src, ref srcPos, posState, out uint len);
        if (lenRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _matchLength = checked((int)len);
        _step = Step.MatchDist;
        continue;
      }

      if (_step == Step.MatchDist)
      {
        // lenToPosState — грубо: min(len - 2, 3)
        int lenToPosState = _matchLength - LzmaConstants.MatchMinLen;
        if (lenToPosState > 3)
          lenToPosState = 3;

        var distRes = _distanceDecoder.TryDecodeDistance(ref _range, src, ref srcPos, lenToPosState, out uint dist);
        if (distRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        // В LZMA "distance" в битстриме хранится как (realDist - 1).
        _matchDistance = checked((int)dist);
        _step = Step.CopyMatch;
        continue;
      }

      if (_step == Step.CopyMatch)
      {
        // Копируем match кусками: сколько помещается в dst.
        if (_matchLength <= 0)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        int canWrite = dst.Length - dstPos;
        if (canWrite <= 0)
        {
          // dst заполнен. Возвращаем Ok, продолжим на следующем вызове.
          result = LzmaDecodeResult.Ok;
          break;
        }

        int chunkLen = _matchLength;
        if (chunkLen > canWrite)
          chunkLen = canWrite;

        var dictRes = _dictionary.TryCopyMatch(distance: _matchDistance, length: chunkLen, dst, ref dstPos);
        if (dictRes == LzmaDictionaryResult.OutputTooSmall)
        {
          // Теоретически сюда не должны попадать (chunkLen <= canWrite), но на всякий случай.
          result = LzmaDecodeResult.Ok;
          break;
        }

        if (dictRes != LzmaDictionaryResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        _matchLength -= chunkLen;
        _previousByte = _dictionary.PeekBackByte(1);

        if (_matchLength > 0)
        {
          // match ещё не докопировали, а dst, вероятно, уже закончился.
          result = LzmaDecodeResult.Ok;
          break;
        }

        // match завершён.
        _rep0 = _matchDistance;
        _state.UpdateMatch();
        _step = Step.IsMatch;
        continue;
      }

      if (_step == Step.IsRepG0)
      {
        // На этом шаге поддерживаем только rep0.
        // Если isRepG0 == 1, значит rep1/rep2/rep3 — вернём NotImplemented.
        ref ushort prob = ref _isRepG0[_state.Value];
        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRepG0);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isRepG0 != 0)
        {
          result = LzmaDecodeResult.NotImplemented;
          shouldTerminate = true;
          break;
        }

        _step = Step.IsRep0Long;
        continue;
      }

      if (_step == Step.IsRep0Long)
      {
        long pos = _dictionary.TotalWritten;
        int posState = (int)pos & _posStateMask;
        int idx = _state.Value * _numPosStates + posState;
        ref ushort prob = ref _isRep0Long[idx];

        var bitRes = _range.TryDecodeBit(ref prob, src, ref srcPos, out uint isRep0Long);
        if (bitRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        if (isRep0Long == 0)
        {
          // short rep0: длина = 1, дистанция = rep0
          var dictRes = _dictionary.TryCopyMatch(distance: _rep0, length: 1, dst, ref dstPos);
          if (dictRes == LzmaDictionaryResult.OutputTooSmall)
          {
            // dst неожиданно мал — просто вернём Ok.
            result = LzmaDecodeResult.Ok;
            break;
          }

          if (dictRes != LzmaDictionaryResult.Ok)
          {
            result = LzmaDecodeResult.InvalidData;
            shouldTerminate = true;
            break;
          }

          _previousByte = _dictionary.PeekBackByte(1);
          _state.UpdateShortRep();
          _step = Step.IsMatch;
          continue;
        }

        _step = Step.RepLen;
        continue;
      }

      if (_step == Step.RepLen)
      {
        int posState = (int)_dictionary.TotalWritten & _posStateMask;
        var lenRes = _repLenDecoder.TryDecode(ref _range, src, ref srcPos, posState, out uint len);
        if (lenRes == LzmaRangeDecodeResult.NeedMoreInput)
        {
          result = LzmaDecodeResult.NeedsMoreInput;
          break;
        }

        _matchLength = checked((int)len);
        _matchDistance = _rep0;
        _step = Step.CopyRep;
        continue;
      }

      if (_step == Step.CopyRep)
      {
        if (_matchLength <= 0)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        int canWrite = dst.Length - dstPos;
        if (canWrite <= 0)
        {
          result = LzmaDecodeResult.Ok;
          break;
        }

        int chunkLen = _matchLength;
        if (chunkLen > canWrite)
          chunkLen = canWrite;

        var dictRes = _dictionary.TryCopyMatch(distance: _matchDistance, length: chunkLen, dst, ref dstPos);
        if (dictRes == LzmaDictionaryResult.OutputTooSmall)
        {
          result = LzmaDecodeResult.Ok;
          break;
        }

        if (dictRes != LzmaDictionaryResult.Ok)
        {
          result = LzmaDecodeResult.InvalidData;
          shouldTerminate = true;
          break;
        }

        _matchLength -= chunkLen;
        _previousByte = _dictionary.PeekBackByte(1);

        if (_matchLength > 0)
        {
          result = LzmaDecodeResult.Ok;
          break;
        }

        // rep завершён.
        _state.UpdateRep();
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

    if (shouldTerminate)
    {
      _isTerminal = true;
      _terminalResult = result;
    }

    // ЕДИНСТВЕННОЕ место присвоения out-параметров.
    bytesConsumed = srcPos;
    bytesWritten = dstPos;
    _totalInputBytes += bytesConsumed;
    progress = new LzmaProgress(_totalInputBytes, _dictionary.TotalWritten);

    return result;
  }
}
