using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractInvalidFileTimeTests
{
  public static IEnumerable<object[]> InvalidTimeProperties()
  {
    yield return [SevenZipNid.CTime];
    yield return [SevenZipNid.ATime];
    yield return [SevenZipNid.MTime];
  }

  [Theory]
  [MemberData(nameof(InvalidTimeProperties))]
  public void ExtractToDirectory_InvalidRawFileTime_InvalidData(byte timeNid)
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);

    // Берём значение > long.MaxValue, чтобы попасть в явную валидацию
    // в ExtractToDirectory до DateTime.FromFileTimeUtc(...).
    ulong invalidRawFileTime = unchecked((ulong)long.MaxValue + 1UL);

    byte[] archive = BuildArchiveSingleFileCopyWithTime(
        plain,
        fileName: "file.bin",
        timeNid: timeNid,
        rawFileTime: invalidRawFileTime);

    // Сам архив как контейнер и как decode-результат корректен:
    // проблема должна проявиться именно на этапе извлечения metadata.
    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);

    Assert.Single(entries);
    Assert.Equal("file.bin", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(plain, entries[0].Bytes);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderExtractInvalidFileTimeTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
      Assert.Equal(archive.Length, consumed2);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] BuildArchiveSingleFileCopyWithTime(
      byte[] plain,
      string fileName,
      byte timeNid,
      ulong rawFileTime)
  {
    byte[] nextHeader = BuildNextHeaderSingleFileCopyWithTime(
        packSize: plain.Length,
        unpackSize: plain.Length,
        fileName: fileName,
        timeNid: timeNid,
        rawFileTime: rawFileTime);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)plain.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + plain.Length + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(plain, 0, archive, SevenZipSignatureHeader.Size, plain.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + plain.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderSingleFileCopyWithTime(
      int packSize,
      int unpackSize,
      string fileName,
      byte timeNid,
      ulong rawFileTime)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            // PackInfo
            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);

    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);   // NumFolders
    h.Add(0x00);      // External = 0
    WriteU64(h, 1);   // NumCoders

    // Copy coder: MethodId = { 00 }, без props.
    h.Add(0x01);      // mainByte: idSize=1, простой coder
    h.Add(0x00);      // methodId = Copy

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1); // NumFiles

    WriteNameProperty(h, fileName);
    WriteSingleDefinedTimeProperty(h, timeNid, rawFileTime);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static void WriteNameProperty(List<byte> h, string fileName)
  {
    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");

    // Property size = External(1 byte) + UTF-16 bytes
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);
  }

  private static void WriteSingleDefinedTimeProperty(List<byte> h, byte timeNid, ulong rawFileTime)
  {
    h.Add(timeNid);

    // payload:
    // [0] AllAreDefined = 1
    // [1] External = 0
    // [2..9] raw FILETIME (LE)
    Span<byte> payload = stackalloc byte[10];
    payload[0] = 0x01;
    payload[1] = 0x00;
    BinaryPrimitives.WriteUInt64LittleEndian(payload[2..], rawFileTime);

    WriteU64(h, (ulong)payload.Length);
    h.AddRange(payload.ToArray());
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
      // Если что-то удерживает папку, удалим вручную.
    }
  }
}
