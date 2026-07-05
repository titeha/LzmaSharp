using System.IO;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Читает СТРУКТУРУ 7z-архива (сигнатуру + next-header) из seekable <see cref="Stream"/>, НЕ загружая
/// packed-данные в память — это позволяет ОТКРЫТЬ архив больше 2 ГиБ (метаданные малы). Сами packed-
/// данные декодируются отдельно, потоково (по offset/размеру из <see cref="SevenZipHeader"/>).
/// </summary>
public static class SevenZipArchiveStreamReader
{
  /// <summary>
  /// Читает сигнатуру и next-header из <paramref name="archive"/>. Packed-данные начинаются со
  /// смещения <paramref name="packedBaseOffset"/> (сразу после сигнатуры). Поддерживается обычный
  /// Header (наш writer его и пишет); EncodedHeader (mhe) пока — <see cref="SevenZipArchiveDecodeResult.NotSupported"/>.
  /// </summary>
  public static SevenZipArchiveDecodeResult ReadHeader(
      Stream archive,
      out SevenZipHeader header,
      out long packedBaseOffset)
  {
    ArgumentNullException.ThrowIfNull(archive);

    header = default;
    packedBaseOffset = SevenZipSignatureHeader.Size;

    if (!archive.CanSeek || !archive.CanRead)
      return SevenZipArchiveDecodeResult.NotSupported;

    // 1) Сигнатура (32 байта в начале).
    Span<byte> signatureBytes = stackalloc byte[SevenZipSignatureHeader.Size];
    archive.Position = 0;
    if (!TryReadFully(archive, signatureBytes))
      return SevenZipArchiveDecodeResult.NeedMoreData;

    if (SevenZipSignatureHeader.TryRead(signatureBytes, out SevenZipSignatureHeader signature)
        != SevenZipSignatureHeader.ReadResult.Ok)
      return SevenZipArchiveDecodeResult.InvalidData;

    // Метаданные (next-header) держим в памяти — они малы; гигантские не поддерживаем.
    if (signature.NextHeaderSize > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    // 2) Next-header (по offset в конце файла).
    long nextHeaderPos = SevenZipSignatureHeader.Size + (long)signature.NextHeaderOffset;
    int nextHeaderSize = (int)signature.NextHeaderSize;

    if (nextHeaderPos < SevenZipSignatureHeader.Size || nextHeaderPos + nextHeaderSize > archive.Length)
      return SevenZipArchiveDecodeResult.InvalidData;

    byte[] nextHeader = new byte[nextHeaderSize];
    archive.Position = nextHeaderPos;
    if (!TryReadFully(archive, nextHeader))
      return SevenZipArchiveDecodeResult.NeedMoreData;

    if (Crc32.Compute(nextHeader) != signature.NextHeaderCrc)
      return SevenZipArchiveDecodeResult.InvalidData;

    // 3) Тип next-header.
    switch (SevenZipNextHeaderKindDetector.TryDetect(nextHeader, out SevenZipNextHeaderKind kind))
    {
      case SevenZipNextHeaderKindDetectResult.Ok:
        break;
      case SevenZipNextHeaderKindDetectResult.NeedMoreInput:
        return SevenZipArchiveDecodeResult.NeedMoreData;
      default:
        return SevenZipArchiveDecodeResult.InvalidData;
    }

    if (kind != SevenZipNextHeaderKind.Header)
      return SevenZipArchiveDecodeResult.NotSupported; // EncodedHeader (mhe) — потоково пока не читаем

    // 4) Парсим обычный Header (StreamsInfo/FilesInfo).
    return SevenZipHeaderReader.TryRead(nextHeader, out header, out _) switch
    {
      SevenZipHeaderReadResult.Ok => SevenZipArchiveDecodeResult.Ok,
      SevenZipHeaderReadResult.NotSupported => SevenZipArchiveDecodeResult.NotSupported,
      SevenZipHeaderReadResult.NeedMoreInput => SevenZipArchiveDecodeResult.NeedMoreData,
      _ => SevenZipArchiveDecodeResult.InvalidData,
    };
  }

  private static bool TryReadFully(Stream stream, Span<byte> buffer)
  {
    int offset = 0;
    while (offset < buffer.Length)
    {
      int got = stream.Read(buffer[offset..]);
      if (got <= 0)
        return false;
      offset += got;
    }

    return true;
  }

  private static bool TryReadFully(Stream stream, byte[] buffer) => TryReadFully(stream, buffer.AsSpan());
}
