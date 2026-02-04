namespace Lzma.Core.SevenZip;

public enum SevenZipArchiveReadResult
{
  Ok = 0,
  NeedMoreInput = 1,
  InvalidData = 2,
  NotSupported = 3,
}

/// <summary>
/// Инкрементальный «сборщик» структуры архива 7z: читает сигнатуру,
/// NextHeader и затем Header.
/// </summary>
public sealed class SevenZipArchiveReader
{
  private readonly SevenZipNextHeaderReader _nextHeaderReader = new();

  // После успешного чтения (или фатальной ошибки) считаем ридер «терминальным».
  private bool _isTerminal;
  private SevenZipArchiveReadResult _terminalResult;

  public SevenZipSignatureHeader SignatureHeader => _nextHeaderReader.SignatureHeader;

  public SevenZipHeader Header { get; private set; }

  /// <summary>
  /// Последний результат, который вернул <see cref="Read"/>.
  /// </summary>
  public SevenZipArchiveReadResult Result { get; private set; }

  /// <summary>
  /// Тип NextHeader (Header/EncodedHeader/Unknown).
  /// Заполняется после того, как NextHeader полностью считан.
  /// </summary>
  public SevenZipNextHeaderKind NextHeaderKind { get; private set; }

  /// <summary>
  /// Байты между сигнатурным заголовком и NextHeader (упакованные данные архива).
  /// На текущем этапе полностью буферизуются внутри SevenZipNextHeaderReader.
  /// </summary>
  public ReadOnlyMemory<byte> PackedStreams => _nextHeaderReader.PackedStreams;

  public SevenZipArchiveReadResult Read(ReadOnlySpan<byte> input, out int bytesConsumed)
  {
    bytesConsumed = 0;

    if (_isTerminal)
    {
      Result = _terminalResult;
      return _terminalResult;
    }

    var nextHeaderRes = _nextHeaderReader.Read(input, out bytesConsumed);

    switch (nextHeaderRes)
    {
      case SevenZipNextHeaderReadResult.NeedMoreInput:
        Result = SevenZipArchiveReadResult.NeedMoreInput;
        return Result;

      case SevenZipNextHeaderReadResult.InvalidData:
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.InvalidData;
        Result = _terminalResult;
        return _terminalResult;

      case SevenZipNextHeaderReadResult.NotSupported:
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.NotSupported;
        Result = _terminalResult;
        return _terminalResult;

      case SevenZipNextHeaderReadResult.Ok:
        break;

      default:
        throw new InvalidOperationException($"Неизвестный результат чтения next header: {nextHeaderRes}.");
    }

    // Определяем тип NextHeader.
    var detectRes = SevenZipNextHeaderKindDetector.TryDetect(_nextHeaderReader.NextHeader.Span, out var kind);
    switch (detectRes)
    {
      case SevenZipNextHeaderKindDetectResult.Ok:
        NextHeaderKind = kind;
        break;

      case SevenZipNextHeaderKindDetectResult.NeedMoreInput:
      case SevenZipNextHeaderKindDetectResult.InvalidData:
        // Неконсистентно: next header уже считан полностью.
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.InvalidData;
        Result = _terminalResult;
        return _terminalResult;

      default:
        throw new InvalidOperationException($"Неизвестный результат определения next header kind: {detectRes}.");
    }

    // NextHeader уже полностью в памяти, поэтому bytesConsumed нам не важен.
    var headerRes = SevenZipHeaderReader.TryRead(
        _nextHeaderReader.NextHeader.Span,
        out var header,
        out _);

    switch (headerRes)
    {
      case SevenZipHeaderReadResult.Ok:
        Header = header;
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.Ok;
        Result = _terminalResult;
        return _terminalResult;

      case SevenZipHeaderReadResult.NeedMoreInput:
        // Неконсистентно: на этом этапе у нас уже есть весь next header в памяти.
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.InvalidData;
        Result = _terminalResult;
        return _terminalResult;

      case SevenZipHeaderReadResult.InvalidData:
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.InvalidData;
        Result = _terminalResult;
        return _terminalResult;

      case SevenZipHeaderReadResult.NotSupported:
        _isTerminal = true;
        _terminalResult = SevenZipArchiveReadResult.NotSupported;
        Result = _terminalResult;
        return _terminalResult;

      default:
        throw new InvalidOperationException($"Неизвестный результат чтения Header: {headerRes}.");
    }
  }

  public void Reset()
  {
    _nextHeaderReader.Reset();
    Header = default;
    NextHeaderKind = default;
    _isTerminal = false;
    _terminalResult = default;
    Result = default;
  }
}
