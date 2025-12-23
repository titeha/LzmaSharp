// SPDX-License-Identifier: MIT
// Порт LzmaDec.c/LzmaDec.h из LZMA SDK 24.09 (публичное достояние).
// ВАЖНО: это низкоуровневое ядро. Мы держим логику максимально близко к C-коду,
// а "шарповый" API будет выше уровнем (Stream/Span и т.п.).

using System.Runtime.CompilerServices;

namespace LzmaCore.SevenZip;

internal struct LzmaProps
{
  public byte Lc;
  public byte Lp;
  public byte Pb;
  public uint DicSize;
}

internal struct CLzmaDec
{
  public LzmaProps Prop;

  // Вероятности (CLzmaProb). В SDK это UInt16 (если не включен Z7_LZMA_PROB32).
  public ushort[]? Probs;
  public int NumProbs;

  // Словарь
  public byte[]? Dic;
  public int DicBufSize;
  public int DicPos;

  // RangeCoder
  public uint Range;
  public uint Code;

  // Позиция/контроль словаря
  public uint ProcessedPos;
  public uint CheckDicSize;

  // Репы и состояние
  public uint Rep0;
  public uint Rep1;
  public uint Rep2;
  public uint Rep3;
  public uint State;
  public uint RemainLen;

  // Буфер для "догрузки" входа (до 20 байт)
  public int TempBufSize;
  public byte[]? TempBuf;
}

internal static class LzmaDec
{
  public const int LZMA_PROPS_SIZE = 5;

  // ----- Константы из LzmaDec.c -----

  private const uint _kTopValue = 1u << 24;

  private const int _kNumBitModelTotalBits = 11;
  private const uint _kBitModelTotal = 1u << _kNumBitModelTotalBits;

  private const int RC_INIT_SIZE = 5;
  private const int LZMA_REQUIRED_INPUT_MAX = 20;

  private const int _kNumMoveBits = 5;

  private const int _kNumPosBitsMax = 4;
  private const int _kNumPosStatesMax = 1 << _kNumPosBitsMax;

  private const int _kLenNumLowBits = 3;
  private const int _kLenNumLowSymbols = 1 << _kLenNumLowBits;
  private const int _kLenNumHighBits = 8;
  private const int _kLenNumHighSymbols = 1 << _kLenNumHighBits;

  private const int LenLow = 0;
  private const int LenHigh = LenLow + 2 * (_kNumPosStatesMax << _kLenNumLowBits);
  private const int _kNumLenProbs = LenHigh + _kLenNumHighSymbols;

  private const int LenChoice = LenLow;
  private const int LenChoice2 = LenLow + (1 << _kLenNumLowBits);

  private const int _kNumStates = 12;
  private const int _kNumStates2 = 16;
  private const int _kNumLitStates = 7;

  private const int _kStartPosModelIndex = 4;
  private const int _kEndPosModelIndex = 14;
  private const int _kNumFullDistances = 1 << (_kEndPosModelIndex >> 1);

  private const int _kNumPosSlotBits = 6;
  private const int _kNumLenToPosStates = 4;

  private const int _kNumAlignBits = 4;
  private const int _kAlignTableSize = 1 << _kNumAlignBits;

  private const int _kMatchMinLen = 2;
  private const int _kMatchSpecLenStart = _kMatchMinLen + _kLenNumLowSymbols * 2 + _kLenNumHighSymbols;

  private const int _kMatchSpecLen_Error_Data = 1 << 9;            // 512
  private const int _kMatchSpecLen_Error_Fail = _kMatchSpecLen_Error_Data - 1; // 511

  private const int _kStartOffset = 1664;

  private const int SpecPos = -_kStartOffset;
  private const int IsRep0Long = SpecPos + _kNumFullDistances;
  private const int RepLenCoder = IsRep0Long + (_kNumStates2 << _kNumPosBitsMax);
  private const int LenCoder = RepLenCoder + _kNumLenProbs;
  private const int IsMatch = LenCoder + _kNumLenProbs;
  private const int Align = IsMatch + (_kNumStates2 << _kNumPosBitsMax);
  private const int IsRep = Align + _kAlignTableSize;
  private const int IsRepG0 = IsRep + _kNumStates;
  private const int IsRepG1 = IsRepG0 + _kNumStates;
  private const int IsRepG2 = IsRepG1 + _kNumStates;
  private const int PosSlot = IsRepG2 + _kNumStates;
  private const int Literal = PosSlot + (_kNumLenToPosStates << _kNumPosSlotBits);
  private const int NUM_BASE_PROBS = Literal + _kStartOffset; // 1984

  private const int LZMA_LIT_SIZE = 0x300;
  private const uint LZMA_DIC_MIN = 1u << 12;

  // kBadRepCode расчёт из C (для ранней проверки невозможного REP в начале потока)
  private const uint _kRange0 = 0xFFFFFFFF;
  private const uint _kBound0 = ((_kRange0 >> _kNumBitModelTotalBits) << (_kNumBitModelTotalBits - 1));
  private const uint _kBadRepCode = _kBound0 + (((_kRange0 - _kBound0) >> _kNumBitModelTotalBits) << (_kNumBitModelTotalBits - 1));

