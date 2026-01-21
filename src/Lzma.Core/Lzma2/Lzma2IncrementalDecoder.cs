using System;

namespace Lzma.Core.Lzma2;

// Incremental LZMA2 decoder.
// At this step we support:
// - End marker (0x00)
// - Copy chunks (0x01/0x02)
// - LZMA chunks WITH properties byte (control >= 0xE0)
//   NOTE: continuation LZMA chunks without properties are still NotSupported.
public sealed class Lzma2IncrementalDecoder
{
  // Header buffer large enough for LZMA2 header with properties byte (max 6).
  private readonly byte[] _headerBuffer = new byte[6];
  private int _headerFilled;
  private int _headerExpected = -1;

  private uint _copyRemaining;

  // LZMA chunk state
  private const int DefaultLzmaDictionarySize = 1 << 23;
  private Lzma1.LzmaDecoder? _lzma;
  private uint _lzmaPackRemaining;
  private uint _lzmaUnpackRemaining;

  private Lzma1.LzmaProperties _lastLzmaProps;
  private bool _hasLastLzmaProps;

  private DecoderState _state = DecoderState.ReadingHeader;
  private bool _isTerminal;
  private Lzma2DecodeResult _terminalResult;

  private long _totalRead;
  private long _totalWritten;

  private readonly IProgress<LzmaProgress>? _progress;
  private readonly int _dictionarySize;
  private LzmaProgress _lastProgress;

