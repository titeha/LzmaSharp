namespace Lzma.Core.SevenZip;

/// <summary>
/// Связка (inIndex -> outIndex) в Folder.
/// </summary>
public readonly record struct SevenZipBindPair(ulong InIndex, ulong OutIndex);
