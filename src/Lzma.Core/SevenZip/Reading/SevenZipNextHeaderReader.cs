using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат чтения NextHeader из 7z-потока.
/// </summary>
public enum SevenZipNextHeaderReadResult
{
  /// <summary>
  /// NextHeader успешно прочитан и прошёл проверку CRC.
  /// </summary>
  Ok,

  /// <summary>
  /// Нужны дополнительные входные данные.
  /// </summary>
  NeedMoreInput,

  /// <summary>
  /// Данные повреждены или не соответствуют формату.
  /// </summary>
  InvalidData,

  /// <summary>
  /// Формат корректен, но этот случай пока не поддержан реализацией.
  /// </summary>
  NotSupported,
}

/// <summary>
/// <para>Инкрементальный (потоковый) читатель NextHeader из контейнера 7z.</para>
/// <para>
/// На этом шаге мы НЕ пытаемся распарсить содержимое NextHeader.
/// Мы делаем маленький шаг: умеем найти NextHeader по смещению/размеру,
/// полностью прочитать его и проверить CRC.
/// </para>
/// <para>
/// Важно:
/// - вход подаётся кусками (ReadOnlySpan);
/// - метод <see cref="Read"/> можно вызывать много раз;
/// - объект хранит состояние между вызовами;
/// - при <see cref="SevenZipNextHeaderReadResult.NeedMoreInput"/> декодер
///   съедает максимально возможный кусок входа.
/// - буферы растут по фактически поступившим данным, а не выделяются целиком по заявленным
///   размерам из заголовка (SEC-013): физический буфер никогда не превышает заявленный размер.
/// </para>
/// </summary>
public sealed class SevenZipNextHeaderReader
{
  private enum Step
  {
    SignatureHeader,
    SkipToNextHeader,
    NextHeader,
    Done,
    Error,
  }

  // Буферизуем все байты между SignatureHeader и NextHeader (упакованные данные), чтобы позже
  // декодировать EncodedHeader и извлекать содержимое. Буфер выделяется РОВНО под фактический размер
  // packed-области, поэтому лимит нужен лишь чтобы не превысить границу int (in-memory путь и так
  // ограничен 2 ГиБ). Раньше здесь стояло 64 МБ — из-за чего любой 7z с данными > 64 МБ не открывался
  // in-memory (NotSupported), хотя формат валиден; для архивов больше 2 ГиБ есть потоковый путь.
  private const int _maxPackedStreamsBytes = int.MaxValue;

  // SEC-013: заявные размеры держим отдельно от физического буфера — буфер растёт по мере
  // поступления данных, а не выделяется целиком по недоверенному значению заголовка.
  private int _packedStreamsTarget;
  private int _nextHeaderTarget;

  // Начальный запас роста: достаточно для типичного заголовка и ограничивает число переразбиений.
  private const int _growFloor = 64 * 1024;

  // SignatureHeader всегда 32 байта.
  private readonly byte[] _signatureHeaderBuffer = new byte[SevenZipSignatureHeader.Size];
  private int _signatureHeaderFilled;

  private SevenZipSignatureHeader _signatureHeader;
  private bool _hasSignatureHeader;

  // Сколько байт ещё нужно пропустить (после signature header) до NextHeader.
  private ulong _skipRemaining;

  // Байты между SignatureHeader и NextHeader (упакованные потоки).
  private byte[] _packedStreams = [];
  private int _packedStreamsFilled;

  // Буфер NextHeader.
  private byte[] _nextHeader = [];
  private int _nextHeaderFilled;

  private Step _step;
  private SevenZipNextHeaderReadResult _terminalResult;

  /// <summary>
  /// Создаёт новый reader в состоянии "в начале потока".
  /// </summary>
  public SevenZipNextHeaderReader() => Reset();

  /// <summary>
  /// Прочитанный SignatureHeader (после успешного чтения).
  /// </summary>
  public bool HasSignatureHeader => _hasSignatureHeader;