  public Lzma2IncrementalDecoder(IProgress<LzmaProgress>? progress = null, int dictionarySize = DefaultLzmaDictionarySize)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    _progress = progress;
    _dictionarySize = dictionarySize;
  }

  public long TotalBytesRead => _totalRead;

  public long TotalBytesWritten => _totalWritten;

  public void Reset()
  {
    _headerFilled = 0;
    _headerExpected = -1;

    _copyRemaining = 0;

    _lzma = null;
    _lzmaPackRemaining = 0;
    _lzmaUnpackRemaining = 0;

    _lastLzmaProps = default;
    _hasLastLzmaProps = false;

    _state = DecoderState.ReadingHeader;
    _isTerminal = false;
    _terminalResult = default;

    _totalRead = 0;
    _totalWritten = 0;
    _lastProgress = default;
  }

  public Lzma2DecodeResult Decode(ReadOnlySpan<byte> input, Span<byte> output, out int bytesConsumed, out int bytesWritten)
  {
    bytesConsumed = 0;
    bytesWritten = 0;

    if (_isTerminal)
    {
      return _terminalResult;
    }

    while (true)
    {
      switch (_state)
      {
        case DecoderState.ReadingHeader:
          // Need at least 1 byte to know the expected header size.
          if (input.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          if (_headerExpected < 0)
          {
            _headerExpected = GetExpectedHeaderSize(input[0]);
            if (_headerExpected is < 1 or > 6)
              return SetError(Lzma2DecodeResult.InvalidData);
          }

          int need = _headerExpected - _headerFilled;
          int canTake = Math.Min(need, input.Length);

          input[..canTake].CopyTo(_headerBuffer.AsSpan(_headerFilled));
          _headerFilled += canTake;

          input = input[canTake..];
          bytesConsumed += canTake;
          _totalRead += canTake;

          if (_headerFilled < _headerExpected)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          var headerRead = Lzma2ChunkHeader.TryRead(
              _headerBuffer.AsSpan(0, _headerExpected),
              out var header,
              out var headerSize);

          if (headerRead == Lzma2ReadHeaderResult.NeedMoreInput)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          if (headerRead == Lzma2ReadHeaderResult.InvalidData || headerSize != _headerExpected)
            return SetError(Lzma2DecodeResult.InvalidData);

          _headerFilled = 0;
          _headerExpected = -1;

          if (header.Kind == Lzma2ChunkKind.End)
          {
            _state = DecoderState.Finished;
            _isTerminal = true;
            _terminalResult = Lzma2DecodeResult.Finished;
            return ReturnWithProgress(Lzma2DecodeResult.Finished);
          }

          if (header.Kind == Lzma2ChunkKind.Copy)
          {
            _copyRemaining = (uint)header.UnpackSize;
            _state = DecoderState.CopyingPayload;
            continue;
          }

          if (header.Kind == Lzma2ChunkKind.Lzma)
          {
            // Step24: support LZMA chunks without properties *when* they reset LZMA state (control 0xA0..0xBF).
            // True continuation chunks (control 0x80..0x9F) would require preserving the range decoder state
            // across chunk boundaries, and are still NotSupported for now.
            Lzma1.LzmaProperties props;

            if (header.HasProperties)
            {
              if (!header.Properties.HasValue || !Lzma1.LzmaProperties.TryParse(header.Properties.Value, out props))
                return SetError(Lzma2DecodeResult.InvalidData);

              _lastLzmaProps = props;
              _hasLastLzmaProps = true;
            }
            else
            {
              if (!_hasLastLzmaProps) // A no-properties LZMA chunk before we've ever seen properties is invalid.
                return SetError(Lzma2DecodeResult.InvalidData);

              if (!header.ResetState)
                return SetError(Lzma2DecodeResult.NotSupported);

              props = _lastLzmaProps;
            }

            try
            {
              _lzma = new Lzma1.LzmaDecoder(props, _dictionarySize);
            }
            catch (ArgumentOutOfRangeException)
            {
              return SetError(Lzma2DecodeResult.InvalidData);
            }

            _lzmaPackRemaining = (uint)header.PackSize;
            _lzmaUnpackRemaining = (uint)header.UnpackSize;

            _state = DecoderState.DecodingLzmaPayload;
            continue;
          }

          return SetError(Lzma2DecodeResult.NotSupported);
        case DecoderState.CopyingPayload:
          if (_copyRemaining == 0)
          {
            _state = DecoderState.ReadingHeader;
            continue;
          }

          if (output.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreOutput);

          if (input.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          int copyNow = (int)Math.Min(_copyRemaining, (uint)Math.Min(input.Length, output.Length));

          input[..copyNow].CopyTo(output);
          input = input[copyNow..];
          output = output[copyNow..];

          bytesConsumed += copyNow;
          bytesWritten += copyNow;

          _totalRead += copyNow;
          _totalWritten += copyNow;

          _copyRemaining -= (uint)copyNow;
          continue;
        case DecoderState.DecodingLzmaPayload:
          // If we're done producing output for this chunk, skip remaining compressed bytes.
          if (_lzmaUnpackRemaining == 0)
          {
            if (_lzmaPackRemaining > 0)
            {
              if (input.IsEmpty)
                return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

              int skip = (int)Math.Min(_lzmaPackRemaining, (uint)input.Length);
              input = input[skip..];

              bytesConsumed += skip;
              _totalRead += skip;

              _lzmaPackRemaining -= (uint)skip;
              continue;
            }

            _state = DecoderState.ReadingHeader;
            continue;
          }

          if (output.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreOutput);

          if (_lzmaPackRemaining == 0)
            return SetError(Lzma2DecodeResult.InvalidData);

          if (input.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          int inLimit = (int)Math.Min(_lzmaPackRemaining, (uint)input.Length);
          int outLimit = (int)Math.Min(_lzmaUnpackRemaining, (uint)output.Length);

          var lzRes = _lzma!.Decode(
              input[..inLimit],
              out int lzConsumed,
              output[..outLimit],
              out int lzWritten,
              out _);

          if (lzConsumed == 0 && lzWritten == 0)
            return SetError(Lzma2DecodeResult.InvalidData);

          input = input[lzConsumed..];
          output = output[lzWritten..];

          bytesConsumed += lzConsumed;
          bytesWritten += lzWritten;

          _totalRead += lzConsumed;
          _totalWritten += lzWritten;

          if ((uint)lzConsumed > _lzmaPackRemaining || (uint)lzWritten > _lzmaUnpackRemaining)
            return SetError(Lzma2DecodeResult.InvalidData);

          _lzmaPackRemaining -= (uint)lzConsumed;
          _lzmaUnpackRemaining -= (uint)lzWritten;

          if (lzRes == Lzma1.LzmaDecodeResult.InvalidData)
            return SetError(Lzma2DecodeResult.InvalidData);

          if (lzRes == Lzma1.LzmaDecodeResult.NotImplemented)
            return SetError(Lzma2DecodeResult.NotSupported);

          if (lzRes == Lzma1.LzmaDecodeResult.NeedsMoreInput && _lzmaPackRemaining == 0 && _lzmaUnpackRemaining > 0)
            return SetError(Lzma2DecodeResult.InvalidData);

          continue;
        case DecoderState.Finished:
          _isTerminal = true;
          _terminalResult = Lzma2DecodeResult.Finished;
          return ReturnWithProgress(Lzma2DecodeResult.Finished);
        default:
          _isTerminal = true;
          return _terminalResult;
      }
    }
  }

  private Lzma2DecodeResult SetError(Lzma2DecodeResult result)
  {
    _isTerminal = true;
    _terminalResult = result;
    _state = DecoderState.Error;
    return ReturnWithProgress(result);
  }

  private Lzma2DecodeResult ReturnWithProgress(Lzma2DecodeResult result)
  {
    var prog = new LzmaProgress(_totalRead, _totalWritten);
    if (prog != _lastProgress)
    {
      _lastProgress = prog;
      _progress?.Report(prog);
    }

    return result;
  }

  private static int GetExpectedHeaderSize(byte control)
  {
    if (control == 0x00)
      return 1;

    if (control is 0x01 or 0x02)
      return 3;

    if ((control & 0x80) == 0)
      return 1; // invalid control -> TryRead will return InvalidData

    return control >= 0xE0 ? 6 : 5;
  }

  private enum DecoderState
  {
    ReadingHeader,
    CopyingPayload,
    DecodingLzmaPayload,
    Finished,
    Error,
  }
}
