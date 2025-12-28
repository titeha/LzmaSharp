using System;

namespace Lzma.Core.Lzma2;

/// <summary>
/// <para>Небольшой «считыватель» одного LZMA2-чанка из непрерывного буфера.</para>
/// <para>
/// Цель шага: научиться безопасно определять границы чанка (заголовок + payload),
/// НЕ выполняя распаковку.
/// </para>
/// <para>
/// Это фундамент для дальнейшей потоковой (incremental) распаковки:
/// - сперва корректно режем вход на чанки;
/// - затем уже учимся выполнять COPY/LZMA-ветки.
/// </para>
/// </summary>
public static class Lzma2ChunkReader
{
  /// <summary>
  /// <para>Пытается прочитать ОДИН чанк из <paramref name="input"/>.</para>
  /// <para>
  /// Важное правило для последующей потоковой модели:
  /// если данных не хватает (NeedMoreInput), мы ничего не «потребляем» (bytesConsumed = 0),
  /// потому что у нас нет внутреннего буфера для хранения частичного чанка.
  /// </para>
  /// </summary>
  public static Lzma2ReadChunkResult TryReadChunk(
      ReadOnlySpan<byte> input,
      out Lzma2ChunkHeader header,
      out ReadOnlySpan<byte> payload,
      out int bytesConsumed)
  {
    header = default;
    payload = default;
    bytesConsumed = 0;

    // 1) Сначала читаем заголовок (control + размеры + опциональные props).
    Lzma2ReadHeaderResult headerResult = Lzma2ChunkHeader.TryRead(input, out header, out int headerSize);
    if (headerResult == Lzma2ReadHeaderResult.NeedMoreInput)
      return Lzma2ReadChunkResult.NeedMoreInput;

    if (headerResult == Lzma2ReadHeaderResult.InvalidData)
      return Lzma2ReadChunkResult.InvalidData;

    // 2) Заголовок валиден и целиком присутствует во входе.
    // Теперь проверим, что во входе есть весь payload.
    int payloadSize = header.PayloadSize;

    // Защитная проверка (в теории PayloadSize не может быть отрицательным).
    if (payloadSize < 0)
      return Lzma2ReadChunkResult.InvalidData;

    int totalSize = headerSize + payloadSize;
    if (input.Length < totalSize)
    {
      // Данных на payload не хватило. Важно: ничего не потребляем.
      header = default;
      return Lzma2ReadChunkResult.NeedMoreInput;
    }

    payload = input.Slice(headerSize, payloadSize);
    bytesConsumed = totalSize;
    return Lzma2ReadChunkResult.Ok;
  }
}
