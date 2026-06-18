namespace Lzma.Core.Lzma1;

/// <summary>
/// Потоковый декодер формата LZMA-Alone (.lzma).
/// </summary>
/// <remarks>
/// <para>
/// Формат LZMA-Alone начинается с 13-байтового заголовка:
/// 1) properties (1 байт)
/// 2) dictionary size (4 байта, LE)
/// 3) uncompressed size (8 байт, LE)
/// </para>
/// <para>
/// На этом шаге поддерживается только случай, когда распакованный размер известен
/// (то есть <c>uncompressedSize != 0xFFFF_FFFF_FFFF_FFFF</c>).
/// </para>
/// <para>
/// Контракт Decode:
/// - метод можно вызывать многократно;
/// - декодер хранит состояние между вызовами;
/// - возвращает NeedMoreInput, если не хватает входа;
/// - возвращает NeedMoreOutput, если не хватает выхода;
/// - возвращает Finished, когда выдан весь ожидаемый распакованный вывод.
/// </para>
/// </remarks>
public sealed class LzmaAloneIncrementalDecoder
{
  private const int _headerSize = LzmaAloneHeader.HeaderSize;

  private readonly byte[] _headerBuffer = new byte[_headerSize];
  private int _headerFilled;

  private bool _headerParsed;
  private LzmaDecoder? _decoder;

  private long _remainingOutput;

  private bool _isTerminal;
  private LzmaAloneDecodeResult _terminalResult;

  private long _totalBytesRead;
  private long _totalBytesWritten;

  /// <summary>
  /// Сколько байт всего прочитано из входа (с начала потока).
  /// </summary>
  public long TotalBytesRead => _totalBytesRead;

  /// <summary>
  /// Сколько байт всего записано в выход (с начала потока).
  /// </summary>
  public long TotalBytesWritten => _totalBytesWritten;

  /// <summary>
  /// Сбрасывает декодер для обработки нового .lzma потока.
  /// </summary>
  public void Reset()
  {
    _headerFilled = 0;
    _headerParsed = false;

    _decoder = null;
    _remainingOutput = 0;

    _isTerminal = false;
    _terminalResult = LzmaAloneDecodeResult.Finished;

    _totalBytesRead = 0;
    _totalBytesWritten = 0;
  }

  /// <summary>
  /// Декодирует данные из <paramref name="input"/> в <paramref name="output"/>.
  /// </summary>
  public LzmaAloneDecodeResult Decode(
    ReadOnlySpan<byte> input,
    Span<byte> output,
    out int bytesConsumed,
    out int bytesWritten)
  {
    bytesConsumed = 0;
    bytesWritten = 0;

    if (_isTerminal)
      return _terminalResult;

    int inPos = 0;
    int outPos = 0;

    // 1) Добираем заголовок (13 байт)
    if (!_headerParsed)
    {
      int need = _headerSize - _headerFilled;
      int take = Math.Min(need, input.Length - inPos);
      if (take > 0)
      {
        input.Slice(inPos, take).CopyTo(_headerBuffer.AsSpan(_headerFilled, take));
        _headerFilled += take;
        inPos += take;
      }

      if (_headerFilled < _headerSize)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return LzmaAloneDecodeResult.NeedMoreInput;
      }

      // Заголовок собран — парсим
      if (LzmaAloneHeader.TryRead(_headerBuffer, out var header, out _) != LzmaAloneHeader.ReadResult.Ok)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.InvalidData);
      }

      // В LZMA-Alone "неизвестный распакованный размер" задаётся как 0xFF..FF.
      // На уровне нашего парсера это превращается в null (см. LzmaAloneHeader.TryRead).
      if (header.UncompressedSize is null)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.NotSupported);
      }

      if (header.DictionarySize == 0)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.InvalidData);
      }

      if (header.DictionarySize > int.MaxValue)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.NotSupported);
      }

      if (header.UncompressedSize > long.MaxValue)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.NotSupported);
      }

      _decoder = new LzmaDecoder(header.Properties, header.DictionarySize);
      _remainingOutput = (long)header.UncompressedSize.Value;
      _headerParsed = true;

      // Если распакованный размер = 0, то мы уже закончили.
      if (_remainingOutput == 0)
      {
        bytesConsumed = inPos;
        _totalBytesRead += bytesConsumed;
        return SetTerminal(LzmaAloneDecodeResult.Finished);
      }
    }

    // 2) Декодируем полезную нагрузку (LZMA-поток) через LzmaDecoder.
    if (_decoder is null)
      return SetTerminal(LzmaAloneDecodeResult.InvalidData);

    if (_remainingOutput <= 0)
      return SetTerminal(LzmaAloneDecodeResult.Finished);

    // Ограничиваем выход ожидаемым остатком.
    int outLimit = output.Length - outPos;
    if (_remainingOutput < outLimit)
      outLimit = (int)_remainingOutput;

    // Если выходного места нет, просим больше output.
    if (outLimit <= 0)
    {
      bytesConsumed = inPos;
      bytesWritten = outPos;
      _totalBytesRead += bytesConsumed;
      _totalBytesWritten += bytesWritten;
      return LzmaAloneDecodeResult.NeedMoreOutput;
    }

    // Прогоняем декодер. Важно: передаём именно Slice(outPos, outLimit)
    // чтобы не выдать больше ожидаемого.
    var res = _decoder.Decode(
      input.Slice(inPos),
      out int innerConsumed,
      output.Slice(outPos, outLimit),
      out int innerWritten,
      out _);

    inPos += innerConsumed;
    outPos += innerWritten;
    _remainingOutput -= innerWritten;

    bytesConsumed = inPos;
    bytesWritten = outPos;

    _totalBytesRead += bytesConsumed;
    _totalBytesWritten += bytesWritten;

    if (_remainingOutput == 0)
      return SetTerminal(LzmaAloneDecodeResult.Finished);

    // Если за вызов не было прогресса, корректно возвращаем "нужно больше".
    // Это защищает от бесконечных циклов в тестах/вызывающем коде.
    if (innerConsumed == 0 && innerWritten == 0)
      return input.Length == bytesConsumed ? LzmaAloneDecodeResult.NeedMoreInput : SetTerminal(LzmaAloneDecodeResult.InvalidData);

    return res switch
    {
      LzmaDecodeResult.Ok => LzmaAloneDecodeResult.NeedMoreOutput,
      LzmaDecodeResult.NeedsMoreInput => LzmaAloneDecodeResult.NeedMoreInput,
      LzmaDecodeResult.InvalidData => SetTerminal(LzmaAloneDecodeResult.InvalidData),
      LzmaDecodeResult.NotImplemented => SetTerminal(LzmaAloneDecodeResult.NotSupported),
      _ => SetTerminal(LzmaAloneDecodeResult.InvalidData),
    };
  }

  private LzmaAloneDecodeResult SetTerminal(LzmaAloneDecodeResult result)
  {
    _isTerminal = true;
    _terminalResult = result;
    return result;
  }
}
