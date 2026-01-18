using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>
/// Тестовый «мини-энкодер», который умеет закодировать очень узкий набор сценариев,
/// нужный для пошаговой разработки декодера.
/// </para>
/// <para>
/// На этом шаге он умеет:
/// - 1 литерал;
/// - затем rep0 (короткий или длинный).
/// </para>
/// <para>
/// Это НЕ полноценный LZMA-энкодер.
/// Он создан только для того, чтобы у нас были детерминированные входные данные
/// для юнит-тестов декодера.
/// </para>
/// </summary>
internal sealed class LzmaTestRep0Encoder
{
  private readonly LzmaProperties _props;

  private readonly LzmaTestRangeEncoder _range = new();
  private readonly LzmaLiteralDecoder _literal;

  private readonly int _numPosStates;
  private readonly int _posStateMask;

  // Модели вероятностей — ровно те же, что использует декодер.
  private readonly ushort[] _isMatch;      // [state][posState]
  private readonly ushort[] _isRep;        // [state]
  private readonly ushort[] _isRepG0;      // [state]
  private readonly ushort[] _isRep0Long;   // [state][posState]

  // Модели длины для repLen (отдельная модель от matchLen).
  private readonly ushort[] _repLenChoice;   // 2
  private readonly ushort[] _repLenChoice2;  // 2
  private readonly ushort[] _repLenLow;      // posStates * (1<<LenNumLowBits)
  private readonly ushort[] _repLenMid;      // posStates * (1<<LenNumMidBits)
  private readonly ushort[] _repLenHigh;     // 1<<LenNumHighBits

  private LzmaState _state;
  private byte _previousByte;
  private long _pos;

  public LzmaTestRep0Encoder(LzmaProperties props)
  {
    _props = props;

    _literal = new LzmaLiteralDecoder(props.Lc, props.Lp);

    _numPosStates = 1 << props.Pb;
    _posStateMask = _numPosStates - 1;

    _isMatch = new ushort[LzmaConstants.NumStates * _numPosStates];
    _isRep = new ushort[LzmaConstants.NumStates];
    _isRepG0 = new ushort[LzmaConstants.NumStates];
    _isRep0Long = new ushort[LzmaConstants.NumStates * _numPosStates];

    _repLenChoice = new ushort[2];
    _repLenChoice2 = new ushort[2];

    int lowCount = 1 << LzmaConstants.LenNumLowBits;
    int midCount = 1 << LzmaConstants.LenNumMidBits;
    int highCount = 1 << LzmaConstants.LenNumHighBits;

    _repLenLow = new ushort[_numPosStates * lowCount];
    _repLenMid = new ushort[_numPosStates * midCount];
    _repLenHigh = new ushort[highCount];

    Reset();
  }

  /// <summary>
  /// Кодирует поток: 1 литерал, затем короткий rep0 (len=1).
  /// Ожидаемый эффект при декодировании: повторится предыдущий байт.
  /// </summary>
  public byte[] Encode_OneLiteral_Then_ShortRep0(byte literal)
  {
    Reset();

    EncodeLiteral(literal);

    // match
    EncodeIsMatch(1);

    // rep
    EncodeIsRep(1);

    // rep0
    EncodeIsRepG0(0);

    // short rep0
    EncodeIsRep0Long(0);

    // «Выпускаем» один байт вывода, который декодер скопирует из словаря.
    _state.UpdateShortRep();
    _pos += 1;
    // previousByte не меняется: он равен скопированному байту.

    _range.Flush();
    return _range.ToArray();
  }

  /// <summary>
  /// Кодирует поток: 1 литерал, затем длинный rep0 (len &gt;= 2).
  /// </summary>
  public byte[] Encode_OneLiteral_Then_LongRep0(byte literal, int repLen)
  {
    if (repLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(repLen), $"repLen должен быть >= {LzmaConstants.MatchMinLen}.");

    Reset();

    EncodeLiteral(literal);

    // match
    EncodeIsMatch(1);

    // rep
    EncodeIsRep(1);

    // rep0
    EncodeIsRepG0(0);

    // long rep0
    EncodeIsRep0Long(1);

    EncodeRepLen(repLen);

    // Декодер после repLen сделает копирование repLen байт.
    _state.UpdateRep();
    _pos += repLen;
    // previousByte после копирования станет таким же (повтор литерала).

    _range.Flush();
    return _range.ToArray();
  }

