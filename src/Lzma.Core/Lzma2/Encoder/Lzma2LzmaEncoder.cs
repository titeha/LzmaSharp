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

  /// <summary>
  /// Кодирует данные в LZMA2 с реальным сжатием через match finder.
  /// </summary>
  /// <remarks>
  /// <para>
  /// MVP-режим: словарь сбрасывается на каждом чанке (каждый чанк независим — control 0xE0,
  /// props в каждом LZMA-чанке). Это просто, надёжно и удобно для будущего распараллеливания.
  /// Несущий словарь между чанками — отдельный поздний шаг.
  /// </para>
  /// <para>
  /// Для каждого чанка выбирается меньший по размеру вариант: LZMA-чанк или COPY-чанк.
  /// Размер чанка ограничен 64 КБ, потому что и COPY-чанк, и packSize LZMA-чанка хранят
  /// размер в 16 битах — это гарантирует, что любой чанк представим.
  /// </para>
  /// </remarks>
  public static byte[] Encode(
    ReadOnlySpan<byte> data,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize = 65536)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    if (maxUnpackChunkSize > 65536)
      throw new ArgumentOutOfRangeException(
        nameof(maxUnpackChunkSize),
        "Размер чанка ограничен 64 КБ: COPY-чанк и packSize LZMA-чанка хранят размер в 16 битах.");

    byte propsByte = lzmaProperties.ToByteOrThrow();

    using var ms = new MemoryStream((data.Length / 2) + 64);

    if (data.Length == 0)
    {
      ms.WriteByte(0x00);
      return ms.ToArray();
    }

    // Несущий словарь: match finder проходит ВЕСЬ вход со скользящим окном (совпадения
    // могут ссылаться через границы чанков, вплоть до dictionarySize назад), а один
    // LzmaEncoder кодирует поток операций, сохраняя словарь и модели между чанками.
    // Операции потоково (без материализации списка) уходят в ChunkingSink, который
    // кодирует их на лету и нарезает LZMA2-чанки. Первый LZMA-чанк сбрасывает словарь/
    // состояние/props (0xE0), последующие сохраняют всё и лишь переинициализируют range
    // coder (0x80) — как и ожидает декодер.
    int windowSize = WindowSizePow2(Math.Min(dictionarySize, data.Length));
    int[] head = new int[LzmaMatchFinder.HashTableSize];
    int[] prev = new int[windowSize];

    var encoder = new LzmaEncoder(lzmaProperties, dictionarySize);
    var sink = new ChunkingSink(ms, encoder, propsByte, maxUnpackChunkSize);

    LzmaMatchFinder.ParseAll(data, dictionarySize, head, prev, windowSize - 1, sink);
    sink.Finish();

    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  /// <summary>
  /// Потоковый приёмник операций match finder: кодирует операции по мере поступления
  /// и нарезает их на LZMA2-чанки по границам pack/unpack-размера. Первый чанк — 0xE0
  /// (сброс словаря/состояния/props), последующие — 0x80 (всё сохраняется, переинициализируется
  /// только range coder).
  /// </summary>
  private sealed class ChunkingSink(MemoryStream ms, LzmaEncoder encoder, byte propsByte, int maxUnpackChunkSize)
      : Lzma1.ILzmaOpSink
  {
    // Граница чанка по packed-размеру: с запасом до 64 КБ, т.к. packSize в заголовке 16-битный.
    private const int PackLimit = 60000;

    private bool _first = true;
    private int _chunkUnpack;

    public void Emit(LzmaEncodeOp op)
    {
      int opUnpack = op.Kind == LzmaEncodeOpKind.Match ? op.Length : 1;

      if (_chunkUnpack > 0
          && (encoder.PendingChunkBytes >= PackLimit || _chunkUnpack + opUnpack > maxUnpackChunkSize))
        FlushChunk();

      encoder.EncodeOp(op);
      _chunkUnpack += opUnpack;
    }

    public void Finish()
    {
      if (_chunkUnpack > 0)
        FlushChunk();
    }

    private void FlushChunk()
    {
      byte[] payload = encoder.FinishChunk();

      WriteLzmaChunk(
          ms,
          payload,
          unpackSize: _chunkUnpack,
          controlBase: _first ? (byte)0xE0 : (byte)0x80,
          writeProps: _first,
          propsByte: propsByte);

      encoder.BeginNextChunkKeepState();
      _first = false;
      _chunkUnpack = 0;
    }
  }

  private static int WindowSizePow2(int n)
  {
    if (n < 1)
      n = 1;

    int size = 1;
    while (size < n)
      size <<= 1;

    return size;
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