  private static int AbsProb(int rel) => _kStartOffset + rel;

  private static void EnsureTempBuf(ref CLzmaDec p)
  {
    p.TempBuf ??= new byte[LZMA_REQUIRED_INPUT_MAX];
  }

  private static int GetNumProbs(in LzmaProps prop)
  {
    // NUM_BASE_PROBS + (LZMA_LIT_SIZE << (lc+lp))
    int shift = prop.Lc + prop.Lp;
    // максимум: 768 << 12 = 3_145_728 (влезает в int)
    return NUM_BASE_PROBS + (LZMA_LIT_SIZE << shift);
  }

  public static int PropsDecode(out LzmaProps p, ReadOnlySpan<byte> data)
  {
    p = default;

    if (data.Length < LZMA_PROPS_SIZE)
      return Sz.ERROR_UNSUPPORTED;

    uint dicSize = (uint)(data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24));
    if (dicSize < LZMA_DIC_MIN)
      dicSize = LZMA_DIC_MIN;
    p.DicSize = dicSize;

    byte d = data[0];
    if (d >= (9 * 5 * 5))
      return Sz.ERROR_UNSUPPORTED;

    p.Lc = (byte)(d % 9);
    d /= 9;
    p.Pb = (byte)(d / 5);
    p.Lp = (byte)(d % 5);

