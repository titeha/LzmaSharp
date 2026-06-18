namespace Lzma.Core.Ppmd;

/// <summary>
/// Результат декодирования PPMd (вариант H / PPMd7, как в 7z).
/// </summary>
public enum Ppmd7DecodeResult
{
  /// <summary>Поток успешно декодирован.</summary>
  Ok = 0,

  /// <summary>Поток повреждён или не соответствует формату.</summary>
  InvalidData = 1,

  /// <summary>Параметры не поддержаны (например, недопустимый order/memSize).</summary>
  NotSupported = 2,
}

/// <summary>
/// <para>Управляемый декодер PPMd var.H (PPMd7) с 7z range coder, без unsafe.</para>
/// <para>
/// Верный порт эталонной реализации LZMA SDK (Ppmd7.c / Ppmd7Dec.c, основанной на
/// PPMd var.H Дмитрия Шкарина). Указатели заменены на UInt32-смещения в общий буфер
/// <c>_base</c>; контекстная модель, suballocator и range-декодер воспроизведены 1:1.
/// </para>
/// </summary>
public static class Ppmd7Decoder
{
  private const int PpmdNumIndexes = 38; // N1+N2+N3+N4 = 4+4+4+26
  private const int UnitSize = 12;
  private const int MaxFreq = 124;
  private const int IntBits = 7;
  private const int PeriodBits = 7;
  private const int BinScale = 1 << (IntBits + PeriodBits); // 16384
  private const uint TopValue = 1u << 24;

  private const int MinOrder = 2;
  private const int MaxOrder = 64;
  private const uint MinMemSize = 1 << 11;
  private const uint MaxMemSize = 0xFFFFFFFF - 12 * 3;

  private static readonly byte[] ExpEscapeInit = [25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2];
  private static readonly ushort[] InitBinEsc = [0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051];

  /// <summary>
  /// Декодирует PPMd7-поток в <paramref name="output"/> ровно на длину буфера.
  /// </summary>
  /// <param name="input">Сжатые данные (после properties).</param>
  /// <param name="order">PPMd order (2..64), из properties.</param>
  /// <param name="memSize">Размер памяти модели в байтах, из properties.</param>
  public static Ppmd7DecodeResult Decode(ReadOnlySpan<byte> input, int order, uint memSize, Span<byte> output)
  {
    if (order < MinOrder || order > MaxOrder)
      return Ppmd7DecodeResult.NotSupported;

    if (memSize < MinMemSize || memSize > MaxMemSize)
      return Ppmd7DecodeResult.NotSupported;

    var model = new Model(memSize, input);

    model.Init(order);

    if (!model.RangeDecInit())
      return Ppmd7DecodeResult.InvalidData;

    for (int i = 0; i < output.Length; i++)
    {
      int sym = model.DecodeSymbol();
      if (sym < 0)
        return Ppmd7DecodeResult.InvalidData;

      output[i] = (byte)sym;
    }

    return Ppmd7DecodeResult.Ok;
  }

  /// <summary>SEE-контекст: ссылочный тип, чтобы возвращать и мутировать на месте.</summary>
  private sealed class See
  {
    public ushort Summ;
    public byte Shift;
    public byte Count;
  }

  private sealed class Model
  {
    // ----- Основной буфер (вместо CPpmd7::Base) -----
    private readonly byte[] _base;
    private readonly uint _size;
    private readonly uint _alignOffset;

    // ----- Состояние модели -----
    private uint _minContext;
    private uint _maxContext;
    private uint _foundState;
    private int _orderFall, _initEsc, _prevSuccess, _maxOrder, _hiBitsFlag;
    private int _runLength, _initRL;
    private uint _glueCount;
    private uint _loUnit, _hiUnit, _text, _unitsStart;

    // ----- Range decoder (7z) -----
    private readonly byte[] _input;
    private int _inPos;
    private uint _range;
    private uint _code;

    // ----- Таблицы -----
    private readonly byte[] _indx2Units = new byte[PpmdNumIndexes + 2];
    private readonly byte[] _units2Indx = new byte[128];
    private readonly uint[] _freeList = new uint[PpmdNumIndexes];
    private readonly byte[] _ns2bsIndx = new byte[256];
    private readonly byte[] _ns2Indx = new byte[256];
    private readonly byte[] _expEscape = new byte[16];
    private readonly See _dummySee = new();
    private readonly See[][] _see = CreateSee();
    private readonly ushort[,] _binSumm = new ushort[128, 64];

