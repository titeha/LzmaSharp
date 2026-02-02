namespace Lzma.Core.SevenZip;

/// <summary>
/// Описание "coder" внутри 7z-папки (Folder).
/// Это один алгоритм/фильтр в цепочке распаковки.
/// </summary>
public sealed class SevenZipCoderInfo(byte[] methodId, byte[] properties, ulong numInStreams, ulong numOutStreams)
{
  /// <summary>
  /// Идентификатор метода (MethodId) в виде массива байт.
  /// Например, для LZMA это обычно 0x03 0x01 0x01 (в 7z).
  /// </summary>
  public byte[] MethodId { get; } = methodId ?? [];

  /// <summary>
  /// Свойства/параметры метода (properties). Может быть пустым массивом.
  /// </summary>
  public byte[] Properties { get; } = properties ?? [];

  /// <summary>
  /// Количество входных потоков у coder'а.
  /// </summary>
  public ulong NumInStreams { get; } = numInStreams;

  /// <summary>
  /// Количество выходных потоков у coder'а.
  /// </summary>
  public ulong NumOutStreams { get; } = numOutStreams;
}
