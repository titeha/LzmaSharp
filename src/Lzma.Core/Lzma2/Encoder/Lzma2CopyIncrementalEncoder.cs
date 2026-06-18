namespace Lzma.Core.Lzma2;

/// <summary>
/// <para>Потоковый (инкрементальный) энкодер LZMA2 для режима COPY (без сжатия).</para>
/// <para>
/// Он полезен на ранних шагах разработки, чтобы получить полностью рабочий пайплайн
/// «кодирование → декодирование» без реализации LZMA-сжатия.
/// </para>
/// <para>
/// Основные свойства:
/// - разбивает вход на COPY-чанки длиной до 64 КБ;
/// - первый чанк всегда помечает «reset dictionary» (0x01);
/// - последующие COPY-чанки используют «no reset» (0x02);
/// - при <paramref name="isFinal"/> == true в конце потока пишет end-marker (0x00);
/// - поддерживает очень маленькие буферы output (может дописывать заголовок/данные по частям).
/// </para>
/// </summary>
public sealed class Lzma2CopyIncrementalEncoder
{
  private enum State
  {
    Ready,
    WritingHeader,
    WritingPayload,
    WritingEndMarker,
    Finished,
  }

  // Спецификация LZMA2: COPY-чунк хранит unpackSize как (size-1) в 16 битах,
  // поэтому реальный максимальный размер = 65536.
  private const int _maxCopyChunkSize = 1 << 16;

  private const byte _copyResetDictionaryControl = 0x01;
  private const byte _copyNoResetDictionaryControl = 0x02;
  private const byte _endMarkerControl = 0x00;

  private readonly IProgress<LzmaProgress>? _progress;

  // Заголовок COPY-чанка: 1 байт control + 2 байта (size-1) big-endian.
  private readonly byte[] _header = new byte[3];

  private State _state = State.Ready;
  private bool _isFirstChunk = true;

  private int _headerOffset;
  private int _payloadRemaining;

  private long _totalInputBytes;
  private long _totalOutputBytes;

  public Lzma2CopyIncrementalEncoder(IProgress<LzmaProgress>? progress = null, int dictionarySize = Lzma2CopyEncoder.DefaultDictionarySize)
  {
    if (!Lzma2Properties.TryEncode(dictionarySize, out byte props))
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Некорректный размер словаря для LZMA2.");

    DictionarySize = dictionarySize;
    PropertiesByte = props;

    _progress = progress;
  }

  /// <summary>
  /// Сколько байт всего принято во вход (с начала потока).
  /// </summary>
  public long TotalBytesRead => _totalInputBytes;

  /// <summary>
  /// Сколько байт всего записано в выход (с начала потока).
  /// </summary>
  public long TotalBytesWritten => _totalOutputBytes;

  /// <summary>
  /// Размер словаря, для которого вычислен <see cref="PropertiesByte"/>.
  /// (Для COPY-чанков на декодирование не влияет, но нужен контейнерам.)
  /// </summary>
  public int DictionarySize { get; }

  /// <summary>
  /// LZMA2 properties byte (как в 7z): кодированное значение размера словаря.
  /// </summary>
  public byte PropertiesByte { get; }

  /// <summary>
  /// Сбрасывает состояние энкодера, чтобы начать новый LZMA2-поток.
  /// </summary>
  public void Reset()
  {
    _state = State.Ready;
    _isFirstChunk = true;

    _headerOffset = 0;
    _payloadRemaining = 0;

    _totalInputBytes = 0;
    _totalOutputBytes = 0;

    _progress?.Report(new LzmaProgress(_totalInputBytes, _totalOutputBytes));
  }

