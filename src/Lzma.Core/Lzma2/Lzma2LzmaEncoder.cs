using Lzma.Core.Lzma1;

namespace Lzma.Core.Lzma2;

/// <summary>
/// <para>
/// Простой LZMA2-энкодер для тестов: кодирует данные как один LZMA-чанк (или набор чанков),
/// а затем добавляет LZMA2 end marker (0x00).
/// </para>
/// <para>
/// ВАЖНО:
/// - Это не "полноценный" компрессор: здесь нет поиска совпадений (match finder).
/// - LZMA-часть в этом классе используется в режимах literal-only и script (для тестов).
/// </para>
/// </summary>
public static class Lzma2LzmaEncoder
{
  public static byte[] EncodeLiteralOnly(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    out byte lzmaPropertiesByte)
  {
    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    var enc = new LzmaEncoder(lzmaProperties, dictionarySize);

    byte[] payload = enc.EncodeLiteralOnly(data);

    // Один LZMA-чанк с props + end marker.
    using var ms = new MemoryStream(payload.Length + 16);

    WriteLzmaChunk(
      ms,
      payload,
      unpackSize: data.Length,
      controlBase: 0xE0, // сброс словаря + сброс состояния + props
      writeProps: true,
      propsByte: lzmaPropertiesByte);

    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  internal static byte[] EncodeScript(
    ReadOnlySpan<LzmaEncodeOp> script,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    out byte lzmaPropertiesByte)
  {
    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    int unpackedSize = EstimateUnpackSize(script);

    var enc = new LzmaEncoder(lzmaProperties, dictionarySize);

    byte[] payload = enc.EncodeScript(script);

    using var ms = new MemoryStream(payload.Length + 16);

    WriteLzmaChunk(
      ms,
      payload,
      unpackSize: unpackedSize,
      controlBase: 0xE0, // сброс словаря + сброс состояния + props
      writeProps: true,
      propsByte: lzmaPropertiesByte);

    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  private static int EstimateUnpackSize(ReadOnlySpan<LzmaEncodeOp> script)
  {
    int total = 0;

    for (int i = 0; i < script.Length; i++)
    {
      LzmaEncodeOp op = script[i];

      total += op.Kind switch
      {
        LzmaEncodeOpKind.Literal => 1,
        LzmaEncodeOpKind.Match => op.Length,
        _ => throw new InvalidOperationException($"Неизвестная операция скрипта: {op.Kind}."),
      };
    }

    return total;
  }

  /// <summary>
  /// Кодирует literal-only данные в несколько LZMA-чанков LZMA2. В первом чанке пишем props,
  /// затем — чанки без props (сброс состояния, но без сброса словаря).
  /// </summary>
  public static byte[] EncodeLiteralOnlyChunked(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize,
    out byte lzmaPropertiesByte)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    using var ms = new MemoryStream(data.Length + 64);

    bool isFirst = true;
    int offset = 0;

    while (offset < data.Length)
    {
      int take = Math.Min(maxUnpackChunkSize, data.Length - offset);
      ReadOnlySpan<byte> slice = data.Slice(offset, take);

      var enc = new LzmaEncoder(lzmaProperties, dictionarySize);

      byte[] payload = enc.EncodeLiteralOnly(slice);

      if (isFirst)
      {
        WriteLzmaChunk(
          ms,
          payload,
          unpackSize: slice.Length,
          controlBase: 0xE0,
          writeProps: true,
          propsByte: lzmaPropertiesByte);

        isFirst = false;
      }
      else
        WriteLzmaChunk(
                  ms,
                  payload,
                  unpackSize: slice.Length,
                  controlBase: 0xA0, // сброс состояния, без props
                  writeProps: false,
                  propsByte: 0);

      offset += take;
    }

    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  /// <summary>
  /// Как <see cref="EncodeLiteralOnlyChunked"/>, но для каждого чанка выбирает:
  /// - COPY-чанк (несжатый), если он короче по байтам;
  /// - или LZMA-чанк (literal-only) иначе.
  ///
  /// На данном шаге это простая эвристика "меньше байт — лучше" без каких-либо порогов.
  ///
  /// Ограничение: maxUnpackChunkSize должен быть &lt;= 64 КБ, так как COPY-чанк хранит размер в 16 битах.
  /// </summary>
  public static byte[] EncodeLiteralOnlyChunkedAuto(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize,
    out byte lzmaPropertiesByte)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    // COPY-чанк в LZMA2 — это 16-битный размер (0..65535) + 1 => максимум 65536 байт.
    if (maxUnpackChunkSize > 65536)
      throw new ArgumentOutOfRangeException(
              nameof(maxUnpackChunkSize),
              "На данном шаге auto-режим поддерживает только maxUnpackChunkSize <= 64 КБ (ограничение COPY-чанка)."
            );

    lzmaPropertiesByte = lzmaProperties.ToByteOrThrow();

    using var ms = new MemoryStream(data.Length + 64);

    bool wroteAnyChunk = false;
    bool wroteAnyLzmaChunk = false;

    int offset = 0;

    while (offset < data.Length)
    {
      int take = Math.Min(maxUnpackChunkSize, data.Length - offset);
      ReadOnlySpan<byte> slice = data.Slice(offset, take);

      // 1) Считаем, сколько будет весить LZMA (literal-only) для этого куска.
      var enc = new LzmaEncoder(lzmaProperties, dictionarySize);
      byte[] lzmaPayload = enc.EncodeLiteralOnly(slice);

      bool needProps = !wroteAnyLzmaChunk;

      int lzmaHeaderSize = needProps ? 6 : 5;
      int lzmaTotalSize = lzmaHeaderSize + lzmaPayload.Length;

      int copyTotalSize = 3 + slice.Length;

      bool chooseCopy = copyTotalSize < lzmaTotalSize;

      if (chooseCopy)
      {
        WriteCopyChunk(ms, slice, resetDictionary: !wroteAnyChunk);
        wroteAnyChunk = true;
        offset += take;
        continue;
      }

      byte controlBase;

      if (needProps)
      {
        // Если это первый LZMA-чанк, но до него уже были чанки (COPY),
        // то dictionary уже содержит данные, и сбрасывать его нельзя.
        //
        // 0xE0: сброс словаря + сброс состояния + props
        // 0xC0: сброс состояния + props (без сброса словаря)
        controlBase = wroteAnyChunk ? (byte)0xC0 : (byte)0xE0;
      }
      else // сброс состояния, без props
        controlBase = 0xA0;

      WriteLzmaChunk(
        ms,
        lzmaPayload,
        unpackSize: slice.Length,
        controlBase: controlBase,
        writeProps: needProps,
        propsByte: lzmaPropertiesByte);

      wroteAnyChunk = true;
      wroteAnyLzmaChunk = true;
      offset += take;
    }

    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  private static void WriteCopyChunk(Stream output, ReadOnlySpan<byte> unpacked, bool resetDictionary)
  {
    if (unpacked.IsEmpty)
      return;

    if (unpacked.Length > 65536)
      throw new ArgumentOutOfRangeException(nameof(unpacked), "COPY-чанк LZMA2 не может быть больше 64 КБ.");

    uint sizeMinus1 = (uint)unpacked.Length - 1;

    output.WriteByte(resetDictionary ? (byte)0x01 : (byte)0x02);
    output.WriteByte((byte)((sizeMinus1 >> 8) & 0xFF));
    output.WriteByte((byte)(sizeMinus1 & 0xFF));

    output.Write(unpacked);
  }

  private static void WriteLzmaChunk(
    Stream output,
    byte[] lzmaPayload,
    int unpackSize,
    byte controlBase,
    bool writeProps,
    byte propsByte)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unpackSize);

    if (lzmaPayload.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(lzmaPayload));

    uint unpackSizeMinus1 = (uint)unpackSize - 1;
    uint packSizeMinus1 = (uint)lzmaPayload.Length - 1;

    byte unpackHi = (byte)((unpackSizeMinus1 >> 16) & 0x1F);

    output.WriteByte((byte)(controlBase | unpackHi));
    output.WriteByte((byte)((unpackSizeMinus1 >> 8) & 0xFF));
    output.WriteByte((byte)(unpackSizeMinus1 & 0xFF));
    output.WriteByte((byte)((packSizeMinus1 >> 8) & 0xFF));
    output.WriteByte((byte)(packSizeMinus1 & 0xFF));

    if (writeProps)
      output.WriteByte(propsByte);

    output.Write(lzmaPayload, 0, lzmaPayload.Length);
  }
}
