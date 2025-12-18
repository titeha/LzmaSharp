// SPDX-License-Identifier: MIT
// Порт (в работе) Lzma2Dec.c + Lzma2Dec.h из 7-Zip / LZMA SDK.
// Это “обвязка” LZMA2: парсит чанки и дергает LZMA-декодер (LzmaDec.*).

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace LzmaCore.SevenZip;

internal enum Lzma2State : int
{
  Control,
  Unpack0,
  Unpack1,
  Pack0,
  Pack1,
  Prop,
  Data,
  DataCont,
  Finished,
  Error
}

internal struct CLzma2Dec
{
  public Lzma2State State;
  public byte Control;
  public byte NeedInitLevel;
  public bool IsExtraMode;
  public uint PackSize;
  public uint UnpackSize;
  public CLzmaDec Decoder;
}

internal static class Lzma2Dec
{
  private const byte LZMA2_CONTROL_COPY_RESET_DIC = 1;
  private const byte LZMA2_LCLP_MAX = 4;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool IsUncompressedState(in CLzma2Dec p) => (p.Control & 0x80) == 0;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint DicSizeFromProp(byte prop) => ((uint)2 | (uint)(prop & 1)) << (prop / 2 + 11);

  private static int GetOldProps(byte prop, Span<byte> props /* длина >= 5 */)
  {
    if (prop > 40)
      return Sz.ERROR_UNSUPPORTED;

    uint dicSize = prop == 40 ? 0xFFFFFFFFu : DicSizeFromProp(prop);

    props[0] = LZMA2_LCLP_MAX;
    BinaryPrimitives.WriteUInt32LittleEndian(props.Slice(1, 4), dicSize);
    return Sz.OK;
  }

  public static int AllocateProbs(ref CLzma2Dec p, byte prop)
  {
    Span<byte> props = stackalloc byte[LzmaDec.LZMA_PROPS_SIZE];
    int res = GetOldProps(prop, props);
    if (res != Sz.OK)
      return res;
    return LzmaDec.AllocateProbs(ref p.Decoder, props);
  }

  public static int Allocate(ref CLzma2Dec p, byte prop)
  {
    Span<byte> props = stackalloc byte[LzmaDec.LZMA_PROPS_SIZE];
    int res = GetOldProps(prop, props);
    if (res != Sz.OK)
      return res;
    return LzmaDec.Allocate(ref p.Decoder, props);
  }

  public static void Init(ref CLzma2Dec p)
  {
    p.State = Lzma2State.Control;
    p.NeedInitLevel = 0xE0;
    p.IsExtraMode = false;
    p.UnpackSize = 0;

    LzmaDec.Init(ref p.Decoder);
  }

  // Аналог Lzma2Dec_UpdateState() из C.
  private static Lzma2State UpdateState(ref CLzma2Dec p, byte b)
  {
    switch (p.State)
    {
      case Lzma2State.Control:
        p.IsExtraMode = false;
        p.Control = b;

        if (b == 0)
          return Lzma2State.Finished;

        if (IsUncompressedState(p))
        {
          if (b == LZMA2_CONTROL_COPY_RESET_DIC)
            p.NeedInitLevel = 0xC0;
          else if (b > 2 || p.NeedInitLevel == 0xE0)
            return Lzma2State.Error;
        }
        else
        {
          if (b < p.NeedInitLevel)
            return Lzma2State.Error;
          p.NeedInitLevel = 0;
          p.UnpackSize = (uint)(b & 0x1F) << 16;
        }
        return Lzma2State.Unpack0;

      case Lzma2State.Unpack0:
        p.UnpackSize |= (uint)b << 8;
        return Lzma2State.Unpack1;

      case Lzma2State.Unpack1:
        p.UnpackSize |= b;
        p.UnpackSize++;
        return IsUncompressedState(p) ? Lzma2State.Data : Lzma2State.Pack0;

      case Lzma2State.Pack0:
        p.PackSize = (uint)b << 8;
        return Lzma2State.Pack1;

      case Lzma2State.Pack1:
        p.PackSize |= b;
        p.PackSize++;
        return (p.Control & 0x40) != 0 ? Lzma2State.Prop : Lzma2State.Data;

      case Lzma2State.Prop:
      {
        if (b >= (9 * 5 * 5))
          return Lzma2State.Error;

        uint lc = (uint)(b % 9);
        b /= 9;

        p.Decoder.Prop.Pb = (byte)(b / 5);
        uint lp = (uint)(b % 5);

        if (lc + lp > LZMA2_LCLP_MAX)
          return Lzma2State.Error;

        p.Decoder.Prop.Lc = (byte)lc;
        p.Decoder.Prop.Lp = (byte)lp;
        return Lzma2State.Data;
      }

      default:
        return Lzma2State.Error;
    }
  }

  private static void UpdateWithUncompressed(ref CLzmaDec p, ReadOnlySpan<byte> src)
  {
    if (p.Dic is null)
      throw new InvalidOperationException("Словарь декодера (Dic) не задан.");

    src.CopyTo(p.Dic.AsSpan(p.DicPos));
    int size = src.Length;

    p.DicPos += size;

    // Поведение как в C (unsigned wrap).
    if (p.CheckDicSize == 0 && unchecked(p.Prop.DicSize - p.ProcessedPos) <= (uint)size)
      p.CheckDicSize = p.Prop.DicSize;

    p.ProcessedPos += (uint)size;
  }