    private readonly byte[] _charMask = new byte[256];

    public Model(uint memSize, ReadOnlySpan<byte> input)
    {
      _input = input.ToArray();
      _size = memSize;
      _alignOffset = (4 - memSize) & 3;
      _base = new byte[_alignOffset + memSize];
      Construct();
    }

    private static See[][] CreateSee()
    {
      var see = new See[25][];
      for (int i = 0; i < 25; i++)
      {
        see[i] = new See[16];
        for (int k = 0; k < 16; k++)
          see[i][k] = new See();
      }

      return see;
    }

    // ============================================================
    // Низкоуровневый доступ к полям (LE) по смещениям в _base
    // ============================================================

    private ushort GetU16(uint off) => (ushort)(_base[off] | (_base[off + 1] << 8));
    private void SetU16(uint off, ushort v) { _base[off] = (byte)v; _base[off + 1] = (byte)(v >> 8); }
    private uint GetU32(uint off) => (uint)(_base[off] | (_base[off + 1] << 8) | (_base[off + 2] << 16) | (_base[off + 3] << 24));
    private void SetU32(uint off, uint v) { _base[off] = (byte)v; _base[off + 1] = (byte)(v >> 8); _base[off + 2] = (byte)(v >> 16); _base[off + 3] = (byte)(v >> 24); }

    // Context (12 байт): NumStats@0(u16), SummFreq@2(u16)/State2{Symbol@2,Freq@3},
    //                    Stats@4(u32)/State4{Succ0@4,Succ1@6}, Suffix@8(u32).
    private int CtxNumStats(uint c) => GetU16(c);
    private void SetCtxNumStats(uint c, int v) => SetU16(c, (ushort)v);
    private int CtxSummFreq(uint c) => GetU16(c + 2);
    private void SetCtxSummFreq(uint c, int v) => SetU16(c + 2, (ushort)v);
    private uint CtxStats(uint c) => GetU32(c + 4);
    private void SetCtxStats(uint c, uint v) => SetU32(c + 4, v);
    private uint CtxSuffix(uint c) => GetU32(c + 8);
    private void SetCtxSuffix(uint c, uint v) => SetU32(c + 8, v);
    private static uint OneState(uint c) => c + 2;

    // State (6 байт): Symbol@0, Freq@1, Successor_0@2(u16), Successor_1@4(u16).
    private byte StSymbol(uint s) => _base[s];
    private void SetStSymbol(uint s, byte v) => _base[s] = v;
    private byte StFreq(uint s) => _base[s + 1];
    private void SetStFreq(uint s, byte v) => _base[s + 1] = v;
    private uint StSuccessor(uint s) => (uint)(GetU16(s + 2) | (GetU16(s + 4) << 16));
    private void SetStSuccessor(uint s, uint v) { SetU16(s + 2, (ushort)v); SetU16(s + 4, (ushort)(v >> 16)); }

    private void CopyState(uint dst, uint src)
    {
      _base[dst] = _base[src];
      _base[dst + 1] = _base[src + 1];
      _base[dst + 2] = _base[src + 2];
      _base[dst + 3] = _base[src + 3];
      _base[dst + 4] = _base[src + 4];
      _base[dst + 5] = _base[src + 5];
    }

    private void SwapStates(uint a, uint b)
    {
      for (uint i = 0; i < 6; i++)
        (_base[a + i], _base[b + i]) = (_base[b + i], _base[a + i]);
    }

    private uint U2B(uint nu) => nu * UnitSize;
    private int U2I(uint nu) => _units2Indx[nu - 1];
    private uint I2U(int indx) => _indx2Units[indx];

    // ============================================================
    // Конструирование таблиц (Ppmd7_Construct)
    // ============================================================