  /// <summary>
  /// Значение SignatureHeader. Допустимо читать только если <see cref="HasSignatureHeader"/> == true.
  /// </summary>
  public SevenZipSignatureHeader SignatureHeader
  {
    get
    {
      if (!_hasSignatureHeader)
        throw new InvalidOperationException("SignatureHeader ещё не прочитан.");
      return _signatureHeader;
    }
  }

  /// <summary>
  /// Считанные байты NextHeader. Допустимо читать только после результата <see cref="SevenZipNextHeaderReadResult.Ok"/>.
  /// </summary>
  public ReadOnlyMemory<byte> NextHeader => _nextHeader.AsMemory(0, _nextHeaderTarget);

  /// <summary>
  /// Байты между сигнатурным заголовком и NextHeader (упакованные данные архива).
  /// </summary>
  public ReadOnlyMemory<byte> PackedStreams => _packedStreams.AsMemory(0, _packedStreamsFilled);

  /// <summary>
  /// Сбрасывает состояние и позволяет читать новый 7z-поток.
  /// </summary>
  public void Reset()
  {
    _signatureHeaderFilled = 0;
    _hasSignatureHeader = false;
    _signatureHeader = default;

    _skipRemaining = 0;

    _packedStreamsTarget = 0;
    _packedStreams = [];
    _packedStreamsFilled = 0;

    _nextHeaderTarget = 0;
    _nextHeader = [];
    _nextHeaderFilled = 0;

    _step = Step.SignatureHeader;
    _terminalResult = SevenZipNextHeaderReadResult.Ok;
  }

