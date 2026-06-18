namespace Lzma.Core.SevenZip;

/// <summary>
/// Тип NextHeader внутри 7z-архива.
/// </summary>
public enum SevenZipNextHeaderKind
{
  /// <summary>
  /// Обычный (не закодированный) заголовок.
  /// </summary>
  Header,

  /// <summary>
  /// Закодированный заголовок (EncodedHeader) — перед тем, как читать Header,
  /// нужно его распаковать (обычно LZMA/LZMA2).
  /// </summary>
  EncodedHeader,
}

/// <summary>
/// Результат попытки определить тип NextHeader.
/// </summary>
public enum SevenZipNextHeaderKindDetectResult
{
  Ok,
  NeedMoreInput,
  InvalidData,
}

public static class SevenZipNextHeaderKindDetector
{
  // В формате 7z NextHeader начинается с NID:
  // 0x01 = Header
  // 0x17 = EncodedHeader
  private const byte _nID_Header = SevenZipNid.Header;
  private const byte _nID_EncodedHeader = SevenZipNid.EncodedHeader;

  /// <summary>
  /// Пытается определить, какой тип NextHeader находится в <paramref name="nextHeader"/>.
  /// </summary>
  public static SevenZipNextHeaderKindDetectResult TryDetect(
    ReadOnlySpan<byte> nextHeader,
    out SevenZipNextHeaderKind kind)
  {
    if (nextHeader.Length == 0)
    {
      kind = default;
      return SevenZipNextHeaderKindDetectResult.NeedMoreInput;
    }

    byte id = nextHeader[0];
    if (id == _nID_Header)
    {
      kind = SevenZipNextHeaderKind.Header;
      return SevenZipNextHeaderKindDetectResult.Ok;
    }

    if (id == _nID_EncodedHeader)
    {
      kind = SevenZipNextHeaderKind.EncodedHeader;
      return SevenZipNextHeaderKindDetectResult.Ok;
    }

    kind = default;
    return SevenZipNextHeaderKindDetectResult.InvalidData;
  }
}