    private void Construct()
    {
      int k = 0;
      for (int i = 0; i < PpmdNumIndexes; i++)
      {
        int step = i >= 12 ? 4 : (i >> 2) + 1;
        do { _units2Indx[k++] = (byte)i; } while (--step != 0);
        _indx2Units[i] = (byte)k;
      }

      _ns2bsIndx[0] = 0 << 1;
      _ns2bsIndx[1] = 1 << 1;
      for (int i = 2; i < 11; i++) _ns2bsIndx[i] = 2 << 1;
      for (int i = 11; i < 256; i++) _ns2bsIndx[i] = 3 << 1;

      for (int i = 0; i < 3; i++) _ns2Indx[i] = (byte)i;

      int m = 3;
      k = 1;
      for (int i = 3; i < 256; i++)
      {
        _ns2Indx[i] = (byte)m;
        if (--k == 0)
          k = ++m - 2;
      }

      Array.Copy(ExpEscapeInit, _expEscape, 16);
    }

    public void Init(int maxOrder)
    {
      _maxOrder = maxOrder;
      RestartModel();
    }

    // ============================================================
    // RestartModel (Ppmd7_RestartModel)
    // ============================================================

    private void RestartModel()
    {
      Array.Clear(_freeList, 0, _freeList.Length);

      _text = _alignOffset;
      _hiUnit = _text + _size;
      _loUnit = _unitsStart = _hiUnit - (_size / 8 / UnitSize * 7 * UnitSize);
      _glueCount = 0;

      _orderFall = _maxOrder;
      _initRL = -(int)((_maxOrder < 12) ? _maxOrder : 12) - 1;
      _runLength = _initRL;
      _prevSuccess = 0;

      _hiUnit -= UnitSize;
      uint mc = _hiUnit; // order-0 context
      uint s = _loUnit;  // 256 states
      _loUnit += U2B(256 / 2);
      _maxContext = _minContext = mc;
      _foundState = s;

      SetCtxNumStats(mc, 256);
      SetCtxSummFreq(mc, 256 + 1);
      SetCtxStats(mc, s);
      SetCtxSuffix(mc, 0);

      for (int i = 0; i < 256; i++, s += 6)
      {
        SetStSymbol(s, (byte)i);
        SetStFreq(s, 1);
        SetStSuccessor(s, 0);
      }

      for (int i = 0; i < 128; i++)
        for (int kk = 0; kk < 8; kk++)
        {
          ushort val = (ushort)(BinScale - InitBinEsc[kk] / (i + 2));
          for (int mm = 0; mm < 64; mm += 8)
            _binSumm[i, kk + mm] = val;
        }

      for (int i = 0; i < 25; i++)
        for (int kk = 0; kk < 16; kk++)
        {
          See se = _see[i][kk];
          se.Summ = (ushort)((5 * i + 10) << (PeriodBits - 4));
          se.Shift = PeriodBits - 4;
          se.Count = 4;
        }

      _dummySee.Summ = 0;
      _dummySee.Shift = PeriodBits;
      _dummySee.Count = 64;
    }

    // ============================================================
    // Suballocator
    // ============================================================

    private void InsertNode(uint node, int indx)
    {
      SetU32(node, _freeList[indx]);
      _freeList[indx] = node;
    }

    private uint RemoveNode(int indx)
    {
      uint node = _freeList[indx];
      _freeList[indx] = GetU32(node);
      return node;
    }

    private void SplitBlock(uint ptr, int oldIndx, int newIndx)
    {
      uint nu = I2U(oldIndx) - I2U(newIndx);
      ptr += U2B(I2U(newIndx));
      int i = U2I(nu);
      if (I2U(i) != nu)
      {
        int k = (int)I2U(--i);
        InsertNode(ptr + U2B((uint)k), (int)nu - k - 1);
      }

      InsertNode(ptr, i);
    }

