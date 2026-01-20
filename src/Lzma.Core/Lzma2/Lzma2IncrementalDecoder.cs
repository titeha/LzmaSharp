// Copyright (c) LzmaSharp project.
// Лицензия: см. корень репозитория.

namespace Lzma.Core.Lzma2;

/// <summary>
/// <para>Инкрементальный (поштучный) декодер LZMA2.</para>
/// <para>
/// На этом шаге мы реализуем только самый простой поднабор формата:
/// - COPY-чанки (0x01 / 0x02) — «несжатые» данные, которые просто копируются в выход;
/// - END-маркер (0x00).
/// </para>
/// <para>Сжатые LZMA-чанки (control >= 0x80) пока не реализованы и возвращают <see cref="Lzma2DecodeResult.NotSupported"/>.</para>
/// <para>
/// Зачем нужен отдельный класс, если есть <see cref="Lzma2CopyDecoder"/>?
/// - <see cref="Lzma2CopyDecoder"/> — статическая «пакетная» функция: удобна для тестов и простых сценариев.
/// - Этот класс — stateful/streaming вариант: он умеет принимать вход маленькими кусочками
///   без необходимости «склеивать» хвосты заголовков снаружи. Это важно для будущей интеграции со Stream/IO.
/// </para>
/// <para>Также здесь сразу «закладываем» прогресс: счётчики общего входа/выхода и необязательный callback.</para>
/// </summary>
/// <remarks>
/// Создаёт новый инкрементальный декодер.
/// </remarks>
/// <param name="progress">
/// Необязательный репортёр прогресса.
/// Он будет вызываться только когда меняются счётчики (BytesRead/BytesWritten).
/// </param>
public sealed class Lzma2IncrementalDecoder(IProgress<LzmaProgress>? progress = null)
{
  private enum DecoderState
  {
    ReadingHeader,
    CopyingPayload,
    Finished,
    Error,
  }

  private readonly IProgress<LzmaProgress>? _progress = progress;

  // Заголовок LZMA2-чанка максимум 6 байт.
  private readonly byte[] _headerBuffer = new byte[6];
  private int _headerFilled;
  private int _headerExpected;

  // Для COPY-чанка: сколько байт полезной нагрузки осталось скопировать.
  private uint _copyRemaining;

  private DecoderState _state = DecoderState.ReadingHeader;
  private Lzma2DecodeResult _errorResult = Lzma2DecodeResult.InvalidData;

  private long _totalBytesRead;
  private long _totalBytesWritten;

  private long _lastReportedRead;
  private long _lastReportedWritten;

  /// <summary>
  /// Сколько байт всего было потреблено из входа (сжатые данные).
  /// </summary>
  public long TotalBytesRead => _totalBytesRead;

  /// <summary>
  /// Сколько байт всего было записано в выход (распакованные данные).
  /// </summary>
  public long TotalBytesWritten => _totalBytesWritten;

  /// <summary>
  /// Удобный флаг для потребителя (например, Stream-обёртки).
  /// </summary>
  public bool IsFinished => _state == DecoderState.Finished;

  /// <summary>
  /// Удобный флаг для потребителя.
  /// </summary>
  public bool HasError => _state == DecoderState.Error;

  /// <summary>
  /// Сбрасывает внутреннее состояние, чтобы переиспользовать экземпляр.
  /// </summary>
  public void Reset()
  {
    _headerFilled = 0;
    _headerExpected = 0;
    _copyRemaining = 0;
    _state = DecoderState.ReadingHeader;
    _errorResult = Lzma2DecodeResult.InvalidData;

    _totalBytesRead = 0;
    _totalBytesWritten = 0;
    _lastReportedRead = 0;
    _lastReportedWritten = 0;
  }

