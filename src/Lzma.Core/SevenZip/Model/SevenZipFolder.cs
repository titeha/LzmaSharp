namespace Lzma.Core.SevenZip;

/// <summary>
/// "Папка" (Folder) в терминах формата 7z.
/// Folder описывает цепочку coder'ов и то, как их потоки соединены (BindPairs).
/// </summary>
/// <remarks>
/// Параметры названы с заглавной буквы специально:
/// мы вызываем этот конструктор с именованными аргументами (Coders: ..., BindPairs: ...).
/// </remarks>
public sealed class SevenZipFolder(
  SevenZipCoderInfo[] Coders,
  SevenZipBindPair[] BindPairs,
  ulong[] PackedStreamIndices,
  ulong NumInStreams,
  ulong NumOutStreams)
{
  /// <summary>
  /// Набор coder'ов (алгоритмов/фильтров) для данного folder'а.
  /// </summary>
  public SevenZipCoderInfo[] Coders { get; } = Coders ?? [];

  /// <summary>
  /// Описывает, какие выходные потоки coder'ов подключены к каким входным.
  /// </summary>
  public SevenZipBindPair[] BindPairs { get; } = BindPairs ?? [];

  /// <summary>
  /// Индексы "упакованных" потоков (packed streams), которые НЕ являются выходами каких-то coder'ов.
  /// (то есть те потоки, которые реально читаются из PackInfo).
  /// </summary>
  public ulong[] PackedStreamIndices { get; } = PackedStreamIndices ?? [];

  /// <summary>
  /// Общее количество входных потоков во всех coder'ах.
  /// </summary>
  public ulong NumInStreams { get; } = NumInStreams;

  /// <summary>
  /// Общее количество выходных потоков во всех coder'ах.
  /// </summary>
  public ulong NumOutStreams { get; } = NumOutStreams;
}