    // Node (для glue): Stamp@0(u16), NU@2(u16), Next@4(u32).
    private void GlueFreeBlocks()
    {
      uint head = 0;
      uint n = 0;

      _glueCount = 255;

      if (_loUnit != _hiUnit)
        SetU16(_loUnit, 1); // guard NODE stamp

      for (int i = 0; i < PpmdNumIndexes; i++)
      {
        ushort nu = (ushort)I2U(i);
        uint next = _freeList[i];
        _freeList[i] = 0;
        while (next != 0)
        {
          uint un = next;
          uint tmp = next;
          next = GetU32(un); // NextRef stored at offset 0 in free node
          SetU16(un, 0);      // Stamp = EMPTY
          SetU16(un + 2, nu); // NU
          SetU32(un + 4, n);  // Next
          n = tmp;
        }
      }

      head = n;

      // Glue
      {
        uint prevField = 0; // 0 => prev is `head` variable; else offset of a node's Next field
        bool prevIsHead = true;
        while (n != 0)
        {
          uint node = n;
          uint nu = GetU16(node + 2);
          uint nextN = GetU32(node + 4);

          if (nu == 0)
          {
            if (prevIsHead) head = nextN; else SetU32(prevField, nextN);
            n = nextN;
            continue;
          }

          prevIsHead = false;
          prevField = node + 4;

          for (; ; )
          {
            uint node2 = node + U2B(nu);
            uint sumNu = nu + (uint)GetU16(node2 + 2);
            if (GetU16(node2) != 0 || sumNu >= 0x10000)
              break;
            SetU16(node + 2, (ushort)sumNu);
            SetU16(node2 + 2, 0);
            nu = sumNu;
          }

          n = nextN;
        }
      }

      // Fill
      for (n = head; n != 0;)
      {
        uint node = n;
        uint nu = GetU16(node + 2);
        n = GetU32(node + 4);
        if (nu == 0)
          continue;

        for (; nu > 128; nu -= 128, node += U2B(128))
          InsertNode(node, PpmdNumIndexes - 1);

        int i = U2I(nu);
        if (I2U(i) != nu)
        {
          int kk = (int)I2U(--i);
          InsertNode(node + U2B((uint)kk), (int)nu - kk - 1);
        }

        InsertNode(node, i);
      }
    }

    private uint AllocUnitsRare(int indx)
    {
      if (_glueCount == 0)
      {
        GlueFreeBlocks();
        if (_freeList[indx] != 0)
          return RemoveNode(indx);
      }

      int i = indx;
      do
      {
        if (++i == PpmdNumIndexes)
        {
          uint numBytes = U2B(I2U(indx));
          _glueCount--;
          if (_unitsStart - _text > numBytes)
          {
            _unitsStart -= numBytes;
            return _unitsStart;
          }

          return 0;
        }
      }
      while (_freeList[i] == 0);

      uint block = RemoveNode(i);
      SplitBlock(block, i, indx);
      return block;
    }

    private uint AllocUnits(int indx)
    {
      if (_freeList[indx] != 0)
        return RemoveNode(indx);

      uint numBytes = U2B(I2U(indx));
      if (_hiUnit - _loUnit >= numBytes)
      {
        uint lo = _loUnit;
        _loUnit += numBytes;
        return lo;
      }

      return AllocUnitsRare(indx);
    }

    private void MemCpyUnits(uint dst, uint src, uint nu)
    {
      Array.Copy(_base, src, _base, dst, nu * UnitSize);
    }

    // ============================================================
    // Модель: CreateSuccessors / UpdateModel / Rescale / MakeEscFreq
    // ============================================================

    private uint CreateSuccessors()
    {
      uint c = _minContext;
      uint upBranch = StSuccessor(_foundState);
      Span<uint> ps = stackalloc uint[MaxOrder];
      int numPs = 0;

      if (_orderFall != 0)
        ps[numPs++] = _foundState;

      while (CtxSuffix(c) != 0)
      {
        uint s;
        c = CtxSuffix(c);

        if (CtxNumStats(c) != 1)
        {
          byte sym = StSymbol(_foundState);
          s = CtxStats(c);
          while (StSymbol(s) != sym)
            s += 6;
        }
        else
        {
          s = OneState(c);
        }

        uint successor = StSuccessor(s);
        if (successor != upBranch)
        {
          c = successor;
          if (numPs == 0)
            return c;
          break;
        }

        ps[numPs++] = s;
      }

      byte newSym = _base[upBranch];
      uint upBranch1 = upBranch + 1;
      byte newFreq;

      if (CtxNumStats(c) == 1)
      {
        newFreq = StFreq(OneState(c));
      }
      else
      {
        uint s = CtxStats(c);
        while (StSymbol(s) != newSym)
          s += 6;

        uint cf = (uint)StFreq(s) - 1;
        uint s0 = (uint)CtxSummFreq(c) - (uint)CtxNumStats(c) - cf;
        newFreq = (byte)(1 + ((2 * cf <= s0) ? (5 * cf > s0 ? 1 : 0) : (2 * cf + s0 - 1) / (2 * s0) + 1));
      }

      do
      {
        uint c1;
        if (_hiUnit != _loUnit)
        {
          _hiUnit -= UnitSize;
          c1 = _hiUnit;
        }
        else if (_freeList[0] != 0)
        {
          c1 = RemoveNode(0);
        }
        else
        {
          c1 = AllocUnitsRare(0);
          if (c1 == 0)
            return 0;
        }

        SetCtxNumStats(c1, 1);
        SetStSymbol(OneState(c1), newSym);
        SetStFreq(OneState(c1), newFreq);
        SetStSuccessor(OneState(c1), upBranch1);
        SetCtxSuffix(c1, c);
        SetStSuccessor(ps[--numPs], c1);
        c = c1;
      }
      while (numPs != 0);

      return c;
    }

