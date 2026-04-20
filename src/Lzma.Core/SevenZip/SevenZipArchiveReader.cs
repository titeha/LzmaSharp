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

  public SevenZipArchiveReadResult Read(ReadOnlySpan<byte> input, out int bytesConsumed) => Read(input: input, options: SevenZipDecodeOptions.Default, bytesConsumed: out bytesConsumed);

  public SevenZipArchiveReadResult Read(ReadOnlySpan<byte> input, SevenZipDecodeOptions options, out int bytesConsumed)
  {
    ArgumentNullException.ThrowIfNull(options);

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

    if (res == SevenZipNextHeaderReadResult.NotSupported)
    {
      if (_nextHeaderReader.HasSignatureHeader)
        SignatureHeader ??= _nextHeaderReader.SignatureHeader;

      PackedStreams = _nextHeaderReader.PackedStreams;
      NextHeaderBytes = _nextHeaderReader.NextHeader;

      MakeTerminal(SevenZipArchiveReadResult.NotSupported);
      return _terminalResult;
    }

    // res == Ok: у нас есть SignatureHeader + packed streams + NextHeader bytes.
    SignatureHeader ??= _nextHeaderReader.SignatureHeader;
    PackedStreams = _nextHeaderReader.PackedStreams;
    NextHeaderBytes = _nextHeaderReader.NextHeader;

    // Определяем тип NextHeader.
    var kindDetectRes = SevenZipNextHeaderKindDetector.TryDetect(NextHeaderBytes.Span, out var kind);
    if (kindDetectRes == SevenZipNextHeaderKindDetectResult.NeedMoreInput)
    {
      // NextHeaderReader уже вернул Ok и отдал все байты NextHeader.
      // Если даже для определения вида заголовка данных не хватает, архив повреждён.
      MakeTerminal(SevenZipArchiveReadResult.InvalidData);
      return _terminalResult;
    }

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
          // NextHeaderReader уже полностью прочитал NextHeader по размеру из signature header.
          // Если парсер Header просит ещё байты, значит заголовок усечён/повреждён.
          MakeTerminal(SevenZipArchiveReadResult.InvalidData);
          return _terminalResult;
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
      nextHeaderBytes: NextHeaderBytes.Span,
      packedStreams: PackedStreams.Span,
      options: options,
      decodedHeaderBytes: out byte[] decodedHeaderBytes,
      decodedHeader: out SevenZipHeader decodedHeader);

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
