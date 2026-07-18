using System.Buffers.Binary;

using Lzma.Core.Checksums;
using Lzma.Core.Deflate;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Потоковое извлечение ZIP из seekable-<see cref="Stream"/> на диск, БЕЗ загрузки архива в
/// память.</para>
/// <para>
/// По метаданным из <see cref="ZipStreamReader"/> прыгает к данным каждого члена и пишет его на диск
/// потоком: Store копируется чанками (размер любой), Deflate распаковывается через потоковый
/// <see cref="DeflateDecoder"/> (кольцевое окно). CRC проверяется на лету. Безопасная запись и откат —
/// через общее ядро <see cref="ZipExtractor.ExtractCore"/>.
/// </para>
/// </summary>
public static class ZipStreamExtractor
{
  private const uint LocalFileSignature = 0x04034b50;
  private const int LocalHeaderSize = 30;

  private const ushort MethodStore = 0;
  private const ushort MethodDeflate = 8;

  private const int CopyBufferSize = 1 << 16;

  /// <summary>
  /// Извлекает элементы <paramref name="entries"/> (метаданные из <see cref="ZipStreamReader"/>) из
  /// <paramref name="archive"/> в <paramref name="destinationDirectory"/>, читая данные потоком.
  /// </summary>
  public static ZipExtractResult ExtractToDirectory(
      Stream archive,
      IReadOnlyList<ZipStreamEntry> entries,
      string destinationDirectory,
      bool overwrite = false,
      IProgress<string>? currentFile = null,
      CancellationToken token = default,
      IProgress<SevenZipProgress>? progress = null)
  {
    if (archive is null || !archive.CanSeek || !archive.CanRead || entries is null)
      return ZipExtractResult.InvalidData;

    // Итог для процентов — суммарный распакованный размер (папки нулевые).
    long total = 0;
    for (int i = 0; i < entries.Count; i++)
      if (!entries[i].IsDirectory)
        total += entries[i].UncompressedSize;

    // Накопленный распакованный объём (в «коробке», чтобы делегат мог его наращивать).
    long[] processed = [0];

    return ZipExtractor.ExtractCore(
        entries.Count,
        i => entries[i].Name,
        i => entries[i].IsDirectory,
        (i, fullPath) => WriteEntry(archive, entries[i], fullPath, token, progress, total, processed),
        destinationDirectory,
        overwrite,
        currentFile,
        token);
  }

  // Пишет данные одного члена на диск: seek к данным по локальному заголовку → Store-копия/Deflate-декод.
  private static ZipExtractResult WriteEntry(
      Stream archive, ZipStreamEntry entry, string fullPath, CancellationToken token,
      IProgress<SevenZipProgress>? progress, long total, long[] processed)
  {
    // Длины имени/extra берём из ЛОКАЛЬНОГО заголовка (могут отличаться от центрального).
    archive.Position = entry.LocalHeaderOffset;

    Span<byte> local = stackalloc byte[LocalHeaderSize];
    try
    {
      archive.ReadExactly(local);
    }
    catch (EndOfStreamException)
    {
      return ZipExtractResult.InvalidData;
    }

    if (BinaryPrimitives.ReadUInt32LittleEndian(local[..4]) != LocalFileSignature)
      return ZipExtractResult.InvalidData;

    int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(local.Slice(26, 2));
    int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(local.Slice(28, 2));
    long dataStart = entry.LocalHeaderOffset + LocalHeaderSize + nameLen + extraLen;

    if (dataStart + entry.CompressedSize > archive.Length)
      return ZipExtractResult.InvalidData;

    archive.Position = dataStart;

    using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write);

    if (entry.Method == MethodStore)
    {
      if (entry.CompressedSize != entry.UncompressedSize)
        return ZipExtractResult.InvalidData;

      return CopyStore(archive, entry, file, token, progress, total, processed);
    }

    if (entry.Method == MethodDeflate)
      return InflateToFile(archive, entry, file, progress, total, processed);

    return ZipExtractResult.InvalidData; // прочие методы отсеяны на чтении каталога
  }

  // Store: копирует CompressedSize байт архив→файл чанками, считая CRC и репортя прогресс.
  private static ZipExtractResult CopyStore(
      Stream archive, ZipStreamEntry entry, Stream file, CancellationToken token,
      IProgress<SevenZipProgress>? progress, long total, long[] processed)
  {
    byte[] buffer = new byte[CopyBufferSize];
    long remaining = entry.CompressedSize;
    uint crc = Crc32.InitialState;

    while (remaining > 0)
    {
      token.ThrowIfCancellationRequested();

      int want = (int)Math.Min(remaining, buffer.Length);
      int read = archive.Read(buffer, 0, want);
      if (read <= 0)
        return ZipExtractResult.InvalidData;

      crc = Crc32.Update(crc, buffer.AsSpan(0, read));
      file.Write(buffer, 0, read);
      remaining -= read;

      processed[0] += read;
      progress?.Report(new SevenZipProgress(processed[0], total));
    }

    return Crc32.Finalize(crc) == entry.Crc ? ZipExtractResult.Ok : ZipExtractResult.InvalidData;
  }

  // Deflate: читает сжатый член в память (≤2 ГиБ) и распаковывает потоково в файл через кольцевое окно.
  private static ZipExtractResult InflateToFile(
      Stream archive, ZipStreamEntry entry, Stream file,
      IProgress<SevenZipProgress>? progress, long total, long[] processed)
  {
    // Потоковый ВХОД (сжатый член >2 ГиБ) пока не поддержан — следующий шаг инкр. Deflate.
    if (entry.CompressedSize > int.MaxValue)
      return ZipExtractResult.InvalidData;

    byte[] compressed = new byte[entry.CompressedSize];
    try
    {
      archive.ReadExactly(compressed, 0, (int)entry.CompressedSize);
    }
    catch (EndOfStreamException)
    {
      return ZipExtractResult.InvalidData;
    }

    var crcStream = new Crc32WriteStream(file, progress, total, processed);
    DeflateDecodeResult result = DeflateDecoder.Decode(compressed, crcStream, deflate64: false, out long written);

    if (result != DeflateDecodeResult.Ok || written != entry.UncompressedSize)
      return ZipExtractResult.InvalidData;

    return crcStream.CurrentCrc == entry.Crc ? ZipExtractResult.Ok : ZipExtractResult.InvalidData;
  }

  // Write-through поток: считает CRC-32 записываемых байт, репортит прогресс и передаёт их дальше.
  private sealed class Crc32WriteStream(
      Stream inner, IProgress<SevenZipProgress>? progress, long total, long[] processed) : Stream
  {
    private uint _state = Crc32.InitialState;

    public uint CurrentCrc => Crc32.Finalize(_state);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
      _state = Crc32.Update(_state, buffer.AsSpan(offset, count));
      inner.Write(buffer, offset, count);
      Report(count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
      _state = Crc32.Update(_state, buffer);
      inner.Write(buffer);
      Report(buffer.Length);
    }

    private void Report(int count)
    {
      processed[0] += count;
      progress?.Report(new SevenZipProgress(processed[0], total));
    }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}
