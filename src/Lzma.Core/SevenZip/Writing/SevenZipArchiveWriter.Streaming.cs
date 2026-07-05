using System.IO;
using System.Linq;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

/// <summary>Элемент потокового создания архива: данные берутся из <see cref="Stream"/> по требованию,
/// а не из <c>byte[]</c> в памяти — это позволяет паковать файлы больше 2 ГиБ.</summary>
/// <param name="Name">Имя записи (путь с '/').</param>
/// <param name="Length">Размер данных в байтах (для каталога/пустого файла — 0).</param>
/// <param name="OpenRead">Открывает читаемый поток данных (вызывается только для непустых файлов).</param>
public sealed record SevenZipStreamingEntry(
    string Name,
    long Length,
    Func<Stream> OpenRead,
    bool IsDirectory = false,
    uint? WindowsAttributes = null,
    DateTime? LastWriteTimeUtc = null);

// Потоковая запись .7z в Stream: сжатые данные каждого файла льются прямо в выходной поток
// (через Lzma2LzmaEncoder.EncodeStreaming), размеры — long/ulong, next-header строится в памяти
// (он мал), сигнатура патчится в конце (нужен seekable output). Не держит архив/файлы в памяти.
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит LZMA2-архив, записывая его потоково в <paramref name="output"/> (seekable). Каждый
  /// непустой файл открывается через <see cref="SevenZipStreamingEntry.OpenRead"/> и сжимается на
  /// лету; ни весь файл, ни весь архив в памяти не держатся.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildLzma2ArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      int dictionarySize,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(output);

    if (!output.CanWrite || !output.CanSeek)
      return SevenZipArchiveWriteResult.NotSupported; // патч сигнатуры требует seek

    if (dictionarySize <= 0)
      return SevenZipArchiveWriteResult.InvalidData;

    if (!Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out Lzma2Properties properties))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!properties.TryGetDictionarySizeInt32(out int effectiveDictionarySize))
      return SevenZipArchiveWriteResult.NotSupported;

    // Валидация записей: имя, корректный путь, отрицательная длина недопустима.
    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry e = entries[i];
      if (e is null || e.Name is null || e.Length < 0)
        return SevenZipArchiveWriteResult.InvalidData;
      if (!IsSupportedEntryPath(e.Name))
        return SevenZipArchiveWriteResult.InvalidData;
      if (e.IsDirectory && e.Length != 0)
        return SevenZipArchiveWriteResult.InvalidData;
      if (!e.IsDirectory && e.Length > 0 && e.OpenRead is null)
        return SevenZipArchiveWriteResult.InvalidData;
    }

    var lzmaProperties = new LzmaProperties(3, 0, 2);
    byte[] coderBytes = [0x21, Lzma2MethodId, 0x01, properties.DictionaryProp];

    long startPos = output.Position;

    // Резервируем место под сигнатуру (пропатчим в конце).
    output.Write(new byte[SevenZipSignatureHeader.Size]);

    int count = 0;
    long totalContent = 0;
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
      {
        count++;
        totalContent += entries[i].Length;
      }

    var packSizes = new ulong[count];
    var unpackSizes = new ulong[count];
    var crcs = new uint[count];

    progress?.Report(new SevenZipProgress(0, totalContent));
    long processed = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry entry = entries[i];
      if (!IsStreamingDataEntry(entry))
        continue;

      token.ThrowIfCancellationRequested();

      long packSize;
      uint crc;

      // Прогресс ВНУТРИ файла: локально обработанные байты → глобальный отчёт (как within-folder в декоде).
      long processedBefore = processed;
      IProgress<long>? fileProgress = progress is null ? null
          : new LongProgressAdapter(local => progress.Report(
              new SevenZipProgress(Math.Min(processedBefore + local, totalContent), totalContent)));

      using (Stream source = entry.OpenRead())
      {
        var crcSource = new CrcReadStream(source);
        packSize = Lzma2LzmaEncoder.EncodeStreaming(
            crcSource, entry.Length, lzmaProperties, effectiveDictionarySize, output,
            token: token, bytesProgress: fileProgress);
        crc = crcSource.CrcValue;
      }

      packSizes[streamIndex] = (ulong)packSize;
      unpackSizes[streamIndex] = (ulong)entry.Length;
      crcs[streamIndex] = crc;
      streamIndex++;

      processed += entry.Length;
      progress?.Report(new SevenZipProgress(processed, totalContent));
    }

    // Синтетические entries для переиспользования FilesInfo-writer-ов: им нужен лишь признак
    // «пустой поток» (Content.Length == 0), а не сами байты — маркер [0] делает файл непустым.
    var synthetic = new SevenZipArchiveWriterEntry[entries.Count];
    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry e = entries[i];
      byte[] marker = IsStreamingDataEntry(e) ? [0] : [];
      synthetic[i] = new SevenZipArchiveWriterEntry(e.Name, marker, e.IsDirectory, e.WindowsAttributes, e.LastWriteTimeUtc);
    }

    if (!TryBuildLzma2StreamingNextHeader(synthetic, packSizes, unpackSizes, crcs, coderBytes, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    long packedEnd = output.Position;
    long nextHeaderOffset = packedEnd - (startPos + SevenZipSignatureHeader.Size);

    output.Write(nextHeaderBytes);

    // Патчим сигнатуру.
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);
    var signature = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)nextHeaderOffset,
        NextHeaderSize: (ulong)nextHeaderBytes.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureBytes = new byte[SevenZipSignatureHeader.Size];
    signature.Write(signatureBytes);

    long endPos = output.Position;
    output.Position = startPos;
    output.Write(signatureBytes);
    output.Position = endPos;
    output.Flush();

    return SevenZipArchiveWriteResult.Ok;
  }

  private static bool IsStreamingDataEntry(SevenZipStreamingEntry e)
      => !e.IsDirectory && e.Length > 0;

  // Строит next-header для потокового LZMA2-сценария: PackInfo/UnpackInfo с ulong-размерами
  // (поддержка >2 ГБ), FilesInfo — через существующие writer-ы (по синтетическим entries).
  private static bool TryBuildLzma2StreamingNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> syntheticEntries,
      ulong[] packSizes,
      ulong[] unpackSizes,
      uint[] unpackCrcs,
      byte[] coderBytes,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteStreamingPackInfo(header, packSizes))
      return false;

    if (!TryWriteStreamingFoldersUnpackInfo(header, unpackSizes, unpackCrcs, coderBytes))
      return false;

    header.Add(SevenZipNid.End);

    if (AllEntriesAreNonEmptyFiles(syntheticEntries))
    {
      if (!TryWriteAllNonEmptyCopyEntriesFilesInfo(header, syntheticEntries))
        return false;
    }
    else if (!TryWriteMixedCopyEntriesFilesInfo(header, syntheticEntries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];
    return true;
  }

  // PackInfo с ulong pack-размерами (twin TryWriteCompressedStreamsPackInfo на long/ulong).
  private static bool TryWriteStreamingPackInfo(List<byte> header, ulong[] packSizes)
  {
    header.Add(SevenZipNid.PackInfo);

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, (ulong)packSizes.Length))
      return false;

    header.Add(SevenZipNid.Size);

    for (int i = 0; i < packSizes.Length; i++)
      if (!TryWriteUInt64(header, packSizes[i]))
        return false;

    header.Add(SevenZipNid.End);
    return true;
  }

  // UnpackInfo (по одному coder-folder на файл) с ulong-размерами.
  private static bool TryWriteStreamingFoldersUnpackInfo(
      List<byte> header, ulong[] unpackSizes, uint[] unpackCrcs, byte[] coderBytes)
  {
    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)unpackSizes.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, 1)) // один coder на folder
        return false;

      header.AddRange(coderBytes);
    }

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < unpackSizes.Length; i++)
      if (!TryWriteUInt64(header, unpackSizes[i]))
        return false;

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, unpackCrcs);

    header.Add(SevenZipNid.End);
    return true;
  }

  // Синхронный IProgress<long> из делегата (отчёты идут на потоке энкодера, не через SynchronizationContext).
  private sealed class LongProgressAdapter(Action<long> report) : IProgress<long>
  {
    public void Report(long value) => report(value);
  }

  // Обёртка чтения, считающая CRC32 несжатых данных по ходу (для folder-CRC потокового файла).
  private sealed class CrcReadStream(Stream inner) : Stream
  {
    private uint _crc = Crc32.InitialState;

    public uint CrcValue => Crc32.Finalize(_crc);

    public override int Read(byte[] buffer, int offset, int count)
    {
      int n = inner.Read(buffer, offset, count);
      if (n > 0)
        _crc = Crc32.Update(_crc, buffer.AsSpan(offset, n));

      return n;
    }

    public override int Read(Span<byte> buffer)
    {
      int n = inner.Read(buffer);
      if (n > 0)
        _crc = Crc32.Update(_crc, buffer[..n]);

      return n;
    }

    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}