  /// <summary>
  /// <para>Читает входные данные и (когда данных достаточно) извлекает NextHeader.</para>
  /// <para>
  /// Контракт по bytesConsumed:
  /// - при любом результате декодер возвращает, сколько байт он "съел" из input;
  /// - если результат NeedMoreInput — это обычно equals input.Length (мы забрали весь кусок в свои буферы).
  /// </para>
  /// </summary>
  public SevenZipNextHeaderReadResult Read(ReadOnlySpan<byte> input, out int bytesConsumed)
  {
    bytesConsumed = 0;

    // Терминальное состояние — возвращаем его и ничего не потребляем.
    if (_step is Step.Done or Step.Error)
      return _terminalResult;

    // Двигаемся по шагам. Важно: НЕ делаем рекурсию, всё в одном методе.
    while (true)
    {
      if (_step == Step.SignatureHeader)
      {
        // Докачиваем ровно 32 байта signature header.
        int need = SevenZipSignatureHeader.Size - _signatureHeaderFilled;
        int take = Math.Min(need, input.Length - bytesConsumed);

        if (take > 0)
        {
          input.Slice(bytesConsumed, take)
            .CopyTo(_signatureHeaderBuffer.AsSpan(_signatureHeaderFilled));

          _signatureHeaderFilled += take;
          bytesConsumed += take;
        }

        if (_signatureHeaderFilled < SevenZipSignatureHeader.Size)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        // Парсим signature header (и внутри проверяем CRC start header).
        var res = SevenZipSignatureHeader.TryRead(
          _signatureHeaderBuffer,
          out _signatureHeader);

        if (res == SevenZipSignatureHeader.ReadResult.InvalidData)
          return SetTerminal(SevenZipNextHeaderReadResult.InvalidData);

        // Теоретически сюда не придём, потому что у нас уже есть все 32 байта.
        if (res == SevenZipSignatureHeader.ReadResult.NeedMoreInput)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        _hasSignatureHeader = true;

        // Подготовка к следующему шагу.
        _skipRemaining = _signatureHeader.NextHeaderOffset;

        if (_skipRemaining > _maxPackedStreamsBytes)
          return SetTerminal(SevenZipNextHeaderReadResult.NotSupported);

        // SEC-013: буфер не выделяется по смещению NextHeaderOffset. Размер из заголовка
        // запоминается как цель, физический массив растёт в EnsureCapacity по фактически
        // поступившим данным. Для файла-«бомбы» в 96 байт это килобайты вместо гигабайтов,
        // а для корректного архива, поданного целиком, — ровно одно выделение нужного размера.
        _packedStreams = [];
        _packedStreamsTarget = (int)_skipRemaining;
        _packedStreamsFilled = 0;

        // Если NextHeaderSize не помещается в int — пока не поддерживаем.
        if (_signatureHeader.NextHeaderSize > int.MaxValue)
          return SetTerminal(SevenZipNextHeaderReadResult.NotSupported);

        _nextHeader = [];
        _nextHeaderTarget = (int)_signatureHeader.NextHeaderSize;
        _nextHeaderFilled = 0;

        _step = Step.SkipToNextHeader;

        // Продолжаем обработку в этом же вызове, если вход ещё есть.
        continue;
      }

      if (_step == Step.SkipToNextHeader)
      {
        if (_skipRemaining == 0)
        {
          _step = Step.NextHeader;
          continue;
        }

        // Съедаем максимум из текущего input.
        int available = input.Length - bytesConsumed;
        if (available <= 0)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        ulong takeU64 = Math.Min((ulong)available, _skipRemaining);
        int take = (int)takeU64;

        if (take > 0)
        {
          EnsureCapacity(ref _packedStreams, _packedStreamsFilled + take, _packedStreamsTarget);
          input.Slice(bytesConsumed, take)
            .CopyTo(_packedStreams.AsSpan(_packedStreamsFilled));
          _packedStreamsFilled += take;
        }

        bytesConsumed += take;
        _skipRemaining -= takeU64;

        if (_skipRemaining > 0)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        _step = Step.NextHeader;
        continue;
      }

      if (_step == Step.NextHeader)
      {
        int need = _nextHeaderTarget - _nextHeaderFilled;
        if (need == 0)
        {
          // Проверяем CRC NextHeader по заявленной длине, а не по физическому массиву.
          uint crc = Crc32.Compute(_nextHeader.AsSpan(0, _nextHeaderTarget));
          if (crc != _signatureHeader.NextHeaderCrc)
            return SetTerminal(SevenZipNextHeaderReadResult.InvalidData);

          return SetTerminal(SevenZipNextHeaderReadResult.Ok);
        }

        int available = input.Length - bytesConsumed;
        if (available <= 0)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        int take = Math.Min(need, available);

        if (take > 0)
        {
          EnsureCapacity(ref _nextHeader, _nextHeaderFilled + take, _nextHeaderTarget);
          input.Slice(bytesConsumed, take)
            .CopyTo(_nextHeader.AsSpan(_nextHeaderFilled));
          _nextHeaderFilled += take;
        }

        bytesConsumed += take;

        // Если NextHeader ещё не полностью прочитан — ждём продолжения.
        if (_nextHeaderFilled < _nextHeaderTarget)
          return SevenZipNextHeaderReadResult.NeedMoreInput;

        // Иначе — цикл продолжится и попадёт в ветку need==0.
        continue;
      }

      // Неверное состояние.
      return SetTerminal(SevenZipNextHeaderReadResult.InvalidData);
    }
  }

  /// <summary>
  /// SEC-013: наращивает физический буфер так, чтобы в него поместилось <paramref name="required"/>
  /// байт, но не больше заявленного <paramref name="target"/>. Рост геометрический с полом
  /// <c>_growFloor</c>, поэтому при подаче данных кусками число переразбиений логарифмическое,
  /// а при подаче целиком выделяется ровно нужный размер.
  /// </summary>
  private static void EnsureCapacity(ref byte[] buffer, int required, int target)
  {
    if (buffer.Length >= required)
      return;

    int capped = Math.Min(required, target);
    int doubled = buffer.Length == 0 ? 0 : buffer.Length * 2;
    int newSize = Math.Min(target, Math.Max(Math.Max(capped, doubled), Math.Min(_growFloor, target)));
    if (newSize < required)
      newSize = Math.Min(target, required);

    Array.Resize(ref buffer, newSize);
  }

  private SevenZipNextHeaderReadResult SetTerminal(SevenZipNextHeaderReadResult result)
  {
    _terminalResult = result;
    _step = result == SevenZipNextHeaderReadResult.Ok ? Step.Done : Step.Error;
    return result;
  }
}