    private void UpdateModel()
    {
      uint fs = _foundState;
      uint maxSuccessor, minSuccessor;
      uint c;

      if (StFreq(fs) < MaxFreq / 4 && CtxSuffix(_minContext) != 0)
      {
        c = CtxSuffix(_minContext);

        if (CtxNumStats(c) == 1)
        {
          uint s = OneState(c);
          if (StFreq(s) < 32)
            SetStFreq(s, (byte)(StFreq(s) + 1));
        }
        else
        {
          uint s = CtxStats(c);
          byte sym = StSymbol(fs);

          if (StSymbol(s) != sym)
          {
            do { s += 6; } while (StSymbol(s) != sym);

            if (StFreq(s) >= StFreq(s - 6))
            {
              SwapStates(s, s - 6);
              s -= 6;
            }
          }

          if (StFreq(s) < MaxFreq - 9)
          {
            SetStFreq(s, (byte)(StFreq(s) + 2));
            SetCtxSummFreq(c, CtxSummFreq(c) + 2);
          }
        }
      }

      if (_orderFall == 0)
      {
        uint cs = CreateSuccessors();
        if (cs == 0)
        {
          RestartModel();
          return;
        }

        _maxContext = _minContext = cs;
        SetStSuccessor(fs, cs);
        return;
      }

      _base[_text] = StSymbol(fs);
      _text++;
      if (_text >= _unitsStart)
      {
        RestartModel();
        return;
      }

      maxSuccessor = _text;
      minSuccessor = StSuccessor(fs);

      if (minSuccessor != 0)
      {
        if (minSuccessor <= maxSuccessor)
        {
          uint cs = CreateSuccessors();
          if (cs == 0)
          {
            RestartModel();
            return;
          }

          minSuccessor = cs;
        }

        if (--_orderFall == 0)
        {
          maxSuccessor = minSuccessor;
          if (_maxContext != _minContext)
            _text--;
        }
      }
      else
      {
        SetStSuccessor(fs, maxSuccessor);
        minSuccessor = _minContext;
      }

      uint mc = _minContext;
      c = _maxContext;
      _maxContext = _minContext = minSuccessor;

      if (c == mc)
        return;

      uint s0 = (uint)CtxSummFreq(mc) - (uint)CtxNumStats(mc) - ((uint)StFreq(fs) - 1);
      int ns = CtxNumStats(mc);

      do
      {
        int ns1 = CtxNumStats(c);
        uint sum;

        if (ns1 != 1)
        {
          if ((ns1 & 1) == 0)
          {
            uint oldNU = (uint)ns1 >> 1;
            int i = U2I(oldNU);
            if (i != U2I(oldNU + 1))
            {
              uint ptr = AllocUnits(i + 1);
              if (ptr == 0)
              {
                RestartModel();
                return;
              }

              uint oldPtr = CtxStats(c);
              MemCpyUnits(ptr, oldPtr, oldNU);
              InsertNode(oldPtr, i);
              SetCtxStats(c, ptr);
            }
          }

          sum = (uint)CtxSummFreq(c);
          sum += (uint)((2 * ns1 < ns ? 1 : 0) + 2 * (((4 * ns1 <= ns ? 1 : 0) & (sum <= 8 * (uint)ns1 ? 1 : 0))));
        }
        else
        {
          uint s = AllocUnits(0);
          if (s == 0)
          {
            RestartModel();
            return;
          }

          CopyState(s, OneState(c));
          SetCtxStats(c, s);

          uint freq = StFreq(s);
          if (freq < MaxFreq / 4 - 1)
            freq <<= 1;
          else
            freq = MaxFreq - 4;

          SetStFreq(s, (byte)freq);
          sum = (uint)(freq + _initEsc + (ns > 3 ? 1 : 0));
        }

        uint sNew = CtxStats(c) + (uint)ns1 * 6;
        uint cf = 2 * (sum + 6) * (uint)StFreq(fs);
        uint sf = s0 + sum;
        SetStSymbol(sNew, StSymbol(fs));
        SetCtxNumStats(c, ns1 + 1);
        SetStSuccessor(sNew, maxSuccessor);

        if (cf < 6 * sf)
        {
          cf = 1u + (cf > sf ? 1u : 0u) + (cf >= 4 * sf ? 1u : 0u);
          sum += 3;
        }
        else
        {
          cf = 4u + (cf >= 9 * sf ? 1u : 0u) + (cf >= 12 * sf ? 1u : 0u) + (cf >= 15 * sf ? 1u : 0u);
          sum += cf;
        }

        SetCtxSummFreq(c, (int)sum);
        SetStFreq(sNew, (byte)cf);
        c = CtxSuffix(c);
      }
      while (c != mc);
    }

