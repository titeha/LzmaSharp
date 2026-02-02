namespace Lzma.Core.SevenZip;

/// <summary>
/// UnpackInfo из Next Header (7z).
/// Здесь хранятся "Folders" (цепочки coder'ов) и размеры выходных потоков.
/// </summary>
public sealed class SevenZipUnpackInfo(SevenZipFolder[] folders, ulong[][] folderUnpackSizes)
{
  /// <summary>
  /// Массив папок (Folders) — каждая описывает цепочку coder'ов.
  /// </summary>
  public SevenZipFolder[] Folders { get; } = folders ?? [];

  /// <summary>
  /// Размеры распакованных потоков для каждой папки.
  /// [folderIndex][outStreamIndex].
  /// </summary>
  public ulong[][] FolderUnpackSizes { get; } = folderUnpackSizes ?? [];
}