  private void Reset()
  {
    _range.Reset();
    _range.WriteInitBytes();

    LzmaProbability.Reset(_isMatch);
    LzmaProbability.Reset(_isRep);
    LzmaProbability.Reset(_isRepG0);
    LzmaProbability.Reset(_isRep0Long);

    LzmaProbability.Reset(_repLenChoice);
    LzmaProbability.Reset(_repLenChoice2);
    LzmaProbability.Reset(_repLenLow);
    LzmaProbability.Reset(_repLenMid);
    LzmaProbability.Reset(_repLenHigh);

    _literal.Reset();

    _state.Reset();
    _previousByte = 0;
    _pos = 0;
  }

  private int GetPosState() => (int)_pos & _posStateMask;

  private void EncodeIsMatch(uint bit)
  {
    int posState = GetPosState();
    ref ushort prob = ref _isMatch[_state.Value * _numPosStates + posState];
    _range.EncodeBit(ref prob, bit);
  }

  private void EncodeIsRep(uint bit)
  {
    ref ushort prob = ref _isRep[_state.Value];
    _range.EncodeBit(ref prob, bit);
  }

  private void EncodeIsRepG0(uint bit)
  {
    ref ushort prob = ref _isRepG0[_state.Value];
    _range.EncodeBit(ref prob, bit);
  }

  private void EncodeIsRep0Long(uint bit)
  {
    int posState = GetPosState();
    ref ushort prob = ref _isRep0Long[_state.Value * _numPosStates + posState];
    _range.EncodeBit(ref prob, bit);
  }

  private void EncodeLiteral(byte b)
  {
    // Перед литералом обязательно кодируем isMatch=0.
    EncodeIsMatch(0);

    int offset = _literal.GetSubCoderOffset(_pos, _previousByte);

    int symbol = 1;
    for (int i = 7; i >= 0; i--)
    {
      uint bit = (uint)((b >> i) & 1);
      _range.EncodeBit(ref _literal.Probs[offset + symbol], bit);
      symbol = (symbol << 1) | (int)bit;
    }

    _previousByte = b;
    _state.UpdateLiteral();
    _pos++;
  }

  private void EncodeRepLen(int len)
  {
    // В LZMA «len» измеряется в реальных байтах (минимум MatchMinLen).
    // Внутри модели кодируется symbol = len - MatchMinLen.
    int posState = GetPosState();
    int symbol = len - LzmaConstants.MatchMinLen;

    if (symbol < LzmaConstants.LenNumLowSymbols)
    {
      // choice0 = 0
      _range.EncodeBit(ref _repLenChoice[0], 0);

      int offset = posState * (1 << LzmaConstants.LenNumLowBits);
      BitTreeEncoderForTests.Encode(
        range: _range,
        probs: _repLenLow,
        offset: offset,
        numBits: LzmaConstants.LenNumLowBits,
        symbol: symbol);

      return;
    }

    // choice0 = 1
    _range.EncodeBit(ref _repLenChoice[0], 1);

    symbol -= LzmaConstants.LenNumLowSymbols;

    if (symbol < LzmaConstants.LenNumMidSymbols)
    {
      // choice1 = 0
      _range.EncodeBit(ref _repLenChoice[1], 0);

      int offset = posState * (1 << LzmaConstants.LenNumMidBits);
      BitTreeEncoderForTests.Encode(
        range: _range,
        probs: _repLenMid,
        offset: offset,
        numBits: LzmaConstants.LenNumMidBits,
        symbol: symbol);

      return;
    }

    // choice1 = 1
    _range.EncodeBit(ref _repLenChoice[1], 1);

    symbol -= LzmaConstants.LenNumMidSymbols;

    BitTreeEncoderForTests.Encode(
      range: _range,
      probs: _repLenHigh,
      offset: 0,
      numBits: LzmaConstants.LenNumHighBits,
      symbol: symbol);
  }

  /// <summary>
  /// Минимальный bit-tree энкодер для тестов.
  /// </summary>
  private static class BitTreeEncoderForTests
  {
    public static void Encode(
      LzmaTestRangeEncoder range,
      ushort[] probs,
      int offset,
      int numBits,
      int symbol)
    {
      // Символ кодируется как путь по дереву от корня:
      // на каждом уровне записываем 0/1 и обновляем prob соответствующей вершины.
      int m = 1;
      for (int bitIndex = numBits - 1; bitIndex >= 0; bitIndex--)
      {
        uint bit = (uint)((symbol >> bitIndex) & 1);
        range.EncodeBit(ref probs[offset + m], bit);
        m = (m << 1) | (int)bit;
      }
    }
  }
}