    private void Rescale()
    {
      uint stats = CtxStats(_minContext);
      uint s = _foundState;

      // Сортировка: переносим найденный символ в начало.
      if (s != stats)
      {
        Span<byte> tmp = stackalloc byte[6];
        for (uint i = 0; i < 6; i++) tmp[(int)i] = _base[s + i];
        while (s != stats)
        {
          CopyState(s, s - 6);
          s -= 6;
        }
        for (uint i = 0; i < 6; i++) _base[s + i] = tmp[(int)i];
      }

      int escFreq = CtxSummFreq(_minContext) - StFreq(s);
      int adder = _orderFall != 0 ? 1 : 0;
      int sumFreq = (StFreq(s) + 4 + adder) >> 1;
      SetStFreq(s, (byte)sumFreq);

      int i2 = CtxNumStats(_minContext) - 1;
      do
      {
        s += 6;
        int freq = StFreq(s);
        escFreq -= freq;
        freq = (freq + adder) >> 1;
        sumFreq += freq;
        SetStFreq(s, (byte)freq);

        if (freq > StFreq(s - 6))
        {
          Span<byte> tmp = stackalloc byte[6];
          for (uint i = 0; i < 6; i++) tmp[(int)i] = _base[s + i];
          uint s1 = s;
          do
          {
            CopyState(s1, s1 - 6);
            s1 -= 6;
          }
          while (s1 != stats && freq > StFreq(s1 - 6));
          for (uint i = 0; i < 6; i++) _base[s1 + i] = tmp[(int)i];
        }
      }
      while (--i2 != 0);

      if (StFreq(s) == 0)
      {
        int i = 0;
        do { i++; s -= 6; } while (StFreq(s) == 0);

        escFreq += i;
        uint mc = _minContext;
        int numStats = CtxNumStats(mc);
        int numStatsNew = numStats - i;
        SetCtxNumStats(mc, numStatsNew);
        int n0 = (numStats + 1) >> 1;

        if (numStatsNew == 1)
        {
          int freq = StFreq(stats);
          do
          {
            escFreq >>= 1;
            freq = (freq + 1) >> 1;
          }
          while (escFreq > 1);

          uint os = OneState(mc);
          CopyState(os, stats);
          SetStFreq(os, (byte)freq);
          _foundState = os;
          InsertNode(stats, U2I((uint)n0));
          return;
        }

        int n1 = (numStatsNew + 1) >> 1;
        if (n0 != n1)
        {
          int i0 = U2I((uint)n0);
          int i1 = U2I((uint)n1);
          if (i0 != i1)
          {
            if (_freeList[i1] != 0)
            {
              uint ptr = RemoveNode(i1);
              SetCtxStats(mc, ptr);
              MemCpyUnits(ptr, stats, (uint)n1);
              InsertNode(stats, i0);
            }
            else
            {
              SplitBlock(stats, i0, i1);
            }
          }
        }
      }

      SetCtxSummFreq(_minContext, sumFreq + escFreq - (escFreq >> 1));
      _foundState = CtxStats(_minContext);
    }