    return Sz.OK;
  }

  private static int AllocateProbs2(ref CLzmaDec p, in LzmaProps propNew)
  {
    int numProbs = GetNumProbs(propNew);
    if (p.Probs is null || p.NumProbs != numProbs)
    {
      p.Probs = new ushort[numProbs];
      p.NumProbs = numProbs;
    }
    return Sz.OK;
  }

  public static int AllocateProbs(ref CLzmaDec p, ReadOnlySpan<byte> props)
  {
    int res = PropsDecode(out LzmaProps propNew, props);
    if (res != Sz.OK)
      return res;

    res = AllocateProbs2(ref p, propNew);
    if (res != Sz.OK)
      return res;

    p.Prop = propNew;
    return Sz.OK;
  }

  public static int Allocate(ref CLzmaDec p, ReadOnlySpan<byte> props)
  {
    int res = PropsDecode(out LzmaProps propNew, props);
    if (res != Sz.OK)
      return res;

    res = AllocateProbs2(ref p, propNew);
    if (res != Sz.OK)
      return res;

    // В C словарь округляется маской; в managed оставим ровный размер dicSize (или уже заданный снаружи).
    uint dictSize = propNew.DicSize;
    if (dictSize > int.MaxValue)
      return Sz.ERROR_MEM;

    int dicBufSize = (int)dictSize;
    if (p.Dic is null || p.DicBufSize != dicBufSize)
    {
      p.Dic = new byte[dicBufSize];
      p.DicBufSize = dicBufSize;
    }

    p.Prop = propNew;
    return Sz.OK;
  }

  public static void InitDicAndState(ref CLzmaDec p, bool initDic, bool initState)
  {
    EnsureTempBuf(ref p);

    p.RemainLen = _kMatchSpecLenStart + 1;
    p.TempBufSize = 0;

    if (initDic)
    {
      p.ProcessedPos = 0;
      p.CheckDicSize = 0;
      p.RemainLen = _kMatchSpecLenStart + 2;
    }
    if (initState)
      p.RemainLen = _kMatchSpecLenStart + 2;
  }

  public static void Init(ref CLzmaDec p)
  {
    p.DicPos = 0;
    InitDicAndState(ref p, initDic: true, initState: true);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void Normalize(ReadOnlySpan<byte> src, ref int buf, ref uint range, ref uint code)
  {
    if (range < _kTopValue)
    {
      range <<= 8;
      code = (code << 8) | src[buf++];
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint GetBit(ref ushort prob, ReadOnlySpan<byte> src, ref int buf, ref uint range, ref uint code)
  {
    uint ttt = prob;
    Normalize(src, ref buf, ref range, ref code);
    uint bound = (range >> _kNumBitModelTotalBits) * ttt;
    if (code < bound)
    {
      range = bound;
      prob = (ushort)(ttt + ((_kBitModelTotal - ttt) >> _kNumMoveBits));
      return 0;
    }
    else
    {
      range -= bound;
      code -= bound;
      prob = (ushort)(ttt - (ttt >> _kNumMoveBits));
      return 1;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool NormalizeCheck(ReadOnlySpan<byte> buf, int bufLimit, ref int pos, ref uint range, ref uint code)
  {
    if (range < _kTopValue)
    {
      if (pos >= bufLimit)
        return false;
      range <<= 8;
      code = (code << 8) | buf[pos++];
    }
    return true;
  }

  // Вариант чтения бита "для проверки" (без изменения prob-модели).
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool TryGetBitCheck(ushort prob, ReadOnlySpan<byte> buf, int bufLimit, ref int pos, ref uint range, ref uint code, out uint bit)
  {
    bit = 0;

    uint ttt = prob;
    if (!NormalizeCheck(buf, bufLimit, ref pos, ref range, ref code))
      return false;

    uint bound = (range >> _kNumBitModelTotalBits) * ttt;
    if (code < bound)
    {
      range = bound;
      bit = 0;
    }
    else
    {
      range -= bound;
      code -= bound;
      bit = 1;
    }
    return true;
  }

  private enum LzmaDummy : int
  {
    InputEof,
    Lit,
    Match,
    Rep
  }

  private static bool IsDummyEndMarkerPossible(LzmaDummy dummyRes) => dummyRes == LzmaDummy.Match;

  private static LzmaDummy TryDummy(in CLzmaDec p, ReadOnlySpan<byte> buf, int bufLimit, out int processed)
  {
    // ВАЖНО: в этом режиме мы НЕ обновляем вероятности p.Probs (как и в C-версии).
    uint range = p.Range;
    uint code = p.Code;

    var probsArr = p.Probs!;
    int baseIdx = _kStartOffset;
    uint state = p.State;

    for (; ; )
    {
      uint bound;
      uint bit;
      int pos = 0;

      uint pbMask = (uint)((1u << p.Prop.Pb) - 1u);
      uint posState = ((p.ProcessedPos & pbMask) << 4);

      int probIndex = baseIdx + IsMatch + (int)(posState + state);
      if (!TryGetBitCheck(probsArr[probIndex], buf, bufLimit, ref pos, ref range, ref code, out bit))
      {
        processed = 0;
        return LzmaDummy.InputEof;
      }

      if (bit == 0)
      {
        // Literal
        int litBase = baseIdx + Literal;

        if (p.CheckDicSize != 0 || p.ProcessedPos != 0)
        {
          // prob += LZMA_LIT_SIZE * ( ((processedPos & ((1<<lp)-1))<<lc) + (prevByte >> (8-lc)) )
          uint prevByte = p.Dic![(p.DicPos == 0 ? p.DicBufSize : p.DicPos) - 1];
          uint ctx = ((p.ProcessedPos & ((1u << p.Prop.Lp) - 1u)) << p.Prop.Lc)
                   + (prevByte >> (8 - p.Prop.Lc));
          litBase += (int)(LZMA_LIT_SIZE * ctx);
        }

        if (state < _kNumLitStates)
        {
          uint symbol = 1;
          while (symbol < 0x100)
          {
            int pi = litBase + (int)symbol;
            if (!TryGetBitCheck(probsArr[pi], buf, bufLimit, ref pos, ref range, ref code, out bit))
            {
              processed = 0;
              return LzmaDummy.InputEof;
            }
            symbol = (symbol << 1) + bit;
          }
        }
        else
        {
          uint matchByte = p.Dic![p.DicPos - (int)p.Rep0 + (p.DicPos < p.Rep0 ? p.DicBufSize : 0)];
          uint offs = 0x100;
          uint symbol = 1;
          while (symbol < 0x100)
          {
            matchByte <<= 1;
            uint b2 = offs;
            offs &= matchByte;
            int pi = litBase + (int)(offs + b2 + symbol);
            if (!TryGetBitCheck(probsArr[pi], buf, bufLimit, ref pos, ref range, ref code, out bit))
            {
              processed = 0;
              return LzmaDummy.InputEof;
            }
            if (bit == 0)
              offs ^= b2;
            symbol = (symbol << 1) + bit;
          }
        }

        // В конце C делает NORMALIZE_CHECK. Мы повторим отдельно.
        if (!NormalizeCheck(buf, bufLimit, ref pos, ref range, ref code))
        {
          processed = 0;
          return LzmaDummy.InputEof;
        }

        processed = pos;
        return LzmaDummy.Lit;
      }
      else
      {
        // Match / Rep
        int probIsRep = baseIdx + IsRep + (int)state;
        if (!TryGetBitCheck(probsArr[probIsRep], buf, bufLimit, ref pos, ref range, ref code, out bit))
        {
          processed = 0;
          return LzmaDummy.InputEof;
        }

        int lenCoderBase;
        LzmaDummy res;

        if (bit == 0)
        {
          // Non-Rep Match
          state = 0;
          lenCoderBase = baseIdx + LenCoder;
          res = LzmaDummy.Match;
        }
        else
        {
          // Rep
          res = LzmaDummy.Rep;

          int probIsRepG0 = baseIdx + IsRepG0 + (int)state;
          if (!TryGetBitCheck(probsArr[probIsRepG0], buf, bufLimit, ref pos, ref range, ref code, out bit))
          {
            processed = 0;
            return LzmaDummy.InputEof;
          }
          if (bit == 0)
          {
            int probIsRep0Long = baseIdx + IsRep0Long + (int)(posState + state);
            if (!TryGetBitCheck(probsArr[probIsRep0Long], buf, bufLimit, ref pos, ref range, ref code, out bit))
            {
              processed = 0;
              return LzmaDummy.InputEof;
            }
            if (bit == 0)
            {
              // REP0_LONG (len=1)
              if (!NormalizeCheck(buf, bufLimit, ref pos, ref range, ref code))
              {
                processed = 0;
                return LzmaDummy.InputEof;
              }
              processed = pos;
              return res;
            }
          }
          else
          {
            int probIsRepG1 = baseIdx + IsRepG1 + (int)state;
            if (!TryGetBitCheck(probsArr[probIsRepG1], buf, bufLimit, ref pos, ref range, ref code, out bit))
            {
              processed = 0;
              return LzmaDummy.InputEof;
            }
            if (bit == 1)
            {
              int probIsRepG2 = baseIdx + IsRepG2 + (int)state;
              if (!TryGetBitCheck(probsArr[probIsRepG2], buf, bufLimit, ref pos, ref range, ref code, out bit))
              {
                processed = 0;
                return LzmaDummy.InputEof;
              }
            }
          }

          state = _kNumStates;
          lenCoderBase = baseIdx + RepLenCoder;
        }

        // Decode len (без обновления prob!)
        uint len;
        int probLenChoice = lenCoderBase + LenChoice;
        if (!TryGetBitCheck(probsArr[probLenChoice], buf, bufLimit, ref pos, ref range, ref code, out bit))
        {
          processed = 0;
          return LzmaDummy.InputEof;
        }

        int probLenBase;
        int limit;
        int offset;

        if (bit == 0)
        {
          probLenBase = lenCoderBase + LenLow + (int)posState;
          offset = 0;
          limit = 1 << _kLenNumLowBits;
        }
        else
        {
          int probLenChoice2 = lenCoderBase + LenChoice2;
          if (!TryGetBitCheck(probsArr[probLenChoice2], buf, bufLimit, ref pos, ref range, ref code, out bit))
          {
            processed = 0;
            return LzmaDummy.InputEof;
          }

          if (bit == 0)
          {
            probLenBase = lenCoderBase + LenLow + (int)posState + (1 << _kLenNumLowBits);
            offset = _kLenNumLowSymbols;
            limit = 1 << _kLenNumLowBits;
          }
          else
          {
            probLenBase = lenCoderBase + LenHigh;
            offset = _kLenNumLowSymbols * 2;
            limit = 1 << _kLenNumHighBits;
          }
        }

        // TREE_DECODE_CHECK
        uint iTree = 1;
        while (iTree < (uint)limit)
        {
          int pi = probLenBase + (int)iTree;
          if (!TryGetBitCheck(probsArr[pi], buf, bufLimit, ref pos, ref range, ref code, out bit))
          {
            processed = 0;
            return LzmaDummy.InputEof;
          }
          iTree = (iTree << 1) + bit;
        }
        len = iTree - (uint)limit;
        len += (uint)offset;

        // Для end-marker возможен только Non-Rep Match (как и в C: state < 4 проверка)
        if (state < 4)
        {
          uint posSlot;
          int posSlotBase = baseIdx + PosSlot + ((int)((len < (_kNumLenToPosStates - 1)) ? len : (_kNumLenToPosStates - 1)) << _kNumPosSlotBits);

          // TREE_DECODE_CHECK (6 бит)
          uint ps = 1;
          const uint lim = 1u << _kNumPosSlotBits;
          while (ps < lim)
          {
            int pi = posSlotBase + (int)ps;
            if (!TryGetBitCheck(probsArr[pi], buf, bufLimit, ref pos, ref range, ref code, out bit))
            {
              processed = 0;
              return LzmaDummy.InputEof;
            }
            ps = (ps << 1) + bit;
          }
          posSlot = ps - lim;

          if (posSlot >= _kStartPosModelIndex)
          {
            int numDirectBits = (int)((posSlot >> 1) - 1);

            int probBase2;
            if (posSlot < _kEndPosModelIndex)
            {
              probBase2 = baseIdx + SpecPos + (int)((2u | (posSlot & 1u)) << numDirectBits);
            }
            else
            {
              numDirectBits -= _kNumAlignBits;
              while (numDirectBits-- > 0)
              {
                if (!NormalizeCheck(buf, bufLimit, ref pos, ref range, ref code))
                {
                  processed = 0;
                  return LzmaDummy.InputEof;
                }
                range >>= 1;
                // branchless: code -= range if (code >= range)
                code -= range & (((code - range) >> 31) - 1);
              }
              probBase2 = baseIdx + Align;
              numDirectBits = _kNumAlignBits;
            }

            // REV_BIT_CHECK
            uint i = 1;
            uint m = 1;
            while (numDirectBits-- > 0)
            {
              int pi = probBase2 + (int)i;
              if (!TryGetBitCheck(probsArr[pi], buf, bufLimit, ref pos, ref range, ref code, out bit))
              {
                processed = 0;
                return LzmaDummy.InputEof;
              }

              if (bit == 0)
              {
                i += m;
                m += m;
              }
              else
              {
                m += m;
                i += m;
              }
            }
          }
        }

        if (!NormalizeCheck(buf, bufLimit, ref pos, ref range, ref code))
        {
          processed = 0;
          return LzmaDummy.InputEof;
        }

        processed = pos;
        return res;
      }
    }
  }

  private static void WriteRem(ref CLzmaDec p, int limit)
  {
    uint lenU = p.RemainLen;
    if (lenU == 0)
      return;

    int len = (int)lenU;

    int dicPos = p.DicPos;
    int rem = limit - dicPos;
    if (rem < len)
    {
      len = rem;
      if (len == 0)
        return;
    }

    if (p.CheckDicSize == 0 && unchecked(p.Prop.DicSize - p.ProcessedPos) <= (uint)len)
      p.CheckDicSize = p.Prop.DicSize;

    p.ProcessedPos += (uint)len;
    p.RemainLen -= (uint)len;

    var dic = p.Dic!;
    uint rep0 = p.Rep0;
    int dicBufSize = p.DicBufSize;

    while (len-- != 0)
    {
      int srcIndex = dicPos - (int)rep0 + (dicPos < rep0 ? dicBufSize : 0);
      dic[dicPos++] = dic[srcIndex];
    }

    p.DicPos = dicPos;
  }

  private static int DecodeReal3(ref CLzmaDec p, int limit, ReadOnlySpan<byte> src, ref int buf, int bufLimit)
  {
    var probsArr = p.Probs!;
    const int baseIdx = _kStartOffset;

    uint state = p.State;
    uint rep0 = p.Rep0, rep1 = p.Rep1, rep2 = p.Rep2, rep3 = p.Rep3;

    uint pbMask = (1u << p.Prop.Pb) - 1u;
    int lc = p.Prop.Lc;
    int lpMask = ((0x100 << p.Prop.Lp) - (0x100 >> lc));

    var dic = p.Dic!;
    int dicBufSize = p.DicBufSize;
    int dicPos = p.DicPos;

    uint processedPos = p.ProcessedPos;
    uint checkDicSize = p.CheckDicSize;

    int len = 0;

    uint range = p.Range;
    uint code = p.Code;

    do
    {
      uint bound;
      uint ttt;
      uint posState = ((processedPos & pbMask) << 4);

      // IsMatch
      int probIsMatch = baseIdx + IsMatch + (int)(posState + state);
      ttt = probsArr[probIsMatch];
      Normalize(src, ref buf, ref range, ref code);
      bound = (range >> _kNumBitModelTotalBits) * ttt;

      if (code < bound)
      {
        // Literal
        range = bound;
        probsArr[probIsMatch] = (ushort)(ttt + ((_kBitModelTotal - ttt) >> _kNumMoveBits));

        int prob = baseIdx + Literal;

        if (processedPos != 0 || checkDicSize != 0)
        {
          uint prevByte = dic[(dicPos == 0 ? dicBufSize : dicPos) - 1];
          uint ctx = ((processedPos << 8) + prevByte) & (uint)lpMask;
          prob += 3 * (int)(ctx << lc);
        }

        processedPos++;

        uint symbol = 1;
        if (state < _kNumLitStates)
        {
          // state переход как в C:
          state -= (state < 4 ? state : 3);

          // 8 бит
          for (int i = 0; i < 8; i++)
          {
            int pi = prob + (int)symbol;
            uint bit = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
            symbol = (symbol << 1) + bit;
          }
        }
        else
        {
          uint matchByte = dic[dicPos - (int)rep0 + (dicPos < rep0 ? dicBufSize : 0)];
          uint offs = 0x100;

          state = (uint)(state - (state < 10 ? 3 : 6));

          for (int i = 0; i < 8; i++)
          {
            matchByte <<= 1;
            uint bit = offs;
            offs &= matchByte;
            int pi = prob + (int)(offs + bit + symbol);
            uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
            if (b == 0)
              offs ^= bit;
            symbol = (symbol << 1) + b;
          }
        }

        dic[dicPos++] = (byte)symbol;
        continue;
      }

      // Not literal
      range -= bound;
      code -= bound;
      probsArr[probIsMatch] = (ushort)(ttt - (ttt >> _kNumMoveBits));

      int probIsRep = baseIdx + IsRep + (int)state;
      uint isRep = GetBit(ref probsArr[probIsRep], src, ref buf, ref range, ref code);

      int lenCoderBase;
      if (isRep == 0)
      {
        // Non-Rep match
        state += _kNumStates;
        lenCoderBase = baseIdx + LenCoder;
      }
      else
      {
        // Rep
        int probIsRepG0 = baseIdx + IsRepG0 + (int)state;
        uint isRepG0 = GetBit(ref probsArr[probIsRepG0], src, ref buf, ref range, ref code);
        if (isRepG0 == 0)
        {
          int probIsRep0Long = baseIdx + IsRep0Long + (int)(posState + state);
          uint isRep0Long = GetBit(ref probsArr[probIsRep0Long], src, ref buf, ref range, ref code);
          if (isRep0Long == 0)
          {
            dic[dicPos] = dic[dicPos - (int)rep0 + (dicPos < rep0 ? dicBufSize : 0)];
            dicPos++;
            processedPos++;
            state = state < _kNumLitStates ? 9u : 11u;
            continue;
          }
        }
        else
        {
          uint distance;
          int probIsRepG1 = baseIdx + IsRepG1 + (int)state;
          uint isRepG1 = GetBit(ref probsArr[probIsRepG1], src, ref buf, ref range, ref code);
          if (isRepG1 == 0)
          {
            distance = rep1;
          }
          else
          {
            int probIsRepG2 = baseIdx + IsRepG2 + (int)state;
            uint isRepG2 = GetBit(ref probsArr[probIsRepG2], src, ref buf, ref range, ref code);
            if (isRepG2 == 0)
            {
              distance = rep2;
            }
            else
            {
              distance = rep3;
              rep3 = rep2;
            }
            rep2 = rep1;
          }
          rep1 = rep0;
          rep0 = distance;
        }

        state = state < _kNumLitStates ? 8u : 11u;
        lenCoderBase = baseIdx + RepLenCoder;
      }

      // Decode len
      int probLenChoice = lenCoderBase + LenChoice;
      uint choice = GetBit(ref probsArr[probLenChoice], src, ref buf, ref range, ref code);
      int probLenBase;
      int offset;
      if (choice == 0)
      {
        probLenBase = lenCoderBase + LenLow + (int)posState;
        len = 1;
        for (int i = 0; i < 3; i++)
        {
          int pi = probLenBase + len;
          uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
          len = (len << 1) + (int)b;
        }
        len -= 8;
        offset = 0;
      }
      else
      {
        int probLenChoice2 = lenCoderBase + LenChoice2;
        uint choice2 = GetBit(ref probsArr[probLenChoice2], src, ref buf, ref range, ref code);
        if (choice2 == 0)
        {
          probLenBase = lenCoderBase + LenLow + (int)posState + (1 << _kLenNumLowBits);
          len = 1;
          for (int i = 0; i < 3; i++)
          {
            int pi = probLenBase + len;
            uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
            len = (len << 1) + (int)b;
          }
          offset = _kLenNumLowSymbols;
        }
        else
        {
          probLenBase = lenCoderBase + LenHigh;
          // TREE_DECODE (8 бит)
          int iTree = 1;
          for (; ; )
          {
            int pi = probLenBase + iTree;
            uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
            iTree = (iTree << 1) + (int)b;
            if (iTree >= (1 << _kLenNumHighBits))
              break;
          }
          len = iTree - (1 << _kLenNumHighBits);
          offset = _kLenNumLowSymbols * 2;
        }
      }

      if (state >= _kNumStates)
      {
        // Non-Rep: decode distance
        int posSlotBase = baseIdx + PosSlot + ((len < _kNumLenToPosStates ? len : _kNumLenToPosStates - 1) << _kNumPosSlotBits);
        int distance = 1;
        for (int i = 0; i < 6; i++)
        {
          int pi = posSlotBase + distance;
          uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
          distance = (distance << 1) + (int)b;
        }
        distance -= 0x40;

        if (distance >= _kStartPosModelIndex)
        {
          int posSlot = distance;
          int numDirectBits = ((distance >> 1) - 1);
          uint dist = (uint)(2 | (distance & 1));

          if (posSlot < _kEndPosModelIndex)
          {
            dist <<= numDirectBits;
            const int probSpecPos = baseIdx + SpecPos;

            uint m = 1;
            dist++;
            //while (numDirectBits-- != 0)
            //{
            //  // REV_BIT_VAR(prob, dist, m)
            //  int pi = probSpecPos + (int)dist;
            //  uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
            //  if (b == 0)
            //  {
            //    dist += m;
            //    m += m;
            //    m += m;
            //    dist += m;
            //  }
            //  // else: ничего
            //}
            do
            {
              // REV_BIT_VAR(prob, dist, m)
              int pi = probSpecPos + (int)dist;
              uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);

              if (b == 0)
              {
                dist += m;
                m += m;
              }
              else
              {
                m += m;
                dist += m;
              }
            }
            while (--numDirectBits != 0);
            dist -= m;
          }
          else
          {
            numDirectBits -= _kNumAlignBits;
            while (numDirectBits-- != 0)
            {
              Normalize(src, ref buf, ref range, ref code);
              range >>= 1;

              uint t = unchecked(0u - (code >> 31));
              code = unchecked(code - range);
              dist = (dist << 1) + (t + 1);
              code = unchecked(code + (range & t));
            }

            const int probAlign = baseIdx + Align;
            dist <<= _kNumAlignBits;

            uint i = 1;
            // REV_BIT_CONST(prob, i, 1)
            {
              int pi = probAlign + (int)i;
              uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
              i += (b == 0) ? 1u : 2u;
            }
            // REV_BIT_CONST(prob, i, 2)
            {
              int pi = probAlign + (int)i;
              uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
              i += (b == 0) ? 2u : 4u;
            }
            // REV_BIT_CONST(prob, i, 4)
            {
              int pi = probAlign + (int)i;
              uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
              i += (b == 0) ? 4u : 8u;
            }
            // REV_BIT_LAST(prob, i, 8)
            {
              int pi = probAlign + (int)i;
              uint b = GetBit(ref probsArr[pi], src, ref buf, ref range, ref code);
              if (b == 0)
                i -= 8u;
            }
            dist |= i;

            if (dist == 0xFFFFFFFF)
            {
              len = _kMatchSpecLenStart;
              state -= _kNumStates;
              break;
            }
          }

          rep3 = rep2;
          rep2 = rep1;
          rep1 = rep0;
          rep0 = dist + 1;

          state = (state < _kNumStates + _kNumLitStates) ? _kNumLitStates : (uint)(_kNumLitStates + 3);

          uint check = (checkDicSize == 0) ? processedPos : checkDicSize;
          if (dist >= check)
          {
            len += _kMatchSpecLen_Error_Data + _kMatchMinLen;
            break;
          }
        }
      }

      len += _kMatchMinLen;

      // Copy match bytes to dictionary
      int rem2 = limit - dicPos;
      if (rem2 == 0)
        break;

      int curLen = rem2 < len ? rem2 : len;
      int pos2 = dicPos - (int)rep0 + (dicPos < rep0 ? dicBufSize : 0);

      processedPos += (uint)curLen;
      len -= curLen;

      if (curLen <= dicBufSize - pos2)
      {
        int srcOffset = pos2 - dicPos;
        int end = dicPos + curLen;
        while (dicPos < end)
        {
          dic[dicPos] = dic[dicPos + srcOffset];
          dicPos++;
        }
      }
      else
      {
        while (curLen-- != 0)
        {
          dic[dicPos++] = dic[pos2];
          pos2++;
          if (pos2 == dicBufSize)
            pos2 = 0;
        }
      }
    }
    while (dicPos < limit && buf < bufLimit);

    Normalize(src, ref buf, ref range, ref code);

    // Сохраняем состояние
    p.Range = range;
    p.Code = code;
    p.RemainLen = (uint)len;
    p.DicPos = dicPos;
    p.ProcessedPos = processedPos;
    p.CheckDicSize = checkDicSize;
    p.Rep0 = rep0;
    p.Rep1 = rep1;
    p.Rep2 = rep2;
    p.Rep3 = rep3;
    p.State = state;

    if (len >= _kMatchSpecLen_Error_Data)
      return Sz.ERROR_DATA;

    return Sz.OK;
  }

  private static int DecodeReal2(ref CLzmaDec p, int limit, ReadOnlySpan<byte> src, ref int buf, int bufLimit)
  {
    if (p.CheckDicSize == 0)
    {
      uint rem = unchecked(p.Prop.DicSize - p.ProcessedPos);
      int avail = limit - p.DicPos;
      if (avail > 0 && rem < (uint)avail)
        limit = p.DicPos + (int)rem;
    }

    int res = DecodeReal3(ref p, limit, src, ref buf, bufLimit);

    if (p.CheckDicSize == 0 && p.ProcessedPos >= p.Prop.DicSize)
      p.CheckDicSize = p.Prop.DicSize;

    return res;
  }

  public static int DecodeToDic(
      ref CLzmaDec p,
      int dicLimit,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      LzmaFinishMode finishMode,
      ref LzmaStatus status)
  {
    EnsureTempBuf(ref p);

    int inSize = srcLen;
    srcLen = 0;
    status = LzmaStatus.NotSpecified;

    if (p.Dic is null)
      throw new InvalidOperationException("Словарь (Dic) не задан. Для LZMA2 его задаёт внешний уровень.");

    if (p.Probs is null)
      throw new InvalidOperationException("Вероятности (Probs) не выделены. Сначала вызови AllocateProbs().");

    // Инициализация range coder / probs
    if (p.RemainLen > _kMatchSpecLenStart)
    {
      if (p.RemainLen > _kMatchSpecLenStart + 2)
        return p.RemainLen == _kMatchSpecLen_Error_Fail ? Sz.ERROR_FAIL : Sz.ERROR_DATA;

      // Набираем 5 байт RC_INIT_SIZE (первый обязан быть 0)
      while (inSize > 0 && p.TempBufSize < RC_INIT_SIZE)
      {
        p.TempBuf![p.TempBufSize++] = src[srcLen++];
        inSize--;
      }

      if (p.TempBufSize != 0 && p.TempBuf![0] != 0)
        return Sz.ERROR_DATA;

      if (p.TempBufSize < RC_INIT_SIZE)
      {
        status = LzmaStatus.NeedsMoreInput;
        return Sz.OK;
      }

      p.Code = ((uint)p.TempBuf![1] << 24)
             | ((uint)p.TempBuf![2] << 16)
             | ((uint)p.TempBuf![3] << 8)
             | (uint)p.TempBuf![4];

      if (p.CheckDicSize == 0 && p.ProcessedPos == 0 && p.Code >= _kBadRepCode)
        return Sz.ERROR_DATA;

      p.Range = 0xFFFFFFFF;
      p.TempBufSize = 0;

      if (p.RemainLen > _kMatchSpecLenStart + 1)
      {
        // init probs/state
        int numProbs = GetNumProbs(p.Prop);
        var probsArr = p.Probs!;
        for (int i = 0; i < numProbs; i++)
          probsArr[i] = (ushort)(_kBitModelTotal >> 1);

        p.Rep0 = p.Rep1 = p.Rep2 = p.Rep3 = 1;
        p.State = 0;
      }

      p.RemainLen = 0;
    }

    for (; ; )
    {
      if (p.RemainLen == _kMatchSpecLenStart)
      {
        if (p.Code != 0)
          return Sz.ERROR_DATA;

        status = LzmaStatus.FinishedWithMark;
        return Sz.OK;
      }

      WriteRem(ref p, dicLimit);

      bool checkEndMarkNow = false;

      if (p.DicPos >= dicLimit)
      {
        if (p.RemainLen == 0 && p.Code == 0)
        {
          status = LzmaStatus.MaybeFinishedWithoutMark;
          return Sz.OK;
        }
        if (finishMode == LzmaFinishMode.Any)
        {
          status = LzmaStatus.NotFinished;
          return Sz.OK;
        }
        if (p.RemainLen != 0)
        {
          status = LzmaStatus.NotFinished;
          return Sz.ERROR_DATA; // строгий режим
        }
        checkEndMarkNow = true;
      }

      // p.RemainLen == 0
      if (p.TempBufSize == 0)
      {
        int dummyProcessed = -1;
        int bufLimitPos;

        if (inSize < LZMA_REQUIRED_INPUT_MAX || checkEndMarkNow)
        {
          // Делаем "пробный" разбор, чтобы понять, есть ли достаточно входа
          var dummyRes = TryDummy(p, src.Slice(srcLen, inSize), inSize, out dummyProcessed);

          if (dummyRes == LzmaDummy.InputEof)
          {
            if (inSize >= LZMA_REQUIRED_INPUT_MAX)
              break;

            // переносим всё во временный буфер
            for (int i = 0; i < inSize; i++)
              p.TempBuf![i] = src[srcLen + i];

            srcLen += inSize;
            p.TempBufSize = inSize;

            status = LzmaStatus.NeedsMoreInput;
            return Sz.OK;
          }

          if ((uint)dummyProcessed > LZMA_REQUIRED_INPUT_MAX)
            break;

          if (checkEndMarkNow && !IsDummyEndMarkerPossible(dummyRes))
          {
            for (int i = 0; i < dummyProcessed; i++)
              p.TempBuf![i] = src[srcLen + i];

            srcLen += dummyProcessed;
            p.TempBufSize = dummyProcessed;

            status = LzmaStatus.NotFinished;
            return Sz.ERROR_DATA; // строгий режим
          }

          // декодируем ровно один символ
          bufLimitPos = srcLen;
        }
        else
        {
          // обычный режим: у нас гарантировано есть запас LZMA_REQUIRED_INPUT_MAX байт
          bufLimitPos = srcLen + inSize - LZMA_REQUIRED_INPUT_MAX;
        }

        int buf = srcLen;
        int res = DecodeReal2(ref p, dicLimit, src, ref buf, bufLimitPos);

        int processed = buf - srcLen;

        if (dummyProcessed < 0)
        {
          if (processed > inSize)
            break;
        }
        else if (processed != dummyProcessed)
          break;

        srcLen += processed;
        inSize -= processed;

        if (res != Sz.OK)
        {
          p.RemainLen = _kMatchSpecLen_Error_Data;
          return Sz.ERROR_DATA;
        }

        continue;
      }

      // В tempBuf уже есть часть входа
      {
        int rem = p.TempBufSize;
        int ahead = 0;

        while (rem < LZMA_REQUIRED_INPUT_MAX && ahead < inSize)
        {
          p.TempBuf![rem++] = src[srcLen + ahead];
          ahead++;
        }

        int dummyProcessed = -1;

        if (rem < LZMA_REQUIRED_INPUT_MAX || checkEndMarkNow)
        {
          var dummyRes = TryDummy(p, p.TempBuf!, rem, out dummyProcessed);

          if (dummyRes == LzmaDummy.InputEof)
          {
            if (rem >= LZMA_REQUIRED_INPUT_MAX)
              break;

            p.TempBufSize = rem;
            srcLen += ahead;

            status = LzmaStatus.NeedsMoreInput;
            return Sz.OK;
          }

          if (dummyProcessed < p.TempBufSize)
            break;

          if (checkEndMarkNow && !IsDummyEndMarkerPossible(dummyRes))
          {
            srcLen += dummyProcessed - p.TempBufSize;
            p.TempBufSize = dummyProcessed;

            status = LzmaStatus.NotFinished;
            return Sz.ERROR_DATA; // строгий режим
          }
        }

        // Декодируем один символ из tempBuf
        int buf = 0;
        int res = DecodeReal2(ref p, dicLimit, p.TempBuf!, ref buf, bufLimit: 0);

        int processedTotal = buf;
        int old = p.TempBufSize;

        if (dummyProcessed < 0)
        {
          if (processedTotal > LZMA_REQUIRED_INPUT_MAX)
            break;
          if (processedTotal < old)
            break;
        }
        else if (processedTotal != dummyProcessed)
          break;

        int processedFromSrc = processedTotal - old;

        srcLen += processedFromSrc;
        inSize -= processedFromSrc;

        p.TempBufSize = 0;

        if (res != Sz.OK)
        {
          p.RemainLen = _kMatchSpecLen_Error_Data;
          return Sz.ERROR_DATA;
        }
      }
    }

    // Непредвиденная ошибка (как в C): внутренний сбой/коррупция памяти/и т.п.
    p.RemainLen = _kMatchSpecLen_Error_Fail;
    return Sz.ERROR_FAIL;
  }
}