  public static int DecodeToDic(
      ref CLzma2Dec p,
      int dicLimit,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      LzmaFinishMode finishMode,
      out LzmaStatus status)
  {
    int inSize = srcLen;
    srcLen = 0;
    status = LzmaStatus.NotSpecified;

    int srcPos = 0;

    while (p.State != Lzma2State.Error)
    {
      if (p.State == Lzma2State.Finished)
      {
        status = LzmaStatus.FinishedWithMark;
        return Sz.OK;
      }

      int dicPos = p.Decoder.DicPos;

      if (dicPos == dicLimit && finishMode == LzmaFinishMode.Any)
      {
        status = LzmaStatus.NotFinished;
        return Sz.OK;
      }

      if (p.State != Lzma2State.Data && p.State != Lzma2State.DataCont)
      {
        if (srcLen == inSize)
        {
          status = LzmaStatus.NeedsMoreInput;
          return Sz.OK;
        }

        srcLen++;
        p.State = UpdateState(ref p, src[srcPos++]);

        if (dicPos == dicLimit && p.State != Lzma2State.Finished)
          break;

        continue;
      }

      int inCur = inSize - srcLen;
      int outCur = dicLimit - dicPos;
      LzmaFinishMode curFinishMode = LzmaFinishMode.Any;

      if ((uint)outCur >= p.UnpackSize)
      {
        outCur = (int)p.UnpackSize;
        curFinishMode = LzmaFinishMode.End;
      }

      if (IsUncompressedState(p))
      {
        if (inCur == 0)
        {
          status = LzmaStatus.NeedsMoreInput;
          return Sz.OK;
        }

        if (p.State == Lzma2State.Data)
        {
          bool initDic = p.Control == LZMA2_CONTROL_COPY_RESET_DIC;
          LzmaDec.InitDicAndState(ref p.Decoder, initDic, initState: false);
        }

        if (inCur > outCur)
          inCur = outCur;
        if (inCur == 0)
          break;

        UpdateWithUncompressed(ref p.Decoder, src.Slice(srcPos, inCur));

        srcPos += inCur;
        srcLen += inCur;

        p.UnpackSize -= (uint)inCur;
        p.State = p.UnpackSize == 0 ? Lzma2State.Control : Lzma2State.DataCont;
      }
      else
      {
        if (p.State == Lzma2State.Data)
        {
          bool initDic = p.Control >= 0xE0;
          bool initState = p.Control >= 0xA0;
          LzmaDec.InitDicAndState(ref p.Decoder, initDic, initState);
          p.State = Lzma2State.DataCont;
        }

        if ((uint)inCur > p.PackSize)
          inCur = (int)p.PackSize;

        int inCur2 = inCur;

        int res = LzmaDec.DecodeToDic(
            ref p.Decoder,
            dicPos + outCur,
            src.Slice(srcPos),
            ref inCur2,
            curFinishMode,
            ref status);

        inCur = inCur2;

        srcPos += inCur;
        srcLen += inCur;

        p.PackSize -= (uint)inCur;

        outCur = p.Decoder.DicPos - dicPos;
        p.UnpackSize -= (uint)outCur;

        if (res != Sz.OK)
          break;

        if (status == LzmaStatus.NeedsMoreInput)
        {
          if (p.PackSize == 0)
            break;

          return Sz.OK;
        }

        if (inCur == 0 && outCur == 0)
        {
          if (status != LzmaStatus.MaybeFinishedWithoutMark
              || p.UnpackSize != 0
              || p.PackSize != 0)
            break;

          p.State = Lzma2State.Control;
        }

        status = LzmaStatus.NotSpecified;
      }
    }

    status = LzmaStatus.NotSpecified;
    p.State = Lzma2State.Error;
    return Sz.ERROR_DATA;
  }