    private See MakeEscFreq(int numMasked, out uint escFreq)
    {
      uint mc = _minContext;
      int numStats = CtxNumStats(mc);

      if (numStats != 256)
      {
        int nonMasked = numStats - numMasked;
        int idx = _ns2Indx[nonMasked - 1];
        int col = (nonMasked < CtxNumStats(CtxSuffix(mc)) - numStats ? 1 : 0)
                + 2 * (CtxSummFreq(mc) < 11 * numStats ? 1 : 0)
                + 4 * (numMasked > nonMasked ? 1 : 0)
                + _hiBitsFlag;

        See see = _see[idx][col];
        int summ = (ushort)see.Summ;
        int r = summ >> see.Shift;
        see.Summ = (ushort)(summ - r);
        escFreq = (uint)(r + (r == 0 ? 1 : 0));
        return see;
      }

      escFreq = 1;
      return _dummySee;
    }

    private static void SeeUpdate(See p)
    {
      if (p.Shift < PeriodBits && --p.Count == 0)
      {
        p.Summ = (ushort)(p.Summ << 1);
        p.Count = (byte)(3 << p.Shift++);
      }
    }

    private void NextContext()
    {
      uint c = StSuccessor(_foundState);
      if (_orderFall == 0 && c > _text)
        _maxContext = _minContext = c;
      else
        UpdateModel();
    }

    private void Update1()
    {
      uint s = _foundState;
      int freq = StFreq(s) + 4;
      SetCtxSummFreq(_minContext, CtxSummFreq(_minContext) + 4);
      SetStFreq(s, (byte)freq);
      if (freq > StFreq(s - 6))
      {
        SwapStates(s, s - 6);
        _foundState = s - 6;
        if (freq > MaxFreq)
          Rescale();
      }

      NextContext();
    }

    private void Update1_0()
    {
      uint s = _foundState;
      uint mc = _minContext;
      int freq = StFreq(s);
      int summFreq = CtxSummFreq(mc);
      _prevSuccess = 2 * freq > summFreq ? 1 : 0;
      _runLength += _prevSuccess;
      SetCtxSummFreq(mc, summFreq + 4);
      freq += 4;
      SetStFreq(s, (byte)freq);
      if (freq > MaxFreq)
        Rescale();

      NextContext();
    }

    private void Update2()
    {
      uint s = _foundState;
      int freq = StFreq(s) + 4;
      _runLength = _initRL;
      SetCtxSummFreq(_minContext, CtxSummFreq(_minContext) + 4);
      SetStFreq(s, (byte)freq);
      if (freq > MaxFreq)
        Rescale();

      UpdateModel();
    }

    private void UpdateBin()
    {
      uint s = _foundState;
      int freq = StFreq(s);
      SetStFreq(s, (byte)(freq + (freq < 128 ? 1 : 0)));
      _prevSuccess = 1;
      _runLength++;
      NextContext();
    }

    // ============================================================
    // Range decoder (Ppmd7z)
    // ============================================================

    private byte ReadByte() => _inPos < _input.Length ? _input[_inPos++] : (byte)0;

    public bool RangeDecInit()
    {
      _code = 0;
      _range = 0xFFFFFFFF;
      if (ReadByte() != 0)
        return false;

      for (int i = 0; i < 4; i++)
        _code = (_code << 8) | ReadByte();

      return _code < 0xFFFFFFFF;
    }

    private void Normalize()
    {
      while (_range < TopValue)
      {
        _code = (_code << 8) | ReadByte();
        _range <<= 8;
      }
    }

    private uint GetThreshold(uint total)
    {
      _range /= total;
      return _code / _range;
    }

    private void RcDecode(uint start, uint size)
    {
      _code -= start * _range;
      _range *= size;
      Normalize();
    }

    // ============================================================
    // DecodeSymbol (Ppmd7z_DecodeSymbol)
    // ============================================================

