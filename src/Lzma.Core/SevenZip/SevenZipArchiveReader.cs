namespace Lzma.Core.SevenZip;

public enum SevenZipArchiveReadResult
{
  Ok,
  NeedMoreInput,
  InvalidData,
  NotSupported,
}

/// <summary>
/// <para>Инкрементальный reader для 7z-архивов: читает SignatureHeader, NextHeader и парсит Header.</para>
/// <para>На этом шаге добавлена поддержка EncodedHeader (когда Header лежит в packed streams и должен быть распакован).</para>
/// </summary>
public sealed class SevenZipArchiveReader
{
  private readonly SevenZipNextHeaderReader _nextHeaderReader = new();

  private bool _isTerminal;
  private SevenZipArchiveReadResult _terminalResult;

  public SevenZipSignatureHeader? SignatureHeader { get; private set; }

  public SevenZipNextHeaderKind? NextHeaderKind { get; private set; }

  public SevenZipHeader? Header { get; private set; }

  /// <summary>
  /// Байты packed streams (данные между SignatureHeader и NextHeader).
  /// Для обычного Header не нужны, а для EncodedHeader содержат упакованный Header.
  /// </summary>
  public ReadOnlyMemory<byte> PackedStreams { get; private set; }

  /// <summary>
  /// Сырые байты NextHeader (то, что лежит в конце файла по смещению/размеру из SignatureHeader).
  /// </summary>
  public ReadOnlyMemory<byte> NextHeaderBytes { get; private set; }

  /// <summary>
  /// Если NextHeaderKind == EncodedHeader — здесь будут распакованные байты обычного Header.
  /// </summary>
  public ReadOnlyMemory<byte> DecodedHeaderBytes { get; private set; }

  public SevenZipArchiveReadResult Read(ReadOnlySpan<byte> input, out int bytesConsumed)
  {
    // После терминального результата больше ничего не читаем.
    if (_isTerminal)
    {
      bytesConsumed = 0;
      return _terminalResult;
    }

    var res = _nextHeaderReader.Read(input, out bytesConsumed);
    if (res == SevenZipNextHeaderReadResult.NeedMoreInput)
      return SevenZipArchiveReadResult.NeedMoreInput;

    if (res == SevenZipNextHeaderReadResult.InvalidData)
    {
      MakeTerminal(SevenZipArchiveReadResult.InvalidData);
      return _terminalResult;
    }

    // res == Ok: у нас есть SignatureHeader + packed streams + NextHeader bytes.
    SignatureHeader ??= _nextHeaderReader.SignatureHeader;
    PackedStreams = _nextHeaderReader.PackedStreams;
    NextHeaderBytes = _nextHeaderReader.NextHeader;

    // Определяем тип NextHeader.
    var kindDetectRes = SevenZipNextHeaderKindDetector.TryDetect(NextHeaderBytes.Span, out var kind);
    if (kindDetectRes == SevenZipNextHeaderKindDetectResult.NeedMoreInput)
      return SevenZipArchiveReadResult.NeedMoreInput;

    if (kindDetectRes == SevenZipNextHeaderKindDetectResult.InvalidData)
    {
      MakeTerminal(SevenZipArchiveReadResult.InvalidData);
      return _terminalResult;
    }

    NextHeaderKind = kind;

    if (kind == SevenZipNextHeaderKind.Header)
    {
      switch (SevenZipHeaderReader.TryRead(NextHeaderBytes.Span, out SevenZipHeader header, out _))
      {
        case SevenZipHeaderReadResult.Ok:
          Header = header;
          MakeTerminal(SevenZipArchiveReadResult.Ok);
          return _terminalResult;
        case SevenZipHeaderReadResult.NeedMoreInput:
          return SevenZipArchiveReadResult.NeedMoreInput;
        case SevenZipHeaderReadResult.NotSupported:
          MakeTerminal(SevenZipArchiveReadResult.NotSupported);
          return _terminalResult;
        default:
          MakeTerminal(SevenZipArchiveReadResult.InvalidData);
          return _terminalResult;
      }
    }

    // EncodedHeader
    var decodeRes = SevenZipEncodedHeaderDecoder.TryDecode(
      NextHeaderBytes.Span,
      PackedStreams.Span,
      out byte[] decodedHeaderBytes,
      out SevenZipHeader decodedHeader);

    if (decodeRes != SevenZipArchiveReadResult.Ok)
    {
      // NeedMoreInput здесь трактуем как повреждение, т.к. NextHeaderReader уже сообщил Ok.
      MakeTerminal(decodeRes == SevenZipArchiveReadResult.NeedMoreInput
        ? SevenZipArchiveReadResult.InvalidData
        : decodeRes);
      return _terminalResult;
    }

    DecodedHeaderBytes = decodedHeaderBytes;
    Header = decodedHeader;

    MakeTerminal(SevenZipArchiveReadResult.Ok);
    return _terminalResult;
  }

  private void MakeTerminal(SevenZipArchiveReadResult result)
  {
    _isTerminal = true;
    _terminalResult = result;
  }
}