  public static Lzma2ParseStatus Parse(
      ref CLzma2Dec p,
      int outSize,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      bool checkFinishBlock)
  {
    int inSize = srcLen;
    srcLen = 0;

    int srcPos = 0;

    while (p.State != Lzma2State.Error)
    {
      if (p.State == Lzma2State.Finished)
        return (Lzma2ParseStatus)LzmaStatus.FinishedWithMark;

      if (outSize == 0 && !checkFinishBlock)
        return (Lzma2ParseStatus)LzmaStatus.NotFinished;

      if (p.State != Lzma2State.Data && p.State != Lzma2State.DataCont)
      {
        if (srcLen == inSize)
          return (Lzma2ParseStatus)LzmaStatus.NeedsMoreInput;

        srcLen++;
        p.State = UpdateState(ref p, src[srcPos++]);

        if (p.State == Lzma2State.Unpack0)
        {
          if (p.Control == LZMA2_CONTROL_COPY_RESET_DIC || p.Control >= 0xE0)
            return Lzma2ParseStatus.NewBlock;
        }

        // Этот блок можно было бы закомментировать.
        // Не критично, если мы прочитаем лишний байт входа — позже остановимся в DATA/DATA_CONT.
        if (outSize == 0 && p.State != Lzma2State.Finished)
        {
          return (Lzma2ParseStatus)LzmaStatus.NotFinished;
        }

        if (p.State == Lzma2State.Data)
          return Lzma2ParseStatus.NewChunk;

        continue;
      }

      if (outSize == 0)
        return (Lzma2ParseStatus)LzmaStatus.NotFinished;

      int inCur = inSize - srcLen;

      if (IsUncompressedState(p))
      {
        if (inCur == 0)
          return (Lzma2ParseStatus)LzmaStatus.NeedsMoreInput;

        if ((uint)inCur > p.UnpackSize)
          inCur = (int)p.UnpackSize;
        if (inCur > outSize)
          inCur = outSize;

        p.Decoder.DicPos += inCur;

        srcPos += inCur;
        srcLen += inCur;

        outSize -= inCur;
        p.UnpackSize -= (uint)inCur;

        p.State = p.UnpackSize == 0 ? Lzma2State.Control : Lzma2State.DataCont;
      }
      else
      {
        p.IsExtraMode = true;

        if (inCur == 0)
        {
          if (p.PackSize != 0)
            return (Lzma2ParseStatus)LzmaStatus.NeedsMoreInput;
        }
        else if (p.State == Lzma2State.Data)
        {
          p.State = Lzma2State.DataCont;

          if (src[srcPos] != 0)
          {
            // Первый байт LZMA-чанка обязан быть 0
            srcLen += 1;
            p.PackSize--;
            break;
          }
        }

        if ((uint)inCur > p.PackSize)
          inCur = (int)p.PackSize;

        srcPos += inCur;
        srcLen += inCur;
        p.PackSize -= (uint)inCur;

        if (p.PackSize == 0)
        {
          int rem = outSize;
          if ((uint)rem > p.UnpackSize)
            rem = (int)p.UnpackSize;

          p.Decoder.DicPos += rem;
          p.UnpackSize -= (uint)rem;
          outSize -= rem;

          if (p.UnpackSize == 0)
            p.State = Lzma2State.Control;
        }
      }
    }

    p.State = Lzma2State.Error;
    return (Lzma2ParseStatus)LzmaStatus.NotSpecified;
  }

  public static uint GetUnpackExtra(in CLzma2Dec p) => p.IsExtraMode ? p.UnpackSize : 0;

  public static int DecodeToBuf(
      ref CLzma2Dec p,
      Span<byte> dest,
      ref int destLen,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      LzmaFinishMode finishMode,
      out LzmaStatus status)
  {
    int outSize = destLen;
    int inSize = srcLen;
    srcLen = 0;
    destLen = 0;

    int destPos = 0;
    int srcPos = 0;

    status = LzmaStatus.NotSpecified;

    for (; ; )
    {
      int inCur = inSize;
      if (p.Decoder.DicPos == p.Decoder.DicBufSize)
        p.Decoder.DicPos = 0;

      int dicPos = p.Decoder.DicPos;
      LzmaFinishMode curFinishMode = LzmaFinishMode.Any;
      int outCur = p.Decoder.DicBufSize - dicPos;

      if (outCur >= outSize)
      {
        outCur = outSize;
        curFinishMode = finishMode;
      }

      int res = DecodeToDic(ref p, dicPos + outCur, src.Slice(srcPos), ref inCur, curFinishMode, out status);

      srcPos += inCur;
      inSize -= inCur;
      srcLen += inCur;

      int produced = p.Decoder.DicPos - dicPos;

      if (p.Decoder.Dic is null)
        throw new InvalidOperationException("Словарь декодера (Dic) не задан.");

      p.Decoder.Dic.AsSpan(dicPos, produced).CopyTo(dest.Slice(destPos));
      destPos += produced;

      outSize -= produced;
      destLen += produced;

      if (res != Sz.OK)
        return res;

      if (produced == 0 || outSize == 0)
        return Sz.OK;
    }
  }

  public static int DecodeOneCall(
      byte[] dest,
      int destOffset,
      ref int destLen,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      byte prop,
      LzmaFinishMode finishMode,
      out LzmaStatus status)
  {
    // Аналог Lzma2Decode() (одноразовый интерфейс из C).
    CLzma2Dec p = default;

    int outSize = destLen;
    int inSize = srcLen;
    destLen = 0;
    srcLen = 0;

    status = LzmaStatus.NotSpecified;

    int res = AllocateProbs(ref p, prop);
    if (res != Sz.OK)
      return res;

    p.Decoder.Dic = dest;
    p.Decoder.DicPos = destOffset;
    p.Decoder.DicBufSize = destOffset + outSize;

    Init(ref p);

    srcLen = inSize;

    res = DecodeToDic(ref p, destOffset + outSize, src, ref srcLen, finishMode, out status);

    destLen = p.Decoder.DicPos - destOffset;

    if (res == Sz.OK && status == LzmaStatus.NeedsMoreInput)
      res = Sz.ERROR_INPUT_EOF;

    return res;
  }
}
