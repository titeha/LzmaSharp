namespace Lzma.Core.Lzma2;

/// <summary>
/// Простейший декодер LZMA2, который на этом шаге умеет обрабатывать только:
/// <list type="bullet">
/// <item><description>«copy» (несжатые) чанки: control = 0x01 или 0x02</description></item>
/// <item><description>маркер конца потока: control = 0x00</description></item>
/// </list>
///
/// <para>LZMA-чанки (control >= 0x80) здесь пока сознательно НЕ поддерживаются.</para>
/// <para>
/// Зачем он нужен?
/// 1) Отладить корректный разбор LZMA2-структуры чанков.
/// 2) Отладить потоковую модель (bytesConsumed/bytesWritten) без RangeCoder и словаря.
/// </para>
/// <para>Когда это будет стабильно и покрыто тестами, следующим шагом подключим LZMA-чанки.</para>
/// </summary>
public static class Lzma2CopyDecoder
{
  /// <summary>
  /// <para>
  /// Пытается распаковать входной LZMA2-поток, содержащий ТОЛЬКО copy-чанки (0x01/0x02)
  /// и маркер конца потока (0x00).
  /// </para>
  /// <para>
  /// Важно: метод не хранит внутреннего состояния.
  /// Поэтому если для текущего (следующего) чанка не хватает входа или выхода —
  /// он возвращает соответствующий результат и НЕ потребляет байты этого чанка.
  /// </para>
  /// <para>bytesConsumed/bytesWritten отражают прогресс по входу/выходу относительно переданных span-ов.</para>
  /// </summary>
  public static Lzma2DecodeResult Decode(
      ReadOnlySpan<byte> input,
      Span<byte> output,
      out int bytesConsumed,
      out int bytesWritten)
  {
    bytesConsumed = 0;
    bytesWritten = 0;

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      ReadOnlySpan<byte> remaining = input.Slice(inPos);

      // 1) Читаем заголовок чанка.
      //    Если данных мало — просто просим добавить вход.
      var headerRes = Lzma2ChunkHeader.TryRead(remaining, out var header, out int headerBytes);
      if (headerRes == Lzma2ReadHeaderResult.NeedMoreInput)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.NeedMoreInput;
      }

      if (headerRes == Lzma2ReadHeaderResult.InvalidData)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.InvalidData;
      }

      // 2) На этом шаге поддерживаем только End и Copy.
      if (header.Kind == Lzma2ChunkKind.Lzma)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.NotSupported;
      }

      if (header.Kind == Lzma2ChunkKind.End)
      {
        // Считаем маркер конца (0x00) потреблённым, чтобы caller мог сдвинуть input.
        inPos += header.TotalSize; // для End это всегда 1

        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.Finished;
      }

      // 3) Copy-чанк.
      //    Сначала проверяем, что выходной буфер может вместить весь распакованный блок.
      if (output.Length - outPos < header.UnpackSize)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.NeedMoreOutput;
      }

      //    Затем проверяем, что вход содержит весь чанк целиком (заголовок + payload).
      if (remaining.Length < header.TotalSize)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.NeedMoreInput;
      }

      ReadOnlySpan<byte> payload = remaining.Slice(headerBytes, header.PayloadSize);

      // Самопроверка: для copy-чанка размер payload обязан совпадать с UnpackSize.
      if (payload.Length != header.UnpackSize)
      {
        bytesConsumed = inPos;
        bytesWritten = outPos;
        return Lzma2DecodeResult.InvalidData;
      }

      payload.CopyTo(output.Slice(outPos, payload.Length));

      inPos += header.TotalSize;
      outPos += payload.Length;
    }
  }
}

/// <summary>
/// Итог выполнения декодирования.
/// </summary>
public enum Lzma2DecodeResult
{
  /// <summary>Декодер дошёл до маркера конца (control = 0x00).</summary>
  Finished = 0,

  /// <summary>Не хватает входных данных, чтобы продолжить.</summary>
  NeedMoreInput = 1,

  /// <summary>Не хватает места в выходном буфере, чтобы записать следующий блок целиком.</summary>
  NeedMoreOutput = 2,

  /// <summary>Поток повреждён или имеет некорректную структуру.</summary>
  InvalidData = 3,

  /// <summary>Встретили LZMA-чанк (control >= 0x80) — пока не реализовано на этом шаге.</summary>
  NotSupported = 4,
}
