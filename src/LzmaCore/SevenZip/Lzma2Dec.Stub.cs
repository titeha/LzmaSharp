// SPDX-License-Identifier: MIT
// ВНИМАНИЕ: это ВРЕМЕННАЯ заглушка, чтобы LZMA2-обвязка компилировалась.
// Следующий шаг — порт LzmaDec.c/LzmaDec.h и замена этих методов.

using System;

namespace LzmaCore.SevenZip;

internal struct LzmaProps
{
  public uint DicSize;
  public byte Lc;
  public byte Lp;
  public byte Pb;
}

internal struct CLzmaDec
{
  public byte[]? Dic;
  public int DicPos;
  public int DicBufSize;

  public LzmaProps Prop;

  // Нужно для Lzma2Dec_UpdateWithUncompressed
  public uint ProcessedPos;
  public uint CheckDicSize;
}

internal static class LzmaDec
{
  public const int LZMA_PROPS_SIZE = 5;

  public static int AllocateProbs(ref CLzmaDec p, ReadOnlySpan<byte> props)
  {
    // Минимальный разбор: сохраняем только dicSize; lc/lp/pb будут выставлены из заголовка LZMA2-чанка.
    if (props.Length < LZMA_PROPS_SIZE)
      return Sz.ERROR_UNSUPPORTED;
    p.Prop.DicSize = (uint)(props[1]
        | (props[2] << 8)
        | (props[3] << 16)
        | (props[4] << 24));
    return Sz.OK;
  }

  public static int Allocate(ref CLzmaDec p, ReadOnlySpan<byte> props)
      => AllocateProbs(ref p, props);

  public static void Init(ref CLzmaDec p)
  {
    // Настоящий LZMA-декодер будет сбрасывать больше состояния.
    p.ProcessedPos = 0;
    p.CheckDicSize = 0;
  }

  public static void InitDicAndState(ref CLzmaDec p, bool initDic, bool initState)
  {
    // Заглушка: при initDic сбрасываем позицию словаря.
    if (initDic)
      p.DicPos = 0;

    _ = initState; // пока не используется
  }

  public static int DecodeToDic(
      ref CLzmaDec p,
      int dicLimit,
      ReadOnlySpan<byte> src,
      ref int srcLen,
      LzmaFinishMode finishMode,
      ref LzmaStatus status)
  {
    // Заглушка: сжатые (LZMA) чанки будут поддержаны после порта LzmaDec.c.
    status = LzmaStatus.NotSpecified;
    _ = p;
    _ = dicLimit;
    _ = src;
    _ = srcLen;
    _ = finishMode;
    return Sz.ERROR_UNSUPPORTED;
  }
}