  /// <summary>
  /// Кодирует <paramref name="input"/> в LZMA2 (COPY) и пишет результат в <paramref name="output"/>.
  /// </summary>
  /// <remarks>
  /// Важное:
  /// - метод можно вызывать много раз;
  /// - энкодер хранит внутреннее состояние между вызовами;
  /// - если <paramref name="output"/> слишком маленький, возвращает <see cref="Lzma2EncodeResult.NeedMoreOutput"/>;
  /// - если вход закончился, но <paramref name="isFinal"/> == false, возвращает <see cref="Lzma2EncodeResult.NeedMoreInput"/>;
  /// - когда <paramref name="isFinal"/> == true и весь вход закодирован, энкодер дописывает end-marker и возвращает <see cref="Lzma2EncodeResult.Finished"/>.
  /// </remarks>
  public Lzma2EncodeResult Encode(
    ReadOnlySpan<byte> input,
    Span<byte> output,
    bool isFinal,
    out int bytesConsumed,
    out int bytesWritten,
    out LzmaProgress progress)
  {
    // Терминальное состояние: повторные вызовы безопасны.
    if (_state == State.Finished)
    {
      bytesConsumed = 0;
      bytesWritten = 0;
      progress = new LzmaProgress(_totalInputBytes, _totalOutputBytes);
      return Lzma2EncodeResult.Finished;
    }

    int inPos = 0;
    int outPos = 0;

    // Пишем "сколько получится": пока есть место в output.
    while (outPos < output.Length)
      switch (_state)
      {
        case State.Ready:
        {
          int availableInput = input.Length - inPos;
          if (availableInput > 0)
          {
            int chunkSize = availableInput;
            if (chunkSize > _maxCopyChunkSize)
              chunkSize = _maxCopyChunkSize;

            PrepareCopyHeader(chunkSize);
            _payloadRemaining = chunkSize;
            _state = State.WritingHeader;
            continue;
          }

          if (isFinal)
          {
            _state = State.WritingEndMarker;
            continue;
          }

          // Нечего делать: ждём ввод.
          goto Done;
        }
        case State.WritingHeader:
        {
          // Дописываем 3 байта заголовка.
          int remainingHeader = _header.Length - _headerOffset;
          if (remainingHeader <= 0)
          {
            _state = State.WritingPayload;
            continue;
          }

          int canWrite = output.Length - outPos;
          if (canWrite <= 0)
            goto Done;

          if (canWrite > remainingHeader)
            canWrite = remainingHeader;

          _header.AsSpan(_headerOffset, canWrite).CopyTo(output.Slice(outPos, canWrite));
          _headerOffset += canWrite;
          outPos += canWrite;

          if (_headerOffset == _header.Length)
            _state = State.WritingPayload;

          continue;
        }
        case State.WritingPayload:
        {
          if (_payloadRemaining == 0)
          {
            // Чанк завершён.
            _isFirstChunk = false;
            _state = State.Ready;
            continue;
          }

          int availableInput = input.Length - inPos;
          if (availableInput <= 0)
          {
            // Мы заранее объявили размер чанка в заголовке, поэтому теперь
            // обязаны получить эти байты во входе.
            goto Done;
          }

          int canWrite = output.Length - outPos;
          if (canWrite <= 0)
            goto Done;

          int copy = _payloadRemaining;
          if (copy > availableInput)
            copy = availableInput;
          if (copy > canWrite)
            copy = canWrite;

          if (copy <= 0)
            goto Done;

          input.Slice(inPos, copy).CopyTo(output.Slice(outPos, copy));
          inPos += copy;
          outPos += copy;
          _payloadRemaining -= copy;

          continue;
        }
        case State.WritingEndMarker:
        {
          if (output.Length - outPos <= 0)
            goto Done;

          output[outPos++] = _endMarkerControl;
          _state = State.Finished;
          goto Done;
        }
        default:
          goto Done;
      }

    Done:
    bytesConsumed = inPos;
    bytesWritten = outPos;

    _totalInputBytes += bytesConsumed;
    _totalOutputBytes += bytesWritten;

    progress = new LzmaProgress(_totalInputBytes, _totalOutputBytes);
    _progress?.Report(progress);

    if (_state == State.Finished)
      return Lzma2EncodeResult.Finished;

    // Если в этом вызове мы не сделали ни одного действия — подсказываем, чего не хватает.
    if (bytesConsumed == 0 && bytesWritten == 0)
    {
      // Если мы в процессе payload — нам точно нужен ввод.
      if (_state == State.WritingPayload)
        return Lzma2EncodeResult.NeedMoreInput;

      // Если мы не в payload, но output пустой, а дописать нужно (заголовок/конец) — нужен output.
      if (output.Length == 0)
        return Lzma2EncodeResult.NeedMoreOutput;

      // Если мы готовы начать новый чанк, но вход пустой и поток не завершён.
      if (_state == State.Ready && !isFinal)
        return Lzma2EncodeResult.NeedMoreInput;

      // В остальных случаях считаем, что не хватило места в выходе.
      return Lzma2EncodeResult.NeedMoreOutput;
    }

    return Lzma2EncodeResult.Ok;
  }

  private void PrepareCopyHeader(int chunkSize)
  {
    // COPY-чанк: control + (size-1) big-endian.
    // (size-1) нужен, потому что размер 1..65536 кодируется диапазоном 0..65535.
    int sizeMinus1 = chunkSize - 1;

    _header[0] = _isFirstChunk ? _copyResetDictionaryControl : _copyNoResetDictionaryControl;
    _header[1] = (byte)(sizeMinus1 >> 8);
    _header[2] = (byte)sizeMinus1;

    _headerOffset = 0;
  }
}