  /// <summary>
  /// Обрабатывает часть входных данных и пишет распакованные байты в <paramref name="output"/>.
  /// </summary>
  /// <param name="input">Очередной кусок входа (LZMA2-поток).</param>
  /// <param name="output">Буфер для распакованных данных.</param>
  /// <param name="bytesConsumed">Сколько байт было реально прочитано из <paramref name="input"/>.</param>
  /// <param name="bytesWritten">Сколько байт было реально записано в <paramref name="output"/>.</param>
  /// <returns>
  /// - <see cref="Lzma2DecodeResult.Finished"/> — встречен END-маркер.
  /// - <see cref="Lzma2DecodeResult.NeedMoreInput"/> — вход закончился, а продолжать нужно.
  /// - <see cref="Lzma2DecodeResult.NeedMoreOutput"/> — выходной буфер кончился, а ещё есть что писать.
  /// - <see cref="Lzma2DecodeResult.InvalidData"/> — поток повреждён.
  /// - <see cref="Lzma2DecodeResult.NotSupported"/> — встретился LZMA-чанк (пока не реализован).
  /// </returns>
  public Lzma2DecodeResult Decode(
      ReadOnlySpan<byte> input,
      Span<byte> output,
      out int bytesConsumed,
      out int bytesWritten)
  {
    bytesConsumed = 0;
    bytesWritten = 0;

    while (true)
    {
      switch (_state)
      {
        case DecoderState.Finished:
          return ReturnWithProgress(Lzma2DecodeResult.Finished);

        case DecoderState.Error:
          return ReturnWithProgress(_errorResult);

        case DecoderState.ReadingHeader:
        {
          // 1) Считываем control-байт, если ещё не считали.
          if (_headerFilled == 0)
          {
            if (input.IsEmpty)
              return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

            byte control = input[0];
            input = input.Slice(1);

            _headerBuffer[0] = control;
            _headerFilled = 1;

            bytesConsumed++;
            _totalBytesRead++;

            _headerExpected = GetExpectedHeaderSize(control);
            if (_headerExpected < 0)
            {
              // Невалидный control.
              SetError(Lzma2DecodeResult.InvalidData);
              return ReturnWithProgress(Lzma2DecodeResult.InvalidData);
            }
          }

          // 2) Добираем оставшиеся байты заголовка.
          if (_headerFilled < _headerExpected)
          {
            int need = _headerExpected - _headerFilled;
            int take = Math.Min(need, input.Length);

            if (take == 0)
              return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

            input.Slice(0, take).CopyTo(_headerBuffer.AsSpan(_headerFilled, take));
            input = input.Slice(take);

            _headerFilled += take;
            bytesConsumed += take;
            _totalBytesRead += take;

            if (_headerFilled < _headerExpected)
              return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);
          }

          // 3) Заголовок готов, парсим его.
          var headerResult = Lzma2ChunkHeader.TryRead(
            _headerBuffer.AsSpan(0, _headerExpected),
            out var header,
            out int consumed);
          if (headerResult != Lzma2ReadHeaderResult.Ok || consumed != _headerExpected)
          {
            SetError(Lzma2DecodeResult.InvalidData);
            return ReturnWithProgress(Lzma2DecodeResult.InvalidData);
          }

          // Сбрасываем буфер заголовка для следующего чанка.
          _headerFilled = 0;
          _headerExpected = 0;

          // 4) Реагируем на тип чанка.
          if (header.Kind == Lzma2ChunkKind.End)
          {
            _state = DecoderState.Finished;
            return ReturnWithProgress(Lzma2DecodeResult.Finished);
          }

          if (header.Kind == Lzma2ChunkKind.Lzma)
          {
            // Следующий шаг будет посвящён именно этому.
            SetError(Lzma2DecodeResult.NotSupported);
            return ReturnWithProgress(Lzma2DecodeResult.NotSupported);
          }

          // COPY-чанк. Для нас payload длиной UnpackSize.
          _copyRemaining = (uint)header.UnpackSize;
          _state = DecoderState.CopyingPayload;

          // Продолжаем цикл: возможно, payload уже находится в input.
          continue;
        }

        case DecoderState.CopyingPayload:
        {
          if (_copyRemaining == 0)
          {
            _state = DecoderState.ReadingHeader;
            continue;
          }

          // Нужен выходной буфер: мы должны писать распакованные байты.
          if (output.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreOutput);

          // Нужен вход: без него нечего копировать.
          if (input.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          int toCopy = Math.Min(input.Length, output.Length);
          if ((uint)toCopy > _copyRemaining)
            toCopy = (int)_copyRemaining;

          input.Slice(0, toCopy).CopyTo(output);

          input = input.Slice(toCopy);
          output = output.Slice(toCopy);

          bytesConsumed += toCopy;
          bytesWritten += toCopy;
          _totalBytesRead += toCopy;
          _totalBytesWritten += toCopy;

          _copyRemaining -= (uint)toCopy;

          // Если payload дописали — возвращаемся к чтению заголовка следующего чанка.
          if (_copyRemaining == 0)
          {
            _state = DecoderState.ReadingHeader;
            continue;
          }

          // Иначе осталось дописывать payload — нужно либо больше input, либо больше output.
          if (output.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreOutput);

          if (input.IsEmpty)
            return ReturnWithProgress(Lzma2DecodeResult.NeedMoreInput);

          // Теоретически сюда не попадём (мы копируем максимум), но оставим как "страховку".
          continue;
        }

        default:
          // На случай, если добавим состояние и забудем обработать.
          SetError(Lzma2DecodeResult.InvalidData);
          return ReturnWithProgress(Lzma2DecodeResult.InvalidData);
      }
    }
  }

  private void SetError(Lzma2DecodeResult error)
  {
    _state = DecoderState.Error;
    _errorResult = error;
  }

  /// <summary>
  /// В LZMA2 размер заголовка определяется только по control-байту.
  /// </summary>
  private static int GetExpectedHeaderSize(byte control)
  {
    // END.
    if (control == 0x00)
      return 1;

    // COPY. У этих чанков ровно 3 байта заголовка.
    if (control == 0x01 || control == 0x02)
      return 3;

    // LZMA.
    // Формат управляющего байта (по спецификации LZMA2 из lzma-sdk):
    // 100xxxxx ... 111xxxxx  => LZMA-чанк.
    // Если (control & 0x40) != 0 (диапазоны 0xC0..0xFF), то после 5 байт заголовка
    // присутствует дополнительный байт свойств (props).
    if (control >= 0x80)
      return control >= 0xC0 ? 6 : 5;

    // Всё остальное (0x03..0x7F) — невалидно.
    return -1;
  }

  private Lzma2DecodeResult ReturnWithProgress(Lzma2DecodeResult result)
  {
    // Репортим прогресс только когда счётчики реально изменились.
    if (_progress is not null
        && (_totalBytesRead != _lastReportedRead || _totalBytesWritten != _lastReportedWritten))
    {
      _lastReportedRead = _totalBytesRead;
      _lastReportedWritten = _totalBytesWritten;
      _progress.Report(new LzmaProgress(_totalBytesRead, _totalBytesWritten));
    }

    return result;
  }
}