    public int DecodeSymbol()
    {
      Span<byte> mask = _charMask;

      if (CtxNumStats(_minContext) != 1)
      {
        uint s = CtxStats(_minContext);
        int summFreq = CtxSummFreq(_minContext);

        uint count = GetThreshold((uint)summFreq);
        uint hiCnt = count;

        uint f = StFreq(s);
        if ((int)(count - f) < 0)
        {
          RcDecode(0, f);
          _foundState = s;
          byte sym = StSymbol(s);
          Update1_0();
          return sym;
        }

        count -= f;
        _prevSuccess = 0;
        int i = CtxNumStats(_minContext) - 1;

        do
        {
          s += 6;
          f = StFreq(s);
          if ((int)(count - f) < 0)
          {
            RcDecode(hiCnt - count, f);
            _foundState = s;
            byte sym = StSymbol(s);
            Update1();
            return sym;
          }

          count -= f;
        }
        while (--i != 0);

        if (hiCnt >= (uint)summFreq)
          return -2;

        hiCnt -= count;
        RcDecode(hiCnt, (uint)summFreq - hiCnt);

        _hiBitsFlag = HiBitsFlag3(StSymbol(_foundState));
        mask.Fill(0xFF);
        mask[StSymbol(s)] = 0;
        int ii = CtxNumStats(_minContext) - 1;
        uint s2 = CtxStats(_minContext);
        do
        {
          mask[StSymbol(s2)] = 0;
          s2 += 6;
        }
        while (s2 != s);
      }
      else
      {
        uint s = OneState(_minContext);
        int binSummRow = StFreq(s) - 1;
        int binSummCol = _prevSuccess
            + ((_runLength >> 26) & 0x20)
            + _ns2bsIndx[CtxNumStats(CtxSuffix(_minContext)) - 1]
            + HiBitsFlag4(StSymbol(s))
            + (_hiBitsFlag = HiBitsFlag3(StSymbol(_foundState)));

        ushort prob = _binSumm[binSummRow, binSummCol];
        uint pr = prob;
        uint size0 = (_range >> 14) * pr;
        pr = (uint)(pr - ((pr + (1 << (PeriodBits - 2))) >> PeriodBits)); // PPMD_UPDATE_PROB_1

        if (_code < size0)
        {
          _binSumm[binSummRow, binSummCol] = (ushort)(pr + (1 << IntBits));
          _range = size0;
          Normalize();
          byte sym = StSymbol(s);
          _foundState = s;
          UpdateBin();
          return sym;
        }

        _binSumm[binSummRow, binSummCol] = (ushort)pr;
        _initEsc = _expEscape[pr >> 10];

        _code -= size0;
        _range -= size0;
        Normalize();

        mask.Fill(0xFF);
        mask[StSymbol(OneState(_minContext))] = 0;
        _prevSuccess = 0;
      }

      for (; ; )
      {
        uint mc = _minContext;
        int numMasked = CtxNumStats(mc);

        do
        {
          _orderFall++;
          if (CtxSuffix(mc) == 0)
            return -1;
          mc = CtxSuffix(mc);
        }
        while (CtxNumStats(mc) == numMasked);

        uint s = CtxStats(mc);
        uint hiCnt = 0;
        int num = CtxNumStats(mc);
        _minContext = mc;

        for (int k = 0; k < num; k++)
        {
          byte sym = StSymbol(s);
          if (mask[sym] != 0)
            hiCnt += StFreq(s);
          s += 6;
        }

        See see = MakeEscFreq(numMasked, out uint freqSum);
        freqSum += hiCnt;

        uint count = GetThreshold(freqSum);

        if (count < hiCnt)
        {
          s = CtxStats(_minContext);
          hiCnt = count;
          for (; ; )
          {
            byte sym = StSymbol(s);
            if (mask[sym] != 0)
            {
              if ((int)(count - StFreq(s)) < 0)
                break;
              count -= StFreq(s);
            }
            s += 6;
          }

          RcDecode(hiCnt - count, StFreq(s));
          SeeUpdate(see);
          _foundState = s;
          byte symbol = StSymbol(s);
          Update2();
          return symbol;
        }

        if (count >= freqSum)
          return -2;

        RcDecode(hiCnt, freqSum - hiCnt);
        see.Summ = (ushort)(see.Summ + freqSum);

        s = CtxStats(_minContext);
        int n = CtxNumStats(_minContext);
        for (int k = 0; k < n; k++)
        {
          mask[StSymbol(s)] = 0;
          s += 6;
        }
      }
    }

    private static int HiBitsFlag3(int sym) => ((sym + 0xC0) >> (8 - 3)) & (1 << 3);
    private static int HiBitsFlag4(int sym) => ((sym + 0xC0) >> (8 - 4)) & (1 << 4);
  }
}
