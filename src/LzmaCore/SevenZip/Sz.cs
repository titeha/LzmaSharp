// SPDX-License-Identifier: MIT
// Внутренние типы/коды ошибок в стиле 7-Zip (для совместимости при переносе).

namespace LzmaCore.SevenZip;

internal static class Sz
{
  public const int OK = 0;
  public const int ERROR_DATA = 1;
  public const int ERROR_MEM = 2;
  public const int ERROR_UNSUPPORTED = 4;
  public const int ERROR_INPUT_EOF = 6;
  public const int ERROR_FAIL = 11;
}

internal enum LzmaFinishMode : byte
{
  Any = 0, // можно остановиться в любой точке
  End = 1  // блок обязан закончиться ровно на границе выхода
}

/// <summary>
/// Соответствует ELzmaStatus из 7-Zip/LZMA SDK.
/// </summary>
internal enum LzmaStatus : int
{
  NotSpecified = 0,
  FinishedWithMark = 1,
  NotFinished = 2,
  NeedsMoreInput = 3,
  MaybeFinishedWithoutMark = 4
}

/// <summary>
/// Соответствует ELzma2ParseStatus из 7-Zip. Функция также может возвращать значения из <see cref="LzmaStatus"/>.
/// </summary>
internal enum Lzma2ParseStatus : int
{
  NewBlock = (int)LzmaStatus.MaybeFinishedWithoutMark + 1,
  NewChunk = NewBlock + 1
}
